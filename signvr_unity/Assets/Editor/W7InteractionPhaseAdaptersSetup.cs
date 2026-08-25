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

        private static readonly TargetSpec[] TargetSpecs =
        {
            new TargetSpec(1, "box_stool", "box", true),
            new TargetSpec(1, "box_floor_a", "box (1)", true),
            new TargetSpec(1, "box_floor_b", "box (2)", true),

            new TargetSpec(2, "coin_dragon", "dragon_coin", false),
            new TargetSpec(2, "coin_a", "golden_coin", false),
            new TargetSpec(2, "coin_b", "golden_coin (1)", false),
            new TargetSpec(2, "plate_dragon", "dragon_plate", false),
            new TargetSpec(2, "plate_a", "plate", false),
            new TargetSpec(2, "plate_b", "plate (1)", false),

            new TargetSpec(3, "picture_frame_a", "picture_frame", true),
            new TargetSpec(3, "picture_frame_b", "fancy_picture_frame", true),
            new TargetSpec(3, "picture_frame_c", "white_photo_frame", true),

            // Phase 4 selects/releases the key; Phase 5 accepts using it.
            new TargetSpec(5, "key_a", "chest/key", true),
            new TargetSpec(5, "key_b", "chest/key (1)", true),
            new TargetSpec(5, "motorbike_key", "chest/motorbike_key", true),

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

        public static void SetupLoadedScene(Scene scene)
        {
            RequireInteractionScene(scene);
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

            InteractionPhaseCoordinator coordinator =
                GetOrAdd<InteractionPhaseCoordinator>(runtimeRoot.gameObject);
            InteractionPhaseAdapter[] adapters = EnsureSixAdapters(runtimeRoot);
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

                binding.Configure(
                    spec.TargetId,
                    adapterByPhase[spec.InputPhaseId],
                    FindInteractionBehaviours(authoredTarget),
                    proxyCollider != null
                        ? new[] { proxyCollider }
                        : Array.Empty<Collider>()
                );
                bindingByTarget.Add(spec.TargetId, binding);
                EditorUtility.SetDirty(binding);
            }

            PhaseOneInteractionAdapter phaseOne =
                (PhaseOneInteractionAdapter)adapterByPhase[1];
            PhaseTwoInteractionAdapter phaseTwo =
                (PhaseTwoInteractionAdapter)adapterByPhase[2];
            PhaseFourInteractionAdapter phaseFour =
                (PhaseFourInteractionAdapter)adapterByPhase[4];

            Transform safe = FindTarget(scene, "safe");
            Transform chest = FindTarget(scene, "chest");
            EnsureSafeKeypad(
                proxyRoot,
                safe,
                phaseOne,
                allowedInteractorRoots
            );
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
            feedbackAudio.playOnAwake = false;
            feedback.Configure(coordinator, feedbackRenderer, feedbackAudio);

            InteractionPlanHintPresenter hints =
                GetOrAdd<InteractionPlanHintPresenter>(runtimeRoot.gameObject);
            TextMesh safeHint = EnsureHintText(
                proxyRoot,
                "W7SafePasswordHint",
                GetFrontPoint(safe, 0.12f) + Vector3.up * 0.22f
            );
            TextMesh chestHint = EnsureHintText(
                proxyRoot,
                "W7ChestOrderHint",
                GetFrontPoint(chest, 0.12f) + Vector3.up * 0.28f
            );
            hints.Configure(coordinator, safeHint, chestHint);

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

        public static void ValidateLoadedScene(Scene scene)
        {
            RequireInteractionScene(scene);
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
                if (binding == null ||
                    relays.Count(item => item.InputReceiver == binding) != 1)
                {
                    failures.Add(
                        $"Target '{spec.TargetId}' requires exactly one " +
                        "bare-hand trigger relay."
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

            InteractionDigitBinding[] digits = objects
                .Select(item => item.GetComponent<InteractionDigitBinding>())
                .Where(item => item != null)
                .OrderBy(item => item.Digit)
                .ToArray();
            if (digits.Length != 10 ||
                !digits.Select(item => item.Digit)
                    .SequenceEqual(Enumerable.Range(0, 10)))
            {
                failures.Add("Phase 1 requires one proxy for each digit 0-9.");
            }
            foreach (InteractionDigitBinding digit in digits)
            {
                if (digit.Adapter == null || digit.Adapter.PhaseId != 1 ||
                    digit.InputCollider == null ||
                    !digit.InputCollider.isTrigger ||
                    relays.Count(item => item.InputReceiver == digit) != 1 ||
                    !HasButtonLabel(digit.transform, digit.Digit.ToString()))
                {
                    failures.Add(
                        $"Digit {digit.Digit} lacks its Phase 1 " +
                        "collider/relay chain."
                    );
                }
            }

            InteractionPasswordBackspaceBinding[] backspaces = objects
                .Select(item =>
                    item.GetComponent<InteractionPasswordBackspaceBinding>())
                .Where(item => item != null)
                .ToArray();
            if (backspaces.Length != 1 ||
                backspaces[0].Adapter == null ||
                backspaces[0].Adapter.PhaseId != 1 ||
                backspaces[0].InputCollider == null ||
                !backspaces[0].InputCollider.isTrigger ||
                relays.Count(item =>
                    item.InputReceiver == backspaces[0]) != 1 ||
                !HasButtonLabel(backspaces[0].transform, "*"))
            {
                failures.Add(
                    "Phase 1 requires one '*' backspace collider/relay proxy."
                );
            }

            InteractionPasswordSubmitBinding[] submits = objects
                .Select(item =>
                    item.GetComponent<InteractionPasswordSubmitBinding>())
                .Where(item => item != null)
                .ToArray();
            if (submits.Length != 1 ||
                submits[0].Adapter == null || submits[0].Adapter.PhaseId != 1 ||
                submits[0].InputCollider == null ||
                !submits[0].InputCollider.isTrigger ||
                relays.Count(item => item.InputReceiver == submits[0]) != 1 ||
                !HasButtonLabel(submits[0].transform, "#"))
            {
                failures.Add(
                    "Phase 1 requires one '#' submit collider/relay proxy."
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
                hints.SafePasswordText == null || hints.ChestOrderText == null)
            {
                failures.Add(
                    "RunPlan safe-password and chest-order displays are missing."
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
            ValidatePresentation(presentation, failures);

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
            collider.isTrigger = true;
            InteractionTriggerRelay relay =
                GetOrAdd<InteractionTriggerRelay>(proxy);
            relay.Configure(binding, allowedInteractorRoots);
            EditorUtility.SetDirty(relay);
            return collider;
        }

        private static void EnsureSafeKeypad(
            Transform proxyRoot,
            Transform safe,
            PhaseOneInteractionAdapter adapter,
            Transform[] allowedInteractorRoots)
        {
            Vector3 origin = GetFrontPoint(safe, 0.08f) +
                Vector3.up * 0.05f;
            for (int digit = 0; digit <= 9; digit++)
            {
                int layoutIndex = digit == 0 ? 10 : digit - 1;
                int row = layoutIndex / 3;
                int column = layoutIndex % 3;
                GameObject button = EnsurePrimitive(
                    proxyRoot,
                    $"W7SafeDigit_{digit}",
                    PrimitiveType.Cube
                );
                button.transform.SetPositionAndRotation(
                    origin + new Vector3(
                        (column - 1) * 0.085f,
                        (1.5f - row) * 0.075f,
                        0f
                    ),
                    Quaternion.identity
                );
                button.transform.localScale =
                    new Vector3(0.065f, 0.055f, 0.025f);
                BoxCollider collider = button.GetComponent<BoxCollider>();
                collider.isTrigger = true;
                InteractionDigitBinding digitBinding =
                    GetOrAdd<InteractionDigitBinding>(button);
                digitBinding.Configure(digit, adapter, collider);
                InteractionTriggerRelay digitRelay =
                    GetOrAdd<InteractionTriggerRelay>(button);
                digitRelay.Configure(
                    digitBinding,
                    allowedInteractorRoots
                );
                EnsureButtonLabel(button.transform, digit.ToString());
                EditorUtility.SetDirty(digitBinding);
                EditorUtility.SetDirty(digitRelay);
            }

            GameObject backspace = EnsurePrimitive(
                proxyRoot,
                "W7SafeBackspace",
                PrimitiveType.Cube
            );
            backspace.transform.SetPositionAndRotation(
                origin + new Vector3(-0.085f, -0.1125f, 0f),
                Quaternion.identity
            );
            backspace.transform.localScale =
                new Vector3(0.065f, 0.055f, 0.025f);
            BoxCollider backspaceCollider =
                backspace.GetComponent<BoxCollider>();
            backspaceCollider.isTrigger = true;
            InteractionPasswordBackspaceBinding backspaceBinding =
                GetOrAdd<InteractionPasswordBackspaceBinding>(backspace);
            backspaceBinding.Configure(adapter, backspaceCollider);
            InteractionTriggerRelay backspaceRelay =
                GetOrAdd<InteractionTriggerRelay>(backspace);
            backspaceRelay.Configure(
                backspaceBinding,
                allowedInteractorRoots
            );
            EnsureButtonLabel(backspace.transform, "*");
            EditorUtility.SetDirty(backspaceBinding);
            EditorUtility.SetDirty(backspaceRelay);

            GameObject submit = EnsurePrimitive(
                proxyRoot,
                "W7SafeSubmit",
                PrimitiveType.Cube
            );
            submit.transform.SetPositionAndRotation(
                origin + new Vector3(0.085f, -0.1125f, 0f),
                Quaternion.identity
            );
            submit.transform.localScale =
                new Vector3(0.065f, 0.055f, 0.025f);
            BoxCollider submitCollider = submit.GetComponent<BoxCollider>();
            submitCollider.isTrigger = true;
            InteractionPasswordSubmitBinding submitBinding =
                GetOrAdd<InteractionPasswordSubmitBinding>(submit);
            submitBinding.Configure(adapter, submitCollider);
            InteractionTriggerRelay submitRelay =
                GetOrAdd<InteractionTriggerRelay>(submit);
            submitRelay.Configure(submitBinding, allowedInteractorRoots);
            EnsureButtonLabel(submit.transform, "#");
            EditorUtility.SetDirty(submitBinding);
            EditorUtility.SetDirty(submitRelay);
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
                button.transform.SetPositionAndRotation(
                    origin + new Vector3((index - 1.5f) * 0.09f, 0f, 0f),
                    Quaternion.identity
                );
                button.transform.localScale =
                    new Vector3(0.07f, 0.07f, 0.03f);
                BoxCollider collider = button.GetComponent<BoxCollider>();
                collider.isTrigger = true;
                InteractionTargetBinding binding =
                    GetOrAdd<InteractionTargetBinding>(button);
                binding.Configure(
                    targetId,
                    adapter,
                    Array.Empty<Behaviour>(),
                    new[] { collider }
                );
                InteractionTriggerRelay relay =
                    GetOrAdd<InteractionTriggerRelay>(button);
                relay.Configure(binding, allowedInteractorRoots);
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
                placementRoot.SetPositionAndRotation(
                    bounds.center + Vector3.up * 0.035f,
                    Quaternion.identity
                );
                placementRoot.localScale = Vector3.one;
                BoxCollider collider = GetOrAdd<BoxCollider>(
                    placementRoot.gameObject
                );
                collider.isTrigger = true;
                collider.size = new Vector3(
                    Mathf.Clamp(bounds.size.x, 0.12f, 0.32f),
                    0.12f,
                    Mathf.Clamp(bounds.size.z, 0.12f, 0.32f)
                );
                Transform snap = EnsureChild(placementRoot, "SnapPoint");
                snap.SetPositionAndRotation(
                    bounds.center + Vector3.up * 0.055f,
                    plate.rotation
                );
                InteractionPlacementBinding placement =
                    GetOrAdd<InteractionPlacementBinding>(
                        placementRoot.gameObject
                    );
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
            beacon.transform.localPosition = new Vector3(0f, 1.45f, 1.1f);
            beacon.transform.localRotation = Quaternion.identity;
            beacon.transform.localScale = Vector3.one * 0.09f;
            Object.DestroyImmediate(beacon.GetComponent<Collider>());
            return beacon.GetComponent<Renderer>();
        }

        private static TextMesh EnsureHintText(
            Transform parent,
            string name,
            Vector3 worldPosition)
        {
            Transform root = EnsureChild(parent, name);
            root.SetPositionAndRotation(worldPosition, Quaternion.identity);
            TextMesh text = GetOrAdd<TextMesh>(root.gameObject);
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
            labelRoot.localPosition = new Vector3(0f, 0f, -0.55f);
            labelRoot.localRotation = Quaternion.identity;
            labelRoot.localScale = Vector3.one;
            TextMesh text = GetOrAdd<TextMesh>(labelRoot.gameObject);
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
            Transform lid = chest?.Find(ChestLidPath) ??
                FindNamedDescendant(
                    chest,
                    "lid",
                    "cover",
                    "top"
                );
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
            Transform left = door?.Find(FinalDoorPanelPath) ??
                FindNamedDescendant(door, "left");
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
            bool hasLeft = roots.Any(item =>
                item.name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0
            );
            bool hasRight = roots.Any(item =>
                item.name.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0
            );
            bool allHandOnly = roots.All(item =>
                item.name.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0 &&
                item.name.IndexOf(
                    "interactor",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0 &&
                item.name.IndexOf(
                    "controller",
                    StringComparison.OrdinalIgnoreCase
                ) < 0
            );
            if (roots.Length != 2 || !hasLeft || !hasRight || !allHandOnly)
            {
                failures.Add(
                    "Coordinator requires exactly the left/right hand-only " +
                    "interactor roots; controller roots are forbidden."
                );
            }
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
            if (!presentation.ChestLid.IsConfigured)
            {
                failures.Add(
                    "Chest lid could not be identified by name. Manually bind " +
                    "the lid and hinge in InteractionDeterministicPresentation."
                );
            }
            if (!presentation.CabinetLeftDoor.IsConfigured ||
                !presentation.CabinetRightDoor.IsConfigured)
            {
                failures.Add("Both cabinet deterministic hinges must be bound.");
            }
            if (!presentation.FinalLeftDoor.IsConfigured)
            {
                failures.Add(
                    "Final left-door child could not be identified by name. " +
                    "Manually bind its moving panel and hinge."
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
                Transform[] serializedRoots = coordinator
                    .AllowedInteractorRoots
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
            {
                string normalized = new string(item.name
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
                return normalized.Contains("handinteractors") &&
                    normalized.Contains(side) &&
                    !normalized.Contains("controller");
            }).ToArray();
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

        private static Transform FindNamedDescendant(
            Transform root,
            params string[] tokens)
        {
            if (root == null)
            {
                return null;
            }
            Transform[] matches = root
                .GetComponentsInChildren<Transform>(true)
                .Where(item => item != root)
                .Where(item => tokens.Any(token =>
                    MatchesNameToken(item.name, token)
                ))
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static bool MatchesNameToken(string value, string token)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            int start = value.IndexOf(
                token,
                StringComparison.OrdinalIgnoreCase
            );
            while (start >= 0)
            {
                int end = start + token.Length;
                bool beforeBoundary = start == 0 ||
                    !char.IsLetterOrDigit(value[start - 1]) ||
                    (char.IsLower(value[start - 1]) &&
                     char.IsUpper(value[start]));
                bool afterBoundary = end == value.Length ||
                    !char.IsLetterOrDigit(value[end]) ||
                    (char.IsLower(value[end - 1]) &&
                     char.IsUpper(value[end]));
                if (beforeBoundary && afterBoundary)
                {
                    return true;
                }

                start = value.IndexOf(
                    token,
                    start + 1,
                    StringComparison.OrdinalIgnoreCase
                );
            }
            return false;
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
            return gameObject.GetComponent<T>() ??
                Undo.AddComponent<T>(gameObject);
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
            Scene scene = SceneManager.GetSceneByPath(
                InteractionLabContract.ScenePath
            );
            if (scene.IsValid() && scene.isLoaded)
            {
                return scene;
            }
            return EditorSceneManager.OpenScene(
                InteractionLabContract.ScenePath,
                OpenSceneMode.Single
            );
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

        private readonly struct TargetSpec
        {
            public TargetSpec(
                int inputPhaseId,
                string targetId,
                string scenePath,
                bool createTriggerProxy)
            {
                InputPhaseId = inputPhaseId;
                TargetId = targetId;
                ScenePath = scenePath;
                CreateTriggerProxy = createTriggerProxy;
            }

            public int InputPhaseId { get; }
            public string TargetId { get; }
            public string ScenePath { get; }
            public bool CreateTriggerProxy { get; }
        }
    }
}
