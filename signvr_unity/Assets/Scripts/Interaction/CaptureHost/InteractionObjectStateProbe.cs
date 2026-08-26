using System;
using System.Text;
using UnityEngine;

namespace SignVR.Interaction.CaptureHost
{
    [DisallowMultipleComponent]
    public sealed class InteractionObjectStateProbe : MonoBehaviour
    {
        [SerializeField]
        private string objectId = "object";

        [SerializeField]
        private Transform capturedTransform;

        [SerializeField]
        private string logicalState = "initial";

        public string ObjectId => objectId;

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            if (capturedTransform == null)
            {
                capturedTransform = transform;
            }
        }

        public void Configure(string id, Transform value = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "Object ID is required.",
                    nameof(id)
                );
            }
            objectId = id.Trim();
            capturedTransform = value == null ? transform : value;
        }

        public void SetLogicalState(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Logical state is required.",
                    nameof(value)
                );
            }
            logicalState = value.Trim();
        }

        public string CaptureStateJson()
        {
            Transform target = capturedTransform == null
                ? transform
                : capturedTransform;
            Vector3 position = target.position;
            Quaternion rotation = target.rotation;
            var builder = new StringBuilder(256);
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(
                builder,
                "active_in_hierarchy"
            );
            InteractionCaptureJson.AppendBoolean(
                builder,
                target.gameObject.activeInHierarchy
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "position"
            );
            AppendVector(builder, position);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "rotation"
            );
            AppendQuaternion(builder, rotation);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "logical_state"
            );
            InteractionJson.AppendQuoted(
                builder,
                logicalState ?? string.Empty
            );
            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendVector(
            StringBuilder builder,
            Vector3 value)
        {
            builder.Append('[');
            InteractionJson.AppendFiniteDouble(builder, value.x);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.y);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.z);
            builder.Append(']');
        }

        private static void AppendQuaternion(
            StringBuilder builder,
            Quaternion value)
        {
            builder.Append('[');
            InteractionJson.AppendFiniteDouble(builder, value.x);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.y);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.z);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.w);
            builder.Append(']');
        }

        private void OnValidate()
        {
            if (capturedTransform == null)
            {
                capturedTransform = transform;
            }
        }
    }
}
