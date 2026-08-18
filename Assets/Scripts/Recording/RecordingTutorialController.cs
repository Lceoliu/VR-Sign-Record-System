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
            "3 · 视野提示",
            "4 · 桌面动作回看",
            "5 · 开始工作"
        };

        private readonly string[] bodies =
        {
            "踩一下外接空格键开始录制；再次踩一下结束录制。录制前默认倒计时 2 秒。",
            "无论当前处于什么状态，持续踩住空格键，直到视野中的圆环填满，即可回到当前句开始前。旧 Take 会保留为可追溯候选。",
            "提示词固定在视野上缘；录制状态固定在左上角。倒计时和长按重录进度只会短暂显示在视野中央。",
            "录制完成后，用食指点按桌面触屏上的“重播动作”。镜像机器人开始回放后，同一按钮会变为“退出重播”。",
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
