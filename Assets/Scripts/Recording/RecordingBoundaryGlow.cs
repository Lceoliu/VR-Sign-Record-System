using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Recording
{
    /// <summary>
    /// Directional warning glow on the four edges of the HMD view.
    ///
    /// Signing needs the centre of the view free: the teacher watches the mirrored
    /// character there. Deaf signers also carry more of their visual attention in
    /// the periphery, so the edges are where a warning can be noticed without
    /// interrupting the sign. Each edge fades in continuously with how far the
    /// hand has pushed toward that side, rather than snapping on at a threshold,
    /// so the teacher can feel how much room is left.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RecordingBoundaryGlow : MaskableGraphic
    {
        [SerializeField]
        [Min(4f)]
        private float bandThickness = 90f;

        [SerializeField]
        [Range(1, 12)]
        private int gradientSteps = 6;

        private float left;
        private float right;
        private float up;
        private float down;

        /// <summary>Sets edge intensities in 0..1 (left, right, up, down).</summary>
        public void SetIntensities(float leftEdge, float rightEdge, float upEdge, float downEdge)
        {
            float l = Mathf.Clamp01(leftEdge);
            float r = Mathf.Clamp01(rightEdge);
            float u = Mathf.Clamp01(upEdge);
            float d = Mathf.Clamp01(downEdge);

            if (Mathf.Approximately(l, left) && Mathf.Approximately(r, right) &&
                Mathf.Approximately(u, up) && Mathf.Approximately(d, down))
            {
                return;
            }

            left = l;
            right = r;
            up = u;
            down = d;
            SetVerticesDirty();
        }

        public void Clear() => SetIntensities(0f, 0f, 0f, 0f);

        public bool HasAnyGlow => left > 0f || right > 0f || up > 0f || down > 0f;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            Rect rect = GetPixelAdjustedRect();
            float band = Mathf.Min(bandThickness, Mathf.Min(rect.width, rect.height) * 0.4f);

            AddGradientBand(vertexHelper, rect, band, left, Edge.Left);
            AddGradientBand(vertexHelper, rect, band, right, Edge.Right);
            AddGradientBand(vertexHelper, rect, band, up, Edge.Up);
            AddGradientBand(vertexHelper, rect, band, down, Edge.Down);
        }

        private enum Edge { Left, Right, Up, Down }

        private void AddGradientBand(
            VertexHelper vertexHelper,
            Rect rect,
            float band,
            float intensity,
            Edge edge)
        {
            if (intensity <= 0f)
            {
                return;
            }

            // Stack thin slices whose alpha decays inward, approximating a soft
            // gradient without needing a texture or a custom shader.
            for (int step = 0; step < gradientSteps; step++)
            {
                float inner = step / (float)gradientSteps;
                float outer = (step + 1) / (float)gradientSteps;
                float sliceAlpha = intensity * (1f - inner) * (1f - inner);
                if (sliceAlpha <= 0.001f)
                {
                    continue;
                }

                Rect slice = edge switch
                {
                    Edge.Left => new Rect(
                        rect.xMin + band * inner, rect.yMin,
                        band * (outer - inner), rect.height),
                    Edge.Right => new Rect(
                        rect.xMax - band * outer, rect.yMin,
                        band * (outer - inner), rect.height),
                    Edge.Up => new Rect(
                        rect.xMin, rect.yMax - band * outer,
                        rect.width, band * (outer - inner)),
                    _ => new Rect(
                        rect.xMin, rect.yMin + band * inner,
                        rect.width, band * (outer - inner)),
                };

                AddQuad(vertexHelper, slice, sliceAlpha);
            }
        }

        private void AddQuad(VertexHelper vertexHelper, Rect rect, float alpha)
        {
            int start = vertexHelper.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            Color sliceColor = color;
            sliceColor.a = color.a * alpha;
            vertex.color = sliceColor;

            vertex.position = new Vector3(rect.xMin, rect.yMin);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector3(rect.xMin, rect.yMax);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMax);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMin);
            vertexHelper.AddVert(vertex);

            vertexHelper.AddTriangle(start, start + 1, start + 2);
            vertexHelper.AddTriangle(start + 2, start + 3, start);
        }
    }
}
