using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.UI
{
    /// <summary>Shared visual tokens and migration for authored and runtime UGUI controls.</summary>
    public static class BlackGoldTheme
    {
        public static readonly Color Gold = new Color32(255, 203, 94, 255);
        public static readonly Color GoldLight = new Color32(255, 245, 200, 255);
        public static readonly Color Ink = new Color32(10, 12, 15, 245);
        public static readonly Color Surface = new Color32(22, 25, 29, 224);
        public static readonly Color Text = new Color32(238, 235, 225, 255);
        public static readonly Color Muted = new Color32(163, 169, 173, 255);

        public static void Apply(GameObject root)
        {
            foreach (var label in root.GetComponentsInChildren<Text>(true))
                label.color = Remap(label.color);
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null) image.color = Remap(image.color);
                if (image.GetComponent<Selectable>() != null || image.GetComponent<UIGlassEffect>() != null) continue;
                // Only solid plates. Never theme masks, progress bars, icons, reticles or input blockers.
                var size = image.rectTransform.sizeDelta;
                if (image.sprite != null || image.type == Image.Type.Filled || image.color.a < .15f ||
                    size.x < 100 || size.y < 40 || image.GetComponent<Mask>() != null ||
                    image.GetComponent<RectMask2D>() != null || image.name.Contains("Background") ||
                    image.name.Contains("Veil") || image.name.Contains("Blocker") ||
                    (size.x >= 1400 && size.y >= 800)) continue;
                if (image.color.r < .23f && image.color.g < .28f && image.color.b < .32f)
                    SurfaceFor(image);
            }
            foreach (var angled in root.GetComponentsInChildren<UIAngledGraphic>(true))
                if (angled.GetComponent<Selectable>() == null && angled.GetComponent<UIGlassEffect>() == null) SurfaceFor(angled);
            foreach (var control in root.GetComponentsInChildren<Selectable>(true))
                Style(control);
        }

        public static UIGlassEffect SurfaceFor(Graphic graphic, UIGlassEffect.Kind kind = UIGlassEffect.Kind.Panel)
        {
            if (graphic == null) return null;
            var effect = graphic.GetComponent<UIGlassEffect>();
            if (effect == null) effect = graphic.gameObject.AddComponent<UIGlassEffect>();
            effect.SetKind(kind);
            return effect;
        }

        public static void Style(Selectable control, bool? primary = null)
        {
            if (control == null || control.targetGraphic == null) return;
            UISoundFeedback.Attach(control);
            // Transparent full-screen dismiss areas must remain invisible.
            if (control.name == "Dismiss" || control.name.Contains("Blocker")) return;
            var existing = control.GetComponent<UIGlassEffect>();
            if (existing != null && !primary.HasValue) return;
            var graphic = control.targetGraphic;
            bool gold = primary ?? (IsGold(graphic.color) || IsGold(control.colors.normalColor));
            var kind = control is InputField ? UIGlassEffect.Kind.Input :
                gold ? UIGlassEffect.Kind.Primary : UIGlassEffect.Kind.Button;
            SurfaceFor(graphic, kind);
            control.transition = Selectable.Transition.None;
            graphic.canvasRenderer.SetColor(Color.white);
            if (control is InputField input)
            {
                input.customCaretColor = true; input.caretColor = GoldLight;
                input.selectionColor = new Color(Gold.r, Gold.g, Gold.b, .28f);
                if (input.textComponent != null) input.textComponent.color = Text;
                if (input.placeholder != null) input.placeholder.color = Muted;
            }
            else
            {
                var label = control.GetComponentInChildren<Text>(true);
                if (label != null) label.color = gold ? GoldLight : Text;
                // These strokes are part of the main action's glyph, above its smoked lens.
                foreach (var image in control.GetComponentsInChildren<Image>(true))
                    if (image.name == "Arrow1" || image.name == "Arrow2" ||
                        image.name == "ChevronTop" || image.name == "ChevronBottom")
                        image.color = gold ? GoldLight : Text;
            }
        }

        public static void Select(Selectable control, bool selected)
        {
            if (control == null) return;
            Style(control);
            var effect = control.targetGraphic != null ? control.targetGraphic.GetComponent<UIGlassEffect>() : null;
            if (effect != null) effect.Selected = selected;
        }

        public static bool IsGold(Color c) => c.r > .6f && c.g > .3f && c.g < c.r * .95f && c.b < c.g * .82f;
        public static Color Remap(Color color)
        {
            if (IsGold(color)) return new Color(Gold.r, Gold.g, Gold.b, color.a);
            // Red warnings, green confirmation, team blue and original art keep their semantic colors.
            return color;
        }
    }
}
