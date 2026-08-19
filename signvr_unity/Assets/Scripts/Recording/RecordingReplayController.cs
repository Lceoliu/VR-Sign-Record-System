using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Meta.XR.Movement;
using Meta.XR.Movement.Retargeting;
using Unity.Collections;
using UnityEngine;

namespace SignVR.Recording
{
    [DefaultExecutionOrder(-200)]
    public sealed class RecordingReplayController : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private CharacterRetargeter liveRetargeter;

        [SerializeField]
        private OVRHand leftLiveHand;

        [SerializeField]
        private OVRHand rightLiveHand;

        [SerializeField]
        private SkinnedMeshRenderer leftLiveHandRenderer;

        [SerializeField]
        private SkinnedMeshRenderer rightLiveHandRenderer;

        [SerializeField]
        [Range(0.25f, 2f)]
        private float playbackSpeed = 1f;

        private readonly List<RecordedFrame> frames = new();
        private Coroutine loadRoutine;
        private bool retargeterWasEnabled;
        private bool retargeterOverrideActive;
        private int frameIndex;
        private double playbackStartedAt;
        private double pausedAtRecordingTime;

        public event Action PresentationChanged;

        public bool IsReviewing { get; private set; }
        public bool IsLoading { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsComplete { get; private set; }
        public float LoadingProgress { get; private set; }
        public float PlaybackProgress { get; private set; }
        public float DurationSeconds { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public bool HasError => !string.IsNullOrWhiteSpace(LastError);

        public string StatusLabel
        {
            get
            {
                if (HasError)
                {
                    return LastError;
                }

                if (IsLoading)
                {
                    return $"正在载入动作 {Mathf.RoundToInt(LoadingProgress * 100f)}%";
                }

                if (IsComplete)
                {
                    return "回看完成 · 点按“重播”或“退出回看”";
                }

                if (IsPaused)
                {
                    return "回看已暂停";
                }

                return IsReviewing
                    ? $"镜像动作回看 {PlaybackProgress * 100f:F0}%"
                    : "尚无可回看的 Take";
            }
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

            public bool IsUsable =>
                pose_valid &&
                joint_count > 0 &&
                positions != null &&
                rotations != null &&
                scales != null &&
                positions.Length >= joint_count &&
                rotations.Length >= joint_count &&
                scales.Length >= joint_count;
        }

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            CharacterRetargeter characterRetargeter,
            OVRHand leftHand,
            OVRHand rightHand)
        {
            coordinator = recordingCoordinator;
            liveRetargeter = characterRetargeter;
            leftLiveHand = leftHand;
            rightLiveHand = rightHand;
            leftLiveHandRenderer = leftHand.GetComponent<SkinnedMeshRenderer>();
            rightLiveHandRenderer = rightHand.GetComponent<SkinnedMeshRenderer>();
        }

        public void PlayLastTake()
        {
            LastError = string.Empty;

            if (IsReviewing)
            {
                RestartPlayback();
                return;
            }

            if (coordinator == null || liveRetargeter == null)
            {
                RejectPlayback(
                    "回放组件未就绪，请呼叫工作人员",
                    "Coordinator or retargeter is not assigned."
                );
                return;
            }

            if (!coordinator.HasLastArtifact)
            {
                RejectPlayback(
                    "当前句还没有可回看的动作",
                    $"No finalized Take. State={coordinator.State}."
                );
                return;
            }

            string posePath = coordinator.LastArtifact.PosePath;
            if (!File.Exists(posePath))
            {
                RejectPlayback(
                    "回放文件不存在，请重新录制本句",
                    $"Pose file is missing at {posePath}."
                );
                return;
            }

            if (!coordinator.TryBeginReview())
            {
                RejectPlayback(
                    "请等待当前操作完成后再回放",
                    $"Cannot enter review from {coordinator.State}."
                );
                return;
            }

            Debug.Log($"[RecordingReplay] Loading {posePath}.");

            loadRoutine = StartCoroutine(LoadAndPlay(posePath));
        }

        public void TogglePause()
        {
            if (!IsReviewing || IsLoading)
            {
                return;
            }

            if (IsComplete)
            {
                RestartPlayback();
                return;
            }

            if (IsPaused)
            {
                playbackStartedAt =
                    Time.realtimeSinceStartupAsDouble -
                    pausedAtRecordingTime / playbackSpeed;
                IsPaused = false;
            }
            else
            {
                pausedAtRecordingTime = CurrentRecordingTime();
                IsPaused = true;
            }

            PresentationChanged?.Invoke();
        }

        public void StopReview()
        {
            bool coordinatorIsReviewing =
                coordinator != null &&
                coordinator.State == RecordingFlowState.Reviewing;

            if (!IsReviewing && !IsLoading && !coordinatorIsReviewing)
            {
                return;
            }

            CleanupPlayback();
            if (coordinatorIsReviewing)
            {
                coordinator.EndReview();
            }
        }

        public void RestartPlayback()
        {
            if (frames.Count == 0 || IsLoading)
            {
                return;
            }

            frameIndex = 0;
            pausedAtRecordingTime = 0d;
            playbackStartedAt = Time.realtimeSinceStartupAsDouble;
            PlaybackProgress = 0f;
            IsPaused = false;
            IsComplete = false;
            if (!ApplyFrame(frames[0]))
            {
                AbortReview(
                    "机器人回放启动失败，请呼叫工作人员",
                    "The retargeter rejected the first playback frame."
                );
                return;
            }
            PresentationChanged?.Invoke();
        }

        private IEnumerator LoadAndPlay(string posePath)
        {
            IsLoading = true;
            IsReviewing = true;
            IsPaused = false;
            IsComplete = false;
            LoadingProgress = 0f;
            PlaybackProgress = 0f;
            frames.Clear();
            PresentationChanged?.Invoke();

            if (!TryOpenPoseFile(
                    posePath,
                    out FileStream stream,
                    out StreamReader reader,
                    out string openError))
            {
                AbortReview("无法读取回放文件，请重新录制本句", openError);
                yield break;
            }

            long fileLength = stream.Length;
            using (stream)
            using (reader)
            {
                int linesRead = 0;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        if (!TryParseFrame(line, out RecordedFrame frame))
                        {
                            AbortReview(
                                "回放文件内容损坏，请重新录制本句",
                                $"Invalid JSON at pose line {linesRead + 1}."
                            );
                            yield break;
                        }
                        if (frame != null && frame.IsUsable)
                        {
                            frames.Add(frame);
                        }
                    }

                    linesRead++;
                    if (linesRead % 24 == 0)
                    {
                        LoadingProgress = fileLength > 0
                            ? Mathf.Clamp01((float)stream.Position / fileLength)
                            : 1f;
                        PresentationChanged?.Invoke();
                        yield return null;
                    }
                }
            }

