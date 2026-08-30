using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Meta.XR.Movement;
using Meta.XR.Movement.Retargeting;
using SignVR.Interaction.Core;
using SignVR.Interaction.Diagnostics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Loads one exact Run Plan Pose artifact and plays it on an independent
    /// signer rig. It has no RecordingCoordinator, LastArtifact, or Take scan.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-250)]
    public sealed class InstructionGhostPlayer : MonoBehaviour
    {
        private const double RetargeterReadinessTimeoutSeconds = 5d;

        private enum RetargeterValidationResult
        {
            Ready,
            Waiting,
            Invalid,
        }

        [Serializable]
        private struct Vector3Record
        {
            public float x;
            public float y;
            public float z;

            public Vector3 ToVector3()
            {
                return new Vector3(x, y, z);
            }
        }

        [Serializable]
        private struct QuaternionRecord
        {
            public float x;
            public float y;
            public float z;
            public float w;

            public Quaternion ToQuaternion()
            {
                return new Quaternion(x, y, z, w);
            }
        }

        [Serializable]
        private sealed class RecordedFrame
        {
            public double recording_time;
            public bool pose_valid;
            public int joint_count;
            public Vector3Record[] positions;
            public QuaternionRecord[] rotations;
            public Vector3Record[] scales;
        }

        [Header("Independent signer rig")]
        [SerializeField]
        private CharacterRetargeter retargeter;

        [Header("Staged content")]
        [SerializeField]
        [Tooltip("Subdirectory below StreamingAssets containing W2's staged root.")]
        private string streamingAssetsSubdirectory = "InstructionContent";

        [SerializeField]
        [Tooltip("Optional development-only absolute directory or URL override.")]
        private string contentRootOverride = string.Empty;

        [Header("Playback")]
        [SerializeField]
        [Range(0.25f, 2f)]
        private float playbackSpeed = 1f;

        private readonly List<RecordedFrame> frames = new(512);
        private readonly InstructionGhostPlaybackState playbackState = new();
        private NativeArray<MSDKUtility.NativeTransform> poseBuffer;
        private Coroutine loadRoutine;
        private InstructionContentReference loadedContent;
        private Renderer[] signerRenderers = Array.Empty<Renderer>();
        private int frameIndex;
        private int loadGeneration;
        private double playbackStartedAt;

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        private int retargeterReadinessFailuresRemainingForTests;
#endif

        public event Action StateChanged;
        public event Action<InstructionContentReference> Loaded;
        public event Action<InstructionPlaybackPass> PlaybackStarted;
        public event Action<InstructionPlaybackPass> Completed;
        public event Action<string> Failed;
        public event Action Stopped;

        public CharacterRetargeter Retargeter => retargeter;

        public InstructionContentReference LoadedContent => loadedContent;

        public InstructionGhostPlaybackStatus Status => playbackState.Status;

        public InstructionPlaybackPass CurrentPass => playbackState.CurrentPass;

        public bool IsLoading { get; private set; }

        public bool IsLoaded =>
            loadedContent != null &&
            playbackState.Status == InstructionGhostPlaybackStatus.Loaded;

        public bool IsPlaying =>
            playbackState.Status == InstructionGhostPlaybackStatus.Playing;

        public bool IsComplete =>
            playbackState.Status == InstructionGhostPlaybackStatus.Completed;

        public float LoadingProgress { get; private set; }

        public float PlaybackProgress { get; private set; }

        public float DurationSeconds { get; private set; }

        public int LoadedFrameCount => frames.Count;

        public int LoadedJointCount => poseBuffer.IsCreated
            ? poseBuffer.Length
            : 0;

        public string LastError { get; private set; } = string.Empty;

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal int RetargeterReadinessFailuresRemainingForTests
        {
            get => retargeterReadinessFailuresRemainingForTests;
            set => retargeterReadinessFailuresRemainingForTests =
                Mathf.Max(0, value);
        }
#endif

        public void Configure(CharacterRetargeter signerRetargeter)
        {
            retargeter = signerRetargeter;
            signerRenderers = retargeter != null
                ? retargeter.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
        }

        public void Load(RunPhasePlan phasePlan)
        {
            if (phasePlan == null)
            {
                throw new ArgumentNullException(nameof(phasePlan));
            }

            Load(phasePlan.Content);
        }

        public void Load(InstructionContentReference content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            CancelLoad();
            ClearLoadedArtifact();
            SetSignerVisible(false);
            playbackState.Stop();
            LastError = string.Empty;
            IsLoading = true;
            LoadingProgress = 0f;
            Debug.Log(
                $"[InstructionGhostPlayer] Loading pose for " +
                $"sentence={content.SentenceId}, artifact={content.ArtifactPath}.",
                this
            );
            int generation = ++loadGeneration;
            StateChanged?.Invoke();
            loadRoutine = StartCoroutine(LoadArtifact(content, generation));
        }

        public bool Play()
        {
            if (IsLoading || loadedContent == null || frames.Count == 0 ||
                !playbackState.Play())
            {
                return false;
            }

            return StartCurrentPass();
        }

        public bool Replay()
        {
            if (IsLoading || loadedContent == null || frames.Count == 0 ||
                !playbackState.Replay())
            {
                return false;
            }

            return StartCurrentPass();
        }

        public void Stop()
        {
            bool hadWork = IsLoading || loadedContent != null || IsPlaying ||
                frames.Count > 0;
            CancelLoad();
            playbackState.Stop();
            ClearLoadedArtifact();
            SetSignerVisible(false);
            LastError = string.Empty;
            if (hadWork)
            {
                Stopped?.Invoke();
            }
            StateChanged?.Invoke();
        }

        private IEnumerator LoadArtifact(
            InstructionContentReference content,
            int generation)
        {
            string artifactUri;
            try
            {
                artifactUri = ResolveArtifactUri(content.ArtifactPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is UriFormatException)
            {
                FailLoad("Invalid frozen artifact path: " + exception.Message);
                yield break;
            }

            using UnityWebRequest request = UnityWebRequest.Get(artifactUri);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (generation != loadGeneration)
                {
                    yield break;
                }
                LoadingProgress = Mathf.Clamp01(operation.progress);
                StateChanged?.Invoke();
                yield return null;
            }

            if (generation != loadGeneration)
            {
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                FailLoad(
                    $"Cannot read frozen Pose artifact '{artifactUri}': " +
                    request.error
                );
                yield break;
            }

            byte[] bytes = request.downloadHandler?.data;
            if (bytes == null || bytes.Length == 0)
            {
                FailLoad("The frozen Pose artifact is empty.");
                yield break;
            }

            if (!HasExpectedSha256(bytes, content.ArtifactSha256))
            {
                FailLoad(
                    "The frozen Pose artifact SHA-256 does not match the Run Plan."
                );
                yield break;
            }

            if (!TryParseArtifact(
                    bytes,
                    out int jointCount,
                    out string parseError))
            {
                FailLoad(parseError);
                yield break;
            }

            double retargeterDeadline =
                Time.realtimeSinceStartupAsDouble +
                RetargeterReadinessTimeoutSeconds;
            while (true)
            {
                if (generation != loadGeneration)
                {
                    yield break;
                }

                RetargeterValidationResult validationResult =
                    InspectRetargeter(jointCount, out string retargeterError);
                if (validationResult == RetargeterValidationResult.Ready)
                {
                    break;
                }

                if (validationResult == RetargeterValidationResult.Invalid)
                {
                    FailLoad(retargeterError);
                    yield break;
                }

                if (Time.realtimeSinceStartupAsDouble >= retargeterDeadline)
                {
                    FailLoad(
                        "Timed out waiting for the instruction signer's " +
                        "CharacterRetargeter. Last status: " +
                        retargeterError
                    );
                    yield break;
                }

                yield return null;
            }

            DisposePoseBuffer();
            poseBuffer = new NativeArray<MSDKUtility.NativeTransform>(
                jointCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );

            // CharacterRetargeter may already have scheduled its normal
            // Update job earlier in this frame. Disabling the component does
            // not complete that job, and immediately resetting/reusing its
            // native pose buffers then violates Unity's job safety rules. This
            // only happens on the first phase because later phases inherit the
            // already-disabled retargeter.
            retargeter.ConvertPoseJobHandle.Complete();
            retargeter.enabled = false;
            MSDKUtility.ResetInterpolators(retargeter.RetargetingHandle);

            loadedContent = content;
            playbackState.Load(content.ArtifactPath);
            IsLoading = false;
            LoadingProgress = 1f;
            DurationSeconds = Mathf.Max(
                0f,
                (float)(frames[frames.Count - 1].recording_time -
                    frames[0].recording_time)
            );
            loadRoutine = null;
            Loaded?.Invoke(content);
            StateChanged?.Invoke();
        }

        private bool TryParseArtifact(
            byte[] bytes,
            out int jointCount,
            out string error)
        {
            frames.Clear();
            var validator = new InstructionPoseArtifactValidator();
            var utf8 = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true
            );

            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var reader = new StreamReader(
                    stream,
                    utf8,
                    detectEncodingFromByteOrderMarks: true
                );
                int lineNumber = 0;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        throw new InvalidDataException(
                            $"Blank Pose JSONL record at line {lineNumber}."
                        );
                    }

                    RecordedFrame frame;
                    try
                    {
                        frame = JsonUtility.FromJson<RecordedFrame>(line);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidDataException(
                            $"Invalid Pose JSON at line {lineNumber}.",
                            exception
                        );
                    }

                    if (frame == null)
                    {
                        throw new InvalidDataException(
                            $"Missing Pose record at line {lineNumber}."
                        );
                    }

                    int positionCount = frame.positions?.Length ?? 0;
                    int rotationCount = frame.rotations?.Length ?? 0;
                    int scaleCount = frame.scales?.Length ?? 0;
                    validator.AcceptFrame(
                        frame.recording_time,
                        frame.pose_valid,
                        frame.joint_count,
                        positionCount,
                        rotationCount,
                        scaleCount
                    );
                    if (frame.pose_valid)
                    {
                        frames.Add(frame);
                    }
                }

                // This is intentionally separate from record acceptance: a
                // truncated last line or decoder error never reaches valid EOF.
                validator.CompleteEof();
                jointCount = validator.ExpectedJointCount;
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is DecoderFallbackException ||
                exception is InvalidDataException ||
                exception is InvalidOperationException)
            {
                frames.Clear();
                jointCount = 0;
                error = "Invalid frozen Pose artifact: " + exception.Message;
                return false;
            }
        }

        private bool StartCurrentPass()
        {
            frameIndex = 0;
            PlaybackProgress = 0f;
            playbackStartedAt = Time.realtimeSinceStartupAsDouble;
            if (!ApplyFrame(frames[0]))
            {
                FailPlayback("The signer retargeter rejected the first frame.");
                return false;
            }

            SetSignerVisible(true);
            PlaybackStarted?.Invoke(playbackState.CurrentPass);
            StateChanged?.Invoke();
            return true;
        }

        private void Update()
        {
            if (!IsPlaying)
            {
                return;
            }

            double elapsed = Math.Max(
                0d,
                (Time.realtimeSinceStartupAsDouble - playbackStartedAt) *
                playbackSpeed
            );
            double targetTime = frames[0].recording_time + elapsed;
            while (frameIndex + 1 < frames.Count &&
                frames[frameIndex + 1].recording_time <= targetTime)
            {
                frameIndex++;
            }

            if (!ApplyFrame(frames[frameIndex]))
            {
                FailPlayback(
                    $"The signer retargeter rejected frame {frameIndex}."
                );
                return;
            }

            PlaybackProgress = DurationSeconds > 0f
                ? Mathf.Clamp01((float)(elapsed / DurationSeconds))
                : 1f;
            if (frameIndex >= frames.Count - 1 &&
                elapsed >= DurationSeconds)
            {
                PlaybackProgress = 1f;
                InstructionPlaybackPass completedPass =
                    playbackState.CurrentPass;
                playbackState.Complete();
                Completed?.Invoke(completedPass);
                StateChanged?.Invoke();
            }
        }

        private bool ApplyFrame(RecordedFrame frame)
        {
            if (retargeter == null || !poseBuffer.IsCreated ||
                frame == null || frame.joint_count != poseBuffer.Length)
            {
                return false;
            }

            for (int index = 0; index < poseBuffer.Length; index++)
            {
                poseBuffer[index] = new MSDKUtility.NativeTransform(
                    frame.rotations[index].ToQuaternion(),
                    frame.positions[index].ToVector3(),
                    frame.scales[index].ToVector3()
                );
            }

            retargeter.IsValid = true;
            retargeter.CalculatePose(poseBuffer);
            retargeter.UpdatePose();
            return retargeter.RetargeterValid;
        }

        private RetargeterValidationResult InspectRetargeter(
            int jointCount,
            out string error)
        {
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
            if (retargeterReadinessFailuresRemainingForTests > 0)
            {
                retargeterReadinessFailuresRemainingForTests--;
                error = "Injected transient CharacterRetargeter readiness delay.";
                return RetargeterValidationResult.Waiting;
            }
#endif
            if (retargeter == null)
            {
                error = "Instruction signer CharacterRetargeter is not assigned.";
                return RetargeterValidationResult.Invalid;
            }

            ulong handle = retargeter.RetargetingHandle;
            if (handle == 0)
            {
                error = "Instruction signer CharacterRetargeter has no native handle.";
                return RetargeterValidationResult.Waiting;
            }

            if (!MSDKUtility.GetSkeletonInfo(
                    handle,
                    MSDKUtility.SkeletonType.SourceSkeleton,
                    out MSDKUtility.SkeletonInfo skeletonInfo))
            {
                error = "Cannot inspect the instruction signer's source skeleton.";
                return RetargeterValidationResult.Waiting;
            }

            if (skeletonInfo.JointCount != jointCount)
            {
                error =
                    $"Pose joint_count {jointCount} does not match signer " +
                    $"source skeleton {skeletonInfo.JointCount}.";
                return RetargeterValidationResult.Invalid;
            }

            error = string.Empty;
            return RetargeterValidationResult.Ready;
        }

        private string ResolveArtifactUri(string artifactPath)
        {
            if (string.IsNullOrWhiteSpace(artifactPath))
            {
                throw new ArgumentException("Artifact path is empty.");
            }

            string normalized = artifactPath.Trim().Replace('\\', '/');
            if (normalized.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (Path.IsPathRooted(artifactPath))
            {
                return new Uri(Path.GetFullPath(artifactPath)).AbsoluteUri;
            }

            string[] segments = normalized.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                if (segments[index] == "..")
                {
                    throw new ArgumentException(
                        "Artifact path cannot leave the staged content root."
                    );
                }
                segments[index] = Uri.EscapeDataString(segments[index]);
            }

            string root = string.IsNullOrWhiteSpace(contentRootOverride)
                ? Application.streamingAssetsPath
                : contentRootOverride.Trim();
            if (string.IsNullOrWhiteSpace(contentRootOverride) &&
                !string.IsNullOrWhiteSpace(streamingAssetsSubdirectory))
            {
                root = AppendUriPath(
                    root,
                    streamingAssetsSubdirectory.Trim().Replace('\\', '/')
                );
            }

            string combined = AppendUriPath(root, string.Join("/", segments));
            if (combined.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("://"))
            {
                return combined;
            }
            return new Uri(Path.GetFullPath(combined)).AbsoluteUri;
        }

        private static string AppendUriPath(string root, string child)
        {
            return root.TrimEnd('/', '\\') + "/" + child.TrimStart('/', '\\');
        }

        private static bool HasExpectedSha256(byte[] bytes, string expected)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] digest = sha256.ComputeHash(bytes);
            var builder = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; index++)
            {
                builder.Append(digest[index].ToString("x2"));
            }
            return string.Equals(
                builder.ToString(),
                expected,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private void FailLoad(string error)
        {
            InteractionRuntimeDiagnosticTrace.Write(
                "ghost_load_failed",
                "error=" + error
            );
            loadRoutine = null;
            IsLoading = false;
            LoadingProgress = 0f;
            frames.Clear();
            playbackState.Fail();
            LastError = error;
            Debug.LogError("[InstructionGhostPlayer] " + error, this);
            Failed?.Invoke(error);
            StateChanged?.Invoke();
        }

        private void FailPlayback(string error)
        {
            InteractionRuntimeDiagnosticTrace.Write(
                "ghost_playback_failed",
                "error=" + error
            );
            playbackState.Fail();
            LastError = error;
            Failed?.Invoke(error);
            StateChanged?.Invoke();
        }

        private void CancelLoad()
        {
            loadGeneration++;
            if (loadRoutine != null)
            {
                StopCoroutine(loadRoutine);
                loadRoutine = null;
            }
            IsLoading = false;
            LoadingProgress = 0f;
        }

        private void ClearLoadedArtifact()
        {
            frames.Clear();
            DisposePoseBuffer();
            loadedContent = null;
            frameIndex = 0;
            PlaybackProgress = 0f;
            DurationSeconds = 0f;
        }

        private void DisposePoseBuffer()
        {
            if (poseBuffer.IsCreated)
            {
                poseBuffer.Dispose();
            }
            poseBuffer = default;
        }

        private void SetSignerVisible(bool visible)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            for (int index = 0; index < signerRenderers.Length; index++)
            {
                if (signerRenderers[index] != null)
                {
                    signerRenderers[index].enabled = visible;
                }
            }
        }

        private void Start()
        {
            if (signerRenderers.Length == 0 && retargeter != null)
            {
                signerRenderers =
                    retargeter.GetComponentsInChildren<Renderer>(true);
            }
            SetSignerVisible(false);
        }

        private void OnDisable()
        {
            Stop();
        }

        private void OnDestroy()
        {
            CancelLoad();
            DisposePoseBuffer();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            playbackSpeed = Mathf.Clamp(playbackSpeed, 0.25f, 2f);
        }
#endif
    }
}
