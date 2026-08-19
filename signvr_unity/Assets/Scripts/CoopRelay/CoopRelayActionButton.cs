using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.CoopRelay
{
    /// <summary>
    /// Connects one world-space UGUI button to a relay state-machine action.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopRelayActionButton : MonoBehaviour
    {
        [SerializeField]
        private CoopRelayGameManager manager;

        [SerializeField]
        private CoopRelayAction action;

        [SerializeField]
        private Button actionButton;

        [SerializeField]
        private TMP_Text buttonLabel;

        [SerializeField]
        private string labelOverride = string.Empty;

        private bool listenerAttached;
        private bool managerSubscribed;

        public CoopRelayGameManager Manager => manager;
        public CoopRelayAction Action => action;
        public Button ActionButton => actionButton;

        private void Awake()
        {
            if (manager == null)
            {
                manager = FindAnyObjectByType<CoopRelayGameManager>();
            }

            if (actionButton == null)
            {
                actionButton = GetComponent<Button>();
            }

            if (buttonLabel == null)
            {
                buttonLabel = GetComponentInChildren<TMP_Text>(true);
            }

            Attach();
            Refresh();
        }

        public void Configure(
            CoopRelayGameManager relayManager,
            CoopRelayAction relayAction,
            Button button,
            TMP_Text label = null,
            string customLabel = ""
        )
        {
            Detach();

            manager = relayManager;
            action = relayAction;
            actionButton = button;
            buttonLabel = label;
            labelOverride = customLabel ?? string.Empty;

            if (Application.isPlaying)
            {
                Attach();
            }

            Refresh();
        }

        public bool InvokeAction()
        {
            if (manager == null)
            {
                return false;
            }

            bool performed = manager.PerformAction(action);
            Refresh();
            return performed;
        }

        public void Refresh()
        {
            if (actionButton != null)
            {
                actionButton.interactable =
                    manager != null && manager.CanPerformAction(action);
            }

            if (buttonLabel != null)
            {
                buttonLabel.text = string.IsNullOrWhiteSpace(labelOverride)
                    ? manager?.GetActionLabel(action) ?? string.Empty
                    : labelOverride;
            }
        }

        private void Attach()
        {
            if (!listenerAttached && actionButton != null)
            {
                actionButton.onClick.AddListener(OnButtonClicked);
                listenerAttached = true;
            }

            if (!managerSubscribed && manager != null)
            {
                manager.StateChanged += OnManagerStateChanged;
                managerSubscribed = true;
            }
        }

        private void Detach()
        {
            if (listenerAttached && actionButton != null)
            {
                actionButton.onClick.RemoveListener(OnButtonClicked);
            }

            if (managerSubscribed && manager != null)
            {
                manager.StateChanged -= OnManagerStateChanged;
            }

            listenerAttached = false;
            managerSubscribed = false;
        }

        private void OnButtonClicked()
        {
            InvokeAction();
        }

        private void OnManagerStateChanged(CoopRelayGameManager relayManager)
        {
            Refresh();
        }

        private void OnDestroy()
        {
            Detach();
        }
    }
}
