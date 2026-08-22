using TMPro;
using UnityEngine;

namespace SignVR.Recording
{
    public sealed class RecordingSidePromptPresenter : MonoBehaviour
    {
        [SerializeField] private RecordingCoordinator coordinator;
        [SerializeField] private TMP_Text previousText;
        [SerializeField] private TMP_Text currentText;
        [SerializeField] private TMP_Text nextText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text modeText;
        [SerializeField] private GameObject noticeRoot;
        [SerializeField] private TMP_Text noticeText;
        private RecordingCoordinator subscribedCoordinator;

        public void Configure(
            RecordingCoordinator source,
            TMP_Text previous,
            TMP_Text current,
            TMP_Text next,
            TMP_Text progress,
            TMP_Text mode,
            GameObject notice,
            TMP_Text noticeLabel)
        {
            Unbind();
            coordinator = source;
            previousText = previous;
            currentText = current;
            nextText = next;
            progressText = progress;
            modeText = mode;
            noticeRoot = notice;
            noticeText = noticeLabel;
            Bind();
            Refresh();
        }

        private void OnEnable()
        {
            Bind();
            Refresh();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void Bind()
        {
            if (coordinator == null || subscribedCoordinator == coordinator)
            {
                return;
            }

            Unbind();
            coordinator.PresentationChanged += Refresh;
            subscribedCoordinator = coordinator;
        }

        private void Unbind()
        {
            if (subscribedCoordinator == null)
            {
                return;
            }

            subscribedCoordinator.PresentationChanged -= Refresh;
            subscribedCoordinator = null;
        }

        private void Refresh()
        {
            if (coordinator == null || previousText == null || currentText == null ||
                nextText == null || progressText == null || modeText == null ||
                noticeRoot == null || noticeText == null)
            {
                return;
            }

            previousText.text = string.IsNullOrWhiteSpace(coordinator.PreviousPromptText)
                ? "—"
                : coordinator.PreviousPromptText;
            currentText.text = coordinator.PromptText;
            nextText.text = string.IsNullOrWhiteSpace(coordinator.NextPromptText)
                ? "—"
                : coordinator.NextPromptText;
            progressText.text = coordinator.TotalSentences > 0
                ? $"第 {coordinator.SentenceIndex + 1} / {coordinator.TotalSentences} 句"
                : "等待主机同步句子";
            modeText.text = coordinator.SigningMode == "rough"
                ? "粗打"
                : coordinator.SigningMode == "precise"
                    ? "精打"
                    : string.Empty;
            bool showNotice = !string.IsNullOrWhiteSpace(coordinator.ModeSwitchNotice);
            noticeRoot.SetActive(showNotice);
            noticeText.text = coordinator.ModeSwitchNotice;
        }
    }
}
