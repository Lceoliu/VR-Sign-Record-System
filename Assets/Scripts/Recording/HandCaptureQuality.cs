using System;

namespace SignVR.Recording
{
    /// <summary>
    /// Per-hand tracking quality for a single frame.
    ///
    /// This is written into the pose stream so a take can be judged on measured
    /// tracking quality instead of someone eyeballing the video, and so the
    /// boundary guidance can later be evaluated against real signing data.
    /// </summary>
    [Serializable]
    public struct HandCaptureSample
    {
        public bool tracked;
        public bool high_confidence;
        public float confidence;
        public bool inside_safe_zone;

        /// <summary>
        /// Normalized headroom before leaving the safe zone: 1 at the centre,
        /// 0 exactly on the boundary, negative once outside.
        /// </summary>
        public float boundary_margin;

        public string status;
    }

    [Serializable]
    public struct HandCaptureFrame
    {
        public HandCaptureSample left;
        public HandCaptureSample right;
    }

    /// <summary>
    /// Aggregated tracking quality for one take, stored in the take metadata so
    /// the operator console can flag takes that need a retake.
    /// </summary>
    [Serializable]
    public sealed class HandCaptureQualitySummary
    {
        public long frames;
        public long left_tracked_frames;
        public long right_tracked_frames;
        public long left_high_confidence_frames;
        public long right_high_confidence_frames;
        public long left_inside_frames;
        public long right_inside_frames;
        public bool guidance_enabled;

        public float left_tracked_ratio;
        public float right_tracked_ratio;
        public float left_high_confidence_ratio;
        public float right_high_confidence_ratio;
        public float left_inside_ratio;
        public float right_inside_ratio;

        /// <summary>Share of frames where both hands were tracked with high confidence and inside the safe zone.</summary>
        public float clean_ratio;

        public long clean_frames;

        public void Accumulate(HandCaptureFrame frame)
        {
            frames++;
            if (frame.left.tracked) left_tracked_frames++;
            if (frame.right.tracked) right_tracked_frames++;
            if (frame.left.high_confidence) left_high_confidence_frames++;
            if (frame.right.high_confidence) right_high_confidence_frames++;
            if (frame.left.inside_safe_zone) left_inside_frames++;
            if (frame.right.inside_safe_zone) right_inside_frames++;

            bool clean =
                frame.left.tracked && frame.right.tracked &&
                frame.left.high_confidence && frame.right.high_confidence &&
                frame.left.inside_safe_zone && frame.right.inside_safe_zone;
            if (clean) clean_frames++;
        }

        public void Finalize(bool guidanceEnabled)
        {
            guidance_enabled = guidanceEnabled;
            float total = frames <= 0 ? 1f : frames;
            left_tracked_ratio = left_tracked_frames / total;
            right_tracked_ratio = right_tracked_frames / total;
            left_high_confidence_ratio = left_high_confidence_frames / total;
            right_high_confidence_ratio = right_high_confidence_frames / total;
            left_inside_ratio = left_inside_frames / total;
            right_inside_ratio = right_inside_frames / total;
            clean_ratio = clean_frames / total;
        }
    }
}
