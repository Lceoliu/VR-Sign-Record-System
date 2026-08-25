using System;
using System.IO;
using System.Text;
using Meta.XR.Movement;
using Meta.XR.Movement.Retargeting;
using SignVR.Recording;
using Unity.Collections;
using UnityEngine;

public sealed class MetaBodyMotionRecorder : MonoBehaviour
{
    public readonly struct RecordingArtifact
    {
        public RecordingArtifact(
            RecordingTakeContext take,
            string posePath,
            string metadataPath,
            string captureStatus)
        {
            Take = take;
            PosePath = posePath;
            MetadataPath = metadataPath;
            CaptureStatus = captureStatus;
        }

        public RecordingTakeContext Take { get; }
        public string PosePath { get; }
        public string MetadataPath { get; }
        public string CaptureStatus { get; }
    }

    [Header("Source")]
    [SerializeField]
    private MetaSourceDataProvider sourceDataProvider;

    [Tooltip("可选：提供每帧手部追踪质量，写入 Pose 流与 Take 元数据。")]
    [SerializeField]
    private SignVR.Recording.HandCaptureBoundaryMonitor boundaryMonitor;

    private SignVR.Recording.HandCaptureQualitySummary handCaptureQuality = new();

    [SerializeField]
    [Tooltip("Captures the selected world viewpoint and XR reference frame for each Take.")]
    private RecordingSpatialMetadataProvider spatialMetadataProvider;

    [Header("Recording")]
    [SerializeField]
    private bool recordAutomatically = true;

    [SerializeField]
    [Min(0f)]
    private float startDelaySeconds = 3f;

    [SerializeField]
    [Min(0f)]
    private float recordingDurationSeconds = 10f;

    [SerializeField]
    [Range(1, 120)]
    private int sampleRate = 30;

