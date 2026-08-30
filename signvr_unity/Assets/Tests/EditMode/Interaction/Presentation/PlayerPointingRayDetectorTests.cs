using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class PlayerPointingRayDetectorTests
    {
        private const BindingFlags InstancePrivate =
            BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void OcclusionIgnoresMatchingLogicalTargetButBlocksDoor()
        {
            GameObject root = new GameObject("PlayerPointingOcclusionTest");
            try
            {
                Type detectorType = RuntimeType(
                    "SignVR.Interaction.Presentation.PlayerPointingRayDetector"
                );
                var player = new GameObject("Player");
                player.transform.SetParent(root.transform, false);
                Component detector = player.AddComponent(detectorType);

                GameObject physicalTarget = GameObject.CreatePrimitive(
                    PrimitiveType.Cube
                );
                physicalTarget.name = "PhysicalTarget";
                physicalTarget.transform.SetParent(root.transform, false);
                physicalTarget.transform.position = new Vector3(0f, 0f, 1f);
                Component binding = physicalTarget.AddComponent(RuntimeType(
                    "SignVR.Interaction.PhaseAdapters.InteractionTargetBinding"
                ));
                binding.GetType().GetField("targetId", InstancePrivate)
                    .SetValue(binding, "button_a");

                GameObject proxy = new GameObject("ButtonProxy");
                proxy.transform.SetParent(root.transform, false);
                proxy.transform.position = new Vector3(0f, 0f, 3f);
                object geometry = CreateTargetGeometry(
                    detectorType,
                    "button_a",
                    proxy.transform
                );
                Physics.SyncTransforms();

                MethodInfo isOccluded = detectorType.GetMethod(
                    "IsRayOccluded",
                    InstancePrivate
                );
                var ray = new Ray(Vector3.zero, Vector3.forward);
                Assert.That(
                    isOccluded.Invoke(detector, new[] { geometry, ray, 3f }),
                    Is.False,
                    "A physical collider must not hide its same-ID proxy."
                );

                GameObject door = GameObject.CreatePrimitive(
                    PrimitiveType.Cube
                );
                door.name = "CabinetDoor";
                door.transform.SetParent(root.transform, false);
                door.transform.position = new Vector3(0f, 0f, 2f);
                Physics.SyncTransforms();

                Assert.That(
                    isOccluded.Invoke(detector, new[] { geometry, ray, 3f }),
                    Is.True,
                    "An unrelated cabinet door must block the button ray."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CaptureExclusionDoesNotHidePlayerOrTrackedHandHierarchy()
        {
            GameObject player = new GameObject("Player");
            try
            {
                GameObject hand = new GameObject("TrackedHandVisual");
                hand.transform.SetParent(player.transform, false);
                Type detectorType = RuntimeType(
                    "SignVR.Interaction.Presentation.PlayerPointingRayDetector"
                );
                Type highlightType = RuntimeType(
                    "SignVR.Interaction.Presentation.InteractionTargetHighlightVisual"
                );
                Component detector = player.AddComponent(detectorType);
                Component highlight = player.AddComponent(highlightType);
                detectorType.GetField("targetHighlight", InstancePrivate)
                    .SetValue(detector, highlight);

                detectorType.GetMethod("ConfigureCaptureExcludedLayer")
                    .Invoke(detector, new object[] { 31 });

                Assert.That(player.layer, Is.EqualTo(0));
                Assert.That(hand.layer, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void ThirtyConsecutiveHitsTriggerThreeSecondHighlight()
        {
            GameObject root = new GameObject("PlayerPointingHighlightTest");
            try
            {
                Type detectorType = RuntimeType(
                    "SignVR.Interaction.Presentation.PlayerPointingRayDetector"
                );
                Component detector = root.AddComponent(detectorType);
                MethodInfo observeHit = detectorType.GetMethod(
                    "ObserveConsecutiveHit",
                    InstancePrivate
                );
                MethodInfo activateHighlight = detectorType.GetMethod(
                    "ActivateHighlight",
                    InstancePrivate
                );
                MethodInfo expireHighlight = detectorType.GetMethod(
                    "ExpireHighlightIfDue",
                    InstancePrivate
                );
                PropertyInfo highlightActive = detectorType.GetProperty(
                    "IsHighlightActive",
                    BindingFlags.Instance | BindingFlags.Public
                );
                PropertyInfo candidateFrames = detectorType.GetProperty(
                    "ConsecutiveHitFrames",
                    BindingFlags.Instance | BindingFlags.Public
                );
                object geometry = CreateTargetGeometry(
                    detectorType,
                    "coin_a",
                    root.transform
                );

                for (int frame = 1; frame < 30; frame++)
                {
                    Assert.That(
                        observeHit.Invoke(
                            detector,
                            new object[] { "coin_a" }
                        ),
                        Is.False,
                        $"Frame {frame} must not highlight yet."
                    );
                }
                Assert.That(candidateFrames.GetValue(detector), Is.EqualTo(29));

                Assert.That(
                    observeHit.Invoke(
                        detector,
                        new object[] { "coin_a" }
                    ),
                    Is.True,
                    "The thirtieth consecutive hit must confirm the target."
                );
                activateHighlight.Invoke(
                    detector,
                    new object[] { geometry, 10d }
                );
                Assert.That(highlightActive.GetValue(detector), Is.True);

                expireHighlight.Invoke(detector, new object[] { 12.99d });
                Assert.That(
                    highlightActive.GetValue(detector),
                    Is.True,
                    "Highlight must remain visible before three seconds."
                );
                expireHighlight.Invoke(detector, new object[] { 13d });
                Assert.That(
                    highlightActive.GetValue(detector),
                    Is.False,
                    "Highlight must disappear after three seconds."
                );

                Assert.That(
                    observeHit.Invoke(
                        detector,
                        new object[] { "plate_a" }
                    ),
                    Is.False,
                    "A new target must restart the consecutive-frame count."
                );
                Assert.That(candidateFrames.GetValue(detector), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LostHitResetsConsecutiveFrameCount()
        {
            GameObject root = new GameObject("PlayerPointingHitResetTest");
            try
            {
                Type detectorType = RuntimeType(
                    "SignVR.Interaction.Presentation.PlayerPointingRayDetector"
                );
                Component detector = root.AddComponent(detectorType);
                MethodInfo observeHit = detectorType.GetMethod(
                    "ObserveConsecutiveHit",
                    InstancePrivate
                );
                MethodInfo resetHit = detectorType.GetMethod(
                    "ResetHitCandidate",
                    InstancePrivate
                );
                PropertyInfo candidateFrames = detectorType.GetProperty(
                    "ConsecutiveHitFrames",
                    BindingFlags.Instance | BindingFlags.Public
                );

                observeHit.Invoke(detector, new object[] { "coin_a" });
                observeHit.Invoke(detector, new object[] { "coin_a" });
                resetHit.Invoke(detector, null);
                Assert.That(candidateFrames.GetValue(detector), Is.EqualTo(0));
                Assert.That(
                    observeHit.Invoke(detector, new object[] { "coin_a" }),
                    Is.False,
                    "After a lost frame, the next hit starts a new sequence."
                );
                Assert.That(candidateFrames.GetValue(detector), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static object CreateTargetGeometry(
            Type detectorType,
            string targetId,
            Transform root)
        {
            Type geometryType = detectorType.GetNestedType(
                "TargetGeometry",
                BindingFlags.NonPublic
            );
            object geometry = Activator.CreateInstance(geometryType);
            geometryType.GetField("TargetId").SetValue(geometry, targetId);
            geometryType.GetField("Root").SetValue(geometry, root);
            geometryType.GetField("Renderers").SetValue(
                geometry,
                Array.Empty<Renderer>()
            );
            geometryType.GetField("Colliders").SetValue(
                geometry,
                Array.Empty<Collider>()
            );
            geometryType.GetField("HighlightRoots").SetValue(
                geometry,
                Array.Empty<Transform>()
            );
            return geometry;
        }

        private static Type RuntimeType(string fullName)
        {
            return Type.GetType(fullName + ", Assembly-CSharp", true);
        }
    }
}
