using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SignVR.Interaction.Editor.Tests.Presentation
{
    public sealed class W5InstructionPresentationEditorTests
    {
        private const string SetupTypeName =
            "SignVR.Editor.Interaction.W5InstructionPresentationSetup, " +
            "Assembly-CSharp-Editor";

        [Test]
        public void W5SourceContractUsesFrozenCatalogConditionAndPlayerApi()
        {
            Invoke("ValidateSourceContractForAutomation");
        }

        [Test]
        public void FingerBindingFallsBackToMetaArmatureNamesWithoutHumanoidAvatar()
        {
            var root = new GameObject("MetaCharacter");
            try
            {
                Animator animator = root.AddComponent<Animator>();
                Transform leftDistal = CreateChild(
                    root.transform,
                    "Left_IndexDistal"
                );
                CreateChild(leftDistal, "Left_IndexDistalEnd");
                Transform rightDistal = CreateChild(
                    root.transform,
                    "Right_IndexDistal"
                );
                CreateChild(rightDistal, "Right_IndexDistalEnd");
                Type detectorType = Type.GetType(
                    "SignVR.Interaction.Presentation.GhostPointingDetector, " +
                    "Assembly-CSharp",
                    throwOnError: true
                );
                Component detector = root.AddComponent(detectorType);
                MethodInfo configure = detectorType.GetMethod(
                    "TryConfigureFingerBones",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: new[] { typeof(Transform) },
                    modifiers: null
                );
                PropertyInfo hasCompleteRig = detectorType.GetProperty(
                    "HasCompleteFingerRig",
                    BindingFlags.Public | BindingFlags.Instance
                );

                Assert.That(animator.isHuman, Is.False);
                Assert.That(configure, Is.Not.Null);
                Assert.That(hasCompleteRig, Is.Not.Null);
                Assert.That(
                    (bool)configure.Invoke(
                        detector,
                        new object[] { animator.transform }
                    ),
                    Is.True
                );
                Assert.That((bool)hasCompleteRig.GetValue(detector), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SavedInteractionLabHasCompleteW5AnchorWiring()
        {
            Invoke("ValidateConfiguredSceneForAutomation");
        }

        [Test]
        public void WorldSpaceCanvasRepairsRaycasterAndSupportsPokeAndRay()
        {
            Type pokeCanvasType = Type.GetType(
                "SignVR.SceneFlow.WorldSpacePokeCanvas, SignVR.SceneFlow",
                throwOnError: true
            );
            Type pokeInteractableType = Type.GetType(
                "Oculus.Interaction.PokeInteractable, Oculus.Interaction",
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
            var root = new GameObject(
                "PokeCanvasWithoutRaycaster",
                typeof(RectTransform),
                typeof(Canvas)
            );
            root.SetActive(false);
            try
            {
                Component pokeCanvas = root.AddComponent(pokeCanvasType);
                MethodInfo ensure = pokeCanvasType.GetMethod(
                    "EnsurePokeInteraction",
                    BindingFlags.Public | BindingFlags.Instance
                );

                Assert.That(ensure, Is.Not.Null);
                Assert.That(root.GetComponent<GraphicRaycaster>(), Is.Null,
                    "The regression precondition requires an old canvas " +
                    "without a serialized raycaster.");

                Assert.That((bool)ensure.Invoke(pokeCanvas, null), Is.True);
                Assert.That(root.GetComponent<GraphicRaycaster>(), Is.Not.Null,
                    "Runtime self-healing must precede PointableCanvas Start.");
                Transform interaction = root.transform.Find(
                    "ISDK_PokeCanvasInteraction"
                );
                Assert.That(interaction, Is.Not.Null);
                Assert.That(
                    interaction.GetComponent(pokeInteractableType),
                    Is.Not.Null,
                    "The canvas must remain directly pokeable."
                );
                Assert.That(
                    interaction.GetComponent(rayInteractableType),
                    Is.Not.Null,
                    "A panel beyond arm's reach must also accept hand rays."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (existingEventSystem == null && EventSystem.current != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        EventSystem.current.gameObject
                    );
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
                        UnityEngine.Object.DestroyImmediate(
                            createdCanvasModule
                        );
                    }
                }
            }
        }

        private static void Invoke(string methodName)
        {
            Type type = Type.GetType(SetupTypeName, throwOnError: true);
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(
                method,
                Is.Not.Null,
                $"Missing W5 validator entry point {methodName}."
            );
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

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }
    }
}
