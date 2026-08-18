using System.Collections;
using TMPro;
using UnityEngine;

namespace SignVR.Recording
{
    public sealed class RecordingTutorialController : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private GameObject tutorialRoot;

        [SerializeField]
        private TMP_Text titleText;

        [SerializeField]
        private TMP_Text bodyText;

        [SerializeField]
        private TMP_Text stepText;

        [SerializeField]
        [Min(2f)]
        private float automaticStepSeconds = 5f;

        private readonly string[] titles =
        {
            "1 · 脚踏录制",
            "2 · 长按重录",
            "3 · 实机追踪边界",
            "4 · 桌面动作回看",
            "5 · 开始工作"
        };

        private readonly string[] bodies =
        {
            "踩一下外接空格键开始录制；再次踩一下结束录制。录制前默认倒计时 2 秒。",
            "无论当前处于什么状态，持续踩住空格键，直到视野中的圆环填满，即可回到当前句开始前。旧 Take 会保留为可追溯候选。",
            "慢慢把双手移向四周。青色线只会画在 Quest 3 从相机追踪切换为推断姿态的实测位置；橙色提示出现时请把手移回中央。",
            "提示词在桌上显示；按住提示板上方按钮只能上下调节高度。录制完成后点按桌面触屏的“重播动作”，同一按钮会变为“退出重播”。",
            "正式录制只使用脚踏键。桌面触屏按钮会在录制期间自动收起，避免手语动作误触。"
        };

        private Coroutine automaticRoutine;
        private int stepIndex;

        public bool IsOpen => tutorialRoot != null && tutorialRoot.activeSelf;

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            GameObject root,
            TMP_Text title,
            TMP_Text body,
            TMP_Text step)
        {
            coordinator = recordingCoordinator;
            tutorialRoot = root;
            titleText = title;
            bodyText = body;
            stepText = step;
            tutorialRoot.SetActive(false);
        }

        public void StartTutorial()
        {
            if (tutorialRoot == null)
            {
                return;
            }

            stepIndex = 0;
            tutorialRoot.SetActive(true);
            RefreshStep();
            RestartAutomaticAdvance();
        }

        public void ToggleTutorial()
        {
            if (IsOpen)
            {
                CloseTutorial();
            }
            else
            {
                StartTutorial();
            }
        }

        public void NextStep()
        {
            if (tutorialRoot == null || !tutorialRoot.activeSelf)
            {
                return;
            }

            stepIndex++;
            if (stepIndex >= titles.Length)
            {
                CloseTutorial();
                return;
            }

            RefreshStep();
            RestartAutomaticAdvance();
        }

        public void CloseTutorial()
        {
            if (automaticRoutine != null)
            {
                StopCoroutine(automaticRoutine);
                automaticRoutine = null;
            }

            tutorialRoot?.SetActive(false);
        }

        private void Update()
        {
            if (
                tutorialRoot == null ||
                !tutorialRoot.activeSelf ||
                coordinator == null
            )
            {
                return;
            }

            switch (coordinator.State)
            {
                case RecordingFlowState.Countdown:
                case RecordingFlowState.Recording:
                case RecordingFlowState.Finalizing:
                case RecordingFlowState.Reviewing:
                case RecordingFlowState.Resetting:
                    CloseTutorial();
                    break;
            }
        }

        private void RefreshStep()
        {
            titleText.text = titles[stepIndex];
            bodyText.text = bodies[stepIndex];
            stepText.text = $"{stepIndex + 1} / {titles.Length}";
        }

        private void RestartAutomaticAdvance()
        {
            if (automaticRoutine != null)
            {
                StopCoroutine(automaticRoutine);
            }
            automaticRoutine = StartCoroutine(AutomaticAdvance());
        }

        private IEnumerator AutomaticAdvance()
        {
            yield return new WaitForSecondsRealtime(automaticStepSeconds);
            automaticRoutine = null;
            NextStep();
        }
    }
}
