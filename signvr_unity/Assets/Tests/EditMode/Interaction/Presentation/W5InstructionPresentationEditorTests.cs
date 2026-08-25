using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine;

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
