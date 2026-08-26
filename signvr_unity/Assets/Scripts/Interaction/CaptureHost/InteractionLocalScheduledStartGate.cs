using System;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Converts a locally chosen UTC start into a monotonic deadline so clock
    /// adjustments cannot move presentation playback after a Run is sealed.
    /// </summary>
    public sealed class InteractionLocalScheduledStartGate
    {
        public InteractionLocalScheduledStartGate(
            DateTimeOffset startAtUtc,
            DateTimeOffset observedUtc,
            double observedMonotonicSeconds)
        {
            InteractionEventSequencer.ValidateFiniteNonNegative(
                observedMonotonicSeconds,
                nameof(observedMonotonicSeconds)
            );
            StartAtUtc = startAtUtc.ToUniversalTime();
            double delay = Math.Max(
                0d,
                (StartAtUtc - observedUtc.ToUniversalTime()).TotalSeconds
            );
            StartAtMonotonicSeconds = observedMonotonicSeconds + delay;
        }

        public DateTimeOffset StartAtUtc { get; }
        public double StartAtMonotonicSeconds { get; }

        public bool IsDue(double monotonicTimeSeconds)
        {
            InteractionEventSequencer.ValidateFiniteNonNegative(
                monotonicTimeSeconds,
                nameof(monotonicTimeSeconds)
            );
            return monotonicTimeSeconds >= StartAtMonotonicSeconds;
        }
    }
}
