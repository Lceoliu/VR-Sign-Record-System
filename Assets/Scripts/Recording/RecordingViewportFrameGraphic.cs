using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Recording
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RecordingViewportFrameGraphic : MaskableGraphic
    {
        [SerializeField]
        [Min(1f)]
        private float thickness = 16f;

        public float Thickness
        {
            get => thickness;
            set
            {
                thickness = Mathf.Max(1f, value);
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            Rect rect = GetPixelAdjustedRect();
            float edge = Mathf.Min(
                thickness,
                Mathf.Min(rect.width, rect.height) * 0.5f
            );

            AddQuad(
                vertexHelper,
                new Rect(rect.xMin, rect.yMax - edge, rect.width, edge)
            );
            AddQuad(
                vertexHelper,
                new Rect(rect.xMin, rect.yMin, rect.width, edge)
            );
            AddQuad(
                vertexHelper,
                new Rect(rect.xMin, rect.yMin + edge, edge, rect.height - edge * 2f)
            );
            AddQuad(
                vertexHelper,
                new Rect(rect.xMax - edge, rect.yMin + edge, edge, rect.height - edge * 2f)
            );
        }

        private void AddQuad(VertexHelper vertexHelper, Rect rect)
        {
            int start = vertexHelper.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;

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
