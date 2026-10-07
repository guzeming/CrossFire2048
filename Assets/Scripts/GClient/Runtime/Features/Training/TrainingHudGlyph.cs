using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Resolution-independent health / armor / ammunition symbols.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TrainingHudGlyph : MaskableGraphic
    {
        public enum Shape { Health, Armor, Ammo }
        [SerializeField] private Shape shape;
        public Shape Icon { get => shape; set { shape = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (shape == Shape.Health)
            {
                Quad(vh, .35f, .08f, .65f, .92f);
                Quad(vh, .08f, .35f, .35f, .65f);
                Quad(vh, .65f, .35f, .92f, .65f);
            }
            else if (shape == Shape.Armor)
            {
                Polygon(vh, new[] { new Vector2(.5f,.05f), new Vector2(.14f,.35f), new Vector2(.1f,.85f),
                    new Vector2(.5f,.97f), new Vector2(.9f,.85f), new Vector2(.86f,.35f) });
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    float x = .08f + i * .3f;
                    Quad(vh, x, .12f, x + .2f, .7f);
                    Polygon(vh, new[] { new Vector2(x,.74f), new Vector2(x+.1f,.96f), new Vector2(x+.2f,.74f) });
                }
            }
        }

        private void Quad(VertexHelper vh, float x0, float y0, float x1, float y1)
        { Polygon(vh, new[] { new Vector2(x0,y0), new Vector2(x0,y1), new Vector2(x1,y1), new Vector2(x1,y0) }); }

        private void Polygon(VertexHelper vh, Vector2[] points)
        {
            int first = vh.currentVertCount;
            Rect rect = GetPixelAdjustedRect();
            foreach (var point in points)
                vh.AddVert(new Vector3(rect.x + point.x * rect.width, rect.y + point.y * rect.height), color, Vector2.zero);
            for (int i = 1; i < points.Length - 1; i++) vh.AddTriangle(first, first + i, first + i + 1);
        }
    }
}
