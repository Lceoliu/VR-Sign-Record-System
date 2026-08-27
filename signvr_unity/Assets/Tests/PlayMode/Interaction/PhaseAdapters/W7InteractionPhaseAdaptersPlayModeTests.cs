using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SignVR.Interaction.PhaseAdapters.PlayMode.Tests
{
    public sealed class W7InteractionPhaseAdaptersPlayModeTests
    {
        private const string AdapterNamespace =
            "SignVR.Interaction.PhaseAdapters.";
        private const string RuntimeAssembly = "Assembly-CSharp";
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<UnityEngine.Object> transientAssets =
            new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = roots.Count - 1; index >= 0; index--)
            {
                if (roots[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(roots[index]);
                }
            }
            roots.Clear();
            for (int index = transientAssets.Count - 1; index >= 0; index--)
            {
                if (transientAssets[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        transientAssets[index]
                    );
                }
            }
            transientAssets.Clear();
        }

        [UnityTest]
        public IEnumerator KinematicBareHandEnteringAuthoredTriggerAcceptsTarget()
        {
            RuntimeFixture fixture = CreateRuntimeFixture("PhysicalTrigger");
            Component phaseOne = fixture.Adapters[0];

            GameObject targetObject = Track(
                new GameObject("W7Target_box_stool")
            );
            targetObject.transform.SetParent(fixture.Root.transform, false);
            BoxCollider trigger = targetObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            Rigidbody triggerBody = targetObject.AddComponent<Rigidbody>();
            triggerBody.isKinematic = false;
            triggerBody.useGravity = false;
            triggerBody.constraints = RigidbodyConstraints.FreezeAll;

            Component targetBinding = targetObject.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                targetBinding,
                "Configure",
                "box_stool",
                phaseOne,
                Array.Empty<Behaviour>(),
                new Collider[] { trigger }
            );

            GameObject handRoot = Track(new GameObject("LeftHandInteractor"));
            handRoot.transform.position = Vector3.right * 2f;
            handRoot.AddComponent<BoxCollider>();
            Rigidbody handBody = handRoot.AddComponent<Rigidbody>();
            handBody.isKinematic = true;
            handBody.useGravity = false;

            Component relay = targetObject.AddComponent(
                RuntimeType("InteractionTriggerRelay")
            );
            InvokePublic(
                relay,
                "Configure",
                targetBinding,
                new[] { handRoot.transform },
                0.05f
            );
            var accepted = new EventCounter();
            SubscribeGenericEvent(phaseOne, "InputAccepted", accepted);

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            handRoot.transform.position = targetObject.transform.position;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Assert.That(
                accepted.Count,
                Is.EqualTo(1),
                "A real Meta-style kinematic hand overlap must reach the " +
                "phase adapter through OnTriggerEnter exactly once."
            );
        }

        [UnityTest]
        public IEnumerator TargetInputIsAcceptedOncePerContactCycleAndTarget()
        {
            RuntimeFixture fixture = CreateRuntimeFixture("ContactCycleGate");
            Component phaseFour = fixture.Adapters[3];
            ActivatePhase(fixture, 2);
            ActivatePhase(fixture, 3);
            ActivatePhase(fixture, 4);

            GameObject leftRoot = new GameObject("LeftHandInteractor");
            leftRoot.transform.SetParent(fixture.Root.transform, false);
            GameObject leftFirstObject = new GameObject("LeftHandColliderA");
            leftFirstObject.transform.SetParent(leftRoot.transform, false);
            Collider leftFirst = leftFirstObject.AddComponent<BoxCollider>();
            GameObject leftSecondObject = new GameObject("LeftHandColliderB");
            leftSecondObject.transform.SetParent(leftRoot.transform, false);
            Collider leftSecond = leftSecondObject.AddComponent<BoxCollider>();

            GameObject rightRoot = new GameObject("RightHandInteractor");
            rightRoot.transform.SetParent(fixture.Root.transform, false);
            GameObject rightObject = new GameObject("RightHandCollider");
            rightObject.transform.SetParent(rightRoot.transform, false);
            Collider right = rightObject.AddComponent<BoxCollider>();
            Transform[] allowedRoots =
                { leftRoot.transform, rightRoot.transform };

            GameObject blueObject = new GameObject("W7Target_key_b");
            blueObject.transform.SetParent(fixture.Root.transform, false);
            Collider blueCollider = blueObject.AddComponent<BoxCollider>();
            Component blueBinding = blueObject.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                blueBinding,
                "Configure",
                "key_b",
                phaseFour,
                Array.Empty<Behaviour>(),
                new[] { blueCollider }
            );
            Component blueRelay = blueObject.AddComponent(
                RuntimeType("InteractionTriggerRelay")
            );
            InvokePublic(
                blueRelay,
                "Configure",
                blueBinding,
                allowedRoots,
                0.05f
            );

            GameObject redObject = new GameObject(
                "W7Target_motorbike_key"
            );
            redObject.transform.SetParent(fixture.Root.transform, false);
            Collider redCollider = redObject.AddComponent<BoxCollider>();
            Component redBinding = redObject.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                redBinding,
                "Configure",
                "motorbike_key",
                phaseFour,
                Array.Empty<Behaviour>(),
                new[] { redCollider }
            );
            Component redRelay = redObject.AddComponent(
                RuntimeType("InteractionTriggerRelay")
            );
            InvokePublic(
                redRelay,
                "Configure",
                redBinding,
                allowedRoots,
                0.05f
            );

            var results = new EventCounter();
            SubscribeGenericEvent(
                fixture.Coordinator,
                "ResultProduced",
                results
            );

            Assert.That(
                InvokePublic(blueRelay, "AcceptTrigger", leftFirst),
                Is.Not.Null
            );
            Assert.That(
                InvokePublic(blueRelay, "AcceptTrigger", leftSecond),
                Is.Null,
                "A second collider on the same hand root is one contact."
            );
            Assert.That(
                InvokePublic(blueRelay, "AcceptTrigger", right),
                Is.Null,
                "Both hands touching one target share one contact cycle."
            );

            yield return new WaitForSecondsRealtime(0.06f);
            InvokePublic(blueBinding, "Poke");
            InvokePublic(blueBinding, "Trigger");
            InvokePublic(blueBinding, "Grab");
            Assert.That(
                results.Count,
                Is.EqualTo(1),
                "Poke/Trigger/Grab must share the active trigger latch."
            );

            Assert.That(
                InvokePublic(redRelay, "AcceptTrigger", right),
                Is.Not.Null,
                "A contact on another target must not be globally blocked."
            );
            Assert.That(results.Count, Is.EqualTo(2));

            InvokePublic(blueRelay, "ReleaseTrigger", leftFirst);
            InvokePublic(blueRelay, "ReleaseTrigger", leftSecond);
            Assert.That(
                InvokePublic(blueRelay, "AcceptTrigger", leftFirst),
                Is.Null,
                "One hand remaining on the target must keep it latched."
            );
            InvokePublic(blueRelay, "ReleaseTrigger", right);
            InvokePublic(blueRelay, "ReleaseTrigger", leftFirst);

            Assert.That(
                InvokePublic(blueRelay, "AcceptTrigger", leftSecond),
                Is.Null,
                "Direct contact sources must keep the shared target latched."
            );
            InvokePublic(blueBinding, "PokeEnded");
            InvokePublic(blueBinding, "TriggerEnded");
            Assert.That(
                InvokePublic(blueBinding, "AcceptInput"),
                Is.Null,
                "The remaining grab source must prevent premature rearm."
            );
            InvokePublic(blueBinding, "GrabEnded");
            InvokePublic(blueRelay, "ReleaseTrigger", leftSecond);

            yield return new WaitForSecondsRealtime(0.06f);
            Assert.That(
                InvokePublic(blueRelay, "AcceptTrigger", leftSecond),
                Is.Not.Null,
                "The target must rearm after every hand collider exits and " +
                "its short cooldown expires."
            );
            Assert.That(results.Count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator SameDirectSourceRequiresEveryContactToEnd()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "SameDirectSourceContactCount"
            );
            Component phaseFive = fixture.Adapters[4];
            ActivatePhase(fixture, 2);
            ActivatePhase(fixture, 3);
            ActivatePhase(fixture, 4);
            ActivatePhase(fixture, 5);

            GameObject targetObject = new GameObject("W7Target_button_a");
            targetObject.transform.SetParent(fixture.Root.transform, false);
            Component binding = targetObject.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                binding,
                "Configure",
                "button_a",
                phaseFive,
                Array.Empty<Behaviour>(),
                Array.Empty<Collider>()
            );
            InvokePublic(binding, "ConfigureSameTargetCooldown", 0f);

            var results = new EventCounter();
            SubscribeGenericEvent(
                fixture.Coordinator,
                "ResultProduced",
                results
            );

            InvokePublic(binding, "Poke");
            InvokePublic(binding, "Poke");
            Assert.That(results.Count, Is.EqualTo(1));

            InvokePublic(binding, "PokeEnded");
            Assert.That(
                InvokePublic(binding, "AcceptInput"),
                Is.Null,
                "One same-source contact remains and must keep the target " +
                "latched."
            );

            InvokePublic(binding, "PokeEnded");
            Assert.That(
                InvokePublic(binding, "AcceptInput"),
                Is.Not.Null,
                "The target must rearm only after both Poke contacts end."
            );
            Assert.That(results.Count, Is.EqualTo(2));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhaseFiveErrorFeedbackBlocksInputForResetWindow()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "PhaseFiveFeedbackInputLock"
            );
            Component phaseFive = fixture.Adapters[4];
            ActivatePhase(fixture, 2);
            ActivatePhase(fixture, 3);
            ActivatePhase(fixture, 4);
            ActivatePhase(fixture, 5);

            var results = new EventCounter();
            SubscribeGenericEvent(
                fixture.Coordinator,
                "ResultProduced",
                results
            );

            AssertAccepted(InvokePublic(
                phaseFive,
                "AcceptTarget",
                "button_a"
            ));
            object repeated = InvokePublic(
                phaseFive,
                "AcceptTarget",
                "button_a"
            );
            Assert.That(repeated, Is.Not.Null);
            Assert.That(
                repeated.GetType().GetProperty("ProgressReset")
                    .GetValue(repeated),
                Is.True
            );
            Assert.That(
                InvokePublic(phaseFive, "AcceptTarget", "button_b"),
                Is.Null,
                "The red feedback window must freeze logical input."
            );
            Assert.That(results.Count, Is.EqualTo(2));

            yield return new WaitForSecondsRealtime(0.61f);

            AssertAccepted(InvokePublic(
                phaseFive,
                "AcceptTarget",
                "button_b"
            ));
            Assert.That(results.Count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator PhaseFiveRejectsSynchronousReentryAndResetWins()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "PhaseFiveSynchronousReentry"
            );
            Component phaseFive = fixture.Adapters[4];
            ActivatePhase(fixture, 2);
            ActivatePhase(fixture, 3);
            ActivatePhase(fixture, 4);
            ActivatePhase(fixture, 5);

            AssertAccepted(InvokePublic(
                phaseFive,
                "AcceptTarget",
                "button_a"
            ));
            var probe = new PhaseFiveResetAndReentryProbe(phaseFive);
            SubscribeGenericEvent(
                phaseFive,
                "TaskProgressReset",
                probe,
                nameof(PhaseFiveResetAndReentryProbe.Observe)
            );

            object repeated = InvokePublic(
                phaseFive,
                "AcceptTarget",
                "button_a"
            );
            Assert.That(repeated, Is.Not.Null);
            Assert.That(probe.ReentrantResult, Is.Null);

            InvokePublic(phaseFive, "Enable");
            AssertAccepted(InvokePublic(
                phaseFive,
                "AcceptTarget",
                "button_b"
            ));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReenabledPresentationStartsAFreshFeedbackClock()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "PhaseFiveReenableFeedbackClock"
            );
            Component phaseFive = fixture.Adapters[4];
            ActivatePhase(fixture, 2);
            ActivatePhase(fixture, 3);
            ActivatePhase(fixture, 4);
            ActivatePhase(fixture, 5);

            GameObject button = new GameObject("button_a");
            button.transform.SetParent(fixture.Root.transform, false);
            object state = CreateTargetStateBinding(
                "button_a",
                button.transform
            );
            Component presentation = fixture.Root.AddComponent(
                RuntimeType("InteractionDeterministicPresentation")
            );
            ConfigurePhaseFivePresentation(
                presentation,
                fixture.Coordinator,
                state
            );

            ((Behaviour)presentation).enabled = false;
            yield return new WaitForSecondsRealtime(0.7f);
            ((Behaviour)presentation).enabled = true;

            AssertAccepted(InvokePublic(
                phaseFive,
                "AcceptTarget",
                "button_a"
            ));
            object repeated = InvokePublic(
                phaseFive,
                "AcceptTarget",
                "button_a"
            );
            Assert.That(repeated, Is.Not.Null);
            Assert.That(
                state.GetType().GetProperty("VisualState").GetValue(state)
                    .ToString(),
                Is.EqualTo("Error")
            );

            yield return null;

            Assert.That(
                state.GetType().GetProperty("VisualState").GetValue(state)
                    .ToString(),
                Is.EqualTo("Error"),
                "The first Update after re-enable cleared fresh red feedback."
            );
        }

        [UnityTest]
        public IEnumerator MovableTargetLifecycleRestoresAuthoredState()
        {
            GameObject adapterObject = Track(
                new GameObject("PhaseTwoMovableLifecycle")
            );
            Component phaseTwo = adapterObject.AddComponent(
                RuntimeType("PhaseTwoInteractionAdapter")
            );

            GameObject coin = Track(new GameObject("MovableCoin"));
            coin.transform.localPosition = new Vector3(1f, 2f, 3f);
            Rigidbody body = coin.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            body.detectCollisions = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            BoxCollider collider = coin.AddComponent<BoxCollider>();
            collider.enabled = false;

            GameObject grabRoot = new GameObject("ISDK_HandGrabInteraction");
            grabRoot.transform.SetParent(coin.transform, false);
            Behaviour grabBehaviour = grabRoot.AddComponent<AudioSource>();
            grabBehaviour.enabled = false;
            grabRoot.SetActive(false);

            Component binding = coin.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                binding,
                "ConfigureMovable",
                "coin_dragon",
                phaseTwo,
                new[] { grabBehaviour },
                new Collider[] { collider },
                new[] { grabRoot }
            );

            InvokePublic(phaseTwo, "Enable");
            Assert.That(grabRoot.activeSelf, Is.True);
            Assert.That(grabBehaviour.enabled, Is.True);
            Assert.That(collider.enabled, Is.True);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(
                body.useGravity,
                Is.False,
                "Phase 2 availability must not turn coin gravity on."
            );
            Assert.That(body.detectCollisions, Is.True);
            Assert.That(body.constraints, Is.EqualTo(RigidbodyConstraints.None));
            Assert.That(
                body.interpolation,
                Is.EqualTo(RigidbodyInterpolation.Interpolate)
            );
            Assert.That(
                body.collisionDetectionMode,
                Is.EqualTo(CollisionDetectionMode.ContinuousDynamic)
            );

            coin.transform.localPosition = Vector3.one * 9f;
            InvokePublic(phaseTwo, "Disable");
            Assert.That(grabRoot.activeSelf, Is.False);
            Assert.That(grabBehaviour.enabled, Is.False);
            Assert.That(collider.enabled, Is.False);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.useGravity, Is.False);
            Assert.That(body.detectCollisions, Is.False);
            Assert.That(
                body.constraints,
                Is.EqualTo(RigidbodyConstraints.FreezeAll)
            );
            Assert.That(
                coin.transform.localPosition,
                Is.EqualTo(Vector3.one * 9f),
                "Phase isolation must freeze the released pose without " +
                "silently resetting it."
            );

            InvokePublic(phaseTwo, "Enable");
            coin.transform.localPosition = Vector3.one * 7f;
            InvokePublic(phaseTwo, "Reset");
            Assert.That(
                coin.transform.localPosition,
                Is.EqualTo(new Vector3(1f, 2f, 3f))
            );
            Assert.That(grabRoot.activeSelf, Is.False);
            Assert.That(collider.enabled, Is.False);
            Assert.That(body.isKinematic, Is.True);

            UnityEngine.Object.DestroyImmediate(binding);
            Assert.That(grabRoot.activeSelf, Is.False);
            Assert.That(grabBehaviour.enabled, Is.False);
            Assert.That(collider.enabled, Is.False);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.useGravity, Is.False);
            Assert.That(body.detectCollisions, Is.False);
            Assert.That(
                body.constraints,
                Is.EqualTo(RigidbodyConstraints.FreezeAll)
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator CorrectPlacementWaitsForReleaseThenSnapsAndLocksCoin()
        {
            RuntimeFixture fixture = CreateRuntimeFixture("CoinSnapLock");
            Component phaseTwo = fixture.Adapters[1];
            ActivatePhase(fixture, 2);

            GameObject coin = new GameObject("coin_dragon");
            coin.transform.SetParent(fixture.Root.transform, false);
            coin.transform.position = new Vector3(1f, 2f, 3f);
            Rigidbody body = coin.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = false;
            body.linearVelocity = new Vector3(2f, 3f, 4f);
            body.angularVelocity = new Vector3(5f, 6f, 7f);
            Collider coinCollider = coin.AddComponent<BoxCollider>();
            FakeSelectionInteractableView selection =
                coin.AddComponent<FakeSelectionInteractableView>();
            selection.IsSelected = true;
            Component coinBinding = coin.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                coinBinding,
                "Configure",
                "coin_dragon",
                phaseTwo,
                new Behaviour[] { selection },
                new[] { coinCollider }
            );

            GameObject plate = new GameObject("plate_dragon");
            plate.transform.SetParent(fixture.Root.transform, false);
            Collider placementCollider = plate.AddComponent<BoxCollider>();
            GameObject snapObject = new GameObject("SnapPoint");
            snapObject.transform.SetParent(plate.transform, false);
            snapObject.transform.position = new Vector3(8f, 9f, 10f);
            snapObject.transform.rotation = Quaternion.Euler(20f, 30f, 40f);
            Component placement = plate.AddComponent(
                RuntimeType("InteractionPlacementBinding")
            );
            InvokePublic(
                placement,
                "Configure",
                "plate_dragon",
                phaseTwo,
                placementCollider,
                snapObject.transform
            );

            object queued = InvokePublic(
                placement,
                "AcceptTrigger",
                coinCollider
            );
            Assert.That(
                queued,
                Is.Null,
                "Entering the plate must only queue a placement."
            );
            Assert.That(
                InvokePublic(placement, "TryAcceptStay", coinCollider),
                Is.Null
            );
            Assert.That(
                body.isKinematic,
                Is.False,
                "A still-selected coin must remain owned by the grab system."
            );

            selection.IsSelected = false;
            AssertAccepted(InvokePublic(
                placement,
                "TryAcceptStay",
                coinCollider
            ));

            Assert.That(coin.GetComponent<Rigidbody>(), Is.SameAs(body));
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.useGravity, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(body.angularVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(
                Vector3.Distance(
                    coin.transform.position,
                    snapObject.transform.position
                ),
                Is.LessThan(0.0001f)
            );
            Assert.That(
                Quaternion.Angle(
                    coin.transform.rotation,
                    snapObject.transform.rotation
                ),
                Is.LessThan(0.0001f)
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyedSelectionViewDoesNotPoisonPlacement()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "DestroyedCoinSelectionView"
            );
            Component phaseTwo = fixture.Adapters[1];
            ActivatePhase(fixture, 2);

            GameObject coin = new GameObject("coin_dragon");
            coin.transform.SetParent(fixture.Root.transform, false);
            Rigidbody body = coin.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = false;
            Collider coinCollider = coin.AddComponent<BoxCollider>();
            FakeSelectionInteractableView selection =
                coin.AddComponent<FakeSelectionInteractableView>();
            Component coinBinding = coin.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                coinBinding,
                "Configure",
                "coin_dragon",
                phaseTwo,
                new Behaviour[] { selection },
                new[] { coinCollider }
            );

            GameObject plate = new GameObject("plate_dragon");
            plate.transform.SetParent(fixture.Root.transform, false);
            Collider placementCollider = plate.AddComponent<BoxCollider>();
            GameObject snap = new GameObject("SnapPoint");
            snap.transform.SetParent(plate.transform, false);
            Component placement = plate.AddComponent(
                RuntimeType("InteractionPlacementBinding")
            );
            InvokePublic(
                placement,
                "Configure",
                "plate_dragon",
                phaseTwo,
                placementCollider,
                snap.transform
            );

            InvokePublic(placement, "AcceptTrigger", coinCollider);
            UnityEngine.Object.DestroyImmediate(selection);

            AssertAccepted(InvokePublic(
                placement,
                "TryAcceptStay",
                coinCollider
            ));
            Assert.That(body.isKinematic, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResetDuringPlacementPreventsStaleSnapAndLock()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "PlacementSynchronousReset"
            );
            Component phaseTwo = fixture.Adapters[1];
            ActivatePhase(fixture, 2);

            GameObject coin = new GameObject("coin_dragon");
            coin.transform.SetParent(fixture.Root.transform, false);
            coin.transform.position = new Vector3(1f, 2f, 3f);
            Vector3 authoredPosition = coin.transform.position;
            Rigidbody body = coin.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = false;
            Collider coinCollider = coin.AddComponent<BoxCollider>();
            Component coinBinding = coin.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                coinBinding,
                "Configure",
                "coin_dragon",
                phaseTwo,
                Array.Empty<Behaviour>(),
                new[] { coinCollider }
            );

            GameObject plate = new GameObject("plate_dragon");
            plate.transform.SetParent(fixture.Root.transform, false);
            Collider placementCollider = plate.AddComponent<BoxCollider>();
            GameObject snap = new GameObject("SnapPoint");
            snap.transform.SetParent(plate.transform, false);
            snap.transform.position = new Vector3(8f, 9f, 10f);
            Component placement = plate.AddComponent(
                RuntimeType("InteractionPlacementBinding")
            );
            InvokePublic(
                placement,
                "Configure",
                "plate_dragon",
                phaseTwo,
                placementCollider,
                snap.transform
            );

            var probe = new AdapterResetProbe(phaseTwo);
            SubscribeGenericEvent(
                phaseTwo,
                "InputAccepted",
                probe,
                nameof(AdapterResetProbe.Observe)
            );
            InvokePublic(placement, "AcceptTrigger", coinCollider);

            AssertAccepted(InvokePublic(
                placement,
                "TryAcceptStay",
                coinCollider
            ));
            Assert.That(probe.Count, Is.EqualTo(1));
            Assert.That(
                Vector3.Distance(coin.transform.position, authoredPosition),
                Is.LessThan(0.0001f),
                "The old placement call snapped after its authority reset."
            );
            Assert.That(body.isKinematic, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyDuringPlacementDoesNotAccessDeadCoin()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "PlacementSynchronousDestroy"
            );
            Component phaseTwo = fixture.Adapters[1];
            ActivatePhase(fixture, 2);

            GameObject coin = new GameObject("coin_dragon");
            coin.transform.SetParent(fixture.Root.transform, false);
            Collider coinCollider = coin.AddComponent<BoxCollider>();
            Component coinBinding = coin.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                coinBinding,
                "Configure",
                "coin_dragon",
                phaseTwo,
                Array.Empty<Behaviour>(),
                new[] { coinCollider }
            );

            GameObject plate = new GameObject("plate_dragon");
            plate.transform.SetParent(fixture.Root.transform, false);
            Collider placementCollider = plate.AddComponent<BoxCollider>();
            GameObject snap = new GameObject("SnapPoint");
            snap.transform.SetParent(plate.transform, false);
            Component placement = plate.AddComponent(
                RuntimeType("InteractionPlacementBinding")
            );
            InvokePublic(
                placement,
                "Configure",
                "plate_dragon",
                phaseTwo,
                placementCollider,
                snap.transform
            );

            var probe = new DestroyGameObjectProbe(coin);
            SubscribeGenericEvent(
                phaseTwo,
                "InputAccepted",
                probe,
                nameof(DestroyGameObjectProbe.Observe)
            );
            InvokePublic(placement, "AcceptTrigger", coinCollider);

            object result = null;
            Assert.DoesNotThrow(() => result = InvokePublic(
                placement,
                "TryAcceptStay",
                coinCollider
            ));
            AssertAccepted(result);
            Assert.That(coin == null, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CoinLabSelectsTargetsReportsResultsAndResets()
        {
            GameObject root = Track(new GameObject("CoinLabRuntime"));
            Component coordinator = root.AddComponent(
                RuntimeType("InteractionPhaseCoordinator")
            );
            Array adapters = Array.CreateInstance(
                RuntimeType("InteractionPhaseAdapter"),
                6
            );
            string[] adapterNames =
            {
                "PhaseOneInteractionAdapter",
                "PhaseTwoInteractionAdapter",
                "PhaseThreeInteractionAdapter",
                "PhaseFourInteractionAdapter",
                "PhaseFiveInteractionAdapter",
                "PhaseSixInteractionAdapter"
            };
            for (int index = 0; index < adapterNames.Length; index++)
            {
                GameObject adapterObject = new GameObject(adapterNames[index]);
                adapterObject.transform.SetParent(root.transform, false);
                adapters.SetValue(
                    adapterObject.AddComponent(RuntimeType(adapterNames[index])),
                    index
                );
            }
            InvokePublic(coordinator, "ConfigureAdapters", adapters);

            GameObject ui = new GameObject("CoinLabUi");
            ui.transform.SetParent(root.transform, false);
            TextMeshProUGUI status = NewLabText(ui.transform, "Status");
            Button[] coinButtons =
            {
                NewLabButton(ui.transform, "DragonCoin"),
                NewLabButton(ui.transform, "CoinA"),
                NewLabButton(ui.transform, "CoinB")
            };
            Button[] plateButtons =
            {
                NewLabButton(ui.transform, "DragonPlate"),
                NewLabButton(ui.transform, "PlateA"),
                NewLabButton(ui.transform, "PlateB")
            };
            Button reset = NewLabButton(ui.transform, "Reset");

            GameObject controllerObject = new GameObject("CoinLabController");
            controllerObject.SetActive(false);
            controllerObject.transform.SetParent(root.transform, false);
            Component controller = controllerObject.AddComponent(
                RuntimeType("CoinInteractionLabController")
            );
            InvokePublic(
                controller,
                "Configure",
                coordinator,
                status,
                coinButtons,
                plateButtons,
                reset
            );
            controllerObject.SetActive(true);
            yield return null;

            Assert.That(
                coordinator.GetType().GetProperty("CurrentPhaseId")
                    .GetValue(coordinator),
                Is.EqualTo(2)
            );
            Assert.That(status.text, Does.Contain("龙纹金币"));
            Assert.That(status.text, Does.Contain("龙纹盘"));

            coinButtons[2].onClick.Invoke();
            plateButtons[1].onClick.Invoke();
            Assert.That(
                controller.GetType().GetProperty("SelectedCoinId")
                    .GetValue(controller),
                Is.EqualTo("coin_b")
            );
            Assert.That(
                controller.GetType().GetProperty("SelectedPlateId")
                    .GetValue(controller),
                Is.EqualTo("plate_a")
            );
            Assert.That(status.text, Does.Contain("金币 B"));
            Assert.That(status.text, Does.Contain("盘子 A"));

            Component phaseTwo = (Component)adapters.GetValue(1);
            InvokePublic(
                phaseTwo,
                "AcceptPlacement",
                "coin_a",
                "plate_a"
            );
            Assert.That(status.text, Does.Contain("错误组合"));

            InvokePublic(
                phaseTwo,
                "AcceptPlacement",
                "coin_b",
                "plate_a"
            );
            Assert.That(status.text, Does.Contain("正确"));
            Assert.That(
                phaseTwo.GetType().GetProperty("IsEnabled")
                    .GetValue(phaseTwo),
                Is.False,
                "A correct placement must lock the completed lab trial."
            );

            reset.onClick.Invoke();
            Assert.That(
                phaseTwo.GetType().GetProperty("IsEnabled")
                    .GetValue(phaseTwo),
                Is.True
            );
            Assert.That(status.text, Does.Contain("当前目标"));
        }

        [UnityTest]
        public IEnumerator PhaseOneTargetCompletesWithoutPasswordGate()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "PasswordFreePhaseOne"
            );
            Component phaseOne = fixture.Adapters[0];
            var completed = new EventCounter();
            SubscribeGenericEvent(phaseOne, "Completed", completed);
            AssertAccepted(AcceptTarget(fixture, "box_stool"));
            yield return null;

            object lastResult = phaseOne.GetType()
                .GetProperty("LastResult")
                .GetValue(phaseOne);
            Assert.That(lastResult, Is.Not.Null);
            Assert.That(
                lastResult.GetType().GetProperty("PhaseCompleted")
                    .GetValue(lastResult),
                Is.True,
                "The planned box must complete Phase 1 without a password " +
                "or keypad gate."
            );
            Assert.That(completed.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RuntimeSubscriptionChainDeliversOnceAcrossLifecycle()
        {
            Assert.That(Application.isPlaying, Is.True);
            RuntimeFixture fixture = CreateRuntimeFixture("SubscriptionChain");
            Component phaseOne = fixture.Adapters[0];

            GameObject targetObject = Track(new GameObject("box_stool"));
            targetObject.transform.SetParent(fixture.Root.transform, false);
            BoxCollider targetCollider = targetObject.AddComponent<BoxCollider>();
            Component targetBinding = targetObject.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                targetBinding,
                "Configure",
                "box_stool",
                phaseOne,
                Array.Empty<Behaviour>(),
                new Collider[] { targetCollider }
            );

            TextMesh safeHint = CreateText(fixture.Root.transform, "SafeHint");
            TextMesh chestHint = CreateText(fixture.Root.transform, "ChestHint");
            Component hintPresenter = fixture.Root.AddComponent(
                RuntimeType("InteractionPlanHintPresenter")
            );
            InvokePublic(
                hintPresenter,
                "Configure",
                fixture.Coordinator,
                safeHint,
                chestHint
            );

            var coordinatorResults = new EventCounter();
            var adapterResults = new EventCounter();
            SubscribeGenericEvent(
                fixture.Coordinator,
                "ResultProduced",
                coordinatorResults
            );
            SubscribeGenericEvent(
                phaseOne,
                "InputAccepted",
                adapterResults
            );
            yield return null;

            object first = InvokePublic(targetBinding, "AcceptInput");
            AssertAccepted(first);
            Assert.That(coordinatorResults.Count, Is.EqualTo(1));
            Assert.That(adapterResults.Count, Is.EqualTo(1));
            Assert.That(safeHint.gameObject.activeSelf, Is.False);

            ((Behaviour)fixture.Coordinator).enabled = false;
            InvokePublic(fixture.Coordinator, "Configure", fixture.Plan);
            InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                1,
                CreateTargetInput("box_stool")
            );
            Assert.That(coordinatorResults.Count, Is.EqualTo(1));
            Assert.That(adapterResults.Count, Is.EqualTo(1));

            ((Behaviour)fixture.Coordinator).enabled = true;
            ActivatePhaseOne(fixture);
            AssertAccepted(InvokePublic(targetBinding, "AcceptInput"));
            Assert.That(coordinatorResults.Count, Is.EqualTo(2));
            Assert.That(adapterResults.Count, Is.EqualTo(2));

            ResetPhaseOne(fixture);
            AssertAccepted(InvokePublic(targetBinding, "AcceptInput"));
            Assert.That(coordinatorResults.Count, Is.EqualTo(3));
            Assert.That(adapterResults.Count, Is.EqualTo(3));

            InvokePublic(fixture.Coordinator, "Configure", fixture.Plan);
            ((Behaviour)fixture.Coordinator).enabled = false;
            ((Behaviour)fixture.Coordinator).enabled = true;
            ActivatePhaseOne(fixture);
            AssertAccepted(InvokePublic(targetBinding, "AcceptInput"));
            Assert.That(coordinatorResults.Count, Is.EqualTo(4));
            Assert.That(adapterResults.Count, Is.EqualTo(4));
            Assert.That(safeHint.gameObject.activeSelf, Is.False);

            RuntimeFixture replacement = CreateRuntimeFixture(
                "AdapterPublisherB"
            );
            InvokePublic(
                phaseOne,
                "Configure",
                replacement.Coordinator,
                replacement.Plan
            );
            InvokePublic(phaseOne, "Enable");
            int movedAdapterBefore = adapterResults.Count;
            int oldCoordinatorBefore = coordinatorResults.Count;
            Assert.That(InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                1,
                InvokeCoreFactory("PhaseInput", "Digit", 1)
            ), Is.Not.Null);
            Assert.That(
                coordinatorResults.Count,
                Is.EqualTo(oldCoordinatorBefore + 1)
            );
            Assert.That(
                adapterResults.Count,
                Is.EqualTo(movedAdapterBefore),
                "The adapter retained publisher A after reconfigure."
            );

            var replacementCoordinatorResults = new EventCounter();
            SubscribeGenericEvent(
                replacement.Coordinator,
                "ResultProduced",
                replacementCoordinatorResults
            );
            AssertAccepted(AcceptTarget(replacement, "box_stool"));
            Assert.That(replacementCoordinatorResults.Count, Is.EqualTo(1));
            Assert.That(
                adapterResults.Count,
                Is.EqualTo(movedAdapterBefore + 1),
                "The adapter did not handle publisher B exactly once."
            );
        }

        [UnityTest]
        public IEnumerator AvailabilitySubscribersStayClosedWhileDisabled()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "AvailabilitySubscribers"
            );
            Component phaseOne = fixture.Adapters[0];
            Component phaseTwo = fixture.Adapters[1];

            string[] subscriberTypes =
            {
                "InteractionTargetBinding",
                "InteractionDigitBinding",
                "InteractionPasswordBackspaceBinding",
                "InteractionPasswordSubmitBinding",
                "InteractionPlacementBinding"
            };
            for (int index = 0; index < subscriberTypes.Length; index++)
            {
                string typeName = subscriberTypes[index];
                if (typeName == "InteractionPlacementBinding")
                {
                    ActivatePhase(fixture, 2);
                }
                GameObject target = Track(new GameObject(typeName));
                target.transform.SetParent(fixture.Root.transform, false);
                BoxCollider collider = target.AddComponent<BoxCollider>();
                Component subscriber = target.AddComponent(RuntimeType(typeName));
                Component publisher = typeName == "InteractionPlacementBinding"
                    ? phaseTwo
                    : phaseOne;
                PlacementProbe placementProbe =
                    typeName == "InteractionPlacementBinding"
                        ? CreatePlacementProbe(fixture, phaseTwo)
                        : null;
                ConfigureAvailabilitySubscriber(
                    subscriber,
                    publisher,
                    collider,
                    placementProbe?.SnapPoint
                );
                InvokePublic(publisher, "Enable");
                Assert.That(
                    collider.enabled,
                    Is.True,
                    typeName + " did not reflect active availability."
                );
                int activeBefore = GetSubscriptionInvocationCount(subscriber);
                InvokePublic(publisher, "Disable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(activeBefore + 1),
                    typeName + " handled one publisher event more than once."
                );
                Assert.That(collider.enabled, Is.False, typeName);
                InvokePublic(publisher, "Enable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(activeBefore + 2),
                    typeName + " did not handle exactly one re-enable event."
                );
                Assert.That(collider.enabled, Is.True, typeName);

                ((Behaviour)subscriber).enabled = false;
                ConfigureAvailabilitySubscriber(
                    subscriber,
                    publisher,
                    collider,
                    placementProbe?.SnapPoint
                );
                int disabledBefore =
                    GetSubscriptionInvocationCount(subscriber);
                InvokePublic(publisher, "Disable");
                InvokePublic(publisher, "Enable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(disabledBefore),
                    typeName + " handled availability while disabled."
                );
                Assert.That(
                    collider.enabled,
                    Is.False,
                    typeName + " subscribed or reopened while disabled."
                );
                AssertDisabledDirectEntries(subscriber, placementProbe);

                ((Behaviour)subscriber).enabled = true;
                Assert.That(collider.enabled, Is.True, typeName);

                ConfigureAvailabilitySubscriber(
                    subscriber,
                    publisher,
                    collider,
                    placementProbe?.SnapPoint
                );
                ((Behaviour)subscriber).enabled = false;
                ((Behaviour)subscriber).enabled = true;
                int reboundBefore =
                    GetSubscriptionInvocationCount(subscriber);
                InvokePublic(publisher, "Disable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(reboundBefore + 1),
                    typeName + " duplicated after disable/re-enable."
                );
                Assert.That(collider.enabled, Is.False, typeName);
                AssertDisabledDirectEntries(subscriber, placementProbe);
                InvokePublic(publisher, "Enable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(reboundBefore + 2),
                    typeName + " duplicated its recovery subscription."
                );
                Assert.That(
                    collider.enabled,
                    Is.True,
                    typeName + " did not recover after re-enable."
                );

                Component replacement = CreateReplacementAdapter(
                    fixture,
                    publisher,
                    typeName
                );
                InvokePublic(replacement, "Enable");
                ConfigureAvailabilitySubscriber(
                    subscriber,
                    replacement,
                    collider,
                    placementProbe?.SnapPoint
                );
                int replacementBefore =
                    GetSubscriptionInvocationCount(subscriber);
                InvokePublic(publisher, "Disable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(replacementBefore),
                    typeName + " retained its old publisher subscription."
                );
                Assert.That(
                    collider.enabled,
                    Is.True,
                    typeName + " reacted to old publisher A."
                );
                InvokePublic(replacement, "Disable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(replacementBefore + 1),
                    typeName + " did not handle publisher B exactly once."
                );
                Assert.That(collider.enabled, Is.False, typeName);
                InvokePublic(publisher, "Enable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(replacementBefore + 1),
                    typeName + " reattached to old publisher A."
                );
                Assert.That(collider.enabled, Is.False, typeName);
                InvokePublic(replacement, "Enable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(replacementBefore + 2),
                    typeName + " duplicated publisher B re-enable."
                );
                Assert.That(collider.enabled, Is.True, typeName);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConfigureFailureIsAtomicAndInputOwnershipMovesToB()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "AtomicInputConfigure"
            );
            Component phaseOne = fixture.Adapters[0];
            Component phaseTwo = fixture.Adapters[1];
            InvokePublic(phaseOne, "Enable");

            GameObject unconfiguredObject = Track(
                new GameObject("UnconfiguredPlacement")
            );
            unconfiguredObject.transform.SetParent(
                fixture.Root.transform,
                false
            );
            unconfiguredObject.AddComponent<BoxCollider>();
            Component unconfiguredPlacement = unconfiguredObject.AddComponent(
                RuntimeType("InteractionPlacementBinding")
            );
            PlacementProbe unconfiguredProbe = CreatePlacementProbe(
                fixture,
                phaseTwo
            );
            Assert.That(
                InvokePublicOverload(
                    unconfiguredPlacement,
                    "AcceptPlacement",
                    typeof(string),
                    "coin_dragon"
                ),
                Is.Null
            );
            Assert.That(
                InvokePublicOverload(
                    unconfiguredPlacement,
                    "AcceptPlacement",
                    RuntimeType("InteractionTargetBinding"),
                    unconfiguredProbe.CoinBinding
                ),
                Is.Null
            );
            unconfiguredProbe.AssertUnchanged();

            foreach (string typeName in new[]
                     {
                         "InteractionTargetBinding",
                         "InteractionDigitBinding",
                         "InteractionPasswordBackspaceBinding",
                         "InteractionPasswordSubmitBinding",
                         "InteractionPlacementBinding"
                     })
            {
                if (typeName == "InteractionPlacementBinding")
                {
                    ActivatePhase(fixture, 2);
                }
                Component publisherA = typeName ==
                    "InteractionPlacementBinding" ? phaseTwo : phaseOne;
                // The previous table row intentionally disables publisher A
                // while proving old-publisher detachment. Each row is an
                // independent A -> invalid -> B transaction, so restore A's
                // precondition before configuring the next subscriber type.
                InvokePublic(publisherA, "Enable");
                Assert.That(
                    publisherA.GetType().GetProperty("IsEnabled")
                        .GetValue(publisherA),
                    Is.True,
                    typeName + " publisher A precondition was not restored."
                );
                GameObject owner = Track(new GameObject(typeName + "_Owner"));
                owner.transform.SetParent(fixture.Root.transform, false);
                BoxCollider colliderA = owner.AddComponent<BoxCollider>();
                colliderA.enabled = false;
                Behaviour behaviourA = typeName == "InteractionTargetBinding"
                    ? owner.AddComponent<AudioSource>()
                    : null;
                if (behaviourA != null)
                {
                    behaviourA.enabled = false;
                }
                Component subscriber = owner.AddComponent(RuntimeType(typeName));
                ConfigureAvailabilitySubscriber(
                    subscriber,
                    publisherA,
                    colliderA,
                    null,
                    behaviourA
                );
                Assert.That(colliderA.enabled, Is.True, typeName);
                if (behaviourA != null)
                {
                    Assert.That(behaviourA.enabled, Is.True);
                }
                if (typeName == "InteractionPlacementBinding")
                {
                    Assert.That(colliderA.isTrigger, Is.True);
                }

                TargetInvocationException failure =
                    Assert.Throws<TargetInvocationException>(() =>
                        InvokeInvalidSubscriberConfigure(
                            subscriber,
                            publisherA,
                            colliderA,
                            behaviourA
                        ));
                Assert.That(
                    failure.InnerException,
                    Is.InstanceOf<ArgumentException>(),
                    typeName
                );
                Assert.That(
                    subscriber.GetType().GetProperty("Adapter")
                        .GetValue(subscriber),
                    Is.SameAs(publisherA),
                    typeName + " replaced authority after failed Configure."
                );
                int invalidBefore =
                    GetSubscriptionInvocationCount(subscriber);
                InvokePublic(publisherA, "Disable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(invalidBefore + 1),
                    typeName + " lost its old subscription after failure."
                );
                InvokePublic(publisherA, "Enable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(invalidBefore + 2),
                    typeName + " duplicated its old subscription after failure."
                );

                Component publisherB = CreateReplacementAdapter(
                    fixture,
                    publisherA,
                    typeName + "Atomic"
                );
                InvokePublic(publisherB, "Enable");
                GameObject outputB = Track(
                    new GameObject(typeName + "_OutputB")
                );
                outputB.transform.SetParent(fixture.Root.transform, false);
                BoxCollider colliderB = outputB.AddComponent<BoxCollider>();
                colliderB.enabled = false;
                Behaviour behaviourB = typeName == "InteractionTargetBinding"
                    ? outputB.AddComponent<AudioSource>()
                    : null;
                if (behaviourB != null)
                {
                    behaviourB.enabled = false;
                }
                ConfigureAvailabilitySubscriber(
                    subscriber,
                    publisherB,
                    colliderB,
                    null,
                    behaviourB
                );
                Assert.That(
                    colliderA.enabled,
                    Is.False,
                    typeName + " did not restore output A's authored state."
                );
                if (behaviourA != null)
                {
                    Assert.That(behaviourA.enabled, Is.False);
                    Assert.That(behaviourB.enabled, Is.True);
                }
                if (typeName == "InteractionPlacementBinding")
                {
                    Assert.That(colliderA.isTrigger, Is.False);
                    Assert.That(colliderB.isTrigger, Is.True);
                }
                Assert.That(colliderB.enabled, Is.True, typeName);

                int movedBefore = GetSubscriptionInvocationCount(subscriber);
                InvokePublic(publisherA, "Disable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(movedBefore),
                    typeName + " retained publisher A."
                );
                Assert.That(colliderB.enabled, Is.True, typeName);
                InvokePublic(publisherB, "Disable");
                Assert.That(
                    GetSubscriptionInvocationCount(subscriber),
                    Is.EqualTo(movedBefore + 1),
                    typeName + " did not handle publisher B exactly once."
                );
                Assert.That(colliderB.enabled, Is.False, typeName);

                UnityEngine.Object.DestroyImmediate(subscriber);
                Assert.That(colliderA.enabled, Is.False, typeName);
                Assert.That(
                    colliderB.enabled,
                    Is.False,
                    typeName + " did not restore output B on destroy."
                );
                if (behaviourB != null)
                {
                    Assert.That(behaviourB.enabled, Is.False);
                }
                if (typeName == "InteractionPlacementBinding")
                {
                    Assert.That(colliderB.isTrigger, Is.False);
                }
            }
            yield return null;
        }

        private static void AssertDisabledDirectEntries(
            Component subscriber,
            PlacementProbe placementProbe)
        {
            if (subscriber.GetType().Name != "InteractionPlacementBinding")
            {
                Assert.That(
                    InvokePublic(subscriber, "AcceptInput"),
                    Is.Null,
                    subscriber.GetType().Name +
                    " accepted a direct seam while disabled."
                );
                return;
            }

            Assert.That(placementProbe, Is.Not.Null);
            Assert.That(
                InvokePublicOverload(
                    subscriber,
                    "AcceptPlacement",
                    typeof(string),
                    "coin_dragon"
                ),
                Is.Null,
                "Disabled placement string overload must fail closed."
            );
            Assert.That(
                InvokePublicOverload(
                    subscriber,
                    "AcceptPlacement",
                    RuntimeType("InteractionTargetBinding"),
                    placementProbe.CoinBinding
                ),
                Is.Null,
                "Disabled placement binding overload must fail closed."
            );
            Assert.That(
                InvokePublicOverload(
                    subscriber,
                    "AcceptPlacement",
                    RuntimeType("InteractionTargetBinding"),
                    null
                ),
                Is.Null,
                "Null coin binding must fail closed."
            );
            placementProbe.AssertUnchanged();
        }

        [UnityTest]
        public IEnumerator PhaseTwoEntryAfterPhaseOneGiveUpOpensSafeDoor()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "PhaseOneGiveUpSafeDoor"
            );
            GameObject door = Track(new GameObject("SafeDoor"));
            door.transform.SetParent(fixture.Root.transform, false);
            GameObject hinge = Track(new GameObject("SafeHinge"));
            hinge.transform.SetParent(fixture.Root.transform, false);
            object safeDoorBinding = CreateHingeBinding(
                door.transform,
                hinge.transform
            );
            Component presentation = fixture.Root.AddComponent(
                RuntimeType("InteractionDeterministicPresentation")
            );
            ConfigurePresentation(
                presentation,
                fixture.Coordinator,
                safeDoorBinding
            );
            object giveUpSnapshot = CreateGiveUpAvailablePhaseSnapshot(1);
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                giveUpSnapshot
            );

            AssertAccepted(InvokePublic(
                fixture.Coordinator,
                "GiveUpCurrentPhase",
                giveUpSnapshot
            ));
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(2)
            );
            yield return null;

            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    door.transform.rotation
                ),
                Is.GreaterThan(0.1f),
                "Entering Phase 2 must leave the safe door open."
            );
        }

        [UnityTest]
        public IEnumerator ResultPresentersUseRealPlayModeSubscriptions()
        {
            RuntimeFixture fixture = CreateRuntimeFixture("ResultPresenters");
            TextMesh safeHint = CreateText(fixture.Root.transform, "SafeHint");
            TextMesh chestHint = CreateText(fixture.Root.transform, "ChestHint");
            Component hints = fixture.Root.AddComponent(
                RuntimeType("InteractionPlanHintPresenter")
            );
            InvokePublic(
                hints,
                "Configure",
                fixture.Coordinator,
                safeHint,
                chestHint
            );

            GameObject feedbackObject = Track(
                GameObject.CreatePrimitive(PrimitiveType.Cube)
            );
            feedbackObject.name = "FeedbackPresenter";
            feedbackObject.transform.SetParent(fixture.Root.transform, false);
            Renderer feedbackRenderer = feedbackObject.GetComponent<Renderer>();
            Component feedback = feedbackObject.AddComponent(
                RuntimeType("InteractionFeedbackPresenter")
            );
            InvokePublic(
                feedback,
                "Configure",
                fixture.Coordinator,
                feedbackRenderer,
                null
            );

            GameObject door = Track(new GameObject("SafeDoor"));
            door.transform.SetParent(fixture.Root.transform, false);
            GameObject hinge = Track(new GameObject("SafeHinge"));
            hinge.transform.SetParent(fixture.Root.transform, false);
            object safeDoorBinding = CreateHingeBinding(
                door.transform,
                hinge.transform
            );
            Component presentation = fixture.Root.AddComponent(
                RuntimeType("InteractionDeterministicPresentation")
            );
            ConfigurePresentation(
                presentation,
                fixture.Coordinator,
                safeDoorBinding
            );

            ((Behaviour)hints).enabled = false;
            ((Behaviour)feedback).enabled = false;
            ((Behaviour)presentation).enabled = false;
            InvokePublic(
                hints,
                "Configure",
                fixture.Coordinator,
                safeHint,
                chestHint
            );
            InvokePublic(
                feedback,
                "Configure",
                fixture.Coordinator,
                feedbackRenderer,
                null
            );
            ConfigurePresentation(
                presentation,
                fixture.Coordinator,
                safeDoorBinding
            );
            int disabledHintCount =
                GetSubscriptionInvocationCount(hints);
            int disabledFeedbackCount =
                GetSubscriptionInvocationCount(feedback);
            int disabledPresentationCount =
                GetSubscriptionInvocationCount(presentation);
            ResetPhaseOne(fixture);
            CompletePhaseOne(fixture);
            yield return null;

            AssertSubscriptionCount(
                hints,
                disabledHintCount,
                "Disabled hint presenter handled a result."
            );
            AssertSubscriptionCount(
                feedback,
                disabledFeedbackCount,
                "Disabled feedback presenter handled a result."
            );
            AssertSubscriptionCount(
                presentation,
                disabledPresentationCount,
                "Disabled deterministic presentation handled a result."
            );

            Assert.That(safeHint.gameObject.activeSelf, Is.False);
            Assert.That(
                Quaternion.Angle(Quaternion.identity, door.transform.rotation),
                Is.LessThan(0.01f)
            );
            AssertColor(
                ReadBaseColor(feedbackRenderer),
                new Color(0.2f, 0.35f, 0.55f, 1f)
            );

            ((Behaviour)hints).enabled = true;
            ((Behaviour)feedback).enabled = true;
            ((Behaviour)presentation).enabled = true;
            Assert.That(
                Quaternion.Angle(Quaternion.identity, door.transform.rotation),
                Is.GreaterThan(0.1f),
                "Presentation must rebuild missed authority on re-enable."
            );

            ResetPhaseOne(fixture);
            int acceptedHintBefore =
                GetSubscriptionInvocationCount(hints);
            int acceptedFeedbackBefore =
                GetSubscriptionInvocationCount(feedback);
            int acceptedPresentationBefore =
                GetSubscriptionInvocationCount(presentation);
            AssertAccepted(AcceptTarget(fixture, "box_stool"));
            yield return null;
            AssertSubscriptionCount(hints, acceptedHintBefore + 1);
            AssertSubscriptionCount(feedback, acceptedFeedbackBefore + 1);
            AssertSubscriptionCount(
                presentation,
                acceptedPresentationBefore + 1
            );
            Assert.That(safeHint.gameObject.activeSelf, Is.False);
            AssertColor(
                ReadBaseColor(feedbackRenderer),
                new Color(0.15f, 1f, 0.35f, 1f)
            );
            int passwordHintBefore =
                GetSubscriptionInvocationCount(hints);
            int passwordFeedbackBefore =
                GetSubscriptionInvocationCount(feedback);
            int passwordPresentationBefore =
                GetSubscriptionInvocationCount(presentation);
            CompletePassword(fixture);
            AssertSubscriptionCount(hints, passwordHintBefore);
            AssertSubscriptionCount(feedback, passwordFeedbackBefore);
            AssertSubscriptionCount(
                presentation,
                passwordPresentationBefore
            );
            Assert.That(
                Quaternion.Angle(Quaternion.identity, door.transform.rotation),
                Is.GreaterThan(0.1f)
            );

            InvokePublic(
                hints,
                "Configure",
                fixture.Coordinator,
                safeHint,
                chestHint
            );
            InvokePublic(
                feedback,
                "Configure",
                fixture.Coordinator,
                feedbackRenderer,
                null
            );
            ConfigurePresentation(
                presentation,
                fixture.Coordinator,
                safeDoorBinding
            );
            ((Behaviour)hints).enabled = false;
            ((Behaviour)feedback).enabled = false;
            ((Behaviour)presentation).enabled = false;
            ((Behaviour)hints).enabled = true;
            ((Behaviour)feedback).enabled = true;
            ((Behaviour)presentation).enabled = true;
            ResetPhaseOne(fixture);
            int reboundHintBefore =
                GetSubscriptionInvocationCount(hints);
            int reboundFeedbackBefore =
                GetSubscriptionInvocationCount(feedback);
            int reboundPresentationBefore =
                GetSubscriptionInvocationCount(presentation);
            AssertAccepted(AcceptTarget(fixture, "box_stool"));
            yield return null;
            AssertSubscriptionCount(hints, reboundHintBefore + 1);
            AssertSubscriptionCount(feedback, reboundFeedbackBefore + 1);
            AssertSubscriptionCount(
                presentation,
                reboundPresentationBefore + 1
            );
            Assert.That(safeHint.gameObject.activeSelf, Is.False);
            CompletePassword(fixture);
            Assert.That(
                Quaternion.Angle(Quaternion.identity, door.transform.rotation),
                Is.GreaterThan(0.1f)
            );

            RuntimeFixture replacement = CreateRuntimeFixture(
                "ResultPresenterPublisherB"
            );
            InvokePublic(
                hints,
                "Configure",
                replacement.Coordinator,
                safeHint,
                chestHint
            );
            InvokePublic(
                feedback,
                "Configure",
                replacement.Coordinator,
                feedbackRenderer,
                null
            );
            ConfigurePresentation(
                presentation,
                replacement.Coordinator,
                safeDoorBinding
            );
            int replacementHintBefore =
                GetSubscriptionInvocationCount(hints);
            int replacementFeedbackBefore =
                GetSubscriptionInvocationCount(feedback);
            int replacementPresentationBefore =
                GetSubscriptionInvocationCount(presentation);

            ResetPhaseOne(fixture);
            AssertAccepted(AcceptTarget(fixture, "box_stool"));
            yield return null;
            AssertSubscriptionCount(hints, replacementHintBefore);
            AssertSubscriptionCount(feedback, replacementFeedbackBefore);
            AssertSubscriptionCount(
                presentation,
                replacementPresentationBefore
            );
            Assert.That(safeHint.gameObject.activeSelf, Is.False);
            AssertColor(
                ReadBaseColor(feedbackRenderer),
                new Color(0.2f, 0.35f, 0.55f, 1f)
            );
            Assert.That(
                Quaternion.Angle(Quaternion.identity, door.transform.rotation),
                Is.LessThan(0.01f),
                "Presenters reacted to old publisher A."
            );

            AssertAccepted(AcceptTarget(replacement, "box_stool"));
            yield return null;
            AssertSubscriptionCount(hints, replacementHintBefore + 1);
            AssertSubscriptionCount(feedback, replacementFeedbackBefore + 1);
            AssertSubscriptionCount(
                presentation,
                replacementPresentationBefore + 1
            );
            Assert.That(safeHint.gameObject.activeSelf, Is.False);
            AssertColor(
                ReadBaseColor(feedbackRenderer),
                new Color(0.15f, 1f, 0.35f, 1f)
            );
        }

        [UnityTest]
        public IEnumerator RuntimeReenableHonorsTerminalSnapshotAndRebuildsChest()
        {
            RuntimeFixture fixture = CreateRuntimeFixture(
                "TerminalSnapshotAndChestRebuild"
            );
            Component phaseFour = fixture.Adapters[3];

            GameObject inputObject = new GameObject("PhaseFourInput");
            inputObject.transform.SetParent(fixture.Root.transform, false);
            BoxCollider inputCollider = inputObject.AddComponent<BoxCollider>();
            Component inputBinding = inputObject.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                inputBinding,
                "Configure",
                "key_a",
                phaseFour,
                Array.Empty<Behaviour>(),
                new Collider[] { inputCollider }
            );

            GameObject lid = new GameObject("ChestLid");
            lid.transform.SetParent(fixture.Root.transform, false);
            GameObject hinge = new GameObject("ChestHinge");
            hinge.transform.SetParent(fixture.Root.transform, false);
            object lidBinding = CreateHingeBinding(
                lid.transform,
                hinge.transform
            );
            string[] buttonIds = { "blue", "red", "yellow", "green" };
            var buttonObjects = new GameObject[buttonIds.Length];
            var buttonBindings = new object[buttonIds.Length];
            for (int index = 0; index < buttonIds.Length; index++)
            {
                buttonObjects[index] = new GameObject(buttonIds[index]);
                buttonObjects[index].transform.SetParent(
                    fixture.Root.transform,
                    false
                );
                buttonBindings[index] = CreateTargetStateBinding(
                    buttonIds[index],
                    buttonObjects[index].transform
                );
            }
            PresentationKeyProbe key = CreatePresentationKey(
                fixture.Root.transform,
                "RuntimeRebuildKey"
            );
            Component presentation = fixture.Root.AddComponent(
                RuntimeType("InteractionDeterministicPresentation")
            );
            ConfigurePhaseFourPresentation(
                presentation,
                fixture.Coordinator,
                lidBinding,
                buttonBindings,
                key.Binding
            );
            key.AssertLocked();

            CompletePhaseOne(fixture);
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(2)
            );
            AssertAccepted(InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                2,
                InvokeCoreFactory(
                    "PhaseInput",
                    "Pair",
                    "coin_dragon",
                    "plate_dragon"
                )
            ));
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(3)
            );
            AssertAccepted(InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                3,
                CreateTargetInput("picture_frame_a")
            ));
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(4)
            );
            Assert.That(inputCollider.enabled, Is.True);
            Assert.That(
                inputBinding.GetType().GetProperty("IsInputAvailable")
                    .GetValue(inputBinding),
                Is.True
            );

            ((Behaviour)presentation).enabled = false;
            AssertAccepted(InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                4,
                CreateTargetInput("key_a")
            ));
            yield return null;

            Assert.That(
                fixture.Coordinator.GetType().GetProperty("IsEnabled")
                    .GetValue(fixture.Coordinator),
                Is.False
            );
            Assert.That(
                phaseFour.GetType().GetProperty("IsEnabled")
                    .GetValue(phaseFour),
                Is.False
            );
            Assert.That(inputCollider.enabled, Is.False);
            Assert.That(
                inputBinding.GetType().GetProperty("IsInputAvailable")
                    .GetValue(inputBinding),
                Is.False
            );
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    lid.transform.localRotation
                ),
                Is.GreaterThan(0.1f)
            );
            foreach (GameObject buttonObject in buttonObjects)
            {
                Assert.That(
                    buttonObject.activeSelf,
                    Is.False
                );
            }
            key.AssertLocked();

            ((Behaviour)presentation).enabled = true;
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    lid.transform.localRotation
                ),
                Is.GreaterThan(0.1f),
                "PlayMode OnEnable did not rebuild the missed chest state."
            );
            foreach (GameObject buttonObject in buttonObjects)
            {
                Assert.That(
                    buttonObject.activeSelf,
                    Is.False,
                    "Deprecated colour buttons must stay disabled."
                );
            }
            key.AssertLocked();

            ((Behaviour)fixture.Coordinator).enabled = false;
            ((Behaviour)fixture.Coordinator).enabled = true;
            InvokePublic(fixture.Coordinator, "Enable");
            ((Behaviour)inputBinding).enabled = false;
            ((Behaviour)inputBinding).enabled = true;
            Assert.That(
                fixture.Coordinator.GetType().GetProperty("IsEnabled")
                    .GetValue(fixture.Coordinator),
                Is.False,
                "Runtime re-enable bypassed the terminal W1/W7 gate."
            );
            Assert.That(inputCollider.enabled, Is.False);
            Assert.That(
                inputBinding.GetType().GetProperty("IsInputAvailable")
                    .GetValue(inputBinding),
                Is.False
            );
        }

        [UnityTest]
        public IEnumerator DeprecatedHintPresenterStaysHiddenAcrossLifecycle()
        {
            RuntimeFixture fixture = CreateRuntimeFixture("HintAuthority");
            TextMesh safeA = CreateText(fixture.Root.transform, "SafeHintA");
            TextMesh chestA = CreateText(fixture.Root.transform, "ChestHintA");
            Component presenter = fixture.Root.AddComponent(
                RuntimeType("InteractionPlanHintPresenter")
            );
            InvokePublic(
                presenter,
                "Configure",
                fixture.Coordinator,
                safeA,
                chestA
            );

            AssertAccepted(AcceptTarget(fixture, "box_stool"));
            Assert.That(safeA.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = false;
            Assert.That(safeA.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = true;
            Assert.That(
                safeA.gameObject.activeSelf,
                Is.False,
                "Phase 1 must not rebuild a removed password hint."
            );

            TextMesh safeB = CreateText(fixture.Root.transform, "SafeHintB");
            TextMesh chestB = CreateText(fixture.Root.transform, "ChestHintB");
            InvokePublic(
                presenter,
                "Configure",
                fixture.Coordinator,
                safeB,
                chestB
            );
            Assert.That(safeA.gameObject.activeSelf, Is.False);
            Assert.That(safeB.gameObject.activeSelf, Is.False);

            UnityEngine.Object.DestroyImmediate(presenter);
            Assert.That(safeB.gameObject.activeSelf, Is.False);
            presenter = fixture.Root.AddComponent(
                RuntimeType("InteractionPlanHintPresenter")
            );
            InvokePublic(
                presenter,
                "Configure",
                fixture.Coordinator,
                safeB,
                chestB
            );
            Assert.That(
                safeB.gameObject.activeSelf,
                Is.False,
                "A recreated presenter must not show a Phase 1 password."
            );

            ResetPhaseOne(fixture);
            AssertAccepted(AcceptTarget(fixture, "box_stool"));
            CompletePassword(fixture);
            Assert.That(safeB.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = false;
            ((Behaviour)presenter).enabled = true;
            Assert.That(
                safeB.gameObject.activeSelf,
                Is.False,
                "A terminal Phase 1 hint must not be resurrected."
            );

            ResetPhaseOne(fixture);
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(2)
            );
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(3)
            );
            AssertAccepted(InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                3,
                CreateTargetInput("picture_frame_a")
            ));
            Assert.That(chestB.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = false;
            Assert.That(chestB.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = true;
            Assert.That(chestB.gameObject.activeSelf, Is.False);

            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(4)
            );
            AssertAccepted(InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                4,
                CreateTargetInput("key_a")
            ));
            Assert.That(chestB.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = false;
            ((Behaviour)presenter).enabled = true;
            Assert.That(
                chestB.gameObject.activeSelf,
                Is.False,
                "A terminal Phase 4 hint must not be resurrected."
            );

            ResetPhaseOne(fixture);
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(2)
            );
            object giveUpSnapshot = CreateGiveUpAvailablePhaseSnapshot(3);
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                giveUpSnapshot
            );
            AssertAccepted(InvokePublic(
                fixture.Coordinator,
                "GiveUpCurrentPhase",
                giveUpSnapshot
            ));
            Assert.That(chestB.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = false;
            ((Behaviour)presenter).enabled = true;
            Assert.That(chestB.gameObject.activeSelf, Is.False);

            InvokePublic(fixture.Coordinator, "Abort");
            Assert.That(chestB.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = false;
            ((Behaviour)presenter).enabled = true;
            Assert.That(chestB.gameObject.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PresenterConfigureIsAtomicAndTransfersOutputOwnership()
        {
            RuntimeFixture fixtureA = CreateRuntimeFixture("PresenterOutputA");
            RuntimeFixture fixtureB = CreateRuntimeFixture("PresenterOutputB");

            TextMesh safeA = CreateText(fixtureA.Root.transform, "SafeA");
            TextMesh chestA = CreateText(fixtureA.Root.transform, "ChestA");
            TextMesh safeB = CreateText(fixtureB.Root.transform, "SafeB");
            TextMesh chestB = CreateText(fixtureB.Root.transform, "ChestB");
            Component hints = fixtureA.Root.AddComponent(
                RuntimeType("InteractionPlanHintPresenter")
            );
            InvokePublic(
                hints,
                "Configure",
                fixtureA.Coordinator,
                safeA,
                chestA
            );
            AssertAccepted(AcceptTarget(fixtureA, "box_stool"));
            Assert.That(safeA.gameObject.activeSelf, Is.False);
            TargetInvocationException hintFailure =
                Assert.Throws<TargetInvocationException>(() => InvokePublic(
                    hints,
                    "Configure",
                    fixtureB.Coordinator,
                    safeB,
                    null
                ));
            Assert.That(
                hintFailure.InnerException,
                Is.TypeOf<ArgumentNullException>()
            );
            Assert.That(safeA.gameObject.activeSelf, Is.False);
            Assert.That(
                hints.GetType().GetProperty("Coordinator").GetValue(hints),
                Is.SameAs(fixtureA.Coordinator)
            );
            Assert.That(
                InvokePublic(
                    fixtureA.Coordinator,
                    "AcceptInput",
                    1,
                    InvokeCoreFactory("PhaseInput", "Submit")
                ),
                Is.Not.Null
            );
            Assert.That(
                safeA.gameObject.activeSelf,
                Is.False,
                "A failed Configure lost the hint publisher-A subscription."
            );
            ResetPhaseOne(fixtureA);
            AssertAccepted(AcceptTarget(fixtureA, "box_stool"));
            Assert.That(safeA.gameObject.activeSelf, Is.False);
            InvokePublic(
                hints,
                "Configure",
                fixtureB.Coordinator,
                safeB,
                chestB
            );
            Assert.That(safeA.gameObject.activeSelf, Is.False);
            Assert.That(safeB.gameObject.activeSelf, Is.False);
            AssertAccepted(AcceptTarget(fixtureB, "box_stool"));
            Assert.That(safeB.gameObject.activeSelf, Is.False);
            UnityEngine.Object.DestroyImmediate(hints);
            Assert.That(safeB.gameObject.activeSelf, Is.False);

            GameObject feedbackAObject = Track(
                GameObject.CreatePrimitive(PrimitiveType.Cube)
            );
            feedbackAObject.transform.SetParent(fixtureA.Root.transform, false);
            Renderer rendererA = feedbackAObject.GetComponent<Renderer>();
            Component feedback = feedbackAObject.AddComponent(
                RuntimeType("InteractionFeedbackPresenter")
            );
            GameObject feedbackBObject = Track(
                GameObject.CreatePrimitive(PrimitiveType.Cube)
            );
            feedbackBObject.transform.SetParent(fixtureB.Root.transform, false);
            Renderer rendererB = feedbackBObject.GetComponent<Renderer>();
            InvokePublic(
                feedback,
                "Configure",
                fixtureA.Coordinator,
                rendererA,
                null
            );
            ResetPhaseOne(fixtureA);
            AssertAccepted(AcceptTarget(fixtureA, "box_stool"));
            AssertColor(
                ReadBaseColor(rendererA),
                new Color(0.15f, 1f, 0.35f, 1f)
            );
            TargetInvocationException feedbackFailure =
                Assert.Throws<TargetInvocationException>(() => InvokePublic(
                    feedback,
                    "Configure",
                    fixtureB.Coordinator,
                    null,
                    null
                ));
            Assert.That(
                feedbackFailure.InnerException,
                Is.TypeOf<ArgumentNullException>()
            );
            AssertColor(
                ReadBaseColor(rendererA),
                new Color(0.15f, 1f, 0.35f, 1f)
            );
            Assert.That(
                feedback.GetType().GetProperty("Coordinator")
                    .GetValue(feedback),
                Is.SameAs(fixtureA.Coordinator)
            );
            ResetPhaseOne(fixtureA);
            Assert.That(
                InvokePublic(
                    fixtureA.Coordinator,
                    "AcceptInput",
                    1,
                    InvokeCoreFactory("PhaseInput", "Submit")
                ),
                Is.Not.Null
            );
            AssertColor(
                ReadBaseColor(rendererA),
                new Color(1f, 0.12f, 0.08f, 1f)
            );
            InvokePublic(
                feedback,
                "Configure",
                fixtureB.Coordinator,
                rendererB,
                null
            );
            AssertColor(
                ReadBaseColor(rendererA),
                new Color(0.2f, 0.35f, 0.55f, 1f)
            );
            AssertColor(
                ReadBaseColor(rendererB),
                new Color(0.2f, 0.35f, 0.55f, 1f)
            );
            ResetPhaseOne(fixtureB);
            AssertAccepted(AcceptTarget(fixtureB, "box_stool"));
            AssertColor(
                ReadBaseColor(rendererB),
                new Color(0.15f, 1f, 0.35f, 1f)
            );
            UnityEngine.Object.DestroyImmediate(feedback);
            AssertColor(
                ReadBaseColor(rendererB),
                new Color(0.2f, 0.35f, 0.55f, 1f)
            );

            ResetPhaseOne(fixtureA);
            ResetPhaseOne(fixtureB);
            GameObject doorA = new GameObject("DoorA");
            doorA.transform.SetParent(fixtureA.Root.transform, false);
            GameObject hingeA = new GameObject("HingeA");
            hingeA.transform.SetParent(fixtureA.Root.transform, false);
            GameObject doorB = new GameObject("DoorB");
            doorB.transform.SetParent(fixtureB.Root.transform, false);
            GameObject hingeB = new GameObject("HingeB");
            hingeB.transform.SetParent(fixtureB.Root.transform, false);
            object doorBindingA = CreateHingeBinding(
                doorA.transform,
                hingeA.transform
            );
            object doorBindingB = CreateHingeBinding(
                doorB.transform,
                hingeB.transform
            );
            PresentationKeyProbe keyA = CreatePresentationKey(
                fixtureA.Root.transform,
                "KeyA"
            );
            PresentationKeyProbe keyB = CreatePresentationKey(
                fixtureB.Root.transform,
                "KeyB"
            );
            GameObject buttonA = new GameObject("ButtonA");
            buttonA.transform.SetParent(fixtureA.Root.transform, false);
            GameObject buttonB = new GameObject("ButtonB");
            buttonB.transform.SetParent(fixtureB.Root.transform, false);
            object stateA = CreateTargetStateBinding(
                "blue",
                buttonA.transform
            );
            object stateB = CreateTargetStateBinding(
                "blue",
                buttonB.transform
            );
            Component presentation = fixtureA.Root.AddComponent(
                RuntimeType("InteractionDeterministicPresentation")
            );
            ConfigurePresentationWithKey(
                presentation,
                fixtureA.Coordinator,
                doorBindingA,
                keyA.Binding,
                stateA
            );
            keyA.AssertLocked();
            InvokePublic(stateA, "Activate");
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    buttonA.transform.localRotation
                ),
                Is.GreaterThan(0.1f)
            );
            CompletePhaseOne(fixtureA);
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    doorA.transform.rotation
                ),
                Is.GreaterThan(0.1f)
            );
            TargetInvocationException presentationFailure =
                Assert.Throws<TargetInvocationException>(() =>
                    ConfigurePresentationWithKey(
                        presentation,
                        null,
                        doorBindingB,
                        keyB.Binding,
                        stateB
                    ));
            Assert.That(
                presentationFailure.InnerException,
                Is.TypeOf<ArgumentNullException>()
            );
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    doorA.transform.rotation
                ),
                Is.GreaterThan(0.1f)
            );
            keyA.AssertLocked();
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    buttonA.transform.localRotation
                ),
                Is.GreaterThan(0.1f),
                "A failed Configure altered output A."
            );
            ResetPhaseOne(fixtureA);
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    doorA.transform.rotation
                ),
                Is.LessThan(0.01f),
                "A failed Configure lost the presentation-A subscription."
            );
            CompletePhaseOne(fixtureA);
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    doorA.transform.rotation
                ),
                Is.GreaterThan(0.1f)
            );

            ConfigurePresentationWithKey(
                presentation,
                fixtureB.Coordinator,
                doorBindingB,
                keyB.Binding,
                stateB
            );
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    doorA.transform.rotation
                ),
                Is.LessThan(0.01f)
            );
            keyA.AssertAuthored();
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    buttonA.transform.localRotation
                ),
                Is.LessThan(0.01f),
                "Presentation did not restore old target-state output A."
            );
            keyB.AssertLocked();
            InvokePublic(stateB, "Activate");
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    buttonB.transform.localRotation
                ),
                Is.GreaterThan(0.1f)
            );
            CompletePhaseOne(fixtureB);
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    doorB.transform.rotation
                ),
                Is.GreaterThan(0.1f)
            );
            UnityEngine.Object.DestroyImmediate(presentation);
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    doorB.transform.rotation
                ),
                Is.LessThan(0.01f)
            );
            keyB.AssertAuthored();
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    buttonB.transform.localRotation
                ),
                Is.LessThan(0.01f),
                "Destroy did not restore target-state output B."
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator FeedbackPresenterStopsOnlyItsOwnedAudioOutput()
        {
            RuntimeFixture fixtureA = CreateRuntimeFixture("FeedbackAudioA");
            RuntimeFixture fixtureB = CreateRuntimeFixture("FeedbackAudioB");
            AudioClip clip = TrackTransientAsset(AudioClip.Create(
                "W7FeedbackOwnership",
                88200,
                1,
                44100,
                false
            ));

            GameObject outputA = Track(
                GameObject.CreatePrimitive(PrimitiveType.Cube)
            );
            outputA.name = "FeedbackAudioOutputA";
            outputA.transform.SetParent(fixtureA.Root.transform, false);
            Renderer rendererA = outputA.GetComponent<Renderer>();
            AudioSource sourceA = outputA.AddComponent<AudioSource>();
            sourceA.playOnAwake = false;
            Component presenter = outputA.AddComponent(
                RuntimeType("InteractionFeedbackPresenter")
            );

            GameObject outputB = Track(
                GameObject.CreatePrimitive(PrimitiveType.Cube)
            );
            outputB.name = "FeedbackAudioOutputB";
            outputB.transform.SetParent(fixtureB.Root.transform, false);
            Renderer rendererB = outputB.GetComponent<Renderer>();
            AudioSource sourceB = outputB.AddComponent<AudioSource>();
            sourceB.playOnAwake = false;

            InvokePublic(
                presenter,
                "ConfigureTestAudioClips",
                clip,
                clip
            );
            InvokePublic(
                presenter,
                "Configure",
                fixtureA.Coordinator,
                rendererA,
                sourceA
            );
            AssertAccepted(AcceptTarget(fixtureA, "box_stool"));
            Assert.That(sourceA.isPlaying, Is.True);

            TargetInvocationException failure =
                Assert.Throws<TargetInvocationException>(() => InvokePublic(
                    presenter,
                    "Configure",
                    fixtureB.Coordinator,
                    null,
                    sourceB
                ));
            Assert.That(
                failure.InnerException,
                Is.TypeOf<ArgumentNullException>()
            );
            Assert.That(
                presenter.GetType().GetProperty("FeedbackAudioSource")
                    .GetValue(presenter),
                Is.SameAs(sourceA)
            );
            Assert.That(
                sourceA.isPlaying,
                Is.True,
                "Invalid Configure stopped output A before validation."
            );
            int resultBefore = GetSubscriptionEventCount(
                presenter,
                "ResultProducedCount"
            );
            ResetPhaseOne(fixtureA);
            AssertAccepted(AcceptTarget(fixtureA, "box_stool"));
            Assert.That(
                GetSubscriptionEventCount(
                    presenter,
                    "ResultProducedCount"
                ),
                Is.EqualTo(resultBefore + 1),
                "Invalid Configure lost the publisher-A subscription."
            );
            Assert.That(sourceA.isPlaying, Is.True);

            sourceB.clip = clip;
            sourceB.Play();
            Assert.That(sourceB.isPlaying, Is.True);
            InvokePublic(
                presenter,
                "Configure",
                fixtureB.Coordinator,
                rendererB,
                sourceB
            );
            Assert.That(
                sourceA.isPlaying,
                Is.False,
                "Legal A-to-B transfer did not stop owned source A."
            );
            Assert.That(
                sourceB.isPlaying,
                Is.True,
                "Legal transfer stopped the newly assigned source B."
            );

            sourceB.Stop();
            AssertAccepted(AcceptTarget(fixtureB, "box_stool"));
            Assert.That(sourceB.isPlaying, Is.True);
            ((Behaviour)presenter).enabled = false;
            Assert.That(
                sourceB.isPlaying,
                Is.False,
                "Disable did not stop the currently owned source."
            );

            ((Behaviour)presenter).enabled = true;
            ResetPhaseOne(fixtureB);
            AssertAccepted(AcceptTarget(fixtureB, "box_stool"));
            Assert.That(sourceB.isPlaying, Is.True);
            UnityEngine.Object.DestroyImmediate(presenter);
            Assert.That(
                sourceB.isPlaying,
                Is.False,
                "Destroy did not stop the currently owned source."
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlannedKeyReleaseRestoresAuthoredPoseAndMotion()
        {
            RuntimeFixture fixtureA = CreateRuntimeFixture("KeyOwnershipA");
            RuntimeFixture fixtureB = CreateRuntimeFixture("KeyOwnershipB");
            GameObject nonConvexKey = new GameObject(
                "KinematicNonConvexKey"
            );
            nonConvexKey.transform.SetParent(
                fixtureA.Root.transform,
                false
            );
            Rigidbody nonConvexBody =
                nonConvexKey.AddComponent<Rigidbody>();
            nonConvexBody.isKinematic = true;
            nonConvexBody.useGravity = false;
            Mesh nonConvexMesh = TrackTransientAsset(new Mesh
            {
                name = "W7KinematicNonConvexKeyMesh"
            });
            nonConvexMesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up
            };
            nonConvexMesh.triangles = new[] { 0, 1, 2 };
            nonConvexMesh.RecalculateBounds();
            MeshCollider nonConvexCollider =
                nonConvexKey.AddComponent<MeshCollider>();
            nonConvexCollider.sharedMesh = nonConvexMesh;
            nonConvexCollider.convex = false;
            object nonConvexBinding = Activator.CreateInstance(
                RuntimeType("PlannedKeyReleaseBinding")
            );
            InvokePublic(
                nonConvexBinding,
                "Configure",
                "key_nonconvex_kinematic",
                nonConvexKey,
                new[] { nonConvexBody },
                Array.Empty<Behaviour>(),
                new Collider[] { nonConvexCollider }
            );
            InvokePublic(nonConvexBinding, "PrepareForRun", true);
            Assert.That(nonConvexBody.isKinematic, Is.True);
            Assert.That(nonConvexCollider.enabled, Is.False);
            LogAssert.NoUnexpectedReceived();

            PresentationKeyProbe keyA = CreatePresentationKey(
                fixtureA.Root.transform,
                "KeyOwnershipA"
            );
            PresentationKeyProbe keyB = CreatePresentationKey(
                fixtureB.Root.transform,
                "KeyOwnershipB"
            );
            Component presentation = fixtureA.Root.AddComponent(
                RuntimeType("InteractionDeterministicPresentation")
            );

            ConfigurePresentationWithKeyOnly(
                presentation,
                fixtureA.Coordinator,
                keyA.Binding
            );
            keyA.AssertLocked();
            LogAssert.NoUnexpectedReceived();
            keyA.PrepareReleaseAndDisplace();

            ConfigurePresentationWithKeyOnly(
                presentation,
                fixtureB.Coordinator,
                keyB.Binding
            );
            keyA.AssertAuthoredComplete();
            keyB.AssertLocked();
            LogAssert.NoUnexpectedReceived();
            keyB.PrepareReleaseAndDisplace();

            UnityEngine.Object.DestroyImmediate(presentation);
            keyB.AssertAuthoredComplete();
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AdapterRecoversAfterDestroyedCoordinatorReplacement()
        {
            RuntimeFixture fixtureA = CreateRuntimeFixture(
                "DestroyedCoordinatorA"
            );
            RuntimeFixture fixtureB = CreateRuntimeFixture(
                "DestroyedCoordinatorB"
            );
            Component adapter = fixtureA.Adapters[0];
            var acceptedResults = new EventCounter();
            SubscribeGenericEvent(adapter, "InputAccepted", acceptedResults);
            GameObject targetObject = Track(
                new GameObject("DestroyedCoordinatorTarget")
            );
            targetObject.transform.SetParent(fixtureA.Root.transform, false);
            BoxCollider targetCollider =
                targetObject.AddComponent<BoxCollider>();
            Component targetBinding = targetObject.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                targetBinding,
                "Configure",
                "box_stool",
                adapter,
                Array.Empty<Behaviour>(),
                new Collider[] { targetCollider }
            );
            Assert.That(
                targetBinding.GetType().GetProperty("IsInputAvailable")
                    .GetValue(targetBinding),
                Is.True
            );
            Assert.That(targetCollider.enabled, Is.True);

            UnityEngine.Object.DestroyImmediate(fixtureA.Coordinator);
            Assert.That(
                fixtureA.Coordinator == null,
                Is.True,
                "The publisher-A coordinator was not destroyed."
            );
            object destroyedAuthority = adapter.GetType()
                .GetProperty("Coordinator").GetValue(adapter);
            Assert.That(
                object.ReferenceEquals(
                    destroyedAuthority,
                    fixtureA.Coordinator
                ),
                Is.True,
                "The adapter did not retain publisher A's Unity wrapper."
            );
            Assert.That(
                object.ReferenceEquals(destroyedAuthority, null),
                Is.False,
                "This regression must exercise Unity fake-null, not CLR null."
            );
            InvokePublic(adapter, "Enable");
            Assert.That(
                adapter.GetType().GetProperty("IsEnabled").GetValue(adapter),
                Is.False,
                "A destroyed coordinator was mistaken for a standalone " +
                "adapter authority."
            );
            Assert.That(
                targetBinding.GetType().GetProperty("IsInputAvailable")
                    .GetValue(targetBinding),
                Is.False
            );
            Assert.That(
                targetCollider.enabled,
                Is.False,
                "The fake-null authority window reopened bound input."
            );
            Assert.That(
                InvokePublic(targetBinding, "AcceptInput"),
                Is.Null
            );

            InvokePublic(
                adapter,
                "Configure",
                fixtureB.Coordinator,
                fixtureB.Plan
            );
            InvokePublic(adapter, "Enable");
            Assert.That(
                adapter.GetType().GetProperty("IsEnabled").GetValue(adapter),
                Is.True
            );
            Assert.That(
                targetBinding.GetType().GetProperty("IsInputAvailable")
                    .GetValue(targetBinding),
                Is.True
            );
            Assert.That(targetCollider.enabled, Is.True);
            AssertAccepted(InvokePublic(targetBinding, "AcceptInput"));
            Assert.That(
                acceptedResults.Count,
                Is.EqualTo(1),
                "The adapter did not release its destroyed publisher A."
            );

            InvokePublic(
                adapter,
                "Configure",
                fixtureB.Coordinator,
                fixtureB.Plan
            );
            ResetPhaseOne(fixtureB);
            InvokePublic(adapter, "Enable");
            AssertAccepted(AcceptTarget(fixtureB, "box_stool"));
            Assert.That(
                acceptedResults.Count,
                Is.EqualTo(2),
                "Reconfigure duplicated the publisher-B subscription."
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator EachSubscribedEventMovesFromPublisherAToBExactlyOnce()
        {
            RuntimeFixture fixtureA = CreateRuntimeFixture("EventPublisherA");
            RuntimeFixture fixtureB = CreateRuntimeFixture("EventPublisherB");

            GameObject targetObject = Track(new GameObject("ResetTarget"));
            targetObject.transform.SetParent(fixtureA.Root.transform, false);
            BoxCollider targetCollider =
                targetObject.AddComponent<BoxCollider>();
            Component targetBinding = targetObject.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                targetBinding,
                "Configure",
                "box_stool",
                fixtureA.Adapters[0],
                Array.Empty<Behaviour>(),
                new Collider[] { targetCollider }
            );
            InvokePublic(
                targetBinding,
                "Configure",
                "box_stool",
                fixtureB.Adapters[0],
                Array.Empty<Behaviour>(),
                new Collider[] { targetCollider }
            );

            GameObject plateObject = Track(new GameObject("ResetPlate"));
            plateObject.transform.SetParent(fixtureA.Root.transform, false);
            BoxCollider plateCollider = plateObject.AddComponent<BoxCollider>();
            Component placement = plateObject.AddComponent(
                RuntimeType("InteractionPlacementBinding")
            );
            InvokePublic(
                placement,
                "Configure",
                "plate_a",
                fixtureA.Adapters[1],
                plateCollider,
                null
            );
            InvokePublic(
                placement,
                "Configure",
                "plate_a",
                fixtureB.Adapters[1],
                plateCollider,
                null
            );

            int targetResetBefore = GetSubscriptionEventCount(
                targetBinding,
                "ResetPerformedCount"
            );
            int placementResetBefore = GetSubscriptionEventCount(
                placement,
                "ResetPerformedCount"
            );
            InvokePublic(fixtureA.Adapters[0], "Reset");
            InvokePublic(fixtureA.Adapters[1], "Reset");
            Assert.That(
                GetSubscriptionEventCount(
                    targetBinding,
                    "ResetPerformedCount"
                ),
                Is.EqualTo(targetResetBefore)
            );
            Assert.That(
                GetSubscriptionEventCount(
                    placement,
                    "ResetPerformedCount"
                ),
                Is.EqualTo(placementResetBefore)
            );
            InvokePublic(fixtureB.Adapters[0], "Reset");
            InvokePublic(fixtureB.Adapters[1], "Reset");
            Assert.That(
                GetSubscriptionEventCount(
                    targetBinding,
                    "ResetPerformedCount"
                ),
                Is.EqualTo(targetResetBefore + 1)
            );
            Assert.That(
                GetSubscriptionEventCount(
                    placement,
                    "ResetPerformedCount"
                ),
                Is.EqualTo(placementResetBefore + 1)
            );

            TextMesh safeHint = CreateText(
                fixtureA.Root.transform,
                "EventSafeHint"
            );
            TextMesh chestHint = CreateText(
                fixtureA.Root.transform,
                "EventChestHint"
            );
            Component hints = fixtureA.Root.AddComponent(
                RuntimeType("InteractionPlanHintPresenter")
            );
            InvokePublic(
                hints,
                "Configure",
                fixtureA.Coordinator,
                safeHint,
                chestHint
            );

            GameObject feedbackObject = Track(
                GameObject.CreatePrimitive(PrimitiveType.Cube)
            );
            feedbackObject.transform.SetParent(fixtureA.Root.transform, false);
            Renderer feedbackRenderer = feedbackObject.GetComponent<Renderer>();
            Component feedback = feedbackObject.AddComponent(
                RuntimeType("InteractionFeedbackPresenter")
            );
            InvokePublic(
                feedback,
                "Configure",
                fixtureA.Coordinator,
                feedbackRenderer,
                null
            );

            GameObject door = Track(new GameObject("EventDoor"));
            door.transform.SetParent(fixtureA.Root.transform, false);
            GameObject hinge = Track(new GameObject("EventHinge"));
            hinge.transform.SetParent(fixtureA.Root.transform, false);
            Component presentation = fixtureA.Root.AddComponent(
                RuntimeType("InteractionDeterministicPresentation")
            );
            object doorBinding = CreateHingeBinding(
                door.transform,
                hinge.transform
            );
            ConfigurePresentation(
                presentation,
                fixtureA.Coordinator,
                doorBinding
            );

            InvokePublic(
                hints,
                "Configure",
                fixtureB.Coordinator,
                safeHint,
                chestHint
            );
            InvokePublic(
                feedback,
                "Configure",
                fixtureB.Coordinator,
                feedbackRenderer,
                null
            );
            ConfigurePresentation(
                presentation,
                fixtureB.Coordinator,
                doorBinding
            );
            Component[] presenters = { hints, feedback, presentation };
            string[] eventCountProperties =
            {
                "RunConfiguredCount",
                "RunResetCount",
                "ResultProducedCount"
            };
            var countsBefore = new int[
                presenters.Length,
                eventCountProperties.Length
            ];
            for (int presenterIndex = 0;
                presenterIndex < presenters.Length;
                presenterIndex++)
            {
                for (int eventIndex = 0;
                    eventIndex < eventCountProperties.Length;
                    eventIndex++)
                {
                    countsBefore[presenterIndex, eventIndex] =
                        GetSubscriptionEventCount(
                            presenters[presenterIndex],
                            eventCountProperties[eventIndex]
                        );
                }
            }

            InvokePublic(fixtureA.Coordinator, "Configure", fixtureA.Plan);
            ActivatePhaseOne(fixtureA);
            AssertAccepted(AcceptTarget(fixtureA, "box_stool"));
            InvokePublic(fixtureA.Coordinator, "Reset");
            AssertNamedEventCounts(
                presenters,
                eventCountProperties,
                countsBefore,
                0,
                0,
                0,
                "Publisher A remained subscribed after reconfigure."
            );

            InvokePublic(fixtureB.Coordinator, "Configure", fixtureB.Plan);
            ActivatePhaseOne(fixtureB);
            AssertAccepted(AcceptTarget(fixtureB, "box_stool"));
            InvokePublic(fixtureB.Coordinator, "Reset");
            AssertNamedEventCounts(
                presenters,
                eventCountProperties,
                countsBefore,
                1,
                1,
                1,
                "Publisher B was not handled exactly once per event."
            );
            yield return null;
        }

        private RuntimeFixture CreateRuntimeFixture(string name)
        {
            GameObject root = Track(new GameObject("W7PlayMode_" + name));
            Component coordinator = root.AddComponent(
                RuntimeType("InteractionPhaseCoordinator")
            );
            string[] adapterTypeNames =
            {
                "PhaseOneInteractionAdapter",
                "PhaseTwoInteractionAdapter",
                "PhaseThreeInteractionAdapter",
                "PhaseFourInteractionAdapter",
                "PhaseFiveInteractionAdapter",
                "PhaseSixInteractionAdapter"
            };
            Type adapterBase = RuntimeType("InteractionPhaseAdapter");
            Array configuredAdapters = Array.CreateInstance(
                adapterBase,
                adapterTypeNames.Length
            );
            var adapters = new Component[adapterTypeNames.Length];
            for (int index = 0; index < adapterTypeNames.Length; index++)
            {
                GameObject child = new GameObject("Phase" + (index + 1));
                child.transform.SetParent(root.transform, false);
                adapters[index] = child.AddComponent(
                    RuntimeType(adapterTypeNames[index])
                );
                configuredAdapters.SetValue(adapters[index], index);
            }
            InvokePublic(coordinator, "ConfigureAdapters", configuredAdapters);
            object plan = CreateRunPlan();
            var fixture = new RuntimeFixture(root, coordinator, adapters, plan);
            ResetPhaseOne(fixture);
            return fixture;
        }

        private static void ResetPhaseOne(RuntimeFixture fixture)
        {
            InvokePublic(fixture.Coordinator, "Configure", fixture.Plan);
            ActivatePhaseOne(fixture);
        }

        private static void ActivatePhaseOne(RuntimeFixture fixture)
        {
            ActivatePhase(fixture, 1);
        }

        private static void ActivatePhase(
            RuntimeFixture fixture,
            int phaseId)
        {
            InvokePublic(fixture.Coordinator, "Enable");
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(phaseId)
            );
        }

        private static object AcceptTarget(
            RuntimeFixture fixture,
            string targetId)
        {
            return InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                1,
                CreateTargetInput(targetId)
            );
        }

        private static void CompletePhaseOne(RuntimeFixture fixture)
        {
            AssertAccepted(AcceptTarget(fixture, "box_stool"));
            CompletePassword(fixture);
        }

        private static void CompletePassword(RuntimeFixture fixture)
        {
            // Phase 1 completes when the planned box is touched.
        }

        private static void ConfigureAvailabilitySubscriber(
            Component subscriber,
            Component adapter,
            Collider collider,
            Transform placementSnapPoint = null,
            Behaviour targetBehaviour = null)
        {
            switch (subscriber.GetType().Name)
            {
                case "InteractionTargetBinding":
                    InvokePublic(
                        subscriber,
                        "Configure",
                        "box_stool",
                        adapter,
                        targetBehaviour == null
                            ? Array.Empty<Behaviour>()
                            : new[] { targetBehaviour },
                        new Collider[] { collider }
                    );
                    break;
                case "InteractionDigitBinding":
                    InvokePublic(subscriber, "Configure", 1, adapter, collider);
                    break;
                case "InteractionPasswordBackspaceBinding":
                case "InteractionPasswordSubmitBinding":
                    InvokePublic(subscriber, "Configure", adapter, collider);
                    break;
                case "InteractionPlacementBinding":
                    InvokePublic(
                        subscriber,
                        "Configure",
                        "plate_dragon",
                        adapter,
                        collider,
                        placementSnapPoint
                    );
                    break;
                default:
                    throw new InvalidOperationException(subscriber.GetType().Name);
            }
        }

        private static void InvokeInvalidSubscriberConfigure(
            Component subscriber,
            Component adapter,
            Collider collider,
            Behaviour targetBehaviour)
        {
            switch (subscriber.GetType().Name)
            {
                case "InteractionTargetBinding":
                    InvokePublic(
                        subscriber,
                        "Configure",
                        string.Empty,
                        adapter,
                        targetBehaviour == null
                            ? Array.Empty<Behaviour>()
                            : new[] { targetBehaviour },
                        new Collider[] { collider }
                    );
                    break;
                case "InteractionDigitBinding":
                    InvokePublic(subscriber, "Configure", 10, adapter, collider);
                    break;
                case "InteractionPasswordBackspaceBinding":
                case "InteractionPasswordSubmitBinding":
                    InvokePublic(subscriber, "Configure", null, collider);
                    break;
                case "InteractionPlacementBinding":
                    InvokePublic(
                        subscriber,
                        "Configure",
                        string.Empty,
                        adapter,
                        collider,
                        null
                    );
                    break;
                default:
                    throw new InvalidOperationException(
                        subscriber.GetType().Name
                    );
            }
        }

        private PlacementProbe CreatePlacementProbe(
            RuntimeFixture fixture,
            Component phaseTwo)
        {
            GameObject parent = new GameObject("PlacementCoinParent");
            parent.transform.SetParent(fixture.Root.transform, false);
            GameObject coin = new GameObject("coin_dragon");
            coin.transform.SetParent(parent.transform, false);
            coin.transform.localPosition = new Vector3(1.2f, 2.3f, 3.4f);
            coin.transform.localRotation = Quaternion.Euler(11f, 22f, 33f);
            Rigidbody body = coin.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = new Vector3(0.3f, 0.4f, 0.5f);
            body.angularVelocity = new Vector3(0.6f, 0.7f, 0.8f);
            Component coinBinding = coin.AddComponent(
                RuntimeType("InteractionTargetBinding")
            );
            InvokePublic(
                coinBinding,
                "Configure",
                "coin_dragon",
                phaseTwo,
                Array.Empty<Behaviour>(),
                Array.Empty<Collider>()
            );

            GameObject snap = new GameObject("PlacementSnapPoint");
            snap.transform.SetParent(fixture.Root.transform, false);
            snap.transform.position = new Vector3(20f, 30f, 40f);
            snap.transform.rotation = Quaternion.Euler(45f, 55f, 65f);
            return new PlacementProbe(coin, coinBinding, body, snap.transform);
        }

        private static Component CreateReplacementAdapter(
            RuntimeFixture fixture,
            Component publisher,
            string subscriberTypeName)
        {
            GameObject replacementObject = new GameObject(
                subscriberTypeName + "_PublisherB"
            );
            replacementObject.transform.SetParent(
                fixture.Root.transform,
                false
            );
            Component replacement = replacementObject.AddComponent(
                publisher.GetType()
            );
            InvokePublic(
                replacement,
                "Configure",
                fixture.Coordinator,
                fixture.Plan
            );
            return replacement;
        }

        private static int GetSubscriptionInvocationCount(Component subscriber)
        {
            PropertyInfo diagnosticProperty = subscriber.GetType().GetProperty(
                "SubscriptionDiagnostic",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(
                diagnosticProperty,
                Is.Not.Null,
                subscriber.GetType().Name +
                " lacks its UNITY_INCLUDE_TESTS diagnostic."
            );
            object diagnostic = diagnosticProperty.GetValue(subscriber);
            Assert.That(diagnostic, Is.Not.Null);
            return (int)diagnostic.GetType().GetProperty(
                "InvocationCount",
                BindingFlags.Instance | BindingFlags.Public
            ).GetValue(diagnostic);
        }

        private static int GetSubscriptionEventCount(
            Component subscriber,
            string propertyName)
        {
            PropertyInfo diagnosticProperty = subscriber.GetType().GetProperty(
                "SubscriptionDiagnostic",
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(
                diagnosticProperty,
                Is.Not.Null,
                subscriber.GetType().Name +
                " lacks its UNITY_INCLUDE_TESTS diagnostic."
            );
            object diagnostic = diagnosticProperty.GetValue(subscriber);
            Assert.That(diagnostic, Is.Not.Null);
            PropertyInfo eventProperty = diagnostic.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(
                eventProperty,
                Is.Not.Null,
                diagnostic.GetType().Name + " lacks " + propertyName + "."
            );
            return (int)eventProperty.GetValue(diagnostic);
        }

        private static void AssertNamedEventCounts(
            Component[] subscribers,
            string[] eventProperties,
            int[,] countsBefore,
            int runConfiguredDelta,
            int runResetDelta,
            int resultProducedDelta,
            string message)
        {
            int[] expectedDeltas =
            {
                runConfiguredDelta,
                runResetDelta,
                resultProducedDelta
            };
            for (int subscriberIndex = 0;
                subscriberIndex < subscribers.Length;
                subscriberIndex++)
            {
                for (int eventIndex = 0;
                    eventIndex < eventProperties.Length;
                    eventIndex++)
                {
                    Assert.That(
                        GetSubscriptionEventCount(
                            subscribers[subscriberIndex],
                            eventProperties[eventIndex]
                        ),
                        Is.EqualTo(
                            countsBefore[subscriberIndex, eventIndex] +
                            expectedDeltas[eventIndex]
                        ),
                        subscribers[subscriberIndex].GetType().Name + " " +
                        eventProperties[eventIndex] + ": " + message
                    );
                }
            }
        }

        private static void AssertSubscriptionCount(
            Component subscriber,
            int expected,
            string message = null)
        {
            Assert.That(
                GetSubscriptionInvocationCount(subscriber),
                Is.EqualTo(expected),
                message ?? subscriber.GetType().Name +
                    " did not handle the publisher event exactly once."
            );
        }

        private static void ConfigurePresentation(
            Component presentation,
            Component coordinator,
            object safeDoorBinding)
        {
            Type hingeType = RuntimeType("DeterministicHingeBinding");
            Type stateType = RuntimeType("DeterministicTargetStateBinding");
            Type keyType = RuntimeType("PlannedKeyReleaseBinding");
            Array noStates = Array.CreateInstance(stateType, 0);
            InvokePublic(
                presentation,
                "Configure",
                coordinator,
                safeDoorBinding,
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                noStates,
                noStates,
                noStates,
                Array.CreateInstance(keyType, 0)
            );
        }

        private static void ConfigurePresentationWithKey(
            Component presentation,
            Component coordinator,
            object safeDoorBinding,
            object keyBinding,
            object chestStateBinding)
        {
            Type hingeType = RuntimeType("DeterministicHingeBinding");
            Type stateType = RuntimeType("DeterministicTargetStateBinding");
            Type keyType = RuntimeType("PlannedKeyReleaseBinding");
            Array noStates = Array.CreateInstance(stateType, 0);
            Array chestStates = Array.CreateInstance(stateType, 1);
            chestStates.SetValue(chestStateBinding, 0);
            Array keys = Array.CreateInstance(keyType, 1);
            keys.SetValue(keyBinding, 0);
            InvokePublic(
                presentation,
                "Configure",
                coordinator,
                safeDoorBinding,
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                chestStates,
                noStates,
                noStates,
                keys
            );
        }

        private static void ConfigurePhaseFourPresentation(
            Component presentation,
            Component coordinator,
            object chestLidBinding,
            object[] chestStateBindings,
            object keyBinding)
        {
            Type hingeType = RuntimeType("DeterministicHingeBinding");
            Type stateType = RuntimeType("DeterministicTargetStateBinding");
            Type keyType = RuntimeType("PlannedKeyReleaseBinding");
            Array chestStates = Array.CreateInstance(
                stateType,
                chestStateBindings.Length
            );
            for (int index = 0; index < chestStateBindings.Length; index++)
            {
                chestStates.SetValue(chestStateBindings[index], index);
            }
            Array noStates = Array.CreateInstance(stateType, 0);
            Array keys = Array.CreateInstance(keyType, 1);
            keys.SetValue(keyBinding, 0);
            InvokePublic(
                presentation,
                "Configure",
                coordinator,
                Activator.CreateInstance(hingeType),
                chestLidBinding,
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                chestStates,
                noStates,
                noStates,
                keys
            );
        }

        private static void ConfigurePhaseFivePresentation(
            Component presentation,
            Component coordinator,
            object cabinetStateBinding)
        {
            Type hingeType = RuntimeType("DeterministicHingeBinding");
            Type stateType = RuntimeType("DeterministicTargetStateBinding");
            Type keyType = RuntimeType("PlannedKeyReleaseBinding");
            Array noStates = Array.CreateInstance(stateType, 0);
            Array cabinetStates = Array.CreateInstance(stateType, 1);
            cabinetStates.SetValue(cabinetStateBinding, 0);
            InvokePublic(
                presentation,
                "Configure",
                coordinator,
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                noStates,
                cabinetStates,
                noStates,
                Array.CreateInstance(keyType, 0)
            );
        }

        private static void ConfigurePresentationWithKeyOnly(
            Component presentation,
            Component coordinator,
            object keyBinding)
        {
            Type hingeType = RuntimeType("DeterministicHingeBinding");
            Type stateType = RuntimeType("DeterministicTargetStateBinding");
            Type keyType = RuntimeType("PlannedKeyReleaseBinding");
            Array noStates = Array.CreateInstance(stateType, 0);
            Array keys = Array.CreateInstance(keyType, 1);
            keys.SetValue(keyBinding, 0);
            InvokePublic(
                presentation,
                "Configure",
                coordinator,
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                Activator.CreateInstance(hingeType),
                noStates,
                noStates,
                noStates,
                keys
            );
        }

        private static object CreateTargetStateBinding(
            string targetId,
            Transform target)
        {
            object binding = Activator.CreateInstance(
                RuntimeType("DeterministicTargetStateBinding")
            );
            InvokePublic(
                binding,
                "Configure",
                targetId,
                target,
                new Vector3(-14f, 0f, 0f)
            );
            return binding;
        }

        private static PresentationKeyProbe CreatePresentationKey(
            Transform parent,
            string name)
        {
            GameObject key = new GameObject(name);
            key.transform.SetParent(parent, false);
            key.transform.localPosition = new Vector3(0.7f, 1.1f, -0.4f);
            key.transform.localRotation = Quaternion.Euler(7f, 19f, 31f);
            Rigidbody body = key.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = new Vector3(0.11f, 0.22f, 0.33f);
            body.angularVelocity = new Vector3(0.44f, 0.55f, 0.66f);
            GameObject child = new GameObject(name + "_ChildBody");
            child.transform.SetParent(key.transform, false);
            child.transform.localPosition = new Vector3(-0.2f, 0.3f, 0.4f);
            child.transform.localRotation = Quaternion.Euler(13f, 17f, 23f);
            Rigidbody childBody = child.AddComponent<Rigidbody>();
            childBody.useGravity = false;
            childBody.isKinematic = true;
            BoxCollider collider = key.AddComponent<BoxCollider>();
            collider.enabled = true;
            object binding = Activator.CreateInstance(
                RuntimeType("PlannedKeyReleaseBinding")
            );
            InvokePublic(
                binding,
                "Configure",
                "key_a",
                key,
                new Rigidbody[] { body, null, childBody },
                Array.Empty<Behaviour>(),
                new Collider[] { collider }
            );
            return new PresentationKeyProbe(
                binding,
                key,
                new[] { body, childBody },
                collider
            );
        }

        private static object CreateHingeBinding(
            Transform movingPart,
            Transform hinge)
        {
            object binding = Activator.CreateInstance(
                RuntimeType("DeterministicHingeBinding")
            );
            InvokePublic(binding, "Configure", movingPart, hinge, Vector3.up, 90f);
            return binding;
        }

        private static TextMesh CreateText(Transform parent, string name)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.AddComponent<TextMesh>();
        }

        private static Color ReadBaseColor(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor(Shader.PropertyToID("_BaseColor"));
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f));
        }

        private static void AssertAccepted(object result)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result.GetType().GetProperty("Accepted").GetValue(result),
                Is.True
            );
        }

        private static void SubscribeGenericEvent(
            object publisher,
            string eventName,
            EventCounter counter)
        {
            SubscribeGenericEvent(
                publisher,
                eventName,
                counter,
                nameof(EventCounter.Observe)
            );
        }

        private static void SubscribeGenericEvent(
            object publisher,
            string eventName,
            object observerTarget,
            string genericMethodName)
        {
            EventInfo eventInfo = publisher.GetType().GetEvent(eventName);
            Assert.That(eventInfo, Is.Not.Null);
            Type argumentType = eventInfo.EventHandlerType
                .GetGenericArguments()[0];
            MethodInfo observer = observerTarget.GetType().GetMethod(
                genericMethodName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.That(observer, Is.Not.Null);
            observer = observer.MakeGenericMethod(argumentType);
            Delegate handler = Delegate.CreateDelegate(
                eventInfo.EventHandlerType,
                observerTarget,
                observer
            );
            eventInfo.AddEventHandler(publisher, handler);
        }

        private static object CreateTargetInput(string targetId)
        {
            return InvokeCoreFactory("PhaseInput", "Target", targetId);
        }

        private static object InvokeCoreFactory(
            string typeName,
            string methodName,
            params object[] arguments)
        {
            return CoreType(typeName).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            ).Invoke(null, arguments);
        }

        private static TextMeshProUGUI NewLabText(
            Transform parent,
            string name)
        {
            var gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI)
            );
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<TextMeshProUGUI>();
        }

        private static Button NewLabButton(Transform parent, string name)
        {
            var gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button)
            );
            gameObject.transform.SetParent(parent, false);
            Button button = gameObject.GetComponent<Button>();
            button.targetGraphic = gameObject.GetComponent<Image>();
            return button;
        }

        private static object CreatePhaseSnapshot(int phaseId)
        {
            Type snapshotType = CoreType("PhaseExecutionSnapshot");
            object firstPlayback = Enum.Parse(
                CoreType("PhaseState"),
                "FirstPlayback"
            );
            return Activator.CreateInstance(
                snapshotType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    phaseId,
                    firstPlayback,
                    null,
                    false,
                    false,
                    false,
                    0,
                    0,
                    TimeSpan.Zero
                },
                culture: null
            );
        }

        private static object CreateGiveUpAvailablePhaseSnapshot(int phaseId)
        {
            Type snapshotType = CoreType("PhaseExecutionSnapshot");
            object active = Enum.Parse(CoreType("PhaseState"), "Active");
            return Activator.CreateInstance(
                snapshotType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    phaseId,
                    active,
                    null,
                    true,
                    true,
                    false,
                    0,
                    0,
                    TimeSpan.Zero
                },
                culture: null
            );
        }

        private static object CreateRunPlan()
        {
            string[] sentenceIds =
                { "001", "004", "013", "016", "025", "026" };
            Type contentType = CoreType("InstructionContentReference");
            Type phaseType = CoreType("RunPhasePlan");
            Type catalogType = CoreType("TaskVariantCatalog");
            Array phases = Array.CreateInstance(phaseType, sentenceIds.Length);
            MethodInfo forSentence = catalogType.GetMethod(
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
                        new DateTimeOffset(
                            2026,
                            8,
                            26,
                            0,
                            0,
                            0,
                            TimeSpan.Zero
                        ),
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

            object assignment = Activator.CreateInstance(
                CoreType("AssistanceAssignment"),
                new object[]
                {
                    Enum.Parse(CoreType("AssistanceCondition"), "SignOnly"),
                    0,
                    0,
                    Enum.Parse(
                        CoreType("AssistanceAssignmentMode"),
                        "RandomizedBlock"
                    )
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
                    "pilot-20260826",
                    "P001",
                    "run_w7_playmode_subscription",
                    "app_w7_playmode_subscription",
                    new DateTimeOffset(
                        2026,
                        8,
                        26,
                        0,
                        0,
                        0,
                        TimeSpan.Zero
                    ),
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

        private static object InvokePublic(
            object target,
            string methodName,
            params object[] arguments)
        {
            MethodInfo[] candidates = target.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public
            );
            for (int index = 0; index < candidates.Length; index++)
            {
                MethodInfo candidate = candidates[index];
                if (candidate.Name == methodName &&
                    candidate.GetParameters().Length == arguments.Length)
                {
                    try
                    {
                        return candidate.Invoke(target, arguments);
                    }
                    catch (ArgumentException)
                    {
                        // Try another public overload with the same arity.
                    }
                }
            }
            throw new MissingMethodException(target.GetType().FullName, methodName);
        }

        private static object InvokePublicOverload(
            object target,
            string methodName,
            Type parameterType,
            object argument)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { parameterType },
                modifiers: null
            );
            Assert.That(method, Is.Not.Null);
            return method.Invoke(target, new[] { argument });
        }

        private GameObject Track(GameObject gameObject)
        {
            if (gameObject.transform.parent == null)
            {
                roots.Add(gameObject);
            }
            return gameObject;
        }

        private T TrackTransientAsset<T>(T asset)
            where T : UnityEngine.Object
        {
            transientAssets.Add(asset);
            return asset;
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

        private sealed class EventCounter
        {
            public int Count { get; private set; }

            public void Observe<T>(T result)
            {
                Count++;
            }
        }

        private sealed class PhaseFiveResetAndReentryProbe
        {
            private readonly Component adapter;

            public PhaseFiveResetAndReentryProbe(Component targetAdapter)
            {
                adapter = targetAdapter;
            }

            public object ReentrantResult { get; private set; }

            public void Observe<T>(T result)
            {
                InvokePublic(adapter, "Reset");
                InvokePublic(adapter, "Enable");
                ReentrantResult = InvokePublic(
                    adapter,
                    "AcceptTarget",
                    "button_b"
                );
            }
        }

        private sealed class AdapterResetProbe
        {
            private readonly Component adapter;

            public AdapterResetProbe(Component targetAdapter)
            {
                adapter = targetAdapter;
            }

            public int Count { get; private set; }

            public void Observe<T>(T result)
            {
                Count++;
                InvokePublic(adapter, "Reset");
            }
        }

        private sealed class DestroyGameObjectProbe
        {
            private readonly GameObject target;

            public DestroyGameObjectProbe(GameObject targetObject)
            {
                target = targetObject;
            }

            public void Observe<T>(T result)
            {
                if (target != null)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
        }

        private sealed class PlacementProbe
        {
            private readonly GameObject coin;
            private readonly Rigidbody body;
            private readonly Transform initialParent;
            private readonly Vector3 initialLocalPosition;
            private readonly Quaternion initialLocalRotation;
            private readonly bool initialIsKinematic;
            private readonly bool initialUseGravity;
            private readonly Vector3 initialLinearVelocity;
            private readonly Vector3 initialAngularVelocity;

            public PlacementProbe(
                GameObject targetCoin,
                Component coinBinding,
                Rigidbody targetBody,
                Transform snapPoint)
            {
                coin = targetCoin;
                CoinBinding = coinBinding;
                body = targetBody;
                SnapPoint = snapPoint;
                initialParent = coin.transform.parent;
                initialLocalPosition = coin.transform.localPosition;
                initialLocalRotation = coin.transform.localRotation;
                initialIsKinematic = body.isKinematic;
                initialUseGravity = body.useGravity;
                initialLinearVelocity = body.linearVelocity;
                initialAngularVelocity = body.angularVelocity;
            }

            public Component CoinBinding { get; }
            public Transform SnapPoint { get; }

            public void AssertUnchanged()
            {
                Assert.That(coin.transform.parent, Is.SameAs(initialParent));
                Assert.That(
                    Vector3.Distance(
                        coin.transform.localPosition,
                        initialLocalPosition
                    ),
                    Is.LessThan(0.0001f)
                );
                Assert.That(
                    Quaternion.Angle(
                        coin.transform.localRotation,
                        initialLocalRotation
                    ),
                    Is.LessThan(0.0001f)
                );
                Assert.That(body.isKinematic, Is.EqualTo(initialIsKinematic));
                Assert.That(body.useGravity, Is.EqualTo(initialUseGravity));
                Assert.That(
                    Vector3.Distance(
                        body.linearVelocity,
                        initialLinearVelocity
                    ),
                    Is.LessThan(0.0001f)
                );
                Assert.That(
                    Vector3.Distance(
                        body.angularVelocity,
                        initialAngularVelocity
                    ),
                    Is.LessThan(0.0001f)
                );
            }
        }

        private sealed class PresentationKeyProbe
        {
            private readonly GameObject key;
            private readonly Rigidbody[] bodies;
            private readonly Collider collider;
            private readonly Vector3 authoredRootLocalPosition;
            private readonly Quaternion authoredRootLocalRotation;
            private readonly Vector3[] authoredBodyLocalPositions;
            private readonly Quaternion[] authoredBodyLocalRotations;
            private readonly bool[] authoredBodyKinematic;
            private readonly bool[] authoredBodyGravity;
            private readonly Vector3[] authoredLinearVelocities;
            private readonly Vector3[] authoredAngularVelocities;

            public PresentationKeyProbe(
                object binding,
                GameObject targetKey,
                Rigidbody[] targetBodies,
                Collider targetCollider)
            {
                Binding = binding;
                key = targetKey;
                bodies = targetBodies;
                collider = targetCollider;
                authoredRootLocalPosition = key.transform.localPosition;
                authoredRootLocalRotation = key.transform.localRotation;
                authoredBodyLocalPositions = new Vector3[bodies.Length];
                authoredBodyLocalRotations = new Quaternion[bodies.Length];
                authoredBodyKinematic = new bool[bodies.Length];
                authoredBodyGravity = new bool[bodies.Length];
                authoredLinearVelocities = new Vector3[bodies.Length];
                authoredAngularVelocities = new Vector3[bodies.Length];
                for (int index = 0; index < bodies.Length; index++)
                {
                    authoredBodyLocalPositions[index] =
                        bodies[index].transform.localPosition;
                    authoredBodyLocalRotations[index] =
                        bodies[index].transform.localRotation;
                    authoredBodyKinematic[index] = bodies[index].isKinematic;
                    authoredBodyGravity[index] = bodies[index].useGravity;
                    authoredLinearVelocities[index] =
                        bodies[index].linearVelocity;
                    authoredAngularVelocities[index] =
                        bodies[index].angularVelocity;
                }
            }

            public object Binding { get; }

            public void AssertLocked()
            {
                for (int index = 0; index < bodies.Length; index++)
                {
                    Assert.That(bodies[index].isKinematic, Is.True);
                    Assert.That(bodies[index].useGravity, Is.False);
                    Assert.That(
                        bodies[index].linearVelocity,
                        Is.EqualTo(Vector3.zero)
                    );
                    Assert.That(
                        bodies[index].angularVelocity,
                        Is.EqualTo(Vector3.zero)
                    );
                }
                Assert.That(collider.enabled, Is.False);
            }

            public void AssertAuthored()
            {
                AssertAuthoredComplete();
            }

            public void AssertReleased()
            {
                Assert.That(key.activeSelf, Is.True);
                for (int index = 0; index < bodies.Length; index++)
                {
                    Assert.That(bodies[index].isKinematic, Is.False);
                    Assert.That(bodies[index].useGravity, Is.True);
                }
                Assert.That(collider.enabled, Is.True);
            }

            public void PrepareReleaseAndDisplace()
            {
                InvokePublic(Binding, "PrepareForRun", true);
                InvokePublic(Binding, "Release");
                key.transform.localPosition = new Vector3(8f, 9f, 10f);
                key.transform.localRotation = Quaternion.Euler(71f, 82f, 93f);
                for (int index = 0; index < bodies.Length; index++)
                {
                    bodies[index].transform.localPosition += new Vector3(
                        index + 1f,
                        index + 2f,
                        index + 3f
                    );
                    bodies[index].transform.localRotation =
                        Quaternion.Euler(
                            101f + index,
                            111f + index,
                            121f + index
                        );
                    bodies[index].isKinematic = false;
                    bodies[index].linearVelocity = new Vector3(
                        4f + index,
                        5f + index,
                        6f + index
                    );
                    bodies[index].angularVelocity = new Vector3(
                        7f + index,
                        8f + index,
                        9f + index
                    );
                }
            }

            public void AssertAuthoredComplete()
            {
                Assert.That(key.activeSelf, Is.True);
                Assert.That(
                    Vector3.Distance(
                        key.transform.localPosition,
                        authoredRootLocalPosition
                    ),
                    Is.LessThan(0.0001f)
                );
                Assert.That(
                    Quaternion.Angle(
                        key.transform.localRotation,
                        authoredRootLocalRotation
                    ),
                    Is.LessThan(0.0001f)
                );
                for (int index = 0; index < bodies.Length; index++)
                {
                    Assert.That(
                        Vector3.Distance(
                            bodies[index].transform.localPosition,
                            authoredBodyLocalPositions[index]
                        ),
                        Is.LessThan(0.0001f)
                    );
                    Assert.That(
                        Quaternion.Angle(
                            bodies[index].transform.localRotation,
                            authoredBodyLocalRotations[index]
                        ),
                        Is.LessThan(0.0001f)
                    );
                    Assert.That(
                        bodies[index].isKinematic,
                        Is.EqualTo(authoredBodyKinematic[index])
                    );
                    Assert.That(
                        bodies[index].useGravity,
                        Is.EqualTo(authoredBodyGravity[index])
                    );
                    Assert.That(
                        Vector3.Distance(
                            bodies[index].linearVelocity,
                            authoredLinearVelocities[index]
                        ),
                        Is.LessThan(0.0001f)
                    );
                    Assert.That(
                        Vector3.Distance(
                            bodies[index].angularVelocity,
                            authoredAngularVelocities[index]
                        ),
                        Is.LessThan(0.0001f)
                    );
                }
                Assert.That(collider.enabled, Is.True);
            }
        }

        private sealed class RuntimeFixture
        {
            public RuntimeFixture(
                GameObject root,
                Component coordinator,
                Component[] adapters,
                object plan)
            {
                Root = root;
                Coordinator = coordinator;
                Adapters = adapters;
                Plan = plan;
            }

            public GameObject Root { get; }
            public Component Coordinator { get; }
            public Component[] Adapters { get; }
            public object Plan { get; }
        }
    }

    public sealed class FakeSelectionInteractableView :
        MonoBehaviour,
        IInteractableView
    {
        private bool isSelected;

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }
                isSelected = value;
                if (isSelected)
                {
                    WhenSelectingInteractorViewAdded.Invoke(null);
                }
                else
                {
                    WhenSelectingInteractorViewRemoved.Invoke(null);
                }
            }
        }

        public object Data => null;

        public InteractableState State => default;

        public int MaxInteractors => 1;

        public int MaxSelectingInteractors => 1;

        public IEnumerable<IInteractorView> InteractorViews =>
            SelectingInteractorViews;

        public IEnumerable<IInteractorView> SelectingInteractorViews
        {
            get
            {
                if (IsSelected)
                {
                    yield return null;
                }
            }
        }

        public event Action<InteractableStateChangeArgs> WhenStateChanged =
            delegate { };

        public event Action<IInteractorView> WhenInteractorViewAdded =
            delegate { };

        public event Action<IInteractorView> WhenInteractorViewRemoved =
            delegate { };

        public event Action<IInteractorView> WhenSelectingInteractorViewAdded =
            delegate { };

        public event Action<IInteractorView>
            WhenSelectingInteractorViewRemoved = delegate { };
    }
}
