using System;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Streaming validation for the Recorder Pose JSONL contract. JSON decoding
    /// remains a Unity adapter concern; this class validates every decoded line
    /// and requires an explicit successful EOF transition.
    /// </summary>
    public sealed class InstructionPoseArtifactValidator
    {
        private bool hasTimestamp;
        private double lastRecordingTime;

        public int RecordCount { get; private set; }

        public int UsableFrameCount { get; private set; }

        public int ExpectedJointCount { get; private set; }

        public bool IsComplete { get; private set; }

        public double FirstUsableRecordingTime { get; private set; }

        public double LastUsableRecordingTime { get; private set; }

        public void AcceptFrame(
            double recordingTime,
            bool poseValid,
            int jointCount,
            int positionCount,
            int rotationCount,
            int scaleCount)
        {
            if (IsComplete)
            {
                throw new InvalidOperationException(
                    "No records may follow the validated end of file."
                );
            }

            if (double.IsNaN(recordingTime) ||
                double.IsInfinity(recordingTime) ||
                recordingTime < 0d)
            {
                throw new InvalidOperationException(
                    "recording_time must be a finite non-negative number."
                );
            }

            if (hasTimestamp && recordingTime < lastRecordingTime)
            {
                throw new InvalidOperationException(
                    "recording_time must be monotonic through the whole file."
                );
            }

            if (jointCount < 0 || positionCount != jointCount ||
                rotationCount != jointCount || scaleCount != jointCount)
            {
                throw new InvalidOperationException(
                    "Every Pose array length must exactly match joint_count."
                );
            }

            if (jointCount > 0)
            {
                if (ExpectedJointCount == 0)
                {
                    ExpectedJointCount = jointCount;
                }
                else if (jointCount != ExpectedJointCount)
                {
                    throw new InvalidOperationException(
                        "joint_count changed within one frozen Pose artifact."
                    );
                }
            }

            if (poseValid && jointCount == 0)
            {
                throw new InvalidOperationException(
                    "A pose_valid record must contain at least one joint."
                );
            }

            RecordCount++;
            hasTimestamp = true;
            lastRecordingTime = recordingTime;
            if (!poseValid)
            {
                return;
            }

            if (UsableFrameCount == 0)
            {
                FirstUsableRecordingTime = recordingTime;
            }
            LastUsableRecordingTime = recordingTime;
            UsableFrameCount++;
        }

        public void CompleteEof()
        {
            if (IsComplete)
            {
                throw new InvalidOperationException(
                    "The Pose artifact EOF was already validated."
                );
            }

            if (RecordCount == 0)
            {
                throw new InvalidOperationException(
                    "The Pose artifact is empty."
                );
            }

            if (UsableFrameCount == 0 || ExpectedJointCount == 0)
            {
                throw new InvalidOperationException(
                    "The Pose artifact contains no usable pose frame."
                );
            }

            IsComplete = true;
        }
    }
}
