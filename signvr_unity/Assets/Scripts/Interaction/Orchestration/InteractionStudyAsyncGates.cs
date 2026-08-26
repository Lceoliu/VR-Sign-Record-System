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
}
