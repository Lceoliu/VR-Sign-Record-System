#if UNITY_INCLUDE_TESTS
namespace SignVR.Interaction.PhaseAdapters
{
    /// <summary>
    /// Test-only observation of a subscriber handler invocation. This type is
    /// excluded from Study Player builds and exposes no publisher internals.
    /// </summary>
    public sealed class InteractionSubscriptionDiagnostic
    {
        public int InvocationCount { get; private set; }

        public int AvailabilityChangedCount { get; private set; }

        public int ResetPerformedCount { get; private set; }

        public int RunConfiguredCount { get; private set; }

        public int RunResetCount { get; private set; }

        public int ResultProducedCount { get; private set; }

        internal void RecordInvocation()
        {
            InvocationCount++;
        }

        internal void RecordAvailabilityChanged()
        {
            AvailabilityChangedCount++;
            InvocationCount++;
        }

        internal void RecordResetPerformed()
        {
            ResetPerformedCount++;
        }

        internal void RecordRunConfigured()
        {
            RunConfiguredCount++;
        }

        internal void RecordRunReset()
        {
            RunResetCount++;
        }

        internal void RecordResultProduced()
        {
            ResultProducedCount++;
            InvocationCount++;
        }
    }
}
#endif
