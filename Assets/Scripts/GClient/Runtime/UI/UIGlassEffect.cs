using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OperationBlacktide.Client.UI
{
    /// <summary>One shared material; per-control optics and interaction are carried by vertex data.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Graphic))]
    public sealed class UIGlassEffect : BaseMeshEffect, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public enum Kind { Panel, Button, Primary, Input, Tab, Rail }
        [SerializeField] private Kind kind;
        [SerializeField] private bool selected;
        private Selectable control;
        private bool hovered, pressed, focused;
        private float emphasis, depression, disabled;
        private static Material sharedMaterial;
        public Kind Style => kind;
        public bool Selected { get => selected; set { if (selected == value) return; selected = value; Dirty(); } }

        public void SetKind(Kind value) { kind = value; Configure(); Dirty(); }
        protected override void OnEnable() { base.OnEnable(); Configure(); }
        protected override void OnDisable()
        {
            hovered = pressed = focused = false; emphasis = depression = 0;
            base.OnDisable();
        }
        protected override void OnCanvasHierarchyChanged() { base.OnCanvasHierarchyChanged(); Configure(); }
        private void Configure()
        {
            if (graphic == null) return;
            control = GetComponent<Selectable>();
            if (sharedMaterial == null) sharedMaterial = Resources.Load<Material>("UI/Theme/BlackGoldGlass");
            if (sharedMaterial != null) graphic.material = sharedMaterial;
            if (graphic.canvas != null)
                graphic.canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
        }
        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            bool enabledControl = control == null || control.IsInteractable();
            float target = enabledControl && (hovered || focused || selected) ? 1 : 0;
            float push = enabledControl && pressed ? 1 : 0;
            float dim = enabledControl ? 0 : 1;
            float next = Mathf.MoveTowards(emphasis, target, Time.unscaledDeltaTime / .14f);
            float down = Mathf.MoveTowards(depression, push, Time.unscaledDeltaTime / .08f);
            float faded = Mathf.MoveTowards(disabled, dim, Time.unscaledDeltaTime / .14f);
            if (next == emphasis && down == depression && faded == disabled) return;
            emphasis = next; depression = down; disabled = faded; Dirty();
        }
        private void Dirty() { if (graphic != null) graphic.SetVerticesDirty(); }
        public void OnPointerEnter(PointerEventData e) { hovered = true; }
        public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) pressed = true; }
        public void OnPointerUp(PointerEventData e) { pressed = false; }
        public void OnSelect(BaseEventData e) { focused = true; }
        public void OnDeselect(BaseEventData e) { focused = pressed = false; }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || graphic == null) return;
            Rect rect = graphic.GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            mesh.Clear();
            float active = Application.isPlaying ? emphasis : selected ? 1 : 0;
            float dim = Application.isPlaying ? disabled : control != null && !control.IsInteractable() ? 1 : 0;
            // Tactical plates have hard edges and no exterior drop shadow.
            for (int i = 0; i < 4; i++)
            {
                float x = i == 0 || i == 3 ? 0 : rect.width;
                float y = i < 2 ? 0 : rect.height;
                var v = UIVertex.simpleVert;
                v.position = new Vector3(rect.xMin + x, rect.yMin + y);
                v.color = Color.white;
                v.uv0 = new Vector2(x / rect.width, y / rect.height);
                v.uv1 = new Vector4(rect.width, rect.height, (float)kind, active);
                v.uv2 = new Vector4(depression, dim, selected ? 1 : 0, 0);
                mesh.AddVert(v);
            }
            mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(2, 3, 0);
        }
    }
}
