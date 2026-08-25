using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Deliberate hold gesture for the whole-Run safety action. Give Up remains
    /// a separate ordinary phase control.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class InteractionHoldToConfirm : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerExitHandler
    {
        [SerializeField]
        [Min(0.5f)]
        private float holdSeconds = 1.25f;

        [SerializeField]
        private TMP_Text label;

        [SerializeField]
        private string idleText = "按住中止本轮";

        [SerializeField]
        private string holdingText = "继续按住以中止…";

        private Button button;
        private bool holding;
        private bool confirmed;
        private float holdStartedAt;

        public event Action Confirmed;

        public bool IsHolding => holding;

        public void Configure(
            TMP_Text targetLabel,
            float requiredHoldSeconds = 1.25f)
        {
            label = targetLabel;
            holdSeconds = Mathf.Max(0.5f, requiredHoldSeconds);
            ResolveButton();
            ResetHold();
        }

        public void OnPointerDown(PointerEventData _)
        {
            ResolveButton();
            if (button == null || !button.interactable)
            {
                return;
            }

            holding = true;
            confirmed = false;
            holdStartedAt = Time.unscaledTime;
            SetLabel(holdingText);
        }

        public void OnPointerUp(PointerEventData _)
        {
            ResetHold();
        }

        public void OnPointerExit(PointerEventData _)
        {
            ResetHold();
        }

        private void Update()
        {
            if (!holding || confirmed ||
                Time.unscaledTime - holdStartedAt < holdSeconds)
            {
                return;
            }

            confirmed = true;
            holding = false;
            SetLabel(idleText);
            Confirmed?.Invoke();
        }

        private void ResolveButton()
        {
            button ??= GetComponent<Button>();
        }

        private void ResetHold()
        {
            holding = false;
            confirmed = false;
            SetLabel(idleText);
        }

        private void SetLabel(string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        private void OnDisable()
        {
            ResetHold();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            holdSeconds = Mathf.Max(0.5f, holdSeconds);
        }
#endif
    }
}