    [Header("Take Quality")]
    [SerializeField]
    [Min(1)]
    [Tooltip("A normally completed Take must contain at least this many sampled frames.")]
    private int minimumCompletedFrameCount = 15;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("On device, this fraction of sampled frames must contain a valid, non-empty pose.")]
    private float minimumValidPoseRatio = 0.9f;

    [Header("Editor Simulation")]
    [SerializeField]
    [Tooltip("Editor only: allow recording workflow tests before a valid Meta body pose is available.")]
    private bool allowEditorSimulationWithoutPose = true;

    private AsyncPoseFileWriter writer;
    private bool isRecording;
    private bool hasRecorded;

    private double appStartTime;
    private double recordingStartTime;
    private double nextSampleTime;
    private double stopTime;

    private long sampleIndex;
    private long validPoseFrameCount;
    private int expectedJointCount;
    private string outputPath;
    private string metadataPath;
    private DateTime recordingStartedUtc;
    private DateTime recordingStoppedUtc;
    private RecordingTakeContext currentTake;
    private RecordingSpatialSnapshot currentSpatialSnapshot;
    private bool currentTakeUsesSimulatedPose;

    public bool IsRecording => isRecording;
    public long SampleCount => sampleIndex;
    public string CurrentOutputPath => outputPath;
    public string CurrentMetadataPath => metadataPath;
    public RecordingTakeContext CurrentTake => currentTake;
    public string LastError { get; private set; } = string.Empty;
    public bool IsPoseReady =>
        sourceDataProvider != null &&
        (sourceDataProvider.IsPoseValid() ||
         CanSimulateInvalidPoseInEditor);

    public MetaSourceDataProvider SourceDataProvider => sourceDataProvider;

    public void ConfigureSource(MetaSourceDataProvider provider)
    {
        sourceDataProvider = provider;
    }

    /// <summary>
    /// Hands start/stop ownership to RecordingCoordinator. Coordinated takes
    /// must not be started or truncated by the legacy timed recording path.
    /// </summary>
    public void ConfigureManagedRecording()
    {
        recordAutomatically = false;
        recordingDurationSeconds = 0f;
    }

    public void ConfigureBoundaryMonitor(
        SignVR.Recording.HandCaptureBoundaryMonitor monitor)
    {
        boundaryMonitor = monitor;
    }

    public void ConfigureSpatialMetadataProvider(
        RecordingSpatialMetadataProvider provider)
    {
        spatialMetadataProvider = provider;
    }

    public event Action<RecordingArtifact> RecordingFinalized;

    public bool TryFindLatestArtifact(
        string sessionId,
        string sentenceId,
        out RecordingArtifact artifact)
    {
        artifact = default;

        string directory = Path.Combine(
            Application.persistentDataPath,
            "Recordings",
            RecordingTakeContext.SanitizeFileSegment(sessionId),
            RecordingTakeContext.SanitizeFileSegment(sentenceId)
        );
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            TakeMetadata latest = null;
            string latestMetadataPath = null;
            string latestPosePath = null;
            DateTime latestWriteTimeUtc = DateTime.MinValue;

            foreach (string candidatePath in Directory.GetFiles(
                         directory,
                         "*.meta.json",
                         SearchOption.TopDirectoryOnly))
            {
                if (!TryReadMetadata(candidatePath, out TakeMetadata candidate))
                {
                    continue;
                }

                if (
                    candidate.session_id != sessionId ||
                    candidate.sentence_id != sentenceId ||
                    candidate.take_index < 1 ||
                    string.IsNullOrWhiteSpace(candidate.take_id) ||
                    string.IsNullOrWhiteSpace(candidate.pose_file)
                )
                {
                    continue;
                }

                string candidatePosePath = Path.Combine(
                    directory,
                    candidate.pose_file
                );
                if (!File.Exists(candidatePosePath))
                {
                    continue;
                }

                DateTime writeTimeUtc = File.GetLastWriteTimeUtc(candidatePath);
                bool isNewer =
                    latest == null ||
                    candidate.take_index > latest.take_index ||
                    candidate.take_index == latest.take_index &&
                    writeTimeUtc > latestWriteTimeUtc;
                if (!isNewer)
                {
                    continue;
                }

                latest = candidate;
                latestMetadataPath = candidatePath;
                latestPosePath = candidatePosePath;
                latestWriteTimeUtc = writeTimeUtc;
            }

            if (latest == null)
            {
                return false;
            }

            DateTime createdAtUtc = DateTime.TryParse(
                latest.utc_started,
                out DateTime parsedStartedAt
            )
                ? parsedStartedAt.ToUniversalTime()
                : latestWriteTimeUtc;
            var take = new RecordingTakeContext(
                latest.session_id,
                latest.sentence_id,
                latest.sentence_text,
                latest.take_index,
                latest.take_id,
                createdAtUtc
            );
            artifact = new RecordingArtifact(
                take,
                latestPosePath,
                latestMetadataPath,
                latest.capture_status
            );
            return true;
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is NotSupportedException ||
            exception is ArgumentException)
        {
            Debug.LogWarning(
                "[MetaBodyMotionRecorder] Cannot scan stored Takes in " +
                $"{directory}: {exception.Message}"
            );
            return false;
        }
    }

    [Serializable]
    private struct Vector3Record
    {
        public float x;
        public float y;
        public float z;

        public Vector3Record(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }
    }

    [Serializable]
    private struct QuaternionRecord
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public QuaternionRecord(Quaternion value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
            w = value.w;
        }
    }

    [Serializable]
    private sealed class FrameRecord
    {
        public long sample_index;
        public int unity_frame;

        public double monotonic_time;
        public double recording_time;

        public bool pose_valid;
        public int joint_count;

        public Vector3Record[] positions;
        public QuaternionRecord[] rotations;
        public Vector3Record[] scales;

        // Per-frame hand tracking quality, so a take can be filtered on measured
        // reliability instead of someone judging it from the video.
        public SignVR.Recording.HandCaptureFrame hand_capture;
    }

    [Serializable]
    private sealed class TakeMetadata
    {
        public string session_id;
        public string sentence_id;
        public string sentence_text;
        public string take_id;
        public int take_index;
        public string capture_status;
        public string review_status;
        public string reset_reason;
        public string utc_started;
        public string utc_stopped;
        public long pose_frame_count;
        public long valid_pose_frame_count;
        public float valid_pose_ratio;
        public int expected_joint_count;
        public string pose_file;
        public string app_version;
        public string device_model;
        public SignVR.Recording.HandCaptureQualitySummary hand_capture_quality;
        public RecordingSpatialSnapshot spatial_context;
        public bool editor_simulation;
        public bool pose_source_simulated;
    }

    private static bool TryReadMetadata(
        string metadataPath,
        out TakeMetadata metadata)
    {
        try
        {
            metadata = JsonUtility.FromJson<TakeMetadata>(
                File.ReadAllText(metadataPath)
            );
            return metadata != null;
        }
        catch (IOException exception)
        {
            Debug.LogWarning(
                $"[MetaBodyMotionRecorder] Cannot read replay metadata " +
                $"{metadataPath}: {exception.Message}"
            );
            metadata = null;
            return false;
        }
        catch (ArgumentException exception)
        {
            Debug.LogWarning(
                $"[MetaBodyMotionRecorder] Invalid replay metadata " +
                $"{metadataPath}: {exception.Message}"
            );
            metadata = null;
            return false;
        }
    }

    private void Awake()
    {
        appStartTime = Time.realtimeSinceStartupAsDouble;
        ResolveSpatialMetadataProvider();

        if (sourceDataProvider == null)
        {
            Debug.LogError(
                "[MetaBodyMotionRecorder] " +
                "MetaSourceDataProvider has not been assigned."
            );
        }
    }

    private void LateUpdate()
    {
        if (sourceDataProvider == null)
        {
            if (isRecording)
            {
                LastError = "姿态数据源已断开，请重录当前句";
                StopRecordingInternal(
                    "capture_error",
                    "pose_source_unavailable"
                );
            }
            return;
        }

        double now = Time.realtimeSinceStartupAsDouble;

        if (!isRecording)
        {
            // MetaSourceDataProvider only advances its pose-validity debounce
            // while GetSkeletonPose is sampled. The preview streamer reads
            // BodyState directly, so without this warm-up the first recording
            // probe is always rejected even when all body joints are valid.
            RefreshPoseProviderWhileIdle();

            bool startDelayFinished =
                now - appStartTime >= startDelaySeconds;

            if (
                recordAutomatically &&
                !hasRecorded &&
                startDelayFinished &&
                IsPoseReady
            )
            {
                StartRecording();
            }

            return;
        }

        if (
            recordingDurationSeconds > 0f &&
            now >= stopTime
        )
        {
            StopRecording();
            return;
        }

        if (now < nextSampleTime)
        {
            return;
        }

        try
        {
            CaptureFrame(now);
        }
        catch (Exception exception)
        {
            LastError = "姿态采样失败，请重录当前句";
            Debug.LogError(
                "[MetaBodyMotionRecorder] Failed to capture a pose frame: " +
                exception
            );
            StopRecordingInternal(
                "capture_error",
                "pose_frame_capture_failed"
            );
            return;
        }

        double interval = 1.0 / Math.Max(sampleRate, 1);
        nextSampleTime += interval;

        // Drop outdated sampling slots rather than writing duplicates.
        if (nextSampleTime < now - interval)
        {
            nextSampleTime = now + interval;
        }
    }

    private void RefreshPoseProviderWhileIdle()
    {
        NativeArray<MSDKUtility.NativeTransform> pose = default;
        try
        {
            pose = sourceDataProvider.GetSkeletonPose();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[MetaBodyMotionRecorder] Could not warm body tracking: " +
                exception.Message
            );
        }
        finally
        {
            if (pose.IsCreated)
            {
                pose.Dispose();
            }
        }
    }

    public void StartRecording()
    {
        RecordingTakeContext legacyTake =
            RecordingTakeContext.CreateLocal(
                "legacy-session",
                "legacy-sentence",
                "Legacy recording",
                1
            );

        TryStartRecording(legacyTake);
    }

    public bool TryStartRecording(RecordingTakeContext take)
    {
        LastError = string.Empty;
        if (isRecording)
        {
            LastError = "已有录制正在进行";
            return false;
        }

        if (sourceDataProvider == null)
        {
            LastError = "姿态数据源尚未就绪";
            return false;
        }

        if (!take.IsValid)
        {
            LastError = "录制任务参数无效";
            return false;
        }

        bool poseValid;
        int probeJointCount = 0;
        NativeArray<MSDKUtility.NativeTransform> poseProbe = default;
        try
        {
            poseProbe = sourceDataProvider.GetSkeletonPose();
            poseValid = sourceDataProvider.IsPoseValid();
            probeJointCount = poseProbe.IsCreated ? poseProbe.Length : 0;
        }
        catch (Exception exception)
        {
            LastError = "身体追踪刷新失败";
            Debug.LogError(
                "[MetaBodyMotionRecorder] Cannot refresh body tracking: " +
                exception
            );
            return false;
        }
        finally
        {
            if (poseProbe.IsCreated)
            {
                poseProbe.Dispose();
            }
        }
        bool useEditorSimulation =
            !poseValid && CanSimulateInvalidPoseInEditor;
        if (poseValid && probeJointCount <= 0)
        {
            poseValid = false;
        }
        if (!poseValid && !useEditorSimulation)
        {
            LastError = "身体追踪尚未就绪";
            Debug.LogWarning(
                "[MetaBodyMotionRecorder] " +
                "Body pose is not valid yet."
            );
            return false;
        }

        if (useEditorSimulation)
        {
            Debug.LogWarning(
                "[MetaBodyMotionRecorder] Starting an Editor simulation " +
                "Take without a valid body pose. Frames will contain zero " +
                "joints and pose_valid=false."
            );
        }

        handCaptureQuality = new SignVR.Recording.HandCaptureQualitySummary();

        ResolveSpatialMetadataProvider();
        RecordingSpatialSnapshot spatialSnapshot = null;
        string spatialError = string.Empty;
        if (spatialMetadataProvider == null ||
            !spatialMetadataProvider.TryCaptureValidatedSnapshot(
                take.StartedAtUtc,
                out spatialSnapshot,
                out spatialError))
        {
            LastError = string.IsNullOrWhiteSpace(spatialError)
                ? "固定视角尚未对齐"
                : spatialError;
            Debug.LogError(
                "[MetaBodyMotionRecorder] Cannot start the Take: " +
                LastError
            );
            return false;
        }

        string directory = Path.Combine(
            Application.persistentDataPath,
            "Recordings",
            take.SafeSessionId,
            take.SafeSentenceId
        );
        Debug.Log($"Motion saved to: {directory}");

        try
        {
            Directory.CreateDirectory(directory);

            outputPath = Path.Combine(
                directory,
                take.FileStem + ".pose.jsonl"
            );
            metadataPath = Path.Combine(
                directory,
                take.FileStem + ".meta.json"
            );

            writer = new AsyncPoseFileWriter(outputPath, 65536);
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is NotSupportedException ||
            exception is ArgumentException)
        {
            try
            {
                writer?.CloseAndWait();
            }
            catch (Exception disposeException)
            {
                Debug.LogWarning(
                    "[MetaBodyMotionRecorder] Failed to close the partial " +
                    "Take file: " + disposeException.Message
                );
            }
            writer = null;
            LastError = "无法创建录制文件，请检查存储空间";
            Debug.LogError(
                "[MetaBodyMotionRecorder] Cannot create Take files: " +
                exception
            );
            return false;
        }

        recordingStartTime =
            Time.realtimeSinceStartupAsDouble;

        nextSampleTime = recordingStartTime;

        stopTime = recordingDurationSeconds > 0f
            ? recordingStartTime + recordingDurationSeconds
            : double.PositiveInfinity;

        sampleIndex = 0;
        validPoseFrameCount = 0;
        isRecording = true;
        hasRecorded = true;
        currentTake = take;
        recordingStartedUtc = take.StartedAtUtc;
        spatialSnapshot.captured_utc = recordingStartedUtc.ToString("O");
        currentTakeUsesSimulatedPose = useEditorSimulation;
        expectedJointCount = useEditorSimulation ? 0 : probeJointCount;
        currentSpatialSnapshot = spatialSnapshot;

        Debug.Log(
            "[MetaBodyMotionRecorder] Recording started: " +
            outputPath
        );

        return true;
    }

    private void CaptureFrame(double now)
    {
        NativeArray<MSDKUtility.NativeTransform> pose = default;
        bool poseValid = false;
        if (!currentTakeUsesSimulatedPose)
        {
            pose = sourceDataProvider.GetSkeletonPose();
            poseValid = sourceDataProvider.IsPoseValid();
        }

        try
        {
            int jointCount = poseValid && pose.IsCreated
                ? pose.Length
                : 0;

            bool validPoseFrame = poseValid &&
                                  jointCount > 0 &&
                                  (expectedJointCount <= 0 ||
                                   jointCount == expectedJointCount);

            var positions =
                new Vector3Record[jointCount];

            var rotations =
                new QuaternionRecord[jointCount];

            var scales =
                new Vector3Record[jointCount];

            for (int i = 0; i < jointCount; i++)
            {
                MSDKUtility.NativeTransform joint = pose[i];

                positions[i] =
                    new Vector3Record(joint.Position);

                rotations[i] =
                    new QuaternionRecord(joint.Orientation);

                scales[i] =
                    new Vector3Record(joint.Scale);
            }

            var frame = new FrameRecord
            {
                sample_index = sampleIndex,
                unity_frame = Time.frameCount,

                monotonic_time = now,
                recording_time =
                    now - recordingStartTime,

                pose_valid = validPoseFrame,
                joint_count = jointCount,

                positions = positions,
                rotations = rotations,
                scales = scales,

                hand_capture = boundaryMonitor != null
                    ? boundaryMonitor.CurrentFrame
                    : default
            };

            if (boundaryMonitor != null)
            {
                handCaptureQuality.Accumulate(frame.hand_capture);
            }

            string jsonLine = JsonUtility.ToJson(frame, false);
            if (!writer.TryWriteLine(jsonLine))
            {
                throw new IOException(
                    "The asynchronous pose writer cannot accept more frames."
                );
            }

            if (validPoseFrame)
            {
                validPoseFrameCount++;
            }
            sampleIndex++;

        }
        finally
        {
            if (pose.IsCreated)
            {
                pose.Dispose();
            }
        }
    }

    public void StopRecording()
    {
        StopRecordingInternal("completed", string.Empty);
    }

    public void StopRecording(DateTime stoppedAtUtc)
    {
        StopRecordingInternal("completed", string.Empty, stoppedAtUtc);
    }

    public void StopRecordingAsInterrupted(DateTime? stoppedAtUtc = null)
    {
        StopRecordingInternal(
            "interrupted_by_retake",
            "reset_current_sentence",
            stoppedAtUtc
        );
    }

    private void StopRecordingInternal(
        string captureStatus,
        string resetReason,
        DateTime? stoppedAtUtc = null)
    {
        if (!isRecording)
        {
            return;
        }

        isRecording = false;
        DateTime resolvedStoppedAtUtc = stoppedAtUtc ?? EstimateHostUtcNow();
        recordingStoppedUtc = resolvedStoppedAtUtc.Kind == DateTimeKind.Utc
            ? resolvedStoppedAtUtc
            : resolvedStoppedAtUtc.ToUniversalTime();

        string finalCaptureStatus = captureStatus;
        string finalResetReason = resetReason;
        EvaluateCompletedTakeQuality(
            ref finalCaptureStatus,
            ref finalResetReason
        );

        try
        {
            writer?.CloseAndWait();
        }
        catch (Exception exception)
        {
            finalCaptureStatus = "io_error";
            finalResetReason = "pose_stream_flush_failed";
            LastError = "录制文件写入失败，请检查存储空间";
            Debug.LogError(
                "[MetaBodyMotionRecorder] Cannot flush the pose stream: " +
                exception
            );
        }
        finally
        {
            writer = null;
        }

        try
        {
            WriteTakeMetadata(finalCaptureStatus, finalResetReason);
        }
        catch (Exception exception)
        {
            finalCaptureStatus = "io_error";
            finalResetReason = "metadata_write_failed";
            LastError = "录制元数据写入失败，请检查存储空间";
            Debug.LogError(
                "[MetaBodyMotionRecorder] Cannot write Take metadata: " +
                exception
            );
        }

        RecordingFinalized?.Invoke(
            new RecordingArtifact(
                currentTake,
                outputPath,
                metadataPath,
                finalCaptureStatus
            )
        );

        Debug.Log(
            "[MetaBodyMotionRecorder] Recording stopped. " +
            $"Status: {finalCaptureStatus}. Samples: {sampleIndex}. " +
            $"File: {outputPath}"
        );
    }

    private DateTime EstimateHostUtcNow()
    {
        double elapsedSeconds = Math.Max(
            0d,
            Time.realtimeSinceStartupAsDouble - recordingStartTime
        );
        return recordingStartedUtc + TimeSpan.FromSeconds(elapsedSeconds);
    }

    public void StopRecordingForPassthrough()
    {
        StopRecordingInternal(
            "interrupted_passthrough",
            "passthrough_pause"
        );
    }

    private void EvaluateCompletedTakeQuality(
        ref string captureStatus,
        ref string resetReason)
    {
        if (!string.Equals(
                captureStatus,
                "completed",
                StringComparison.Ordinal))
        {
            return;
        }

        if (sampleIndex < Mathf.Max(1, minimumCompletedFrameCount))
        {
            captureStatus = "invalid_pose_quality";
            resetReason = "insufficient_pose_frames";
            LastError = "录制时间过短，请重录当前句";
            return;
        }

        float validRatio = sampleIndex > 0
            ? (float)validPoseFrameCount / sampleIndex
            : 0f;
        if (!currentTakeUsesSimulatedPose &&
            validRatio < Mathf.Clamp01(minimumValidPoseRatio))
        {
            captureStatus = "invalid_pose_quality";
            resetReason = "insufficient_valid_pose_ratio";
            LastError = "姿态追踪质量不足，请重录当前句";
        }
    }

    private void WriteTakeMetadata(
        string captureStatus,
        string resetReason)
    {
        handCaptureQuality.Finalize(
            boundaryMonitor == null || boundaryMonitor.GuidanceEnabled
        );

        float validPoseRatio = sampleIndex > 0
            ? (float)validPoseFrameCount / sampleIndex
            : 0f;
        spatialMetadataProvider?.RefreshFinalState(currentSpatialSnapshot);
        var metadata = new TakeMetadata
        {
            session_id = currentTake.SessionId,
            sentence_id = currentTake.SentenceId,
            sentence_text = currentTake.PromptText,
            take_id = currentTake.TakeId,
            take_index = currentTake.TakeIndex,
            capture_status = captureStatus,
            review_status = "candidate",
            reset_reason = resetReason,
            utc_started = recordingStartedUtc.ToString("O"),
            utc_stopped = recordingStoppedUtc.ToString("O"),
            pose_frame_count = sampleIndex,
            valid_pose_frame_count = validPoseFrameCount,
            valid_pose_ratio = validPoseRatio,
            expected_joint_count = expectedJointCount,
            pose_file = Path.GetFileName(outputPath),
            app_version = Application.version,
            device_model = SystemInfo.deviceModel,
            hand_capture_quality = handCaptureQuality,
            spatial_context = currentSpatialSnapshot ??
                              RecordingSpatialSnapshot.CreateUnavailable(
                                  "VRroom-world-v1",
                                  recordingStartedUtc
                              ),
            editor_simulation = Application.isEditor,
            pose_source_simulated = currentTakeUsesSimulatedPose
        };

        string temporaryMetadataPath = metadataPath + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryMetadataPath,
                JsonUtility.ToJson(metadata, true),
                new UTF8Encoding(false)
            );
            File.Move(temporaryMetadataPath, metadataPath);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryMetadataPath))
                {
                    File.Delete(temporaryMetadataPath);
                }
            }
            catch (Exception cleanupException)
            {
                Debug.LogWarning(
                    "[MetaBodyMotionRecorder] Could not remove the " +
                    "temporary metadata file: " + cleanupException.Message
                );
            }

            throw;
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            StopRecordingInternal(
                "interrupted_application_pause",
                "application_pause"
            );
        }
    }

    private void OnApplicationQuit()
    {
        StopRecordingInternal(
            "interrupted_application_quit",
            "application_quit"
        );
    }

    private void OnDestroy()
    {
        StopRecordingInternal(
            "interrupted_component_destroy",
            "component_destroy"
        );
    }

    private bool CanSimulateInvalidPoseInEditor
    {
        get
        {
#if UNITY_EDITOR
            return allowEditorSimulationWithoutPose;
#else
            return false;
#endif
        }
    }

    private void ResolveSpatialMetadataProvider()
    {
        if (spatialMetadataProvider != null)
        {
            return;
        }

        spatialMetadataProvider =
            GetComponent<RecordingSpatialMetadataProvider>() ??
            FindAnyObjectByType<RecordingSpatialMetadataProvider>(
                FindObjectsInactive.Include
            );
    }

    private void OnValidate()
    {
        sampleRate = Mathf.Clamp(sampleRate, 1, 120);
        minimumCompletedFrameCount = Mathf.Max(
            1,
            minimumCompletedFrameCount
        );
        minimumValidPoseRatio = Mathf.Clamp01(minimumValidPoseRatio);
    }
}
