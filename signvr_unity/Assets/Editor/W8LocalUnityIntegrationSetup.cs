using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.PhaseAdapters;
using SignVR.Interaction.Presentation;
using SignVR.SceneFlow;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SignVR.Editor.Interaction
{
    /// <summary>
    /// Transactional integration facade. The production menu idempotently
    /// validates the saved W5 baseline, then runs W6 -> W7 -> W8 in one outer
    /// Undo transaction; the explicit
    /// scene overload remains W8-only for prepared test fixtures. Neither path
    /// ever saves a scene.
    /// </summary>
    public static class W8LocalUnityIntegrationSetup
    {
        private const string RuntimeAnchorPath =
            "InteractionSceneRoot/RuntimeSystemsAnchor";
        private const string UiAnchorPath =
            "InteractionSceneRoot/Anchors/InteractionUiAnchor";
        private const string CaptureAnchorPath =
            "InteractionSceneRoot/Anchors/ExperimentCaptureAnchor";
        private const string StartSurfaceName = "W8StudyStartSurface";
        private const string ExpectedManifestRelativePath =
            "InstructionContent/instruction-content-manifest.json";

        [MenuItem(
            "Tools/SignVR/Interaction/W8 Configure Local Study Flow (Unsaved)"
        )]
        public static void ConfigureLoadedSceneUnsaved()
        {
            ConfigureLoadedSceneUnsavedForAutomation(null);
            Debug.Log(
                "[W8LocalUnityIntegrationSetup] W8 structure is valid. " +
                "The scene is intentionally unsaved; review it before saving."
            );
        }

        public static void ConfigureLoadedSceneUnsavedForAutomation(
            Action afterWiring)
        {
            ConfigureSceneUnsavedForAutomation(
                SceneManager.GetActiveScene(),
                true,
                afterWiring
            );
        }

        [MenuItem(
            "Tools/SignVR/Interaction/W8 Configure Complete Integration (Unsaved)"
        )]
        public static void ConfigureIntegratedLoadedSceneUnsavedFromMenu()
        {
            ConfigureIntegratedLoadedSceneUnsavedForAutomation(null);
            Debug.Log(
                "[W8LocalUnityIntegrationSetup] Saved W5 was validated and " +
                "W6 -> W7 -> W8 were configured in one outer Undo group. " +
                "The scene remains intentionally unsaved."
            );
        }

        public static void ConfigureIntegratedLoadedSceneUnsavedForAutomation(
            Action afterWiring)
        {
            Scene scene = SceneManager.GetActiveScene();
            PreflightIntegratedSetup(scene);
            ExecuteSceneUndoGroupForAutomation(
                scene,
                "W8 Configure Complete Local Study Flow",
                () =>
                {
                    // Production's single facade is deliberately ordered.
                    // W5 is already serialized in canonical InteractionLab
                    // and is validated without mutation. W6 and W7 then
                    // establish their authoritative publishers before W8
                    // wires only its orchestration/capture/UI adapters.
                    W5InstructionPresentationSetup
                        .ValidateConfiguredSceneForAutomation();
                    W6InteractionCaptureHostSetup
                        .ConfigureSceneUnsavedForAutomation(
                            scene,
                            true,
                            null
                        );
                    W7InteractionPhaseAdaptersSetup.SetupLoadedScene(scene);
                    Preflight(scene, true);
                    ConfigureUnchecked(scene);
                    afterWiring?.Invoke();
                    ValidateSceneStructure(scene, true);
                }
            );
        }

        public static void ConfigureSceneUnsavedForAutomation(
            Scene scene,
            bool requireCanonicalScenePath,
            Action afterWiring)
        {
            Preflight(scene, requireCanonicalScenePath);
            ExecuteSceneUndoGroupForAutomation(
                scene,
                "W8 Configure Local Study Flow",
                () =>
                {
                    ConfigureUnchecked(scene);
                    afterWiring?.Invoke();
                    ValidateSceneStructure(
                        scene,
                        requireCanonicalScenePath
                    );
                }
            );
        }

        [MenuItem(
            "Tools/SignVR/Interaction/W8 Validate Local Study Structure"
        )]
        public static void ValidateLoadedSceneStructureFromMenu()
        {
            ValidateLoadedSceneStructureForAutomation();
            Debug.Log(
                "[W8LocalUnityIntegrationSetup] Structural validation passed."
            );
        }

        [MenuItem(
            "Tools/SignVR/Interaction/W8 Validate Strict Study Readiness"
        )]
        public static void ValidateStrictStudyReadinessFromMenu()
        {
            ValidateStrictStudyReadinessForAutomation();
            Debug.Log(
                "[W8LocalUnityIntegrationSetup] Strict Study readiness passed."
            );
        }

        public static void ValidateLoadedSceneStructureForAutomation()
        {
            ValidateSourceContractForAutomation();
            Scene scene = SceneManager.GetActiveScene();
            W6InteractionCaptureHostSetup.ValidateLoadedSceneForAutomation();
            W7InteractionPhaseAdaptersSetup.ValidateLoadedScene(scene);
            ValidateSceneStructure(scene, true);
        }

        [MenuItem(
            "Tools/SignVR/Interaction/W8 Validate Saved Integrated Study Flow"
        )]
        public static void ValidateSavedIntegratedStudyFlowFromMenu()
        {
            ValidateSavedIntegratedStudyFlowForAutomation();
            Debug.Log(
                "[W8LocalUnityIntegrationSetup] Saved W5/W6/W7/W8 " +
                "integration validation passed."
            );
        }

        public static void ValidateSavedIntegratedStudyFlowForAutomation()
        {
            Scene loaded = SceneManager.GetSceneByPath(
                InteractionLabContract.ScenePath
            );
            if (!loaded.IsValid() || !loaded.isLoaded)
            {
                throw new InvalidOperationException(
                    "Saved integration validation requires the canonical " +
                    "InteractionLab scene to be loaded."
                );
            }
            if (loaded.isDirty)
            {
                throw new InvalidOperationException(
                    "InteractionLab has unsaved integration changes. Review " +
                    "and save before running the saved-scene validator."
                );
            }
            W5InstructionPresentationSetup
                .ValidateConfiguredSceneForAutomation();
            ValidateLoadedSceneStructureForAutomation();

            // Re-open the asset as an isolated preview scene so this gate
            // verifies serialized disk state, not merely clean-looking live
            // objects. Preview loading preserves the operator's scene setup.
            Scene persisted = default;
            try
            {
                persisted = EditorSceneManager.OpenPreviewScene(
                    InteractionLabContract.ScenePath
                );
                if (!persisted.IsValid() || !persisted.isLoaded)
                {
                    throw new InvalidOperationException(
                        "Unity could not reopen InteractionLab from disk for " +
                        "saved integration validation."
                    );
                }
                W7InteractionPhaseAdaptersSetup.ValidateLoadedScene(persisted);
                ValidateSceneStructure(persisted, true);
            }
            finally
            {
                if (persisted.IsValid() && persisted.isLoaded &&
                    !EditorSceneManager.ClosePreviewScene(persisted))
                {
                    Debug.LogError(
                        "[W8LocalUnityIntegrationSetup] Could not close the " +
                        "saved-scene validation preview."
                    );
                }
            }
        }

        public static void ValidateSceneStructureForAutomation(
            Scene scene,
            bool requireCanonicalScenePath)
        {
            ValidateSourceContractForAutomation();
            ValidateSceneStructure(scene, requireCanonicalScenePath);
        }

        public static void ValidateStrictStudyReadinessForAutomation()
        {
            ValidateLoadedSceneStructureForAutomation();
            Scene scene = SceneManager.GetActiveScene();
            InteractionStudyFlowController flow =
                EnumerateSceneComponents<InteractionStudyFlowController>(scene)
                    .Single();
            string manifestPath = Path.Combine(
                Application.streamingAssetsPath,
                flow.ContentManifestRelativePath.Replace('/',
                    Path.DirectorySeparatorChar)
            );
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    "W2 staged manifest is missing: " + manifestPath +
                    ". Stage the reviewed 31-sentence generation into " +
                    "StreamingAssets/InstructionContent before Study use."
                );
            }
            InstructionContentCatalog catalog =
                InteractionInstructionContentManifestReader.Read(
                    File.ReadAllBytes(manifestPath)
                );
            if (catalog.Entries.Count != PhaseSentenceRanges.TotalSentenceCount)
            {
                throw new InvalidOperationException(
                    "W2 staged manifest must contain exactly 31 sentences."
                );
            }

            InteractionStudyCaptureBinding capture = flow.CaptureBinding;
            if (!capture.CanStartStudy(out string reason))
            {
                throw new InvalidOperationException(
                    "Strict Study capture is not ready: " + reason
                );
            }
        }

        public static void ValidateSourceContractForAutomation()
        {
            var failures = new List<string>();
            string[] conditions = Enum.GetNames(typeof(AssistanceCondition));
            string[] expectedConditions =
            {
                nameof(AssistanceCondition.TextAndPointing),
                nameof(AssistanceCondition.TextOnly),
                nameof(AssistanceCondition.SignOnly)
            };
            if (!conditions.OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(expectedConditions.OrderBy(
                        value => value,
                        StringComparer.Ordinal)))
            {
                failures.Add(
                    "AssistanceCondition must remain exactly " +
                    "TextAndPointing/TextOnly/SignOnly."
                );
            }

            if (typeof(InteractionInstructionControls).GetMethod(
                    "TryInstallCommandSink",
                    new[] { typeof(IInteractionInstructionCommandSink) }) ==
                null ||
                typeof(InteractionInstructionControls).GetMethod(
                    "TryClearCommandSink",
                    new[] { typeof(IInteractionInstructionCommandSink) }) ==
                null ||
                typeof(InteractionInstructionControls).GetMethod(
                    "ConfigureCommandRouting",
                    new[] { typeof(bool) }) == null ||
                !typeof(IInteractionInstructionCommandSink).IsAssignableFrom(
                    typeof(InteractionStudyFlowControls)))
            {
                failures.Add(
                    "The real W5 controls lack W8's fail-closed command sink seam."
                );
            }

            RequireSourceContains(
                "Scripts/Interaction/Presentation/" +
                    "InteractionInstructionControls.cs",
                new[]
                {
                    "if (requireCommandSink)",
                    "liveSink.RequestReplay()",
                    "TryClearCommandSink",
                    "controller?.Replay()"
                },
                failures
            );
            RequireSourceContains(
                "Scripts/Interaction/Orchestration/" +
                    "InteractionStudyFlowControls.cs",
                new[]
                {
                    "instructionControls.TryInstallCommandSink(this)",
                    "instructionControls.TryClearCommandSink(this)",
                    "flowController?.TryReplay()",
                    "flowController?.TryStart()"
                },
                failures
            );
            RequireSourceContains(
                "Scripts/Interaction/Orchestration/" +
                    "InteractionStudyFlowController.cs",
                new[]
                {
                    "ParticipantSession",
                    "EnsureParticipantSession",
                    "ApplyAutomaticIdentityToCurrentRun",
                    "StopManifestLoad",
                    "SafeSuspendFlow(\"application_pause\")",
                    "SafeSuspendFlow(\"component_disabled\")"
                },
                failures
            );
            RequireSourceContains(
                "Editor/W8LocalUnityIntegrationSetup.cs",
                new[]
                {
                    "W5InstructionPresentationSetup",
                    "W6InteractionCaptureHostSetup",
                    "W7InteractionPhaseAdaptersSetup.SetupLoadedScene(scene)",
                    "ExecuteSceneUndoGroupForAutomation",
                    "W6 Configure Local Capture (Unsaved)",
                    "W7 Setup Phase Adapters"
                },
                failures
            );

            string driver = ReadAssetSource(
                "Scripts/Interaction/Orchestration/" +
                    "W8InteractionStudyFlowTestDriver.cs"
            );
            if (driver.IndexOf(
                    "NUnit.Framework",
                    StringComparison.Ordinal) >= 0)
            {
                failures.Add(
                    "The Assembly-CSharp W8 runtime driver must not reference NUnit."
                );
            }

            ThrowIfInvalid("W8 source contract", failures);
        }

        public static void ExecuteUndoGroupForAutomation(
            string operationName,
            Action operation)
        {
            if (string.IsNullOrWhiteSpace(operationName))
            {
                throw new ArgumentException(
                    "Undo operation name is required.",
                    nameof(operationName)
                );
            }
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(operationName.Trim());
            try
            {
                operation();
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        private static void ExecuteSceneUndoGroupForAutomation(
            Scene scene,
            string operationName,
            Action operation)
        {
            bool wasDirty = scene.isDirty;
            Dictionary<TMP_Text, bool> richTextBefore =
                CaptureExistingW8RichTextState(scene);
            try
            {
                ExecuteUndoGroupForAutomation(operationName, operation);
            }
            catch
            {
                RestoreExistingW8RichTextState(richTextBefore);
                if (!wasDirty && scene.IsValid() && scene.isLoaded)
                {
                    TryRestoreCleanSceneState(scene);
                }
                throw;
            }
        }

        private static Dictionary<TMP_Text, bool>
            CaptureExistingW8RichTextState(Scene scene)
        {
            Transform uiAnchor = TryFindTransform(scene, UiAnchorPath);
            Transform surface = uiAnchor == null
                ? null
                : FindDirectChild(uiAnchor, StartSurfaceName);
            return surface == null
                ? new Dictionary<TMP_Text, bool>()
                : surface.GetComponentsInChildren<TMP_Text>(true)
                    .Where(value => value != null)
                    .ToDictionary(value => value, value => value.richText);
        }

        private static void RestoreExistingW8RichTextState(
            IReadOnlyDictionary<TMP_Text, bool> snapshot)
        {
            foreach (KeyValuePair<TMP_Text, bool> pair in snapshot)
            {
                if (pair.Key == null || pair.Key.richText == pair.Value)
                {
                    continue;
                }
                pair.Key.richText = pair.Value;
                EditorUtility.SetDirty(pair.Key);
            }
        }

        private static void TryRestoreCleanSceneState(Scene scene)
        {
            try
            {
                MethodInfo clear = typeof(EditorSceneManager).GetMethod(
                    "ClearSceneDirtiness",
                    BindingFlags.Static | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Scene) },
                    null
                );
                clear?.Invoke(null, new object[] { scene });
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[W8LocalUnityIntegrationSetup] Undo restored objects, " +
                    "but Unity could not restore the scene's prior clean " +
                    "flag: " + exception.Message
                );
            }
        }

        private static void ConfigureUnchecked(Scene scene)
        {
            Transform runtimeAnchor = RequireTransform(scene, RuntimeAnchorPath);
            Transform uiAnchor = RequireTransform(scene, UiAnchorPath);
            Transform captureAnchor = RequireTransform(scene, CaptureAnchorPath);

            InteractionRunController run =
                RequireSingle<InteractionRunController>(scene);
            InteractionCaptureSampler sampler =
                RequireSingle<InteractionCaptureSampler>(scene);
            InstructionPresentationController presentation =
                RequireSingle<InstructionPresentationController>(scene);
            InteractionInstructionControls instructionControls =
                RequireSingle<InteractionInstructionControls>(scene);
            InteractionPhaseCoordinator phases =
                RequireSingle<InteractionPhaseCoordinator>(scene);

            HandSources hands = ResolveHands(scene);
            OVRSkeleton leftSkeleton = EnsureSkeleton(
                hands.Left,
                hands.LeftType
            );
            OVRSkeleton rightSkeleton = EnsureSkeleton(
                hands.Right,
                hands.RightType
            );
            Transform hmd = ResolveHmd(scene);
            InteractionObjectStateProbe[] probes = EnsureObjectProbes(scene);

            InteractionStudyFlowController flow =
                GetOrAdd<InteractionStudyFlowController>(
                    runtimeAnchor.gameObject
                );
            InteractionStudyCaptureBinding capture =
                GetOrAdd<InteractionStudyCaptureBinding>(
                    captureAnchor.gameObject
                );
            InteractionStudyFlowControls controls =
                GetOrAdd<InteractionStudyFlowControls>(uiAnchor.gameObject);

            SetObjectReference(flow, "runController", run);
            SetObjectReference(
                flow,
                "presentationController",
                presentation
            );
            SetObjectReference(flow, "phaseCoordinator", phases);
            SetObjectReference(flow, "captureBinding", capture);

            SetObjectReference(capture, "sampler", sampler);
            SetObjectReference(capture, "hmd", hmd);
            SetObjectReference(capture, "leftHand", hands.Left);
            SetObjectReference(capture, "leftSkeleton", leftSkeleton);
            SetObjectReference(capture, "rightHand", hands.Right);
            SetObjectReference(capture, "rightSkeleton", rightSkeleton);
            SetObjectArray(capture, "objectStateProbes", probes);

            SetObjectReference(sampler, "hmd", hmd);
            SetObjectReference(sampler, "leftHand", hands.Left);
            SetObjectReference(sampler, "leftSkeleton", leftSkeleton);
            SetObjectReference(sampler, "rightHand", hands.Right);
            SetObjectReference(sampler, "rightSkeleton", rightSkeleton);
            SetObjectArray(sampler, "objectStateProbes", probes);

            GameObject surface = EnsureStartSurface(uiAnchor, hmd);
            Button start = RequireSurfaceComponent<Button>(surface, "Start");
            TMP_Text status = RequireSurfaceComponent<TMP_Text>(
                surface,
                "Status"
            );
            TMP_Text progress = RequireSurfaceComponent<TMP_Text>(
                surface,
                "Progress"
            );

            SetObjectReference(controls, "flowController", flow);
            SetObjectReference(
                controls,
                "instructionControls",
                instructionControls
            );
            SetObjectReference(controls, "preStartRoot", surface);
            SetObjectReference(controls, "startButton", start);
            SetObjectReference(controls, "participantIdInput", null);
            SetObjectReference(controls, "buildIdentityInput", null);
            SetObjectReference(controls, "applyIdentityButton", null);
            SetObjectReference(controls, "statusLabel", status);
            SetObjectReference(controls, "progressLabel", progress);
            SetBoolean(instructionControls, "requireCommandSink", true);
        }

        private static void ValidateSceneStructure(
            Scene scene,
            bool requireCanonicalScenePath)
        {
            RequireInteractionScene(scene, requireCanonicalScenePath);
            var failures = new List<string>();
            Transform runtimeAnchor = TryFindTransform(scene, RuntimeAnchorPath);
            Transform uiAnchor = TryFindTransform(scene, UiAnchorPath);
            Transform captureAnchor = TryFindTransform(scene, CaptureAnchorPath);
            if (runtimeAnchor == null || uiAnchor == null ||
                captureAnchor == null)
            {
                failures.Add("One or more canonical W8 anchors are missing.");
                ThrowIfInvalid("W8 scene structure", failures);
            }

            InteractionRunController run = ValidateSingleAt(
                scene,
                runtimeAnchor,
                failures,
                allowDescendant: false,
                default(InteractionRunController)
            );
            InstructionPresentationController presentation = ValidateSingleAt(
                scene,
                runtimeAnchor,
                failures,
                allowDescendant: false,
                default(InstructionPresentationController)
            );
            InteractionCaptureSampler sampler = ValidateSingleAt(
                scene,
                captureAnchor,
                failures,
                allowDescendant: false,
                default(InteractionCaptureSampler)
            );
            InteractionInstructionControls instructionControls =
                ValidateSingleAt(
                    scene,
                    uiAnchor,
                    failures,
                    allowDescendant: false,
                    default(InteractionInstructionControls)
                );
            InteractionPhaseCoordinator phases = ValidateSingleAnywhere<
                InteractionPhaseCoordinator>(scene, failures);
            InteractionStudyFlowController flow = ValidateSingleAt(
                scene,
                runtimeAnchor,
                failures,
                allowDescendant: false,
                default(InteractionStudyFlowController)
            );
            InteractionStudyCaptureBinding capture = ValidateSingleAt(
                scene,
                captureAnchor,
                failures,
                allowDescendant: false,
                default(InteractionStudyCaptureBinding)
            );
            InteractionStudyFlowControls controls = ValidateSingleAt(
                scene,
                uiAnchor,
                failures,
                allowDescendant: false,
                default(InteractionStudyFlowControls)
            );

            if (run != null && (!string.Equals(
                    run.ParticipantId,
                    "UNCONFIGURED",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    run.GitCommit,
                    "unintegrated",
                    StringComparison.Ordinal)))
            {
                failures.Add(
                    "Scene identity must remain UNCONFIGURED/unintegrated; " +
                    "the application assigns participant/build identity at runtime."
                );
            }

            HandSources hands = null;
            try
            {
                hands = ResolveHands(scene);
                ValidateHandSkeleton(
                    hands.Left,
                    hands.LeftType,
                    "left",
                    failures
                );
                ValidateHandSkeleton(
                    hands.Right,
                    hands.RightType,
                    "right",
                    failures
                );
                if (EnumerateSceneComponents<OVRSkeleton>(scene).Count() != 2)
                {
                    failures.Add(
                        "The scene must contain exactly two genuine OVRSkeleton " +
                        "components, one per real OVRHand source."
                    );
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }

            InteractionTargetBinding[] bindings =
                EnumerateSceneComponents<InteractionTargetBinding>(scene)
                    .OrderBy(value => value.TargetId, StringComparer.Ordinal)
                    .ToArray();
            InteractionObjectStateProbe[] probes =
                EnumerateSceneComponents<InteractionObjectStateProbe>(scene)
                    .OrderBy(value => value.ObjectId, StringComparer.Ordinal)
                    .ToArray();
            ValidateProbeInventory(bindings, probes, failures);

            OVRSkeleton leftSkeleton = hands?.Left == null
                ? null
                : hands.Left.GetComponent<OVRSkeleton>();
            OVRSkeleton rightSkeleton = hands?.Right == null
                ? null
                : hands.Right.GetComponent<OVRSkeleton>();
            Transform hmd = null;
            try
            {
                hmd = ResolveHmd(scene);
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }

            if (flow != null && (flow.RunController != run ||
                flow.PresentationController != presentation ||
                flow.PhaseCoordinator != phases ||
                flow.CaptureBinding != capture ||
                !string.Equals(
                    flow.ContentManifestRelativePath,
                    ExpectedManifestRelativePath,
                    StringComparison.Ordinal)))
            {
                failures.Add(
                    "W8 flow references or manifest path are not canonical."
                );
            }
            if (capture != null)
            {
                if (capture.Sampler != sampler || capture.Hmd != hmd ||
                    capture.LeftHand != hands?.Left ||
                    capture.LeftSkeleton != leftSkeleton ||
                    capture.RightHand != hands?.Right ||
                    capture.RightSkeleton != rightSkeleton ||
                    !SameReferences(capture.ObjectStateProbes, probes))
                {
                    failures.Add(
                        "W8 capture binding does not reference the canonical " +
                        "HMD, two Meta hands/skeletons, and W7 probes."
                    );
                }
                else if (!capture.ValidateStructure(out string reason))
                {
                    failures.Add("W8 capture structure: " + reason);
                }
            }

            GameObject surface = FindDirectChild(uiAnchor, StartSurfaceName)
                ?.gameObject;
            if (surface == null || surface.GetComponent<Canvas>() == null ||
                surface.GetComponent<WorldSpacePokeCanvas>() == null)
            {
                failures.Add(
                    "Participant-visible W8StudyStartSurface world-space UI is missing."
                );
            }
            if (controls != null && (flow == null ||
                controls.FlowController != flow ||
                controls.InstructionControls != instructionControls ||
                controls.PreStartRoot != surface ||
                controls.StartButton == null ||
                controls.ParticipantIdInput != null ||
                controls.BuildIdentityInput != null ||
                controls.ApplyIdentityButton != null ||
                controls.StatusLabel == null ||
                controls.ProgressLabel == null ||
                !controls.enabled))
            {
                failures.Add(
                    "W8 standalone Start/status controls are incomplete, " +
                    "disabled, or still reference legacy identity widgets."
                );
            }
            if (surface != null && (
                    FindDirectChild(surface.transform, "ParticipantId") != null ||
                    FindDirectChild(surface.transform, "BuildIdentity") != null ||
                    FindDirectChild(surface.transform, "ApplyIdentity") != null))
            {
                failures.Add(
                    "W8 standalone Start surface still contains legacy identity widgets."
                );
            }
            if (instructionControls != null &&
                !instructionControls.RequireCommandSink)
            {
                failures.Add(
                    "Real Replay/GiveUp/Abort controls are not fail-closed for W8."
                );
            }

            ThrowIfInvalid("W8 scene structure", failures);
        }

        private static void PreflightIntegratedSetup(Scene scene)
        {
            RequireInteractionScene(scene, true);
            if (SceneManager.GetActiveScene().handle != scene.handle)
            {
                throw new InvalidOperationException(
                    "The canonical InteractionLab must be the active scene " +
                    "before the integrated W5 -> W6 -> W7 -> W8 setup."
                );
            }
            RequireTransform(scene, RuntimeAnchorPath);
            RequireTransform(scene, UiAnchorPath);
            RequireTransform(scene, CaptureAnchorPath);
            ResolveHands(scene);
            ResolveHmd(scene);
            if (ResolveFont() == null)
            {
                throw new InvalidOperationException(
                    "A TMP font is required for the participant Start surface."
                );
            }
        }

        private static void Preflight(
            Scene scene,
            bool requireCanonicalScenePath)
        {
            RequireInteractionScene(scene, requireCanonicalScenePath);
            RequireTransform(scene, RuntimeAnchorPath);
            Transform uiAnchor = RequireTransform(scene, UiAnchorPath);
            RequireTransform(scene, CaptureAnchorPath);
            int runCount = EnumerateSceneComponents<
                InteractionRunController>(scene).Count();
            int samplerCount = EnumerateSceneComponents<
                InteractionCaptureSampler>(scene).Count();
            if (runCount != 1 || samplerCount != 1)
            {
                throw new InvalidOperationException(
                    "W8 prerequisite W6 is missing or ambiguous (RunController=" +
                    runCount + ", CaptureSampler=" + samplerCount + "). " +
                    "Run 'Tools/SignVR/Interaction/W6 Configure Local " +
                    "Capture (Unsaved)' first, then W7, then W8; or use the " +
                    "single W8 integrated setup menu."
                );
            }
            int presentationCount = EnumerateSceneComponents<
                InstructionPresentationController>(scene).Count();
            int instructionControlCount = EnumerateSceneComponents<
                InteractionInstructionControls>(scene).Count();
            if (presentationCount != 1 || instructionControlCount != 1)
            {
                throw new InvalidOperationException(
                    "W8 prerequisite W5 is missing or ambiguous " +
                    "(InstructionPresentationController=" +
                    presentationCount + ", InteractionInstructionControls=" +
                    instructionControlCount + "). Run 'Tools/SignVR/" +
                    "Interaction/W5 Configure Instruction Presentation " +
                    "(Unsaved)' before W6 -> W7 -> W8."
                );
            }
            int coordinatorCount = EnumerateSceneComponents<
                InteractionPhaseCoordinator>(scene).Count();
            int targetBindingCount = EnumerateSceneComponents<
                InteractionTargetBinding>(scene).Count();
            if (coordinatorCount != 1 || targetBindingCount == 0)
            {
                throw new InvalidOperationException(
                    "W8 prerequisite W7 is missing or ambiguous " +
                    "(InteractionPhaseCoordinator=" + coordinatorCount +
                    ", InteractionTargetBinding=" + targetBindingCount +
                    "). After W6, run 'Tools/SignVR/Interaction/W7 Setup " +
                    "Phase Adapters', then run W8; or use the single W8 " +
                    "integrated setup menu."
                );
            }
            ResolveHands(scene);
            ResolveHmd(scene);
            TMP_FontAsset font = ResolveFont();
            if (font == null)
            {
                throw new InvalidOperationException(
                    "A TMP font is required for the participant Start surface."
                );
            }

            EnsureAtMostOneOnAnchor<InteractionStudyFlowController>(
                scene,
                RuntimeAnchorPath
            );
            EnsureAtMostOneOnAnchor<InteractionStudyCaptureBinding>(
                scene,
                CaptureAnchorPath
            );
            EnsureAtMostOneOnAnchor<InteractionStudyFlowControls>(
                scene,
                UiAnchorPath
            );

            Transform[] namedSurfaces = uiAnchor.Cast<Transform>()
                .Where(child => string.Equals(
                    child.name,
                    StartSurfaceName,
                    StringComparison.Ordinal))
                .ToArray();
            if (namedSurfaces.Length > 1 ||
                (namedSurfaces.Length == 1 &&
                 namedSurfaces[0].GetComponent<RectTransform>() == null))
            {
                throw new InvalidOperationException(
                    "W8StudyStartSurface is duplicated or is not a UI object."
                );
            }

            InteractionTargetBinding[] bindings =
                EnumerateSceneComponents<InteractionTargetBinding>(scene)
                    .ToArray();
            if (bindings.Length == 0 || bindings.Any(binding =>
                    binding == null ||
                    string.IsNullOrWhiteSpace(binding.TargetId)) ||
                bindings.Select(binding => binding.TargetId)
                    .Distinct(StringComparer.Ordinal).Count() != bindings.Length)
            {
                throw new InvalidOperationException(
                    "W7 target bindings must be present with unique stable IDs."
                );
            }
            foreach (InteractionObjectStateProbe probe in
                     EnumerateSceneComponents<InteractionObjectStateProbe>(scene))
            {
                InteractionTargetBinding binding =
                    probe.GetComponent<InteractionTargetBinding>();
                if (binding == null || !string.Equals(
                        probe.ObjectId,
                        binding.TargetId,
                        StringComparison.Ordinal) ||
                    binding.GetComponents<InteractionObjectStateProbe>().Length !=
                        1)
                {
                    throw new InvalidOperationException(
                        "Existing object probes must be one-to-one with W7 " +
                        "target bindings and use the same stable ID."
                    );
                }
            }
        }

        private static HandSources ResolveHands(Scene scene)
        {
            OVRHand[] hands = EnumerateSceneComponents<OVRHand>(scene).ToArray();
            if (hands.Length != 2)
            {
                throw new InvalidOperationException(
                    "InteractionLab requires exactly two real OVRHand data " +
                    "sources; found " + hands.Length + "."
                );
            }

            OVRHand left = null;
            OVRHand right = null;
            OVRSkeleton.SkeletonType leftType =
                OVRSkeleton.SkeletonType.None;
            OVRSkeleton.SkeletonType rightType =
                OVRSkeleton.SkeletonType.None;
            for (int index = 0; index < hands.Length; index++)
            {
                var provider =
                    (OVRSkeleton.IOVRSkeletonDataProvider)hands[index];
                OVRSkeleton.SkeletonType type = provider.GetSkeletonType();
                string name = type.ToString();
                if (name.IndexOf("Left", StringComparison.Ordinal) >= 0)
                {
                    if (left != null)
                    {
                        throw new InvalidOperationException(
                            "More than one left OVRHand provider was found."
                        );
                    }
                    left = hands[index];
                    leftType = type;
                }
                else if (name.IndexOf("Right", StringComparison.Ordinal) >= 0)
                {
                    if (right != null)
                    {
                        throw new InvalidOperationException(
                            "More than one right OVRHand provider was found."
                        );
                    }
                    right = hands[index];
                    rightType = type;
                }
            }
            if (left == null || right == null || left == right ||
                leftType == OVRSkeleton.SkeletonType.None ||
                rightType == OVRSkeleton.SkeletonType.None)
            {
                throw new InvalidOperationException(
                    "The two OVRHand components must expose distinct Left and " +
                    "Right Meta skeleton provider types."
                );
            }
            return new HandSources(left, leftType, right, rightType);
        }

        private static OVRSkeleton EnsureSkeleton(
            OVRHand hand,
            OVRSkeleton.SkeletonType expectedType)
        {
            OVRSkeleton[] skeletons = hand.GetComponents<OVRSkeleton>();
            if (skeletons.Length > 1)
            {
                throw new InvalidOperationException(
                    "OVRHand data source has more than one OVRSkeleton: " +
                    HierarchyPath(hand.transform) + "."
                );
            }
            OVRSkeleton skeleton = skeletons.SingleOrDefault() ??
                Undo.AddComponent<OVRSkeleton>(hand.gameObject);
            Undo.RecordObject(skeleton, "Configure W8 Meta hand skeleton");
            var serialized = new SerializedObject(skeleton);
            serialized.Update();
            SerializedProperty type = serialized.FindProperty("_skeletonType");
            if (type == null)
            {
                throw new InvalidOperationException(
                    "Meta OVRSkeleton._skeletonType is unavailable."
                );
            }
            type.intValue = (int)expectedType;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(skeleton);
            return skeleton;
        }

        private static InteractionObjectStateProbe[] EnsureObjectProbes(
            Scene scene)
        {
            InteractionTargetBinding[] bindings =
                EnumerateSceneComponents<InteractionTargetBinding>(scene)
                    .OrderBy(value => value.TargetId, StringComparer.Ordinal)
                    .ToArray();
            var probes = new InteractionObjectStateProbe[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
            {
                InteractionTargetBinding binding = bindings[index];
                InteractionObjectStateProbe[] existing =
                    binding.GetComponents<InteractionObjectStateProbe>();
                if (existing.Length > 1)
                {
                    throw new InvalidOperationException(
                        "W7 target has duplicate probes: " + binding.TargetId +
                        "."
                    );
                }
                InteractionObjectStateProbe probe = existing.SingleOrDefault() ??
                    Undo.AddComponent<InteractionObjectStateProbe>(
                        binding.gameObject
                    );
                Undo.RecordObject(probe, "Configure W8 object probe");
                probe.Configure(binding.TargetId, binding.transform);
                EditorUtility.SetDirty(probe);
                probes[index] = probe;
            }
            return probes;
        }

        private static Transform ResolveHmd(Scene scene)
        {
            Camera[] cameras = EnumerateSceneComponents<Camera>(scene).ToArray();
            Camera[] main = cameras.Where(camera =>
                camera.CompareTag("MainCamera")).ToArray();
            if (main.Length == 1)
            {
                return main[0].transform;
            }
            if (main.Length > 1)
            {
                throw new InvalidOperationException(
                    "More than one scene Camera is tagged MainCamera."
                );
            }
            Camera[] centerEye = cameras.Where(camera =>
                HierarchyPath(camera.transform).IndexOf(
                    "CenterEyeAnchor",
                    StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (centerEye.Length != 1)
            {
                throw new InvalidOperationException(
                    "A unique real XR CenterEye/MainCamera is required; found " +
                    centerEye.Length + "."
                );
            }
            return centerEye[0].transform;
        }

        private static GameObject EnsureStartSurface(
            Transform uiAnchor,
            Transform hmd)
        {
            TMP_FontAsset font = ResolveFont();
            Transform existing = FindDirectChild(uiAnchor, StartSurfaceName);
            GameObject root = existing == null
                ? CreateUiObject(uiAnchor, StartSurfaceName)
                : existing.gameObject;
            RectTransform rect = RequireRect(root);
            Undo.RecordObject(rect, "Layout W8 Start surface");
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(780f, 500f);
            rect.localPosition = new Vector3(0f, 0.20f, 0f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.001f;

            GetOrAdd<Canvas>(root);
            GetOrAdd<CanvasScaler>(root);
            GetOrAdd<GraphicRaycaster>(root);
            GetOrAdd<WorldSpacePokeCanvas>(root);

            // Adding a RequireComponent user through Undo may cause Unity to
            // replace a just-created required-component wrapper. Re-resolve
            // every reference after the component set is complete so setup
            // never records or configures a stale marshalled object.
            Canvas canvas = root.GetComponent<Canvas>();
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            WorldSpacePokeCanvas poke = root.GetComponent<WorldSpacePokeCanvas>();
            if (canvas == null || scaler == null || poke == null)
            {
                throw new InvalidOperationException(
                    "W8 Start surface could not create its required Canvas " +
                    "component set. Missing: " + string.Join(", ", new[]
                    {
                        canvas == null ? nameof(Canvas) : null,
                        scaler == null ? nameof(CanvasScaler) : null,
                        poke == null ? nameof(WorldSpacePokeCanvas) : null
                    }.Where(value => value != null)) + "."
                );
            }
            Undo.RecordObjects(
                new UnityEngine.Object[] { canvas, scaler, poke },
                "Configure W8 Start canvas"
            );
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 29991;
            canvas.worldCamera = hmd.GetComponent<Camera>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 100f;
            poke.Configure(canvas);

            Image background = EnsurePanel(root.transform, "Background");
            Undo.RecordObject(background, "Configure W8 Start background");
            background.color = new Color(0.035f, 0.05f, 0.075f, 0.96f);
            Stretch(background.rectTransform, 0f);
            background.raycastTarget = false;

            TMP_Text title = EnsureText(
                root.transform,
                "Title",
                "本地手语交互研究",
                font,
                32f
            );
            SetRect(title.rectTransform, new Vector2(0f, 190f),
                new Vector2(700f, 60f));

            RemoveDirectChildIfPresent(root.transform, "ParticipantId");
            RemoveDirectChildIfPresent(root.transform, "BuildIdentity");
            RemoveDirectChildIfPresent(root.transform, "ApplyIdentity");

            Button start = EnsureButton(
                root.transform,
                "Start",
                "开始体验",
                font,
                new Color(0.12f, 0.56f, 0.30f, 1f)
            );
            SetRect(start.GetComponent<RectTransform>(),
                new Vector2(0f, 70f), new Vector2(520f, 110f));

            TMP_Text status = EnsureText(
                root.transform,
                "Status",
                "正在自动准备实验，请稍候…",
                font,
                23f
            );
            if (status.textWrappingMode != TextWrappingModes.Normal)
            {
                Undo.RecordObject(status, "Configure W8 status wrapping");
                status.textWrappingMode = TextWrappingModes.Normal;
                EditorUtility.SetDirty(status);
            }
            SetRect(status.rectTransform, new Vector2(0f, -55f),
                new Vector2(700f, 150f));

            TMP_Text progress = EnsureText(
                root.transform,
                "Progress",
                string.Empty,
                font,
                24f
            );
            SetRect(progress.rectTransform, new Vector2(0f, -215f),
                new Vector2(700f, 48f));

            SetLayerRecursively(root.transform, uiAnchor.gameObject.layer);
            return root;
        }

        private static Button EnsureButton(
            Transform parent,
            string name,
            string label,
            TMP_FontAsset font,
            Color color)
        {
            GameObject root = EnsureNamedUiObject(parent, name);
            Image image = GetOrAdd<Image>(root);
            Button button = GetOrAdd<Button>(root);
            Undo.RecordObjects(
                new UnityEngine.Object[] { image, button },
                "Configure W8 Study button"
            );
            image.color = color;
            button.targetGraphic = image;
            TMP_Text text = EnsureText(
                root.transform,
                "Label",
                label,
                font,
                25f
            );
            Stretch(text.rectTransform, 8f);
            EditorUtility.SetDirty(button);
            return button;
        }

        private static TMP_Text EnsureText(
            Transform parent,
            string name,
            string value,
            TMP_FontAsset font,
            float fontSize,
            Color? color = null)
        {
            GameObject root = EnsureNamedUiObject(parent, name);
            TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(root);
            bool needsBasicChange =
                !string.Equals(text.text, value, StringComparison.Ordinal) ||
                text.font != font ||
                !Mathf.Approximately(text.fontSize, fontSize) ||
                text.alignment != TextAlignmentOptions.Center ||
                text.richText ||
                text.raycastTarget;
            if (needsBasicChange)
            {
                Undo.RecordObject(text, "Configure W8 UI text");
            }
            if (!string.Equals(text.text, value, StringComparison.Ordinal))
            {
                text.text = value;
            }
            if (text.font != font)
            {
                text.font = font;
            }
            if (!Mathf.Approximately(text.fontSize, fontSize))
            {
                text.fontSize = fontSize;
            }
            if (text.alignment != TextAlignmentOptions.Center)
            {
                text.alignment = TextAlignmentOptions.Center;
            }
            if (text.richText)
            {
                text.richText = false;
            }
            if (text.raycastTarget)
            {
                text.raycastTarget = false;
            }
            bool colorChanged = SetTextColor(text, color ?? Color.white);
            if (needsBasicChange || colorChanged)
            {
                EditorUtility.SetDirty(text);
            }
            return text;
        }

        private static bool SetTextColor(TMP_Text text, Color value)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            var serialized = new SerializedObject(text);
            serialized.Update();
            var colorProperties = new List<SerializedProperty>();
            foreach (string propertyName in new[]
                     {
                         "m_fontColor",
                         "m_fontColor32"
                     })
            {
                SerializedProperty property = serialized.FindProperty(
                    propertyName
                );
                if (property != null &&
                    property.propertyType == SerializedPropertyType.Color)
                {
                    colorProperties.Add(property);
                }
            }
            bool requiresChange = text.color != value ||
                colorProperties.Any(property => property.colorValue != value);
            if (!requiresChange)
            {
                return false;
            }

            Undo.RecordObject(text, "Configure W8 UI text color");
            text.color = value;
            foreach (SerializedProperty property in colorProperties)
            {
                property.colorValue = value;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(text);
            return true;
        }

        private static Image EnsurePanel(Transform parent, string name)
        {
            return GetOrAdd<Image>(EnsureNamedUiObject(parent, name));
        }

        private static void RemoveDirectChildIfPresent(
            Transform parent,
            string name)
        {
            Transform existing = FindDirectChild(parent, name);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }
        }

        private static GameObject EnsureNamedUiObject(
            Transform parent,
            string name)
        {
            Transform existing = FindDirectChild(parent, name);
            return existing == null
                ? CreateUiObject(parent, name)
                : existing.gameObject;
        }

        private static GameObject CreateUiObject(
            Transform parent,
            string name)
        {
            var value = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(value, "Create W8 Study UI");
            Undo.SetTransformParent(value.transform, parent, "Parent W8 Study UI");
            value.transform.localPosition = Vector3.zero;
            value.transform.localRotation = Quaternion.identity;
            value.transform.localScale = Vector3.one;
            return value;
        }

        private static void ValidateProbeInventory(
            InteractionTargetBinding[] bindings,
            InteractionObjectStateProbe[] probes,
            ICollection<string> failures)
        {
            if (bindings.Length == 0 || probes.Length != bindings.Length)
            {
                failures.Add(
                    "Every W7 key task binding must have exactly one capture probe."
                );
                return;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (InteractionTargetBinding binding in bindings)
            {
                InteractionObjectStateProbe[] local =
                    binding.GetComponents<InteractionObjectStateProbe>();
                if (local.Length != 1 || string.IsNullOrWhiteSpace(
                        binding.TargetId) || !string.Equals(
                        local[0].ObjectId,
                        binding.TargetId,
                        StringComparison.Ordinal) ||
                    !ids.Add(local[0].ObjectId))
                {
                    failures.Add(
                        "Probe IDs must be nonempty, unique, stable, and " +
                        "one-to-one with W7 TargetId values."
                    );
                    return;
                }
            }
        }

        private static void ValidateHandSkeleton(
            OVRHand hand,
            OVRSkeleton.SkeletonType expected,
            string side,
            ICollection<string> failures)
        {
            OVRSkeleton[] skeletons = hand.GetComponents<OVRSkeleton>();
            if (skeletons.Length != 1 ||
                skeletons[0].GetSkeletonType() != expected ||
                ((OVRSkeleton.IOVRSkeletonDataProvider)hand)
                    .GetSkeletonType() != expected)
            {
                failures.Add(
                    "The " + side + " OVRHand requires one same-object " +
                    "OVRSkeleton configured to its provider SkeletonType."
                );
            }
        }

        private static bool SameReferences<T>(
            IReadOnlyList<T> left,
            IReadOnlyList<T> right)
            where T : UnityEngine.Object
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }
            for (int index = 0; index < left.Count; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }
            return true;
        }

        private static T RequireSurfaceComponent<T>(
            GameObject root,
            string path)
            where T : Component
        {
            Transform child = root.transform.Find(path);
            T component = child == null ? null : child.GetComponent<T>();
            return component ?? throw new InvalidOperationException(
                "W8 Start surface is missing " + path + "/" +
                typeof(T).Name + "."
            );
        }

        private static TMP_FontAsset ResolveFont()
        {
            return Resources.Load<TMP_FontAsset>("Fonts/SignVRChinese SDF") ??
                TMP_Settings.defaultFontAsset;
        }

        private static RectTransform RequireRect(GameObject value)
        {
            return value.GetComponent<RectTransform>() ??
                throw new InvalidOperationException(
                    value.name + " must have a RectTransform."
                );
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            Undo.RecordObject(rect, "Layout W8 Study UI");
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            Undo.RecordObject(rect, "Layout W8 Study UI");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(
                         true))
            {
                Undo.RecordObject(child.gameObject, "Set W8 Study UI layer");
                child.gameObject.layer = layer;
            }
        }

        private static void SetObjectReference(
            UnityEngine.Object target,
            string propertyName,
            UnityEngine.Object value)
        {
            Undo.RecordObject(target, "Wire W8 Study Flow");
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null ||
                property.propertyType != SerializedPropertyType.ObjectReference)
            {
                throw new InvalidOperationException(
                    target.GetType().Name + " is missing object reference " +
                    propertyName + "."
                );
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetObjectArray<T>(
            UnityEngine.Object target,
            string propertyName,
            IReadOnlyList<T> values)
            where T : UnityEngine.Object
        {
            Undo.RecordObject(target, "Wire W8 Study Flow array");
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                throw new InvalidOperationException(
                    target.GetType().Name + " is missing array " +
                    propertyName + "."
                );
            }
            property.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue =
                    values[index];
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetBoolean(
            UnityEngine.Object target,
            string propertyName,
            bool value)
        {
            Undo.RecordObject(target, "Configure W8 command routing");
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null ||
                property.propertyType != SerializedPropertyType.Boolean)
            {
                throw new InvalidOperationException(
                    target.GetType().Name + " is missing boolean " +
                    propertyName + "."
                );
            }
            property.boolValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static T GetOrAdd<T>(GameObject target)
            where T : Component
        {
            return target.GetComponent<T>() ?? Undo.AddComponent<T>(target);
        }

        private static T RequireSingle<T>(Scene scene)
            where T : Component
        {
            T[] values = EnumerateSceneComponents<T>(scene).ToArray();
            if (values.Length != 1)
            {
                throw new InvalidOperationException(
                    "W8 requires exactly one existing " + typeof(T).Name +
                    "; found " + values.Length + ". Run W5/W6/W7 setup first."
                );
            }
            return values[0];
        }

        private static T ValidateSingleAnywhere<T>(
            Scene scene,
            ICollection<string> failures)
            where T : Component
        {
            T[] values = EnumerateSceneComponents<T>(scene).ToArray();
            if (values.Length != 1)
            {
                failures.Add(
                    "Expected exactly one " + typeof(T).Name + ", found " +
                    values.Length + "."
                );
            }
            return values.FirstOrDefault();
        }

        private static T ValidateSingleAt<T>(
            Scene scene,
            Transform anchor,
            ICollection<string> failures,
            bool allowDescendant,
            T unused)
            where T : Component
        {
            T value = ValidateSingleAnywhere<T>(scene, failures);
            if (value != null && (allowDescendant
                    ? !value.transform.IsChildOf(anchor)
                    : value.transform != anchor))
            {
                failures.Add(
                    typeof(T).Name + " is not on its canonical anchor."
                );
            }
            return value;
        }

        private static void EnsureAtMostOneOnAnchor<T>(
            Scene scene,
            string anchorPath)
            where T : Component
        {
            T[] values = EnumerateSceneComponents<T>(scene).ToArray();
            Transform anchor = RequireTransform(scene, anchorPath);
            if (values.Length > 1 ||
                (values.Length == 1 && values[0].transform != anchor))
            {
                throw new InvalidOperationException(
                    "Existing " + typeof(T).Name +
                    " is duplicated or is not on " + anchorPath + "."
                );
            }
        }

        private static void RequireInteractionScene(
            Scene scene,
            bool requireCanonicalScenePath)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                (requireCanonicalScenePath && !string.Equals(
                    scene.path,
                    InteractionLabContract.ScenePath,
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    requireCanonicalScenePath
                        ? "Load Assets/Scenes/InteractionLab.unity before W8 setup."
                        : "W8 automation scene is invalid or not loaded."
                );
            }
        }

        private static Transform RequireTransform(Scene scene, string path)
        {
            return TryFindTransform(scene, path) ??
                throw new InvalidOperationException(
                    "Required W8 anchor is missing or ambiguous: " + path + "."
                );
        }

        private static Transform TryFindTransform(Scene scene, string path)
        {
            string[] segments = path.Split('/');
            GameObject[] roots = scene.GetRootGameObjects().Where(root =>
                string.Equals(root.name, segments[0], StringComparison.Ordinal))
                .ToArray();
            if (roots.Length != 1)
            {
                return null;
            }
            Transform current = roots[0].transform;
            for (int index = 1; index < segments.Length; index++)
            {
                Transform[] matches = current.Cast<Transform>().Where(child =>
                    string.Equals(
                        child.name,
                        segments[index],
                        StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1)
                {
                    return null;
                }
                current = matches[0];
            }
            return current;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            Transform[] values = parent.Cast<Transform>().Where(child =>
                string.Equals(child.name, name, StringComparison.Ordinal))
                .ToArray();
            if (values.Length > 1)
            {
                throw new InvalidOperationException(
                    "Duplicate child named " + name + " below " +
                    HierarchyPath(parent) + "."
                );
            }
            return values.SingleOrDefault();
        }

        private static IEnumerable<T> EnumerateSceneComponents<T>(Scene scene)
            where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T component in root.GetComponentsInChildren<T>(true))
                {
                    yield return component;
                }
            }
        }

        private static string HierarchyPath(Transform value)
        {
            var parts = new Stack<string>();
            Transform current = value;
            while (current != null)
            {
                parts.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

        private static string ReadAssetSource(string relativeBelowAssets)
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                relativeBelowAssets.Replace('/', Path.DirectorySeparatorChar)
            ));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "W8 source file is missing.",
                    path
                );
            }
            return File.ReadAllText(path);
        }

        private static void RequireSourceContains(
            string relativeBelowAssets,
            IReadOnlyList<string> tokens,
            ICollection<string> failures)
        {
            string source = ReadAssetSource(relativeBelowAssets);
            for (int index = 0; index < tokens.Count; index++)
            {
                if (source.IndexOf(tokens[index], StringComparison.Ordinal) < 0)
                {
                    failures.Add(
                        relativeBelowAssets + " is missing routing token: " +
                        tokens[index]
                    );
                }
            }
        }

        private static void ThrowIfInvalid(
            string label,
            IReadOnlyCollection<string> failures)
        {
            if (failures.Count == 0)
            {
                return;
            }
            throw new InvalidOperationException(
                label + " failed:" + Environment.NewLine + "- " +
                string.Join(Environment.NewLine + "- ", failures)
            );
        }

        private sealed class HandSources
        {
            public HandSources(
                OVRHand left,
                OVRSkeleton.SkeletonType leftType,
                OVRHand right,
                OVRSkeleton.SkeletonType rightType)
            {
                Left = left;
                LeftType = leftType;
                Right = right;
                RightType = rightType;
            }

            public OVRHand Left { get; }
            public OVRSkeleton.SkeletonType LeftType { get; }
            public OVRHand Right { get; }
            public OVRSkeleton.SkeletonType RightType { get; }
        }
    }
}
