using System;

namespace SignVR.Interaction.Orchestration
{
    /// <summary>
    /// Generation gate for a single cancellable asynchronous operation.
    /// A completion from an invalidated generation is deliberately stale.
    /// </summary>
    internal sealed class InteractionStudyOperationGate
    {
        private long generation;

        public bool InFlight { get; private set; }

        public bool TryBegin(out long token)
        {
            if (InFlight)
            {
                token = generation;
                return false;
            }
            generation = checked(generation + 1L);
            InFlight = true;
            token = generation;
            return true;
        }

        public bool TryComplete(long token)
        {
            if (!InFlight || token != generation)
            {
                return false;
            }
            InFlight = false;
            return true;
        }

        public void Invalidate()
        {
            generation = checked(generation + 1L);
            InFlight = false;
        }
    }

    /// <summary>
    /// One-at-a-time readiness poll gate. The interval starts at completion,
    /// so a slow request cannot be made stale by a newer periodic request.
    /// </summary>
    internal sealed class InteractionStudyReadinessPollGate
    {
        private readonly double intervalSeconds;
        private readonly InteractionStudyOperationGate operation = new();
        private double nextAllowedAt = double.NegativeInfinity;

        public InteractionStudyReadinessPollGate(double intervalSeconds)
        {
            if (double.IsNaN(intervalSeconds) ||
                double.IsInfinity(intervalSeconds) || intervalSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(intervalSeconds)
                );
            }
            this.intervalSeconds = intervalSeconds;
        }

        public bool InFlight => operation.InFlight;

        public bool TryBegin(double now, out long token)
        {
            ValidateTime(now);
            if (now < nextAllowedAt)
            {
                token = 0L;
                return false;
            }
            return operation.TryBegin(out token);
        }

        public bool TryComplete(long token, double now)
        {
            ValidateTime(now);
            if (!operation.TryComplete(token))
            {
                return false;
            }
            nextAllowedAt = now + intervalSeconds;
            return true;
        }

        public void Invalidate()
        {
            operation.Invalidate();
            nextAllowedAt = double.NegativeInfinity;
        }

        private static void ValidateTime(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }
}
