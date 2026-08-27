#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;
using SignVR.Interaction.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace SignVR.Interaction.Orchestration
{
    public static class StandaloneInteractionStudyFlowUiTestDriver
    {
        private static readonly DateTimeOffset SessionTime =
            new DateTimeOffset(
                2026,
                8,
                27,
                8,
                0,
                0,
                TimeSpan.Zero
            );

        public static void ParticipantSessionIsCreatedOncePerApplicationLaunch()
        {
            GameObject owner = NewInactiveOwner("Standalone Session Once");
            try
            {
                var controller =
                    owner.AddComponent<InteractionStudyFlowController>();
                int factoryCalls = 0;
                ParticipantSession first =
                    controller.EnsureParticipantSessionForTests(() =>
                    {
                        factoryCalls++;
                        return NewSession(
                            "11111111-2222-3333-4444-555555555555"
                        );
                    });
                ParticipantSession second =
                    controller.EnsureParticipantSessionForTests(() =>
                    {
                        factoryCalls++;
                        return NewSession(
                            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
                        );
                    });

                Require(factoryCalls == 1, "Session factory ran more than once.");
                Require(
                    ReferenceEquals(first, second),
                    "Composition replaced the application Participant Session."
                );
                Require(
                    string.Equals(
                        first.ParticipantId,
                        controller.ParticipantSessionId,
                        StringComparison.Ordinal
                    ),
                    "Controller did not expose the application identity."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void AutomaticSessionIdentityIsAppliedToRunController()
        {
            GameObject owner = NewInactiveOwner("Standalone Automatic Identity");
            try
            {
                var run = owner.AddComponent<InteractionRunController>();
                var presentation =
                    owner.AddComponent<InstructionPresentationController>();
                var tasks = owner.AddComponent<
                    SignVR.Interaction.PhaseAdapters
                        .InteractionPhaseCoordinator>();
                var capture =
                    owner.AddComponent<InteractionStudyCaptureBinding>();
                var controller =
                    owner.AddComponent<InteractionStudyFlowController>();
                controller.Configure(run, presentation, tasks, capture);
                ParticipantSession session = NewSession(
                    "11111111-2222-3333-4444-555555555555"
                );

                controller.ApplyAutomaticIdentityForTests(() => session);

                Require(controller.IdentityReady, "Automatic identity was not ready.");
                Require(
                    string.Equals(
                        run.ParticipantId,
                        session.ParticipantId,
                        StringComparison.Ordinal
                    ),
                    "RunController did not receive the Participant Session ID."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void SuccessiveRunsKeepTheApplicationParticipantIdentity()
        {
            var fixture = new FlowFixture();
            GameObject owner = NewInactiveOwner("Standalone Repeated Runs");
            try
            {
                var controller =
                    owner.AddComponent<InteractionStudyFlowController>();
                ParticipantSession session = NewSession(
                    "11111111-2222-3333-4444-555555555555"
                );
                controller.InstallStandaloneStateForTests(
                    fixture.Flow,
                    manifestIsReady: true,
                    recoveryIsComplete: true,
                    session: session
                );

                string firstIdentity = controller.ParticipantSessionId;
                Require(controller.TryStart().Succeeded, "First Start failed.");
                fixture.Run.MarkCompleted();
                fixture.Flow.Tick();
                Require(controller.TryStart().Succeeded, "Second Start failed.");

                Require(
                    string.Equals(
                        firstIdentity,
                        controller.ParticipantSessionId,
                        StringComparison.Ordinal
                    ),
                    "A second Run replaced the application participant_id."
                );
                Require(
                    controller.IdentityReady,
                    "Automatic identity was consumed after Start."
                );
                Require(fixture.Run.StartCount == 2, "Expected two Runs.");
            }
            finally
            {
                fixture.Flow.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void SuccessiveRunsUseIndependentRunIds()
        {
            var fixture = new FlowFixture();
            Require(fixture.Flow.TryStart().Succeeded, "First Start failed.");
            string firstRunId = fixture.Run.Plan.RunId;
            fixture.Run.MarkCompleted();
            fixture.Flow.Tick();
            Require(fixture.Flow.TryStart().Succeeded, "Second Start failed.");
            string secondRunId = fixture.Run.Plan.RunId;

            Require(
                !string.Equals(
                    firstRunId,
                    secondRunId,
                    StringComparison.Ordinal
                ),
                "Successive Runs reused run_id."
            );
            fixture.Flow.Dispose();
        }

        public static IEnumerator RecreatedControllersShareApplicationSessionButNotRunId()
        {
            Require(
                Application.isPlaying,
                "This regression must exercise the live application session."
            );

            GameObject firstOwner = null;
            GameObject secondOwner = null;
            var firstFixture = new FlowFixture(
                Guid.Parse("10101010-1111-2222-3333-444444444444")
            );
            var secondFixture = new FlowFixture(
                Guid.Parse("20202020-1111-2222-3333-444444444444")
            );
            try
            {
                firstOwner = NewInactiveOwner(
                    "PlayMode First Application Session Controller"
                );
                var firstController = firstOwner.AddComponent<
                    InteractionStudyFlowController>();
                ParticipantSession firstSession =
                    firstController.EnsureParticipantSessionForTests(
                        () => NewSession(
                            "31313131-2222-3333-4444-555555555555"
                        )
                    );
                firstFixture.Run.ParticipantId = firstSession.ParticipantId;
                firstController.InstallStandaloneStateForTests(
                    firstFixture.Flow,
                    manifestIsReady: true,
                    recoveryIsComplete: true,
                    session: firstSession
                );
                firstOwner.SetActive(true);

                Require(
                    firstController.TryStart().Succeeded,
                    "The first reconstructed Controller could not start."
                );
                string participantId = firstController.ParticipantSessionId;
                string firstRunId = firstFixture.Run.Plan.RunId;

                UnityEngine.Object.Destroy(firstOwner);
                yield return null;
                Require(
                    firstOwner == null,
                    "The first Controller was not destroyed before recreation."
                );
                firstOwner = null;

                secondOwner = NewInactiveOwner(
                    "PlayMode Recreated Application Session Controller"
                );
                var secondController = secondOwner.AddComponent<
                    InteractionStudyFlowController>();
                int replacementFactoryCalls = 0;
                ParticipantSession secondSession =
                    secondController.EnsureParticipantSessionForTests(() =>
                    {
                        replacementFactoryCalls++;
                        return NewSession(
                            "41414141-2222-3333-4444-555555555555"
                        );
                    });
                secondFixture.Run.ParticipantId = secondSession.ParticipantId;
                secondController.InstallStandaloneStateForTests(
                    secondFixture.Flow,
                    manifestIsReady: true,
                    recoveryIsComplete: true,
                    session: secondSession
                );
                secondOwner.SetActive(true);

                Require(
                    secondController.TryStart().Succeeded,
                    "The recreated Controller could not start."
                );
                string secondRunId = secondFixture.Run.Plan.RunId;

                Require(
                    replacementFactoryCalls == 0,
                    "Recreating the Controller replaced the application " +
                        "Participant Session."
                );
                Require(
                    ReferenceEquals(firstSession, secondSession),
                    "Recreated Controllers did not share the same " +
                        "ParticipantSession instance."
                );
                Require(
                    string.Equals(
                        participantId,
                        secondController.ParticipantSessionId,
                        StringComparison.Ordinal
                    ),
                    "Recreated Controllers did not reuse participant_id."
                );
                Require(
                    string.Equals(
                        firstFixture.Run.Plan.ParticipantId,
                        secondFixture.Run.Plan.ParticipantId,
                        StringComparison.Ordinal
                    ) && string.Equals(
                        firstFixture.Run.Plan.ParticipantId,
                        participantId,
                        StringComparison.Ordinal
                    ),
                    "Independent Runs did not retain the application " +
                        "participant_id."
                );
                Require(
                    !string.IsNullOrWhiteSpace(firstRunId) &&
                        !string.IsNullOrWhiteSpace(secondRunId),
                    "An independently started Run did not receive run_id."
                );
                Require(
                    !string.Equals(
                        firstRunId,
                        secondRunId,
                        StringComparison.Ordinal
                    ),
                    "Independent Runs reused run_id across Controllers."
                );

                UnityEngine.Object.Destroy(secondOwner);
                yield return null;
                secondOwner = null;
            }
            finally
            {
                if (firstOwner != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstOwner);
                }
                if (secondOwner != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondOwner);
                }
            }
        }

        public static void SuccessfulStartDoesNotReturnImmediatelyToPreStart()
        {
            var fixture = new FlowFixture();
            GameObject owner = NewInactiveOwner("Standalone Start State");
            try
            {
                var controller =
                    owner.AddComponent<InteractionStudyFlowController>();
                controller.InstallStandaloneStateForTests(
                    fixture.Flow,
                    manifestIsReady: true,
                    recoveryIsComplete: true,
                    session: NewSession(
                        "11111111-2222-3333-4444-555555555555"
                    )
                );

                Require(controller.TryStart().Succeeded, "Start was rejected.");
                Require(
                    controller.Snapshot.RunState == RunState.Scheduled,
                    "Accepted Start flashed back to PreStart."
                );
                Require(
                    fixture.Run.StartCount == 1,
                    "Start was not one action; observed " +
                        fixture.Run.StartCount + " Run starts."
                );
            }
            finally
            {
                fixture.Flow.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void TerminalCleanupRestoresPreStartExactlyOnce()
        {
            var fixture = new FlowFixture();
            Require(fixture.Flow.TryStart().Succeeded, "Start was rejected.");
            fixture.Run.MarkCompleted();

            fixture.Flow.Tick();
            fixture.Flow.Tick();

            Require(
                fixture.Run.State == RunState.PreStart,
                "Terminal cleanup did not restore PreStart."
            );
            Require(
                fixture.Run.ResetCount == 1,
                "Terminal cleanup restored PreStart more than once."
            );
            fixture.Flow.Dispose();
        }

        public static void StartupRecoveryBlocksStartUntilItCompletes()
        {
            var fixture = new FlowFixture();
            GameObject owner = NewInactiveOwner("Standalone Recovery Gate");
            try
            {
                var controller =
                    owner.AddComponent<InteractionStudyFlowController>();
                controller.InstallStandaloneStateForTests(
                    fixture.Flow,
                    manifestIsReady: true,
                    recoveryIsComplete: false,
                    session: NewSession(
                        "11111111-2222-3333-4444-555555555555"
                    )
                );

                Require(
                    !controller.TryStart().Succeeded,
                    "Start bypassed startup recovery."
                );
                Require(fixture.Run.StartCount == 0, "Blocked Start consumed a Run.");
                controller.SetRecoveryStateForTests(
                    complete: true,
                    failure: null
                );
                Require(
                    controller.TryStart().Succeeded,
                    "Start remained blocked after successful recovery."
                );
            }
            finally
            {
                fixture.Flow.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void LegacyIdentityWidgetsAreAbsentFromStandaloneControls()
        {
            Type controls = typeof(InteractionStudyFlowControls);
            foreach (string fieldName in new[]
                     {
                         "participantIdInput",
                         "buildIdentityInput",
                         "applyIdentityButton"
                     })
            {
                Require(
                    controls.GetField(
                        fieldName,
                        BindingFlags.Instance | BindingFlags.NonPublic
                    ) == null,
                    "Standalone Controls still serialize " + fieldName + "."
                );
            }
            Require(
                controls.GetProperty("ParticipantIdInput") == null &&
                controls.GetProperty("BuildIdentityInput") == null &&
                controls.GetProperty("ApplyIdentityButton") == null,
                "Standalone Controls still expose legacy identity widgets."
            );
        }

        public static void StandaloneControlsComposeWithoutIdentityWidgets()
        {
            var fixture = new FlowFixture();
            GameObject root = NewInactiveOwner("Standalone No Identity UI");
            try
            {
                InteractionStudyFlowController controller =
                    NewReadyController(root.transform, fixture.Flow);
                InteractionStudyFlowControls controls =
                    root.AddComponent<InteractionStudyFlowControls>();
                var instruction =
                    root.AddComponent<InteractionInstructionControls>();

                controls.Configure(
                    controller,
                    instruction,
                    NewChild(root.transform, "Surface"),
                    NewUiComponent<Button>(root.transform, "Start"),
                    status: NewUiComponent<TextMeshProUGUI>(
                        root.transform,
                        "Status"
                    ),
                    progress: NewUiComponent<TextMeshProUGUI>(
                        root.transform,
                        "Progress"
                    )
                );
            }
            finally
            {
                fixture.Flow.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void StartButtonIsTheOnlyParticipantAction()
        {
            var fixture = new FlowFixture();
            GameObject root = NewInactiveOwner("Standalone One Action UI");
            try
            {
                InteractionStudyFlowController controller =
                    NewReadyController(root.transform, fixture.Flow);
                InteractionStudyFlowControls controls =
                    root.AddComponent<InteractionStudyFlowControls>();
                var instruction =
                    root.AddComponent<InteractionInstructionControls>();
                GameObject surface = NewChild(root.transform, "Surface");
                Button start = NewUiComponent<Button>(root.transform, "Start");
                root.SetActive(true);
                controls.Configure(
                    controller,
                    instruction,
                    surface,
                    start,
                    status: NewUiComponent<TextMeshProUGUI>(
                        root.transform,
                        "Status"
                    ),
                    progress: NewUiComponent<TextMeshProUGUI>(
                        root.transform,
                        "Progress"
                    )
                );

                Require(start.interactable, "Ready Start button is disabled.");
                start.onClick.Invoke();

                Require(
                    fixture.Run.StartCount == 1,
                    "Start was not one action; observed " +
                        fixture.Run.StartCount + " Run starts."
                );
                Require(!surface.activeSelf, "Start panel flashed back to PreStart.");
            }
            finally
            {
                fixture.Flow.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static IEnumerator WorldSpaceStartButtonRaycastClicksExactlyOnce()
        {
            var fixture = new FlowFixture();
            GameObject canvasOwner = null;
            GameObject cameraOwner = null;
            GameObject eventSystemOwner = null;
            try
            {
                EventSystem eventSystem = EventSystem.current;
                if (eventSystem == null)
                {
                    eventSystemOwner = new GameObject(
                        "PlayMode Study EventSystem",
                        typeof(EventSystem)
                    );
                    eventSystem = eventSystemOwner.GetComponent<EventSystem>();
                }

                cameraOwner = new GameObject(
                    "PlayMode Study UI Camera",
                    typeof(Camera)
                );
                Camera eventCamera = cameraOwner.GetComponent<Camera>();
                int activeDisplay = ResolveActiveEditorGameViewTarget();
                eventCamera.targetDisplay = activeDisplay;
                eventCamera.transform.position = new Vector3(0f, 0f, -10f);
                eventCamera.transform.rotation = Quaternion.identity;

                canvasOwner = new GameObject(
                    "PlayMode World-Space Study Canvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(GraphicRaycaster)
                );
                canvasOwner.SetActive(false);
                var canvas = canvasOwner.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.targetDisplay = activeDisplay;
                canvas.worldCamera = eventCamera;
                GraphicRaycaster raycaster =
                    canvasOwner.GetComponent<GraphicRaycaster>();
                raycaster.ignoreReversedGraphics = false;
                var canvasRect = (RectTransform)canvasOwner.transform;
                canvasRect.sizeDelta = new Vector2(800f, 600f);
                canvasRect.localScale = Vector3.one * 0.01f;

                InteractionStudyFlowController controller =
                    NewReadyController(canvasOwner.transform, fixture.Flow);
                var controls =
                    canvasOwner.AddComponent<InteractionStudyFlowControls>();
                var instruction = canvasOwner.AddComponent<
                    InteractionInstructionControls>();
                GameObject surface = NewChild(
                    canvasOwner.transform,
                    "Study Start Surface"
                );
                var surfaceRect = (RectTransform)surface.transform;
                surfaceRect.anchorMin = Vector2.zero;
                surfaceRect.anchorMax = Vector2.one;
                surfaceRect.offsetMin = Vector2.zero;
                surfaceRect.offsetMax = Vector2.zero;

                GameObject startObject = NewChild(
                    surface.transform,
                    "Study Start Button"
                );
                var startImage = startObject.AddComponent<Image>();
                var startButton = startObject.AddComponent<Button>();
                startButton.targetGraphic = startImage;
                var startRect = (RectTransform)startObject.transform;
                startRect.sizeDelta = new Vector2(520f, 110f);
                startRect.anchoredPosition = new Vector2(0f, 70f);

                var status = NewUiComponent<TextMeshProUGUI>(
                    canvasOwner.transform,
                    "Study Status"
                );
                status.raycastTarget = false;
                var progress = NewUiComponent<TextMeshProUGUI>(
                    canvasOwner.transform,
                    "Study Progress"
                );
                progress.raycastTarget = false;

                controls.Configure(
                    controller,
                    instruction,
                    surface,
                    startButton,
                    status,
                    progress
                );
                canvasOwner.SetActive(true);
                startImage.raycastTarget = false;
                startImage.raycastTarget = true;
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();

                Require(
                    startButton.interactable,
                    "The live standalone Start button was not interactable."
                );
                Vector2 pointerPosition = RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    startRect.position
                );
                var pointer = new PointerEventData(eventSystem)
                {
                    button = PointerEventData.InputButton.Left,
                    position = pointerPosition
                };
                var hits = new List<RaycastResult>();
                raycaster.Raycast(pointer, hits);

                GameObject hitObject = null;
                foreach (RaycastResult hit in hits)
                {
                    if (hit.gameObject == startObject)
                    {
                        hitObject = hit.gameObject;
                        break;
                    }
                }
                if (hitObject == null)
                {
                    // Synthetic PointerEventData is remapped through the
                    // Editor's desktop/display coordinates before the public
                    // raycaster reaches its graphic filter. Invoke that same
                    // UGUI graphic filter directly so PC PlayMode still tests
                    // the world-space target without a real OS pointer.
                    hitObject = FindCoreGraphicRaycastHit(
                        canvas,
                        eventCamera,
                        pointerPosition,
                        startObject
                    );
                }
                Require(
                    hitObject != null,
                    "GraphicRaycaster did not hit the world-space Start button. " +
                    "hits=" + hits.Count +
                    ", screen=" + pointerPosition +
                    ", Screen=" + Screen.width + "x" + Screen.height +
                    ", pixelRect=" + eventCamera.pixelRect +
                    ", canvasActive=" + canvas.isActiveAndEnabled +
                    ", imageActive=" + startImage.isActiveAndEnabled +
                    ", imageDepth=" + startImage.depth +
                    ", imageCull=" + startImage.canvasRenderer.cull +
                    ", imageRaycast=" + startImage.Raycast(
                        pointerPosition,
                        eventCamera
                    ) +
                    ", registeredGraphics=" +
                        GraphicRegistry.GetGraphicsForCanvas(canvas).Count +
                    ", raycastableGraphics=" +
                        GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas).Count +
                    ", activeDisplay=" + activeDisplay +
                    ", cameraDisplay=" + eventCamera.targetDisplay +
                    ", contains=" + RectTransformUtility.RectangleContainsScreenPoint(
                        startRect,
                        pointerPosition,
                        eventCamera
                    ) +
                    ", rect=" + startRect.rect + "."
                );

                ExecuteEvents.ExecuteHierarchy(
                    hitObject,
                    pointer,
                    ExecuteEvents.pointerClickHandler
                );

                Require(
                    fixture.Run.StartCount == 1,
                    "One EventSystem pointer click triggered " +
                        fixture.Run.StartCount + " Run starts."
                );
                Require(
                    controller.Snapshot.RunState == RunState.Scheduled,
                    "The real Start listener did not leave the Run scheduled."
                );
                Require(
                    !surface.activeSelf,
                    "The Start surface remained visible after a real click."
                );
            }
            finally
            {
                if (canvasOwner != null)
                {
                    UnityEngine.Object.DestroyImmediate(canvasOwner);
                }
                if (cameraOwner != null)
                {
                    UnityEngine.Object.DestroyImmediate(cameraOwner);
                }
                if (eventSystemOwner != null)
                {
                    UnityEngine.Object.DestroyImmediate(eventSystemOwner);
                }
                fixture.Flow.Dispose();
            }
        }

#if UNITY_EDITOR
        public static IEnumerator SavedInteractionLabStartButtonConsumesOneRun()
        {
            return RunSavedInteractionLabScenarioWithCleanup(
                injectFailureAfterRunStart: false
            );
        }

        public static IEnumerator
            SavedInteractionLabFailureAfterStartCleansOwnedResources()
        {
            return RunSavedInteractionLabScenarioWithCleanup(
                injectFailureAfterRunStart: true
            );
        }

        private static IEnumerator RunSavedInteractionLabScenarioWithCleanup(
            bool injectFailureAfterRunStart)
        {
            Require(
                Application.isPlaying,
                "The saved InteractionLab integration must run in Play Mode."
            );

            var context = new SavedInteractionLabTestContext(
                injectFailureAfterRunStart
            );
            Exception primaryFailure = null;
            Exception cleanupFailure = null;
            IEnumerator scenario =
                RunSavedInteractionLabStartButtonScenario(context);

            while (true)
            {
                bool hasNext = false;
                object current = null;
                try
                {
                    hasNext = scenario.MoveNext();
                    if (hasNext)
                    {
                        current = scenario.Current;
                    }
                }
                catch (Exception exception)
                {
                    primaryFailure = exception;
                }
                if (primaryFailure != null || !hasNext)
                {
                    break;
                }
                yield return current;
            }

            try
            {
                (scenario as IDisposable)?.Dispose();
            }
            catch (Exception exception)
            {
                cleanupFailure = AppendCleanupFailure(
                    cleanupFailure,
                    exception
                );
            }

            InteractionRunController runController = context.RunController;
            try
            {
                if (runController != null && runController.Plan != null &&
                    runController.State != RunState.Completed &&
                    runController.State != RunState.Aborted &&
                    runController.State != RunState.Faulted)
                {
                    runController.ProcessApplicationPauseForTests();
                    if (!runController.WaitForLifecycleTerminalizationForTests(
                            TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Saved-scene cleanup did not finish Run " +
                            "terminalization; fixture retained at " +
                            context.TemporaryRoot + "."
                        );
                    }
                }
                else if (runController != null && runController.Plan != null &&
                    runController.State == RunState.Faulted)
                {
                    throw new InvalidOperationException(
                        "Saved-scene Controller faulted; fixture retained at " +
                            context.TemporaryRoot + "."
                    );
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = AppendCleanupFailure(
                    cleanupFailure,
                    exception
                );
            }

            Scene ownedScene = ResolveOwnedScene(context);
            if (context.LoadRequested &&
                (!ownedScene.IsValid() || !ownedScene.isLoaded))
            {
                double cleanupLoadDeadline =
                    Time.realtimeSinceStartupAsDouble + 10d;
                while ((!ownedScene.IsValid() || !ownedScene.isLoaded) &&
                    Time.realtimeSinceStartupAsDouble < cleanupLoadDeadline)
                {
                    yield return null;
                    ownedScene = ResolveOwnedScene(context);
                }
            }

            if (ownedScene.IsValid() && ownedScene.isLoaded &&
                !context.PreexistingSceneHandles.Contains(
                    ownedScene.handle.GetRawData()
                ))
            {
                AsyncOperation unload = null;
                try
                {
                    unload = SceneManager.UnloadSceneAsync(ownedScene);
                    if (unload == null)
                    {
                        throw new InvalidOperationException(
                            "Unity refused to unload the test-owned " +
                                "InteractionLab scene."
                        );
                    }
                }
                catch (Exception exception)
                {
                    cleanupFailure = AppendCleanupFailure(
                        cleanupFailure,
                        exception
                    );
                }
                if (unload != null)
                {
                    yield return unload;
                    if (ownedScene.isLoaded)
                    {
                        cleanupFailure = AppendCleanupFailure(
                            cleanupFailure,
                            new InvalidOperationException(
                                "The test-owned InteractionLab scene remained " +
                                    "loaded after cleanup."
                            )
                        );
                    }
                }
            }
            else if (context.LoadRequested)
            {
                cleanupFailure = AppendCleanupFailure(
                    cleanupFailure,
                    new InvalidOperationException(
                        "Could not identify a loaded test-owned InteractionLab " +
                            "scene without touching a preexisting scene."
                    )
                );
            }

            if (cleanupFailure == null)
            {
                try
                {
                    W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(
                        context.TemporaryRoot
                    );
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
            }

            if (context.InjectFailureAfterRunStart)
            {
                if (cleanupFailure != null)
                {
                    ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                }
                Require(
                    primaryFailure is InvalidOperationException &&
                        string.Equals(
                            primaryFailure.Message,
                            SavedInteractionLabTestContext.InjectedFailureMessage,
                            StringComparison.Ordinal
                        ),
                    "The saved-scene failure-path test did not capture its " +
                        "injected primary failure."
                );
                Require(
                    !Directory.Exists(context.TemporaryRoot),
                    "The saved-scene failure path retained its temporary root."
                );
                Require(
                    !FindNewLoadedSceneByPath(
                        SavedInteractionLabTestContext.ScenePath,
                        context.PreexistingSceneHandles
                    ).IsValid(),
                    "The saved-scene failure path left its owned scene loaded."
                );
                Require(
                    context.PreexistingSceneHandles.IsSubsetOf(
                        CaptureLoadedSceneHandles()
                    ),
                    "The saved-scene failure path unloaded a preexisting scene."
                );
                yield break;
            }

            if (primaryFailure != null && cleanupFailure != null)
            {
                throw new AggregateException(
                    "Saved InteractionLab scenario and cleanup both failed.",
                    primaryFailure,
                    cleanupFailure
                );
            }
            if (primaryFailure != null)
            {
                ExceptionDispatchInfo.Capture(primaryFailure).Throw();
            }
            if (cleanupFailure != null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }

        private static IEnumerator RunSavedInteractionLabStartButtonScenario(
            SavedInteractionLabTestContext context)
        {
            // Always load a test-owned scene instance. Reusing the scene that
            // entered Play Mode would leave its mode, Run, and storage root
            // mutated after this integration test.
            context.LoadRequested = true;
            context.LoadedScene = EditorSceneManager.LoadSceneInPlayMode(
                SavedInteractionLabTestContext.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive)
            );
            double loadDeadline = Time.realtimeSinceStartupAsDouble + 10d;
            while ((!context.LoadedScene.IsValid() ||
                    !context.LoadedScene.isLoaded ||
                    context.PreexistingSceneHandles.Contains(
                        context.LoadedScene.handle.GetRawData()
                    )) &&
                Time.realtimeSinceStartupAsDouble < loadDeadline)
            {
                yield return null;
                Scene discovered = FindNewLoadedSceneByPath(
                    SavedInteractionLabTestContext.ScenePath,
                    context.PreexistingSceneHandles
                );
                if (discovered.IsValid())
                {
                    context.LoadedScene = discovered;
                }
            }
            Scene loadedScene = context.LoadedScene;
            Require(
                loadedScene.IsValid() && loadedScene.isLoaded &&
                    !context.PreexistingSceneHandles.Contains(
                        loadedScene.handle.GetRawData()
                    ),
                "Unity did not load a test-owned InteractionLab scene."
            );
            yield return null;

            InteractionStudyFlowControls controls =
                FindSingleSceneComponent<InteractionStudyFlowControls>(
                    loadedScene
                );
            InteractionStudyFlowController flowController =
                FindSingleSceneComponent<InteractionStudyFlowController>(
                    loadedScene
                );
            context.RunController =
                FindSingleSceneComponent<InteractionRunController>(
                    loadedScene
                );
            InteractionRunController runController = context.RunController;
            Button startButton = controls.StartButton;

            Require(
                controls.FlowController == flowController &&
                    flowController.RunController == runController,
                "Saved InteractionLab Start controls are not wired to " +
                    "the saved Flow and Run controllers."
            );
            Require(
                startButton != null &&
                    startButton.gameObject.scene == loadedScene,
                "Saved InteractionLab has no scene-owned Start button."
            );

            string editorStorageRoot = Path.GetFullPath(
                runController.StorageRootForTests
            );
            string realPersistentRoot = Path.GetFullPath(
                Application.persistentDataPath
            );
            string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );
            string editorStorageParent =
                Directory.GetParent(editorStorageRoot)?.FullName;
            string editorStorageName = Path.GetFileName(editorStorageRoot);
            StringComparison pathComparison =
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            Require(
                !string.Equals(
                    editorStorageRoot,
                    realPersistentRoot,
                    pathComparison
                ) &&
                    string.Equals(
                        editorStorageParent,
                        tempParent,
                        pathComparison
                    ) &&
                    editorStorageName.StartsWith(
                        "sv-",
                        StringComparison.Ordinal
                    ) &&
                    editorStorageName.Length == 11,
                "Editor Play Mode startup recovery was not isolated from " +
                    "the real persistent experiment root or exceeded its " +
                    "short-path contract."
            );

            double preparationDeadline =
                Time.realtimeSinceStartupAsDouble + 10d;
            while ((!flowController.ManifestReady ||
                    runController.StartupRecoveryStatus ==
                        InteractionStandaloneLocalRunRecoveryStatus.NotStarted ||
                    runController.StartupRecoveryStatus ==
                        InteractionStandaloneLocalRunRecoveryStatus.Recovering) &&
                Time.realtimeSinceStartupAsDouble < preparationDeadline)
            {
                yield return null;
            }
            Require(
                flowController.ManifestReady,
                "Saved InteractionLab did not load its real manifest."
            );
            Require(
                runController.StartupRecoveryStatus ==
                    InteractionStandaloneLocalRunRecoveryStatus.Succeeded,
                "Saved InteractionLab did not finish its real startup " +
                    "recovery. Status=" +
                    runController.StartupRecoveryStatus + "."
            );
            Require(
                runController.State == RunState.PreStart,
                "Saved InteractionLab was not in PreStart before the click."
            );

            // Preserve the saved UI/Flow/Controller wiring. Only replace the
            // device-owned XR gate and write root through the existing Editor
            // EngineeringLocal seam. The sandbox recovery and real manifest
            // catalog stay installed.
            runController.RedirectStandaloneStorageRootForTests(
                context.TemporaryRoot,
                debugBuild: true
            );
            runController.ConfigureMode(
                InteractionRunMode.EngineeringLocal,
                configuredDebugOverridesActive: false,
                explicitlyArmEngineeringLocal: true
            );

            double readyDeadline = Time.realtimeSinceStartupAsDouble + 5d;
            while (!startButton.interactable &&
                Time.realtimeSinceStartupAsDouble < readyDeadline)
            {
                yield return null;
            }
            Require(
                startButton.interactable,
                "Saved InteractionLab Start button did not become ready " +
                    "through its real FlowControls binding."
            );

            startButton.onClick.Invoke();
            RunPlan consumedPlan = runController.Plan;
            Require(
                consumedPlan != null &&
                    !string.IsNullOrWhiteSpace(consumedPlan.RunId),
                "The saved Start listener did not consume a Run Plan."
            );
            Require(
                runController.State != RunState.PreStart,
                "Saved InteractionLab flashed immediately back to PreStart."
            );
            Require(
                string.Equals(
                    flowController.Snapshot?.Status,
                    "Run consumed; waiting for local capture and presentation.",
                    StringComparison.Ordinal
                ),
                "The saved Start listener did not finish the Flow start " +
                    "transaction. State=" + runController.State +
                    ", flow_status=" +
                    (flowController.Snapshot?.Status ?? "<none>") + "."
            );
            Require(
                runController.WaitForCaptureInitializationForTests(
                    TimeSpan.FromSeconds(5)
                ),
                "Saved InteractionLab capture initialization did not finish."
            );
            runController.ReconcileConsumedRunInitializationForTests();
            string interactionRoot = InteractionStoragePaths.GetInteractionRoot(
                context.TemporaryRoot
            );
            Require(
                Directory.GetDirectories(
                    interactionRoot,
                    "run_*",
                    SearchOption.AllDirectories
                ).Length == 1,
                "The first saved-button click did not create exactly one Run."
            );

            if (context.InjectFailureAfterRunStart)
            {
                throw new InvalidOperationException(
                    SavedInteractionLabTestContext.InjectedFailureMessage
                );
            }

            string consumedRunId = consumedPlan.RunId;
            startButton.onClick.Invoke();
            Require(
                ReferenceEquals(consumedPlan, runController.Plan) &&
                    string.Equals(
                        consumedRunId,
                        runController.Plan?.RunId,
                        StringComparison.Ordinal
                ),
                "A repeated saved-button click consumed another Run Plan."
            );
            Require(
                Directory.GetDirectories(
                    interactionRoot,
                    "run_*",
                    SearchOption.AllDirectories
                ).Length == 1,
                "A repeated saved-button click created another Run directory."
            );
            Require(
                runController.State != RunState.PreStart,
                "Repeated Start returned the active Run to PreStart."
            );

            yield return null;
            Require(
                ReferenceEquals(consumedPlan, runController.Plan) &&
                    runController.State != RunState.PreStart,
                "The saved scene did not retain the consumed Run next frame. " +
                    "State=" + runController.State +
                    ", controller_error=" + runController.LastError +
                    ", flow_status=" +
                    (flowController.Snapshot?.Status ?? "<none>") + "."
            );
        }

        private static Scene ResolveOwnedScene(
            SavedInteractionLabTestContext context)
        {
            Scene candidate = context.LoadedScene;
            if (candidate.IsValid() &&
                !context.PreexistingSceneHandles.Contains(
                    candidate.handle.GetRawData()
                ))
            {
                return candidate;
            }
            candidate = FindNewLoadedSceneByPath(
                SavedInteractionLabTestContext.ScenePath,
                context.PreexistingSceneHandles
            );
            if (candidate.IsValid())
            {
                context.LoadedScene = candidate;
            }
            return candidate;
        }

        private static Exception AppendCleanupFailure(
            Exception current,
            Exception next)
        {
            return current == null
                ? next
                : new AggregateException(
                    "Multiple saved-scene cleanup steps failed.",
                    current,
                    next
                );
        }

        private sealed class SavedInteractionLabTestContext
        {
            internal const string ScenePath =
                "Assets/Scenes/InteractionLab.unity";
            internal const string InjectedFailureMessage =
                "injected_saved_scene_failure_after_run_start";

            internal SavedInteractionLabTestContext(
                bool injectFailureAfterRunStart)
            {
                TemporaryRoot = CreateShortTemporaryRoot();
                PreexistingSceneHandles = CaptureLoadedSceneHandles();
                InjectFailureAfterRunStart = injectFailureAfterRunStart;
            }

            internal string TemporaryRoot { get; }
            internal HashSet<ulong> PreexistingSceneHandles { get; }
            internal bool InjectFailureAfterRunStart { get; }
            internal bool LoadRequested { get; set; }
            internal Scene LoadedScene { get; set; }
            internal InteractionRunController RunController { get; set; }
        }

        private static HashSet<ulong> CaptureLoadedSceneHandles()
        {
            var handles = new HashSet<ulong>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                handles.Add(
                    SceneManager.GetSceneAt(index).handle.GetRawData()
                );
            }
            return handles;
        }

        private static Scene FindNewLoadedSceneByPath(
            string scenePath,
            ISet<ulong> excludedHandles)
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene candidate = SceneManager.GetSceneAt(index);
                if (candidate.isLoaded &&
                    !excludedHandles.Contains(
                        candidate.handle.GetRawData()
                    ) &&
                    string.Equals(
                        candidate.path,
                        scenePath,
                        StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return default;
        }

        private static string CreateShortTemporaryRoot()
        {
            return W6InteractionCaptureHostTestDriver
                .CreateOwnedShortTemporaryRoot();
        }
#endif

        private static GameObject FindCoreGraphicRaycastHit(
            Canvas canvas,
            Camera eventCamera,
            Vector2 pointerPosition,
            GameObject expected)
        {
            MethodInfo coreRaycast = null;
            foreach (MethodInfo candidate in typeof(GraphicRaycaster).GetMethods(
                         BindingFlags.NonPublic | BindingFlags.Static))
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.Name == "Raycast" && parameters.Length == 5 &&
                    parameters[0].ParameterType == typeof(Canvas) &&
                    parameters[1].ParameterType == typeof(Camera))
                {
                    coreRaycast = candidate;
                    break;
                }
            }
            Require(
                coreRaycast != null,
                "Unity UGUI core GraphicRaycaster filter was not found."
            );

            var graphics = new List<Graphic>();
            coreRaycast.Invoke(null, new object[]
            {
                canvas,
                eventCamera,
                pointerPosition,
                GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas),
                graphics
            });
            foreach (Graphic graphic in graphics)
            {
                if (graphic != null && graphic.gameObject == expected)
                {
                    return expected;
                }
            }
            return null;
        }

        private static T FindSingleSceneComponent<T>(Scene scene)
            where T : Component
        {
            T found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T candidate in root.GetComponentsInChildren<T>(true))
                {
                    Require(
                        found == null,
                        "Saved InteractionLab contains more than one " +
                            typeof(T).Name + "."
                    );
                    found = candidate;
                }
            }
            Require(
                found != null,
                "Saved InteractionLab contains no " + typeof(T).Name + "."
            );
            return found;
        }

        public static void RecoveringMessageIsStable()
        {
            Require(
                InteractionStudyParticipantText.ForPreStart(
                    recoveryComplete: false,
                    recoveryFailed: false,
                    manifestReady: true,
                    identityReady: true,
                    canStart: true,
                    initializationStatus: string.Empty,
                    flowStatus: string.Empty,
                    commandFeedback: string.Empty,
                    pauseNotice: string.Empty
                ) ==
                    "正在整理上一次未完整结束的实验，请稍候…",
                "Recovering participant message changed."
            );
        }

        public static void RecoveryFailureMessageIsStable()
        {
            Require(
                InteractionStudyParticipantText.ForPreStart(
                    recoveryComplete: false,
                    recoveryFailed: true,
                    manifestReady: true,
                    identityReady: true,
                    canStart: true,
                    initializationStatus: string.Empty,
                    flowStatus: string.Empty,
                    commandFeedback: string.Empty,
                    pauseNotice: string.Empty
                ) ==
                    "上一次实验数据整理失败，请联系工作人员。",
                "Recovery failure participant message changed."
            );
        }

        public static void ReadyMessageIsStable()
        {
            Require(
                InteractionStudyParticipantText.ForPreStart(
                    recoveryComplete: true,
                    recoveryFailed: false,
                    manifestReady: true,
                    identityReady: true,
                    canStart: true,
                    initializationStatus: string.Empty,
                    flowStatus: string.Empty,
                    commandFeedback: string.Empty,
                    pauseNotice: string.Empty
                ) ==
                    "准备就绪，请点击“开始体验”。",
                "Ready participant message changed."
            );
        }

        public static void PauseAbortedMessageIsStableAndConsumedOnce()
        {
            var fixture = new FlowFixture();
            GameObject owner = NewInactiveOwner("Standalone Pause Notice");
            try
            {
                var controller =
                    owner.AddComponent<InteractionStudyFlowController>();
                controller.InstallStandaloneStateForTests(
                    fixture.Flow,
                    manifestIsReady: true,
                    recoveryIsComplete: true,
                    session: NewSession(
                        "11111111-2222-3333-4444-555555555555"
                    )
                );
                Require(controller.TryStart().Succeeded, "Start was rejected.");

                controller.HandleApplicationPauseForTests(true);
                controller.HandleApplicationPauseForTests(false);

                Require(fixture.Run.AbortCount == 1, "Pause did not locally abort.");
                Require(
                    controller.TryConsumePauseAbortNotice(),
                    "Resume did not publish its non-blocking notice."
                );
                Require(
                    !controller.TryConsumePauseAbortNotice(),
                    "Pause notice was published more than once."
                );
                Require(
                    InteractionStudyParticipantText.ForPreStart(
                        recoveryComplete: true,
                        recoveryFailed: false,
                        manifestReady: true,
                        identityReady: true,
                        canStart: true,
                        initializationStatus: string.Empty,
                        flowStatus: string.Empty,
                        commandFeedback: string.Empty,
                        pauseNotice:
                            InteractionStudyParticipantText.PauseAborted
                    ) ==
                        "上一次体验因头盔暂停已安全结束，可以开始新的体验。",
                    "Pause-aborted participant message changed."
                );
            }
            finally
            {
                fixture.Flow.Dispose();
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void ParticipantMessagesDoNotExposeRemovedServices()
        {
            string[] messages =
            {
                InteractionStudyParticipantText.Recovering,
                InteractionStudyParticipantText.RecoveryFailed,
                InteractionStudyParticipantText.Ready,
                InteractionStudyParticipantText.PauseAborted,
                InteractionStudyParticipantText.ForFailure(
                    "Host camera upload readiness ACK sync failure"
                ),
                InteractionStudyParticipantText.ForRunState(
                    RunState.Scheduled,
                    "Host registration pending"
                ),
                InteractionStudyParticipantText.ForRunState(
                    RunState.Completing,
                    "upload pending"
                )
            };
            string[] forbidden =
            {
                "Host",
                "camera",
                "upload",
                "ACK",
                "readiness",
                "摄像头",
                "上传",
                "同步"
            };
            foreach (string message in messages)
            {
                foreach (string token in forbidden)
                {
                    Require(
                        message.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0,
                        "Participant text exposed removed service token: " + token
                    );
                }
            }
        }

        public static void StandaloneStudyRequiresStrictXrGate()
        {
            Require(
                InteractionStudyRunModePolicy.RequiresStrictXrGate(
                    InteractionRunMode.StandaloneStudy
                ),
                "StandaloneStudy bypassed strict XR capture readiness."
            );
        }

        public static void EngineeringLocalBypassesStrictXrGate()
        {
            Require(
                !InteractionStudyRunModePolicy.RequiresStrictXrGate(
                    InteractionRunMode.EngineeringLocal
                ),
                "EngineeringLocal cannot run PC automation without a HMD."
            );
        }

        private static int ResolveActiveEditorGameViewTarget()
        {
            PropertyInfo property = typeof(Display).GetProperty(
                "activeEditorGameViewTarget",
                BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static
            );
            return property?.GetValue(null) is int display ? display : 0;
        }

        private static InteractionStudyFlowController NewReadyController(
            Transform parent,
            InteractionStudyFlow flow)
        {
            GameObject owner = NewChild(parent, "Controller");
            owner.SetActive(false);
            var controller =
                owner.AddComponent<InteractionStudyFlowController>();
            controller.InstallStandaloneStateForTests(
                flow,
                manifestIsReady: true,
                recoveryIsComplete: true,
                session: NewSession(
                    "11111111-2222-3333-4444-555555555555"
                )
            );
            return controller;
        }

        private static ParticipantSession NewSession(string guid)
        {
            return new ParticipantSession(
                () => SessionTime,
                () => Guid.Parse(guid)
            );
        }

        private static GameObject NewInactiveOwner(string name)
        {
            var owner = new GameObject(name);
            owner.SetActive(false);
            return owner;
        }

        private static GameObject NewChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }

        private static T NewUiComponent<T>(
            Transform parent,
            string name) where T : Component
        {
            GameObject item = NewChild(parent, name);
            return item.AddComponent<T>();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class FlowFixture
        {
            public FlowFixture(Guid? firstRunGuid = null)
            {
                Run = new FakeRunPort(firstRunGuid);
                Presentation = new FakePresentationPort();
                Tasks = new FakeTaskPort();
                Flow = new InteractionStudyFlow(Run, Presentation, Tasks);
            }

            public FakeRunPort Run { get; }
            public FakePresentationPort Presentation { get; }
            public FakeTaskPort Tasks { get; }
            public InteractionStudyFlow Flow { get; }
        }

        private sealed class FakeRunPort : IInteractionStudyRunPort
        {
            private Action<InteractionStudyPresentationRequest> subscriber;
            private readonly RunPlanGenerator generator;
            private readonly AssistanceBlockAllocator allocator =
                new AssistanceBlockAllocator(71);
            private readonly Guid? firstRunGuid;
            private int guidSequence;

            public FakeRunPort(Guid? firstRunGuid)
            {
                this.firstRunGuid = firstRunGuid;
                generator = new RunPlanGenerator(NextRunGuid, () => SessionTime);
            }

            public RunState State { get; private set; } = RunState.PreStart;
            public RunPlan Plan { get; private set; }
            public string ParticipantId { get; set; } = "P-SESSION";
            public PhaseExecutionSnapshot CurrentPhase => null;
            public string LastError { get; private set; } = string.Empty;
            public int StartCount { get; private set; }
            public int AbortCount { get; private set; }
            public int ResetCount { get; private set; }

            public IDisposable Subscribe(
                Action<InteractionStudyPresentationRequest> callback)
            {
                subscriber += callback;
                return new CallbackDisposable(() => subscriber -= callback);
            }

            public bool CanStart(out string reason)
            {
                reason = State == RunState.PreStart ? null : "Run is active.";
                return State == RunState.PreStart;
            }

            public bool TryStart(out string error)
            {
                if (!CanStart(out error))
                {
                    return false;
                }
                Plan = generator.Generate(
                    new RunPlanGenerationRequest(
                        "pilot-20260827",
                        ParticipantId,
                        "standalone_flow_test",
                        "1.0.0",
                        "abcdef0123456789",
                        20260827,
                        CreateCatalog()
                    ),
                    allocator.AllocateNext()
                );
                State = RunState.Scheduled;
                StartCount++;
                return true;
            }

            public bool TryRequestReplay(out string error)
            {
                error = "Replay is unavailable.";
                return false;
            }

            public bool TryAcknowledgePresentationStarted(
                InteractionStudyPresentationRequest request,
                out string error)
            {
                error = null;
                return true;
            }

            public bool TryNotifyPlaybackCompleted(
                InteractionPresentationPlaybackKind playbackKind,
                out string error)
            {
                error = null;
                return true;
            }

            public bool TryRecordValidationResult(
                ValidationResult result,
                out string error)
            {
                error = null;
                return true;
            }

            public bool TryRecordPresentationObservation(
                InteractionStudyPresentationObservation observation,
                out string error)
            {
                error = null;
                return true;
            }

            public bool TryFinishPhase(bool stuck, out string error)
            {
                error = null;
                return true;
            }

            public bool TryAbort(string reason, out string error)
            {
                if (State != RunState.Scheduled &&
                    State != RunState.Running &&
                    State != RunState.Completing &&
                    State != RunState.Preparing)
                {
                    error = "Run is not active.";
                    return false;
                }
                State = RunState.Aborting;
                AbortCount++;
                error = null;
                return true;
            }

            public bool TryResetToPreStart()
            {
                if (State != RunState.Completed &&
                    State != RunState.Aborted &&
                    State != RunState.Faulted)
                {
                    return false;
                }
                State = RunState.PreStart;
                Plan = null;
                ResetCount++;
                return true;
            }

            public void MarkCompleted()
            {
                State = RunState.Completed;
            }

            private Guid NextRunGuid()
            {
                guidSequence++;
                if (guidSequence == 1 && firstRunGuid.HasValue)
                {
                    return firstRunGuid.Value;
                }
                return guidSequence == 1
                    ? Guid.Parse(
                        "99999999-8888-7777-6666-555555555555"
                    )
                    : Guid.Parse(
                        "88888888-7777-6666-5555-444444444444"
                    );
            }
        }

        private sealed class FakePresentationPort :
            IInteractionStudyPresentationPort
        {
            public bool PhaseActive => false;
            public bool ReplayAvailable => false;
            public bool GiveUpAvailable => false;

            public IDisposable Subscribe(
                Action<InteractionPresentationPlaybackKind> firstFramePresented,
                Action<InteractionPresentationPlaybackKind> playbackCompleted,
                Action<InteractionStudyPresentationObservation>
                    presentationObserved,
                Action<string> presentationFaulted)
            {
                return new CallbackDisposable(() => { });
            }

            public bool TryBeginPhase(
                RunPhasePlan phasePlan,
                AssistanceCondition condition,
                out string error)
            {
                error = null;
                return true;
            }

            public bool TryBeginReplay(out string error)
            {
                error = "Replay is unavailable.";
                return false;
            }

            public void EndPhase()
            {
            }
        }

        private sealed class FakeTaskPort : IInteractionStudyTaskPort
        {
            public RunPlan Plan { get; private set; }
            public ValidationResult LastResult => null;

            public IDisposable Subscribe(Action<ValidationResult> resultProduced)
            {
                return new CallbackDisposable(() => { });
            }

            public void Configure(RunPlan plan)
            {
                Plan = plan;
            }

            public void Enable()
            {
            }

            public void Disable()
            {
            }

            public void Synchronize(PhaseExecutionSnapshot snapshot)
            {
            }

            public ValidationResult GiveUp(PhaseExecutionSnapshot snapshot)
            {
                return null;
            }

            public void Reset()
            {
                Plan = null;
            }

            public void Abort()
            {
                Plan = null;
            }
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action callback;

            public CallbackDisposable(Action callback)
            {
                this.callback = callback;
            }

            public void Dispose()
            {
                Action current = callback;
                callback = null;
                current?.Invoke();
            }
        }

        private static InstructionContentCatalog CreateCatalog()
        {
            var values = new List<InstructionContentReference>();
            foreach (string sentenceId in PhaseSentenceRanges.AllSentenceIds)
            {
                values.Add(new InstructionContentReference(
                    PhaseSentenceRanges.GetPhaseId(sentenceId),
                    sentenceId,
                    InteractionContractV1.PilotSignerId,
                    "take_001",
                    SessionTime,
                    1,
                    "wang/sentence_" + sentenceId + "/take_001.pose.jsonl",
                    new string('a', 64)
                ));
            }
            return new InstructionContentCatalog(values);
        }
    }
}
#endif
