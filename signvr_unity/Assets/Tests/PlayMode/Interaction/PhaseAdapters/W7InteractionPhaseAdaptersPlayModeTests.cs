using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
            Assert.That(safeHint.gameObject.activeSelf, Is.True);

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
            Assert.That(safeHint.gameObject.activeSelf, Is.True);

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
            AssertAccepted(InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                1,
                InvokeCoreFactory("PhaseInput", "Digit", 1)
            ));
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
            InvokePublic(phaseTwo, "Enable");

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
            InvokePublic(phaseTwo, "Enable");

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
                Component publisherA = typeName ==
                    "InteractionPlacementBinding" ? phaseTwo : phaseOne;
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
            Assert.That(safeHint.gameObject.activeSelf, Is.True);
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
            AssertSubscriptionCount(hints, passwordHintBefore + 5);
            AssertSubscriptionCount(feedback, passwordFeedbackBefore + 5);
            AssertSubscriptionCount(
                presentation,
                passwordPresentationBefore + 5
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
            Assert.That(safeHint.gameObject.activeSelf, Is.True);
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
            Assert.That(safeHint.gameObject.activeSelf, Is.True);
            AssertColor(
                ReadBaseColor(feedbackRenderer),
                new Color(0.15f, 1f, 0.35f, 1f)
            );
        }

        [UnityTest]
        public IEnumerator HintPresenterRebuildsAuthorityAcrossLifecycle()
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
            Assert.That(safeA.gameObject.activeSelf, Is.True);
            ((Behaviour)presenter).enabled = false;
            Assert.That(safeA.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = true;
            Assert.That(
                safeA.gameObject.activeSelf,
                Is.True,
                "Re-enable must rebuild the revealed password from authority."
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
            Assert.That(safeB.gameObject.activeSelf, Is.True);

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
                Is.True,
                "A recreated presenter must rebuild from the W7 snapshot."
            );

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
            Assert.That(chestB.gameObject.activeSelf, Is.True);
            ((Behaviour)presenter).enabled = false;
            Assert.That(chestB.gameObject.activeSelf, Is.False);
            ((Behaviour)presenter).enabled = true;
            Assert.That(chestB.gameObject.activeSelf, Is.True);

            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(4)
            );
            foreach (string targetId in
                     new[] { "blue", "red", "yellow", "green" })
            {
                AssertAccepted(InvokePublic(
                    fixture.Coordinator,
                    "AcceptInput",
                    4,
                    CreateTargetInput(targetId)
                ));
            }
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
            Assert.That(chestB.gameObject.activeSelf, Is.True);
            ((Behaviour)presenter).enabled = false;
            ((Behaviour)presenter).enabled = true;
            Assert.That(chestB.gameObject.activeSelf, Is.True);

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
            Assert.That(safeA.gameObject.activeSelf, Is.True);
            TargetInvocationException hintFailure =
                Assert.Throws<TargetInvocationException>(() => InvokePublic(
                    hints,
                    "Configure",
                    fixtureB.Coordinator,
                    null,
                    chestB
                ));
            Assert.That(
                hintFailure.InnerException,
                Is.TypeOf<ArgumentNullException>()
            );
            Assert.That(safeA.gameObject.activeSelf, Is.True);
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
            Assert.That(safeA.gameObject.activeSelf, Is.True);
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
            Assert.That(safeB.gameObject.activeSelf, Is.True);
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
            AssertAccepted(InvokePublic(
                fixtureA.Coordinator,
                "AcceptInput",
                1,
                InvokeCoreFactory("PhaseInput", "Digit", 1)
            ));
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
            AssertAccepted(InvokePublic(
                fixtureB.Coordinator,
                "AcceptInput",
                1,
                InvokeCoreFactory("PhaseInput", "Digit", 1)
            ));
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
            AssertAccepted(InvokePublic(
                fixtureA.Coordinator,
                "AcceptInput",
                1,
                InvokeCoreFactory("PhaseInput", "Digit", 1)
            ));
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
            AssertAccepted(InvokePublic(
                fixtureB.Coordinator,
                "AcceptInput",
                1,
                InvokeCoreFactory("PhaseInput", "Digit", 1)
            ));
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

            UnityEngine.Object.DestroyImmediate(fixtureA.Coordinator);
            Assert.That(
                fixtureA.Coordinator == null,
                Is.True,
                "The publisher-A coordinator was not destroyed."
            );
            InvokePublic(
                adapter,
                "Configure",
                fixtureB.Coordinator,
                fixtureB.Plan
            );
            InvokePublic(adapter, "Enable");
            AssertAccepted(AcceptTarget(fixtureB, "box_stool"));
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
            InvokePublic(adapter, "Enable");
            AssertAccepted(InvokePublic(
                fixtureB.Coordinator,
                "AcceptInput",
                1,
                InvokeCoreFactory("PhaseInput", "Digit", 1)
            ));
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
            InvokePublic(fixture.Coordinator, "Enable");
            InvokePublic(
                fixture.Coordinator,
                "Synchronize",
                CreatePhaseSnapshot(1)
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
            for (int digit = 1; digit <= 4; digit++)
            {
                AssertAccepted(InvokePublic(
                    fixture.Coordinator,
                    "AcceptInput",
                    1,
                    InvokeCoreFactory("PhaseInput", "Digit", digit)
                ));
            }
            AssertAccepted(InvokePublic(
                fixture.Coordinator,
                "AcceptInput",
                1,
                InvokeCoreFactory("PhaseInput", "Submit")
            ));
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
            EventInfo eventInfo = publisher.GetType().GetEvent(eventName);
            Assert.That(eventInfo, Is.Not.Null);
            Type argumentType = eventInfo.EventHandlerType
                .GetGenericArguments()[0];
            MethodInfo observer = typeof(EventCounter).GetMethod(
                nameof(EventCounter.Observe)
            ).MakeGenericMethod(argumentType);
            Delegate handler = Delegate.CreateDelegate(
                eventInfo.EventHandlerType,
                counter,
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
                    0
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
                Assert.That(key.activeSelf, Is.True);
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
}
