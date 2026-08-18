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

        public string StatusLabel
        {
            get
            {
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
            CharacterRetargeter characterRetargeter)
        {
            coordinator = recordingCoordinator;
            liveRetargeter = characterRetargeter;
        }

        public void PlayLastTake()
        {
            if (IsReviewing)
            {
                RestartPlayback();
                return;
            }

            if (
                coordinator == null ||
                liveRetargeter == null ||
                !coordinator.HasLastArtifact
            )
            {
                return;
            }

            string posePath = coordinator.LastArtifact.PosePath;
            if (!File.Exists(posePath) || !coordinator.TryBeginReview())
            {
                return;
            }

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
            ApplyFrame(frames[0]);
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

            long fileLength = new FileInfo(posePath).Length;
            using (var stream = new FileStream(
                       posePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                int linesRead = 0;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        RecordedFrame frame =
                            JsonUtility.FromJson<RecordedFrame>(line);
                        if (frame != null && frame.IsUsable)
                        {
                            frames.Add(frame);
                        }
                    }

                    linesRead++;
                    if (linesRead % 120 == 0)
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
                Debug.LogError(
                    "[RecordingReplayController] The Take contains no valid pose frames."
                );
                CleanupPlayback();
                coordinator.EndReview();
                yield break;
            }

            DurationSeconds = Mathf.Max(
                0f,
                (float)(frames[^1].recording_time - frames[0].recording_time)
            );
            retargeterWasEnabled = liveRetargeter.enabled;
            liveRetargeter.enabled = false;
            retargeterOverrideActive = true;
            RestartPlayback();
        }

        private void Update()
        {
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

            ApplyFrame(frames[frameIndex]);
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

        private double CurrentRecordingTime()
        {
            return Math.Max(
                0d,
                (Time.realtimeSinceStartupAsDouble - playbackStartedAt) *
                playbackSpeed
            );
        }

        private void ApplyFrame(RecordedFrame frame)
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
            }
            finally
            {
                pose.Dispose();
            }
        }

        private void CleanupPlayback()
        {
            if (loadRoutine != null)
            {
                StopCoroutine(loadRoutine);
                loadRoutine = null;
            }

            if (liveRetargeter != null && retargeterOverrideActive)
            {
                liveRetargeter.enabled = retargeterWasEnabled;
                liveRetargeter.IsValid = false;
            }
            retargeterOverrideActive = false;

            frames.Clear();
            frameIndex = 0;
            IsReviewing = false;
            IsLoading = false;
            IsPaused = false;
            IsComplete = false;
            LoadingProgress = 0f;
            PlaybackProgress = 0f;
            DurationSeconds = 0f;
            PresentationChanged?.Invoke();
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
