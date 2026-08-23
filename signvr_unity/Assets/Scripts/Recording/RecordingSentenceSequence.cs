using System;
using UnityEngine;

namespace SignVR.Recording
{
    [Serializable]
    public sealed class RecordingSentence
    {
        [SerializeField]
        private string sentenceId = string.Empty;

        [SerializeField]
        [TextArea(2, 5)]
        private string text = string.Empty;

        [SerializeField]
        [Tooltip("World-space recording viewpoint selected before this sentence is loaded.")]
        private string viewpointId = string.Empty;

        [SerializeField]
        [Tooltip("Operator-facing description that distinguishes repeated prompts.")]
        private string targetLabel = string.Empty;

        [SerializeField]
        [Tooltip("Root object names or root-relative paths highlighted for this sentence.")]
        private string[] highlightTargetIds = Array.Empty<string>();

        [SerializeField]
        [Tooltip("Optional 1-based labels parallel to Highlight Target Ids.")]
        private int[] sequenceNumbers = Array.Empty<int>();

        [SerializeField]
        [Min(1)]
        private int startingTakeIndex = 1;

        public RecordingSentence()
        {
        }

        public RecordingSentence(
            string id,
            string sentenceText,
            int firstTakeIndex = 1)
            : this(id, sentenceText, string.Empty, firstTakeIndex)
        {
        }

        public RecordingSentence(
            string id,
            string sentenceText,
            string recordingViewpointId,
            int firstTakeIndex = 1)
        {
            sentenceId = id ?? string.Empty;
            text = sentenceText ?? string.Empty;
            viewpointId = recordingViewpointId ?? string.Empty;
            startingTakeIndex = Mathf.Max(1, firstTakeIndex);
        }

        public RecordingSentence(
            string id,
            string sentenceText,
            string recordingViewpointId,
            string recordingTargetLabel,
            string[] targetIds,
            int[] targetSequenceNumbers = null,
            int firstTakeIndex = 1)
        {
            sentenceId = id ?? string.Empty;
            text = sentenceText ?? string.Empty;
            viewpointId = recordingViewpointId ?? string.Empty;
            targetLabel = recordingTargetLabel ?? string.Empty;
            highlightTargetIds = targetIds ?? Array.Empty<string>();
            sequenceNumbers = targetSequenceNumbers ?? Array.Empty<int>();
            startingTakeIndex = Mathf.Max(1, firstTakeIndex);
        }

        public string SentenceId => sentenceId;
        public string Text => text;
        public string ViewpointId => viewpointId ?? string.Empty;
        public string TargetLabel => targetLabel ?? string.Empty;
        public string[] HighlightTargetIds =>
            highlightTargetIds ?? Array.Empty<string>();
        public int[] SequenceNumbers =>
            sequenceNumbers ?? Array.Empty<int>();
        public int StartingTakeIndex => Mathf.Max(1, startingTakeIndex);

        internal string ResolveSentenceId(int index)
        {
            return string.IsNullOrWhiteSpace(sentenceId)
                ? $"sentence_{index + 1:D3}"
                : sentenceId.Trim();
        }
    }

    /// <summary>
    /// Owns the editable local prompt list used by Editor and standalone Quest
    /// recording. The Python host remains authoritative when configured, so a
    /// remote start_take can keep its existing sentence and Take semantics.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-9000)]
    public sealed class RecordingSentenceSequence : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private RecordingViewpointController viewpointController;

        [Header("Sequence Ownership")]
        [SerializeField]
        private bool autoAdvance = true;

        [SerializeField]
        [Tooltip("When enabled, prompts and sentence advancement are owned by the host.")]
        private bool hostAuthoritative;

        [SerializeField]
        private bool loadInitialSentenceOnEnable = true;

        [Header("Local Session")]
        [SerializeField]
        private string sessionId = "editor-session";

        [SerializeField]
        [Min(0)]
        private int initialSentenceIndex;

        [Header("Editable Sentence Sequence")]
        [SerializeField]
        private RecordingSentence[] sentences =
            RecordingPointingSentenceCatalog.CreateSentences();

        private bool bound;
        private bool initialized;
        private bool sequenceCompleted;
        private int currentSentenceIndex = -1;

        public event Action<int, RecordingSentence> SentenceChanged;
        public event Action SequenceCompleted;

