using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Meta.XR.Movement.Retargeting;
using UnityEngine;

/// <summary>
/// Sends the raw, pre-retargeting Meta body skeleton to an external client.
/// Packets are UTF-8 JSON over UDP so non-Unity clients can consume them easily.
/// </summary>
public sealed class MetaBodyMotionStreamer : MonoBehaviour
{
    private const int ProtocolVersion = 1;

    [Header("Source")]
    [SerializeField]
    private MetaSourceDataProvider sourceDataProvider;

    [Header("UDP Destination")]
    [SerializeField]
    private bool streamAutomatically = true;

    [SerializeField]
    private string remoteHost = "255.255.255.255";

    [SerializeField]
    [Range(1, 65535)]
    private int remotePort = 5005;

    [Header("Streaming")]
    [SerializeField]
    [Range(1, 120)]
    private int streamRate = 30;

    [SerializeField]
    [Min(0.25f)]
    private float metadataIntervalSeconds = 2f;

    [Header("Diagnostics")]
    [SerializeField]
    private bool debugLogging = true;

    [SerializeField]
    [Min(1f)]
    private float debugLogIntervalSeconds = 5f;

    private UdpClient udpClient;
    private IPEndPoint remoteEndPoint;
    private double nextFrameTime;
    private double nextMetadataTime;
    private double nextStatusTime;
    private double nextDebugLogTime;
    private double nextErrorLogTime;
    private double lastSuccessfulSendTime = -1.0;
    private long sequence;
    private long packetsSent;
    private long framesSent;
    private long bytesSent;
    private long sendFailures;
    private uint lastSkeletonChangedCount = uint.MaxValue;
    private bool hasBodyState;
    private bool hasReportedBodyState;
    private bool metadataAvailable;
    private int lastJointCount;
    private int lastValidJointCount;
    private float lastConfidence;
    private string lastError = string.Empty;

    public bool IsStreaming => udpClient != null;
    public long PacketsSent => packetsSent;
    public long FramesSent => framesSent;
    public long BytesSent => bytesSent;
    public long SendFailures => sendFailures;
    public bool HasBodyState => hasBodyState;
    public int LastValidJointCount => lastValidJointCount;
    public float LastConfidence => lastConfidence;
    public string Destination => remoteEndPoint != null
        ? remoteEndPoint.ToString()
        : remoteHost + ":" + remotePort;
    public bool IsBroadcasting =>
        remoteEndPoint != null &&
        remoteEndPoint.Address.Equals(IPAddress.Broadcast);

    public MetaSourceDataProvider SourceDataProvider => sourceDataProvider;

    public void ConfigureSource(MetaSourceDataProvider provider)
    {
        sourceDataProvider = provider;
    }

    public string ShortDebugStatus
    {
        get
        {
            if (!IsStreaming)
            {
                return string.IsNullOrEmpty(lastError)
                    ? "UDP OFF"
                    : "UDP ERROR: " + lastError;
            }

            string mode = IsBroadcasting
                ? "UDP BROADCAST ON"
                : "UDP ON";

            string body = hasBodyState
                ? $"Body {lastValidJointCount}/{lastJointCount}"
                : "Body waiting";

            return $"{mode} | Sent {framesSent}\n{body}";
        }
    }

    [Serializable]
    private sealed class SkeletonPacket
    {
        public string type = "skeleton";
        public int version = ProtocolVersion;
        public string skeleton_type;
        public string[] joint_names;
        public int[] parent_indices;
    }

    [Serializable]
    private sealed class FramePacket
    {
        public string type = "frame";
        public int version = ProtocolVersion;
        public long sequence;
        public double timestamp;
        public float confidence;
        public int joint_count;
        public float[] positions;
        public bool[] valid;
    }

