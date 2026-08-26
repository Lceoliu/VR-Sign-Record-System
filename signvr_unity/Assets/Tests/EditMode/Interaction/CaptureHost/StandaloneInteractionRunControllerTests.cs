using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class StandaloneInteractionRunControllerTests
    {
        private const string DriverTypeName =
            "SignVR.Interaction.CaptureHost." +
            "StandaloneInteractionRunControllerTestDriver, Assembly-CSharp";

        [Test]
        public void StartupRecoveryInProgressBlocksStartUntilSuccess()
        {
            Invoke(nameof(StartupRecoveryInProgressBlocksStartUntilSuccess));
        }

        [Test]
        public void WriterInitializationSchedulesRunLocally()
        {
            Invoke(nameof(WriterInitializationSchedulesRunLocally));
        }

        [Test]
        public void CompletedSealIsTerminalWithoutUploadState()
        {
            Invoke(nameof(CompletedSealIsTerminalWithoutUploadState));
        }

        [Test]
        public void AbortedSealIsTerminalWithoutUploadState()
        {
            Invoke(nameof(AbortedSealIsTerminalWithoutUploadState));
        }

        [Test]
        public void ApplicationPauseSealsConsumedRunExactlyOnce()
        {
            Invoke(nameof(ApplicationPauseSealsConsumedRunExactlyOnce));
        }

        [Test]
        public void HeadsetUnmountSealsConsumedRunExactlyOnce()
        {
            Invoke(nameof(HeadsetUnmountSealsConsumedRunExactlyOnce));
        }

        [Test]
        public void PreStartHeadsetRemountRestoresLocalLifecycle()
        {
            Invoke(nameof(PreStartHeadsetRemountRestoresLocalLifecycle));
        }

        [Test]
        public void StartupRecoveryFailureBlocksStartAndPreservesEvidence()
        {
            Invoke(nameof(StartupRecoveryFailureBlocksStartAndPreservesEvidence));
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
                "Missing standalone Controller scenario " + methodName
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
