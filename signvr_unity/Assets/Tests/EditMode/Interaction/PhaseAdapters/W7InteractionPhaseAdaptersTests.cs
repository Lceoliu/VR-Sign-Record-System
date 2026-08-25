using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class W7InteractionPhaseAdaptersTests
    {
        private const string RuntimeAssembly = "Assembly-CSharp";
        private const string AdapterNamespace =
            "SignVR.Interaction.PhaseAdapters.";
        private const string ValidatorTypeName =
            "SignVR.Editor.Interaction.W7InteractionPhaseAdaptersSetup, " +
            "Assembly-CSharp-Editor";

        [Test]
        public void AllSixAdaptersExposeAuditableLifecycleAndInputApi()
        {
            string[] adapterNames =
            {
                "PhaseOneInteractionAdapter",
                "PhaseTwoInteractionAdapter",
                "PhaseThreeInteractionAdapter",
                "PhaseFourInteractionAdapter",
                "PhaseFiveInteractionAdapter",
                "PhaseSixInteractionAdapter"
            };

            foreach (string adapterName in adapterNames)
            {
                Type type = Type.GetType(
                    AdapterNamespace + adapterName + ", " + RuntimeAssembly,
                    throwOnError: true
                );
                AssertPublicInstanceMethod(type, "Configure");
                AssertPublicInstanceMethod(type, "Reset");
                AssertPublicInstanceMethod(type, "Enable");
                AssertPublicInstanceMethod(type, "Disable");
                AssertPublicInstanceMethod(type, "AcceptInput");
                AssertPublicInstanceMethod(type, "AcceptTarget");

                Assert.That(type.GetEvent("InputAccepted"), Is.Not.Null);
                Assert.That(type.GetEvent("InteractionError"), Is.Not.Null);
                Assert.That(type.GetEvent("TaskProgressReset"), Is.Not.Null);
                Assert.That(type.GetEvent("Completed"), Is.Not.Null);
                Assert.That(type.GetEvent("ResetPerformed"), Is.Not.Null);
            }

            Type backspace = RuntimeType(
                "InteractionPasswordBackspaceBinding"
            );
            AssertPublicInstanceMethod(backspace, "Configure");
            AssertPublicInstanceMethod(backspace, "AcceptInput");
            AssertPublicInstanceMethod(backspace, "Poke");
            AssertPublicInstanceMethod(backspace, "Trigger");
            Assert.That(
                backspace.GetInterface("IInteractionTriggerInput"),
                Is.Not.Null
            );
        }

        [Test]
        public void InteractionLabPassesW7AdapterAndBindingValidator()
        {
            Type validator = Type.GetType(
                ValidatorTypeName,
                throwOnError: true
            );
            MethodInfo method = validator.GetMethod(
                "ValidateForAutomation",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method, Is.Not.Null);

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

        [Test]
        public void CoordinatorNeedsW1SnapshotAndSelfLocksCompletedTask()
        {
            object root = CreateGameObject("W7SnapshotAuthorityTest");
            try
            {
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(GetTransform(root));
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);

                Assert.That(
                    coordinatorType.GetProperty("CurrentPhaseId")
                        .GetValue(coordinator),
                    Is.Null
                );
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False
                );

                SynchronizePhase(coordinator, 1);
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.True
                );

                AcceptTarget(coordinator, 1, "box_stool");
                for (int digit = 1; digit <= 4; digit++)
                {
                    AcceptInput(
                        coordinator,
                        1,
                        CoreType("PhaseInput").GetMethod("Digit").Invoke(
                            null,
                            new object[] { digit }
                        )
                    );
                }
                AcceptInput(
                    coordinator,
                    1,
                    CoreType("PhaseInput").GetMethod("Submit")
                        .Invoke(null, null)
                );

                Assert.That(
                    coordinatorType.GetProperty("CurrentPhaseId")
                        .GetValue(coordinator),
                    Is.EqualTo(1)
                );
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False
                );

                SynchronizePhase(coordinator, 2);

                Assert.That(
                    coordinatorType.GetProperty("CurrentPhaseId")
                        .GetValue(coordinator),
                    Is.EqualTo(2)
                );
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.True
                );

                coordinatorType.GetProperty("enabled")
                    .SetValue(coordinator, false);
                coordinatorType.GetProperty("enabled")
                    .SetValue(coordinator, true);
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.False,
                    "OnEnable must not bypass the last W1 snapshot gate."
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                Assert.That(
                    coordinatorType.GetProperty("IsEnabled")
                        .GetValue(coordinator),
                    Is.True
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void TriggerRelayAcceptsOnlyConfiguredBareHandRoot()
        {
            object root = CreateGameObject("W7BareHandRelayTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 1);

                object target = CreateGameObject("box_stool");
                SetParent(GetTransform(target), rootTransform);
                object binding = AddComponent(
                    target,
                    RuntimeType("InteractionTargetBinding")
                );
                binding.GetType().GetMethod("Configure").Invoke(
                    binding,
                    new object[]
                    {
                        "box_stool",
                        adapters.GetValue(0),
                        null,
                        null
                    }
                );

                object handRoot = CreateGameObject("HandInteractorsLeft");
                SetParent(GetTransform(handRoot), rootTransform);
                object handColliderObject = CreateGameObject("HandCapsule");
                SetParent(GetTransform(handColliderObject), GetTransform(handRoot));
                object handCollider = AddComponent(
                    handColliderObject,
                    UnityPhysicsType("BoxCollider")
                );
                object stranger = CreateGameObject("UntrustedCollider");
                SetParent(GetTransform(stranger), rootTransform);
                object strangerCollider = AddComponent(
                    stranger,
                    UnityPhysicsType("BoxCollider")
                );

                object relayObject = CreateGameObject("Relay");
                SetParent(GetTransform(relayObject), rootTransform);
                AddComponent(relayObject, UnityPhysicsType("BoxCollider"));
                object relay = AddComponent(
                    relayObject,
                    RuntimeType("InteractionTriggerRelay")
                );
                Array allowedRoots = Array.CreateInstance(
                    UnityType("Transform"),
                    1
                );
                allowedRoots.SetValue(GetTransform(handRoot), 0);
                relay.GetType().GetMethod("Configure").Invoke(
                    relay,
                    new object[] { binding, allowedRoots, 0.05f }
                );

                Assert.That(
                    relay.GetType().GetMethod("IsAllowedInteractor").Invoke(
                        relay,
                        new[] { strangerCollider }
                    ),
                    Is.False
                );
                Assert.That(
                    relay.GetType().GetMethod("AcceptTrigger").Invoke(
                        relay,
                        new[] { strangerCollider }
                    ),
                    Is.Null
                );
                object accepted = relay.GetType()
                    .GetMethod("AcceptTrigger")
                    .Invoke(relay, new[] { handCollider });
                Assert.That(accepted, Is.Not.Null);
                Assert.That(
                    accepted.GetType().GetProperty("Accepted")
                        .GetValue(accepted),
                    Is.True
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PlacementIgnoresNonCoinsAndDeduplicatesCoinColliders()
        {
            object root = CreateGameObject("W7PlacementDedupeTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 2);

                object placementObject = CreateGameObject("plate_a");
                SetParent(GetTransform(placementObject), rootTransform);
                object placementCollider = AddComponent(
                    placementObject,
                    UnityPhysicsType("BoxCollider")
                );
                object snap = CreateGameObject("SnapPoint");
                SetParent(GetTransform(snap), GetTransform(placementObject));
                object placement = AddComponent(
                    placementObject,
                    RuntimeType("InteractionPlacementBinding")
                );
                placement.GetType().GetMethod("Configure").Invoke(
                    placement,
                    new object[]
                    {
                        "plate_a",
                        adapters.GetValue(1),
                        placementCollider,
                        GetTransform(snap)
                    }
                );

                object ignored = CreateBoundTarget(
                    rootTransform,
                    adapters.GetValue(1),
                    "plate_dragon"
                );
                object ignoredCollider = AddComponent(
                    ignored,
                    UnityPhysicsType("BoxCollider")
                );
                object coin = CreateBoundTarget(
                    rootTransform,
                    adapters.GetValue(1),
                    "coin_dragon"
                );
                object firstCollider = AddComponent(
                    coin,
                    UnityPhysicsType("BoxCollider")
                );
                object secondCollider = AddComponent(
                    coin,
                    UnityPhysicsType("BoxCollider")
                );

                Assert.That(
                    placement.GetType().GetMethod("AcceptTrigger").Invoke(
                        placement,
                        new[] { ignoredCollider }
                    ),
                    Is.Null
                );
                object first = placement.GetType()
                    .GetMethod("AcceptTrigger")
                    .Invoke(placement, new[] { firstCollider });
                object duplicate = placement.GetType()
                    .GetMethod("AcceptTrigger")
                    .Invoke(placement, new[] { secondCollider });
                Assert.That(first, Is.Not.Null);
                Assert.That(
                    first.GetType().GetProperty("InteractionError")
                        .GetValue(first),
                    Is.True,
                    "coin_dragon on plate_a is intentionally the wrong pair."
                );
                Assert.That(duplicate, Is.Null);
                Assert.That(
                    placement.GetType().GetMethod(
                        "AcceptPlacement",
                        new[] { typeof(string) }
                    ).Invoke(placement, new object[] { "box_stool" }),
                    Is.Null
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PlanHintsFollowErrorCompletionAndGiveUpLifecycle()
        {
            object root = CreateGameObject("W7HintLifecycleTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                object plan = CreateRunPlan();
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { plan }
                );

                object safeText = CreateTextMesh(rootTransform, "SafeHint");
                object chestText = CreateTextMesh(rootTransform, "ChestHint");
                object hints = AddComponent(
                    root,
                    RuntimeType("InteractionPlanHintPresenter")
                );
                hints.GetType().GetMethod("Configure").Invoke(
                    hints,
                    new[] { coordinator, safeText, chestText }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 1);

                AcceptTarget(coordinator, 1, "box_stool");
                Assert.That(IsComponentGameObjectActive(safeText), Is.True);
                AcceptInput(
                    coordinator,
                    1,
                    CoreType("PhaseInput").GetMethod("Submit")
                        .Invoke(null, null)
                );
                Assert.That(IsComponentGameObjectActive(safeText), Is.False);

                AcceptTarget(coordinator, 1, "box_stool");
                for (int digit = 1; digit <= 4; digit++)
                {
                    AcceptInput(
                        coordinator,
                        1,
                        CoreType("PhaseInput").GetMethod("Digit").Invoke(
                            null,
                            new object[] { digit }
                        )
                    );
                }
                AcceptInput(
                    coordinator,
                    1,
                    CoreType("PhaseInput").GetMethod("Submit")
                        .Invoke(null, null)
                );
                Assert.That(IsComponentGameObjectActive(safeText), Is.False);

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { plan }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 1);
                AcceptTarget(coordinator, 1, "box_stool");
                SynchronizePhase(coordinator, 1, giveUpAvailable: true);
                GiveUp(coordinator, 1);
                Assert.That(IsComponentGameObjectActive(safeText), Is.False);

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { plan }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 3, giveUpAvailable: true);
                GiveUp(coordinator, 3);
                Assert.That(IsComponentGameObjectActive(chestText), Is.True);

                SynchronizePhase(coordinator, 4);
                foreach (string targetId in
                         new[] { "blue", "red", "yellow", "green" })
                {
                    AcceptTarget(coordinator, 4, targetId);
                }
                Assert.That(IsComponentGameObjectActive(chestText), Is.False);

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { plan }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 3, giveUpAvailable: true);
                GiveUp(coordinator, 3);
                Assert.That(IsComponentGameObjectActive(chestText), Is.True);
                SynchronizePhase(coordinator, 4, giveUpAvailable: true);
                GiveUp(coordinator, 4);
                Assert.That(IsComponentGameObjectActive(chestText), Is.False);
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PhaseFourGiveUpOpensChestAndReleasesOnlyPlannedKey()
        {
            object root = CreateGameObject("W7PhaseFourGiveUpFallbackTest");
            try
            {
                object rootTransform = GetTransform(root);
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );

                object lid = CreateVisual(rootTransform, "ChestLid");
                object hinge = CreateVisual(rootTransform, "ChestHinge");
                Type hingeType = RuntimeType("DeterministicHingeBinding");
                object chestHinge = Activator.CreateInstance(hingeType);
                hingeType.GetMethod("Configure").Invoke(
                    chestHinge,
                    new object[]
                    {
                        lid,
                        hinge,
                        CreateVector3(1f, 0f, 0f),
                        -90f
                    }
                );

                object key = CreateGameObject("key_a");
                SetParent(GetTransform(key), rootTransform);
                object keyBody = AddComponent(
                    key,
                    UnityPhysicsType("Rigidbody")
                );
                object keyCollider = AddComponent(
                    key,
                    UnityPhysicsType("BoxCollider")
                );
                Type keyBindingType = RuntimeType(
                    "PlannedKeyReleaseBinding"
                );
                object keyBinding = Activator.CreateInstance(keyBindingType);
                Array bodies = TypedArray(
                    UnityPhysicsType("Rigidbody"),
                    keyBody
                );
                Array behaviours = Array.CreateInstance(
                    UnityType("Behaviour"),
                    0
                );
                Array colliders = TypedArray(
                    UnityPhysicsType("Collider"),
                    keyCollider
                );
                keyBindingType.GetMethod("Configure").Invoke(
                    keyBinding,
                    new object[]
                    {
                        "key_a",
                        key,
                        bodies,
                        behaviours,
                        colliders
                    }
                );

                Type presentationType = RuntimeType(
                    "InteractionDeterministicPresentation"
                );
                object presentation = AddComponent(root, presentationType);
                Type stateType = RuntimeType(
                    "DeterministicTargetStateBinding"
                );
                Array noStates = Array.CreateInstance(stateType, 0);
                Array keys = TypedArray(keyBindingType, keyBinding);
                presentationType.GetMethod("Configure").Invoke(
                    presentation,
                    new object[]
                    {
                        coordinator,
                        Activator.CreateInstance(hingeType),
                        chestHinge,
                        Activator.CreateInstance(hingeType),
                        Activator.CreateInstance(hingeType),
                        Activator.CreateInstance(hingeType),
                        noStates,
                        noStates,
                        noStates,
                        keys
                    }
                );

                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                SynchronizePhase(coordinator, 4, giveUpAvailable: true);
                object result = GiveUp(coordinator, 4);

                Assert.That(
                    result.GetType().GetProperty("PhaseGivenUp")
                        .GetValue(result),
                    Is.True
                );
                Assert.That(
                    RotationAngleFromIdentity(lid),
                    Is.GreaterThan(0.1f)
                );
                Assert.That(
                    keyBody.GetType().GetProperty("isKinematic")
                        .GetValue(keyBody),
                    Is.False
                );
                Assert.That(
                    keyCollider.GetType().GetProperty("enabled")
                        .GetValue(keyCollider),
                    Is.True
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void PhaseFiveProgressResetRestoresAllCabinetButtonVisuals()
        {
            object root = CreateGameObject("W7PhaseFiveVisualResetTest");
            try
            {
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                object rootTransform = GetTransform(root);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );

                object buttonA = CreateVisual(rootTransform, "button_a");
                object buttonB = CreateVisual(rootTransform, "button_b");
                object buttonC = CreateVisual(rootTransform, "button_c");
                Array cabinetBindings = CreateTargetStateBindings(
                    new[] { buttonA, buttonB, buttonC },
                    new[] { "button_a", "button_b", "button_c" }
                );
                ConfigurePresentation(
                    root,
                    coordinator,
                    cabinetBindings
                );

                object runPlan = CreateRunPlan();
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { runPlan }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                AdvanceToPhaseFive(coordinator);

                AcceptTarget(coordinator, 5, "key_a");
                AcceptTarget(coordinator, 5, "button_a");
                AcceptTarget(coordinator, 5, "button_b");
                Assert.That(
                    RotationAngleFromIdentity(buttonA),
                    Is.GreaterThan(0.1f)
                );
                Assert.That(
                    RotationAngleFromIdentity(buttonB),
                    Is.GreaterThan(0.1f)
                );

                object resetResult = AcceptTarget(
                    coordinator,
                    5,
                    "button_b"
                );

                Assert.That(
                    (bool)resetResult.GetType()
                        .GetProperty("ProgressReset")
                        .GetValue(resetResult),
                    Is.True
                );
                Assert.That(
                    (int)resetResult.GetType()
                        .GetProperty("Progress")
                        .GetValue(resetResult),
                    Is.EqualTo(1)
                );
                Assert.That(
                    RotationAngleFromIdentity(buttonA),
                    Is.LessThan(0.001f)
                );
                Assert.That(
                    RotationAngleFromIdentity(buttonB),
                    Is.LessThan(0.001f)
                );
                Assert.That(
                    RotationAngleFromIdentity(buttonC),
                    Is.LessThan(0.001f)
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        [Test]
        public void DisabledPresentationDoesNotRespondToCoordinatorResults()
        {
            object root = CreateGameObject("W7DisabledPresentationTest");
            try
            {
                Type coordinatorType = RuntimeType(
                    "InteractionPhaseCoordinator"
                );
                object coordinator = AddComponent(root, coordinatorType);
                object rootTransform = GetTransform(root);
                Array adapters = CreateSixAdapters(rootTransform);
                coordinatorType.GetMethod("ConfigureAdapters").Invoke(
                    coordinator,
                    new object[] { adapters }
                );
                object buttonA = CreateVisual(rootTransform, "button_a");
                Array bindings = CreateTargetStateBindings(
                    new[] { buttonA },
                    new[] { "button_a" }
                );
                object presentation = ConfigurePresentation(
                    root,
                    coordinator,
                    bindings
                );
                coordinatorType.GetMethod("Configure").Invoke(
                    coordinator,
                    new[] { CreateRunPlan() }
                );
                coordinatorType.GetMethod("Enable").Invoke(coordinator, null);
                AdvanceToPhaseFive(coordinator);
                presentation.GetType().GetProperty("enabled")
                    .SetValue(presentation, false);

                AcceptTarget(coordinator, 5, "key_a");
                AcceptTarget(coordinator, 5, "button_a");

                Assert.That(
                    RotationAngleFromIdentity(buttonA),
                    Is.LessThan(0.001f)
                );
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        private static Array CreateSixAdapters(object parent)
        {
            Type adapterType = RuntimeType("InteractionPhaseAdapter");
            Array adapters = Array.CreateInstance(adapterType, 6);
            string[] names =
            {
                "PhaseOneInteractionAdapter",
                "PhaseTwoInteractionAdapter",
                "PhaseThreeInteractionAdapter",
                "PhaseFourInteractionAdapter",
                "PhaseFiveInteractionAdapter",
                "PhaseSixInteractionAdapter"
            };
            for (int index = 0; index < names.Length; index++)
            {
                object child = CreateGameObject(names[index]);
                SetParent(GetTransform(child), parent);
                adapters.SetValue(
                    AddComponent(child, RuntimeType(names[index])),
                    index
                );
            }
            return adapters;
        }

        private static object CreateVisual(
            object parent,
            string name)
        {
            object visual = CreateGameObject(name);
            object transform = GetTransform(visual);
            SetParent(transform, parent);
            return transform;
        }

        private static object CreateBoundTarget(
            object parent,
            object adapter,
            string targetId)
        {
            object target = CreateGameObject(targetId);
            SetParent(GetTransform(target), parent);
            object binding = AddComponent(
                target,
                RuntimeType("InteractionTargetBinding")
            );
            binding.GetType().GetMethod("Configure").Invoke(
                binding,
                new object[] { targetId, adapter, null, null }
            );
            return target;
        }

        private static object CreateTextMesh(object parent, string name)
        {
            object textObject = CreateGameObject(name);
            SetParent(GetTransform(textObject), parent);
            return AddComponent(
                textObject,
                Type.GetType(
                    "UnityEngine.TextMesh, UnityEngine.TextRenderingModule",
                    throwOnError: true
                )
            );
        }

        private static bool IsComponentGameObjectActive(object component)
        {
            object gameObject = component.GetType().GetProperty("gameObject")
                .GetValue(component);
            return (bool)gameObject.GetType().GetProperty("activeSelf")
                .GetValue(gameObject);
        }

        private static Array TypedArray(Type elementType, params object[] values)
        {
            Array result = Array.CreateInstance(elementType, values.Length);
            for (int index = 0; index < values.Length; index++)
            {
                result.SetValue(values[index], index);
            }
            return result;
        }

        private static Array CreateTargetStateBindings(
            object[] targets,
            string[] targetIds)
        {
            Type bindingType = RuntimeType(
                "DeterministicTargetStateBinding"
            );
            Array bindings = Array.CreateInstance(
                bindingType,
                targets.Length
            );
            MethodInfo configure = bindingType.GetMethod("Configure");
            for (int index = 0; index < targets.Length; index++)
            {
                object binding = Activator.CreateInstance(bindingType);
                configure.Invoke(
                    binding,
                    new object[]
                    {
                        targetIds[index],
                        targets[index],
                        CreateVector3(-10f, 0f, 0f)
                    }
                );
                bindings.SetValue(binding, index);
            }
            return bindings;
        }

        private static object ConfigurePresentation(
            object root,
            object coordinator,
            Array cabinetBindings)
        {
            Type presentationType = RuntimeType(
                "InteractionDeterministicPresentation"
            );
            object presentation = AddComponent(root, presentationType);
            Type hingeType = RuntimeType("DeterministicHingeBinding");
            Type stateType = RuntimeType(
                "DeterministicTargetStateBinding"
            );
            Type keyType = RuntimeType("PlannedKeyReleaseBinding");
            Array noStates = Array.CreateInstance(stateType, 0);
            Array noKeys = Array.CreateInstance(keyType, 0);
            presentationType.GetMethod("Configure").Invoke(
                presentation,
                new object[]
                {
                    coordinator,
                    Activator.CreateInstance(hingeType),
                    Activator.CreateInstance(hingeType),
                    Activator.CreateInstance(hingeType),
                    Activator.CreateInstance(hingeType),
                    Activator.CreateInstance(hingeType),
                    noStates,
                    cabinetBindings,
                    noStates,
                    noKeys
                }
            );
            return presentation;
        }

        private static object CreateRunPlan()
        {
            string[] sentenceIds =
                { "001", "004", "013", "016", "025", "026" };
            Type contentType = CoreType("InstructionContentReference");
            Type phaseType = CoreType("RunPhasePlan");
            Type variantCatalogType = CoreType("TaskVariantCatalog");
            Array phases = Array.CreateInstance(phaseType, sentenceIds.Length);
            MethodInfo forSentence = variantCatalogType.GetMethod(
                "ForSentence",
                BindingFlags.Public | BindingFlags.Static
            );
            for (int index = 0; index < sentenceIds.Length; index++)
            {
                string sentenceId = sentenceIds[index];
                object content = Activator.CreateInstance(
                    contentType,
                    new object[]
                    {
                        index + 1,
                        sentenceId,
                        "wang",
                        "take_" + sentenceId,
                        new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
                        0,
                        "content/" + sentenceId + ".mp4",
                        new string('a', 64)
                    }
                );
                object variant = forSentence.Invoke(
                    null,
                    new object[] { sentenceId }
                );
                phases.SetValue(
                    Activator.CreateInstance(
                        phaseType,
                        new[] { (object)(index + 1), content, variant }
                    ),
                    index
                );
            }

            Type assistanceConditionType = CoreType(
                "AssistanceCondition"
            );
            object assignment = Activator.CreateInstance(
                CoreType("AssistanceAssignment"),
                new[]
                {
                    Enum.Parse(assistanceConditionType, "SignOnly"),
                    (object)0,
                    (object)0
                }
            );
            object password = Activator.CreateInstance(
                CoreType("SafePassword"),
                new object[] { new[] { 1, 2, 3, 4 } }
            );
            object chestOrder = Activator.CreateInstance(
                CoreType("ChestButtonOrder"),
                new object[]
                {
                    new[] { "blue", "red", "yellow", "green" }
                }
            );
            return Activator.CreateInstance(
                CoreType("RunPlan"),
                new object[]
                {
                    "pilot-20260825",
                    "P001",
                    "run_w7_phase5_visual_reset",
                    "app_w7_phase5_visual_reset",
                    new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
                    "1.0.0-test",
                    "335befa",
                    12345,
                    assignment,
                    password,
                    chestOrder,
                    phases
                }
            );
        }

        private static void AdvanceToPhaseFive(object coordinator)
        {
            SynchronizePhase(coordinator, 1);
            AcceptTarget(coordinator, 1, "box_stool");
            for (int digit = 1; digit <= 4; digit++)
            {
                AcceptInput(
                    coordinator,
                    1,
                    CoreType("PhaseInput").GetMethod("Digit").Invoke(
                        null,
                        new object[] { digit }
                    )
                );
            }
            AcceptInput(
                coordinator,
                1,
                CoreType("PhaseInput").GetMethod("Submit").Invoke(null, null)
            );
            SynchronizePhase(coordinator, 2);
            AcceptInput(
                coordinator,
                2,
                CoreType("PhaseInput").GetMethod("Pair").Invoke(
                    null,
                    new object[] { "coin_dragon", "plate_dragon" }
                )
            );
            SynchronizePhase(coordinator, 3);
            AcceptTarget(coordinator, 3, "picture_frame_a");
            SynchronizePhase(coordinator, 4);
            foreach (string targetId in
                     new[] { "blue", "red", "yellow", "green" })
            {
                AcceptTarget(coordinator, 4, targetId);
            }
            SynchronizePhase(coordinator, 5);
        }

        private static void SynchronizePhase(
            object coordinator,
            int phaseId,
            bool giveUpAvailable = false)
        {
            object snapshot = CreatePhaseSnapshot(
                phaseId,
                giveUpAvailable
            );
            coordinator.GetType().GetMethod("Synchronize").Invoke(
                coordinator,
                new[] { snapshot }
            );
        }

        private static object GiveUp(object coordinator, int phaseId)
        {
            object snapshot = CreatePhaseSnapshot(
                phaseId,
                giveUpAvailable: true
            );
            return coordinator.GetType().GetMethod("GiveUpCurrentPhase")
                .Invoke(coordinator, new[] { snapshot });
        }

        private static object CreatePhaseSnapshot(
            int phaseId,
            bool giveUpAvailable)
        {
            Type snapshotType = CoreType("PhaseExecutionSnapshot");
            Type phaseStateType = CoreType("PhaseState");
            object state = Enum.Parse(
                phaseStateType,
                giveUpAvailable ? "Active" : "FirstPlayback"
            );
            return Activator.CreateInstance(
                snapshotType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    phaseId,
                    state,
                    null,
                    giveUpAvailable,
                    giveUpAvailable,
                    false,
                    0,
                    0,
                    TimeSpan.Zero
                },
                culture: null
            );
        }

        private static object AcceptTarget(
            object coordinator,
            int phaseId,
            string targetId)
        {
            object input = CoreType("PhaseInput").GetMethod("Target").Invoke(
                null,
                new object[] { targetId }
            );
            return AcceptInput(coordinator, phaseId, input);
        }

        private static object AcceptInput(
            object coordinator,
            int phaseId,
            object input)
        {
            return coordinator.GetType().GetMethod("AcceptInput").Invoke(
                coordinator,
                new[] { (object)phaseId, input }
            );
        }

        private static object CreateGameObject(string name)
        {
            return Activator.CreateInstance(
                UnityType("GameObject"),
                new object[] { name }
            );
        }

        private static object AddComponent(object gameObject, Type type)
        {
            return gameObject.GetType().GetMethod(
                "AddComponent",
                new[] { typeof(Type) }
            ).Invoke(gameObject, new object[] { type });
        }

        private static object GetTransform(object gameObject)
        {
            return gameObject.GetType().GetProperty("transform")
                .GetValue(gameObject);
        }

        private static void SetParent(object transform, object parent)
        {
            Type transformType = UnityType("Transform");
            transformType.GetMethod(
                "SetParent",
                new[] { transformType, typeof(bool) }
            ).Invoke(transform, new[] { parent, (object)false });
        }

        private static object CreateVector3(float x, float y, float z)
        {
            return Activator.CreateInstance(
                UnityType("Vector3"),
                new object[] { x, y, z }
            );
        }

        private static float RotationAngleFromIdentity(object transform)
        {
            Type quaternionType = UnityType("Quaternion");
            object identity = quaternionType.GetProperty(
                "identity",
                BindingFlags.Public | BindingFlags.Static
            ).GetValue(null);
            object rotation = transform.GetType()
                .GetProperty("localRotation")
                .GetValue(transform);
            object angle = quaternionType.GetMethod(
                "Angle",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { quaternionType, quaternionType },
                modifiers: null
            ).Invoke(null, new[] { identity, rotation });
            return Convert.ToSingle(angle);
        }

        private static void DestroyImmediate(object target)
        {
            Type objectType = UnityType("Object");
            objectType.GetMethod(
                "DestroyImmediate",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { objectType },
                modifiers: null
            ).Invoke(null, new[] { target });
        }

        private static Type UnityType(string typeName)
        {
            return Type.GetType(
                "UnityEngine." + typeName + ", UnityEngine.CoreModule",
                throwOnError: true
            );
        }

        private static Type UnityPhysicsType(string typeName)
        {
            return Type.GetType(
                "UnityEngine." + typeName + ", UnityEngine.PhysicsModule",
                throwOnError: true
            );
        }

        private static Type RuntimeType(string typeName)
        {
            return Type.GetType(
                AdapterNamespace + typeName + ", " + RuntimeAssembly,
                throwOnError: true
            );
        }

        private static Type CoreType(string typeName)
        {
            return Type.GetType(
                "SignVR.Interaction.Core." + typeName +
                ", SignVR.Interaction.Core",
                throwOnError: true
            );
        }

        private static void AssertPublicInstanceMethod(
            Type type,
            string methodName)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.That(
                method,
                Is.Not.Null,
                $"{type.Name}.{methodName} is missing."
            );
        }
    }
}
