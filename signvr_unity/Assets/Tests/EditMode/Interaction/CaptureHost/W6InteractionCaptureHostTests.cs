using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class W6InteractionCaptureHostTests
    {
        private const string DriverTypeName =
            "SignVR.Interaction.CaptureHost.W6InteractionCaptureHostTestDriver, " +
            "Assembly-CSharp";

        [Test]
        public void ManifestMatchesFrozenContract()
        {
            Invoke(nameof(ManifestMatchesFrozenContract));
        }

        [Test]
        public void FrozenRegistrationRetriesExactBytesAfter409()
        {
            Invoke(nameof(FrozenRegistrationRetriesExactBytesAfter409));
        }

        [Test]
        public void ConflictRecoveryRequiresMatchingSnapshot()
        {
            Invoke(nameof(ConflictRecoveryRequiresMatchingSnapshot));
        }

        [Test]
        public void HostNotReadyAndConflictNeverRedrawPlan()
        {
            Invoke(nameof(HostNotReadyAndConflictNeverRedrawPlan));
        }

        [Test]
        public void StudyReadinessRequiresMatchingFreshParticipant()
        {
            Invoke(nameof(StudyReadinessRequiresMatchingFreshParticipant));
        }

        [Test]
        public void StudyReadinessRejectsQuestIdCaseMismatch()
        {
            Invoke(nameof(StudyReadinessRejectsQuestIdCaseMismatch));
        }

        [Test]
        public void ScheduledStartGateBlocksPreStartCapture()
        {
            Invoke(nameof(ScheduledStartGateBlocksPreStartCapture));
        }

        [Test]
        public void PresentationHandshakeUsesActualPlaybackOrigin()
        {
            Invoke(nameof(PresentationHandshakeUsesActualPlaybackOrigin));
        }

        [Test]
        public void StudyCapturePrerequisitesFailClosed()
        {
            Invoke(nameof(StudyCapturePrerequisitesFailClosed));
        }

        [Test]
        public void CompleteAndAbortSealAllLocalFiles()
        {
            Invoke(nameof(CompleteAndAbortSealAllLocalFiles));
        }

        [Test]
        public void EventSequenceAndMonotonicTimeAreStrict()
        {
            Invoke(nameof(EventSequenceAndMonotonicTimeAreStrict));
        }

        [Test]
        public void EventSinkSeamWorksWithFakeAdapter()
        {
            Invoke(nameof(EventSinkSeamWorksWithFakeAdapter));
        }

        [Test]
        public void BufferOverflowEmitsCaptureGap()
        {
            Invoke(nameof(BufferOverflowEmitsCaptureGap));
        }

        [Test]
        public void EveryPhaseFlushFlushesAllStreams()
        {
            Invoke(nameof(EveryPhaseFlushFlushesAllStreams));
        }

        [Test]
        public void RestartDiscoversPendingRun()
        {
            Invoke(nameof(RestartDiscoversPendingRun));
        }

        [Test]
        public void RestartFlagsAbnormalPartialRunForRecovery()
        {
            Invoke(nameof(RestartFlagsAbnormalPartialRunForRecovery));
        }

        [Test]
        public void PartialRunCanBeTerminalizedAfterRestart()
        {
            Invoke(nameof(PartialRunCanBeTerminalizedAfterRestart));
        }

        [Test]
        public void AtomicBackupRecoversBeforeRead()
        {
            Invoke(nameof(AtomicBackupRecoversBeforeRead));
        }

        [Test]
        public void AckRequiresWebcamAndAllFiveArtifacts()
        {
            Invoke(nameof(AckRequiresWebcamAndAllFiveArtifacts));
        }

        [Test]
        public void AckStateNeverMutatesSealedArtifacts()
        {
            Invoke(nameof(AckStateNeverMutatesSealedArtifacts));
        }

        [Test]
        public void ArtifactRetrySnapshotStaysByteIdentical()
        {
            Invoke(nameof(ArtifactRetrySnapshotStaysByteIdentical));
        }

        [Test]
        public void FrozenArtifactsEnforceHostByteLimits()
        {
            Invoke(nameof(FrozenArtifactsEnforceHostByteLimits));
        }

        [Test]
        public void W3AccurateResponsesAndTerminalBodiesMatch()
        {
            Invoke(nameof(W3AccurateResponsesAndTerminalBodiesMatch));
        }

        [Test]
        public void QuestHeartbeatContractIsExplicit()
        {
            Invoke(nameof(QuestHeartbeatContractIsExplicit));
        }

        [Test]
        public void HeartbeatLifecycleHasOneRoutineAcrossPauseResume()
        {
            Invoke(nameof(HeartbeatLifecycleHasOneRoutineAcrossPauseResume));
        }

        [Test]
        public void ReadinessGenerationRejectsStaleCallbacks()
        {
            Invoke(nameof(ReadinessGenerationRejectsStaleCallbacks));
        }

        [Test]
        public void WriterWaitsHaveShortFiniteLimits()
        {
            Invoke(nameof(WriterWaitsHaveShortFiniteLimits));
        }

        [Test]
        public void LifecyclePolicyTerminalizesConsumedRuns()
        {
            Invoke(nameof(LifecyclePolicyTerminalizesConsumedRuns));
        }

        [Test]
        public void GeneratedFixtureCarriesW3StrictIdentity()
        {
            Invoke(nameof(GeneratedFixtureCarriesW3StrictIdentity));
        }

        [Test]
        public void PathsFailClosedAgainstTraversalAndIllegalIds()
        {
            Invoke(nameof(PathsFailClosedAgainstTraversalAndIllegalIds));
        }

        [Test]
        public void StudyRejectsDebugOverrides()
        {
            Invoke(nameof(StudyRejectsDebugOverrides));
        }

        [Test]
        public void SummaryKeepsAccuracyAndStuckSeparate()
        {
            Invoke(nameof(SummaryKeepsAccuracyAndStuckSeparate));
        }

        [Test]
        public void InstructionManifestReaderMapsExactArtifacts()
        {
            Invoke(nameof(InstructionManifestReaderMapsExactArtifacts));
        }

        private static void Invoke(string methodName)
        {
            Type driver = Type.GetType(DriverTypeName, throwOnError: true);
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method, Is.Not.Null, "Missing W6 scenario " + methodName);
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
