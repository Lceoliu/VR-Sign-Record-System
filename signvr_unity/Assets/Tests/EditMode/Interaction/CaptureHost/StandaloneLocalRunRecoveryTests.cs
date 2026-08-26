using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class StandaloneLocalRunRecoveryTests
    {
        private const string DriverTypeName =
            "SignVR.Interaction.CaptureHost." +
            "StandaloneLocalRunRecoveryTestDriver, Assembly-CSharp";

        [Test]
        public void SealedCompletedRunIsLocallyCompleteWithoutHostAck()
        {
            Invoke(nameof(SealedCompletedRunIsLocallyCompleteWithoutHostAck));
        }

        [Test]
        public void SealedAbortedRunIsLocallyCompleteWithoutHostAck()
        {
            Invoke(nameof(SealedAbortedRunIsLocallyCompleteWithoutHostAck));
        }

        [Test]
        public void StartupRecoverySealsOnlyUnsealedRunAndPreservesValidRows()
        {
            Invoke(nameof(StartupRecoverySealsOnlyUnsealedRunAndPreservesValidRows));
        }

        [Test]
        public void StartupRecoveryIsIdempotent()
        {
            Invoke(nameof(StartupRecoveryIsIdempotent));
        }

        [Test]
        public void EmptyRunDirectoryBeforeManifestPublicationIsPruned()
        {
            Invoke(nameof(EmptyRunDirectoryBeforeManifestPublicationIsPruned));
        }

        [Test]
        public void AtomicManifestTemporaryIsPublishedAndSealedAborted()
        {
            Invoke(nameof(AtomicManifestTemporaryIsPublishedAndSealedAborted));
        }

        [Test]
        public void SealedAbortedRunCleansKnownPartialResidueIdempotently()
        {
            Invoke(nameof(SealedAbortedRunCleansKnownPartialResidueIdempotently));
        }

        [Test]
        public void ManifestIdentityMismatchFailsClosedAndRetainsEvidence()
        {
            Invoke(nameof(ManifestIdentityMismatchFailsClosedAndRetainsEvidence));
        }

        [Test]
        public void CorruptManifestFailsClosedAndRetainsEvidence()
        {
            Invoke(nameof(CorruptManifestFailsClosedAndRetainsEvidence));
        }

        [Test]
        public void RecoveryWriteFailureFailsClosedAndRetainsEvidence()
        {
            Invoke(nameof(RecoveryWriteFailureFailsClosedAndRetainsEvidence));
        }

        [Test]
        public void RecoveryCoordinatorPublishesPollableStartupState()
        {
            Invoke(nameof(RecoveryCoordinatorPublishesPollableStartupState));
        }

        private static void Invoke(string methodName)
        {
            Type driver = Type.GetType(DriverTypeName, throwOnError: true);
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(
                method,
                Is.Not.Null,
                "Missing standalone recovery scenario " + methodName
            );
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
