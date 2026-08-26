using System;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
            InvokeDriver("SuspendFailureStillDisablesAndResumeRetriesCleanup");
            InvokeDriver(
                "LifecycleAbortRetryIsBackedOffAndEventuallyConverges"
            );
            InvokeDriver("AcceptedAbortRetriesOnlyFailedTaskDisable");
            InvokeDriver(
                "SuspendedAcceptedAbortRetainsFailedDisableRetry"
            );
            InvokeDriver(
                "TerminalCleanupTakesOverPersistentlyFailedDisable"
            );
            InvokeDriver("ControllerPauseAndDisableUseTheSafeAbortPath");
            InvokeDriver("TerminalAdapterFailuresStillConvergeToPreStart");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealW5EndPhaseConvergesAfterCallbackFailure()
        {
            InvokeDriver(
                "RealW5EndPhaseCleansEveryResourceAfterCallbackFailure"
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealPointingCleanupSurvivesHitEndedFailure()
        {
            InvokeDriver("RealGhostPointingCleanupSurvivesHitEndedFailure");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThrowingDisableStillUnbindsLifecycleSubscriptions()
        {
            LogAssert.Expect(
                LogType.Exception,
                new Regex(
                    "injected controller OnDisable cleanup failure",
                    RegexOptions.Singleline
                )
            );
            LogAssert.Expect(
                LogType.Exception,
                new Regex(
                    "injected detector OnDisable cleanup failure",
                    RegexOptions.Singleline
                )
            );
            InvokeDriver("ThrowingDisableStillUnbindsControllerAndDetector");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealPortTerminalTickRetriesWithoutSideEffectReplay()
        {
            InvokeDriver(
                "RealPresentationPortTerminalTickRetriesWithoutReplay"
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator PresentationReplacementRejectsOnlyUnsettledCleanup()
        {
            InvokeDriver(
                "PresentationReplacementRejectsOnlyUnsettledCleanup"
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator PreviouslyReportedQuestInteractionRegressionsStayFixed()
        {
            InvokeDriver(
                "FixedStudyModeLeavesRuntimeTrackingOriginOwnedByXrRuntime"
            );
            InvokeDriver(
                "HeadLockedStudyUiInheritsHmdPoseWithoutLateWorldCopy"
            );

            Type pokeCanvasType = Type.GetType(
                "SignVR.SceneFlow.WorldSpacePokeCanvas, SignVR.SceneFlow",
                throwOnError: true
            );
            Type rayInteractableType = Type.GetType(
                "Oculus.Interaction.RayInteractable, Oculus.Interaction",
                throwOnError: true
            );
            Type pointableCanvasModuleType = Type.GetType(
                "Oculus.Interaction.PointableCanvasModule, Oculus.Interaction",
                throwOnError: true
            );
            GameObject existingEventSystem = EventSystem.current != null
                ? EventSystem.current.gameObject
                : null;
            Component existingCanvasModule =
                UnityEngine.Object.FindAnyObjectByType(
                    pointableCanvasModuleType
                ) as Component;
            var panel = new GameObject(
                "PlayMode Hand Ray Regression Panel",
                typeof(RectTransform),
                typeof(Canvas)
            );
            panel.SetActive(false);
            try
            {
                Component pokeCanvas = panel.AddComponent(pokeCanvasType);
                MethodInfo ensure = pokeCanvasType.GetMethod(
                    "EnsurePokeInteraction",
                    BindingFlags.Public | BindingFlags.Instance
                );

                Assert.That(ensure, Is.Not.Null);
                Assert.That(
                    (bool)ensure.Invoke(pokeCanvas, null),
                    Is.True
                );
                panel.SetActive(true);
                yield return null;

                Assert.That(
                    panel.GetComponent<GraphicRaycaster>(),
                    Is.Not.Null,
                    "The live world-space panel lost its UGUI raycaster."
                );
                Transform interaction = panel.transform.Find(
                    "ISDK_PokeCanvasInteraction"
                );
                Assert.That(interaction, Is.Not.Null);
                Assert.That(
                    interaction.GetComponent(rayInteractableType),
                    Is.Not.Null,
                    "The live study panel no longer accepts a hand ray."
                );
            }
            finally
            {
                UnityEngine.Object.Destroy(panel);
                if (existingEventSystem == null && EventSystem.current != null)
                {
                    UnityEngine.Object.Destroy(EventSystem.current.gameObject);
                }
                else if (existingCanvasModule == null &&
                    existingEventSystem != null)
                {
                    Component createdCanvasModule =
                        existingEventSystem.GetComponent(
                            pointableCanvasModuleType
                        );
                    if (createdCanvasModule != null)
                    {
                        UnityEngine.Object.Destroy(createdCanvasModule);
                    }
                }
            }
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
