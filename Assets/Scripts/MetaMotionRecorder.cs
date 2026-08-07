using System;
using System.IO;
using System.Text;
using Meta.XR.Movement;
using Meta.XR.Movement.Retargeting;
using Unity.Collections;
using UnityEngine;

public sealed class MetaBodyMotionRecorder : MonoBehaviour
{
    [Header("Source")]
    [SerializeField]
    private MetaSourceDataProvider sourceDataProvider;

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

    public bool IsRecording => isRecording;
    public long SampleCount => sampleIndex;
    public string CurrentOutputPath => outputPath;

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
        if (isRecording || sourceDataProvider == null)
        {
            return;
        }

        if (!sourceDataProvider.IsPoseValid())
        {
            Debug.LogWarning(
                "[MetaBodyMotionRecorder] " +
                "Body pose is not valid yet."
            );
            return;
        }

        string directory = Path.Combine(
            Application.persistentDataPath,
            "Recordings"
        );
        Debug.Log($"Motion saved to: {directory}");

        Directory.CreateDirectory(directory);

        string sessionName =
            "meta_body_" +
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        outputPath = Path.Combine(
            directory,
            sessionName + "_frames.jsonl"
        );

        writer = new StreamWriter(
            outputPath,
            false,
            new UTF8Encoding(false),
            65536
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

        Debug.Log(
            "[MetaBodyMotionRecorder] Recording started: " +
            outputPath
        );
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
                scales = scales
            };

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
        if (!isRecording)
        {
            return;
        }

        isRecording = false;

        writer?.Flush();
        writer?.Dispose();
        writer = null;

        Debug.Log(
            "[MetaBodyMotionRecorder] Recording stopped. " +
            $"Samples: {sampleIndex}. File: {outputPath}"
        );
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            StopRecording();
        }
    }

    private void OnApplicationQuit()
    {
        StopRecording();
    }

    private void OnDestroy()
    {
        StopRecording();
    }
}