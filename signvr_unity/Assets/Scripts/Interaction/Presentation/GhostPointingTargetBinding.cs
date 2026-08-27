using System;
using System.Collections.Generic;
using UnityEngine;

namespace SignVR.Interaction.Presentation
{
    [Serializable]
    public sealed class GhostPointingTargetBinding
    {
        [SerializeField]
        private string targetId = string.Empty;

        [SerializeField]
        private Transform targetRoot;

        [SerializeField]
        private Transform[] highlightRoots = Array.Empty<Transform>();

        public GhostPointingTargetBinding(string id, Transform root)
            : this(id, root, root != null ? new[] { root } : null)
        {
        }

        public GhostPointingTargetBinding(
            string id,
            Transform root,
            Transform[] rootsToHighlight)
        {
            targetId = id ?? string.Empty;
            targetRoot = root;
            var uniqueRoots = new List<Transform>();
            if (root != null)
            {
                uniqueRoots.Add(root);
            }
            if (rootsToHighlight != null)
            {
                for (int index = 0;
                    index < rootsToHighlight.Length;
                    index++)
                {
                    Transform candidate = rootsToHighlight[index];
                    if (candidate != null && !uniqueRoots.Contains(candidate))
                    {
                        uniqueRoots.Add(candidate);
                    }
                }
            }
            highlightRoots = uniqueRoots.ToArray();
        }

        public string TargetId => targetId ?? string.Empty;

        public Transform TargetRoot => targetRoot;

        public IReadOnlyList<Transform> HighlightRoots => highlightRoots;
    }
}
