using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Interaction.Editor.Tests
{
    public sealed class DualBuildAndInteractionLabTests
    {
        private const string ValidatorTypeName =
            "SignVR.Editor.Interaction.InteractionLabValidator, " +
            "Assembly-CSharp-Editor";

        private const string SignSequenceSetupTypeName =
            "SignVR.Editor.Interaction.InteractionSignSequenceTestSceneSetup, " +
            "Assembly-CSharp-Editor";

        [Test]
        public void BuildEntriesUseDistinctIdentityAndOneExplicitScene()
        {
            InvokeValidator("ValidateBuildContractForAutomation");
        }

        [Test]
        public void GeneratedInteractionLabPassesCleanSceneContract()
        {
            InvokeValidator("ValidateSceneForAutomation");
        }

        [Test]
        public void SignSequenceSceneEnablesFormalStudyFlowOnEntry()
        {
            InvokeStatic(
                SignSequenceSetupTypeName,
                "ValidateSceneForAutomation"
            );
        }

        [Test]
        public void SeatedMoverUsesTheCompleteThreeDimensionalHmdForward()
        {
            Type moverType = Type.GetType(
                "SignVR.Interaction.InteractionSeatedRigMover, Assembly-CSharp",
                throwOnError: true
            );
            Assert.That(
                (float)moverType.GetField("DefaultStepDistance")
                    ?.GetRawConstantValue(),
                Is.EqualTo(0.2f)
            );
            var player = new GameObject("VRPlayer");
            var offset = new GameObject("InteractionSeatedRigOffset");
            var xrOrigin = new GameObject("OVRCameraRig");
            var hmd = new GameObject("CenterEyeAnchor");
            var staticTarget = new GameObject("StaticTaskTarget");
            var controlsObject = new GameObject("InteractionUiAnchor");
            try
            {
                offset.transform.SetParent(player.transform, false);
                xrOrigin.transform.SetParent(offset.transform, false);
                hmd.transform.SetParent(xrOrigin.transform, false);
                Component mover = offset.AddComponent(moverType);
                Type controlsType = Type.GetType(
                    "SignVR.Interaction.Presentation." +
                    "InteractionInstructionControls, Assembly-CSharp",
                    throwOnError: true
                );
                Component controls = controlsObject.AddComponent(controlsType);
                Vector3 forward = new Vector3(-2f, 3f, -6f).normalized;
                hmd.transform.rotation = Quaternion.LookRotation(
                    forward,
                    Vector3.up
                );
                Vector3 playerBefore = player.transform.position;
                Vector3 staticTargetBefore = new Vector3(4f, 2f, -3f);
                staticTarget.transform.position = staticTargetBefore;
                Vector3 expected = offset.transform.position + forward * 0.2f;

                moverType.GetMethod("Configure")?.Invoke(
                    mover,
                    new object[] { hmd.transform, 0.2f }
                );
                controlsType.GetMethod("ConfigureSeatedMovement")?.Invoke(
                    controls,
                    new object[] { mover }
                );
                Button button = controlsType.GetProperty("MoveButton")
                    ?.GetValue(controls) as Button;
                Button replay = controlsType.GetProperty("ReplayButton")
                    ?.GetValue(controls) as Button;
                Assert.That(button, Is.Not.Null);
                Assert.That(replay, Is.Not.Null);
                Assert.That(button.transform.parent, Is.EqualTo(
                    replay.transform.parent
                ));
                Assert.That(button.targetGraphic.GetType(), Is.EqualTo(
                    replay.targetGraphic.GetType()
                ));
                Assert.That(
                    button.GetComponentInParent<GraphicRaycaster>(),
                    Is.Not.Null
                );
                button.onClick.Invoke();

                Assert.That(offset.transform.position.x, Is.EqualTo(expected.x)
                    .Within(0.000001f));
                Assert.That(offset.transform.position.y, Is.EqualTo(expected.y)
                    .Within(0.000001f));
                Assert.That(offset.transform.position.z, Is.EqualTo(expected.z)
                    .Within(0.000001f));
                Assert.That(player.transform.position, Is.EqualTo(playerBefore));
                Assert.That(xrOrigin.transform.position,
                    Is.EqualTo(expected));
                Assert.That(staticTarget.transform.position,
                    Is.EqualTo(staticTargetBefore));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controlsObject);
                UnityEngine.Object.DestroyImmediate(staticTarget);
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void SignSequenceMoveUsesFullViewAtTwentyCentimetersWithoutCooldown()
        {
            Type moverType = Type.GetType(
                "SignVR.Interaction.InteractionSeatedRigMover, Assembly-CSharp",
                throwOnError: true
            );
            Type sourceControlsType = Type.GetType(
                "SignVR.Interaction.Presentation." +
                "InteractionInstructionControls, Assembly-CSharp",
                throwOnError: true
            );
            Type sequenceControlsType = Type.GetType(
                "SignVR.Interaction.Presentation." +
                "InteractionSignSequenceTestControls, Assembly-CSharp",
                throwOnError: true
            );
            Type sequenceControlsBaseType = sequenceControlsType.BaseType;

            var root = new GameObject("SignSequenceMoveRegression");
            var offset = new GameObject("InteractionSeatedRigOffset");
            var hmd = new GameObject("CenterEyeAnchor");
            var sourceObject = new GameObject("InteractionUiAnchor");
            var sequenceObject = new GameObject("SignSequenceTestHarness");
            try
            {
                offset.transform.SetParent(root.transform, false);
                hmd.transform.SetParent(offset.transform, false);
                sourceObject.transform.SetParent(root.transform, false);
                sequenceObject.transform.SetParent(sourceObject.transform, false);

                Component mover = offset.AddComponent(moverType);
                Component sourceControls = sourceObject.AddComponent(
                    sourceControlsType
                );
                Component sequenceControls = sequenceObject.AddComponent(
                    sequenceControlsType
                );
                Vector3 forward = new Vector3(-2f, 3f, -6f).normalized;
                hmd.transform.rotation = Quaternion.LookRotation(
                    forward,
                    Vector3.up
                );
                moverType.GetMethod("Configure")?.Invoke(
                    mover,
                    new object[] { hmd.transform, 0.2f }
                );
                sourceControlsType.GetMethod("ConfigureSeatedMovement")?.Invoke(
                    sourceControls,
                    new object[] { mover }
                );
                sequenceControlsBaseType.GetField(
                    "sourceControls",
                    BindingFlags.Instance | BindingFlags.NonPublic
                )?.SetValue(sequenceControls, sourceControls);
                sequenceControlsBaseType.GetMethod(
                    "EnsureVisualsForValidation",
                    BindingFlags.Instance | BindingFlags.Public
                )?.Invoke(sequenceControls, null);

                Button moveButton = sequenceControlsBaseType.GetProperty(
                    "MoveButton",
                    BindingFlags.Instance | BindingFlags.Public
                )?.GetValue(sequenceControls) as Button;
                Assert.That(moveButton, Is.Not.Null);

                Vector3 expected = offset.transform.position +
                    forward * 0.2f;
                MethodInfo handleMove = sequenceControlsBaseType.GetMethod(
                    "HandleMove",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.That(handleMove, Is.Not.Null);
                handleMove.Invoke(sequenceControls, null);

                Assert.That(offset.transform.position.x, Is.EqualTo(expected.x)
                    .Within(0.000001f));
                Assert.That(offset.transform.position.y, Is.EqualTo(expected.y)
                    .Within(0.000001f));
                Assert.That(offset.transform.position.z, Is.EqualTo(expected.z)
                    .Within(0.000001f));
                Assert.That(moveButton.gameObject.activeSelf, Is.True,
                    "Moving must keep the menu open for repeated clicks.");

                handleMove.Invoke(sequenceControls, null);
                Vector3 repeatedExpected = expected + forward * 0.2f;
                Assert.That(offset.transform.position.x,
                    Is.EqualTo(repeatedExpected.x).Within(0.000001f));
                Assert.That(offset.transform.position.y,
                    Is.EqualTo(repeatedExpected.y).Within(0.000001f));
                Assert.That(offset.transform.position.z,
                    Is.EqualTo(repeatedExpected.z).Within(0.000001f));

                MethodInfo handleMenuToggle =
                    sequenceControlsBaseType.GetMethod(
                        "HandleMenuToggle",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    );
                Assert.That(handleMenuToggle, Is.Not.Null);
                handleMenuToggle.Invoke(sequenceControls, null);
                Assert.That(moveButton.gameObject.activeSelf, Is.False,
                    "Only the explicit menu toggle may hide commands.");
                handleMenuToggle.Invoke(sequenceControls, null);
                Assert.That(moveButton.gameObject.activeSelf, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void InvokeValidator(string methodName)
        {
            InvokeStatic(ValidatorTypeName, methodName);
        }

        private static void InvokeStatic(
            string typeName,
            string methodName)
        {
            Type validator = Type.GetType(typeName, throwOnError: true);
            MethodInfo method = validator.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(
                method,
                Is.Not.Null,
                $"Missing validator entry point {methodName}."
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
    }
}
