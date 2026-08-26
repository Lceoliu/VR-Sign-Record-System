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
        private const string StandaloneDriverTypeName =
            "SignVR.Interaction.Orchestration." +
            "StandaloneInteractionStudyFlowUiTestDriver, Assembly-CSharp";

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
        public IEnumerator RecreatedControllersShareParticipantSessionAndUseIndependentRunIds()
        {
            yield return InvokeDriverCoroutine(
                StandaloneDriverTypeName,
                "RecreatedControllersShareApplicationSessionButNotRunId"
            );
        }

        [UnityTest]
        public IEnumerator WorldSpaceStartButtonGraphicRaycastAndPointerClickStartsExactlyOnce()
        {
            // Meta's live hand-ray source is device-owned and cannot be
            // deterministically synthesized in PC Play Mode. The topology
            // test above retains the RayInteractable/PointableCanvasModule
            // contract; this test covers the downstream UGUI click chain.
            yield return InvokeDriverCoroutine(
                StandaloneDriverTypeName,
                "WorldSpaceStartButtonRaycastClicksExactlyOnce"
            );
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SavedInteractionLabStartButtonConsumesOneRunAndStaysActive()
        {
            yield return InvokeDriverCoroutine(
                StandaloneDriverTypeName,
                "SavedInteractionLabStartButtonConsumesOneRun"
            );
        }
#endif

        [UnityTest]
        public IEnumerator FixedStudyPlayerRootResistsGravityAndVerticalDisturbanceAcrossFrames()
        {
            const int ObservedFrames = 12;
            const float PositionTolerance = 0.001f;
            const float ConfiguredHeight = 1.25f;

            var root = new GameObject("Fixed Study Player Multiframe Regression");
            root.SetActive(false);
            Transform origin = null;
            Transform head = null;
            Transform spawn = null;
            try
            {
                origin = new GameObject("XR Origin").transform;
                origin.SetParent(root.transform, false);
                head = new GameObject("Tracked Head").transform;
                head.SetParent(origin, false);
                head.localPosition = Vector3.up * 1.6f;

                spawn = new GameObject("Configured Study Spawn").transform;
                spawn.SetPositionAndRotation(
                    new Vector3(0.4f, ConfiguredHeight, -0.7f),
                    Quaternion.Euler(0f, 18f, 0f)
                );

                var body = root.AddComponent<Rigidbody>();
                body.useGravity = true;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                Type rigType = Type.GetType(
                    "VRPlayerRig, Assembly-CSharp",
                    throwOnError: true
                );
                Component rig = root.AddComponent(rigType);
                MethodInfo configureSceneReferences = rigType.GetMethod(
                    "ConfigureSceneReferences"
                ) ?? throw new MissingMethodException(
                    rigType.FullName,
                    "ConfigureSceneReferences"
                );
                MethodInfo setSpawnPoint = rigType.GetMethod(
                    "SetSpawnPoint"
                ) ?? throw new MissingMethodException(
                    rigType.FullName,
                    "SetSpawnPoint"
                );
                MethodInfo setRecordingMode = rigType.GetMethod(
                    "SetRecordingMode"
                ) ?? throw new MissingMethodException(
                    rigType.FullName,
                    "SetRecordingMode"
                );
                PropertyInfo spawnPosition = rigType.GetProperty(
                    "SpawnPosition"
                ) ?? throw new MissingMemberException(
                    rigType.FullName,
                    "SpawnPosition"
                );
                configureSceneReferences.Invoke(
                    rig,
                    new object[] { origin, head, null }
                );
                setSpawnPoint.Invoke(
                    rig,
                    new object[] { spawn, false }
                );
                setRecordingMode.Invoke(
                    rig,
                    new object[] { true }
                );
                root.SetActive(true);

                yield return new WaitForEndOfFrame();
                Vector3 expectedPosition = (Vector3)spawnPosition.GetValue(rig);
                Assert.That(
                    expectedPosition.y,
                    Is.EqualTo(ConfiguredHeight).Within(PositionTolerance),
                    "The fixture must exercise the configured study height."
                );

                for (int frame = 0; frame < ObservedFrames; frame++)
                {
                    root.transform.position += Vector3.down *
                        (0.04f + frame * 0.005f);
                    body.linearVelocity = Vector3.down * (2f + frame);

                    yield return new WaitForEndOfFrame();

                    Assert.That(
                        Vector3.Distance(
                            root.transform.position,
                            expectedPosition
                        ),
                        Is.LessThanOrEqualTo(PositionTolerance),
                        $"The fixed Study player drifted or fell on frame {frame}."
                    );
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
                if (spawn != null)
                {
                    UnityEngine.Object.Destroy(spawn.gameObject);
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator HeadLockedStudyUiKeepsOneLocalPoseAcrossMovingHmdFrames()
        {
            const int ObservedFrames = 12;
            const float PositionTolerance = 0.0001f;
            const float RotationToleranceDegrees = 0.01f;
            var root = new GameObject("Head Locked UI Multiframe Regression");
            try
            {
                Transform hmd = new GameObject("Moving HMD").transform;
                hmd.SetParent(root.transform, false);
                Transform anchor = new GameObject("Interaction UI Anchor")
                    .transform;
                anchor.SetParent(root.transform, false);
                Type controlsType = Type.GetType(
                    "SignVR.Interaction.Presentation." +
                    "InteractionInstructionControls, Assembly-CSharp",
                    throwOnError: true
                );
                Component controls = anchor.gameObject.AddComponent(
                    controlsType
                );
                MethodInfo configureHmd = controlsType.GetMethod(
                    "ConfigureHmd"
                ) ?? throw new MissingMethodException(
                    controlsType.FullName,
                    "ConfigureHmd"
                );
                configureHmd.Invoke(
                    controls,
                    new object[] { hmd }
                );

                Vector3 expectedLocalPosition = new(0f, -0.22f, 0.72f);
                Quaternion expectedLocalRotation = Quaternion.LookRotation(
                    expectedLocalPosition.normalized,
                    Vector3.up
                );

                for (int frame = 0; frame < ObservedFrames; frame++)
                {
                    float alternatingHeight = frame % 2 == 0 ? 1.42f : 1.78f;
                    hmd.SetPositionAndRotation(
                        new Vector3(
                            frame * 0.025f,
                            alternatingHeight,
                            -1.2f + frame * 0.01f
                        ),
                        Quaternion.Euler(
                            -4f + frame * 0.7f,
                            20f + frame * 5f,
                            frame % 2 == 0 ? -2f : 3f
                        )
                    );

                    yield return new WaitForEndOfFrame();

                    Assert.That(
                        anchor.parent,
                        Is.SameAs(hmd),
                        $"The Study UI detached from the HMD on frame {frame}."
                    );
                    Assert.That(
                        Vector3.Distance(
                            anchor.localPosition,
                            expectedLocalPosition
                        ),
                        Is.LessThanOrEqualTo(PositionTolerance),
                        $"The Study UI alternated local height on frame {frame}."
                    );
                    Assert.That(
                        Quaternion.Angle(
                            anchor.localRotation,
                            expectedLocalRotation
                        ),
                        Is.LessThanOrEqualTo(RotationToleranceDegrees),
                        $"The Study UI local rotation drifted on frame {frame}."
                    );
                    Assert.That(
                        Vector3.Distance(
                            anchor.position,
                            hmd.TransformPoint(expectedLocalPosition)
                        ),
                        Is.LessThanOrEqualTo(PositionTolerance),
                        $"The Study UI did not inherit the HMD pose on frame {frame}."
                    );
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator StandaloneControlsAndCaptureFailClosed()
        {
            InvokeDriver("RealInstructionControlsRouteReplayThroughW8Sink");
            InvokeDriver(
                "CaptureBindingRequiresRealMatchedMetaSourcesAndProbes"
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator ManifestAndLifecycleReplacementRemainTransactional()
        {
            InvokeDriver(
                "ControllerReconfigureFailurePreservesDependenciesAndSubscriptions"
            );
            InvokeDriver("ManifestFailureRetryAndDisableCancelAreSafe");
            yield return null;
        }

        private static void InvokeDriver(string methodName)
        {
            InvokeDriver(DriverTypeName, methodName);
        }

        private static void InvokeDriver(
            string driverTypeName,
            string methodName)
        {
            Type driver = Type.GetType(driverTypeName, throwOnError: true);
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

        private static IEnumerator InvokeDriverCoroutine(
            string driverTypeName,
            string methodName)
        {
            Type driver = Type.GetType(driverTypeName, throwOnError: true);
            MethodInfo method = driver.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            ) ?? throw new MissingMethodException(driver.FullName, methodName);
            try
            {
                return method.Invoke(null, null) as IEnumerator ??
                    throw new InvalidOperationException(
                        driver.FullName + "." + methodName +
                        " did not return IEnumerator."
                    );
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
