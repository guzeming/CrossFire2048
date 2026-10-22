using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.UI
{
    /// <summary>加载背景下缘的烟黑渐隐，无额外纹理或逐帧分配。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UILoadingShade : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            AddRow(mesh, rect, 0, 1);
            AddRow(mesh, rect, .35f, .93f);
            AddRow(mesh, rect, .72f, .12f);
            AddRow(mesh, rect, 1, .25f);
            for (int row = 0; row < 3; row++)
            {
                int i = row * 2;
                mesh.AddTriangle(i, i + 2, i + 1);
                mesh.AddTriangle(i + 1, i + 2, i + 3);
            }
        }

        private void AddRow(VertexHelper mesh, Rect rect, float y, float alpha)
        {
            Color tint = color; tint.a *= alpha;
            mesh.AddVert(new Vector3(rect.xMin, rect.yMin + rect.height * y), tint, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMax, rect.yMin + rect.height * y), tint, Vector2.zero);
        }
    }
}