        public int CurrentSentenceIndex => currentSentenceIndex;
        public int SentenceCount => sentences?.Length ?? 0;
        public bool IsSequenceCompleted => sequenceCompleted;
        public bool HasCurrentSentence =>
            currentSentenceIndex >= 0 && currentSentenceIndex < SentenceCount;
        public RecordingSentence CurrentSentence => HasCurrentSentence
            ? sentences[currentSentenceIndex]
            : null;
        public string LocalSessionId => sessionId ?? string.Empty;
        public RecordingViewpointController ViewpointController =>
            viewpointController;

        public bool AutoAdvance
        {
            get => autoAdvance;
            set => autoAdvance = value;
        }

        public bool HostAuthoritative
        {
            get => hostAuthoritative;
            set
            {
                if (hostAuthoritative == value)
                {
                    return;
                }

                hostAuthoritative = value;
                if (!hostAuthoritative && isActiveAndEnabled)
                {
                    TryInitializeLocalSequence();
                }
            }
        }

        public void Configure(RecordingCoordinator recordingCoordinator)
        {
            if (recordingCoordinator == null)
            {
                throw new ArgumentNullException(nameof(recordingCoordinator));
            }

            Unbind();
            coordinator = recordingCoordinator;
            Bind();

            if (isActiveAndEnabled)
            {
                TryInitializeLocalSequence();
            }
        }

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            bool advanceAutomatically,
            bool useHostAuthority)
        {
            autoAdvance = advanceAutomatically;
            hostAuthoritative = useHostAuthority;
            Configure(recordingCoordinator);
        }

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            RecordingViewpointController recordingViewpoints,
            bool advanceAutomatically,
            bool useHostAuthority)
        {
            viewpointController = recordingViewpoints;
            Configure(
                recordingCoordinator,
                advanceAutomatically,
                useHostAuthority
            );
        }

        public void ConfigureSentences(RecordingSentence[] configuredSentences)
        {
            sentences = configuredSentences ?? Array.Empty<RecordingSentence>();
            initialSentenceIndex = SentenceCount == 0
                ? 0
                : Mathf.Clamp(initialSentenceIndex, 0, SentenceCount - 1);
            currentSentenceIndex = -1;
            initialized = false;
            sequenceCompleted = false;
        }

        public bool TryGetSentence(
            string sentenceId,
            out int index,
            out RecordingSentence sentence)
        {
            index = -1;
            sentence = null;
            if (string.IsNullOrWhiteSpace(sentenceId) || sentences == null)
            {
                return false;
            }

            string requestedId = sentenceId.Trim();
            for (int candidateIndex = 0;
                candidateIndex < sentences.Length;
                candidateIndex++)
            {
                RecordingSentence candidate = sentences[candidateIndex];
                if (candidate == null || !string.Equals(
                        candidate.ResolveSentenceId(candidateIndex),
                        requestedId,
                        StringComparison.Ordinal
                    ))
                {
                    continue;
                }

                index = candidateIndex;
                sentence = candidate;
                return true;
            }

            return false;
        }

        public bool TryLoadSentence(int index)
        {
            if (hostAuthoritative || coordinator == null)
            {
                return false;
            }

            if (sentences == null || index < 0 || index >= sentences.Length)
            {
                return false;
            }

            RecordingSentence sentence = sentences[index];
            if (sentence == null)
            {
                Debug.LogWarning(
                    $"[RecordingSentenceSequence] Sentence {index + 1} is empty."
                );
                return false;
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                Debug.LogWarning(
                    "[RecordingSentenceSequence] A local session ID is required."
                );
                return false;
            }

            string resolvedId = sentence.ResolveSentenceId(index);
            if (viewpointController != null)
            {
                bool viewpointSelected = !string.IsNullOrWhiteSpace(
                        sentence.ViewpointId
                    )
                    ? viewpointController.TrySelectViewpoint(
                        sentence.ViewpointId.Trim()
                    )
                    : viewpointController.TrySelectViewpoint(index);
                if (!viewpointSelected)
                {
                    Debug.LogWarning(
                        $"[RecordingSentenceSequence] Could not select the " +
                        $"viewpoint for sentence {index + 1}."
                    );
                    return false;
                }
            }

            if (!coordinator.LoadPrompt(
                    sessionId.Trim(),
                    resolvedId,
                    sentence.Text,
                    sentence.StartingTakeIndex
                ))
            {
                return false;
            }

            currentSentenceIndex = index;
            initialized = true;
            sequenceCompleted = false;
            SentenceChanged?.Invoke(index, sentence);
            return true;
        }

        /// <summary>
        /// Updates the local progress display when the host selects a sentence.
        /// The host still owns the prompt, Take ID, and recording transitions;
        /// this method only mirrors a matching editable entry for the bubble.
        /// </summary>
        public bool SyncHostSentence(string sentenceId)
        {
            if (!hostAuthoritative || string.IsNullOrWhiteSpace(sentenceId))
            {
                return false;
            }

            if (!TryGetSentence(
                    sentenceId,
                    out int index,
                    out RecordingSentence sentence))
            {
                currentSentenceIndex = -1;
                initialized = true;
                sequenceCompleted = false;
                return false;
            }

            currentSentenceIndex = index;
            initialized = true;
            sequenceCompleted = false;
            SentenceChanged?.Invoke(index, sentence);
            return true;
        }

        public bool TryMoveNext()
        {
            if (hostAuthoritative || !HasCurrentSentence)
            {
                return false;
            }

            int nextIndex = currentSentenceIndex + 1;
            if (nextIndex >= SentenceCount)
            {
                NotifySequenceCompleted();
                return false;
            }

            return TryLoadSentence(nextIndex);
        }

        public bool TryMovePrevious()
        {
            if (hostAuthoritative || !HasCurrentSentence)
            {
                return false;
            }

            int previousIndex = currentSentenceIndex - 1;
            return previousIndex >= 0 && TryLoadSentence(previousIndex);
        }

        private void Awake()
        {
            ResolveLocalSessionId();
        }

        private void OnEnable()
        {
            Bind();
            TryInitializeLocalSequence();
        }

        private void Bind()
        {
            if (bound || coordinator == null)
            {
                return;
            }

            coordinator.TakeCompleted += HandleTakeCompleted;
            bound = true;
        }

        private void Unbind()
        {
            if (!bound)
            {
                return;
            }

            coordinator.TakeCompleted -= HandleTakeCompleted;
            bound = false;
        }

        private void TryInitializeLocalSequence()
        {
            if (
                initialized ||
                hostAuthoritative ||
                !loadInitialSentenceOnEnable ||
                coordinator == null ||
                SentenceCount == 0
            )
            {
                return;
            }

            TryLoadSentence(Mathf.Clamp(
                initialSentenceIndex,
                0,
                SentenceCount - 1
            ));
        }

        private void HandleTakeCompleted(
            MetaBodyMotionRecorder.RecordingArtifact artifact)
        {
            if (hostAuthoritative || !autoAdvance || !HasCurrentSentence)
            {
                return;
            }

            RecordingSentence current = CurrentSentence;
            bool isCurrentSentence = string.Equals(
                artifact.Take.SentenceId,
                current.ResolveSentenceId(currentSentenceIndex),
                StringComparison.Ordinal
            );
            bool isCurrentSession = string.Equals(
                artifact.Take.SessionId,
                sessionId.Trim(),
                StringComparison.Ordinal
            );
            if (!isCurrentSentence || !isCurrentSession)
            {
                return;
            }

            // Interrupted retakes never reach this callback. Loading the next
            // prompt only after a completed artifact preserves every old Take.
            TryMoveNext();
        }

        private void NotifySequenceCompleted()
        {
            if (sequenceCompleted)
            {
                return;
            }

            sequenceCompleted = true;
            SequenceCompleted?.Invoke();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void OnValidate()
        {
            initialSentenceIndex = Mathf.Max(0, initialSentenceIndex);
        }

        private void ResolveLocalSessionId()
        {
            if (!IsPlaceholderSessionId(sessionId))
            {
                sessionId = sessionId.Trim();
                return;
            }

#if UNITY_EDITOR
            sessionId = "editor-session";
#else
            string runId = Guid.NewGuid().ToString("N").Substring(0, 8);
            sessionId = $"quest-local-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{runId}";
#endif
        }

        private static bool IsPlaceholderSessionId(string candidate)
        {
            return string.IsNullOrWhiteSpace(candidate) ||
                string.Equals(
                    candidate.Trim(),
                    "editor-session",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    candidate.Trim(),
                    "local-session",
                    StringComparison.OrdinalIgnoreCase
                );
        }
    }
}
