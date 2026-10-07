using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Cursor-centred scope, target brackets and confirmed-hit feedback. Never intercepts input.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TrainingCombatOverlay : MaskableGraphic
    {
        private const int Segments = 128;
        private readonly Text[] numbers = new Text[12];
        private readonly Vector3[] locations = new Vector3[12];
        private readonly float[] born = new float[12];
        private int next;
        private float hitUntil, shotPulse, health;
        private bool visible, hasTarget;
        private Vector2 cursor, healthPosition;
        private Camera uiCamera;
        private TrainingVision vision;
        public float ScopeAmount { get; private set; }
        public Vector2 ScopeCenter => cursor;
        public float ScopeRadius => Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .25f;

        public void Initialize(Font font)
        {
            raycastTarget = false;
            for (int i = 0; i < numbers.Length; i++)
            {
                var item = new GameObject("Damage " + i, typeof(RectTransform)).AddComponent<Text>();
                item.gameObject.layer = gameObject.layer;
                item.transform.SetParent(transform, false);
                item.font = font; item.fontSize = 24; item.fontStyle = FontStyle.Bold;
                item.alignment = TextAnchor.MiddleCenter; item.raycastTarget = false;
                item.rectTransform.sizeDelta = new Vector2(100, 36);
                item.gameObject.SetActive(false); numbers[i] = item;
            }
        }

        public void ShowHit(TrainingTarget target, float damage, Vector3 location)
        {
            if (vision != null && !vision.CanSeePoint(location)) return;
            hitUntil = Time.time + .16f;
            int slot = next++ % numbers.Length;
            numbers[slot].text = Mathf.CeilToInt(damage).ToString();
            numbers[slot].color = target.IsAlive ? new Color(1, .88f, .6f) : new Color(1, .35f, .2f);
            locations[slot] = location; born[slot] = Time.time;
            numbers[slot].gameObject.SetActive(true);
        }

        public void Present(TrainingCharacterController player, TrainingWeaponController weapon, float scope, bool active, Camera canvasCamera)
        {
            uiCamera = canvasCamera; visible = active; ScopeAmount = active ? scope : 0;
            cursor = Local(player.AimScreenPosition);
            vision = player.ViewCamera.GetComponent<TrainingVision>();
            shotPulse = Mathf.Clamp01(1 - (Time.time - weapon.LastShotTime) / .14f);
            hasTarget = false;
            var world = weapon.Throwables != null ? weapon.Throwables.World : null;
            if (active && player.TryGetAimHit(out var hit))
            {
                var target = hit.collider.GetComponentInParent<TrainingTarget>();
                hasTarget = target != null && target.IsAlive;
                if (hasTarget && vision != null && !vision.CanSeeTarget(target)) hasTarget = false;
                if (hasTarget && world != null && (world.FlashOpacity > .15f ||
                    world.IsSmokeOccluded(player.transform.position + Vector3.up * 1.5f, hit.point) ||
                    world.IsSmokeOccluded(player.ViewCamera.transform.position, hit.point))) hasTarget = false;
                if (hasTarget)
                {
                    health = target.HealthFraction;
                    healthPosition = Local(player.ViewCamera.WorldToScreenPoint(target.transform.position + Vector3.up * 2));
                }
            }
            for (int i = 0; i < numbers.Length; i++)
            {
                if (!numbers[i].gameObject.activeSelf) continue;
                float age = Time.time - born[i];
                Vector3 screen = player.ViewCamera.WorldToScreenPoint(locations[i]);
                if (age >= .7f || screen.z <= 0 || !active || (vision != null && !vision.CanSeePoint(locations[i])) || (world != null && (world.FlashOpacity > .15f ||
                    world.IsSmokeOccluded(player.transform.position + Vector3.up * 1.5f, locations[i]) ||
                    world.IsSmokeOccluded(player.ViewCamera.transform.position, locations[i])))) { numbers[i].gameObject.SetActive(false); continue; }
                numbers[i].rectTransform.anchoredPosition = Local(screen) + Vector2.up * (24 + age * 55);
                Color tint = numbers[i].color; tint.a = Mathf.Clamp01((.7f - age) / .25f); numbers[i].color = tint;
            }
            SetVerticesDirty();
        }

        private Vector2 Local(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, uiCamera, out var local);
            return local;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (!visible) return;
            Color reticle = hasTarget ? new Color(1, .52f, .3f) : new Color(.86f, .96f, .88f);
            if (ScopeAmount > .005f)
            {
                float radius = ScopeRadius;
                float outer = rectTransform.rect.size.magnitude + cursor.magnitude;
                Ring(mesh, cursor, radius, outer, new Color(.015f, .022f, .025f, .78f * ScopeAmount));
                Ring(mesh, cursor, radius - 3, radius, new Color(.07f, .09f, .08f, ScopeAmount));
                Ring(mesh, cursor, radius, radius + 1.5f, new Color(.72f, .8f, .73f, .55f * ScopeAmount));
                Color ink = new Color(.04f, .07f, .05f, .9f * ScopeAmount);
                Line(mesh, cursor + Vector2.left * (radius - 5), cursor + Vector2.left * 12, 2f, ink);
                Line(mesh, cursor + Vector2.right * 12, cursor + Vector2.right * (radius - 5), 2f, ink);
                Line(mesh, cursor + Vector2.down * (radius - 5), cursor + Vector2.down * 12, 2f, ink);
                Line(mesh, cursor + Vector2.up * 12, cursor + Vector2.up * (radius - 5), 2f, ink);
                for (int i = -3; i <= 3; i++)
                {
                    if (i == 0) continue;
                    Vector2 point = cursor + Vector2.right * i * radius * .18f;
                    Line(mesh, point + Vector2.down * 4, point + Vector2.up * 4, 1.8f, ink);
                }
            }
            float gap = 8 + shotPulse * 4;
            for (int i = 0; i < 4; i++)
            {
                Vector2 direction = new Vector2(Mathf.Cos(i * Mathf.PI / 2), Mathf.Sin(i * Mathf.PI / 2));
                Line(mesh, cursor + direction * gap, cursor + direction * (gap + 6), 4, new Color(0, 0, 0, .8f));
                Line(mesh, cursor + direction * gap, cursor + direction * (gap + 6), 2, reticle);
            }
            Quad(mesh, cursor - Vector2.one * 1.5f, cursor + Vector2.one * 1.5f, reticle);
            if (hasTarget)
            {
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    Vector2 corner = cursor + new Vector2(x, y) * 24;
                    Line(mesh, corner, corner - Vector2.right * x * 8, 2, reticle);
                    Line(mesh, corner, corner - Vector2.up * y * 8, 2, reticle);
                }
                Quad(mesh, healthPosition + new Vector2(-29, -4), healthPosition + new Vector2(29, 4), new Color(0, 0, 0, .8f));
                Quad(mesh, healthPosition + new Vector2(-27, -2), healthPosition + new Vector2(-27 + 54 * health, 2), new Color(1, .23f, .12f));
            }
            if (Time.time < hitUntil)
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    Line(mesh, cursor + new Vector2(x, y) * 5, cursor + new Vector2(x, y) * 10, 2.5f, Color.white);
        }

        private static void Ring(VertexHelper mesh, Vector2 center, float inner, float outer, Color tint)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a = i * 2 * Mathf.PI / Segments, b = (i + 1) * 2 * Mathf.PI / Segments;
                Vector2 from = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), to = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Face(mesh, center + from * inner, center + from * outer, center + to * outer, center + to * inner, tint);
            }
        }
        private static void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color tint)
        {
            Vector2 d = (to - from).normalized, side = new Vector2(-d.y, d.x) * width * .5f;
            Face(mesh, from - side, from + side, to + side, to - side, tint);
        }
        private static void Quad(VertexHelper mesh, Vector2 min, Vector2 max, Color tint) =>
            Face(mesh, min, new Vector2(min.x, max.y), max, new Vector2(max.x, min.y), tint);
        private static void Face(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero);
            mesh.AddVert(c, tint, Vector2.zero); mesh.AddVert(d, tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
