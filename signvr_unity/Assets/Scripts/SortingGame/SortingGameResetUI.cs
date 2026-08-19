using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SignVR.SortingGame
{
    /// <summary>
    /// Owns the explicit round reset action and makes its world-space button
    /// available to Meta hand poke interactors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SortingGameResetUI : MonoBehaviour
    {
        private const string PokeInteractionName =
            "ISDK_PokeCanvasInteraction";

        [SerializeField]
        private SortingGameManager manager;

        [SerializeField]
        private Button resetButton;

        private bool listenerAttached;

        public SortingGameManager Manager => manager;
        public Button ResetButton => resetButton;

        public void Configure(
            SortingGameManager roundManager,
            Button button
        )
        {
            manager = roundManager;
            resetButton = button;

            if (Application.isPlaying)
            {
                EnsurePokeInteraction();
                AttachButtonListener();
            }
        }

        private void Awake()
        {
            if (manager == null)
            {
                manager = FindAnyObjectByType<SortingGameManager>();
            }

            if (resetButton == null)
            {
                resetButton = GetComponentInChildren<Button>(true);
            }

            EnsurePokeInteraction();
            AttachButtonListener();
        }

        public void ResetRound()
        {
            manager?.ResetRound();
        }

        private void AttachButtonListener()
        {
            if (listenerAttached || resetButton == null)
            {
                return;
            }

            resetButton.onClick.AddListener(ResetRound);
            listenerAttached = true;
        }

        private void EnsurePokeInteraction()
        {
            Canvas canvas = GetComponent<Canvas>();
            RectTransform canvasRect = transform as RectTransform;

            if (canvas == null || canvasRect == null)
            {
                Debug.LogError(
                    "[SortingGameResetUI] A world-space Canvas is required.",
                    this
                );
                return;
            }

            canvas.renderMode = RenderMode.WorldSpace;

            PointableCanvasModule canvasModule =
                FindAnyObjectByType<PointableCanvasModule>();

            if (canvasModule == null)
            {
                EventSystem eventSystem =
                    FindAnyObjectByType<EventSystem>();

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

            if (transform.Find(PokeInteractionName) != null)
            {
                return;
            }

            GameObject interactionObject =
                new GameObject(PokeInteractionName, typeof(RectTransform));
            interactionObject.SetActive(false);
            interactionObject.layer = gameObject.layer;

            RectTransform interactionRect =
                interactionObject.GetComponent<RectTransform>();
            interactionRect.SetParent(transform, false);
            StretchToParent(interactionRect);

            PointableCanvas pointableCanvas =
                interactionObject.AddComponent<PointableCanvas>();
            pointableCanvas.InjectCanvas(canvas);

            GameObject surfaceObject =
                new GameObject("Surface", typeof(RectTransform));
            surfaceObject.layer = gameObject.layer;

            RectTransform surfaceRect =
                surfaceObject.GetComponent<RectTransform>();
            surfaceRect.SetParent(interactionRect, false);
            StretchToParent(surfaceRect);

            PlaneSurface planeSurface = surfaceObject.AddComponent<PlaneSurface>();
            planeSurface.Facing = PlaneSurface.NormalFacing.Backward;

            BoundsClipper boundsClipper =
                surfaceObject.AddComponent<BoundsClipper>();
            boundsClipper.Size = new Vector3(
                canvasRect.rect.width,
                canvasRect.rect.height,
                0.01f
            );

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

            interactionObject.SetActive(true);
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

        private void OnDestroy()
        {
            if (listenerAttached && resetButton != null)
            {
                resetButton.onClick.RemoveListener(ResetRound);
            }

            listenerAttached = false;
        }
    }
}
