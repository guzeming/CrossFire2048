using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Demolition
{
    // Mesh gradient keeps the live lobby visible without another texture or material.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DemolitionGradient : MaskableGraphic
    {
        public Color From, To;
        public bool Vertical;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;
            vh.AddVert(new Vector3(r.xMin,r.yMin),Vertical?To:From,Vector2.zero);
            vh.AddVert(new Vector3(r.xMin,r.yMax),From,Vector2.zero);
            vh.AddVert(new Vector3(r.xMax,r.yMax),Vertical?From:To,Vector2.zero);
            vh.AddVert(new Vector3(r.xMax,r.yMin),To,Vector2.zero);
            vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);
        }
    }
}
