using System;
using System.Collections.Generic;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Allocation-free hot-path state for inferred ghost pointing. The caller
    /// supplies only already-resolved target IDs and a monotonic clock.
    /// </summary>
    public sealed class GhostPointingState
    {
        public const double LossGraceSeconds = 0.15d;

        private readonly HashSet<string> allowedTargetIds = new(
            StringComparer.Ordinal
        );

        private bool playbackActive;
        private string activeTargetId = string.Empty;
        private double exposureSeconds;
        private double lastMonotonicTime = double.NaN;
        private double lossDeadline = double.PositiveInfinity;

        public GhostPointingState(IEnumerable<string> targetIds)
        {
            ReplaceAllowlist(targetIds);
        }

        public event Action<string, double> HitStarted;
        public event Action<string, double> HitEnded;

        public bool PlaybackActive => playbackActive;

        public bool VisualVisible => activeTargetId.Length > 0;

        public string ActiveTargetId => activeTargetId;

        public double ExposureSeconds => exposureSeconds;

        public bool LossPending => !double.IsPositiveInfinity(lossDeadline);

        public void BeginPlayback(double monotonicTime)
        {
            if (playbackActive)
            {
                StopPlayback(monotonicTime);
            }
            else
            {
                AdvanceTo(monotonicTime);
            }

            playbackActive = true;
            lossDeadline = double.PositiveInfinity;
        }

        /// <summary>
        /// Returns true only for a current-phase legal target. A legal entry is
        /// exposed in this same call; there is deliberately no entry dwell.
        /// </summary>
        public bool ObserveHit(string targetId, double monotonicTime)
        {
            if (!playbackActive)
            {
                return false;
            }

            AdvanceTo(monotonicTime);
            if (string.IsNullOrEmpty(targetId) ||
                !allowedTargetIds.Contains(targetId))
            {
                BeginLossGrace(monotonicTime);
                return false;
            }

            if (string.Equals(
                    activeTargetId,
                    targetId,
                    StringComparison.Ordinal))
            {
                lossDeadline = double.PositiveInfinity;
                return true;
            }

            if (VisualVisible)
            {
                EndCurrentHit(monotonicTime);
            }

            activeTargetId = targetId;
            lossDeadline = double.PositiveInfinity;
            HitStarted?.Invoke(activeTargetId, monotonicTime);
            return true;
        }

        public void ObserveNoHit(double monotonicTime)
        {
            if (!playbackActive)
            {
                return;
            }

            AdvanceTo(monotonicTime);
            BeginLossGrace(monotonicTime);
        }

        public void Tick(double monotonicTime)
        {
            if (playbackActive)
            {
                AdvanceTo(monotonicTime);
            }
        }

        public void StopPlayback(double monotonicTime)
        {
            if (!playbackActive)
            {
                return;
            }

            AdvanceTo(monotonicTime);
            if (VisualVisible)
            {
                EndCurrentHit(monotonicTime);
            }

            playbackActive = false;
            lossDeadline = double.PositiveInfinity;
        }

        public void ResetPhase(
            IEnumerable<string> targetIds,
            double monotonicTime)
        {
            if (playbackActive)
            {
                StopPlayback(monotonicTime);
            }
            else
            {
                AdvanceTo(monotonicTime);
            }

            ReplaceAllowlist(targetIds);
            activeTargetId = string.Empty;
            exposureSeconds = 0d;
            lossDeadline = double.PositiveInfinity;
        }

        private void BeginLossGrace(double monotonicTime)
        {
            if (VisualVisible && double.IsPositiveInfinity(lossDeadline))
            {
                lossDeadline = monotonicTime + LossGraceSeconds;
            }
        }

        private void AdvanceTo(double monotonicTime)
        {
            ValidateTime(monotonicTime);
            if (double.IsNaN(lastMonotonicTime))
            {
                lastMonotonicTime = monotonicTime;
                return;
            }

            if (VisualVisible && !double.IsPositiveInfinity(lossDeadline) &&
                monotonicTime >= lossDeadline)
            {
                exposureSeconds += Math.Max(
                    0d,
                    lossDeadline - lastMonotonicTime
                );
                lastMonotonicTime = lossDeadline;
                EndCurrentHit(lossDeadline);
            }

            if (VisualVisible)
            {
                exposureSeconds += Math.Max(
                    0d,
                    monotonicTime - lastMonotonicTime
                );
            }

            lastMonotonicTime = monotonicTime;
        }

        private void EndCurrentHit(double monotonicTime)
        {
            string endedTargetId = activeTargetId;
            activeTargetId = string.Empty;
            lossDeadline = double.PositiveInfinity;
            HitEnded?.Invoke(endedTargetId, monotonicTime);
        }

        private void ReplaceAllowlist(IEnumerable<string> targetIds)
        {
            if (targetIds == null)
            {
                throw new ArgumentNullException(nameof(targetIds));
            }

            allowedTargetIds.Clear();
            foreach (string targetId in targetIds)
            {
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    throw new ArgumentException(
                        "Pointing target IDs cannot be empty.",
                        nameof(targetIds)
                    );
                }

                string normalized = targetId.Trim();
                if (!allowedTargetIds.Add(normalized))
                {
                    throw new ArgumentException(
                        "Pointing target IDs must be unique.",
                        nameof(targetIds)
                    );
                }
            }
        }

        private void ValidateTime(double monotonicTime)
        {
            if (double.IsNaN(monotonicTime) ||
                double.IsInfinity(monotonicTime) ||
                monotonicTime < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(monotonicTime));
            }

            if (!double.IsNaN(lastMonotonicTime) &&
                monotonicTime < lastMonotonicTime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTime),
                    "Pointing time must be monotonic."
                );
            }
        }
    }
}
