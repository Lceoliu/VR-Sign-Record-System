using System;
using System.Collections.Generic;
using System.Linq;
using SignVR.Interaction.PhaseAdapters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SignVR.Editor.Interaction
{
    /// <summary>
    /// Idempotent W7-only scene wiring. It consumes W4's named anchors without
    /// modifying the W4 generator and never derives task choices from scene
    /// state. Runtime Configure still requires W1's immutable RunPlan.
    /// </summary>
    public static class W7InteractionPhaseAdaptersSetup
    {
        private const string RuntimeRootName = "W7PhaseInteractionAdapters";
        private const string ProxyRootName = "W7InteractionProxies";
        private const string SafeDoorPath =
            "root/GLTF_SceneRootNode/Safe_0/Object_5";
        private const string ClosetDoorRootPath =
            "daadbec63f9940779964efd3fd00cb5c.fbx/RootNode";
        private const string ClosetLeftDoorPath =
            ClosetDoorRootPath + "/LeftDoor";
        private const string ClosetRightDoorPath =
            ClosetDoorRootPath + "/RightDoor";
        private const string ChestLidPath =
            "Collada visual scene group/ChestUpper_low";
        private const string FinalDoorPanelPath =
            "ce5f462b0dd34333a6588509140a7fb8.fbx/RootNode/Door";
        private const string TestScenePathPrefix =
            "Assets/__W7InteractionPhaseAdaptersTests_";
        private const string TestScenePathSuffix =
            "/InteractionLab_W7Test.unity";
        private const string TestSceneMarkerName =
            "__W7_TEST_OWNED_INTERACTION_SCENE__";

        private static readonly TargetSpec[] TargetSpecs =
        {
            new TargetSpec(1, "box_stool", "box", true),
            new TargetSpec(1, "box_floor_a", "box (1)", true),
            new TargetSpec(1, "box_floor_b", "box (2)", true),

            new TargetSpec(2, "coin_dragon", "dragon_coin", false, true),
            new TargetSpec(2, "coin_a", "golden_coin", false, true),
            new TargetSpec(2, "coin_b", "golden_coin (1)", false, true),
            new TargetSpec(2, "plate_dragon", "dragon_plate", false),
            new TargetSpec(2, "plate_a", "plate", false),
            new TargetSpec(2, "plate_b", "plate (1)", false),

            new TargetSpec(3, "picture_frame_a", "picture_frame", true),
            new TargetSpec(3, "picture_frame_b", "fancy_picture_frame", true),
            new TargetSpec(3, "picture_frame_c", "white_photo_frame", true),

            // Phase 4 selects/releases each key through its existing proxy.
            new TargetSpec(4, "key_a", "chest/key", true),
            new TargetSpec(4, "key_b", "chest/key (1)", true),
            new TargetSpec(4, "motorbike_key", "chest/motorbike_key", true),

            new TargetSpec(5, "button_a", "industrial_button", true),
            new TargetSpec(5, "button_b", "red_button", true),
            new TargetSpec(5, "button_c", "alarm_button", true),

            new TargetSpec(6, "breaker_a", "free_switch_handler_ue5", true),
            new TargetSpec(6, "breaker_b", "free_switch_handler_ue5 (1)", true),
            new TargetSpec(6, "breaker_c", "free_switch_handler_ue5 (2)", true)
        };

        private static readonly string[] ChestButtonIds =
            { "blue", "red", "yellow", "green" };

        [MenuItem("Tools/SignVR/Interaction/W7 Setup Phase Adapters")]
        public static void SetupFromMenu()
        {
            RunMenuOperationWithRestoredSceneSetup(
                SetupAndSaveLoadedScene
            );
        }

        [MenuItem("Tools/SignVR/Interaction/W7 Validate Phase Adapters")]
        public static void ValidateFromMenu()
        {
            RunMenuOperationWithRestoredSceneSetup(scene =>
            {
                ValidateLoadedScene(scene);
                Debug.Log(
                    "[W7InteractionPhaseAdaptersSetup] Phase adapter " +
                    "validation passed."
                );
            });
        }

        public static void SetupAndSaveForAutomation()
        {
            SetupAndSaveLoadedScene(OpenInteractionScene());
        }

        public static void SetupAndValidateForAutomationWithoutSaving()
        {
            Scene scene = OpenInteractionScene();
            SetupLoadedScene(scene);
            ValidateLoadedScene(scene);
            Debug.Log(
                "[W7InteractionPhaseAdaptersSetup] Unsaved clean setup " +
                "and validation passed."
            );
        }

        private static void SetupAndSaveLoadedScene(Scene scene)
        {
            SetupLoadedScene(scene);
            ValidateLoadedScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException(
                    "Unity could not save W7 InteractionLab wiring."
                );
            }
            Debug.Log(
                "[W7InteractionPhaseAdaptersSetup] Setup and validation " +
                "completed."
            );
        }

        public static void ValidateForAutomation()
        {
            ValidateLoadedScene(OpenInteractionScene());
            Debug.Log(
                "[W7InteractionPhaseAdaptersSetup] Phase adapter validation " +
                "passed."
            );
        }

        /// <summary>
        /// Marks a strict, test-created temporary InteractionLab asset copy.
        /// Canonical scenes and arbitrary scene paths fail closed.
        /// </summary>
        public static void MarkTestOwnedSceneForAutomation(Scene scene)
        {
            RequireTestScenePath(scene);
            RequireActiveSceneForCreation(scene);
            int markerCount = scene.GetRootGameObjects().Count(item =>
                string.Equals(
                    item.name,
                    TestSceneMarkerName,
                    StringComparison.Ordinal
                ));
            if (markerCount != 0)
            {
                throw new InvalidOperationException(
                    "The test-owned scene marker must be created exactly once."
                );
            }

            GameObject marker = null;
            try
            {
                marker = new GameObject(TestSceneMarkerName);
                if (marker.scene != scene)
                {
                    throw new InvalidOperationException(
                        "The test-owned marker was not created in the " +
                        "active temporary scene."
                    );
                }
            }
            catch
            {
                if (marker != null)
                {
                    Object.DestroyImmediate(marker);
                }
                throw;
            }
        }

        public static void SetupAndValidateTestOwnedSceneWithoutSaving(
            Scene scene)
        {
            RequireTestOwnedScene(scene);
            RequireActiveSceneForCreation(scene);
            SetupSceneTransactional(scene);
            ValidateSceneContents(scene);
        }

        public static void SetupTestOwnedSceneForAutomation(Scene scene)
        {
            RequireTestOwnedScene(scene);
            RequireActiveSceneForCreation(scene);
            SetupSceneTransactional(scene);
        }

        public static void ValidateTestOwnedSceneForAutomation(Scene scene)
        {
            RequireTestOwnedScene(scene);
            ValidateSceneContents(scene);
        }

        /// <summary>
        /// Removes only W7-owned objects and components from the loaded scene.
        /// Tests use this on an unsaved, in-memory InteractionLab instance so
        /// setup cannot pass because of wiring already serialized in the asset.
        /// </summary>
        public static void StripW7OwnedWiringForTests(Scene scene)
        {
            RequireTestOwnedScene(scene);
            RequireActiveSceneForCreation(scene);
            GameObject[] sceneObjects = Enumerate(scene).ToArray();

            foreach (GameObject sceneObject in sceneObjects)
            {
                Component[] components = sceneObject.GetComponents<Component>();
                foreach (Component component in components)
                {
                    if (component != null && IsW7OwnedComponent(component))
                    {
                        if (component is IInteractionOwnedStateTeardown
                            ownedState)
                        {
                            ownedState.ReleaseOwnedStateForEditorTeardown();
                        }
                        Object.DestroyImmediate(component);
                    }
                }
            }

            GameObject[] ownedRoots = sceneObjects
                .Where(item => item != null &&
                    IsW7OwnedObjectName(item.name) &&
                    !HasW7OwnedAncestor(item.transform.parent))
                .ToArray();
            foreach (GameObject ownedRoot in ownedRoots)
            {
                Object.DestroyImmediate(ownedRoot);
            }
        }

        public static int CountW7OwnedWiringForTests(Scene scene)
        {
            RequireTestOwnedScene(scene);
            int count = 0;
            foreach (GameObject sceneObject in Enumerate(scene))
            {
                if (IsW7OwnedObjectName(sceneObject.name))
                {
                    count++;
                }
                count += sceneObject.GetComponents<Component>()
                    .Count(component => component != null &&
                        IsW7OwnedComponent(component));
            }
            return count;
        }

        public static GameObject FindGameObjectInSceneForTests(
            Scene scene,
            string exactName)
        {
            GameObject[] matches = FindGameObjectsInSceneForTests(
                scene,
                exactName
            );
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one scene object named '{exactName}'; found " +
                    $"{matches.Length}."
                );
            }
            return matches[0];
        }

        public static GameObject[] FindGameObjectsInSceneForTests(
            Scene scene,
            string exactName)
        {
            RequireTestOwnedScene(scene);
            if (string.IsNullOrWhiteSpace(exactName))
            {
                throw new ArgumentException(
                    "An exact scene object name is required.",
                    nameof(exactName)
                );
            }

            GameObject[] matches = Enumerate(scene)
                .Where(item => string.Equals(
                    item.name,
                    exactName,
                    StringComparison.Ordinal
                ))
                .ToArray();
            return matches;
        }

        public static void SetupLoadedScene(Scene scene)
        {
            RequireInteractionScene(scene);
            RequireActiveSceneForCreation(scene);
            SetupSceneTransactional(scene);
        }

        private static void SetupSceneTransactional(Scene scene)
        {
            PreflightScene(scene);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure W7 phase adapters");
            try
            {
                SetupLoadedSceneUnchecked(scene);
                ValidateSceneContents(scene);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        private static void SetupLoadedSceneUnchecked(Scene scene)
        {
            Transform runtimeAnchor = FindUniquePath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.RuntimeSystemsAnchorName
            );
            Transform phaseContentAnchor = FindUniquePath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.AnchorsRootName +
                "/PhaseContentAnchor"
            );
            Transform interactionUiAnchor = FindUniquePath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.AnchorsRootName +
                "/InteractionUiAnchor"
            );

            Transform runtimeRoot = EnsureChild(
                runtimeAnchor,
                RuntimeRootName
            );
            Transform proxyRoot = EnsureChild(
                phaseContentAnchor,
                ProxyRootName
            );
            RemoveObsoletePhaseOnePasswordObjects(proxyRoot);

            InteractionPhaseCoordinator coordinator =
                GetOrAdd<InteractionPhaseCoordinator>(runtimeRoot.gameObject);
            InteractionPhaseAdapter[] adapters = EnsureSixAdapters(runtimeRoot);
            RecordForUndo(coordinator);
            coordinator.ConfigureAdapters(adapters);
            Transform[] allowedInteractorRoots =
                ResolveBareHandInteractorRoots(scene, coordinator);
            coordinator.ConfigureAllowedInteractorRoots(
                allowedInteractorRoots
            );

            var adapterByPhase = adapters.ToDictionary(
                adapter => adapter.PhaseId
            );
            var bindingByTarget = new Dictionary<string, InteractionTargetBinding>(
                StringComparer.Ordinal
            );

            foreach (TargetSpec spec in TargetSpecs)
            {
                Transform authoredTarget = FindTarget(scene, spec.ScenePath);
                if (authoredTarget == null)
                {
                    throw new InvalidOperationException(
                        $"W7 target '{spec.ScenePath}' for {spec.TargetId} " +
                        "is missing."
                    );
                }

                InteractionTargetBinding binding =
                    GetOrAdd<InteractionTargetBinding>(
                        authoredTarget.gameObject
                    );
                Behaviour[] interactionBehaviours =
                    FindInteractionBehaviours(authoredTarget);
                RecordForUndo(binding);
                RecordForUndo(interactionBehaviours);
                Collider proxyCollider = null;
                if (spec.CreateTriggerProxy)
                {
                    proxyCollider = EnsureTargetProxy(
                        proxyRoot,
                        spec.TargetId,
                        authoredTarget,
                        binding,
                        allowedInteractorRoots
                    );
                }

                if (spec.RestoreGrabTopology)
                {
                    Collider[] inputColliders = authoredTarget
                        .GetComponentsInChildren<Collider>(true);
                    GameObject[] availabilityObjects = interactionBehaviours
                        .Select(item => item.gameObject)
                        .Where(item =>
                            item != authoredTarget.gameObject &&
                            !item.activeSelf)
                        .Distinct()
                        .ToArray();
                    RecordForUndo(inputColliders);
                    RecordForUndo(availabilityObjects);
                    binding.ConfigureMovable(
                        spec.TargetId,
                        adapterByPhase[spec.InputPhaseId],
                        interactionBehaviours,
                        inputColliders,
                        availabilityObjects
                    );
                }
                else
                {
                    binding.Configure(
                        spec.TargetId,
                        adapterByPhase[spec.InputPhaseId],
                        interactionBehaviours,
                        proxyCollider != null
                            ? new[] { proxyCollider }
                            : Array.Empty<Collider>()
                    );
                }
                bindingByTarget.Add(spec.TargetId, binding);
                EditorUtility.SetDirty(binding);
            }

            PhaseTwoInteractionAdapter phaseTwo =
                (PhaseTwoInteractionAdapter)adapterByPhase[2];
            PhaseFourInteractionAdapter phaseFour =
                (PhaseFourInteractionAdapter)adapterByPhase[4];

            Transform chest = FindTarget(scene, "chest");
            DeterministicTargetStateBinding[] chestButtonStates =
                EnsureChestButtons(
                    proxyRoot,
                    chest,
                    phaseFour,
                    bindingByTarget,
                    allowedInteractorRoots
                );
            EnsurePlatePlacements(
                scene,
                proxyRoot,
                phaseTwo
            );

            InteractionFeedbackPresenter feedback =
                GetOrAdd<InteractionFeedbackPresenter>(runtimeRoot.gameObject);
            Renderer feedbackRenderer = EnsureFeedbackBeacon(
                interactionUiAnchor
            );
            AudioSource feedbackAudio =
                GetOrAdd<AudioSource>(feedbackRenderer.gameObject);
            RecordForUndo(feedbackAudio);
            feedbackAudio.playOnAwake = false;
            RecordForUndo(feedback);
            feedback.Configure(coordinator, feedbackRenderer, feedbackAudio);

            InteractionPlanHintPresenter hints =
                GetOrAdd<InteractionPlanHintPresenter>(runtimeRoot.gameObject);
            TextMesh chestHint = EnsureHintText(
                proxyRoot,
                "W7ChestOrderHint",
                GetFrontPoint(chest, 0.12f) + Vector3.up * 0.28f
            );
            RecordForUndo(hints);
            RecordForUndo(chestHint.gameObject);
            hints.Configure(coordinator, null, chestHint);

            InteractionDeterministicPresentation presentation =
                GetOrAdd<InteractionDeterministicPresentation>(
                    runtimeRoot.gameObject
                );
            DeterministicHingeBinding safeDoor = CreateSafeDoorBinding(scene);
            DeterministicHingeBinding chestLid = CreateChestLidBinding(chest);
            DeterministicHingeBinding[] cabinetDoors =
                CreateCabinetBindings(scene);
            DeterministicHingeBinding finalLeftDoor =
                CreateFinalLeftDoorBinding(scene);
            DeterministicTargetStateBinding[] cabinetButtons =
                CreateTargetStates(
                    bindingByTarget,
                    new[] { "button_a", "button_b", "button_c" },
                    new Vector3(-10f, 0f, 0f)
                );
            DeterministicTargetStateBinding[] breakers =
                CreateTargetStates(
                    bindingByTarget,
                    new[] { "breaker_a", "breaker_b", "breaker_c" },
                    new Vector3(-18f, 0f, 0f)
                );
            PlannedKeyReleaseBinding[] keys = CreateKeyBindings(
                bindingByTarget,
                new[] { "key_a", "key_b", "motorbike_key" }
            );
            RecordForUndo(presentation);
            presentation.Configure(
                coordinator,
                safeDoor,
                chestLid,
                cabinetDoors[0],
                cabinetDoors[1],
                finalLeftDoor,
                chestButtonStates,
                cabinetButtons,
                breakers,
                keys
            );

            EditorUtility.SetDirty(coordinator);
            EditorUtility.SetDirty(feedback);
            EditorUtility.SetDirty(hints);
            EditorUtility.SetDirty(presentation);
        }

        private static void PreflightScene(Scene scene)
        {
            FindUniquePath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.RuntimeSystemsAnchorName
            );
            FindUniquePath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.AnchorsRootName +
                "/PhaseContentAnchor"
            );
            FindUniquePath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.AnchorsRootName +
                "/InteractionUiAnchor"
            );

            foreach (TargetSpec spec in TargetSpecs)
            {
                Transform target = RequireTarget(
                    scene,
                    spec.ScenePath,
                    spec.TargetId
                );
                if (spec.CreateTriggerProxy || spec.InputPhaseId == 2)
                {
                    GetBounds(target);
                }
            }

            Transform safe = RequireTarget(scene, "safe", "safe");
            Transform safeDoor = RequireRelativeTarget(
                safe,
                SafeDoorPath,
                "safe door"
            );
            RequireRelativeTarget(
                safeDoor.parent,
                "RecordingSafeDoorLeftHinge",
                "safe door hinge"
            );
            GetBounds(safe);

            Transform chest = RequireTarget(scene, "chest", "chest");
            Transform chestLid = RequireRelativeTarget(
                chest,
                ChestLidPath,
                "chest lid"
            );
            if (chestLid.parent == null)
            {
                throw new InvalidOperationException(
                    "The exact chest lid has no hinge parent."
                );
            }
            GetBounds(chest);
            GetBounds(chestLid);

            Transform closet = RequireTarget(scene, "closet", "closet");
            Transform closetLeft = RequireRelativeTarget(
                closet,
                ClosetLeftDoorPath,
                "closet left door"
            );
            Transform closetRight = RequireRelativeTarget(
                closet,
                ClosetRightDoorPath,
                "closet right door"
            );
            RequireRelativeTarget(
                closetLeft.parent,
                "RecordingClosetLeftHinge",
                "closet left hinge"
            );
            RequireRelativeTarget(
                closetRight.parent,
                "RecordingClosetRightHinge",
                "closet right hinge"
            );

            Transform door = RequireTarget(scene, "door", "final door");
            Transform finalPanel = RequireRelativeTarget(
                door,
                FinalDoorPanelPath,
                "final door panel"
            );
            if (finalPanel.parent == null)
            {
                throw new InvalidOperationException(
                    "The exact final door panel has no hinge parent."
                );
            }
            GetBounds(finalPanel);

            InteractionPhaseCoordinator[] coordinators = Enumerate(scene)
                .Select(item =>
                    item.GetComponent<InteractionPhaseCoordinator>())
                .Where(item => item != null)
                .ToArray();
            if (coordinators.Length > 1)
            {
                throw new InvalidOperationException(
                    "Preflight found multiple W7 phase coordinators."
                );
            }
            InteractionPhaseCoordinator existingCoordinator =
                coordinators.SingleOrDefault();
            Transform[] handRoots = ResolveBareHandInteractorRoots(
                scene,
                existingCoordinator
            );
            if (!AreValidBareHandRoots(handRoots))
            {
                throw new InvalidOperationException(
                    "Preflight requires exact left/right hand-only " +
                    "interactor roots."
                );
            }
        }

        private static Transform RequireTarget(
            Scene scene,
            string path,
            string label)
        {
            Transform target = FindTarget(scene, path);
            if (target == null)
            {
                throw new InvalidOperationException(
                    $"Preflight target '{label}' is missing at '{path}'."
                );
            }
            return target;
        }

        private static Transform RequireRelativeTarget(
            Transform root,
            string relativePath,
            string label)
        {
            Transform target = root?.Find(relativePath);
            if (target == null)
            {
                throw new InvalidOperationException(
                    $"Preflight {label} is missing at exact path " +
                    $"'{relativePath}'."
                );
            }
            return target;
        }

        public static void ValidateLoadedScene(Scene scene)
        {
            RequireInteractionScene(scene);
            ValidateSceneContents(scene);
        }

        private static void ValidateSceneContents(Scene scene)
        {
            var failures = new List<string>();
            GameObject[] objects = Enumerate(scene).ToArray();

            InteractionPhaseCoordinator[] coordinators = objects
                .Select(item => item.GetComponent<InteractionPhaseCoordinator>())
                .Where(item => item != null)
                .ToArray();
            if (coordinators.Length != 1)
            {
                failures.Add(
                    $"Expected one W7 coordinator; found {coordinators.Length}."
                );
            }
            InteractionPhaseCoordinator coordinator =
                coordinators.Length == 1 ? coordinators[0] : null;

            InteractionPhaseAdapter[] adapters = objects
                .Select(item => item.GetComponent<InteractionPhaseAdapter>())
                .Where(item => item != null)
                .ToArray();
            int[] phases = adapters.Select(adapter => adapter.PhaseId)
                .OrderBy(value => value)
                .ToArray();
            if (!phases.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6 }))
            {
                failures.Add(
                    "W7 adapters must uniquely cover phases 1 through 6."
                );
            }
            if (coordinator != null &&
                (coordinator.PhaseAdapters.Count != 6 ||
                 !coordinator.PhaseAdapters
                     .Where(item => item != null)
                     .Select(item => item.PhaseId)
                     .OrderBy(value => value)
                     .SequenceEqual(new[] { 1, 2, 3, 4, 5, 6 })))
            {
                failures.Add(
                    "Coordinator must reference the six phase adapters."
                );
            }
            ValidateBareHandRoots(coordinator, failures);

            InteractionTargetBinding[] targetBindings = objects
                .Select(item => item.GetComponent<InteractionTargetBinding>())
                .Where(item => item != null)
                .ToArray();
            string[] expectedTargets = TargetSpecs
                .Select(spec => spec.TargetId)
                .Concat(ChestButtonIds)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] actualTargets = targetBindings
                .Select(item => item.TargetId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (!actualTargets.SequenceEqual(expectedTargets))
            {
                failures.Add(
                    "Stable W7 target bindings do not exactly match the 25 " +
                    "required box/coin/plate/frame/chest-button/key/button/" +
                    "breaker IDs."
                );
            }

            var expectedPhaseByTarget = TargetSpecs.ToDictionary(
                item => item.TargetId,
                item => item.InputPhaseId,
                StringComparer.Ordinal
            );
            foreach (string chestButtonId in ChestButtonIds)
            {
                expectedPhaseByTarget.Add(chestButtonId, 4);
            }
            foreach (InteractionTargetBinding binding in targetBindings)
            {
                if (!expectedPhaseByTarget.TryGetValue(
                        binding.TargetId,
                        out int expectedPhase) ||
                    binding.Adapter == null ||
                    binding.PhaseId != expectedPhase)
                {
                    failures.Add(
                        $"Target '{binding.TargetId}' is not bound to its " +
                        "required phase adapter."
                    );
                }
            }
            foreach (TargetSpec spec in TargetSpecs.Where(
                         item => item.RestoreGrabTopology))
            {
                InteractionTargetBinding binding = targetBindings
                    .SingleOrDefault(item => string.Equals(
                        item.TargetId,
                        spec.TargetId,
                        StringComparison.Ordinal
                    ));
                Transform target = FindTarget(scene, spec.ScenePath);
                Collider[] targetColliders = target == null
                    ? Array.Empty<Collider>()
                    : target.GetComponentsInChildren<Collider>(true);
                Rigidbody[] targetBodies = target == null
                    ? Array.Empty<Rigidbody>()
                    : target.GetComponentsInChildren<Rigidbody>(true);
                bool hasInactiveGrabRoot = binding != null &&
                    binding.AvailabilityObjects.Any(item =>
                        item != null &&
                        string.Equals(
                            item.name,
                            "ISDK_HandGrabInteraction",
                            StringComparison.Ordinal
                        ));
                if (binding == null ||
                    !binding.EnablesMovablePhysicsWhenAvailable ||
                    targetBodies.Length != 1 ||
                    targetColliders.Length == 0 ||
                    !new HashSet<Collider>(binding.InputColliders)
                        .SetEquals(targetColliders) ||
                    binding.InteractionBehaviours.Count == 0 ||
                    !hasInactiveGrabRoot)
                {
                    failures.Add(
                        $"Phase 2 coin '{spec.TargetId}' must bind its " +
                        "inactive grab root, grab behaviours, colliders, " +
                        "and movable Rigidbody lifecycle."
                    );
                }
            }

            InteractionTriggerRelay[] relays = objects
                .Select(item => item.GetComponent<InteractionTriggerRelay>())
                .Where(item => item != null)
                .ToArray();
            foreach (InteractionTriggerRelay relay in relays)
            {
                ValidateRelay(
                    relay,
                    relay.InputReceiver,
                    coordinator,
                    failures
                );
            }
            foreach (TargetSpec spec in TargetSpecs.Where(
                         item => item.CreateTriggerProxy))
            {
                InteractionTargetBinding binding = targetBindings
                    .SingleOrDefault(item => string.Equals(
                        item.TargetId,
                        spec.TargetId,
                        StringComparison.Ordinal
                    ));
                string proxyName = "W7Target_" + spec.TargetId;
                GameObject[] proxyObjects = objects.Where(item =>
                    string.Equals(
                        item.name,
                        proxyName,
                        StringComparison.Ordinal
                    )).ToArray();
                Collider proxyCollider = proxyObjects.Length == 1
                    ? proxyObjects[0].GetComponent<Collider>()
                    : null;
                InteractionTriggerRelay proxyRelay =
                    proxyObjects.Length == 1
                        ? proxyObjects[0]
                            .GetComponent<InteractionTriggerRelay>()
                        : null;
                Transform authoredTarget = FindTarget(scene, spec.ScenePath);
                bool authoredTargetHasRelay = authoredTarget != null &&
                    authoredTarget
                        .GetComponentsInChildren<InteractionTriggerRelay>(true)
                        .Length > 0;
                bool hasExclusiveProxyInput = binding != null &&
                    proxyCollider != null &&
                    binding.InputColliders.Count == 1 &&
                    ReferenceEquals(
                        binding.InputColliders[0],
                        proxyCollider
                    );
                if (binding == null ||
                    proxyObjects.Length != 1 ||
                    proxyRelay == null ||
                    proxyRelay.InputReceiver != binding ||
                    relays.Count(item => item.InputReceiver == binding) != 1 ||
                    !hasExclusiveProxyInput ||
                    authoredTargetHasRelay)
                {
                    failures.Add(
                        $"Target '{spec.TargetId}' must use only its exact " +
                        $"'{proxyName}' bare-hand trigger proxy."
                    );
                }
            }
            foreach (string targetId in ChestButtonIds)
            {
                InteractionTargetBinding binding = targetBindings
                    .SingleOrDefault(item => string.Equals(
                        item.TargetId,
                        targetId,
                        StringComparison.Ordinal
                    ));
                if (binding == null ||
                    relays.Count(item => item.InputReceiver == binding) != 1)
                {
                    failures.Add(
                        $"Chest button '{targetId}' requires exactly one " +
                        "bare-hand trigger relay."
                    );
                }
            }

            if (objects.Any(item =>
                    IsObsoletePhaseOnePasswordObjectName(item.name)))
            {
                failures.Add(
                    "Phase 1 password hint and keypad proxies must be absent."
                );
            }

            InteractionPlacementBinding[] placements = objects
                .Select(item => item.GetComponent<InteractionPlacementBinding>())
                .Where(item => item != null)
                .ToArray();
            string[] placementIds = placements
                .Select(item => item.PlateTargetId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (!placementIds.SequenceEqual(
                    new[] { "plate_a", "plate_b", "plate_dragon" }))
            {
                failures.Add(
                    "Phase 2 requires placement bindings for all three plates."
                );
            }
            foreach (InteractionPlacementBinding placement in placements)
            {
                if (placement.Adapter == null ||
                    placement.Adapter.PhaseId != 2 ||
                    placement.PlacementCollider == null ||
                    !placement.PlacementCollider.isTrigger ||
                    placement.SnapPoint == null)
                {
                    failures.Add(
                        $"Placement '{placement.PlateTargetId}' lacks its " +
                        "Phase 2 adapter/collider/snap references."
                    );
                }
            }

            InteractionFeedbackPresenter feedback = objects
                .Select(item => item.GetComponent<InteractionFeedbackPresenter>())
                .FirstOrDefault(item => item != null);
            if (feedback == null || feedback.FeedbackRenderer == null ||
                feedback.Coordinator != coordinator)
            {
                failures.Add(
                    "Immediate visible fallback feedback is not configured."
                );
            }

            InteractionPlanHintPresenter hints = objects
                .Select(item => item.GetComponent<InteractionPlanHintPresenter>())
                .FirstOrDefault(item => item != null);
            if (hints == null || hints.Coordinator != coordinator ||
                hints.ChestOrderText == null)
            {
                failures.Add(
                    "RunPlan chest-order display is missing."
                );
            }

            InteractionDeterministicPresentation presentation = objects
                .Select(item =>
                    item.GetComponent<InteractionDeterministicPresentation>())
                .FirstOrDefault(item => item != null);
            if (presentation != null &&
                presentation.Coordinator != coordinator)
            {
                failures.Add(
                    "Deterministic presentation must reference the coordinator."
                );
            }
            ValidatePresentation(scene, presentation, failures);

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "Invalid W7 phase adapter setup:\n- " +
                    string.Join("\n- ", failures.Distinct())
                );
            }
        }

        private static InteractionPhaseAdapter[] EnsureSixAdapters(
            Transform runtimeRoot)
        {
            InteractionPhaseAdapter[] adapters =
            {
                GetOrAdd<PhaseOneInteractionAdapter>(
                    EnsureChild(runtimeRoot, "Phase1Adapter").gameObject
                ),
                GetOrAdd<PhaseTwoInteractionAdapter>(
                    EnsureChild(runtimeRoot, "Phase2Adapter").gameObject
                ),
                GetOrAdd<PhaseThreeInteractionAdapter>(
                    EnsureChild(runtimeRoot, "Phase3Adapter").gameObject
                ),
                GetOrAdd<PhaseFourInteractionAdapter>(
                    EnsureChild(runtimeRoot, "Phase4Adapter").gameObject
                ),
                GetOrAdd<PhaseFiveInteractionAdapter>(
                    EnsureChild(runtimeRoot, "Phase5Adapter").gameObject
                ),
                GetOrAdd<PhaseSixInteractionAdapter>(
                    EnsureChild(runtimeRoot, "Phase6Adapter").gameObject
                )
            };
            return adapters;
        }

        private static Collider EnsureTargetProxy(
            Transform proxyRoot,
            string targetId,
            Transform authoredTarget,
            InteractionTargetBinding binding,
            Transform[] allowedInteractorRoots)
        {
            Bounds bounds = GetBounds(authoredTarget);
            GameObject proxy = EnsurePrimitive(
                proxyRoot,
                "W7Target_" + targetId,
                PrimitiveType.Cube
            );
            RecordForUndo(proxy.transform);
            proxy.transform.SetPositionAndRotation(
                new Vector3(
                    bounds.center.x,
                    bounds.center.y,
                    bounds.max.z + 0.045f
                ),
                Quaternion.identity
            );
            proxy.transform.localScale = new Vector3(0.08f, 0.08f, 0.025f);
            BoxCollider collider = proxy.GetComponent<BoxCollider>();
            RecordForUndo(collider);
            InteractionTriggerRelay relay =
                GetOrAdd<InteractionTriggerRelay>(proxy);
            RecordForUndo(relay);
            relay.Configure(binding, allowedInteractorRoots);
            EnsureTouchableTriggerBody(proxy);
            EditorUtility.SetDirty(relay);
            return collider;
        }

        private static void RemoveObsoletePhaseOnePasswordObjects(
            Transform proxyRoot)
        {
            Transform[] obsolete = proxyRoot
                .GetComponentsInChildren<Transform>(true)
                .Where(item => item != proxyRoot &&
                    IsObsoletePhaseOnePasswordObjectName(item.name) &&
                    !HasObsoletePhaseOnePasswordAncestor(item.parent))
                .ToArray();
            foreach (Transform item in obsolete)
            {
                Undo.DestroyObjectImmediate(item.gameObject);
            }
        }

        private static bool HasObsoletePhaseOnePasswordAncestor(
            Transform parent)
        {
            Transform current = parent;
            while (current != null)
            {
                if (IsObsoletePhaseOnePasswordObjectName(current.name))
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private static DeterministicTargetStateBinding[] EnsureChestButtons(
            Transform proxyRoot,
            Transform chest,
            PhaseFourInteractionAdapter adapter,
            IDictionary<string, InteractionTargetBinding> bindingByTarget,
            Transform[] allowedInteractorRoots)
        {
            Vector3 origin = GetFrontPoint(chest, 0.08f) +
                Vector3.up * 0.12f;
            var states = new DeterministicTargetStateBinding[
                ChestButtonIds.Length
            ];
            for (int index = 0; index < ChestButtonIds.Length; index++)
            {
                string targetId = ChestButtonIds[index];
                GameObject button = EnsurePrimitive(
                    proxyRoot,
                    "W7ChestButton_" + targetId,
                    PrimitiveType.Cube
                );
                RecordForUndo(button.transform);
                button.transform.SetPositionAndRotation(
                    origin + new Vector3((index - 1.5f) * 0.09f, 0f, 0f),
                    Quaternion.identity
                );
                button.transform.localScale =
                    new Vector3(0.07f, 0.07f, 0.03f);
                BoxCollider collider = button.GetComponent<BoxCollider>();
                RecordForUndo(collider);
                InteractionTargetBinding binding =
                    GetOrAdd<InteractionTargetBinding>(button);
                RecordForUndo(binding);
                binding.Configure(
                    targetId,
                    adapter,
                    Array.Empty<Behaviour>(),
                    new[] { collider }
                );
                InteractionTriggerRelay relay =
                    GetOrAdd<InteractionTriggerRelay>(button);
                RecordForUndo(relay);
                relay.Configure(binding, allowedInteractorRoots);
                EnsureTouchableTriggerBody(button);
                EnsureButtonLabel(button.transform, targetId.ToUpperInvariant());
                bindingByTarget.Add(targetId, binding);
                states[index] = new DeterministicTargetStateBinding();
                states[index].Configure(
                    targetId,
                    button.transform,
                    new Vector3(-12f, 0f, 0f)
                );
                EditorUtility.SetDirty(binding);
                EditorUtility.SetDirty(relay);
            }
            return states;
        }

        private static void EnsurePlatePlacements(
            Scene scene,
            Transform proxyRoot,
            PhaseTwoInteractionAdapter adapter)
        {
            foreach (string plateId in
                     new[] { "plate_dragon", "plate_a", "plate_b" })
            {
                TargetSpec spec = TargetSpecs.First(item =>
                    item.TargetId == plateId
                );
                Transform plate = FindTarget(scene, spec.ScenePath);
                Bounds bounds = GetBounds(plate);
                Transform placementRoot = EnsureChild(
                    proxyRoot,
                    "W7Placement_" + plateId
                );
                RecordForUndo(placementRoot);
                placementRoot.SetPositionAndRotation(
                    bounds.center + Vector3.up * 0.035f,
                    Quaternion.identity
                );
                placementRoot.localScale = Vector3.one;
                BoxCollider collider = GetOrAdd<BoxCollider>(
                    placementRoot.gameObject
                );
                RecordForUndo(collider);
                collider.isTrigger = true;
                collider.size = new Vector3(
                    Mathf.Clamp(bounds.size.x, 0.12f, 0.32f),
                    0.12f,
                    Mathf.Clamp(bounds.size.z, 0.12f, 0.32f)
                );
                Transform snap = EnsureChild(placementRoot, "SnapPoint");
                RecordForUndo(snap);
                snap.SetPositionAndRotation(
                    bounds.center + Vector3.up * 0.055f,
                    plate.rotation
                );
                InteractionPlacementBinding placement =
                    GetOrAdd<InteractionPlacementBinding>(
                        placementRoot.gameObject
                    );
                RecordForUndo(placement);
                placement.Configure(plateId, adapter, collider, snap);
                EditorUtility.SetDirty(placement);
            }
        }

        private static Renderer EnsureFeedbackBeacon(Transform uiAnchor)
        {
            GameObject beacon = EnsurePrimitive(
                uiAnchor,
                "W7InteractionFeedbackBeacon",
                PrimitiveType.Sphere
            );
            RecordForUndo(beacon.transform);
            beacon.transform.localPosition = new Vector3(0f, 1.45f, 1.1f);
            beacon.transform.localRotation = Quaternion.identity;
            beacon.transform.localScale = Vector3.one * 0.09f;
            Collider collider = beacon.GetComponent<Collider>();
            if (collider != null)
            {
                Undo.DestroyObjectImmediate(collider);
            }
            return beacon.GetComponent<Renderer>();
        }

        private static TextMesh EnsureHintText(
            Transform parent,
            string name,
            Vector3 worldPosition)
        {
            Transform root = EnsureChild(parent, name);
            RecordForUndo(root);
            root.SetPositionAndRotation(worldPosition, Quaternion.identity);
            TextMesh text = GetOrAdd<TextMesh>(root.gameObject);
            RecordForUndo(text);
            text.text = string.Empty;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.035f;
            text.fontSize = 64;
            text.color = Color.white;
            EditorUtility.SetDirty(text);
            return text;
        }

        private static TextMesh EnsureButtonLabel(
            Transform button,
            string label)
        {
            Transform labelRoot = EnsureChild(button, "Label");
            RecordForUndo(labelRoot);
            labelRoot.localPosition = new Vector3(0f, 0f, -0.55f);
            labelRoot.localRotation = Quaternion.identity;
            labelRoot.localScale = Vector3.one;
            TextMesh text = GetOrAdd<TextMesh>(labelRoot.gameObject);
            RecordForUndo(text);
            text.text = label;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.12f;
            text.fontSize = 48;
            text.color = Color.black;
            EditorUtility.SetDirty(text);
            return text;
        }

        private static DeterministicHingeBinding CreateSafeDoorBinding(
            Scene scene)
        {
            Transform panel = FindTarget(scene, "safe")?.Find(SafeDoorPath);
            Transform hinge = panel?.parent?.Find(
                "RecordingSafeDoorLeftHinge"
            );
            return CreateHingeBinding(panel, hinge, Vector3.up, 105f);
        }

        private static DeterministicHingeBinding CreateChestLidBinding(
            Transform chest)
        {
            Transform lid = chest?.Find(ChestLidPath);
            if (lid == null)
            {
                return new DeterministicHingeBinding();
            }
            Bounds bounds = GetBounds(lid);
            Transform hinge = EnsureWorldHinge(
                lid.parent,
                "W7ChestLidHinge",
                new Vector3(bounds.center.x, bounds.center.y, bounds.min.z)
            );
            return CreateHingeBinding(lid, hinge, Vector3.right, -100f);
        }

        private static DeterministicHingeBinding[] CreateCabinetBindings(
            Scene scene)
        {
            Transform closet = FindTarget(scene, "closet");
            Transform left = closet?.Find(ClosetLeftDoorPath);
            Transform right = closet?.Find(ClosetRightDoorPath);
            Transform leftHinge = left?.parent?.Find(
                "RecordingClosetLeftHinge"
            );
            Transform rightHinge = right?.parent?.Find(
                "RecordingClosetRightHinge"
            );
            return new[]
            {
                CreateHingeBinding(left, leftHinge, Vector3.up, 105f),
                CreateHingeBinding(right, rightHinge, Vector3.up, -105f)
            };
        }

        private static DeterministicHingeBinding CreateFinalLeftDoorBinding(
            Scene scene)
        {
            Transform door = FindTarget(scene, "door");
            Transform left = door?.Find(FinalDoorPanelPath);
            if (left == null)
            {
                return new DeterministicHingeBinding();
            }
            Bounds bounds = GetBounds(left);
            Transform hinge = EnsureWorldHinge(
                left.parent,
                "W7FinalLeftDoorHinge",
                new Vector3(bounds.min.x, bounds.center.y, bounds.center.z)
            );
            return CreateHingeBinding(left, hinge, Vector3.up, -100f);
        }

        private static DeterministicHingeBinding CreateHingeBinding(
            Transform movingPart,
            Transform hinge,
            Vector3 axis,
            float angle)
        {
            var binding = new DeterministicHingeBinding();
            if (movingPart != null && hinge != null)
            {
                binding.Configure(movingPart, hinge, axis, angle);
            }
            return binding;
        }

        private static DeterministicTargetStateBinding[] CreateTargetStates(
            IReadOnlyDictionary<string, InteractionTargetBinding> bindings,
            IReadOnlyList<string> targetIds,
            Vector3 offset)
        {
            var result = new DeterministicTargetStateBinding[targetIds.Count];
            for (int index = 0; index < targetIds.Count; index++)
            {
                string targetId = targetIds[index];
                result[index] = new DeterministicTargetStateBinding();
                result[index].Configure(
                    targetId,
                    bindings[targetId].transform,
                    offset
                );
            }
            return result;
        }

        private static PlannedKeyReleaseBinding[] CreateKeyBindings(
            IReadOnlyDictionary<string, InteractionTargetBinding> bindings,
            IReadOnlyList<string> targetIds)
        {
            var result = new PlannedKeyReleaseBinding[targetIds.Count];
            for (int index = 0; index < targetIds.Count; index++)
            {
                string targetId = targetIds[index];
                InteractionTargetBinding target = bindings[targetId];
                result[index] = new PlannedKeyReleaseBinding();
                result[index].Configure(
                    targetId,
                    target.gameObject,
                    target.GetComponentsInChildren<Rigidbody>(true),
                    FindInteractionBehaviours(target.transform),
                    target.GetComponentsInChildren<Collider>(true)
                );
            }
            return result;
        }

        private static void ValidateBareHandRoots(
            InteractionPhaseCoordinator coordinator,
            ICollection<string> failures)
        {
            if (coordinator == null)
            {
                return;
            }

            Transform[] roots = coordinator.AllowedInteractorRoots
                .Where(item => item != null)
                .Distinct()
                .ToArray();
            if (!AreValidBareHandRoots(roots))
            {
                failures.Add(
                    "Coordinator requires exactly the left/right hand-only " +
                    "interactor roots; controller roots are forbidden."
                );
            }
        }

        private static bool AreValidBareHandRoots(
            IEnumerable<Transform> configuredRoots)
        {
            Transform[] roots = (configuredRoots ??
                    Enumerable.Empty<Transform>())
                .Where(item => item != null)
                .Distinct()
                .ToArray();
            return roots.Length == 2 &&
                roots.Count(item => IsBareHandInteractorRoot(item, "left")) == 1 &&
                roots.Count(item => IsBareHandInteractorRoot(item, "right")) == 1;
        }

        private static void ValidateRelay(
            InteractionTriggerRelay relay,
            MonoBehaviour expectedReceiver,
            InteractionPhaseCoordinator coordinator,
            ICollection<string> failures)
        {
            if (relay == null || expectedReceiver == null ||
                relay.InputReceiver != expectedReceiver ||
                !relay.IsConfigured || coordinator == null ||
                !SameTransforms(
                    relay.AllowedInteractorRoots,
                    coordinator.AllowedInteractorRoots
                ))
            {
                failures.Add(
                    $"Trigger relay '{relay?.name ?? "<missing>"}' lacks its " +
                    "receiver, trigger collider, or allowed hand roots."
                );
            }

            Rigidbody body = relay != null
                ? relay.GetComponent<Rigidbody>()
                : null;
            if (body == null || body.isKinematic || body.useGravity ||
                body.constraints != RigidbodyConstraints.FreezeAll)
            {
                failures.Add(
                    $"Trigger relay '{relay?.name ?? "<missing>"}' requires " +
                    "a gravity-free, non-kinematic Rigidbody with FreezeAll " +
                    "so Meta's kinematic hand colliders can raise trigger " +
                    "callbacks under the project's default contact-pair mode."
                );
            }
        }

        private static bool SameTransforms(
            IReadOnlyList<Transform> left,
            IReadOnlyList<Transform> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }
            return new HashSet<Transform>(left).SetEquals(right);
        }

        private static bool HasButtonLabel(
            Transform button,
            string expectedText)
        {
            TextMesh label = button?.Find("Label")?.GetComponent<TextMesh>();
            return label != null && string.Equals(
                label.text,
                expectedText,
                StringComparison.Ordinal
            );
        }

        private static void ValidatePresentation(
            Scene scene,
            InteractionDeterministicPresentation presentation,
            ICollection<string> failures)
        {
            if (presentation == null)
            {
                failures.Add("Deterministic phase presentation is missing.");
                return;
            }
            if (!presentation.SafeDoor.IsConfigured)
            {
                failures.Add("Safe-door deterministic hinge is not bound.");
            }
            Transform chest = FindTarget(scene, "chest");
            if (!HasExactMovingPartPath(
                    chest,
                    presentation.ChestLid,
                    ChestLidPath))
            {
                failures.Add(
                    "Chest lid MovingPart must equal the frozen exact path '" +
                    ChestLidPath + "'."
                );
            }
            if (!presentation.CabinetLeftDoor.IsConfigured ||
                !presentation.CabinetRightDoor.IsConfigured)
            {
                failures.Add("Both cabinet deterministic hinges must be bound.");
            }
            Transform door = FindTarget(scene, "door");
            if (!HasExactMovingPartPath(
                    door,
                    presentation.FinalLeftDoor,
                    FinalDoorPanelPath))
            {
                failures.Add(
                    "Final door MovingPart must equal the frozen exact path '" +
                    FinalDoorPanelPath + "'."
                );
            }
            ValidateIds(
                presentation.ChestButtons.Select(item => item.TargetId),
                ChestButtonIds,
                "chest button visuals",
                failures
            );
            ValidateIds(
                presentation.CabinetButtons.Select(item => item.TargetId),
                new[] { "button_a", "button_b", "button_c" },
                "cabinet button visuals",
                failures
            );
            ValidateIds(
                presentation.Breakers.Select(item => item.TargetId),
                new[] { "breaker_a", "breaker_b", "breaker_c" },
                "breaker reset visuals",
                failures
            );
            ValidateIds(
                presentation.PlannedKeys.Select(item => item.TargetId),
                new[] { "key_a", "key_b", "motorbike_key" },
                "planned key release bindings",
                failures
            );
        }

        public static bool HasExactMovingPartPath(
            Transform targetRoot,
            DeterministicHingeBinding binding,
            string expectedRelativePath)
        {
            if (targetRoot == null || binding == null ||
                string.IsNullOrWhiteSpace(expectedRelativePath))
            {
                return false;
            }

            if (!binding.IsConfigured)
            {
                return false;
            }
            Transform movingPart = binding.MovingPart;
            Transform exact = targetRoot.Find(expectedRelativePath);
            return movingPart != null && exact != null &&
                movingPart == exact &&
                string.Equals(
                    GetRelativeHierarchyPath(targetRoot, movingPart),
                    expectedRelativePath,
                    StringComparison.Ordinal
                );
        }

        private static string GetRelativeHierarchyPath(
            Transform root,
            Transform target)
        {
            var names = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return current == root ? string.Join("/", names) : null;
        }

        private static void ValidateIds(
            IEnumerable<string> actual,
            IEnumerable<string> expected,
            string label,
            ICollection<string> failures)
        {
            string[] actualIds = actual
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] expectedIds = expected
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (!actualIds.SequenceEqual(expectedIds))
            {
                failures.Add($"Invalid {label}.");
            }
        }

        private static Behaviour[] FindInteractionBehaviours(Transform root)
        {
            return root.GetComponentsInChildren<Behaviour>(true)
                .Where(behaviour =>
                {
                    Type type = behaviour.GetType();
                    string typeName = type.Name;
                    string typeNamespace = type.Namespace ?? string.Empty;
                    return typeNamespace.StartsWith(
                               "Oculus.Interaction",
                               StringComparison.Ordinal
                           ) &&
                           (typeName.IndexOf(
                                "Interactable",
                                StringComparison.OrdinalIgnoreCase
                            ) >= 0 ||
                            typeName.IndexOf(
                                "Grabbable",
                                StringComparison.OrdinalIgnoreCase
                            ) >= 0);
                })
                .ToArray();
        }

        private static Transform[] ResolveBareHandInteractorRoots(
            Scene scene,
            InteractionPhaseCoordinator coordinator)
        {
            try
            {
                return FindBareHandInteractorRoots(scene);
            }
            catch (InvalidOperationException)
            {
                Transform[] serializedRoots = (coordinator == null
                        ? Array.Empty<Transform>()
                        : coordinator.AllowedInteractorRoots)
                    .Where(item => item != null)
                    .Distinct()
                    .ToArray();
                if (serializedRoots.Length == 2)
                {
                    return serializedRoots;
                }
                throw;
            }
        }

        private static Transform[] FindBareHandInteractorRoots(Scene scene)
        {
            Transform[] transforms = Enumerate(scene)
                .Select(item => item.transform)
                .ToArray();
            Transform left = FindBareHandInteractorRoot(transforms, "left");
            Transform right = FindBareHandInteractorRoot(transforms, "right");
            return new[] { left, right };
        }

        private static Transform FindBareHandInteractorRoot(
            IEnumerable<Transform> transforms,
            string side)
        {
            Transform[] matches = transforms.Where(item =>
                IsBareHandInteractorRoot(item, side)).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one {side} hand-only interactor root; found " +
                    $"{matches.Length}. Bind the Meta hand hierarchy " +
                    "explicitly before W7 setup."
                );
            }
            return matches[0];
        }

        private static bool IsBareHandInteractorRoot(
            Transform item,
            string side)
        {
            if (item == null || string.IsNullOrWhiteSpace(side))
            {
                return false;
            }
            string normalized = new string(item.name
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
            bool legacyNamedRoot =
                normalized.Contains("handinteractors") &&
                normalized.Contains(side) &&
                !normalized.Contains("controller");

            // Meta's Comprehensive Interaction Rig names the actual
            // controller-free subtree "Hand and No Controller" and puts the
            // side on its ComprehensiveInteractorsLeft/Right grandparent.
            // Bind that narrow subtree, never ActiveStatesTrackers or the
            // comprehensive parent that also contains controller interactors.
            string normalizedParent = item.parent == null
                ? string.Empty
                : new string(item.parent.name
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
            string normalizedGrandparent = item.parent?.parent == null
                ? string.Empty
                : new string(item.parent.parent.name
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
            bool comprehensiveHandOnlyRoot =
                normalized == "handandnocontroller" &&
                normalizedParent == "interactors" &&
                normalizedGrandparent.Contains(
                    "comprehensiveinteractors" + side
                );
            return legacyNamedRoot || comprehensiveHandOnlyRoot;
        }

        private static Transform FindTarget(Scene scene, string path)
        {
            string[] segments = path.Split('/');
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(item =>
                string.Equals(item.name, segments[0], StringComparison.Ordinal)
            );
            return segments.Length == 1
                ? root?.transform
                : root?.transform.Find(string.Join(
                    "/",
                    segments.Skip(1)
                ));
        }

        private static Transform EnsureWorldHinge(
            Transform parent,
            string name,
            Vector3 worldPosition)
        {
            if (parent == null)
            {
                return null;
            }
            Transform hinge = EnsureChild(parent, name);
            RecordForUndo(hinge);
            hinge.SetPositionAndRotation(worldPosition, Quaternion.identity);
            hinge.localScale = Vector3.one;
            return hinge;
        }

        private static Vector3 GetFrontPoint(
            Transform target,
            float offset)
        {
            Bounds bounds = GetBounds(target);
            return new Vector3(
                bounds.center.x,
                bounds.center.y,
                bounds.max.z + offset
            );
        }

        private static Bounds GetBounds(Transform root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{root.name} has no renderer bounds."
                );
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        private static Transform FindUniquePath(Scene scene, string path)
        {
            Transform[] matches = Enumerate(scene)
                .Select(item => item.transform)
                .Where(item => string.Equals(
                    GetHierarchyPath(item),
                    path,
                    StringComparison.Ordinal
                ))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one W4 anchor '{path}'; found {matches.Length}."
                );
            }
            return matches[0];
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
            {
                RecordForUndo(child);
                return child;
            }
            var gameObject = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create W7 binding");
            gameObject.transform.SetParent(parent, false);
            return gameObject.transform;
        }

        private static GameObject EnsurePrimitive(
            Transform parent,
            string name,
            PrimitiveType primitiveType)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                RecordForUndo(existing);
                return existing.gameObject;
            }
            GameObject gameObject = GameObject.CreatePrimitive(primitiveType);
            gameObject.name = name;
            Undo.RegisterCreatedObjectUndo(gameObject, "Create W7 proxy");
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static T GetOrAdd<T>(GameObject gameObject)
            where T : Component
        {
            T existing = gameObject.GetComponent<T>();
            if (existing != null)
            {
                RecordForUndo(existing);
                return existing;
            }
            return Undo.AddComponent<T>(gameObject);
        }

        private static void EnsureTouchableTriggerBody(GameObject proxy)
        {
            Rigidbody body = GetOrAdd<Rigidbody>(proxy);
            RecordForUndo(body);
            body.isKinematic = false;
            body.useGravity = false;
            body.detectCollisions = true;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            EditorUtility.SetDirty(body);
        }

        private static void RecordForUndo(Object target)
        {
            if (target != null)
            {
                Undo.RecordObject(target, "Configure W7 phase adapters");
            }
        }

        private static void RecordForUndo(IEnumerable<Object> targets)
        {
            if (targets == null)
            {
                return;
            }
            foreach (Object target in targets)
            {
                RecordForUndo(target);
            }
        }

        private static IEnumerable<GameObject> Enumerate(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform item in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    yield return item.gameObject;
                }
            }
        }

        private static bool IsW7OwnedComponent(Component component)
        {
            string componentNamespace = component.GetType().Namespace ??
                string.Empty;
            return componentNamespace.Equals(
                    "SignVR.Interaction.PhaseAdapters",
                    StringComparison.Ordinal
                ) || componentNamespace.StartsWith(
                    "SignVR.Interaction.PhaseAdapters.",
                    StringComparison.Ordinal
                );
        }

        private static bool IsW7OwnedObjectName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            if (string.Equals(
                    objectName,
                    RuntimeRootName,
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    objectName,
                    ProxyRootName,
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    objectName,
                    "W7ChestOrderHint",
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    objectName,
                    "W7InteractionFeedbackBeacon",
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    objectName,
                    "W7ChestLidHinge",
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    objectName,
                    "W7FinalLeftDoorHinge",
                    StringComparison.Ordinal
                ))
            {
                return true;
            }

            if (IsObsoletePhaseOnePasswordObjectName(objectName))
            {
                return true;
            }

            for (int index = 0; index < TargetSpecs.Length; index++)
            {
                if (string.Equals(
                        objectName,
                        "W7Target_" + TargetSpecs[index].TargetId,
                        StringComparison.Ordinal
                    ))
                {
                    return true;
                }
            }

            for (int index = 0; index < ChestButtonIds.Length; index++)
            {
                if (string.Equals(
                        objectName,
                        "W7ChestButton_" + ChestButtonIds[index],
                        StringComparison.Ordinal
                    ))
                {
                    return true;
                }
            }

            return string.Equals(
                    objectName,
                    "W7Placement_plate_dragon",
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    objectName,
                    "W7Placement_plate_a",
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    objectName,
                    "W7Placement_plate_b",
                    StringComparison.Ordinal
                );
        }

        private static bool IsObsoletePhaseOnePasswordObjectName(
            string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }
            if (string.Equals(
                    objectName,
                    "W7SafePasswordHint",
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    objectName,
                    "W7SafeBackspace",
                    StringComparison.Ordinal
                ) ||
                string.Equals(
                    objectName,
                    "W7SafeSubmit",
                    StringComparison.Ordinal
                ))
            {
                return true;
            }

            for (int digit = 0; digit <= 9; digit++)
            {
                if (string.Equals(
                        objectName,
                        "W7SafeDigit_" + digit,
                        StringComparison.Ordinal
                    ))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasW7OwnedAncestor(Transform parent)
        {
            Transform current = parent;
            while (current != null)
            {
                if (IsW7OwnedObjectName(current.name))
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names);
        }

        private static void RunMenuOperationWithRestoredSceneSetup(
            Action<Scene> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            SceneSetup[] previousSetup =
                EditorSceneManager.GetSceneManagerSetup();
            try
            {
                operation(OpenInteractionScene());
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }

        private static Scene OpenInteractionScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded && string.Equals(
                    scene.path,
                    InteractionLabContract.ScenePath,
                    StringComparison.Ordinal
                ))
            {
                return scene;
            }

            scene = SceneManager.GetSceneByPath(
                InteractionLabContract.ScenePath
            );
            if (scene.IsValid() && scene.isLoaded)
            {
                ActivateSceneForSetup(scene);
                return scene;
            }
            scene = EditorSceneManager.OpenScene(
                InteractionLabContract.ScenePath,
                OpenSceneMode.Single
            );
            ActivateSceneForSetup(scene);
            return scene;
        }

        private static void ActivateSceneForSetup(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "W7 setup could not activate its target scene; no " +
                    "scene object was created."
                );
            }
            if (SceneManager.GetActiveScene() != scene &&
                !SceneManager.SetActiveScene(scene))
            {
                throw new InvalidOperationException(
                    "W7 setup could not activate its target scene; no " +
                    "scene object was created."
                );
            }

            if (SceneManager.GetActiveScene() != scene)
            {
                throw new InvalidOperationException(
                    "W7 setup could not activate its target scene; no " +
                    "scene object was created."
                );
            }
        }

        private static void RequireActiveSceneForCreation(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                SceneManager.GetActiveScene() != scene)
            {
                throw new InvalidOperationException(
                    "W7 scene mutation requires its target scene to be " +
                    "loaded and active before object creation."
                );
            }
        }

        private static void RequireInteractionScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(
                    scene.path,
                    InteractionLabContract.ScenePath,
                    StringComparison.Ordinal
                ))
            {
                throw new InvalidOperationException(
                    $"W7 setup requires {InteractionLabContract.ScenePath}."
                );
            }
        }

        private static void RequireTestScenePath(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "A loaded test-owned W7 scene is required."
                );
            }

            string path = scene.path ?? string.Empty;
            if (!path.StartsWith(
                    TestScenePathPrefix,
                    StringComparison.Ordinal) ||
                !path.EndsWith(
                    TestScenePathSuffix,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "W7 test helpers accept only strict temporary " +
                    "InteractionLab asset copies."
                );
            }

            int ownerIdLength = path.Length -
                TestScenePathPrefix.Length - TestScenePathSuffix.Length;
            string ownerId = ownerIdLength > 0
                ? path.Substring(TestScenePathPrefix.Length, ownerIdLength)
                : string.Empty;
            if (ownerId.Length != 32 || !ownerId.All(Uri.IsHexDigit))
            {
                throw new InvalidOperationException(
                    "The W7 test scene path lacks its exact ownership token."
                );
            }
        }

        private static void RequireTestOwnedScene(Scene scene)
        {
            RequireTestScenePath(scene);
            int markerCount = scene.GetRootGameObjects().Count(item =>
                string.Equals(
                    item.name,
                    TestSceneMarkerName,
                    StringComparison.Ordinal
                ));
            if (markerCount != 1)
            {
                throw new InvalidOperationException(
                    "W7 test helpers require exactly one test-owned marker."
                );
            }
        }

        private readonly struct TargetSpec
        {
            public TargetSpec(
                int inputPhaseId,
                string targetId,
                string scenePath,
                bool createTriggerProxy,
                bool restoreGrabTopology = false)
            {
                InputPhaseId = inputPhaseId;
                TargetId = targetId;
                ScenePath = scenePath;
                CreateTriggerProxy = createTriggerProxy;
                RestoreGrabTopology = restoreGrabTopology;
            }

            public int InputPhaseId { get; }
            public string TargetId { get; }
            public string ScenePath { get; }
            public bool CreateTriggerProxy { get; }
            public bool RestoreGrabTopology { get; }
        }
    }
}
