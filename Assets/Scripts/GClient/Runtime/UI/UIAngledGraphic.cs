using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.UI
{
    /// <summary>可缩放的切角底板，用于按钮和输入框，支持 UGUI 的颜色过渡。</summary>
    [AddComponentMenu("UI/Angled Graphic")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIAngledGraphic : MaskableGraphic
    {
        [SerializeField, Min(0)] private float cornerCut = 12f;
        [SerializeField, Min(0)] private float borderWidth = 1f;
        [SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.2f);

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect bounds = GetPixelAdjustedRect();
            if (bounds.width <= 0f || bounds.height <= 0f) return;

            float cut = Mathf.Clamp(cornerCut, 0f, Mathf.Min(bounds.width, bounds.height) * 0.5f);
            float border = Mathf.Clamp(borderWidth, 0f, Mathf.Min(bounds.width, bounds.height) * 0.5f);
            if (border > 0f)
            {
                Rect inner = Rect.MinMaxRect(bounds.xMin + border, bounds.yMin + border,
                    bounds.xMax - border, bounds.yMax - border);
                float innerCut = Mathf.Clamp(cut - border * (2f - Mathf.Sqrt(2f)), 0f,
                    Mathf.Min(inner.width, inner.height) * 0.5f);
                AddBorder(mesh, bounds, cut, inner, innerCut, borderColor);
                bounds = inner;
                cut = innerCut;
            }

            if (bounds.width > 0f && bounds.height > 0f)
                AddPolygon(mesh, bounds, cut, color);
        }

        private static void AddPolygon(VertexHelper mesh, Rect rect, float cut, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(rect.center, tint, Vector2.zero);
            for (int i = 0; i < 6; i++)
                mesh.AddVert(Corner(rect, cut, i), tint, Vector2.zero);
            for (int i = 0; i < 6; i++)
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 6);
        }

        private static void AddBorder(VertexHelper mesh, Rect outer, float outerCut,
            Rect inner, float innerCut, Color tint)
        {
            int start = mesh.currentVertCount;
            for (int i = 0; i < 6; i++)
            {
                mesh.AddVert(Corner(outer, outerCut, i), tint, Vector2.zero);
                mesh.AddVert(Corner(inner, innerCut, i), tint, Vector2.zero);
            }
            for (int i = 0; i < 6; i++)
            {
                int a = start + i * 2;
                int b = start + ((i + 1) % 6) * 2;
                mesh.AddTriangle(a, b, b + 1);
                mesh.AddTriangle(a, b + 1, a + 1);
            }
        }

        private static Vector2 Corner(Rect rect, float cut, int index)
        {
            switch (index)
            {
                case 0: return new Vector2(rect.xMin + cut, rect.yMax);
                case 1: return new Vector2(rect.xMax, rect.yMax);
                case 2: return new Vector2(rect.xMax, rect.yMin + cut);
                case 3: return new Vector2(rect.xMax - cut, rect.yMin);
                case 4: return new Vector2(rect.xMin, rect.yMin);
                default: return new Vector2(rect.xMin, rect.yMax - cut);
            }
        }
    }
}
