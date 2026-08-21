using System;
using System.Collections.Generic;
using System.IO;
using Meta.XR.Movement;
using Meta.XR.Movement.Retargeting;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Demo
{
    [DefaultExecutionOrder(-1000)]
    public sealed class AvatarPreviewMetaPosePlayer : MonoBehaviour
    {
        [Header("Recorded Take")]
        [SerializeField]
        private TextAsset metadataAsset;

        [SerializeField]
        private TextAsset poseAsset;

        [Header("Retargeting")]
        [SerializeField]
        private RecordedMetaPoseProvider[] providers = Array.Empty<RecordedMetaPoseProvider>();

        [SerializeField]
        private CharacterRetargeter[] targets = Array.Empty<CharacterRetargeter>();

        [Header("Controls")]
        [SerializeField]
        private Button playbackButton;

        [SerializeField]
        private TMP_Text playbackLabel;

        private readonly List<RecordedFrame> frames = new();
        private NativeArray<MSDKUtility.NativeTransform> currentPose;
        private int frameIndex;
        private double playbackStartedAt;
        private double firstRecordingTime;
        private double durationSeconds;
        private bool isPlaying;
        private bool hasValidPose;

        public NativeArray<MSDKUtility.NativeTransform> CurrentPose => currentPose;
        public bool HasValidPose => hasValidPose && currentPose.IsCreated;

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
                positions != null && positions.Length >= joint_count &&
                rotations != null && rotations.Length >= joint_count &&
                scales != null && scales.Length >= joint_count;
        }

        [Serializable]
        private sealed class TakeMetadata
        {
            public string sentence_id;
            public string take_id;
            public int pose_frame_count;
        }

        public void Configure(
            TextAsset metadata,
            TextAsset pose,
            RecordedMetaPoseProvider[] poseProviders,
            CharacterRetargeter[] retargeters,
            Button button,
            TMP_Text label)
        {
            metadataAsset = metadata;
            poseAsset = pose;
            providers = poseProviders;
            targets = retargeters;
            playbackButton = button;
            playbackLabel = label;

            foreach (RecordedMetaPoseProvider provider in providers)
            {
                provider.Configure(this);
            }
        }

        private void Awake()
        {
            foreach (CharacterRetargeter target in targets)
            {
                target.SkeletonRetargeter.ApplyRootScale = false;
            }

            LoadPose();
            SetButtonLabel("PLAY META POSE");
        }

        public void PlayOrRestart()
        {
            if (frames.Count == 0)
            {
                SetButtonLabel("POSE DATA ERROR");
                return;
            }

            int jointCount = frames[0].joint_count;
            foreach (CharacterRetargeter target in targets)
            {
                if (target.RetargetingHandle == 0)
                {
                    Debug.LogError($"[AvatarPreviewPose] {target.name} retargeter is not initialized.");
                    SetButtonLabel("RETARGETER NOT READY");
                    return;
                }

                if (!MSDKUtility.GetSkeletonInfo(
                        target.RetargetingHandle,
                        MSDKUtility.SkeletonType.SourceSkeleton,
                        out MSDKUtility.SkeletonInfo sourceSkeleton) ||
                    sourceSkeleton.JointCount != jointCount)
                {
                    Debug.LogError(
                        $"[AvatarPreviewPose] {target.name} expects " +
                        $"{sourceSkeleton.JointCount} source joints, recording has {jointCount}."
                    );
                    SetButtonLabel("SKELETON MISMATCH");
                    return;
                }
            }

            frameIndex = 0;
            playbackStartedAt = Time.unscaledTimeAsDouble;
            isPlaying = true;
            ApplyFrame(frames[0]);
            SetButtonLabel("PLAYING 0%");
        }

        private void Update()
        {
            if (!isPlaying)
            {
                return;
            }

            double elapsed = Time.unscaledTimeAsDouble - playbackStartedAt;
            double targetTime = firstRecordingTime + elapsed;

            while (
                frameIndex + 1 < frames.Count &&
                frames[frameIndex + 1].recording_time <= targetTime)
            {
                frameIndex++;
            }

            ApplyFrame(frames[frameIndex]);

            float progress = durationSeconds > 0d
                ? Mathf.Clamp01((float)(elapsed / durationSeconds))
                : 1f;
            SetButtonLabel($"PLAYING {Mathf.RoundToInt(progress * 100f)}%");

            if (frameIndex >= frames.Count - 1)
            {
                isPlaying = false;
                SetButtonLabel("REPLAY META POSE");
            }
        }

        private void LoadPose()
        {
            if (poseAsset == null)
            {
                Debug.LogError("[AvatarPreviewPose] Pose TextAsset is not assigned.");
                return;
            }

            frames.Clear();
            using var reader = new StringReader(poseAsset.text);
            string line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                RecordedFrame frame;
                try
                {
                    frame = JsonUtility.FromJson<RecordedFrame>(line);
                }
                catch (ArgumentException exception)
                {
                    Debug.LogError(
                        $"[AvatarPreviewPose] Invalid JSON at line {lineNumber}: {exception.Message}"
                    );
                    frames.Clear();
                    return;
                }

                if (frame == null || !frame.IsUsable)
                {
                    Debug.LogError($"[AvatarPreviewPose] Invalid pose frame at line {lineNumber}.");
                    frames.Clear();
                    return;
                }

                frames.Add(frame);
            }

            if (frames.Count == 0)
            {
                Debug.LogError("[AvatarPreviewPose] Pose asset contains no usable frames.");
                return;
            }

            int jointCount = frames[0].joint_count;
            foreach (RecordedFrame frame in frames)
            {
                if (frame.joint_count != jointCount)
                {
                    Debug.LogError("[AvatarPreviewPose] Joint count changes inside the recording.");
                    frames.Clear();
                    return;
                }
            }

            TakeMetadata metadata = metadataAsset != null
                ? JsonUtility.FromJson<TakeMetadata>(metadataAsset.text)
                : null;
            if (metadata != null && metadata.pose_frame_count != frames.Count)
            {
                Debug.LogError(
                    $"[AvatarPreviewPose] Metadata declares {metadata.pose_frame_count} frames, " +
                    $"but {frames.Count} were loaded."
                );
                frames.Clear();
                return;
            }

            currentPose = new NativeArray<MSDKUtility.NativeTransform>(
                jointCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory
            );
            firstRecordingTime = frames[0].recording_time;
            durationSeconds = Math.Max(
                0d,
                frames[^1].recording_time - firstRecordingTime
            );

            Debug.Log(
                $"[AvatarPreviewPose] Loaded {frames.Count} frames, {jointCount} joints, " +
                $"{durationSeconds:F2}s ({metadata?.sentence_id}/{metadata?.take_id})."
            );
        }

        private void ApplyFrame(RecordedFrame frame)
        {
            for (int i = 0; i < frame.joint_count; i++)
            {
                currentPose[i] = new MSDKUtility.NativeTransform(
                    frame.rotations[i].ToQuaternion(),
                    frame.positions[i].ToVector3(),
                    frame.scales[i].ToVector3()
                );
            }

            hasValidPose = true;
        }

        private void SetButtonLabel(string value)
        {
            if (playbackLabel != null)
            {
                playbackLabel.text = value;
            }
        }

        private void OnDestroy()
        {
            hasValidPose = false;
            if (currentPose.IsCreated)
            {
                currentPose.Dispose();
            }
        }
    }
}
