using System;
using TMPro;
using UnityEngine;

namespace SignVR.CoopRelay
{
    public enum CoopRelayPhase
    {
        CollectItems,
        AwaitConsoleConfirmation,
        AwaitStationReady,
        Complete
    }

    public enum CoopRelayStation
    {
        Left,
        Right
    }

    public enum CoopRelayAction
    {
        ConfirmConsole,
        ReadyLeft,
        ReadyRight,
        ResetRound
    }

    /// <summary>
    /// Local-only state machine for a two-role relay task. Networking and
    /// player ownership are intentionally outside this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopRelayGameManager : MonoBehaviour
    {
        [SerializeField]
        private CoopRelayItem[] items = Array.Empty<CoopRelayItem>();

        [SerializeField]
        private CoopRelaySocket[] sockets = Array.Empty<CoopRelaySocket>();

        [SerializeField]
        private TMP_Text statusLabel;

        [SerializeField]
        private bool soloDebugMode = true;

        [SerializeField, Min(1f)]
        private float readyWindowSeconds = 6f;

        [SerializeField, Min(1f)]
        private float soloDebugReadyWindowSeconds = 20f;

        [SerializeField]
        private float resetBelowHeight = -0.75f;

        private float readyWindowDeadline = -1f;

        public event Action<CoopRelayGameManager> StateChanged;

        public CoopRelayPhase Phase { get; private set; } =
            CoopRelayPhase.CollectItems;
        public int DockedCount { get; private set; }
        public int RequiredItemCount => sockets?.Length ?? 0;
        public bool LeftReady { get; private set; }
        public bool RightReady { get; private set; }
        public bool RoundComplete => Phase == CoopRelayPhase.Complete;
        public bool SoloDebugMode => soloDebugMode;
        public float StandardReadyWindowSeconds => readyWindowSeconds;
        public float EffectiveReadyWindowSeconds => soloDebugMode
            ? Mathf.Max(readyWindowSeconds, soloDebugReadyWindowSeconds)
            : readyWindowSeconds;
        public float ResetBelowHeight => resetBelowHeight;
        public int SynchronizationAttempts { get; private set; }
        public string StatusMessage { get; private set; } = string.Empty;
        public string ExpectedItemId
        {
            get
            {
                CoopRelaySocket expected = GetExpectedSocket();
                return expected != null
                    ? expected.AcceptedItemId
                    : string.Empty;
            }
        }
        public float ReadyTimeRemaining =>
            IsReadyWindowActive
                ? Mathf.Max(0f, readyWindowDeadline - Time.unscaledTime)
                : 0f;
        public bool IsReadyWindowActive =>
            Phase == CoopRelayPhase.AwaitStationReady &&
            (LeftReady || RightReady) &&
            readyWindowDeadline >= 0f;

        private void Awake()
        {
            BindSockets();
            RefreshStatus();
        }

        private void Start()
        {
            ResetRound();
        }

        private void Update()
        {
            RecoverFallenItems();

            if (
                IsReadyWindowActive &&
                Time.unscaledTime >= readyWindowDeadline
            )
            {
                ExpireReadyWindow();
            }
        }

        public void Configure(
            CoopRelayItem[] relayItems,
            CoopRelaySocket[] relaySockets,
            TMP_Text relayStatusLabel
        )
        {
            Configure(
                relayItems,
                relaySockets,
                relayStatusLabel,
                allowSoloDebug: true,
                standardReadyWindowSeconds: 6f,
                soloReadyWindowSeconds: 20f
            );
        }

        public void Configure(
            CoopRelayItem[] relayItems,
            CoopRelaySocket[] relaySockets,
            TMP_Text relayStatusLabel,
            bool allowSoloDebug,
            float standardReadyWindowSeconds,
            float soloReadyWindowSeconds
        )
        {
            items = relayItems ?? Array.Empty<CoopRelayItem>();
            sockets = relaySockets ?? Array.Empty<CoopRelaySocket>();
            statusLabel = relayStatusLabel;
            soloDebugMode = allowSoloDebug;
            readyWindowSeconds = Mathf.Max(1f, standardReadyWindowSeconds);
            soloDebugReadyWindowSeconds = Mathf.Max(
                readyWindowSeconds,
                soloReadyWindowSeconds
            );

            BindSockets();
            ClearRoundState();
            RefreshStatus();
        }

        public bool CanAcceptSocket(
            CoopRelaySocket socket,
            CoopRelayItem item
        )
        {
            if (
                Phase != CoopRelayPhase.CollectItems ||
                socket == null ||
                item == null ||
                item.IsDocked ||
                socket.IsOccupied
            )
            {
                return false;
            }

            CoopRelaySocket expected = GetExpectedSocket();
            return
                expected == socket &&
                string.Equals(
                    socket.AcceptedItemId,
                    item.ItemId,
                    StringComparison.Ordinal
                );
        }

        public void NotifyItemDocked(
            CoopRelayItem item,
            CoopRelaySocket socket
        )
        {
            if (
                Phase != CoopRelayPhase.CollectItems ||
                item == null ||
                socket == null ||
                socket.SequenceIndex != DockedCount ||
                !string.Equals(
                    socket.AcceptedItemId,
                    item.ItemId,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }

            DockedCount++;

            if (RequiredItemCount > 0 && DockedCount >= RequiredItemCount)
            {
                Phase = CoopRelayPhase.AwaitConsoleConfirmation;
            }

            PublishState();
        }

        public bool ConfirmConsole()
        {
            if (Phase != CoopRelayPhase.AwaitConsoleConfirmation)
            {
                return false;
            }

            Phase = CoopRelayPhase.AwaitStationReady;
            LeftReady = false;
            RightReady = false;
            readyWindowDeadline = -1f;
            PublishState();
            return true;
        }

        public bool ActivateStation(CoopRelayStation station)
        {
            if (Phase != CoopRelayPhase.AwaitStationReady)
            {
                return false;
            }

            if (
                IsReadyWindowActive &&
                Time.unscaledTime >= readyWindowDeadline
            )
            {
                ExpireReadyWindow(publish: false);
            }

            bool alreadyReady = station == CoopRelayStation.Left
                ? LeftReady
                : RightReady;

            if (alreadyReady)
            {
                return false;
            }

            if (!LeftReady && !RightReady)
            {
                readyWindowDeadline =
                    Time.unscaledTime + EffectiveReadyWindowSeconds;
                SynchronizationAttempts++;
            }

            if (station == CoopRelayStation.Left)
            {
                LeftReady = true;
            }
            else
            {
                RightReady = true;
            }

            if (LeftReady && RightReady)
            {
                readyWindowDeadline = -1f;
                Phase = CoopRelayPhase.Complete;
            }

            PublishState();
            return true;
        }

        public bool PressLeftReady()
        {
            return ActivateStation(CoopRelayStation.Left);
        }

        public bool PressRightReady()
        {
            return ActivateStation(CoopRelayStation.Right);
        }

        public bool PerformAction(CoopRelayAction action)
        {
            return action switch
            {
                CoopRelayAction.ConfirmConsole => ConfirmConsole(),
                CoopRelayAction.ReadyLeft => PressLeftReady(),
                CoopRelayAction.ReadyRight => PressRightReady(),
                CoopRelayAction.ResetRound => ResetRoundAndReport(),
                _ => false
            };
        }

        public bool CanPerformAction(CoopRelayAction action)
        {
            return action switch
            {
                CoopRelayAction.ConfirmConsole =>
                    Phase == CoopRelayPhase.AwaitConsoleConfirmation,
                CoopRelayAction.ReadyLeft =>
                    Phase == CoopRelayPhase.AwaitStationReady && !LeftReady,
                CoopRelayAction.ReadyRight =>
                    Phase == CoopRelayPhase.AwaitStationReady && !RightReady,
                CoopRelayAction.ResetRound => true,
                _ => false
            };
        }

        public string GetActionLabel(CoopRelayAction action)
        {
            return action switch
            {
                CoopRelayAction.ConfirmConsole => "CONFIRM LOAD",
                CoopRelayAction.ReadyLeft =>
                    LeftReady ? "LEFT READY" : "LEFT READY",
                CoopRelayAction.ReadyRight =>
                    RightReady ? "RIGHT READY" : "RIGHT READY",
                CoopRelayAction.ResetRound => "RESET ROUND",
                _ => string.Empty
            };
        }

        public void ResetRound()
        {
            foreach (CoopRelaySocket socket in sockets)
            {
                socket?.ResetSocket();
            }

            foreach (CoopRelayItem item in items)
            {
                item?.ResetToSpawn();
            }

            ClearRoundState();
            PublishState();
        }

        private bool ResetRoundAndReport()
        {
            ResetRound();
            return true;
        }

        private void ClearRoundState()
        {
            Phase = CoopRelayPhase.CollectItems;
            DockedCount = 0;
            LeftReady = false;
            RightReady = false;
            SynchronizationAttempts = 0;
            readyWindowDeadline = -1f;
        }

        private void ExpireReadyWindow(bool publish = true)
        {
            LeftReady = false;
            RightReady = false;
            readyWindowDeadline = -1f;

            if (publish)
            {
                PublishState();
            }
        }

        private CoopRelaySocket GetExpectedSocket()
        {
            if (
                Phase != CoopRelayPhase.CollectItems ||
                DockedCount < 0 ||
                DockedCount >= RequiredItemCount
            )
            {
                return null;
            }

            foreach (CoopRelaySocket socket in sockets)
            {
                if (socket != null && socket.SequenceIndex == DockedCount)
                {
                    return socket;
                }
            }

            return null;
        }

        private void RecoverFallenItems()
        {
            if (RoundComplete)
            {
                return;
            }

            foreach (CoopRelayItem item in items)
            {
                if (
                    item != null &&
                    !item.IsDocked &&
                    !item.IsGrabbed &&
                    item.transform.position.y < resetBelowHeight
                )
                {
                    item.ResetToSpawn();
                }
            }
        }

        private void BindSockets()
        {
            foreach (CoopRelaySocket socket in sockets)
            {
                socket?.SetGameManager(this);
            }
        }

        private void PublishState()
        {
            RefreshStatus();
            StateChanged?.Invoke(this);
        }

        private void RefreshStatus()
        {
            StatusMessage = BuildStatusMessage();

            if (statusLabel != null)
            {
                statusLabel.text = StatusMessage;
            }
        }

        private string BuildStatusMessage()
        {
            switch (Phase)
            {
                case CoopRelayPhase.CollectItems:
                    return
                        $"TEAM RELAY  {DockedCount} / {RequiredItemCount}\n" +
                        "A: LOAD NEXT CARGO  B: READ ORDER";

                case CoopRelayPhase.AwaitConsoleConfirmation:
                    return "LOAD SECURED\nOPERATOR: CONFIRM CONSOLE";

                case CoopRelayPhase.AwaitStationReady:
                    if (!LeftReady && !RightReady)
                    {
                        return "FINAL LIFT\nBOTH STATIONS: PRESS READY";
                    }

                    string waitingFor = LeftReady
                        ? "RIGHT STATION"
                        : "LEFT STATION";
                    return
                        $"{waitingFor}: PRESS READY\n" +
                        $"WINDOW: {Mathf.CeilToInt(ReadyTimeRemaining)}s";

                case CoopRelayPhase.Complete:
                    return "CO-OP LIFT COMPLETE\nSIDE PANEL: RESET OR SWITCH";

                default:
                    return string.Empty;
            }
        }
    }
}
