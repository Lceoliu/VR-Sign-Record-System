using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace SignVR.Interaction.CaptureHost
{
    public sealed class InteractionHostResult<T>
    {
        internal InteractionHostResult(
            bool success,
            long responseCode,
            T value,
            string error,
            string responseText)
        {
            Success = success;
            ResponseCode = responseCode;
            Value = value;
            Error = error;
            ResponseText = responseText;
        }

        public bool Success { get; }
        public long ResponseCode { get; }
        public T Value { get; }
        public string Error { get; }
        public string ResponseText { get; }
        public bool IsConflict => ResponseCode == 409L;
    }

    [DisallowMultipleComponent]
    public sealed class InteractionHostClient : MonoBehaviour
    {
        public const string DefaultBaseUrl = "http://192.168.1.100:8011";
        public const string HostUrlArgument = "-interactionHostUrl";
        public const float QuestHeartbeatIntervalSeconds = 2f;

        [SerializeField]
        private string hostBaseUrl = DefaultBaseUrl;

        [SerializeField]
        [Min(3)]
        private int requestTimeoutSeconds = 15;

        [SerializeField]
        [Min(15)]
        private int uploadTimeoutSeconds = 120;

        private string questDeviceId;
        private long heartbeatGeneration = -1L;
        private bool heartbeatEnabled;
        private bool applicationPaused;
        private Coroutine heartbeatRoutine;
        private readonly InteractionHeartbeatLoopState heartbeatLoop =
            new InteractionHeartbeatLoopState();

        public string BaseUrl { get; private set; }
        public string QuestDeviceId => questDeviceId;
        public bool HeartbeatRoutineActive => heartbeatRoutine != null &&
            heartbeatLoop.RoutineActive;
        public string LastHeartbeatError { get; private set; }

        private void Awake()
        {
            heartbeatGeneration = Math.Max(
                0L,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
            ConfigureBaseUrl(ResolveConfiguredBaseUrl(
                hostBaseUrl,
                Environment.GetCommandLineArgs()
            ));
        }

        public void ConfigureBaseUrl(string value)
        {
            BaseUrl = NormalizeHttpBaseUrl(value);
            hostBaseUrl = BaseUrl;
        }

        public void ConfigureQuestDeviceId(string value)
        {
            StopQuestHeartbeat();
            questDeviceId = InteractionStoragePaths.ValidateSegment(
                value,
                nameof(value)
            );
            StartQuestHeartbeatIfEligible();
        }

        public void ConfigureQuestHeartbeat(bool enabled)
        {
            if (heartbeatEnabled != enabled)
            {
                StopQuestHeartbeat();
            }
            heartbeatEnabled = enabled;
            if (heartbeatGeneration < 0L)
            {
                heartbeatGeneration = Math.Max(
                    0L,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );
            }
            StartQuestHeartbeatIfEligible();
        }

        public void DisableQuestHeartbeat()
        {
            heartbeatEnabled = false;
            StopQuestHeartbeat();
        }

        public IEnumerator GetReadiness(
            Action<InteractionHostResult<InteractionHostReadiness>> callback)
        {
            EnsureCallback(callback);
            string url = Endpoint("/api/interaction/readiness");
            if (!string.IsNullOrWhiteSpace(questDeviceId))
            {
                url += "?quest_device_id=" +
                    UnityWebRequest.EscapeURL(questDeviceId);
            }
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                ConfigureRequest(request, requestTimeoutSeconds);
                yield return Send(
                    request,
                    text => InteractionHostReadiness.Parse(text),
                    callback
                );
            }
        }

        public IEnumerator RegisterRun(
            string runId,
            byte[] exactManifestBytes,
            Action<InteractionHostResult<InteractionHostRegistration>> callback)
        {
            EnsureCallback(callback);
            string expectedRunId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            if (exactManifestBytes == null || exactManifestBytes.Length == 0)
            {
                throw new ArgumentException(
                    "Manifest bytes are required.",
                    nameof(exactManifestBytes)
                );
            }
            using (UnityWebRequest request = CreateBodyRequest(
                Endpoint("/api/interaction/runs"),
                UnityWebRequest.kHttpVerbPOST,
                exactManifestBytes,
                "application/json"
            ))
            {
                ConfigureRequest(request, requestTimeoutSeconds);
                yield return Send(
                    request,
                    text => InteractionHostRegistration.Parse(
                        text,
                        expectedRunId
                    ),
                    callback
                );
            }
        }

        public IEnumerator GetRunSnapshot(
            string runId,
            Action<InteractionHostResult<InteractionHostRunSnapshot>> callback)
        {
            EnsureCallback(callback);
            string safeRunId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            string url = Endpoint(
                "/api/interaction/runs/" +
                UnityWebRequest.EscapeURL(safeRunId)
            );
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                ConfigureRequest(request, requestTimeoutSeconds);
                yield return Send(
                    request,
                    text => InteractionHostRunSnapshot.Parse(text, safeRunId),
                    callback
                );
            }
        }

        public IEnumerator PutQuestHeartbeat(
            bool ready,
            long generation,
            long sequence,
            Action<InteractionHostResult<bool>> callback)
        {
            EnsureCallback(callback);
            if (string.IsNullOrWhiteSpace(questDeviceId))
            {
                throw new InvalidOperationException(
                    "Quest device identity must be configured before heartbeat."
                );
            }
            byte[] bytes = InteractionHostContractV1
                .BuildQuestHeartbeatBodyUtf8(
                    questDeviceId,
                    ready,
                    generation,
                    sequence
                );
            using (UnityWebRequest request = CreateBodyRequest(
                Endpoint(InteractionHostContractV1.QuestHeartbeatPath),
                UnityWebRequest.kHttpVerbPUT,
                bytes,
                "application/json"
            ))
            {
                ConfigureRequest(request, requestTimeoutSeconds);
                yield return Send(
                    request,
                    text => true,
                    callback,
                    allowEmptySuccessBody: true
                );
            }
        }

        public IEnumerator NotifyComplete(
            string runId,
            DateTimeOffset completedUtc,
            Action<InteractionHostResult<bool>> callback)
        {
            return NotifyTerminal(
                runId,
                "complete",
                "completed_utc",
                completedUtc,
                null,
                callback
            );
        }

        public IEnumerator NotifyAbort(
            string runId,
            DateTimeOffset abortedUtc,
            string reason,
            Action<InteractionHostResult<bool>> callback)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException(
                    "Abort reason is required.",
                    nameof(reason)
                );
            }
            return NotifyTerminal(
                runId,
                "abort",
                "aborted_utc",
                abortedUtc,
                reason.Trim(),
                callback
            );
        }

        public IEnumerator PutArtifact(
            string runId,
            InteractionFrozenArtifact artifact,
            Action<InteractionHostResult<bool>> callback)
        {
            EnsureCallback(callback);
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            string safeRunId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            string type = InteractionArtifactTypes.Validate(
                artifact.ArtifactType,
                false
            );
            string url = Endpoint(
                "/api/interaction/runs/" +
                UnityWebRequest.EscapeURL(safeRunId) +
                "/artifacts/" + UnityWebRequest.EscapeURL(type)
            );
            artifact.VerifyUnchanged();
            using (UnityWebRequest request = CreateFileBodyRequest(
                url,
                UnityWebRequest.kHttpVerbPUT,
                artifact.Path,
                InteractionArtifactTypes.ContentTypeFor(type)
            ))
            {
                request.SetRequestHeader(
                    "X-Content-SHA256",
                    artifact.Sha256
                );
                ConfigureRequest(request, uploadTimeoutSeconds);
                yield return Send(
                    request,
                    text => true,
                    callback,
                    allowEmptySuccessBody: true
                );
            }
        }

        public IEnumerator GetAck(
            string runId,
            Action<InteractionHostResult<InteractionHostArtifactAck>> callback)
        {
            EnsureCallback(callback);
            string safeRunId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            string url = Endpoint(
                "/api/interaction/runs/" +
                UnityWebRequest.EscapeURL(safeRunId) + "/ack"
            );
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                ConfigureRequest(request, requestTimeoutSeconds);
                yield return Send(
                    request,
                    text => InteractionHostArtifactAck.Parse(text, safeRunId),
                    callback
                );
            }
        }

        public static string ResolveConfiguredBaseUrl(
            string inspectorValue,
            string[] commandLineArguments)
        {
            string selected = inspectorValue;
            if (commandLineArguments != null)
            {
                for (int index = 0; index < commandLineArguments.Length; index++)
                {
                    string argument = commandLineArguments[index];
                    if (string.Equals(
                            argument,
                            HostUrlArgument,
                            StringComparison.OrdinalIgnoreCase) &&
                        index + 1 < commandLineArguments.Length)
                    {
                        selected = commandLineArguments[index + 1];
                        break;
                    }
                    string prefix = HostUrlArgument + "=";
                    if (argument != null && argument.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        selected = argument.Substring(prefix.Length);
                        break;
                    }
                }
            }
            return NormalizeHttpBaseUrl(selected);
        }

        public static string NormalizeHttpBaseUrl(string value)
        {
            return InteractionHostContractV1.NormalizeHttpBaseUrl(value);
        }

        private IEnumerator NotifyTerminal(
            string runId,
            string route,
            string utcProperty,
            DateTimeOffset utcTime,
            string abortReason,
            Action<InteractionHostResult<bool>> callback)
        {
            EnsureCallback(callback);
            string safeRunId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            byte[] bytes = BuildTerminalBodyUtf8(
                safeRunId,
                utcProperty,
                utcTime,
                abortReason
            );
            string url = Endpoint(
                "/api/interaction/runs/" +
                UnityWebRequest.EscapeURL(safeRunId) + "/" + route
            );
            using (UnityWebRequest request = CreateBodyRequest(
                url,
                UnityWebRequest.kHttpVerbPOST,
                bytes,
                "application/json"
            ))
            {
                ConfigureRequest(request, requestTimeoutSeconds);
                yield return Send(
                    request,
                    text => true,
                    callback,
                    allowEmptySuccessBody: true
                );
            }
        }

        internal static byte[] BuildTerminalBodyUtf8(
            string runId,
            string utcProperty,
            DateTimeOffset utcTime,
            string abortReason)
        {
            return InteractionHostContractV1.BuildTerminalBodyUtf8(
                runId,
                utcProperty,
                utcTime,
                abortReason
            );
        }

        private IEnumerator Send<T>(
            UnityWebRequest request,
            Func<string, T> parseSuccess,
            Action<InteractionHostResult<T>> callback,
            bool allowEmptySuccessBody = false)
        {
            UnityWebRequestAsyncOperation operation;
            try
            {
                operation = request.SendWebRequest();
            }
            catch (Exception exception)
            {
                callback(new InteractionHostResult<T>(
                    false,
                    0L,
                    default(T),
                    "Request could not be sent: " + exception.Message,
                    null
                ));
                yield break;
            }
            yield return operation;

            string responseText = request.downloadHandler == null
                ? string.Empty
                : request.downloadHandler.text;
            bool httpSuccess = request.responseCode >= 200L &&
                request.responseCode <= 299L &&
                request.result == UnityWebRequest.Result.Success;
            if (!httpSuccess)
            {
                callback(new InteractionHostResult<T>(
                    false,
                    request.responseCode,
                    default(T),
                    string.IsNullOrWhiteSpace(request.error)
                        ? "Host rejected the request."
                        : request.error,
                    responseText
                ));
                yield break;
            }

            try
            {
                T value = allowEmptySuccessBody &&
                    string.IsNullOrWhiteSpace(responseText)
                    ? parseSuccess(string.Empty)
                    : parseSuccess(responseText);
                callback(new InteractionHostResult<T>(
                    true,
                    request.responseCode,
                    value,
                    null,
                    responseText
                ));
            }
            catch (Exception exception) when (
                exception is FormatException ||
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                callback(new InteractionHostResult<T>(
                    false,
                    request.responseCode,
                    default(T),
                    "Host response violated Interaction Contract V1: " +
                        exception.Message,
                    responseText
                ));
            }
        }

        private void ConfigureRequest(UnityWebRequest request, int timeout)
        {
            request.timeout = Mathf.Max(1, timeout);
            if (!string.IsNullOrWhiteSpace(questDeviceId))
            {
                request.SetRequestHeader(
                    "X-SignVR-Quest-Id",
                    questDeviceId
                );
            }
        }

        private static UnityWebRequest CreateBodyRequest(
            string url,
            string method,
            byte[] bytes,
            string contentType)
        {
            return new UnityWebRequest(url, method)
            {
                uploadHandler = new UploadHandlerRaw(bytes),
                downloadHandler = new DownloadHandlerBuffer(),
                disposeUploadHandlerOnDispose = true,
                disposeDownloadHandlerOnDispose = true
            }.WithContentType(contentType);
        }

        private static UnityWebRequest CreateFileBodyRequest(
            string url,
            string method,
            string path,
            string contentType)
        {
            return new UnityWebRequest(url, method)
            {
                uploadHandler = new UploadHandlerFile(path),
                downloadHandler = new DownloadHandlerBuffer(),
                disposeUploadHandlerOnDispose = true,
                disposeDownloadHandlerOnDispose = true
            }.WithContentType(contentType);
        }

        private string Endpoint(string path)
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                ConfigureBaseUrl(hostBaseUrl);
            }
            return BaseUrl + path;
        }

        private static void EnsureCallback<T>(Action<T> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
        }

        private void StartQuestHeartbeatIfEligible()
        {
            if (!heartbeatEnabled || applicationPaused ||
                !isActiveAndEnabled || heartbeatRoutine != null ||
                string.IsNullOrWhiteSpace(questDeviceId) ||
                heartbeatGeneration < 0L ||
                !heartbeatLoop.TryStart(heartbeatGeneration))
            {
                return;
            }
            heartbeatRoutine = StartCoroutine(QuestHeartbeatRoutine());
        }

        private void StopQuestHeartbeat()
        {
            heartbeatLoop.Stop();
            if (heartbeatRoutine != null)
            {
                StopCoroutine(heartbeatRoutine);
                heartbeatRoutine = null;
            }
        }

        private IEnumerator QuestHeartbeatRoutine()
        {
            while (heartbeatEnabled && !applicationPaused &&
                isActiveAndEnabled && heartbeatLoop.RoutineActive)
            {
                long sequence = heartbeatLoop.NextSequence();
                InteractionHostResult<bool> result = null;
                yield return PutQuestHeartbeat(
                    true,
                    heartbeatGeneration,
                    sequence,
                    value => result = value
                );
                LastHeartbeatError = result != null && result.Success
                    ? null
                    : result == null
                        ? "Quest heartbeat produced no result."
                        : result.Error;
                if (!heartbeatEnabled || applicationPaused ||
                    !isActiveAndEnabled || !heartbeatLoop.RoutineActive)
                {
                    break;
                }
                yield return new WaitForSecondsRealtime(
                    QuestHeartbeatIntervalSeconds
                );
            }
            heartbeatLoop.Stop();
            heartbeatRoutine = null;
        }

        private void OnEnable()
        {
            StartQuestHeartbeatIfEligible();
        }

        private void OnDisable()
        {
            StopQuestHeartbeat();
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused)
            {
                StopQuestHeartbeat();
            }
            else
            {
                StartQuestHeartbeatIfEligible();
            }
        }

        private void OnValidate()
        {
            requestTimeoutSeconds = Mathf.Max(3, requestTimeoutSeconds);
            uploadTimeoutSeconds = Mathf.Max(15, uploadTimeoutSeconds);
        }
    }

    internal static class InteractionUnityWebRequestExtensions
    {
        public static UnityWebRequest WithContentType(
            this UnityWebRequest request,
            string contentType)
        {
            request.SetRequestHeader("Content-Type", contentType);
            return request;
        }
    }
}
