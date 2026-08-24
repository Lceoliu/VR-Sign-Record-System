using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace SignVR.Recording
{
    [DefaultExecutionOrder(-9000)]
    public sealed class QuestDeviceGateway : MonoBehaviour
    {
        private const int ProtocolVersion = 3;
        private const int ControlPort = 5012;
        private const int HostAnnouncementPort = 5011;
        private const string PointingPairingKey = "signvr-pointing-2026-01";
        private const string DeviceIdPlayerPrefsKey = "SignVR.DeviceId";
        private const string PairedStationPlayerPrefsKey = "SignVR.PairedStationId";

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

        [SerializeField]
        private RecordingSentenceSequence sentenceSequence;

        [SerializeField]
        private RecordingViewpointController viewpointController;

        [Header("Discovery")]
        [SerializeField]
        [Min(1f)]
        private float announcementIntervalSeconds = 3f;

        private UdpClient listener;
        private UdpClient sender;
        private string deviceId;
        private string pairedStationId;
        private IPAddress pairedHostAddress;
        private int pairedHttpPort;
        private int pairedPosePort;
        private Coroutine announcementRoutine;
        private readonly ConcurrentQueue<ReceivedDatagram> receivedDatagrams = new();
        private readonly ConcurrentQueue<string> receiveErrors = new();
        private CommandPacket pendingSentenceSelection;
        private bool pendingSentenceIndexSupplied;

        public int ReceivedPacketCount { get; private set; }
        public string LastPacketType { get; private set; } = string.Empty;
        public string LastReceiveError { get; private set; } = string.Empty;

        /// <summary>
        /// Injects the recording stack before this component is enabled.  The
        /// source project serialized these references in one scene; the target
        /// project also supports additive/runtime scene composition.
        /// </summary>
        public void Configure(
            RecordingCoordinator recordingCoordinator,
            MetaBodyMotionRecorder recordingRecorder,
            MetaBodyMotionStreamer recordingStreamer,
            QuestTakeUploader uploader,
            QuestPreviewStreamer preview,
            HandCaptureBoundaryMonitor monitor,
            RecordingSentenceSequence sequence = null,
            RecordingViewpointController viewpoints = null)
        {
            coordinator = recordingCoordinator;
            recorder = recordingRecorder;
            motionStreamer = recordingStreamer;
            takeUploader = uploader;
            previewStreamer = preview;
            boundaryMonitor = monitor;
            sentenceSequence = sequence;
            viewpointController = viewpoints;
        }

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
            public string station_id;
            public string device_id;
            public string pairing_key;
        }

        [Serializable]
        private sealed class CommandPacket
        {
            public string type;
            public int version;
            public string command_id;
            public string action;
            public string station_id;
            public string session_id;
            public string sentence_id;
            public int sentence_index;
            public string viewpoint_id;
            public string prompt;
            public string take_id;
            public int take_index;
            public long start_at_unix_ms;
            public float countdown_seconds;

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
            public string station_id;
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
            public string paired_station_id;
            public bool paired;
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
            pairedStationId = PlayerPrefs.GetString(
                PairedStationPlayerPrefsKey,
                string.Empty
            );

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                deviceId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceIdPlayerPrefsKey, deviceId);
                PlayerPrefs.Save();
            }

            if (coordinator == null || recorder == null || motionStreamer == null ||
                takeUploader == null)
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
                station_id = pairedStationId,
                signal = "help",
                active = active
            };

            SendPacket(
                packet,
                new IPEndPoint(pairedHostAddress, HostAnnouncementPort)
            );
        }

        private void OnEnable()
        {
            try
            {
                StartListener();
                announcementRoutine = StartCoroutine(AnnouncementLoop());
            }
            catch (SocketException exception)
            {
                LastReceiveError = exception.Message;
                Debug.LogError(
                    "[QuestDeviceGateway] Could not bind UDP control port " +
                    $"{ControlPort}: {exception.Message}"
                );
                enabled = false;
            }
        }

        private void Update()
        {
            while (receivedDatagrams.TryDequeue(out ReceivedDatagram datagram))
            {
                HandleDatagram(datagram);
            }

            while (receiveErrors.TryDequeue(out string error))
            {
                LastReceiveError = error;
                Debug.LogError("[QuestDeviceGateway] UDP receive failed: " + error);
            }

            TryApplyPendingSentenceSelection();
        }

        private void ReceiveDatagram(IAsyncResult result)
        {
            var activeListener = (UdpClient)result.AsyncState;
            try
            {
                var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = activeListener.EndReceive(result, ref remoteEndPoint);
                receivedDatagrams.Enqueue(
                    new ReceivedDatagram(
                        Encoding.UTF8.GetString(bytes),
                        remoteEndPoint
                    )
                );
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException exception)
            {
                receiveErrors.Enqueue(exception.Message);
            }

            if (ReferenceEquals(activeListener, listener))
            {
                try
                {
                    activeListener.BeginReceive(ReceiveDatagram, activeListener);
                }
                catch (ObjectDisposedException)
                {
                    // Shutdown can race the async callback. The socket is
                    // already closed, so there is nothing left to receive.
                }
                catch (SocketException exception)
                {
                    receiveErrors.Enqueue(exception.Message);
                }
            }
        }

        private IEnumerator AnnouncementLoop()
        {
            var wait = new WaitForSecondsRealtime(announcementIntervalSeconds);

            while (true)
            {
                IPEndPoint target = pairedHostAddress == null
                    ? new IPEndPoint(IPAddress.Broadcast, HostAnnouncementPort)
                    : new IPEndPoint(pairedHostAddress, HostAnnouncementPort);

                SendAnnouncement(target);
                yield return wait;
            }
        }

        private void HandleDatagram(ReceivedDatagram datagram)
        {
            PacketEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<PacketEnvelope>(datagram.Json);
            }
            catch (ArgumentException exception)
            {
                LastReceiveError = "Invalid UDP JSON: " + exception.Message;
                return;
            }

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.type))
            {
                LastReceiveError = "UDP packet has no type.";
                return;
            }

            ReceivedPacketCount++;
            LastPacketType = envelope.type;

            try
            {
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
            catch (Exception exception)
            {
                LastReceiveError = "UDP packet handling failed: " + exception.Message;
                Debug.LogWarning(
                    "[QuestDeviceGateway] Ignoring malformed UDP packet: " +
                    exception.Message
                );
            }
        }

        private void HandleDiscover(string json, IPEndPoint remoteEndPoint)
        {
            DiscoverPacket packet = JsonUtility.FromJson<DiscoverPacket>(json);
            if (packet == null || remoteEndPoint == null)
            {
                return;
            }

            int replyPort = packet.reply_port > 0
                ? packet.reply_port
                : remoteEndPoint.Port;

            SendAnnouncement(new IPEndPoint(remoteEndPoint.Address, replyPort));
        }

        private void HandlePair(string json, IPEndPoint remoteEndPoint)
        {
            PairPacket packet = JsonUtility.FromJson<PairPacket>(json);
            if (packet == null || remoteEndPoint == null)
            {
                return;
            }

            bool hostAddressValid = IPAddress.TryParse(
                packet.host_ip,
                out IPAddress hostAddress
            );
            bool deviceTargetValid = string.Equals(
                packet.device_id,
                deviceId,
                StringComparison.OrdinalIgnoreCase
            );
            bool pairingKeyValid = string.Equals(
                packet.pairing_key,
                PointingPairingKey,
                StringComparison.Ordinal
            );
            bool stationValid = !string.IsNullOrWhiteSpace(packet.station_id);
            bool accepted = packet.version == ProtocolVersion &&
                deviceTargetValid && pairingKeyValid &&
                hostAddressValid && stationValid &&
                packet.http_port > 0 && packet.pose_port > 0;

            string message = "paired";
            if (packet.version != ProtocolVersion)
            {
                message = "protocol_version_mismatch";
            }
            else if (!deviceTargetValid)
            {
                message = "device_id_mismatch";
            }
            else if (!pairingKeyValid)
            {
                message = "pairing_key_mismatch";
            }
            else if (!hostAddressValid)
            {
                message = "host_ip_invalid";
            }
            else if (!stationValid)
            {
                message = "station_id_missing";
            }
            if (accepted)
            {
                bool endpointChanged = pairedHostAddress == null ||
                    !pairedHostAddress.Equals(hostAddress) ||
                    pairedHttpPort != packet.http_port ||
                    pairedPosePort != packet.pose_port;
                pairedStationId = packet.station_id.Trim();
                pairedHostAddress = hostAddress;
                pairedHttpPort = packet.http_port;
                pairedPosePort = packet.pose_port;
                PlayerPrefs.SetString(
                    PairedStationPlayerPrefsKey,
                    pairedStationId
                );
                PlayerPrefs.Save();
                string baseUrl = $"http://{packet.host_ip}:{packet.http_port}";

                if (endpointChanged)
                {
                    motionStreamer.ConfigureDestination(
                        packet.host_ip,
                        packet.pose_port
                    );
                    takeUploader.ConfigureHost(baseUrl, deviceId);
                    previewStreamer?.ConfigureHost(baseUrl, deviceId);
                }
                if (sentenceSequence != null)
                {
                    sentenceSequence.HostAuthoritative = true;
                }
            }

            SendAck(
                remoteEndPoint,
                packet.command_id,
                accepted,
                message
            );
        }

        private void HandleCommand(string json, IPEndPoint remoteEndPoint)
        {
            CommandPacket packet = JsonUtility.FromJson<CommandPacket>(json);
            if (packet == null || remoteEndPoint == null)
            {
                return;
            }

            if (packet.version != ProtocolVersion)
            {
                SendAck(
                    remoteEndPoint,
                    packet.command_id,
                    false,
                    "protocol_version_mismatch"
                );
                return;
            }

            bool hostMatches = pairedHostAddress != null &&
                remoteEndPoint.Address.Equals(pairedHostAddress);
            bool stationMatches = !string.IsNullOrWhiteSpace(pairedStationId) &&
                string.Equals(
                    packet.station_id,
                    pairedStationId,
                    StringComparison.Ordinal
                );
            if (!hostMatches || !stationMatches)
            {
                SendAck(
                    remoteEndPoint,
                    packet.command_id,
                    false,
                    hostMatches ? "station_id_mismatch" : "host_not_paired"
                );
                return;
            }

            LastReceiveError = string.Empty;
            bool accepted;
            bool sentenceIndexSupplied = ContainsJsonProperty(
                json,
                "sentence_index"
            );

            switch (packet.action)
            {
                case "start_take":
                    accepted = StartRemoteTake(
                        packet,
                        sentenceIndexSupplied
                    );
                    break;
                case "stop_take":
                    accepted = StopRemoteTake();
                    break;
                case "reset_take":
                    accepted = ResetRemoteTake(
                        packet,
                        sentenceIndexSupplied
                    );
                    break;
                case "select_sentence":
                    accepted = SelectRemoteSentence(
                        packet,
                        sentenceIndexSupplied
                    );
                    break;
                case "pedal":
                    accepted = HandlePedal(packet);
                    break;
                case "set_guidance":
                    if (boundaryMonitor == null)
                    {
                        accepted = false;
                        break;
                    }
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
                accepted
                    ? packet.action
                    : string.IsNullOrWhiteSpace(LastReceiveError)
                        ? "command_rejected"
                        : LastReceiveError
            );
        }

        private bool SelectRemoteSentence(
            CommandPacket packet,
            bool sentenceIndexSupplied)
        {
            if (!IsRemoteSentenceSelectionValid(
                    packet,
                    sentenceIndexSupplied))
            {
                return false;
            }

            if (CanApplySentenceSelection(coordinator.State))
            {
                return ApplyRemoteSentenceSelection(
                    packet,
                    sentenceIndexSupplied
                );
            }

            if (coordinator.State != RecordingFlowState.Finalizing &&
                coordinator.State != RecordingFlowState.Resetting)
            {
                return false;
            }

            QueueSentenceSelection(packet, sentenceIndexSupplied);
            return true;
        }

        private bool ResetRemoteTake(
            CommandPacket packet,
            bool sentenceIndexSupplied)
        {
            if (!IsRemoteSentenceSelectionValid(
                    packet,
                    sentenceIndexSupplied))
            {
                return false;
            }

            coordinator.ResetCurrentPrompt();
            QueueSentenceSelection(packet, sentenceIndexSupplied);
            return true;
        }

        private bool StopRemoteTake()
        {
            if (coordinator == null)
            {
                return RejectCommand("recording_coordinator_missing");
            }

            // Stop is idempotent over UDP. This prevents a delayed/retried host
            // packet from surfacing as command_rejected after a Take has
            // already finalized or a countdown has already been cancelled.
            if (coordinator.State == RecordingFlowState.Ready ||
                coordinator.State == RecordingFlowState.Completed)
            {
                return true;
            }

            return coordinator.StopCurrentTake() ||
                   RejectCommand(
                       "stop_take_failed_state_" +
                       coordinator.State.ToString().ToLowerInvariant()
                   );
        }

        private bool IsRemoteSentenceSelectionValid(
            CommandPacket packet,
            bool sentenceIndexSupplied)
        {
            if (packet == null ||
                string.IsNullOrWhiteSpace(packet.session_id) ||
                string.IsNullOrWhiteSpace(packet.sentence_id))
            {
                return false;
            }

            if (viewpointController == null)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(packet.viewpoint_id) ||
                   (sentenceSequence != null && sentenceSequence.TryGetSentence(
                       packet.sentence_id,
                       out _,
                       out _
                   )) ||
                   (sentenceIndexSupplied && packet.sentence_index >= 0 &&
                    packet.sentence_index < viewpointController.ViewpointCount);
        }

        private bool ApplyRemoteSentenceSelection(
            CommandPacket packet,
            bool sentenceIndexSupplied)
        {
            if (!CanApplySentenceSelection(coordinator.State) ||
                !IsRemoteSentenceSelectionValid(
                    packet,
                    sentenceIndexSupplied))
            {
                return false;
            }

            if (viewpointController != null &&
                !TrySelectRemoteViewpoint(packet, sentenceIndexSupplied))
            {
                return false;
            }

            if (sentenceSequence != null)
            {
                sentenceSequence.HostAuthoritative = true;
            }

            bool promptLoaded = coordinator.LoadPrompt(
                packet.session_id,
                packet.sentence_id,
                packet.prompt,
                Mathf.Max(1, packet.take_index)
            );
            if (!promptLoaded)
            {
                return false;
            }

            sentenceSequence?.SyncHostSentence(packet.sentence_id);
            return true;
        }

        private void QueueSentenceSelection(
            CommandPacket packet,
            bool sentenceIndexSupplied)
        {
            pendingSentenceSelection = packet;
            pendingSentenceIndexSupplied = sentenceIndexSupplied;
        }

        private void TryApplyPendingSentenceSelection()
        {
            if (pendingSentenceSelection == null || coordinator == null ||
                !CanApplySentenceSelection(coordinator.State))
            {
                return;
            }

            CommandPacket packet = pendingSentenceSelection;
            bool sentenceIndexSupplied = pendingSentenceIndexSupplied;
            pendingSentenceSelection = null;
            pendingSentenceIndexSupplied = false;

            if (ApplyRemoteSentenceSelection(packet, sentenceIndexSupplied))
            {
                return;
            }

            LastReceiveError =
                "Deferred sentence selection could not be applied.";
            Debug.LogWarning(
                "[QuestDeviceGateway] " + LastReceiveError,
                this
            );
        }

        private static bool CanApplySentenceSelection(
            RecordingFlowState state)
        {
            return state == RecordingFlowState.Ready ||
                   state == RecordingFlowState.Completed;
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

        private bool StartRemoteTake(
            CommandPacket packet,
            bool sentenceIndexSupplied)
        {
            if (packet == null || coordinator == null || recorder == null ||
                string.IsNullOrWhiteSpace(packet.session_id) ||
                string.IsNullOrWhiteSpace(packet.sentence_id) ||
                string.IsNullOrWhiteSpace(packet.take_id) ||
                packet.take_index < 1)
            {
                return RejectCommand(
                    "recording_context_invalid"
                );
            }

            if (!recorder.IsPoseReady)
            {
                // Meta tracking can briefly report invalid between otherwise
                // valid frames. Accept the host command and let the recorder
                // perform the authoritative pose check after the countdown.
                Debug.LogWarning(
                    "[QuestDeviceGateway] Body pose was not ready when the " +
                    "start command arrived; validation is deferred until " +
                    "capture begins.",
                    this
                );
            }

            if (viewpointController != null)
            {
                if (!TrySelectRemoteViewpoint(packet, sentenceIndexSupplied))
                {
                    return RejectCommand("viewpoint_selection_failed");
                }
            }

            if (sentenceSequence != null)
            {
                sentenceSequence.HostAuthoritative = true;
            }

            bool promptLoaded = coordinator.LoadPrompt(
                packet.session_id,
                packet.sentence_id,
                packet.prompt,
                packet.take_index
            );

            if (!promptLoaded)
            {
                return RejectCommand(
                    "prompt_load_failed_state_" +
                    coordinator.State.ToString().ToLowerInvariant()
                );
            }

            // The host owns the recording state, but the local sequence still
            // mirrors its sentence index so the in-headset progress label and
            // editable viewpoint mapping remain truthful.
            sentenceSequence?.SyncHostSentence(packet.sentence_id);

            var take = new RecordingTakeContext(
                packet.session_id,
                packet.sentence_id,
                packet.prompt,
                packet.take_index,
                packet.take_id,
                DateTime.UtcNow
            );

            if (coordinator.BeginRemoteTake(take, packet.countdown_seconds))
            {
                return true;
            }

            string detail = !string.IsNullOrWhiteSpace(coordinator.LastError)
                ? coordinator.LastError
                : !string.IsNullOrWhiteSpace(recorder.LastError)
                    ? recorder.LastError
                    : "state_" + coordinator.State.ToString().ToLowerInvariant();
            return RejectCommand("start_take_failed:" + detail);
        }

        private bool RejectCommand(string reason)
        {
            LastReceiveError = string.IsNullOrWhiteSpace(reason)
                ? "command_rejected"
                : reason;
            Debug.LogWarning(
                "[QuestDeviceGateway] Command rejected: " + LastReceiveError,
                this
            );
            return false;
        }

        private bool TrySelectRemoteViewpoint(
            CommandPacket packet,
            bool sentenceIndexSupplied)
        {
            if (!string.IsNullOrWhiteSpace(packet.viewpoint_id))
            {
                return viewpointController.TrySelectViewpoint(
                    packet.viewpoint_id.Trim()
                );
            }

            if (sentenceSequence != null && sentenceSequence.TryGetSentence(
                    packet.sentence_id,
                    out int sequenceIndex,
                    out RecordingSentence sentence
                ))
            {
                return !string.IsNullOrWhiteSpace(sentence.ViewpointId)
                    ? viewpointController.TrySelectViewpoint(
                        sentence.ViewpointId.Trim()
                    )
                    : viewpointController.TrySelectViewpoint(sequenceIndex);
            }

            return sentenceIndexSupplied && packet.sentence_index >= 0 &&
                packet.sentence_index < viewpointController.ViewpointCount &&
                viewpointController.TrySelectViewpoint(packet.sentence_index);
        }

        private static bool ContainsJsonProperty(
            string json,
            string propertyName)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            for (int index = 0; index < json.Length; index++)
            {
                if (json[index] != '"')
                {
                    continue;
                }

                int valueStart = ++index;
                bool escaped = false;
                while (index < json.Length)
                {
                    char current = json[index];
                    if (current == '\\')
                    {
                        escaped = true;
                        index++;
                    }
                    else if (current == '"')
                    {
                        break;
                    }

                    index++;
                }

                if (index >= json.Length)
                {
                    return false;
                }

                int valueLength = index - valueStart;
                int separatorIndex = index + 1;
                while (separatorIndex < json.Length &&
                    char.IsWhiteSpace(json[separatorIndex]))
                {
                    separatorIndex++;
                }

                if (!escaped && separatorIndex < json.Length &&
                    json[separatorIndex] == ':' &&
                    valueLength == propertyName.Length &&
                    string.CompareOrdinal(
                        json,
                        valueStart,
                        propertyName,
                        0,
                        propertyName.Length
                    ) == 0)
                {
                    return true;
                }
            }

            return false;
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
                control_port = ControlPort,
                paired_station_id = pairedStationId,
                paired = pairedHostAddress != null,
                capabilities = previewStreamer != null && previewStreamer.isActiveAndEnabled
                    ? new[]
                    {
                        "pose",
                        "preview",
                        "take_upload"
                    }
                    : new[]
                    {
                        "pose",
                        "take_upload"
                    },
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
            if (sender == null || packet == null || target == null)
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
            try
            {
                sender.Send(bytes, bytes.Length, target);
            }
            catch (ObjectDisposedException)
            {
                // The app can shut down while an announcement coroutine is
                // between frames.
            }
            catch (SocketException exception)
            {
                LastReceiveError = exception.Message;
            }
        }

        private void StartListener()
        {
            listener = new UdpClient(new IPEndPoint(IPAddress.Any, ControlPort));
            listener.BeginReceive(ReceiveDatagram, listener);
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
            pendingSentenceSelection = null;
            pendingSentenceIndexSupplied = false;
        }
    }
}
