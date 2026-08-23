using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SignVR.Recording
{
    /// <summary>
    /// Resolves authored sentence target IDs when a sentence changes and feeds
    /// the visual cues. Object lookup is event-driven and cached for the session.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RecordingPointingTargetController : MonoBehaviour
    {
        [SerializeField]
        private RecordingSentenceSequence sentenceSequence;

        [SerializeField]
        private RecordingTargetVisualCues visualCues;

        private readonly Dictionary<string, Transform> targetCache = new(
            StringComparer.Ordinal
        );
        private bool bound;

        public event Action TargetsChanged;

        public string CurrentTargetLabel { get; private set; } = string.Empty;
        public string[] CurrentTargetIds { get; private set; } =
            Array.Empty<string>();
        public string[] UnresolvedTargetIds { get; private set; } =
            Array.Empty<string>();

        public void Configure(
            RecordingSentenceSequence sequence,
            RecordingTargetVisualCues cues)
        {
            Unbind();
            sentenceSequence = sequence;
            visualCues = cues;
            targetCache.Clear();
            Bind();
            ApplyCurrentSentence();
        }

        private void OnEnable()
        {
            Bind();
            ApplyCurrentSentence();
        }

        private void Bind()
        {
            if (bound || sentenceSequence == null)
            {
                return;
            }

            sentenceSequence.SentenceChanged += HandleSentenceChanged;
            bound = true;
        }

        private void Unbind()
        {
            if (!bound)
            {
                return;
            }

            if (sentenceSequence != null)
            {
                sentenceSequence.SentenceChanged -= HandleSentenceChanged;
            }
            bound = false;
        }

        private void HandleSentenceChanged(int _, RecordingSentence sentence)
        {
            ApplySentence(sentence);
        }

        public void ApplyCurrentSentence()
        {
            ApplySentence(sentenceSequence != null
                ? sentenceSequence.CurrentSentence
                : null);
        }

        private void ApplySentence(RecordingSentence sentence)
        {
            if (visualCues == null || sentence == null)
            {
                CurrentTargetLabel = string.Empty;
                CurrentTargetIds = Array.Empty<string>();
                UnresolvedTargetIds = Array.Empty<string>();
                visualCues?.ClearTargets();
                TargetsChanged?.Invoke();
                return;
            }

            string[] ids = sentence.HighlightTargetIds;
            var targets = new List<Transform>(ids.Length);
            var ordinals = new List<int>(ids.Length);
            var unresolved = new List<string>();
            int[] configuredOrdinals = sentence.SequenceNumbers;

            for (int index = 0; index < ids.Length; index++)
            {
                string id = ids[index]?.Trim() ?? string.Empty;
                Transform target = ResolveTarget(id);
                if (target == null)
                {
                    unresolved.Add(id);
                    continue;
                }

                targets.Add(target);
                ordinals.Add(index < configuredOrdinals.Length
                    ? configuredOrdinals[index]
                    : 0);
            }

            CurrentTargetLabel = sentence.TargetLabel;
            CurrentTargetIds = ids.ToArray();
            UnresolvedTargetIds = unresolved.ToArray();
            visualCues.SetTargets(targets.ToArray(), ordinals.ToArray());

            if (unresolved.Count > 0)
            {
                Debug.LogError(
                    "[RecordingPointingTargetController] Missing targets for " +
                    $"{sentence.SentenceId}: {string.Join(", ", unresolved)}",
                    this
                );
            }

            TargetsChanged?.Invoke();
        }

        private Transform ResolveTarget(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            if (targetCache.TryGetValue(id, out Transform cached) &&
                cached != null)
            {
                return cached;
            }

            Scene scene = gameObject.scene;
            string[] path = id.Split('/');
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(
                candidate => string.Equals(
                    candidate.name,
                    path[0],
                    StringComparison.Ordinal
                )
            );
            Transform resolved = path.Length == 1
                ? root?.transform
                : root?.transform.Find(string.Join("/", path.Skip(1)));
            if (resolved != null)
            {
                targetCache[id] = resolved;
            }
            return resolved;
        }

        private void OnDisable()
        {
            Unbind();
            visualCues?.ClearTargets();
        }
    }
}
