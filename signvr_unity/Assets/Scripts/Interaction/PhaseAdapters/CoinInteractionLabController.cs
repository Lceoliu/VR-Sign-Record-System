using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    public sealed class CoinInteractionLabController : MonoBehaviour
    {
        private static readonly string[] CoinIds =
            { "coin_dragon", "coin_a", "coin_b" };

        private static readonly string[] PlateIds =
            { "plate_dragon", "plate_a", "plate_b" };

        private static readonly string[] CoinLabels =
            { "龙纹金币", "金币 A", "金币 B" };

        private static readonly string[] PlateLabels =
            { "龙纹盘", "盘子 A", "盘子 B" };

        [SerializeField]
        private InteractionPhaseCoordinator coordinator;

        [SerializeField]
        private TMP_Text statusLabel;

        [SerializeField]
        private Button[] coinButtons = Array.Empty<Button>();

        [SerializeField]
        private Button[] plateButtons = Array.Empty<Button>();

        [SerializeField]
        private Button resetButton;

        [SerializeField, Range(0, 2)]
        private int selectedCoinIndex;

        [SerializeField, Range(0, 2)]
        private int selectedPlateIndex;

        private bool coordinatorSubscribed;
        private bool buttonsBound;

        public InteractionPhaseCoordinator Coordinator => coordinator;
        public TMP_Text StatusLabel => statusLabel;
        public IReadOnlyList<Button> CoinButtons => coinButtons;
        public IReadOnlyList<Button> PlateButtons => plateButtons;
        public Button ResetButton => resetButton;
        public string SelectedCoinId => CoinIds[selectedCoinIndex];
        public string SelectedPlateId => PlateIds[selectedPlateIndex];

        public void Configure(
            InteractionPhaseCoordinator targetCoordinator,
            TMP_Text targetStatusLabel,
            Button[] targetCoinButtons,
            Button[] targetPlateButtons,
            Button targetResetButton)
        {
            if (Application.isPlaying)
            {
                Unbind();
            }
            coordinator = targetCoordinator ??
                throw new ArgumentNullException(nameof(targetCoordinator));
            statusLabel = targetStatusLabel ??
                throw new ArgumentNullException(nameof(targetStatusLabel));
            coinButtons = RequireThreeButtons(
                targetCoinButtons,
                nameof(targetCoinButtons)
            );
            plateButtons = RequireThreeButtons(
                targetPlateButtons,
                nameof(targetPlateButtons)
            );
            resetButton = targetResetButton ??
                throw new ArgumentNullException(nameof(targetResetButton));
            if (Application.isPlaying)
            {
                BindCoordinator();
                BindButtons();
            }
        }

        private void Awake()
        {
            BindCoordinator();
            BindButtons();
        }

        private void Start()
        {
            RestartTrial();
        }

        private void OnEnable()
        {
            BindCoordinator();
            BindButtons();
        }

        public void SelectDragonCoin()
        {
            SelectCoin(0);
        }

        public void SelectCoinA()
        {
            SelectCoin(1);
        }

        public void SelectCoinB()
        {
            SelectCoin(2);
        }

        public void SelectDragonPlate()
        {
            SelectPlate(0);
        }

        public void SelectPlateA()
        {
            SelectPlate(1);
        }

        public void SelectPlateB()
        {
            SelectPlate(2);
        }

        public void RestartTrial()
        {
            if (coordinator == null)
            {
                throw new InvalidOperationException(
                    "Coin Interaction Lab requires its W7 coordinator."
                );
            }

            coordinator.Reset();
            coordinator.Configure(CreateRunPlan());
            coordinator.Synchronize(
                PhaseExecutionSnapshot.CreateEngineeringLabActivePhase(2)
            );
            coordinator.Enable();
            ShowReadyStatus();
        }

        private void SelectCoin(int index)
        {
            selectedCoinIndex = Mathf.Clamp(index, 0, CoinIds.Length - 1);
            RestartTrial();
        }

        private void SelectPlate(int index)
        {
            selectedPlateIndex = Mathf.Clamp(index, 0, PlateIds.Length - 1);
            RestartTrial();
        }

        private void BindCoordinator()
        {
            if (!Application.isPlaying || coordinatorSubscribed ||
                coordinator == null || !isActiveAndEnabled)
            {
                return;
            }
            coordinator.ResultProduced += HandleResult;
            coordinatorSubscribed = true;
        }

        private void BindButtons()
        {
            if (!Application.isPlaying || buttonsBound ||
                coinButtons.Length != 3 || plateButtons.Length != 3 ||
                resetButton == null || !isActiveAndEnabled)
            {
                return;
            }
            coinButtons[0].onClick.AddListener(SelectDragonCoin);
            coinButtons[1].onClick.AddListener(SelectCoinA);
            coinButtons[2].onClick.AddListener(SelectCoinB);
            plateButtons[0].onClick.AddListener(SelectDragonPlate);
            plateButtons[1].onClick.AddListener(SelectPlateA);
            plateButtons[2].onClick.AddListener(SelectPlateB);
            resetButton.onClick.AddListener(RestartTrial);
            buttonsBound = true;
        }

        private void HandleResult(ValidationResult result)
        {
            if (result == null || result.PhaseId != 2 || statusLabel == null)
            {
                return;
            }

            if (result.PhaseCompleted)
            {
                statusLabel.color = new Color(0.3f, 1f, 0.45f, 1f);
                statusLabel.text =
                    "正确：目标金币已放入目标盘。\n" +
                    "本轮已锁定；点击“重置本轮”再次测试。";
                return;
            }

            if (result.InteractionError)
            {
                statusLabel.color = new Color(1f, 0.35f, 0.25f, 1f);
                statusLabel.text =
                    "错误组合：请确认金币和盘子是否符合当前目标。\n" +
                    "点击“重置本轮”让金币回到起点。";
            }
        }

        private void ShowReadyStatus()
        {
            if (statusLabel == null)
            {
                return;
            }
            statusLabel.color = Color.white;
            statusLabel.text =
                $"当前目标：{CoinLabels[selectedCoinIndex]} → " +
                $"{PlateLabels[selectedPlateIndex]}\n" +
                "请抓取金币、移动并释放到盘子中；也请测试错误盘。";
        }

        private static Button[] RequireThreeButtons(
            Button[] buttons,
            string parameterName)
        {
            if (buttons == null || buttons.Length != 3)
            {
                throw new ArgumentException(
                    "Exactly three lab selection buttons are required.",
                    parameterName
                );
            }
            var copy = new Button[buttons.Length];
            for (int index = 0; index < buttons.Length; index++)
            {
                copy[index] = buttons[index] ??
                    throw new ArgumentException(
                        "Lab selection buttons cannot contain null.",
                        parameterName
                    );
            }
            return copy;
        }

        private RunPlan CreateRunPlan()
        {
            string phaseTwoSentence = (
                4 + selectedCoinIndex * 3 + selectedPlateIndex
            ).ToString("000");
            string[] sentenceIds =
                { "001", phaseTwoSentence, "013", "016", "025", "026" };
            var phases = new RunPhasePlan[sentenceIds.Length];
            DateTimeOffset createdUtc = DateTimeOffset.UtcNow;
            for (int index = 0; index < sentenceIds.Length; index++)
            {
                string sentenceId = sentenceIds[index];
                int phaseId = index + 1;
                var content = new InstructionContentReference(
                    phaseId,
                    sentenceId,
                    InteractionContractV1.PilotSignerId,
                    "coin_lab_" + sentenceId,
                    createdUtc,
                    0,
                    "coin-lab/" + sentenceId + ".bin",
                    new string('0', 64)
                );
                phases[index] = new RunPhasePlan(
                    phaseId,
                    content,
                    TaskVariantCatalog.ForSentence(sentenceId)
                );
            }

            return new RunPlan(
                "coin-interaction-lab",
                "LAB001",
                "coin_lab_" + Guid.NewGuid().ToString("N"),
                "coin_lab_session",
                createdUtc,
                Application.version,
                "coin-lab",
                2000 + selectedCoinIndex * 10 + selectedPlateIndex,
                new AssistanceAssignment(
                    AssistanceCondition.SignOnly,
                    0,
                    0,
                    AssistanceAssignmentMode.RandomizedBlock
                ),
                new SafePassword(new[] { 1, 2, 3, 4 }),
                new ChestButtonOrder(
                    new[] { "blue", "red", "yellow", "green" }
                ),
                phases
            );
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Unbind()
        {
            if (coordinatorSubscribed && coordinator != null)
            {
                coordinator.ResultProduced -= HandleResult;
            }
            coordinatorSubscribed = false;
            if (!buttonsBound)
            {
                return;
            }
            if (coinButtons.Length == 3)
            {
                coinButtons[0]?.onClick.RemoveListener(SelectDragonCoin);
                coinButtons[1]?.onClick.RemoveListener(SelectCoinA);
                coinButtons[2]?.onClick.RemoveListener(SelectCoinB);
            }
            if (plateButtons.Length == 3)
            {
                plateButtons[0]?.onClick.RemoveListener(SelectDragonPlate);
                plateButtons[1]?.onClick.RemoveListener(SelectPlateA);
                plateButtons[2]?.onClick.RemoveListener(SelectPlateB);
            }
            resetButton?.onClick.RemoveListener(RestartTrial);
            buttonsBound = false;
        }
    }
}
