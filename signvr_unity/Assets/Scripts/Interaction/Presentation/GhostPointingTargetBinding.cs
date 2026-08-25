using System;
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

        public GhostPointingTargetBinding(string id, Transform root)
        {
            targetId = id ?? string.Empty;
            targetRoot = root;
        }

        public string TargetId => targetId ?? string.Empty;

        public Transform TargetRoot => targetRoot;
    }
}