    [Serializable]
    private sealed class StatusPacket
    {
        public string type = "status";
        public int version = ProtocolVersion;
        public string device_name;
        public string device_model;
        public string destination;
        public bool broadcast;
        public bool socket_open;
        public bool body_state_available;
        public bool pose_valid;
        public bool skeleton_metadata_available;
        public long frames_sent;
        public long packets_sent;
        public long bytes_sent;
        public long send_failures;
        public int joint_count;
        public int valid_joint_count;
        public float confidence;
        public string last_error;
    }

    private void Awake()
    {
        if (sourceDataProvider == null)
        {
            Debug.LogError(
                "[MetaBodyMotionStreamer] " +
                "MetaSourceDataProvider has not been assigned."
            );
        }
    }

    private void Start()
    {
        if (streamAutomatically)
        {
            StartStreaming();
        }
    }

    private void LateUpdate()
    {
        if (udpClient == null || sourceDataProvider == null)
        {
            return;
        }

        OVRPlugin.BodyState? nullableBodyState =
            sourceDataProvider.BodyState;

        UpdateTrackingDiagnostics(nullableBodyState);

        double now = Time.realtimeSinceStartupAsDouble;

        if (now >= nextStatusTime)
        {
            SendStatusPacket();
            nextStatusTime = now + 1.0;
        }

        if (debugLogging && now >= nextDebugLogTime)
        {
            LogDiagnostics(now);
            nextDebugLogTime =
                now + Math.Max(debugLogIntervalSeconds, 1f);
        }

        if (now < nextFrameTime)
        {
            return;
        }

        double interval = 1.0 / Math.Max(streamRate, 1);
        nextFrameTime += interval;

        if (nextFrameTime < now - interval)
        {
            nextFrameTime = now + interval;
        }

        if (!nullableBodyState.HasValue)
        {
            return;
        }

        OVRPlugin.BodyState bodyState = nullableBodyState.Value;

        if (
            now >= nextMetadataTime ||
            bodyState.SkeletonChangedCount != lastSkeletonChangedCount
        )
        {
            if (SendSkeletonPacket())
            {
                lastSkeletonChangedCount =
                    bodyState.SkeletonChangedCount;
            }

            nextMetadataTime =
                now + Math.Max(metadataIntervalSeconds, 0.25f);
        }

        SendFramePacket(bodyState);
    }

