using System;
using System.Linq;
using SignVR.Interaction;
using SignVR.Interaction.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SignVR.Editor.Interaction
{
    /// <summary>
    /// Idempotently installs InteractionLab's application-owned seated XR
    /// offset and integrates movement into the shared auxiliary control panel.
    /// </summary>
    internal static class InteractionSeatedMoveSetup
    {
        internal const string OffsetName = "InteractionSeatedRigOffset";
        internal const string ButtonName = "MoveAlongView";
        internal const string ButtonLabel = "向视线方向移动 20 cm";
        internal const string LegacyCanvasName = "InteractionSeatedMoveCanvas";

        internal static bool Configure(Scene scene)
        {
            VRPlayerRig player = InteractionLabSceneTool
                .EnumerateGameObjects(scene)
                .Select(value => value.GetComponent<VRPlayerRig>())
                .Single(value => value != null);
            Transform xrOrigin = player.XROrigin;
            Transform hmd = player.Head;
            if (xrOrigin == null || hmd == null)
            {
                throw new InvalidOperationException(
                    "InteractionLab seated movement requires the canonical " +
                    "VRPlayerRig XR origin and HMD references."
                );
            }

            bool changed = false;
            Transform playerRoot = player.transform;
            Transform offset = FindDirectChild(playerRoot, OffsetName);
            if (offset == null)
            {
                var offsetObject = new GameObject(OffsetName);
                offset = offsetObject.transform;
                offset.SetParent(playerRoot, false);
                changed = true;
            }
            changed |= ResetLocalTransform(offset);

            if (xrOrigin.parent != offset)
            {
                xrOrigin.SetParent(offset, true);
                changed = true;
            }

            InteractionSeatedRigMover mover = GetOrAdd<
                InteractionSeatedRigMover>(offset.gameObject, ref changed);
            if (mover.Hmd != hmd || !Mathf.Approximately(
                    mover.StepDistance,
                    InteractionSeatedRigMover.DefaultStepDistance
                ))
            {
                mover.Configure(
                    hmd,
                    InteractionSeatedRigMover.DefaultStepDistance
                );
                changed = true;
            }

            InteractionInstructionControls[] controls =
                InteractionLabSceneTool.EnumerateGameObjects(scene)
                    .Select(value => value.GetComponent<
                        InteractionInstructionControls>())
                    .Where(value => value != null)
                    .ToArray();
            if (controls.Length > 1)
            {
                throw new InvalidOperationException(
                    "InteractionLab seated movement requires exactly one " +
                    "shared InteractionInstructionControls instance."
                );
            }
            InteractionInstructionControls sharedControls = controls.Length == 1
                ? controls[0]
                : GetOrAdd<InteractionInstructionControls>(
                    InteractionLabSceneTool.EnumerateGameObjects(scene)
                        .Single(value => string.Equals(
                            value.name,
                            "InteractionUiAnchor",
                            StringComparison.Ordinal
                        )),
                    ref changed
                );
            bool controlsChanged = sharedControls.SeatedRigMover != mover ||
                !HasCanonicalSharedLayout(sharedControls);
            sharedControls.ConfigureSeatedMovement(mover);
            changed |= controlsChanged;

            Transform legacyCanvas = FindDirectChild(hmd, LegacyCanvasName);
            if (legacyCanvas != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyCanvas.gameObject);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(offset.gameObject);
                EditorUtility.SetDirty(sharedControls);
            }
            return changed;
        }

        private static bool HasCanonicalSharedLayout(
            InteractionInstructionControls controls)
        {
            Button move = controls.MoveButton;
            Button replay = controls.ReplayButton;
            Button giveUp = controls.GiveUpButton;
            Button abort = controls.AbortButton;
            RectTransform root = move?.transform.parent as RectTransform;
            TMP_Text label = move?.GetComponentInChildren<TMP_Text>(true);
            return move != null && replay != null && giveUp != null &&
                abort != null && root != null &&
                string.Equals(root.name, "InstructionControlCanvas",
                    StringComparison.Ordinal) &&
                root.sizeDelta == new Vector2(960f, 130f) &&
                HasPosition(move, -360f) && HasPosition(replay, -120f) &&
                HasPosition(giveUp, 120f) && HasPosition(abort, 360f) &&
                move.GetComponent<InteractionRoundedRectangleGraphic>() != null &&
                label != null && string.Equals(
                    label.text,
                    ButtonLabel,
                    StringComparison.Ordinal
                );
        }

        private static bool HasPosition(Button button, float x)
        {
            RectTransform rect = button?.transform as RectTransform;
            return rect != null &&
                rect.anchoredPosition == new Vector2(x, 0f) &&
                rect.sizeDelta == new Vector2(220f, 92f);
        }

        private static T GetOrAdd<T>(GameObject value, ref bool changed)
            where T : Component
        {
            T component = value.GetComponent<T>();
            if (component != null)
            {
                return component;
            }
            changed = true;
            return value.AddComponent<T>();
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            return parent.Cast<Transform>().SingleOrDefault(
                child => string.Equals(child.name, name, StringComparison.Ordinal)
            );
        }

        private static bool ResetLocalTransform(Transform value)
        {
            return SetLocalPose(
                value,
                Vector3.zero,
                Quaternion.identity,
                Vector3.one
            );
        }

        private static bool SetLocalPose(
            Transform value,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale)
        {
            if ((value.localPosition - position).sqrMagnitude <= 0.00000001f &&
                Quaternion.Angle(value.localRotation, rotation) <= 0.001f &&
                (value.localScale - scale).sqrMagnitude <= 0.00000001f)
            {
                return false;
            }
            value.localPosition = position;
            value.localRotation = rotation;
            value.localScale = scale;
            return true;
        }

    }
}
