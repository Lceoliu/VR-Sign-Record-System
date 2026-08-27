using System;
using System.Linq;
using SignVR.Interaction;
using SignVR.SceneFlow;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SignVR.Editor.Interaction
{
    /// <summary>
    /// Idempotently installs InteractionLab's application-owned seated XR
    /// offset and its HMD-following naked-hand Poke button.
    /// </summary>
    internal static class InteractionSeatedMoveSetup
    {
        internal const string OffsetName = "InteractionSeatedRigOffset";
        internal const string CanvasName = "InteractionSeatedMoveCanvas";
        internal const string ButtonName = "MoveAlongView";
        internal const string ButtonLabel = "向视线方向移动 10 cm";

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

            Transform canvasTransform = FindDirectChild(hmd, CanvasName);
            GameObject canvasObject;
            if (canvasTransform == null)
            {
                canvasObject = new GameObject(CanvasName, typeof(RectTransform));
                canvasTransform = canvasObject.transform;
                canvasTransform.SetParent(hmd, false);
                changed = true;
            }
            else
            {
                canvasObject = canvasTransform.gameObject;
            }

            RectTransform canvasRect = RequireRect(canvasObject);
            changed |= SetLocalPose(
                canvasRect,
                new Vector3(0f, -0.28f, 0.42f),
                Quaternion.LookRotation(
                    new Vector3(0f, -0.28f, 0.42f).normalized,
                    Vector3.up
                ),
                Vector3.one * 0.001f
            );
            changed |= SetRect(canvasRect, new Vector2(560f, 140f));

            Canvas canvas = GetOrAdd<Canvas>(canvasObject, ref changed);
            CanvasScaler scaler = GetOrAdd<CanvasScaler>(canvasObject, ref changed);
            _ = GetOrAdd<GraphicRaycaster>(canvasObject, ref changed);
            WorldSpacePokeCanvas poke = GetOrAdd<WorldSpacePokeCanvas>(
                canvasObject,
                ref changed
            );
            if (canvas.renderMode != RenderMode.WorldSpace)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                changed = true;
            }
            if (canvas.sortingOrder != 29992)
            {
                canvas.sortingOrder = 29992;
                changed = true;
            }
            Camera hmdCamera = hmd.GetComponent<Camera>();
            if (canvas.worldCamera != hmdCamera)
            {
                canvas.worldCamera = hmdCamera;
                changed = true;
            }
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize ||
                !Mathf.Approximately(scaler.dynamicPixelsPerUnit, 100f))
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.dynamicPixelsPerUnit = 100f;
                changed = true;
            }
            if (poke.TargetCanvas != canvas)
            {
                poke.Configure(canvas);
                changed = true;
            }

            Transform buttonTransform = FindDirectChild(
                canvasTransform,
                ButtonName
            );
            GameObject buttonObject;
            if (buttonTransform == null)
            {
                buttonObject = new GameObject(ButtonName, typeof(RectTransform));
                buttonTransform = buttonObject.transform;
                buttonTransform.SetParent(canvasTransform, false);
                changed = true;
            }
            else
            {
                buttonObject = buttonTransform.gameObject;
            }
            RectTransform buttonRect = RequireRect(buttonObject);
            changed |= SetLocalPose(
                buttonRect,
                Vector3.zero,
                Quaternion.identity,
                Vector3.one
            );
            changed |= SetRect(buttonRect, new Vector2(520f, 110f));
            Image image = GetOrAdd<Image>(buttonObject, ref changed);
            Button button = GetOrAdd<Button>(buttonObject, ref changed);
            Color buttonColor = new(0.08f, 0.42f, 0.78f, 0.98f);
            if (((Vector4)image.color - (Vector4)buttonColor).sqrMagnitude >
                0.00000001f)
            {
                image.color = buttonColor;
                changed = true;
            }
            if (button.targetGraphic != image)
            {
                button.targetGraphic = image;
                changed = true;
            }

            changed |= EnsureLabel(buttonTransform);
            InteractionSeatedRigMover mover = GetOrAdd<
                InteractionSeatedRigMover>(offset.gameObject, ref changed);
            if (mover.Hmd != hmd || mover.MoveButton != button ||
                !Mathf.Approximately(
                    mover.StepDistance,
                    InteractionSeatedRigMover.DefaultStepDistance
                ))
            {
                mover.Configure(
                    hmd,
                    button,
                    InteractionSeatedRigMover.DefaultStepDistance
                );
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(offset.gameObject);
                EditorUtility.SetDirty(canvasObject);
            }
            return changed;
        }

        private static bool EnsureLabel(Transform button)
        {
            bool changed = false;
            Transform labelTransform = FindDirectChild(button, "Label");
            GameObject labelObject;
            if (labelTransform == null)
            {
                labelObject = new GameObject("Label", typeof(RectTransform));
                labelTransform = labelObject.transform;
                labelTransform.SetParent(button, false);
                changed = true;
            }
            else
            {
                labelObject = labelTransform.gameObject;
            }

            RectTransform rect = RequireRect(labelObject);
            Vector2 zero = Vector2.zero;
            Vector2 one = Vector2.one;
            if (rect.anchorMin != zero || rect.anchorMax != one ||
                rect.offsetMin != new Vector2(10f, 10f) ||
                rect.offsetMax != new Vector2(-10f, -10f))
            {
                rect.anchorMin = zero;
                rect.anchorMax = one;
                rect.offsetMin = new Vector2(10f, 10f);
                rect.offsetMax = new Vector2(-10f, -10f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                changed = true;
            }

            TextMeshProUGUI label = GetOrAdd<TextMeshProUGUI>(
                labelObject,
                ref changed
            );
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(
                "Fonts/SignVRChinese SDF"
            ) ?? TMP_Settings.defaultFontAsset;
            if (!string.Equals(label.text, ButtonLabel, StringComparison.Ordinal) ||
                label.font != font || !Mathf.Approximately(label.fontSize, 27f) ||
                label.alignment != TextAlignmentOptions.Center ||
                label.raycastTarget || label.richText)
            {
                label.text = ButtonLabel;
                label.font = font;
                label.fontSize = 27f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.raycastTarget = false;
                label.richText = false;
                changed = true;
            }
            return changed;
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

        private static RectTransform RequireRect(GameObject value)
        {
            return value.GetComponent<RectTransform>() ??
                throw new InvalidOperationException(
                    value.name + " must have a RectTransform."
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

        private static bool SetRect(RectTransform value, Vector2 size)
        {
            Vector2 center = new(0.5f, 0.5f);
            if (value.anchorMin == center && value.anchorMax == center &&
                value.pivot == center && value.sizeDelta == size)
            {
                return false;
            }
            value.anchorMin = center;
            value.anchorMax = center;
            value.pivot = center;
            value.sizeDelta = size;
            return true;
        }
    }
}
