using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SignVR.SceneFlow
{
    /// <summary>
    /// Makes a world-space UGUI canvas touchable by Meta hand poke
    /// interactors. The helper is reusable by gameplay and scene menus.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class WorldSpacePokeCanvas : MonoBehaviour
    {
        private const string InteractionObjectName =
            "ISDK_PokeCanvasInteraction";

        // Keep the poke plane aligned with actual buttons instead of the
        // often much larger backing canvas. Values are canvas-local pixels.
        private const float InteractivePaddingPixels = 16f;

        [SerializeField]
        private Canvas targetCanvas;

        public Canvas TargetCanvas => targetCanvas;
        public bool IsConfigured =>
            transform.Find(InteractionObjectName) != null;

        private void Awake()
        {
            EnsurePokeInteraction();
        }

        public void Configure(Canvas canvas)
        {
            targetCanvas = canvas != null ? canvas : GetComponent<Canvas>();

            if (Application.isPlaying)
            {
                EnsurePokeInteraction();
            }
        }

        [ContextMenu("Ensure Meta Poke Interaction")]
        public bool EnsurePokeInteraction()
        {
            if (targetCanvas == null)
            {
                targetCanvas = GetComponent<Canvas>();
            }

            RectTransform canvasRect = transform as RectTransform;

            if (targetCanvas == null || canvasRect == null)
            {
                Debug.LogError(
                    "[WorldSpacePokeCanvas] A Canvas and RectTransform are required.",
                    this
                );
                return false;
            }

            targetCanvas.renderMode = RenderMode.WorldSpace;
            EnsureGraphicRaycaster(targetCanvas);
            EnsurePointableCanvasModule();

            if (IsConfigured)
            {
                Transform existingInteraction =
                    transform.Find(InteractionObjectName);
                BoundsClipper existingClipper = existingInteraction
                    ?.GetComponentInChildren<BoundsClipper>(true);
                ClippedPlaneSurface existingSurface = existingInteraction
                    ?.GetComponentInChildren<ClippedPlaneSurface>(true);
                PointableCanvas existingPointable = existingInteraction
                    ?.GetComponent<PointableCanvas>();
                if (existingClipper == null || existingSurface == null ||
                    existingPointable == null)
                {
                    Debug.LogError(
                        "[WorldSpacePokeCanvas] Existing interaction is " +
                        "missing its pointable canvas or clipped surface.",
                        this
                    );
                    return false;
                }

                SetInteractiveBounds(canvasRect, existingClipper);
                EnsureRayInteraction(
                    existingInteraction.gameObject,
                    existingSurface,
                    existingPointable
                );
                return true;
            }

            GameObject interactionObject = new GameObject(
                InteractionObjectName,
                typeof(RectTransform)
            );
            interactionObject.SetActive(false);
            interactionObject.layer = gameObject.layer;

            RectTransform interactionRect =
                interactionObject.GetComponent<RectTransform>();
            interactionRect.SetParent(transform, false);
            StretchToParent(interactionRect);

            PointableCanvas pointableCanvas =
                interactionObject.AddComponent<PointableCanvas>();
            pointableCanvas.InjectCanvas(targetCanvas);

            GameObject surfaceObject = new GameObject(
                "Surface",
                typeof(RectTransform)
            );
            surfaceObject.layer = gameObject.layer;

            RectTransform surfaceRect =
                surfaceObject.GetComponent<RectTransform>();
            surfaceRect.SetParent(interactionRect, false);
            StretchToParent(surfaceRect);

            PlaneSurface planeSurface =
                surfaceObject.AddComponent<PlaneSurface>();
            planeSurface.Facing = PlaneSurface.NormalFacing.Backward;

            BoundsClipper boundsClipper =
                surfaceObject.AddComponent<BoundsClipper>();
            SetInteractiveBounds(canvasRect, boundsClipper);

            ClippedPlaneSurface clippedSurface =
                surfaceObject.AddComponent<ClippedPlaneSurface>();
            clippedSurface.InjectAllClippedPlaneSurface(
                planeSurface,
                new[] { boundsClipper }
            );

            PokeInteractable pokeInteractable =
                interactionObject.AddComponent<PokeInteractable>();
            pokeInteractable.InjectAllPokeInteractable(clippedSurface);
            pokeInteractable.InjectOptionalPointableElement(pointableCanvas);

            EnsureRayInteraction(
                interactionObject,
                clippedSurface,
                pointableCanvas
            );

            interactionObject.SetActive(true);
            return true;
        }

        private static void EnsureRayInteraction(
            GameObject interactionObject,
            ClippedPlaneSurface clippedSurface,
            PointableCanvas pointableCanvas)
        {
            RayInteractable rayInteractable =
                interactionObject.GetComponent<RayInteractable>() ??
                interactionObject.AddComponent<RayInteractable>();
            rayInteractable.InjectAllRayInteractable(clippedSurface);
            rayInteractable.InjectOptionalPointableElement(pointableCanvas);
        }

        private static void EnsureGraphicRaycaster(Canvas canvas)
        {
            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private static void SetInteractiveBounds(
            RectTransform canvasRect,
            BoundsClipper boundsClipper
        )
        {
            Bounds bounds = default;
            bool foundButton = false;

            foreach (Selectable selectable in
                canvasRect.GetComponentsInChildren<Selectable>(true))
            {
                RectTransform selectableRect =
                    selectable.transform as RectTransform;

                if (selectableRect == null)
                {
                    continue;
                }

                Bounds candidate =
                    RectTransformUtility.CalculateRelativeRectTransformBounds(
                        canvasRect,
                        selectableRect
                    );

                if (!foundButton)
                {
                    bounds = candidate;
                    foundButton = true;
                }
                else
                {
                    bounds.Encapsulate(candidate);
                }
            }

            if (!foundButton)
            {
                bounds = new Bounds(
                    canvasRect.rect.center,
                    canvasRect.rect.size
                );
            }

            Vector3 padding = new Vector3(
                InteractivePaddingPixels,
                InteractivePaddingPixels,
                0.01f
            );
            bounds.Expand(padding);
            boundsClipper.Position = bounds.center;
            boundsClipper.Size = new Vector3(
                Mathf.Max(1f, bounds.size.x),
                Mathf.Max(1f, bounds.size.y),
                0.01f
            );
        }

        private static void EnsurePointableCanvasModule()
        {
            PointableCanvasModule canvasModule =
                FindAnyObjectByType<PointableCanvasModule>();

            if (canvasModule != null)
            {
                return;
            }

            EventSystem eventSystem = FindAnyObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                GameObject eventSystemObject =
                    new GameObject("PointableCanvasEventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            canvasModule =
                eventSystem.gameObject.AddComponent<PointableCanvasModule>();
            canvasModule.ExclusiveMode = true;
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
        }
    }
}
