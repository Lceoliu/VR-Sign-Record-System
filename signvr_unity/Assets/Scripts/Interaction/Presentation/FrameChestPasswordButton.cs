using SignVR.Interaction.Core;
using SignVR.Interaction.PhaseAdapters;
using UnityEngine;

namespace SignVR.Interaction.Presentation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class FrameChestPasswordButton : MonoBehaviour,
        IInteractionTriggerInput,
        IInteractionContactCycleInput
    {
        [SerializeField] private string buttonId = string.Empty;
        [SerializeField] private FrameChestRecordingSequenceController controller;
        [SerializeField] private Transform movingVisual;
        [SerializeField] private Collider inputCollider;
        [SerializeField] private Renderer[] auxiliaryRenderers =
            System.Array.Empty<Renderer>();
        [SerializeField] private Vector3 pressedLocalOffset = new(0f, 0f, -0.012f);
        [SerializeField] private Color buttonColor = Color.white;
        [SerializeField, Min(0f)] private float sameTargetCooldown = 0.25f;

        private Vector3 idleLocalPosition;
        private bool accepted;
        private bool animating;
        private float animationTime;

        public string ButtonId => buttonId;
        public bool IsInputAvailable => enabled && gameObject.activeInHierarchy &&
            controller != null && controller.IsPasswordInputAvailable;

        public void Configure(
            string id,
            FrameChestRecordingSequenceController configuredController,
            Transform visual,
            Collider collider)
        {
            buttonId = id;
            controller = configuredController;
            movingVisual = visual != null ? visual : transform;
            inputCollider = collider != null ? collider : GetComponent<Collider>();
            idleLocalPosition = movingVisual.localPosition;
            ApplyButtonColor();
        }

        public void ConfigureColor(Color color)
        {
            buttonColor = color;
            ApplyButtonColor();
        }

        public void ConfigureAuxiliaryRenderers(params Renderer[] renderers)
        {
            auxiliaryRenderers = renderers ?? System.Array.Empty<Renderer>();
            ApplyButtonColor();
        }

        public void SetInputAvailable(bool available)
        {
            if (inputCollider != null)
            {
                inputCollider.enabled = available;
            }
            enabled = true;
            if (!available)
            {
                ResetVisual();
            }
        }

        public void ConfigureSameTargetCooldown(float seconds)
        {
            sameTargetCooldown = Mathf.Max(0f, seconds);
        }

        public ValidationResult BeginContactCycle() => AcceptInput();

        public void EndContactCycle() { }

        public ValidationResult AcceptInput()
        {
            if (!IsInputAvailable || accepted)
            {
                return null;
            }
            animating = true;
            animationTime = 0f;
            return controller.PressPasswordButton(buttonId);
        }

        public void ShowAccepted()
        {
            accepted = true;
            animating = true;
            animationTime = 0f;
        }

        public void ResetVisual()
        {
            accepted = false;
            animating = false;
            animationTime = 0f;
            if (movingVisual != null)
            {
                movingVisual.localPosition = idleLocalPosition;
            }
        }

        private void Update()
        {
            if (!animating || movingVisual == null)
            {
                return;
            }
            animationTime += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(animationTime / 0.1f);
            movingVisual.localPosition = Vector3.Lerp(
                idleLocalPosition,
                idleLocalPosition + pressedLocalOffset,
                progress
            );
            if (progress >= 1f)
            {
                animating = false;
            }
        }

        private void OnEnable()
        {
            ApplyButtonColor();
        }

        private void ApplyButtonColor()
        {
            Renderer renderer = movingVisual != null
                ? movingVisual.GetComponent<Renderer>()
                : null;
            if (renderer == null)
            {
                return;
            }
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", buttonColor);
            block.SetColor("_Color", buttonColor);
            renderer.SetPropertyBlock(block);
            for (int index = 0; index < auxiliaryRenderers.Length; index++)
            {
                Renderer auxiliary = auxiliaryRenderers[index];
                if (auxiliary == null)
                {
                    continue;
                }
                auxiliary.GetPropertyBlock(block);
                block.SetColor("_BaseColor", buttonColor);
                block.SetColor("_Color", buttonColor);
                auxiliary.SetPropertyBlock(block);
            }
        }
    }
}
