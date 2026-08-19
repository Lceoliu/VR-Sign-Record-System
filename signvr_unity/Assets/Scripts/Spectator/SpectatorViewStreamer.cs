using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SignVR.Streaming
{
    public enum SpectatorViewMode
    {
        HeadsetPov = 0,
        Fixed = 1
    }

    /// <summary>
    /// Renders a lightweight mono spectator camera and sends JPEG frames to a
    /// desktop receiver. It never changes the XR camera's target texture.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class SpectatorViewStreamer : MonoBehaviour
    {
        public const int DefaultPort = 5006;
        public const int DefaultWidth = 640;
        public const int DefaultHeight = 360;
        public const int DefaultFrameRate = 10;
        public const int DefaultJpegQuality = 55;

        [Header("View")]
        [SerializeField]
        private SpectatorViewMode viewMode = SpectatorViewMode.HeadsetPov;

        [Tooltip("Camera settings to copy. Camera.main is used when empty.")]
        [SerializeField]
        private Camera sourceCamera;

        [Tooltip("World pose used in Fixed mode. This transform is used when empty.")]
        [SerializeField]
        private Transform fixedViewAnchor;

        [Tooltip("Flip the image in the desktop browser without re-encoding it.")]
        [SerializeField]
        private bool flipVerticallyOnDesktop;

        [Header("UDP Destination")]
        [SerializeField]
        private bool streamAutomatically = true;

        [SerializeField]
        private string remoteHost = "255.255.255.255";

        [SerializeField]
        [Range(1, 65535)]
        private int remotePort = DefaultPort;

        [Header("Capture")]
        [SerializeField]
        [Range(160, 1920)]
        private int width = DefaultWidth;

        [SerializeField]
        [Range(90, 1080)]
        private int height = DefaultHeight;

        [SerializeField]
        [Range(1, 30)]
        private int frameRate = DefaultFrameRate;

        [SerializeField]
        [Range(10, 90)]
        private int jpegQuality = DefaultJpegQuality;

        [SerializeField]
        private bool useAsyncGpuReadback = true;

        [Header("Transport")]
        [Tooltip("1200 avoids IP fragmentation on normal 1500-byte networks.")]
        [SerializeField]
        [Range(512, 1400)]
        private int maxDatagramBytes =
            SpectatorViewProtocol.DefaultMaxDatagramBytes;

        [Header("Diagnostics")]
        [SerializeField]
        private bool debugLogging = true;

        [SerializeField]
        [Min(1f)]
        private float debugLogIntervalSeconds = 5f;

        private readonly object sendLock = new object();
        private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();

        private UdpClient udpClient;
        private IPEndPoint remoteEndPoint;
        private Thread senderThread;
        private AutoResetEvent senderSignal;
        private volatile bool senderRunning;
        private EncodedFrame pendingSend;
        private Coroutine captureCoroutine;
        private Camera captureCamera;
        private RenderTexture captureTexture;
        private Texture2D synchronousReadbackTexture;
        private UniversalRenderPipeline.SingleCameraRequest renderRequest;
        private AsyncGPUReadbackRequest readbackRequest;
        private bool readbackPending;
        private bool forceSynchronousReadback;
        private uint pendingFrameId;
        private byte pendingFrameFlags;
        private int pendingFrameWidth;
        private int pendingFrameHeight;
        private int pendingJpegQuality;
        private uint nextFrameId;
        private double nextCaptureTime;
        private double nextDebugLogTime;
        private double nextCameraWarningTime;
        private double nextErrorLogTime;
        private string lastError = string.Empty;
        private volatile string workerError = string.Empty;
        private long packetsSent;
        private long framesSent;
        private long bytesSent;
        private long sendFailures;
        private long framesDropped;
        private bool resumeAfterPause;
        private bool resumeAfterDisable;

        public SpectatorViewMode ViewMode => viewMode;
        public Camera ConfiguredSourceCamera => sourceCamera;
        public string ConfiguredRemoteHost => remoteHost;
        public int ConfiguredRemotePort => remotePort;
        public Transform FixedViewAnchor => fixedViewAnchor;
        public int CaptureWidth => width;
        public int CaptureHeight => height;
        public int CaptureFrameRate => frameRate;
        public int JpegQuality => jpegQuality;
        public bool StreamsAutomatically => streamAutomatically;
        public bool IsStreaming => udpClient != null && captureCoroutine != null;
        public string Destination => remoteEndPoint != null
            ? remoteEndPoint.ToString()
            : $"{remoteHost}:{remotePort}";
        public long PacketsSent => Interlocked.Read(ref packetsSent);
        public long FramesSent => Interlocked.Read(ref framesSent);
        public long BytesSent => Interlocked.Read(ref bytesSent);
        public long SendFailures => Interlocked.Read(ref sendFailures);
        public long FramesDropped => Interlocked.Read(ref framesDropped);
        public string LastError => lastError;

        /// <summary>
        /// Configures every setting a generated scene normally needs. Call this
        /// immediately after AddComponent; Start will honor the new values.
        /// </summary>
        public void Configure(
            SpectatorViewMode mode,
            string host,
            int port = DefaultPort,
            Camera camera = null,
            Transform fixedAnchor = null,
            int captureWidth = DefaultWidth,
            int captureHeight = DefaultHeight,
            int captureFrameRate = DefaultFrameRate,
            int quality = DefaultJpegQuality
        )
        {
            bool restart = IsStreaming;

            if (restart)
            {
                StopStreaming();
            }

            viewMode = mode;
            remoteHost = host;
            remotePort = port;
            sourceCamera = camera;
            fixedViewAnchor = fixedAnchor;
            width = captureWidth;
            height = captureHeight;
            frameRate = captureFrameRate;
            jpegQuality = quality;
            ValidateSettings();

            if (restart)
            {
                StartStreaming();
            }
        }

        public void ConfigureNetwork(string host, int port = DefaultPort)
        {
            bool restart = IsStreaming;

            if (restart)
            {
                StopStreaming();
            }

            remoteHost = host;
            remotePort = Mathf.Clamp(port, 1, 65535);

            if (restart)
            {
                StartStreaming();
            }
        }

        public void ConfigureView(
            SpectatorViewMode mode,
            Camera camera = null,
            Transform fixedAnchor = null,
            bool flipVertical = false
        )
        {
            viewMode = mode;
            sourceCamera = camera;
            fixedViewAnchor = fixedAnchor;
            flipVerticallyOnDesktop = flipVertical;
        }

        public void ConfigureCapture(
            int captureWidth,
            int captureHeight,
            int captureFrameRate,
            int quality
        )
        {
            bool restart = IsStreaming;

            if (restart)
            {
                StopStreaming();
            }

            width = captureWidth;
            height = captureHeight;
            frameRate = captureFrameRate;
            jpegQuality = quality;
            ValidateSettings();

            if (restart)
            {
                StartStreaming();
            }
        }

        private void Start()
        {
            if (streamAutomatically)
            {
                StartStreaming();
            }
        }

        public void StartStreaming()
        {
            if (IsStreaming)
            {
                return;
            }

            if (!isActiveAndEnabled)
            {
                lastError = "The component must be active before streaming starts.";
                return;
            }

            try
            {
                ValidateSettings();
                IPAddress address = ResolveIPv4Address(remoteHost);
                remoteEndPoint = new IPEndPoint(address, remotePort);
                udpClient = new UdpClient(AddressFamily.InterNetwork)
                {
                    EnableBroadcast = address.Equals(IPAddress.Broadcast)
                };
                udpClient.Connect(remoteEndPoint);

                senderSignal = new AutoResetEvent(false);
                senderRunning = true;
                senderThread = new Thread(SenderLoop)
                {
                    IsBackground = true,
                    Name = "SignVR Spectator UDP Sender"
                };
                senderThread.Start();

                ResetDiagnostics();
                nextCaptureTime = Time.realtimeSinceStartupAsDouble;
                nextDebugLogTime = nextCaptureTime;
                captureCoroutine = StartCoroutine(CaptureLoop());

                Debug.Log(
                    "[SpectatorViewStreamer][STARTED] " +
                    $"mode={viewMode}, destination={Destination}, " +
                    $"capture={width}x{height}@{frameRate}, " +
                    $"quality={jpegQuality}, datagram={maxDatagramBytes}."
                );
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                StopSenderThread();
                CleanupTransport();
                ReleaseCaptureResources();
                Debug.LogError(
                    "[SpectatorViewStreamer][START FAILED] " +
                    exception.Message
                );
            }
        }

        public void StopStreaming()
        {
            bool wasActive =
                captureCoroutine != null ||
                senderThread != null ||
                udpClient != null;

            if (captureCoroutine != null)
            {
                StopCoroutine(captureCoroutine);
                captureCoroutine = null;
            }

            FinishPendingReadback();
            StopSenderThread();
            CleanupTransport();
            ReleaseCaptureResources();

            if (
                wasActive &&
                debugLogging &&
                (PacketsSent > 0 || SendFailures > 0)
            )
            {
                Debug.Log(
                    "[SpectatorViewStreamer][STOPPED] " +
                    $"frames={FramesSent}, packets={PacketsSent}, " +
                    $"bytes={BytesSent}, dropped={FramesDropped}, " +
                    $"failures={SendFailures}."
                );
            }
        }

        private IEnumerator CaptureLoop()
        {
            while (true)
            {
                yield return endOfFrame;

                double now = Time.realtimeSinceStartupAsDouble;
                ProcessWorkerError(now);

                if (readbackPending && readbackRequest.done)
                {
                    ProcessCompletedReadback();
                }

                if (!readbackPending && now >= nextCaptureTime)
                {
                    nextCaptureTime = now + 1.0 / frameRate;
                    CaptureFrame(now);
                }

                if (debugLogging && now >= nextDebugLogTime)
                {
                    LogDiagnostics();
                    nextDebugLogTime =
                        now + Math.Max(debugLogIntervalSeconds, 1f);
                }
            }
        }

        private void CaptureFrame(double now)
        {
            Camera settingsCamera = ResolveSourceCamera();

            if (settingsCamera == null)
            {
                if (now >= nextCameraWarningTime)
                {
                    Debug.LogWarning(
                        "[SpectatorViewStreamer][CAMERA WAITING] " +
                        "Assign Source Camera or tag the XR center-eye camera " +
                        "as MainCamera."
                    );
                    nextCameraWarningTime = now + 5.0;
                }

                return;
            }

            EnsureCaptureResources();
            PrepareCaptureCamera(settingsCamera);

            uint frameId = nextFrameId++;
            byte flags = BuildFrameFlags();

            try
            {
                SubmitCaptureRenderRequest();

                if (
                    useAsyncGpuReadback &&
                    !forceSynchronousReadback &&
                    SystemInfo.supportsAsyncGPUReadback
                )
                {
                    pendingFrameId = frameId;
                    pendingFrameFlags = flags;
                    pendingFrameWidth = width;
                    pendingFrameHeight = height;
                    pendingJpegQuality = jpegQuality;
                    readbackRequest = AsyncGPUReadback.Request(
                        captureTexture,
                        0,
                        TextureFormat.RGB24
                    );
                    readbackPending = true;
                }
                else
                {
                    CaptureSynchronously(frameId, flags);
                }
            }
            catch (Exception exception)
            {
                ReportCaptureError("capture", exception);
            }
        }

        private void ProcessCompletedReadback()
        {
            readbackPending = false;

            if (readbackRequest.hasError)
            {
                forceSynchronousReadback = true;
                ReportCaptureError(
                    "GPU readback",
                    new InvalidOperationException(
                        "Async GPU readback failed; falling back to ReadPixels."
                    )
                );
                return;
            }

            NativeArray<byte> encoded = default;

            try
            {
                encoded = ImageConversion.EncodeNativeArrayToJPG(
                    readbackRequest.GetData<byte>(),
                    GraphicsFormat.R8G8B8_UNorm,
                    (uint)pendingFrameWidth,
                    (uint)pendingFrameHeight,
                    0,
                    pendingJpegQuality
                );
                QueueFrame(
                    encoded.ToArray(),
                    pendingFrameId,
                    pendingFrameFlags,
                    pendingFrameWidth,
                    pendingFrameHeight
                );
                lastError = string.Empty;
            }
            catch (Exception exception)
            {
                forceSynchronousReadback = true;
                ReportCaptureError("JPEG encoding", exception);
            }
            finally
            {
                if (encoded.IsCreated)
                {
                    encoded.Dispose();
                }
            }
        }

        private void CaptureSynchronously(uint frameId, byte flags)
        {
            if (
                synchronousReadbackTexture == null ||
                synchronousReadbackTexture.width != width ||
                synchronousReadbackTexture.height != height
            )
            {
                DestroyUnityObject(synchronousReadbackTexture);
                synchronousReadbackTexture = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGB24,
                    false
                )
                {
                    name = "Spectator Synchronous Readback",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            RenderTexture previous = RenderTexture.active;

            try
            {
                RenderTexture.active = captureTexture;
                synchronousReadbackTexture.ReadPixels(
                    new Rect(0f, 0f, width, height),
                    0,
                    0,
                    false
                );
                synchronousReadbackTexture.Apply(false, false);
                byte[] jpeg = synchronousReadbackTexture.EncodeToJPG(
                    jpegQuality
                );
                QueueFrame(jpeg, frameId, flags, width, height);
                lastError = string.Empty;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private void PrepareCaptureCamera(Camera settingsCamera)
        {
            captureCamera.CopyFrom(settingsCamera);
            captureCamera.enabled = false;
            // URP owns stereo target configuration and logs a warning when
            // this legacy built-in-pipeline property is assigned.
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
            }
            captureCamera.targetTexture = captureTexture;
            captureCamera.aspect = (float)width / height;
            captureCamera.allowHDR = false;
            captureCamera.allowMSAA = false;
            captureCamera.useOcclusionCulling = settingsCamera.useOcclusionCulling;

            Transform pose = viewMode == SpectatorViewMode.HeadsetPov
                ? settingsCamera.transform
                : fixedViewAnchor != null
                    ? fixedViewAnchor
                    : transform;
            captureCamera.transform.SetPositionAndRotation(
                pose.position,
                pose.rotation
            );

            UniversalAdditionalCameraData captureData =
                captureCamera.GetUniversalAdditionalCameraData();
            captureData.renderType = CameraRenderType.Base;
            captureData.renderPostProcessing = false;

            if (
                settingsCamera.TryGetComponent(
                    out UniversalAdditionalCameraData sourceData
                )
            )
            {
                captureData.renderShadows = sourceData.renderShadows;
            }
        }

        private void SubmitCaptureRenderRequest()
        {
            renderRequest ??=
                new UniversalRenderPipeline.SingleCameraRequest();
            renderRequest.destination = captureTexture;

            if (
                !RenderPipeline.SupportsRenderRequest(
                    captureCamera,
                    renderRequest
                )
            )
            {
                throw new InvalidOperationException(
                    "The active render pipeline does not support URP single-camera " +
                    "render requests."
                );
            }

            RenderPipeline.SubmitRenderRequest(captureCamera, renderRequest);
        }

        private Camera ResolveSourceCamera()
        {
            if (sourceCamera != null)
            {
                return sourceCamera;
            }

            return Camera.main;
        }

        private void EnsureCaptureResources()
        {
            bool correctTexture =
                captureTexture != null &&
                captureTexture.width == width &&
                captureTexture.height == height;

            if (!correctTexture)
            {
                ReleaseCaptureResources();
            }

            if (captureCamera == null)
            {
                GameObject cameraObject = new GameObject(
                    "Spectator Capture Camera"
                )
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                cameraObject.transform.SetParent(transform, false);
                captureCamera = cameraObject.AddComponent<Camera>();
                captureCamera.enabled = false;
                cameraObject.AddComponent<UniversalAdditionalCameraData>();
            }

            if (captureTexture == null)
            {
                captureTexture = new RenderTexture(
                    width,
                    height,
                    16,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default
                )
                {
                    name = "Spectator Capture",
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                captureTexture.Create();
            }
        }

        private void QueueFrame(
            byte[] jpeg,
            uint frameId,
            byte flags,
            int frameWidth,
            int frameHeight
        )
        {
            if (jpeg == null || jpeg.Length == 0)
            {
                Interlocked.Increment(ref framesDropped);
                return;
            }

            if (jpeg.Length > SpectatorViewProtocol.MaxFrameBytes)
            {
                Interlocked.Increment(ref framesDropped);
                lastError =
                    $"Encoded frame is too large ({jpeg.Length} bytes).";
                return;
            }

            int chunkCount = SpectatorViewProtocol.CalculateChunkCount(
                jpeg.Length,
                maxDatagramBytes
            );

            if (chunkCount > SpectatorViewProtocol.MaxChunkCount)
            {
                Interlocked.Increment(ref framesDropped);
                lastError =
                    $"Encoded frame requires too many chunks ({chunkCount}).";
                return;
            }

            var frame = new EncodedFrame(
                jpeg,
                frameId,
                flags,
                checked((ushort)frameWidth),
                checked((ushort)frameHeight)
            );

            lock (sendLock)
            {
                if (pendingSend != null)
                {
                    Interlocked.Increment(ref framesDropped);
                }

                pendingSend = frame;
            }

            senderSignal?.Set();
        }

        private void SenderLoop()
        {
            while (senderRunning)
            {
                senderSignal.WaitOne(250);

                if (!senderRunning)
                {
                    break;
                }

                EncodedFrame frame;

                lock (sendLock)
                {
                    frame = pendingSend;
                    pendingSend = null;
                }

                if (frame != null)
                {
                    SendFrame(frame);
                }
            }
        }

        private void SendFrame(EncodedFrame frame)
        {
            try
            {
                int datagramSize = maxDatagramBytes;
                int payloadSize = SpectatorViewProtocol.GetChunkPayloadSize(
                    datagramSize
                );
                int chunkCount = SpectatorViewProtocol.CalculateChunkCount(
                    frame.Jpeg.Length,
                    datagramSize
                );

                if (chunkCount > SpectatorViewProtocol.MaxChunkCount)
                {
                    throw new InvalidOperationException(
                        $"A frame requires too many chunks ({chunkCount})."
                    );
                }

                byte[] datagram = new byte[datagramSize];

                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    int sourceOffset = chunkIndex * payloadSize;
                    int bytesThisChunk = Math.Min(
                        payloadSize,
                        frame.Jpeg.Length - sourceOffset
                    );
                    SpectatorViewProtocol.WriteHeader(
                        datagram,
                        frame.Flags,
                        frame.Id,
                        (ushort)chunkIndex,
                        (ushort)chunkCount,
                        frame.Width,
                        frame.Height,
                        (uint)frame.Jpeg.Length
                    );
                    Buffer.BlockCopy(
                        frame.Jpeg,
                        sourceOffset,
                        datagram,
                        SpectatorViewProtocol.HeaderSize,
                        bytesThisChunk
                    );

                    int datagramLength =
                        SpectatorViewProtocol.HeaderSize + bytesThisChunk;
                    udpClient.Send(datagram, datagramLength);
                    Interlocked.Increment(ref packetsSent);
                    Interlocked.Add(ref bytesSent, datagramLength);
                }

                Interlocked.Increment(ref framesSent);
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref sendFailures);
                Interlocked.Increment(ref framesDropped);
                workerError = exception.Message;
            }
        }

        private byte BuildFrameFlags()
        {
            byte flags = 0;

            if (viewMode == SpectatorViewMode.HeadsetPov)
            {
                flags |= SpectatorViewProtocol.HeadsetPovFlag;
            }

            if (flipVerticallyOnDesktop)
            {
                flags |= SpectatorViewProtocol.FlipVerticalFlag;
            }

            return flags;
        }

        private void ProcessWorkerError(double now)
        {
            string error = workerError;

            if (string.IsNullOrEmpty(error))
            {
                return;
            }

            workerError = string.Empty;
            lastError = error;

            if (now >= nextErrorLogTime)
            {
                Debug.LogWarning(
                    "[SpectatorViewStreamer][UDP SEND FAILED] " + error
                );
                nextErrorLogTime = now + 5.0;
            }
        }

        private void ReportCaptureError(string stage, Exception exception)
        {
            lastError = exception.Message;
            double now = Time.realtimeSinceStartupAsDouble;

            if (now >= nextErrorLogTime)
            {
                Debug.LogWarning(
                    $"[SpectatorViewStreamer][{stage.ToUpperInvariant()} FAILED] " +
                    exception.Message
                );
                nextErrorLogTime = now + 5.0;
            }
        }

        private void LogDiagnostics()
        {
            Debug.Log(
                "[SpectatorViewStreamer][DIAGNOSTICS] " +
                $"mode={viewMode}, destination={Destination}, " +
                $"frames={FramesSent}, packets={PacketsSent}, " +
                $"bytes={BytesSent}, dropped={FramesDropped}, " +
                $"failures={SendFailures}, readback=" +
                $"{(forceSynchronousReadback ? "sync" : "async")}."
            );
        }

        private void FinishPendingReadback()
        {
            if (!readbackPending)
            {
                return;
            }

            try
            {
                readbackRequest.WaitForCompletion();
            }
            catch (Exception)
            {
                // Shutdown must still release the camera and socket.
            }

            readbackPending = false;
        }

        private void StopSenderThread()
        {
            senderRunning = false;

            lock (sendLock)
            {
                if (pendingSend != null)
                {
                    pendingSend = null;
                    Interlocked.Increment(ref framesDropped);
                }
            }

            senderSignal?.Set();

            if (senderThread != null && senderThread.IsAlive)
            {
                if (!senderThread.Join(1000))
                {
                    // Closing the socket unblocks a send if the OS buffer is
                    // saturated. The worker catches that disposal and exits.
                    udpClient?.Dispose();
                    senderThread.Join(1000);
                }
            }

            senderThread = null;
            senderSignal?.Dispose();
            senderSignal = null;
        }

        private void CleanupTransport()
        {
            udpClient?.Dispose();
            udpClient = null;
            remoteEndPoint = null;
        }

        private void ReleaseCaptureResources()
        {
            if (captureCamera != null)
            {
                captureCamera.targetTexture = null;
                DestroyUnityObject(captureCamera.gameObject);
                captureCamera = null;
            }

            if (captureTexture != null)
            {
                captureTexture.Release();
                DestroyUnityObject(captureTexture);
                captureTexture = null;
            }

            DestroyUnityObject(synchronousReadbackTexture);
            synchronousReadbackTexture = null;
            renderRequest = null;
        }

        private void ResetDiagnostics()
        {
            Interlocked.Exchange(ref packetsSent, 0);
            Interlocked.Exchange(ref framesSent, 0);
            Interlocked.Exchange(ref bytesSent, 0);
            Interlocked.Exchange(ref sendFailures, 0);
            Interlocked.Exchange(ref framesDropped, 0);
            nextFrameId = 0;
            readbackPending = false;
            forceSynchronousReadback = false;
            workerError = string.Empty;
            lastError = string.Empty;
        }

        private void ValidateSettings()
        {
            width = Mathf.Clamp(width, 160, 1920);
            height = Mathf.Clamp(height, 90, 1080);
            frameRate = Mathf.Clamp(frameRate, 1, 30);
            jpegQuality = Mathf.Clamp(jpegQuality, 10, 90);
            remotePort = Mathf.Clamp(remotePort, 1, 65535);
            maxDatagramBytes = Mathf.Clamp(maxDatagramBytes, 512, 1400);
            debugLogIntervalSeconds = Mathf.Max(
                debugLogIntervalSeconds,
                1f
            );
        }

        private static IPAddress ResolveIPv4Address(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Remote host is empty.");
            }

            if (IPAddress.TryParse(host, out IPAddress parsed))
            {
                if (parsed.AddressFamily != AddressFamily.InterNetwork)
                {
                    throw new ArgumentException(
                        "Only IPv4 destinations are supported."
                    );
                }

                return parsed;
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
                $"Host '{host}' did not resolve to an IPv4 address."
            );
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private void OnValidate()
        {
            ValidateSettings();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                resumeAfterPause = IsStreaming;
                StopStreaming();
            }
            else if (resumeAfterPause && isActiveAndEnabled)
            {
                resumeAfterPause = false;
                StartStreaming();
            }
        }

        private void OnEnable()
        {
            if (
                Application.isPlaying &&
                resumeAfterDisable &&
                streamAutomatically
            )
            {
                resumeAfterDisable = false;
                StartStreaming();
            }
        }

        private void OnDisable()
        {
            resumeAfterDisable = IsStreaming;
            StopStreaming();
        }

        private void OnDestroy()
        {
            StopStreaming();
        }

        private sealed class EncodedFrame
        {
            public EncodedFrame(
                byte[] jpeg,
                uint id,
                byte flags,
                ushort width,
                ushort height
            )
            {
                Jpeg = jpeg;
                Id = id;
                Flags = flags;
                Width = width;
                Height = height;
            }

            public byte[] Jpeg { get; }
            public uint Id { get; }
            public byte Flags { get; }
            public ushort Width { get; }
            public ushort Height { get; }
        }
    }
}