    public void StartStreaming()
    {
        if (udpClient != null || sourceDataProvider == null)
        {
            return;
        }

        try
        {
            IPAddress address = ResolveIPv4Address(remoteHost);

            remoteEndPoint = new IPEndPoint(
                address,
                Mathf.Clamp(remotePort, 1, 65535)
            );

            udpClient = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = address.Equals(IPAddress.Broadcast)
            };

            udpClient.Connect(remoteEndPoint);

            double now = Time.realtimeSinceStartupAsDouble;
            nextFrameTime = now;
            nextMetadataTime = now;
            nextStatusTime = now + 1.0;
            nextDebugLogTime = now;
            lastSkeletonChangedCount = uint.MaxValue;
            sequence = 0;
            packetsSent = 0;
            framesSent = 0;
            bytesSent = 0;
            sendFailures = 0;
            lastSuccessfulSendTime = -1.0;
            hasBodyState = false;
            hasReportedBodyState = false;
            metadataAvailable = false;
            lastJointCount = 0;
            lastValidJointCount = 0;
            lastConfidence = 0f;
            lastError = string.Empty;

            Debug.Log(
                "[MetaBodyMotionStreamer][UDP STARTED] " +
                $"Mode={(IsBroadcasting ? "broadcast" : "unicast")}, " +
                $"destination={remoteEndPoint}, rate={streamRate} fps. " +
                "A successful UDP send only confirms that the local socket " +
                "accepted the packet; client reception is reported by the client."
            );

            // Send immediately, even before body tracking becomes valid. This
            // separates network problems from tracking problems on the client.
            SendStatusPacket();
        }
        catch (Exception exception)
        {
            udpClient?.Dispose();
            udpClient = null;
            remoteEndPoint = null;
            lastError = exception.Message;

            Debug.LogError(
                "[MetaBodyMotionStreamer][UDP START FAILED] " +
                exception.Message
            );
        }
    }

    public bool ConfigureDestination(
        string host,
        int port,
        bool startImmediately = true)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Remote host is empty.", nameof(host));
        }

        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        StopStreaming();
        remoteHost = host;
        remotePort = port;
        streamAutomatically = startImmediately;

        if (startImmediately)
        {
            StartStreaming();
        }

        return IsStreaming;
    }

    public void StopStreaming()
    {
        if (udpClient == null)
        {
            return;
        }

        udpClient.Dispose();
        udpClient = null;
        remoteEndPoint = null;

        Debug.Log(
            "[MetaBodyMotionStreamer][UDP STOPPED] " +
            $"packets={packetsSent}, frames={framesSent}, " +
            $"bytes={bytesSent}, failures={sendFailures}."
        );
    }

    private bool SendSkeletonPacket()
    {
        OVRPlugin.SkeletonType skeletonType =
            sourceDataProvider.ProvidedSkeletonType ==
            OVRPlugin.BodyJointSet.FullBody
                ? OVRPlugin.SkeletonType.FullBody
                : OVRPlugin.SkeletonType.Body;

        var skeleton = new OVRPlugin.Skeleton2();

        if (!OVRPlugin.GetSkeleton2(skeletonType, ref skeleton))
        {
            metadataAvailable = false;
            return false;
        }

        int boneCount = Math.Min(
            (int)skeleton.NumBones,
            skeleton.Bones?.Length ?? 0
        );

        var names = new string[boneCount];
        var parents = new int[boneCount];

        for (int i = 0; i < boneCount; i++)
        {
            OVRPlugin.Bone bone = skeleton.Bones[i];
            names[i] = bone.Id.ToString();
            parents[i] = bone.ParentBoneIndex;
        }

        var packet = new SkeletonPacket
        {
            skeleton_type = skeletonType.ToString(),
            joint_names = names,
            parent_indices = parents
        };

        bool sent = SendJson(packet);
        metadataAvailable = sent;

        if (sent && debugLogging)
        {
            Debug.Log(
                "[MetaBodyMotionStreamer][SKELETON SENT] " +
                $"type={skeletonType}, joints={boneCount}."
            );
        }

        return sent;
    }

    private void SendFramePacket(OVRPlugin.BodyState bodyState)
    {
        OVRPlugin.BodyJointLocation[] joints =
            bodyState.JointLocations;

        if (joints == null || joints.Length == 0)
        {
            return;
        }

        var positions = new float[joints.Length * 3];
        var valid = new bool[joints.Length];

        for (int i = 0; i < joints.Length; i++)
        {
            OVRPlugin.BodyJointLocation joint = joints[i];
            OVRPlugin.Vector3f position = joint.Pose.Position;
            int offset = i * 3;

            // Oculus tracking space is right-handed. Flip Z so the client
            // receives the same left-handed coordinates used by Unity.
            positions[offset] = position.x;
            positions[offset + 1] = position.y;
            positions[offset + 2] = -position.z;
            valid[i] = joint.PositionValid;
        }

        var packet = new FramePacket
        {
            sequence = sequence++,
            timestamp = bodyState.Time,
            confidence = bodyState.Confidence,
            joint_count = joints.Length,
            positions = positions,
            valid = valid
        };

        if (SendJson(packet))
        {
            framesSent++;
        }
    }

    private void SendStatusPacket()
    {
        var packet = new StatusPacket
        {
            device_name = SystemInfo.deviceName,
            device_model = SystemInfo.deviceModel,
            destination = Destination,
            broadcast = IsBroadcasting,
            socket_open = udpClient != null,
            body_state_available = hasBodyState,
            pose_valid =
                sourceDataProvider != null &&
                sourceDataProvider.IsPoseValid(),
            skeleton_metadata_available = metadataAvailable,
            frames_sent = framesSent,
            packets_sent = packetsSent,
            bytes_sent = bytesSent,
            send_failures = sendFailures,
            joint_count = lastJointCount,
            valid_joint_count = lastValidJointCount,
            confidence = lastConfidence,
            last_error = lastError ?? string.Empty
        };

        SendJson(packet);
    }

    private void UpdateTrackingDiagnostics(
        OVRPlugin.BodyState? nullableBodyState
    )
    {
        bool isAvailable = nullableBodyState.HasValue;

        if (!hasReportedBodyState || isAvailable != hasBodyState)
        {
            hasReportedBodyState = true;

            if (isAvailable)
            {
                Debug.Log(
                    "[MetaBodyMotionStreamer][BODY DATA AVAILABLE] " +
                    "Starting frame transmission."
                );
            }
            else
            {
                Debug.LogWarning(
                    "[MetaBodyMotionStreamer][BODY DATA WAITING] " +
                    "UDP status packets are being sent, but Meta body " +
                    "tracking has not produced a BodyState yet."
                );
            }
        }

        hasBodyState = isAvailable;

        if (!nullableBodyState.HasValue)
        {
            lastJointCount = 0;
            lastValidJointCount = 0;
            lastConfidence = 0f;
            return;
        }

        OVRPlugin.BodyState bodyState = nullableBodyState.Value;
        OVRPlugin.BodyJointLocation[] joints = bodyState.JointLocations;

        lastJointCount = joints?.Length ?? 0;
        lastValidJointCount = 0;
        lastConfidence = bodyState.Confidence;

        if (joints == null)
        {
            return;
        }

        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i].PositionValid)
            {
                lastValidJointCount++;
            }
        }
    }

    private void LogDiagnostics(double now)
    {
        string local = udpClient?.Client?.LocalEndPoint?.ToString()
            ?? "not-assigned";

        string lastSend = lastSuccessfulSendTime < 0.0
            ? "never"
            : $"{now - lastSuccessfulSendTime:F1}s ago";

        Debug.Log(
            "[MetaBodyMotionStreamer][UDP DIAGNOSTICS] " +
            $"local={local}, destination={Destination}, " +
            $"broadcast={IsBroadcasting}, packets={packetsSent}, " +
            $"frames={framesSent}, bytes={bytesSent}, " +
            $"failures={sendFailures}, lastSend={lastSend}, " +
            $"bodyState={hasBodyState}, " +
            $"validJoints={lastValidJointCount}/{lastJointCount}, " +
            $"confidence={lastConfidence:F2}."
        );
    }

    private bool SendJson(object packet)
    {
        try
        {
            string json = JsonUtility.ToJson(packet, false);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            udpClient.Send(payload, payload.Length);
            packetsSent++;
            bytesSent += payload.Length;
            lastSuccessfulSendTime =
                Time.realtimeSinceStartupAsDouble;
            lastError = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            sendFailures++;
            lastError = exception.Message;
            double now = Time.realtimeSinceStartupAsDouble;

            if (now >= nextErrorLogTime)
            {
                Debug.LogWarning(
                    "[MetaBodyMotionStreamer][UDP SEND FAILED] " +
                    exception.Message
                );
                nextErrorLogTime = now + 5.0;
            }

            return false;
        }
    }

    private static IPAddress ResolveIPv4Address(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Remote host is empty.");
        }

        if (IPAddress.TryParse(host, out IPAddress parsedAddress))
        {
            if (parsedAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException(
                    "Only IPv4 destinations are supported."
                );
            }

            return parsedAddress;
        }

        IPAddress[] addresses = Dns.GetHostAddresses(host);

        foreach (IPAddress address in addresses)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return address;
            }
        }

        throw new ArgumentException(
            "No IPv4 address was found for " + host + "."
        );
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            StopStreaming();
        }
        else if (streamAutomatically)
        {
            StartStreaming();
        }
    }

    private void OnApplicationQuit()
    {
        StopStreaming();
    }

    private void OnDestroy()
    {
        StopStreaming();
    }
}
