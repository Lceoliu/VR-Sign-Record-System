using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests.Orchestration
{
    public sealed class StandaloneInteractionStudyFlowUiTests
    {
        private const string DriverTypeName =
            "SignVR.Interaction.Orchestration." +
            "StandaloneInteractionStudyFlowUiTestDriver, Assembly-CSharp";

        [Test]
        public void ParticipantSessionIsCreatedOncePerApplicationLaunch()
        {
            InvokeDriver(nameof(
                ParticipantSessionIsCreatedOncePerApplicationLaunch));
        }

        [Test]
        public void AutomaticSessionIdentityIsAppliedToRunController()
        {
            InvokeDriver(nameof(
                AutomaticSessionIdentityIsAppliedToRunController));
        }

        [Test]
        public void SuccessiveRunsKeepTheApplicationParticipantIdentity()
        {
            InvokeDriver(nameof(
                SuccessiveRunsKeepTheApplicationParticipantIdentity));
        }

        [Test]
        public void SuccessiveRunsUseIndependentRunIds()
        {
            InvokeDriver(nameof(SuccessiveRunsUseIndependentRunIds));
        }

        [Test]
        public void SuccessfulStartDoesNotReturnImmediatelyToPreStart()
        {
            InvokeDriver(nameof(
                SuccessfulStartDoesNotReturnImmediatelyToPreStart));
        }

        [Test]
        public void TerminalCleanupRestoresPreStartExactlyOnce()
        {
            InvokeDriver(nameof(TerminalCleanupRestoresPreStartExactlyOnce));
        }

        [Test]
        public void ResultPageRoutesCompletedAndAbortedAcknowledgement()
        {
            InvokeDriver(nameof(
                ResultPageRoutesCompletedAndAbortedAcknowledgement));
        }

        [Test]
        public void StartupRecoveryBlocksStartUntilItCompletes()
        {
            InvokeDriver(nameof(StartupRecoveryBlocksStartUntilItCompletes));
        }

        [Test]
        public void LegacyIdentityWidgetsAreAbsentFromStandaloneControls()
        {
            InvokeDriver(nameof(LegacyIdentityWidgetsAreAbsentFromStandaloneControls));
        }

        [Test]
        public void LegacyConfigureSignatureRemainsAvailable()
        {
            InvokeDriver(nameof(LegacyConfigureSignatureRemainsAvailable));
        }

        [Test]
        public void StandaloneControlsComposeWithoutIdentityWidgets()
        {
            InvokeDriver(nameof(StandaloneControlsComposeWithoutIdentityWidgets));
        }

        [Test]
        public void StartButtonIsTheOnlyParticipantAction()
        {
            InvokeDriver(nameof(StartButtonIsTheOnlyParticipantAction));
        }

        [Test]
        public void RecoveringMessageIsStable()
        {
            InvokeDriver(nameof(RecoveringMessageIsStable));
        }

        [Test]
        public void RecoveryFailureMessageIsStable()
        {
            InvokeDriver(nameof(RecoveryFailureMessageIsStable));
        }

        [Test]
        public void ReadyMessageIsStable()
        {
            InvokeDriver(nameof(ReadyMessageIsStable));
        }

        [Test]
        public void PauseAbortedMessageIsStableAndConsumedOnce()
        {
            InvokeDriver(nameof(PauseAbortedMessageIsStableAndConsumedOnce));
        }

        [Test]
        public void ParticipantMessagesDoNotExposeRemovedServices()
        {
            InvokeDriver(nameof(ParticipantMessagesDoNotExposeRemovedServices));
        }

        [Test]
        public void StandaloneStudyRequiresStrictXrGate()
        {
            InvokeDriver(nameof(StandaloneStudyRequiresStrictXrGate));
        }

        [Test]
        public void EngineeringLocalBypassesStrictXrGate()
        {
            InvokeDriver(nameof(EngineeringLocalBypassesStrictXrGate));
        }

        private static void InvokeDriver(string methodName)
        {
            Type driver = Type.GetType(DriverTypeName, throwOnError: true);
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            ) ?? throw new MissingMethodException(driver.FullName, methodName);
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(
                    exception.InnerException ?? exception
                ).Throw();
                throw;
            }
        }
    }
}
