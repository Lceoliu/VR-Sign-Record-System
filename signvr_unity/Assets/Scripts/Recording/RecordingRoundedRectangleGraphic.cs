using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Recording
{
    /// <summary>A sprite-free filled rounded rectangle for runtime UGUI.</summary>
    [DisallowMultipleComponent]
    public sealed class RecordingRoundedRectangleGraphic : MaskableGraphic
    {
        [SerializeField]
        [Min(0f)]
        private float cornerRadius = 32f;

        [SerializeField]
        [Range(2, 16)]
        private int cornerSegments = 8;

        public float CornerRadius
        {
            get => cornerRadius;
            set
            {
                float clamped = Mathf.Max(0f, value);
                if (Mathf.Approximately(cornerRadius, clamped))
                {
                    return;
                }

                cornerRadius = clamped;
                SetVerticesDirty();
            }
        }

        public int CornerSegments
        {
            get => cornerSegments;
            set
            {
                int clamped = Mathf.Clamp(value, 2, 16);
                if (cornerSegments == clamped)
                {
                    return;
                }

                cornerSegments = clamped;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float radius = Mathf.Min(
                Mathf.Max(0f, cornerRadius),
                Mathf.Min(rect.width, rect.height) * 0.5f
            );
            int segments = radius > 0.01f
                ? Mathf.Clamp(cornerSegments, 2, 16)
                : 1;

            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = rect.center;
            vertex.uv0 = new Vector2(0.5f, 0.5f);
            vertexHelper.AddVert(vertex);

            int perimeterCount = segments * 4;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 center = CornerCenter(rect, radius, corner);
                float startDegrees = 180f + corner * 90f;
                for (int segment = 0; segment < segments; segment++)
                {
                    float fraction = (float)segment / segments;
                    float radians = (startDegrees + fraction * 90f) *
                                    Mathf.Deg2Rad;
                    Vector2 position = center + new Vector2(
                        Mathf.Cos(radians),
                        Mathf.Sin(radians)
                    ) * radius;

                    if (radius <= 0.01f)
                    {
                        position = SquareCorner(rect, corner);
                    }

                    vertex.position = position;
                    vertex.uv0 = new Vector2(
                        Mathf.InverseLerp(rect.xMin, rect.xMax, position.x),
                        Mathf.InverseLerp(rect.yMin, rect.yMax, position.y)
                    );
                    vertexHelper.AddVert(vertex);
                }
            }

            for (int index = 0; index < perimeterCount; index++)
            {
                int current = index + 1;
                int next = ((index + 1) % perimeterCount) + 1;
                vertexHelper.AddTriangle(0, current, next);
            }
        }

        private static Vector2 CornerCenter(Rect rect, float radius, int corner)
        {
            switch (corner)
            {
                case 0:
                    return new Vector2(rect.xMin + radius, rect.yMin + radius);
                case 1:
                    return new Vector2(rect.xMax - radius, rect.yMin + radius);
                case 2:
                    return new Vector2(rect.xMax - radius, rect.yMax - radius);
                default:
                    return new Vector2(rect.xMin + radius, rect.yMax - radius);
            }
        }

        private static Vector2 SquareCorner(Rect rect, int corner)
        {
            switch (corner)
            {
                case 0:
                    return new Vector2(rect.xMin, rect.yMin);
                case 1:
                    return new Vector2(rect.xMax, rect.yMin);
                case 2:
                    return new Vector2(rect.xMax, rect.yMax);
                default:
                    return new Vector2(rect.xMin, rect.yMax);
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            cornerRadius = Mathf.Max(0f, cornerRadius);
            cornerSegments = Mathf.Clamp(cornerSegments, 2, 16);
            SetVerticesDirty();
        }
#endif
    }
}
