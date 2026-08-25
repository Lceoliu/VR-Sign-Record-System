using System;
using System.Collections;
using System.Globalization;
using System.Threading;
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

    internal sealed class InteractionHostCompletion<T>
    {
        private readonly InteractionOncePublisher<InteractionHostResult<T>>
            publisher;

        public InteractionHostCompletion(
            Action<InteractionHostResult<T>> callback)
        {
            publisher = new InteractionOncePublisher<InteractionHostResult<T>>(
                callback
            );
        }

        public bool TryComplete(InteractionHostResult<T> result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            return publisher.TryPublish(result);
        }
    }

    internal interface IInteractionHostRequestFactory
    {
        UnityWebRequest CreateGet(string url);
        UnityWebRequest CreateBody(
            string url,
            string method,
            byte[] bytes,
            string contentType);
        UnityWebRequest CreateFileBody(
            string url,
            string method,
            string path,
            string contentType);
    }

    internal sealed class InteractionUnityWebRequestFactory :
        IInteractionHostRequestFactory
    {
        public static InteractionUnityWebRequestFactory Shared { get; } =
            new InteractionUnityWebRequestFactory();

        private InteractionUnityWebRequestFactory()
        {
        }

        public UnityWebRequest CreateGet(string url)
        {
            return UnityWebRequest.Get(url);
        }

        public UnityWebRequest CreateBody(
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

        public UnityWebRequest CreateFileBody(
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
    }

    [DisallowMultipleComponent]
    public sealed partial class InteractionHostClient : MonoBehaviour
    {
        public const string DefaultBaseUrl = "http://192.168.1.100:8011";
        public const string HostUrlArgument = "-interactionHostUrl";
        public const float QuestHeartbeatIntervalSeconds = 2f;
        private const string HeartbeatGenerationPlayerPrefsKey =
            "SignVR.Interaction.QuestHeartbeatGeneration.v1";

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
        private readonly InteractionRequestCancellationRegistry activeRequests =
            new InteractionRequestCancellationRegistry();
        private readonly InteractionArtifactOperationRegistry artifactOperations =
            new InteractionArtifactOperationRegistry();
        private readonly InteractionHostRequestEpoch requestEpoch =
            new InteractionHostRequestEpoch();
        private IInteractionArtifactReadObserver artifactReadObserver =
            InteractionArtifactReadObserver.None;
        private IInteractionHostRequestFactory requestFactory =
            InteractionUnityWebRequestFactory.Shared;
        private InteractionUnityWebRequestCancellation activeHeartbeatRequest;
        private bool requestLifecycleExplicitlySuspended;
        private bool destroyed;
#if UNITY_EDITOR
        private bool editorLifecycleTestsArmed;
#endif

        public string BaseUrl { get; private set; }
        public string QuestDeviceId => questDeviceId;
        public bool HeartbeatRoutineActive => heartbeatRoutine != null &&
            heartbeatLoop.RoutineActive;
        public bool LastHeartbeatReady { get; private set; }
        public string LastHeartbeatError { get; private set; }
        public int ActiveRequestCount => activeRequests.ActiveCount;
        internal int ActiveArtifactOperationCount =>
            artifactOperations.ActiveCount;

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                BaseUrl = NormalizeHttpBaseUrl(hostBaseUrl);
                return;
            }
            ConfigureBaseUrl(ResolveConfiguredBaseUrl(
                hostBaseUrl,
                Environment.GetCommandLineArgs()
            ));
            TryAllocateApplicationHeartbeatGeneration();
        }

        public void ConfigureBaseUrl(string value)
        {
            BaseUrl = NormalizeHttpBaseUrl(value);
            hostBaseUrl = BaseUrl;
        }

        public void ConfigureQuestDeviceId(string value)
        {
            if (Application.isPlaying)
            {
                StopQuestHeartbeat();
            }
            questDeviceId = InteractionStoragePaths.ValidateSegment(
                value,
                nameof(value)
            );
            if (Application.isPlaying)
            {
                StartQuestHeartbeatIfEligible();
            }
        }

        public void ConfigureQuestHeartbeat(bool enabled)
        {
            if (heartbeatEnabled != enabled)
            {
                if (Application.isPlaying)
                {
                    StopQuestHeartbeat();
                }
            }
            heartbeatEnabled = enabled;
            if (!Application.isPlaying)
            {
                return;
            }
            if (heartbeatGeneration < 0L)
            {
                TryAllocateApplicationHeartbeatGeneration();
            }
            StartQuestHeartbeatIfEligible();
        }

        public void DisableQuestHeartbeat()
        {
            heartbeatEnabled = false;
            StopQuestHeartbeat();
        }

        /// <summary>
        /// Explicit lifecycle seam used by the Run controller before stopping
        /// its coroutines. Aborting the owned request also disposes the request
        /// and any UploadHandlerFile immediately.
        /// </summary>
        public int CancelActiveRequests()
        {
            requestLifecycleExplicitlySuspended = true;
            return CancelOwnedOperationsAndAdvanceEpoch();
        }

        private int CancelOwnedOperationsAndAdvanceEpoch()
        {
            requestEpoch.CloseAndAdvance();
            activeHeartbeatRequest = null;
            int artifactCancellations = artifactOperations.CancelAll();
            return checked(activeRequests.CancelAll() + artifactCancellations);
        }

        private void EnableRequestLifecycle()
        {
            if (destroyed)
            {
                return;
            }
            requestLifecycleExplicitlySuspended = false;
            if (!applicationPaused && isActiveAndEnabled)
            {
                requestEpoch.Open();
            }
        }

        internal void RestoreRequestsForPreStart()
        {
            EnableRequestLifecycle();
        }

        private void RestoreComponentRequestLifecycleIfAllowed()
        {
            if (!destroyed && !applicationPaused &&
                !requestLifecycleExplicitlySuspended)
            {
                requestEpoch.Open();
            }
        }

        public IEnumerator GetReadiness(
            Action<InteractionHostResult<InteractionHostReadiness>> callback)
        {
            EnsureCallback(callback);
            var completion = new InteractionHostCompletion<
                InteractionHostReadiness>(callback);
            if (!TryAcquireRequestLease(completion, out InteractionHostRequestLease lease))
            {
                yield break;
            }
            string url = Endpoint("/api/interaction/readiness");
            if (!string.IsNullOrWhiteSpace(questDeviceId))
            {
                url += "?quest_device_id=" +
                    UnityWebRequest.EscapeURL(questDeviceId);
            }
            if (!TryCreateRequest(
                    lease,
                    completion,
                    () => requestFactory.CreateGet(url),
                    requestTimeoutSeconds,
                    null,
                    false,
                    out UnityWebRequest request,
                    out InteractionUnityWebRequestCancellation requestCancellation))
            {
                yield break;
            }
            yield return Send(
                request,
                requestCancellation,
                text => InteractionHostReadiness.Parse(text),
                completion,
                lease
            );
        }

        public IEnumerator RegisterRun(
            string runId,
            byte[] exactManifestBytes,
            Action<InteractionHostResult<InteractionHostRegistration>> callback)
        {
            EnsureCallback(callback);
            var completion = new InteractionHostCompletion<
                InteractionHostRegistration>(callback);
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
            if (!TryAcquireRequestLease(completion, out InteractionHostRequestLease lease) ||
                !TryCreateRequest(
                    lease,
                    completion,
                    () => requestFactory.CreateBody(
                        Endpoint("/api/interaction/runs"),
                        UnityWebRequest.kHttpVerbPOST,
                        exactManifestBytes,
                        "application/json"
                    ),
                    requestTimeoutSeconds,
                    null,
                    false,
                    out UnityWebRequest request,
                    out InteractionUnityWebRequestCancellation requestCancellation))
            {
                yield break;
            }
            yield return Send(
                request,
                requestCancellation,
                text => InteractionHostRegistration.Parse(
                    text,
                    expectedRunId
                ),
                completion,
                lease
            );
        }

        public IEnumerator GetRunSnapshot(
            string runId,
            Action<InteractionHostResult<InteractionHostRunSnapshot>> callback)
        {
            EnsureCallback(callback);
            var completion = new InteractionHostCompletion<
                InteractionHostRunSnapshot>(callback);
            string safeRunId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            string url = Endpoint(
                "/api/interaction/runs/" +
                UnityWebRequest.EscapeURL(safeRunId)
            );
            if (!TryAcquireRequestLease(completion, out InteractionHostRequestLease lease) ||
                !TryCreateRequest(
                    lease,
                    completion,
                    () => requestFactory.CreateGet(url),
                    requestTimeoutSeconds,
                    null,
                    false,
                    out UnityWebRequest request,
                    out InteractionUnityWebRequestCancellation requestCancellation))
            {
                yield break;
            }
            yield return Send(
                request,
                requestCancellation,
                text => InteractionHostRunSnapshot.Parse(text, safeRunId),
                completion,
                lease
            );
        }

        public IEnumerator PutQuestHeartbeat(
            bool ready,
            long generation,
            long sequence,
            Action<InteractionHostResult<bool>> callback)
        {
            EnsureCallback(callback);
            var completion = new InteractionHostCompletion<bool>(callback);
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
            if (!TryAcquireRequestLease(completion, out InteractionHostRequestLease lease) ||
                !TryCreateRequest(
                    lease,
                    completion,
                    () => requestFactory.CreateBody(
                        Endpoint(InteractionHostContractV1.QuestHeartbeatPath),
                        UnityWebRequest.kHttpVerbPUT,
                        bytes,
                        "application/json"
                    ),
                    requestTimeoutSeconds,
                    null,
                    true,
                    out UnityWebRequest request,
                    out InteractionUnityWebRequestCancellation requestCancellation))
            {
                yield break;
            }
            yield return Send(
                request,
                requestCancellation,
                text => InteractionQuestHeartbeatAck.Parse(
                    text,
                    questDeviceId,
                    generation,
                    sequence
                ).Accepted,
                completion,
                lease,
                isHeartbeat: true
            );
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
            var completion = new InteractionHostCompletion<bool>(callback);
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
            if (!TryAcquireRequestLease(completion, out InteractionHostRequestLease lease))
            {
                yield break;
            }
            InteractionArtifactOperation<bool> verification = null;
            bool verificationStarted = false;
            if (!requestEpoch.TryExecute(lease, () =>
                    verificationStarted = TryBeginArtifactVerification(
                        artifact,
                        out verification
                    )))
            {
                completion.TryComplete(LifecycleFailure<bool>());
                yield break;
            }
            if (!verificationStarted)
            {
                completion.TryComplete(new InteractionHostResult<bool>(
                    false,
                    0L,
                    false,
                    "Frozen artifact verification is already active for this artifact.",
                    null
                ));
                yield break;
            }
            while (!verification.IsCompleted)
            {
                yield return null;
            }
            if (!requestEpoch.IsCurrent(lease))
            {
                completion.TryComplete(LifecycleFailure<bool>());
                yield break;
            }
            if (!verification.Succeeded)
            {
                completion.TryComplete(new InteractionHostResult<bool>(
                    false,
                    0L,
                    false,
                    "Frozen artifact verification failed: " +
                        (verification.Error == null
                            ? "unknown background failure."
                            : verification.Error.Message),
                    null
                ));
                yield break;
            }
            if (!TryCreateRequest(
                    lease,
                    completion,
                    () => requestFactory.CreateFileBody(
                        url,
                        UnityWebRequest.kHttpVerbPUT,
                        artifact.Path,
                        InteractionArtifactTypes.ContentTypeFor(type)
                    ),
                    uploadTimeoutSeconds,
                    request => request.SetRequestHeader(
                        "X-Content-SHA256",
                        artifact.Sha256
                    ),
                    false,
                    out UnityWebRequest request,
                    out InteractionUnityWebRequestCancellation requestCancellation))
            {
                yield break;
            }
            yield return Send(
                request,
                requestCancellation,
                text => true,
                completion,
                lease,
                allowEmptySuccessBody: true
            );
        }

        private bool TryBeginArtifactVerification(
            InteractionFrozenArtifact artifact,
            out InteractionArtifactOperation<bool> operation)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            InteractionFrozenArtifact artifactSnapshot = artifact;
            IInteractionArtifactReadObserver observerSnapshot =
                artifactReadObserver;
            string key = InteractionArtifactOperationKeys.ForVerify(
                artifactSnapshot
            );
            return artifactOperations.TryStart(
                key,
                cancellation => artifactSnapshot.VerifyUnchangedOnWorker(
                    cancellation,
                    observerSnapshot
                ),
                out operation
            );
        }

        public IEnumerator GetAck(
            string runId,
            Action<InteractionHostResult<InteractionHostArtifactAck>> callback)
        {
            EnsureCallback(callback);
            var completion = new InteractionHostCompletion<
                InteractionHostArtifactAck>(callback);
            string safeRunId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            string url = Endpoint(
                "/api/interaction/runs/" +
                UnityWebRequest.EscapeURL(safeRunId) + "/ack"
            );
            if (!TryAcquireRequestLease(completion, out InteractionHostRequestLease lease) ||
                !TryCreateRequest(
                    lease,
                    completion,
                    () => requestFactory.CreateGet(url),
                    requestTimeoutSeconds,
                    null,
                    false,
                    out UnityWebRequest request,
                    out InteractionUnityWebRequestCancellation requestCancellation))
            {
                yield break;
            }
            yield return Send(
                request,
                requestCancellation,
                text => InteractionHostArtifactAck.Parse(text, safeRunId),
                completion,
                lease
            );
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
            var completion = new InteractionHostCompletion<bool>(callback);
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
            if (!TryAcquireRequestLease(completion, out InteractionHostRequestLease lease) ||
                !TryCreateRequest(
                    lease,
                    completion,
                    () => requestFactory.CreateBody(
                        url,
                        UnityWebRequest.kHttpVerbPOST,
                        bytes,
                        "application/json"
                    ),
                    requestTimeoutSeconds,
                    null,
                    false,
                    out UnityWebRequest request,
                    out InteractionUnityWebRequestCancellation requestCancellation))
            {
                yield break;
            }
            yield return Send(
                request,
                requestCancellation,
                text => true,
                completion,
                lease,
                allowEmptySuccessBody: true
            );
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
            InteractionUnityWebRequestCancellation cancellation,
            Func<string, T> parseSuccess,
            InteractionHostCompletion<T> completion,
            InteractionHostRequestLease lease,
            bool allowEmptySuccessBody = false,
            bool isHeartbeat = false)
        {
            if (request == null || cancellation == null)
            {
                throw new ArgumentNullException(
                    request == null ? nameof(request) : nameof(cancellation)
                );
            }
            try
            {
                if (!requestEpoch.IsCurrent(lease))
                {
                    completion.TryComplete(LifecycleFailure<T>());
                    yield break;
                }
                UnityWebRequestAsyncOperation operation = null;
                try
                {
                    bool sent = requestEpoch.TryExecute(lease, () =>
                    {
                        if (!cancellation.IsCancelled)
                        {
                            operation = request.SendWebRequest();
                        }
                    });
                    if (!sent || operation == null)
                    {
                        completion.TryComplete(LifecycleFailure<T>());
                        yield break;
                    }
                }
                catch (Exception exception)
                {
                    CompleteIfCurrent(
                        lease,
                        completion,
                        new InteractionHostResult<T>(
                        false,
                        0L,
                        default(T),
                        "Request could not be sent: " + exception.Message,
                        null
                        )
                    );
                    yield break;
                }
                yield return operation;

                if (cancellation.IsCancelled ||
                    !requestEpoch.IsCurrent(lease))
                {
                    completion.TryComplete(LifecycleFailure<T>());
                    yield break;
                }

                string responseText = request.downloadHandler == null
                    ? string.Empty
                    : request.downloadHandler.text;
                bool httpSuccess = request.responseCode >= 200L &&
                    request.responseCode <= 299L &&
                    request.result == UnityWebRequest.Result.Success;
                if (!httpSuccess)
                {
                    CompleteIfCurrent(
                        lease,
                        completion,
                        new InteractionHostResult<T>(
                            false,
                            request.responseCode,
                            default(T),
                            string.IsNullOrWhiteSpace(request.error)
                                ? "Host rejected the request."
                                : request.error,
                            responseText
                        )
                    );
                    yield break;
                }

                try
                {
                    T value = allowEmptySuccessBody &&
                        string.IsNullOrWhiteSpace(responseText)
                        ? parseSuccess(string.Empty)
                        : parseSuccess(responseText);
                    CompleteIfCurrent(
                        lease,
                        completion,
                        new InteractionHostResult<T>(
                            true,
                            request.responseCode,
                            value,
                            null,
                            responseText
                        )
                    );
                }
                catch (Exception exception) when (
                    exception is FormatException ||
                    exception is ArgumentException ||
                    exception is InvalidOperationException)
                {
                    CompleteIfCurrent(
                        lease,
                        completion,
                        new InteractionHostResult<T>(
                            false,
                            request.responseCode,
                            default(T),
                            "Host response violated Interaction Contract V1: " +
                                exception.Message,
                            responseText
                        )
                    );
                }
            }
            finally
            {
                if (ReferenceEquals(activeHeartbeatRequest, cancellation))
                {
                    activeHeartbeatRequest = null;
                }
                activeRequests.Unregister(cancellation);
                cancellation.Dispose();
            }
        }

        private bool TryAcquireRequestLease<T>(
            InteractionHostCompletion<T> completion,
            out InteractionHostRequestLease lease)
        {
            if (requestEpoch.TryAcquire(out lease))
            {
                return true;
            }
            completion.TryComplete(LifecycleFailure<T>());
            return false;
        }

        private bool TryCreateRequest<T>(
            InteractionHostRequestLease lease,
            InteractionHostCompletion<T> completion,
            Func<UnityWebRequest> create,
            int timeout,
            Action<UnityWebRequest> configure,
            bool isHeartbeat,
            out UnityWebRequest request,
            out InteractionUnityWebRequestCancellation cancellation)
        {
            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }
            request = null;
            cancellation = null;
            UnityWebRequest createdRequest = null;
            InteractionUnityWebRequestCancellation createdCancellation = null;
            Exception creationFailure = null;
            bool current = requestEpoch.TryExecute(lease, () =>
            {
                try
                {
                    createdRequest = create();
                    if (createdRequest == null)
                    {
                        throw new InvalidOperationException(
                            "Host request factory returned no request."
                        );
                    }
                    ConfigureRequest(createdRequest, timeout);
                    configure?.Invoke(createdRequest);
                    createdCancellation =
                        new InteractionUnityWebRequestCancellation(
                            createdRequest
                        );
                    activeRequests.Register(createdCancellation);
                    if (isHeartbeat)
                    {
                        activeHeartbeatRequest = createdCancellation;
                    }
                }
                catch (Exception exception)
                {
                    creationFailure = exception;
                }
            });
            if (!current)
            {
                completion.TryComplete(LifecycleFailure<T>());
                return false;
            }
            if (creationFailure == null)
            {
                request = createdRequest;
                cancellation = createdCancellation;
                return true;
            }
            if (createdCancellation != null)
            {
                activeRequests.Unregister(createdCancellation);
                if (ReferenceEquals(
                        activeHeartbeatRequest,
                        createdCancellation))
                {
                    activeHeartbeatRequest = null;
                }
                createdCancellation.Dispose();
            }
            else
            {
                createdRequest?.Dispose();
            }
            completion.TryComplete(new InteractionHostResult<T>(
                false,
                0L,
                default(T),
                "Request could not be created: " + creationFailure.Message,
                null
            ));
            return false;
        }

        private void CompleteIfCurrent<T>(
            InteractionHostRequestLease lease,
            InteractionHostCompletion<T> completion,
            InteractionHostResult<T> result)
        {
            if (!requestEpoch.TryExecute(
                    lease,
                    () => completion.TryComplete(result)))
            {
                completion.TryComplete(LifecycleFailure<T>());
            }
        }

        private static InteractionHostResult<T> LifecycleFailure<T>()
        {
            return new InteractionHostResult<T>(
                false,
                0L,
                default(T),
                "Request was cancelled by Interaction lifecycle shutdown.",
                null
            );
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

        private void TryAllocateApplicationHeartbeatGeneration()
        {
            if (!Application.isPlaying || heartbeatGeneration >= 0L)
            {
                return;
            }
            try
            {
                heartbeatGeneration =
                    InteractionHeartbeatGenerationAllocator.AllocateNext(
                        new InteractionPlayerPrefsHeartbeatGenerationStore(
                            HeartbeatGenerationPlayerPrefsKey
                        )
                    );
                LastHeartbeatError = null;
            }
            catch (Exception exception) when (
                exception is FormatException ||
                exception is OverflowException ||
                exception is InvalidOperationException ||
                exception is UnityException)
            {
                heartbeatGeneration = -1L;
                LastHeartbeatReady = false;
                LastHeartbeatError =
                    "Heartbeat generation could not be persisted: " +
                    exception.Message;
            }
        }

        private void StartQuestHeartbeatIfEligible()
        {
            if (!Application.isPlaying || !heartbeatEnabled || applicationPaused ||
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
            LastHeartbeatReady = false;
            InteractionUnityWebRequestCancellation heartbeatRequest =
                activeHeartbeatRequest;
            activeHeartbeatRequest = null;
            if (heartbeatRequest != null)
            {
                activeRequests.Unregister(heartbeatRequest);
                heartbeatRequest.Abort();
            }
            if (heartbeatRoutine != null)
            {
                StopCoroutine(heartbeatRoutine);
                heartbeatRoutine = null;
            }
        }

        private IEnumerator QuestHeartbeatRoutine()
        {
            var schedule = new InteractionHeartbeatDeadlineSchedule(
                QuestHeartbeatIntervalSeconds
            );
            schedule.Reset(Time.realtimeSinceStartupAsDouble);
            while (heartbeatEnabled && !applicationPaused &&
                isActiveAndEnabled && heartbeatLoop.RoutineActive)
            {
                double delaySeconds = schedule.DelaySeconds(
                    Time.realtimeSinceStartupAsDouble
                );
                if (delaySeconds > 0d)
                {
                    yield return new WaitForSecondsRealtime(
                        (float)delaySeconds
                    );
                }
                if (!heartbeatEnabled || applicationPaused ||
                    !isActiveAndEnabled || !heartbeatLoop.RoutineActive)
                {
                    break;
                }
                long sequence = heartbeatLoop.NextSequence();
                InteractionHostResult<bool> result = null;
                yield return PutQuestHeartbeat(
                    true,
                    heartbeatGeneration,
                    sequence,
                    value => result = value
                );
                LastHeartbeatReady = result != null && result.Success &&
                    result.Value;
                LastHeartbeatError = LastHeartbeatReady
                    ? null
                    : result == null
                        ? "Quest heartbeat produced no result."
                        : result.Error;
                schedule.AdvanceAfterAttempt(
                    Time.realtimeSinceStartupAsDouble
                );
                if (!heartbeatEnabled || applicationPaused ||
                    !isActiveAndEnabled || !heartbeatLoop.RoutineActive)
                {
                    break;
                }
            }
            heartbeatLoop.Stop();
            heartbeatRoutine = null;
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                RestoreComponentRequestLifecycleIfAllowed();
                StartQuestHeartbeatIfEligible();
            }
#if UNITY_EDITOR
            else if (editorLifecycleTestsArmed)
            {
                RestoreComponentRequestLifecycleIfAllowed();
            }
#endif
        }

        private void OnDisable()
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            StopQuestHeartbeat();
            CancelOwnedOperationsAndAdvanceEpoch();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            applicationPaused = paused;
            if (paused)
            {
                StopQuestHeartbeat();
                CancelOwnedOperationsAndAdvanceEpoch();
            }
            else
            {
                RestoreComponentRequestLifecycleIfAllowed();
                StartQuestHeartbeatIfEligible();
            }
        }

        private void OnValidate()
        {
            requestTimeoutSeconds = Mathf.Max(3, requestTimeoutSeconds);
            uploadTimeoutSeconds = Mathf.Max(15, uploadTimeoutSeconds);
        }

        private void OnDestroy()
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            StopQuestHeartbeat();
            destroyed = true;
            CancelOwnedOperationsAndAdvanceEpoch();
        }

        private bool ShouldProcessUnityLifecycle()
        {
            if (Application.isPlaying)
            {
                return true;
            }
#if UNITY_EDITOR
            return editorLifecycleTestsArmed;
#else
            return false;
#endif
        }
    }

    internal sealed class InteractionPlayerPrefsHeartbeatGenerationStore :
        IInteractionHeartbeatGenerationStore
    {
        private readonly string key;

        public InteractionPlayerPrefsHeartbeatGenerationStore(string key)
        {
            this.key = string.IsNullOrWhiteSpace(key)
                ? throw new ArgumentException(
                    "Heartbeat generation PlayerPrefs key is required.",
                    nameof(key)
                )
                : key;
        }

        public bool TryRead(out long generation)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                generation = -1L;
                return false;
            }
            string serialized = PlayerPrefs.GetString(key, string.Empty);
            if (!long.TryParse(
                    serialized,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out generation) || generation < 0L)
            {
                throw new FormatException(
                    "Persisted heartbeat generation is not a non-negative Int64."
                );
            }
            return true;
        }

        public void WriteAndFlush(long generation)
        {
            if (generation < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }
            PlayerPrefs.SetString(
                key,
                generation.ToString(CultureInfo.InvariantCulture)
            );
            PlayerPrefs.Save();
        }
    }

    internal sealed class InteractionUnityWebRequestCancellation :
        IInteractionCancelableRequest,
        IDisposable
    {
        private UnityWebRequest request;
        private int cancelled;
        private int disposed;

        public InteractionUnityWebRequestCancellation(UnityWebRequest request)
        {
            this.request = request ??
                throw new ArgumentNullException(nameof(request));
        }

        public bool IsCancelled => Volatile.Read(ref cancelled) != 0;

        public void Abort()
        {
            if (Interlocked.Exchange(ref cancelled, 1) != 0)
            {
                return;
            }
            UnityWebRequest owned = request;
            try
            {
                owned?.Abort();
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            UnityWebRequest owned = request;
            request = null;
            owned?.Dispose();
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
