using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace SignVR.Recording
{
    [DefaultExecutionOrder(-9000)]
    public sealed class QuestDeviceGateway : MonoBehaviour
    {
        private const int ProtocolVersion = 2;
        private const string DeviceIdPlayerPrefsKey = "SignVR.DeviceId";

        [Header("Dependencies")]
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private MetaBodyMotionRecorder recorder;

        [SerializeField]
        private MetaBodyMotionStreamer motionStreamer;

        [SerializeField]
        private QuestTakeUploader takeUploader;

        [SerializeField]
        private QuestPreviewStreamer previewStreamer;

        [SerializeField]
        private HandCaptureBoundaryMonitor boundaryMonitor;

        [Header("Discovery")]
        [SerializeField]
        [Range(1, 65535)]
        private int controlPort = 5006;

        [SerializeField]
        [Range(1, 65535)]
        private int hostAnnouncementPort = 5005;

        [SerializeField]
        [Min(1f)]
        private float announcementIntervalSeconds = 3f;

        private UdpClient listener;
        private UdpClient sender;
        private string deviceId;
        private IPAddress pairedHostAddress;
        private Coroutine announcementRoutine;

        public int ReceivedPacketCount { get; private set; }
        public string LastPacketType { get; private set; } = string.Empty;
        public string LastReceiveError { get; private set; } = string.Empty;

        [Serializable]
        private sealed class PacketEnvelope
        {
            public string type;
        }

        [Serializable]
        private sealed class DiscoverPacket
        {
            public string type;
            public int version;
            public string nonce;
            public int reply_port;
        }

        [Serializable]
        private sealed class PairPacket
        {
            public string type;
            public int version;
            public string command_id;
            public string host_ip;
            public int http_port;
            public int pose_port;
            public string session_token;
        }

        [Serializable]
        private sealed class CommandPacket
        {
            public string type;
            public int version;
            public string command_id;
            public string action;
            public string session_id;
            public string sentence_id;
            public int sentence_index;
            public string prompt;
            public string take_id;
            public int take_index;
            public long start_at_unix_ms;

            // pedal: "down" / "hold" / "up"; hold carries progress in 0..1.
            public string phase;
            public float progress;

            // set_guidance toggles the hand boundary guidance for A/B recording.
            public bool enabled;
        }

        [Serializable]
        private sealed class SignalPacket
        {
            public string type = "signal";
            public int version = ProtocolVersion;
            public string device_id;
            public string signal;
            public bool active;
        }

        [Serializable]
        private sealed class AnnouncePacket
        {
            public string type = "announce";
            public int version = ProtocolVersion;
            public string device_id;
            public string name;
            public string model;
            public string app_version;
            public int control_port;
            public string[] capabilities;
            public string state;
        }

        [Serializable]
        private sealed class AckPacket
        {
            public string type = "ack";
            public int version = ProtocolVersion;
            public string command_id;
            public string device_id;
            public bool accepted;
            public string state;
            public string message;
        }

        private readonly struct ReceivedDatagram
        {
            public ReceivedDatagram(string json, IPEndPoint remoteEndPoint)
            {
                Json = json;
                RemoteEndPoint = remoteEndPoint;
            }

            public string Json { get; }
            public IPEndPoint RemoteEndPoint { get; }
        }

        private void Awake()
        {
            deviceId = PlayerPrefs.GetString(DeviceIdPlayerPrefsKey, string.Empty);

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                deviceId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceIdPlayerPrefsKey, deviceId);
                PlayerPrefs.Save();
            }

            if (coordinator == null || recorder == null || motionStreamer == null ||
                takeUploader == null || previewStreamer == null ||
                boundaryMonitor == null)
            {
                Debug.LogError("[QuestDeviceGateway] Scene dependencies are not assigned.");
                enabled = false;
            }
        }

        /// <summary>
        /// Raises a help flag on the operator console. A deaf teacher cannot call
        /// out from inside the headset, so this is their only way to ask for help
        /// without ending the session.
        /// </summary>
        public void SendHelpSignal(bool active)
        {
            if (pairedHostAddress == null)
            {
                return;
            }

            var packet = new SignalPacket
            {
                device_id = deviceId,
                signal = "help",
                active = active
            };

            SendPacket(
                packet,
                new IPEndPoint(pairedHostAddress, hostAnnouncementPort)
            );
        }

        private void OnEnable()
        {
            StartListener();
            announcementRoutine = StartCoroutine(AnnouncementLoop());
        }

        private void Update()
        {
            ReceiveAvailableDatagrams();
        }

        private void ReceiveAvailableDatagrams()
        {
            try
            {
                while (listener != null && listener.Available > 0)
                {
                    var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] bytes = listener.Receive(ref remoteEndPoint);
                    HandleDatagram(
                        new ReceivedDatagram(
                            Encoding.UTF8.GetString(bytes),
                            remoteEndPoint
                        )
                    );
                }
            }
            catch (SocketException exception)
            {
                LastReceiveError = exception.Message;
                Debug.LogError(
                    "[QuestDeviceGateway] UDP receive failed: " +
                    exception.Message
                );
                enabled = false;
            }
        }

        private IEnumerator AnnouncementLoop()
        {
            var wait = new WaitForSecondsRealtime(announcementIntervalSeconds);

            while (true)
            {
                IPEndPoint target = pairedHostAddress == null
                    ? new IPEndPoint(IPAddress.Broadcast, hostAnnouncementPort)
                    : new IPEndPoint(pairedHostAddress, hostAnnouncementPort);

                SendAnnouncement(target);
                yield return wait;
            }
        }

        private void HandleDatagram(ReceivedDatagram datagram)
        {
            PacketEnvelope envelope = JsonUtility.FromJson<PacketEnvelope>(datagram.Json);
            ReceivedPacketCount++;
            LastPacketType = envelope.type ?? string.Empty;

            switch (envelope.type)
            {
                case "discover":
                    HandleDiscover(datagram.Json, datagram.RemoteEndPoint);
                    break;
                case "pair":
                    HandlePair(datagram.Json, datagram.RemoteEndPoint);
                    break;
                case "command":
                    HandleCommand(datagram.Json, datagram.RemoteEndPoint);
                    break;
            }
        }

        private void HandleDiscover(string json, IPEndPoint remoteEndPoint)
        {
            DiscoverPacket packet = JsonUtility.FromJson<DiscoverPacket>(json);
            int replyPort = packet.reply_port > 0
                ? packet.reply_port
                : remoteEndPoint.Port;

            SendAnnouncement(new IPEndPoint(remoteEndPoint.Address, replyPort));
        }

        private void HandlePair(string json, IPEndPoint remoteEndPoint)
        {
            PairPacket packet = JsonUtility.FromJson<PairPacket>(json);
            bool accepted = IPAddress.TryParse(packet.host_ip, out IPAddress hostAddress);

            if (accepted)
            {
                pairedHostAddress = hostAddress;
                string baseUrl = $"http://{packet.host_ip}:{packet.http_port}";

                motionStreamer.ConfigureDestination(packet.host_ip, packet.pose_port);
                takeUploader.ConfigureHost(
                    baseUrl,
                    deviceId,
                    packet.session_token
                );
                previewStreamer.ConfigureHost(
                    baseUrl,
                    deviceId,
                    packet.session_token
                );
            }

            SendAck(
                remoteEndPoint,
                packet.command_id,
                accepted,
                accepted ? "paired" : "host_ip_invalid"
            );
        }

        private void HandleCommand(string json, IPEndPoint remoteEndPoint)
        {
            CommandPacket packet = JsonUtility.FromJson<CommandPacket>(json);
            bool accepted;

            switch (packet.action)
            {
                case "start_take":
                    accepted = StartRemoteTake(packet);
                    break;
                case "stop_take":
                    accepted = coordinator.StopCurrentTake();
                    break;
                case "reset_take":
                    coordinator.ResetCurrentPrompt();
                    accepted = true;
                    break;
                case "pedal":
                    accepted = HandlePedal(packet);
                    break;
                case "set_guidance":
                    boundaryMonitor.SetGuidanceEnabled(packet.enabled);
                    accepted = true;
                    break;
                default:
                    accepted = false;
                    break;
            }

            SendAck(
                remoteEndPoint,
                packet.command_id,
                accepted,
                accepted ? packet.action : "command_rejected"
            );
        }

        /// <summary>
        /// Mirrors the operator pedal into the headset. The teacher cannot hear
        /// the pedal click, so every press needs an immediate visual receipt and
        /// the long-press ring has to fill inside the HMD, not only in the browser.
        /// </summary>
        private bool HandlePedal(CommandPacket packet)
        {
            switch (packet.phase)
            {
                case "down":
                    coordinator.PulsePedal();
                    return true;
                case "hold":
                    coordinator.SetResetHoldProgress(packet.progress);
                    return true;
                case "up":
                    coordinator.CancelResetHold();
                    return true;
                default:
                    return false;
            }
        }

        private bool StartRemoteTake(CommandPacket packet)
        {
            if (!recorder.IsPoseReady)
            {
                return false;
            }

            bool promptLoaded = coordinator.LoadPrompt(
                packet.session_id,
                packet.sentence_id,
                packet.prompt,
                packet.take_index
            );

            if (!promptLoaded)
            {
                return false;
            }

            var take = new RecordingTakeContext(
                packet.session_id,
                packet.sentence_id,
                packet.prompt,
                packet.take_index,
                packet.take_id,
                DateTime.UtcNow
            );

            return coordinator.BeginRemoteTake(take, packet.start_at_unix_ms);
        }

        private void SendAnnouncement(IPEndPoint target)
        {
            var packet = new AnnouncePacket
            {
                device_id = deviceId,
                name = string.IsNullOrWhiteSpace(SystemInfo.deviceName)
                    ? "Quest 3"
                    : SystemInfo.deviceName,
                model = SystemInfo.deviceModel,
                app_version = Application.version,
                control_port = controlPort,
                capabilities = new[] { "pose", "preview", "take_upload" },
                state = coordinator.State.ToString().ToLowerInvariant()
            };

            SendPacket(packet, target);
        }

        private void SendAck(
            IPEndPoint target,
            string commandId,
            bool accepted,
            string message)
        {
            var packet = new AckPacket
            {
                command_id = commandId,
                device_id = deviceId,
                accepted = accepted,
                state = coordinator.State.ToString().ToLowerInvariant(),
                message = message
            };

            SendPacket(packet, target);
        }

        private void SendPacket(object packet, IPEndPoint target)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
            sender.Send(bytes, bytes.Length, target);
        }

        private void StartListener()
        {
            listener = new UdpClient(new IPEndPoint(IPAddress.Any, controlPort));
            sender = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true
            };
        }

        private void OnDisable()
        {
            if (announcementRoutine != null)
            {
                StopCoroutine(announcementRoutine);
                announcementRoutine = null;
            }

            listener?.Close();
            listener = null;
            sender?.Dispose();
            sender = null;
        }
    }
}
