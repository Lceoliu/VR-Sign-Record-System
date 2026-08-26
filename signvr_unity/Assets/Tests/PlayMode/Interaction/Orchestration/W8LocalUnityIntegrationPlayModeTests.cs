using System;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace SignVR.Interaction.PlayMode.Tests.Orchestration
{
    public sealed class W8LocalUnityIntegrationPlayModeTests
    {
        private const string DriverTypeName =
            "SignVR.Interaction.Orchestration." +
            "W8InteractionStudyFlowTestDriver, Assembly-CSharp";

        [UnityTest]
        public IEnumerator SixPhaseFlowKeepsW1W5W6W7AuthorityBoundaries()
        {
            InvokeDriver("SixPhaseHappyPathAdvancesOnlyOnActualFirstFrames");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReplayGiveUpAndValidationDriftAreExactlyOnce()
        {
            InvokeDriver("ReplayUsesOneW6TokenAndCannotStartTwice");
            InvokeDriver("ValidationErrorsResynchronizeBeforeReplayGiveUp");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisableResumeUsesSafeAbortAndSingleResubscription()
        {
            InvokeDriver("SuspendUsesTheSameSafeAbortAndResumeResubscribesOnce");
            InvokeDriver("ControllerPauseAndDisableUseTheSafeAbortPath");
            InvokeDriver("TerminalAdapterFailuresStillConvergeToPreStart");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealControlsIdentityAndCaptureFailClosed()
        {
            InvokeDriver("IdentityIsPreStartArmedAndLocksAfterConsumption");
            InvokeDriver("ArmedIdentityInputsCannotDivergeFromW6Identity");
            InvokeDriver("RealInstructionControlsRouteReplayThroughW8Sink");
            InvokeDriver(
                "CaptureBindingRequiresRealMatchedMetaSourcesAndProbes"
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator SlowHostAndLifecycleReplacementRemainTransactional()
        {
            InvokeDriver("SlowReadinessResponseCannotBeStaledByPolling");
            InvokeDriver(
                "ControllerReconfigureFailurePreservesDependenciesAndSubscriptions"
            );
            InvokeDriver("ManifestFailureRetryAndDisableCancelAreSafe");
            yield return null;
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