            LoadingProgress = 1f;
            IsLoading = false;
            loadRoutine = null;

            if (frames.Count == 0)
            {
                AbortReview(
                    "这次录制没有可回放的动作帧，请重新录制",
                    "The Take contains no valid pose frames."
                );
                yield break;
            }

            if (!ValidateRetargeter(
                    frames[0].joint_count,
                    out string retargeterError))
            {
                AbortReview(
                    "机器人回放配置不匹配，请呼叫工作人员",
                    retargeterError
                );
                yield break;
            }

            DurationSeconds = Mathf.Max(
                0f,
                (float)(frames[^1].recording_time - frames[0].recording_time)
            );
            retargeterWasEnabled = liveRetargeter.enabled;
            liveRetargeter.enabled = false;
            retargeterOverrideActive = true;
            MSDKUtility.ResetInterpolators(liveRetargeter.RetargetingHandle);
            Debug.Log(
                $"[RecordingReplay] Loaded {frames.Count} frames, " +
                $"duration {DurationSeconds:F2}s."
            );
            RestartPlayback();
        }

        private void Update()
        {
            if (
                HasError &&
                coordinator != null &&
                coordinator.State != RecordingFlowState.Ready &&
                coordinator.State != RecordingFlowState.Completed
            )
            {
                LastError = string.Empty;
                PresentationChanged?.Invoke();
            }

            if (!IsReviewing || IsLoading)
            {
                return;
            }

            if (
                coordinator == null ||
                coordinator.State != RecordingFlowState.Reviewing
            )
            {
                CleanupPlayback();
                return;
            }

            if (IsPaused || IsComplete)
            {
                return;
            }

            double recordingTime = CurrentRecordingTime();
            double firstTime = frames[0].recording_time;
            double targetTime = firstTime + recordingTime;

            while (
                frameIndex + 1 < frames.Count &&
                frames[frameIndex + 1].recording_time <= targetTime
            )
            {
                frameIndex++;
            }

            if (!ApplyFrame(frames[frameIndex]))
            {
                AbortReview(
                    "机器人回放中断，请呼叫工作人员",
                    $"Retargeter rejected playback frame {frameIndex}."
                );
                return;
            }
            PlaybackProgress = DurationSeconds > 0f
                ? Mathf.Clamp01((float)(recordingTime / DurationSeconds))
                : 1f;

            if (frameIndex >= frames.Count - 1)
            {
                PlaybackProgress = 1f;
                IsComplete = true;
                IsPaused = true;
                PresentationChanged?.Invoke();
            }
        }

        private void LateUpdate()
        {
            if (!IsReviewing && !IsLoading)
            {
                return;
            }

            KeepTrackedHandVisible(leftLiveHand, leftLiveHandRenderer);
            KeepTrackedHandVisible(rightLiveHand, rightLiveHandRenderer);
        }

        private static void KeepTrackedHandVisible(
            OVRHand hand,
            SkinnedMeshRenderer renderer)
        {
            renderer.enabled = hand.IsDataValid && hand.IsTracked;
        }

        private double CurrentRecordingTime()
        {
            return Math.Max(
                0d,
                (Time.realtimeSinceStartupAsDouble - playbackStartedAt) *
                playbackSpeed
            );
        }

        private bool ApplyFrame(RecordedFrame frame)
        {
            var pose = new NativeArray<MSDKUtility.NativeTransform>(
                frame.joint_count,
                Allocator.Temp
            );
            try
            {
                for (int i = 0; i < frame.joint_count; i++)
                {
                    pose[i] = new MSDKUtility.NativeTransform(
                        frame.rotations[i].ToQuaternion(),
                        frame.positions[i].ToVector3(),
                        frame.scales[i].ToVector3()
                    );
                }

                liveRetargeter.IsValid = true;
                liveRetargeter.CalculatePose(pose);
                liveRetargeter.UpdatePose();
                return liveRetargeter.RetargeterValid;
            }
            finally
            {
                pose.Dispose();
            }
        }

        private void CleanupPlayback(bool clearError = true)
        {
            if (loadRoutine != null)
            {
                StopCoroutine(loadRoutine);
                loadRoutine = null;
            }

            if (liveRetargeter != null && retargeterOverrideActive)
            {
                if (liveRetargeter.RetargetingHandle != 0)
                {
                    MSDKUtility.ResetInterpolators(
                        liveRetargeter.RetargetingHandle
                    );
                }
                liveRetargeter.enabled = retargeterWasEnabled;
            }
            retargeterOverrideActive = false;

            RestoreHandRenderer(leftLiveHand, leftLiveHandRenderer);
            RestoreHandRenderer(rightLiveHand, rightLiveHandRenderer);

            frames.Clear();
            frameIndex = 0;
            IsReviewing = false;
            IsLoading = false;
            IsPaused = false;
            IsComplete = false;
            LoadingProgress = 0f;
            PlaybackProgress = 0f;
            DurationSeconds = 0f;
            if (clearError)
            {
                LastError = string.Empty;
            }
            PresentationChanged?.Invoke();
        }

        private static void RestoreHandRenderer(
            OVRHand hand,
            SkinnedMeshRenderer renderer)
        {
            renderer.enabled = hand.IsDataValid && hand.IsDataHighConfidence;
        }

        private static bool TryOpenPoseFile(
            string posePath,
            out FileStream stream,
            out StreamReader reader,
            out string error)
        {
            stream = null;
            reader = null;
            error = string.Empty;

            try
            {
                stream = new FileStream(
                    posePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                );
                reader = new StreamReader(stream);
                return true;
            }
            catch (IOException exception)
            {
                stream?.Dispose();
                error = exception.Message;
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                stream?.Dispose();
                error = exception.Message;
                return false;
            }
        }

        private static bool TryParseFrame(
            string json,
            out RecordedFrame frame)
        {
            try
            {
                frame = JsonUtility.FromJson<RecordedFrame>(json);
                return true;
            }
            catch (ArgumentException)
            {
                frame = null;
                return false;
            }
        }

        private bool ValidateRetargeter(
            int recordedJointCount,
            out string error)
        {
            ulong handle = liveRetargeter.RetargetingHandle;
            if (handle == 0)
            {
                error = "CharacterRetargeter has not initialized its native handle.";
                return false;
            }

            if (!MSDKUtility.GetSkeletonInfo(
                    handle,
                    MSDKUtility.SkeletonType.SourceSkeleton,
                    out MSDKUtility.SkeletonInfo skeletonInfo))
            {
                error = "Cannot read the retargeter's source skeleton info.";
                return false;
            }

            if (skeletonInfo.JointCount != recordedJointCount)
            {
                error =
                    $"Recorded joint count {recordedJointCount} does not match " +
                    $"retargeter source joint count {skeletonInfo.JointCount}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void RejectPlayback(string userMessage, string diagnostic)
        {
            LastError = userMessage;
            Debug.LogWarning($"[RecordingReplay] Aborted: {diagnostic}");
            PresentationChanged?.Invoke();
        }

        private void AbortReview(string userMessage, string diagnostic)
        {
            bool coordinatorIsReviewing =
                coordinator != null &&
                coordinator.State == RecordingFlowState.Reviewing;

            LastError = userMessage;
            Debug.LogError($"[RecordingReplay] Failed: {diagnostic}");
            loadRoutine = null;
            CleanupPlayback(false);
            if (coordinatorIsReviewing)
            {
                coordinator.EndReview();
            }
        }

        private void OnDisable()
        {
            if (IsReviewing || IsLoading)
            {
                CleanupPlayback();
            }
        }
    }
}
