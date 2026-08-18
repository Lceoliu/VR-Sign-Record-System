using System;
using System.IO;
using System.Text;

namespace SignVR.Recording
{
    public enum RecordingFlowState
    {
        Disconnected,
        Ready,
        Countdown,
        Recording,
        Finalizing,
        Completed,
        Reviewing,
        Resetting,
        Error
    }

    public readonly struct RecordingTakeContext
    {
        public RecordingTakeContext(
            string sessionId,
            string sentenceId,
            string promptText,
            int takeIndex,
            string takeId,
            DateTime createdAtUtc)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID is required.", nameof(sessionId));
            }

            if (string.IsNullOrWhiteSpace(sentenceId))
            {
                throw new ArgumentException("Sentence ID is required.", nameof(sentenceId));
            }

            if (takeIndex < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(takeIndex));
            }

            if (string.IsNullOrWhiteSpace(takeId))
            {
                throw new ArgumentException("Take ID is required.", nameof(takeId));
            }

            SessionId = sessionId;
            SentenceId = sentenceId;
            PromptText = promptText ?? string.Empty;
            TakeIndex = takeIndex;
            TakeId = takeId;
            CreatedAtUtc = createdAtUtc;
        }

        public string SessionId { get; }
        public string SentenceId { get; }
        public string PromptText { get; }
        public int TakeIndex { get; }
        public string TakeId { get; }
        public DateTime CreatedAtUtc { get; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(SessionId) &&
            !string.IsNullOrWhiteSpace(SentenceId) &&
            TakeIndex > 0 &&
            !string.IsNullOrWhiteSpace(TakeId);

        public string SafeSessionId => SanitizeFileSegment(SessionId);
        public string SafeSentenceId => SanitizeFileSegment(SentenceId);

        public string FileStem =>
            $"{SafeSentenceId}__take-{TakeIndex:D3}__{SanitizeFileSegment(TakeId)}";

        public static RecordingTakeContext CreateLocal(
            string sessionId,
            string sentenceId,
            string promptText,
            int takeIndex)
        {
            return new RecordingTakeContext(
                sessionId,
                sentenceId,
                promptText,
                takeIndex,
                Guid.NewGuid().ToString("N"),
                DateTime.UtcNow
            );
        }

        private static string SanitizeFileSegment(string value)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);

            foreach (char character in value)
            {
                builder.Append(Array.IndexOf(invalidCharacters, character) >= 0
                    ? '_'
                    : character);
            }

            return builder.ToString();
        }
    }
}
