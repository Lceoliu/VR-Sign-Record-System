using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Recording
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RecordingProgressRingGraphic : MaskableGraphic
    {
        [SerializeField]
        [Range(0f, 1f)]
        private float progress;

        [SerializeField]
        [Min(1f)]
        private float thickness = 12f;

        [SerializeField]
        [Range(12, 128)]
        private int segments = 64;

        public float Progress
        {
            get => progress;
            set
            {
                float next = Mathf.Clamp01(value);

                if (Mathf.Approximately(progress, next))
                {
                    return;
                }

                progress = next;
                SetVerticesDirty();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            if (progress <= 0f)
            {
                return;
            }

            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            float innerRadius = Mathf.Max(0f, outerRadius - thickness);
            int visibleSegments = Mathf.Max(
                1,
                Mathf.CeilToInt(segments * progress)
            );
            float sweep = 360f * progress;

            for (int index = 0; index < visibleSegments; index++)
            {
                float startRatio = index / (float)visibleSegments;
                float endRatio = (index + 1) / (float)visibleSegments;
                float startAngle = Mathf.Deg2Rad * (90f - sweep * startRatio);
                float endAngle = Mathf.Deg2Rad * (90f - sweep * endRatio);

                Vector2 startDirection = new Vector2(
                    Mathf.Cos(startAngle),
                    Mathf.Sin(startAngle)
                );
                Vector2 endDirection = new Vector2(
                    Mathf.Cos(endAngle),
                    Mathf.Sin(endAngle)
                );

                int firstVertex = vertexHelper.currentVertCount;
                vertexHelper.AddVert(
                    center + startDirection * innerRadius,
                    color,
                    Vector2.zero
                );
                vertexHelper.AddVert(
                    center + startDirection * outerRadius,
                    color,
                    Vector2.zero
                );
                vertexHelper.AddVert(
                    center + endDirection * outerRadius,
                    color,
                    Vector2.zero
                );
                vertexHelper.AddVert(
                    center + endDirection * innerRadius,
                    color,
                    Vector2.zero
                );

                vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
                vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
            }
        }

        private void OnValidate()
        {
            progress = Mathf.Clamp01(progress);
            thickness = Mathf.Max(1f, thickness);
            segments = Mathf.Clamp(segments, 12, 128);
            SetVerticesDirty();
        }
    }
}
