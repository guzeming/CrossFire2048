using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Gameplay.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TacticalMapMarker : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            Triangle(vh, r, 1, new Color32(8, 12, 16, 255));
            Triangle(vh, r, .67f, color);
        }

        static void Triangle(VertexHelper vh, Rect r, float scale, Color tint)
        {
            int first = vh.currentVertCount;
            var center = r.center;
            vh.AddVert(center + new Vector2(0, r.height * .5f) * scale, tint, Vector2.zero);
            vh.AddVert(center + new Vector2(r.width * .5f, -r.height * .5f) * scale, tint, Vector2.zero);
            vh.AddVert(center + new Vector2(-r.width * .5f, -r.height * .5f) * scale, tint, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2);
        }
    }
}
