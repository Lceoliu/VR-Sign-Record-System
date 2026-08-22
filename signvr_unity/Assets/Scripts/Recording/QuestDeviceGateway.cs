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

        [Header("Recording Safety")]
        [SerializeField]
        [Min(30f)]
        private float maximumRecordingSeconds = 600f;

        private UdpClient listener;
        private UdpClient sender;
        private string deviceId;
        private string pairedStationId;
        private IPAddress pairedHostAddress;
        private Coroutine announcementRoutine;
        private bool safetyStopTriggered;
        private string pendingStartCommandId;
        private string pendingStartTakeId;
        private IPEndPoint pendingStartAckTarget;
        private string lastStartCommandId;
        private string lastStartTakeId;
        private string lastStartMessage;
        private string lastStartPhase;
        private bool lastStartAccepted;
        private long lastStartActualAtUnixMs;
        private readonly ConcurrentQueue<ReceivedDatagram> receivedDatagrams = new();
        private readonly ConcurrentQueue<string> receiveErrors = new();

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
            public string station_id;
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
            public string previous_prompt;
            public string next_prompt;
            public int total_sentences;
            public string signing_mode;
            public string mode_switch_notice;
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
            public string message;
            public string take_id;
            public long actual_at_unix_ms;
            public int direction;
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
            public string action;
            public string phase;
            public string take_id;
            public long actual_at_unix_ms;
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
                station_id = pairedStationId,
                signal = "help",
                active = active
            };

            SendPacket(
                packet,
                new IPEndPoint(pairedHostAddress, hostAnnouncementPort)
            );
        }

        public bool RequestSentenceNavigation(int direction)
        {
            if (direction != -1 && direction != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            if (pairedHostAddress == null ||
                (coordinator.State != RecordingFlowState.Ready &&
                 coordinator.State != RecordingFlowState.Completed))
            {
                return false;
            }

            var packet = new SignalPacket
            {
                device_id = deviceId,
                station_id = pairedStationId,
                signal = "navigate_sentence",
                direction = direction
            };
            SendPacket(packet, new IPEndPoint(pairedHostAddress, hostAnnouncementPort));
            return true;
        }

        private void OnEnable()
        {
            if (coordinator != null)
            {
                coordinator.TakeStartResolved += HandleTakeStartResolved;
            }
            StartListener();
            announcementRoutine = StartCoroutine(AnnouncementLoop());
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

            MonitorRecordingSafety();
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
                activeListener.BeginReceive(ReceiveDatagram, activeListener);
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
            bool hostAddressValid = IPAddress.TryParse(
                packet.host_ip,
                out IPAddress hostAddress
            );
            bool stationValid = !string.IsNullOrWhiteSpace(packet.station_id);
            bool accepted = packet.version == ProtocolVersion &&
                hostAddressValid && stationValid &&
                packet.http_port > 0 && packet.pose_port > 0;

            string message = "paired";
            if (packet.version != ProtocolVersion)
            {
                message = "protocol_version_mismatch";
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
                pairedStationId = packet.station_id.Trim();
                pairedHostAddress = hostAddress;
                PlayerPrefs.SetString(
                    PairedStationPlayerPrefsKey,
                    pairedStationId
                );
                PlayerPrefs.Save();
                string baseUrl = $"http://{packet.host_ip}:{packet.http_port}";

                motionStreamer.ConfigureDestination(packet.host_ip, packet.pose_port);
                takeUploader.ConfigureHost(baseUrl, deviceId);
                previewStreamer.ConfigureHost(baseUrl, deviceId);
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

            bool accepted;

            switch (packet.action)
            {
                case "start_take":
                    if (string.Equals(
                        packet.command_id,
                        lastStartCommandId,
                        StringComparison.Ordinal))
                    {
                        SendAck(
                            remoteEndPoint,
                            packet.command_id,
                            lastStartAccepted,
                            lastStartMessage,
                            packet.action,
                            lastStartPhase,
                            lastStartTakeId,
                            lastStartActualAtUnixMs
                        );
                        return;
                    }
                    pendingStartCommandId = packet.command_id;
                    pendingStartTakeId = packet.take_id;
                    pendingStartAckTarget = remoteEndPoint;
                    accepted = StartRemoteTake(packet);
                    if (!accepted)
                    {
                        ClearPendingStart();
                    }
                    RememberStartAck(
                        packet.command_id,
                        packet.take_id,
                        accepted,
                        accepted ? "start_scheduled" : "command_rejected",
                        accepted ? "scheduled" : "failed",
                        0L
                    );
                    SendAck(
                        remoteEndPoint,
                        packet.command_id,
                        accepted,
                        accepted ? "start_scheduled" : "command_rejected",
                        packet.action,
                        accepted ? "scheduled" : "failed",
                        packet.take_id
                    );
                    return;
                case "stop_take":
                    accepted = coordinator.StopCurrentTake();
                    if (!accepted && coordinator.State != RecordingFlowState.Countdown &&
                        coordinator.State != RecordingFlowState.Recording)
                    {
                        accepted = true;
                    }
                    break;
                case "reset_take":
                    coordinator.LoadPrompt(
                        packet.session_id,
                        packet.sentence_id,
                        packet.prompt,
                        Mathf.Max(1, packet.take_index)
                    );
                    ApplyPromptContext(packet);
                    coordinator.ResetCurrentPrompt();
                    accepted = true;
                    break;
                case "prompt_context":
                    ApplyPromptContext(packet);
                    accepted = true;
                    break;
                case "pedal":
                    accepted = HandlePedal(packet);
                    break;
                case "heartbeat":
                    accepted = true;
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
                accepted ? packet.action : "command_rejected",
                packet.action,
                accepted ? "completed" : "failed"
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

            ApplyPromptContext(packet);

            var take = new RecordingTakeContext(
                packet.session_id,
                packet.sentence_id,
                packet.prompt,
                packet.take_index,
                packet.take_id,
                DateTimeOffset.FromUnixTimeMilliseconds(packet.start_at_unix_ms).UtcDateTime
            );

            return coordinator.BeginRemoteTake(
                take,
                Mathf.Max(0f, packet.countdown_seconds)
            );
        }

        private void ApplyPromptContext(CommandPacket packet)
        {
            coordinator.ApplyPromptContext(
                packet.previous_prompt,
                packet.prompt,
                packet.next_prompt,
                packet.sentence_index,
                packet.total_sentences,
                packet.signing_mode,
                packet.mode_switch_notice
            );
        }

        private void HandleTakeStartResolved(
            RecordingTakeContext take,
            bool started,
            long actualAtUnixMs,
            string message)
        {
            if (pendingStartAckTarget == null ||
                !string.Equals(take.TakeId, pendingStartTakeId, StringComparison.Ordinal))
            {
                return;
            }

            SendAck(
                pendingStartAckTarget,
                pendingStartCommandId,
                started,
                message,
                "start_take",
                started ? "started" : "failed",
                take.TakeId,
                actualAtUnixMs
            );
            RememberStartAck(
                pendingStartCommandId,
                take.TakeId,
                started,
                message,
                started ? "started" : "failed",
                actualAtUnixMs
            );
            ClearPendingStart();
        }

        private void RememberStartAck(
            string commandId,
            string takeId,
            bool accepted,
            string message,
            string phase,
            long actualAtUnixMs)
        {
            lastStartCommandId = commandId;
            lastStartTakeId = takeId;
            lastStartAccepted = accepted;
            lastStartMessage = message;
            lastStartPhase = phase;
            lastStartActualAtUnixMs = actualAtUnixMs;
        }

        private void MonitorRecordingSafety()
        {
            if (coordinator.State != RecordingFlowState.Recording)
            {
                safetyStopTriggered = false;
                return;
            }

            if (safetyStopTriggered)
            {
                return;
            }

            string reason = coordinator.RecordingElapsedSeconds >= maximumRecordingSeconds
                ? "maximum_recording_duration"
                : string.Empty;

            if (string.IsNullOrEmpty(reason) ||
                !coordinator.StopCurrentTakeAsInterrupted(reason))
            {
                return;
            }

            safetyStopTriggered = true;
            SendRecordingInterrupted(reason);
        }

        private void SendRecordingInterrupted(string reason)
        {
            if (pairedHostAddress == null)
            {
                return;
            }

            var packet = new SignalPacket
            {
                device_id = deviceId,
                station_id = pairedStationId,
                signal = "recording_interrupted",
                active = true,
                message = reason,
                take_id = coordinator.CurrentTake.TakeId,
                actual_at_unix_ms = 0L
            };
            SendPacket(
                packet,
                new IPEndPoint(pairedHostAddress, hostAnnouncementPort)
            );
        }

        private void ClearPendingStart()
        {
            pendingStartCommandId = string.Empty;
            pendingStartTakeId = string.Empty;
            pendingStartAckTarget = null;
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
                paired_station_id = pairedStationId,
                paired = pairedHostAddress != null,
                capabilities = new[] { "pose", "preview", "take_upload" },
                state = coordinator.State.ToString().ToLowerInvariant()
            };

            SendPacket(packet, target);
        }

        private void SendAck(
            IPEndPoint target,
            string commandId,
            bool accepted,
            string message,
            string action = "",
            string phase = "completed",
            string takeId = "",
            long actualAtUnixMs = 0L)
        {
            var packet = new AckPacket
            {
                command_id = commandId,
                device_id = deviceId,
                accepted = accepted,
                state = coordinator.State.ToString().ToLowerInvariant(),
                message = message,
                action = action,
                phase = phase,
                take_id = takeId,
                actual_at_unix_ms = actualAtUnixMs
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
            listener.BeginReceive(ReceiveDatagram, listener);
            sender = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true
            };
        }

        private void OnDisable()
        {
            if (coordinator != null)
            {
                coordinator.TakeStartResolved -= HandleTakeStartResolved;
            }
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
