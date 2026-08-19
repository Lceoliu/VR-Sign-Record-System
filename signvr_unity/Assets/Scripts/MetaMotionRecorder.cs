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

    private StreamWriter writer;
    private bool isRecording;
    private bool hasRecorded;

    private double appStartTime;
    private double recordingStartTime;
    private double nextSampleTime;
    private double stopTime;

    private long sampleIndex;
    private string outputPath;
    private string metadataPath;
    private DateTime recordingStartedUtc;
    private RecordingTakeContext currentTake;

    public bool IsRecording => isRecording;
    public long SampleCount => sampleIndex;
    public string CurrentOutputPath => outputPath;
    public string CurrentMetadataPath => metadataPath;
    public RecordingTakeContext CurrentTake => currentTake;
    public bool IsPoseReady =>
        sourceDataProvider != null &&
        sourceDataProvider.IsPoseValid();

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
        public string pose_file;
        public string app_version;
        public string device_model;
        public SignVR.Recording.HandCaptureQualitySummary hand_capture_quality;
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
            return;
        }

        double now = Time.realtimeSinceStartupAsDouble;

        if (!isRecording)
        {
            bool startDelayFinished =
                now - appStartTime >= startDelaySeconds;

            if (
                recordAutomatically &&
                !hasRecorded &&
                startDelayFinished &&
                sourceDataProvider.IsPoseValid()
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

        CaptureFrame(now);

        double interval = 1.0 / Math.Max(sampleRate, 1);
        nextSampleTime += interval;

        // Drop outdated sampling slots rather than writing duplicates.
        if (nextSampleTime < now - interval)
        {
            nextSampleTime = now + interval;
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
        if (isRecording || sourceDataProvider == null || !take.IsValid)
        {
            return false;
        }

        if (!sourceDataProvider.IsPoseValid())
        {
            Debug.LogWarning(
                "[MetaBodyMotionRecorder] " +
                "Body pose is not valid yet."
            );
            return false;
        }

        handCaptureQuality = new SignVR.Recording.HandCaptureQualitySummary();

        string directory = Path.Combine(
            Application.persistentDataPath,
            "Recordings",
            take.SafeSessionId,
            take.SafeSentenceId
        );
        Debug.Log($"Motion saved to: {directory}");

        Directory.CreateDirectory(directory);

        outputPath = Path.Combine(
            directory,
            take.FileStem + ".pose.jsonl"
        );
        metadataPath = Path.Combine(
            directory,
            take.FileStem + ".meta.json"
        );

        writer = new StreamWriter(
            new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                65536
            ),
            new UTF8Encoding(false),
            65536,
            false
        );

        recordingStartTime =
            Time.realtimeSinceStartupAsDouble;

        nextSampleTime = recordingStartTime;

        stopTime = recordingDurationSeconds > 0f
            ? recordingStartTime + recordingDurationSeconds
            : double.PositiveInfinity;

        sampleIndex = 0;
        isRecording = true;
        hasRecorded = true;
        currentTake = take;
        recordingStartedUtc = DateTime.UtcNow;

        Debug.Log(
            "[MetaBodyMotionRecorder] Recording started: " +
            outputPath
        );

        return true;
    }

    private void CaptureFrame(double now)
    {
        bool poseValid =
            sourceDataProvider.IsPoseValid();

        NativeArray<MSDKUtility.NativeTransform> pose =
            sourceDataProvider.GetSkeletonPose();

        try
        {
            int jointCount = pose.IsCreated
                ? pose.Length
                : 0;

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

                pose_valid = poseValid,
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

            writer.WriteLine(
                JsonUtility.ToJson(frame, false)
            );

            sampleIndex++;

            if (sampleIndex % sampleRate == 0)
            {
                writer.Flush();
            }
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

    public void StopRecordingAsInterrupted()
    {
        StopRecordingInternal(
            "interrupted_by_retake",
            "reset_current_sentence"
        );
    }

    private void StopRecordingInternal(
        string captureStatus,
        string resetReason)
    {
        if (!isRecording)
        {
            return;
        }

        isRecording = false;

        writer?.Flush();
        writer?.Dispose();
        writer = null;

        WriteTakeMetadata(captureStatus, resetReason);

        RecordingFinalized?.Invoke(
            new RecordingArtifact(
                currentTake,
                outputPath,
                metadataPath,
                captureStatus
            )
        );

        Debug.Log(
            "[MetaBodyMotionRecorder] Recording stopped. " +
            $"Samples: {sampleIndex}. File: {outputPath}"
        );
    }

    private void WriteTakeMetadata(
        string captureStatus,
        string resetReason)
    {
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
            utc_stopped = DateTime.UtcNow.ToString("O"),
            pose_frame_count = sampleIndex,
            pose_file = Path.GetFileName(outputPath),
            app_version = Application.version,
            device_model = SystemInfo.deviceModel,
            hand_capture_quality = handCaptureQuality
        };

        handCaptureQuality.Finalize(
            boundaryMonitor == null || boundaryMonitor.GuidanceEnabled
        );

        File.WriteAllText(
            metadataPath,
            JsonUtility.ToJson(metadata, true),
            new UTF8Encoding(false)
        );
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
}
