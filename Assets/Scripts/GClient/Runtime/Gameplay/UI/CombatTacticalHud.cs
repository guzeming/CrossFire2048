using System.Collections.Generic;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Client.Gameplay.Vision;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Gameplay.UI
{
    /// <summary>Passive shared radar and team roster. Modes supply authoritative participant state.</summary>
    [DefaultExecutionOrder(230)]
    public sealed class CombatTacticalHud : MonoBehaviour
    {
        sealed class Slot
        {
            public RectTransform Root;
            public Image Portrait, Health, Accent, Shade;
            public Text Name, Vitals, Status, Self;
            public TacticalMapMarker Marker;
        }

        const float CardWidth = 68, CardStep = 74, MapSize = 208;
        static readonly Color Friendly = new Color32(117, 199, 227, 255);
        static readonly Color Enemy = new Color32(235, 131, 123, 255);
        static readonly Color LowHealth = new Color32(255, 99, 87, 255);
        readonly Dictionary<int, Slot> slots = new Dictionary<int, Slot>();
        readonly List<CombatHudParticipant> ordered = new List<CombatHudParticipant>();
        readonly List<int> removed = new List<int>();
        RectTransform root, roster, map;
        Text allies, opponents;
        Text bomb;
        Font font;
        CombatMapLayout layout;
        int localTeam, localId;
        CharacterVision observerVision;
        bool observerAlive;

        public static CombatTacticalHud Create(Transform parent, Font font)
        {
            var rect = Rect(parent, "Tactical HUD", 0, 0, 1600, 302);
            var hud = rect.gameObject.AddComponent<CombatTacticalHud>();
            hud.Build(font);
            return hud;
        }

        void Build(Font value)
        {
            font = value;
            root = (RectTransform)transform;
            layout = Resources.Load<CombatMapLayout>("Gameplay/DustIIRadar");
            var radar = Rect(root, "Minimap", 24, 24, 232, 270);
            Fill(radar, "Radar background", 0, 0, 232, 270, new Color32(9, 14, 19, 228));
            Fill(radar, "Accent", 0, 0, 232, 2, BlackGoldTheme.Gold);
            Label(radar, "Map name", "DUST II", 12, 4, 158, 24, 14, BlackGoldTheme.Text, TextAnchor.MiddleLeft);
            Label(radar, "North", "N ↑", 176, 4, 44, 24, 13, BlackGoldTheme.Muted);
            map = Rect(radar, "Map", 12, 32, MapSize, MapSize);
            var image = map.gameObject.AddComponent<RawImage>();
            image.texture = layout != null ? layout.texture : null;
            image.color = layout != null ? Color.white : new Color32(19, 28, 35, 255);
            image.raycastTarget = false;
            map.gameObject.AddComponent<RectMask2D>();
            Label(radar, "Self legend", "自己", 12, 242, 48, 24, 12, BlackGoldTheme.Gold);
            Label(radar, "Ally legend", "队友", 66, 242, 48, 24, 12, Friendly);
            Label(radar, "Enemy legend", "可见敌人", 120, 242, 100, 24, 12, Enemy);
            bomb = Label(map, "Bomb", "C4", 0, 0, 28, 18, 12, BlackGoldTheme.Gold);
            bomb.gameObject.SetActive(false);
            roster = Rect(root, "Team rosters", 0, 14, 1040, 134);
            roster.anchorMin = roster.anchorMax = new Vector2(.5f, 1);
            roster.pivot = new Vector2(.5f, 1);
            allies = Label(roster, "Allies", "我方", 0, 0, 364, 23, 13, Friendly, TextAnchor.MiddleLeft);
            opponents = Label(roster, "Opponents", "敌方", 676, 0, 364, 23, 13, Enemy, TextAnchor.MiddleRight);
            Resize();
        }

        public void SetSites(IReadOnlyList<Vector3> sites)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                string name = "Site " + i;
                var marker = map.Find(name)?.GetComponent<Text>() ?? Label(map, name, i == 0 ? "A" : "B", 0, 0, 26, 30, 17, BlackGoldTheme.Gold);
                Place(marker.rectTransform, sites[i]);
                marker.transform.SetSiblingIndex(i);
            }
        }

        public void SetBomb(bool visible, Vector3 position, bool planted)
        {
            bomb.gameObject.SetActive(visible && layout != null);
            if (!visible) return;
            Place(bomb.rectTransform, position);
            bomb.color = planted ? LowHealth : BlackGoldTheme.Gold;
            bomb.transform.SetAsLastSibling();
        }

        public void Present(IReadOnlyList<CombatHudParticipant> participants, int localId)
        {
            this.localId = localId;
            Resize();
            localTeam = 0;
            for (int i = 0; i < participants.Count; i++)
                if (participants[i].Id == localId) { localTeam = participants[i].Team; break; }
            ordered.Clear();
            for (int i = 0; i < participants.Count; i++) ordered.Add(participants[i]);
            // Identity order stays stable through death, reconnects and half-time side swaps.
            ordered.Sort((a, b) => a.Id.CompareTo(b.Id));
            removed.Clear();
            foreach (int id in slots.Keys)
            {
                bool exists = false;
                for (int i = 0; i < ordered.Count; i++) if (ordered[i].Id == id) { exists = true; break; }
                if (!exists) removed.Add(id);
            }
            foreach (int id in removed)
            {
                Destroy(slots[id].Root.gameObject); Destroy(slots[id].Marker.gameObject); slots.Remove(id);
            }
            int friendlyIndex = 0, enemyIndex = 0, friendlyAlive = 0, enemyAlive = 0;
            foreach (var p in ordered)
            {
                bool friendly = p.Team == localTeam, self = p.Id == localId;
                if (!slots.TryGetValue(p.Id, out var slot)) { slot = CreateSlot(p.Id); slots.Add(p.Id, slot); }
                int index = friendly ? friendlyIndex++ : enemyIndex++;
                slot.Root.anchoredPosition = new Vector2((friendly ? 0 : 676) + index * CardStep, -26);
                Color team = friendly ? Friendly : Enemy;
                bool alive = p.HasSnapshot && p.Health > 0;
                if (alive && p.Connected) { if (friendly) friendlyAlive++; else enemyAlive++; }
                slot.Name.text = p.Name ?? "";
                slot.Self.text = self ? "你" : "";
                slot.Portrait.sprite = p.Portrait; slot.Portrait.enabled = p.Portrait != null;
                slot.Portrait.color = alive && p.Connected ? Color.white : new Color(.38f, .38f, .38f, .8f);
                slot.Shade.color = alive && p.Connected ? Color.clear : new Color(0, 0, 0, .28f);
                slot.Accent.color = self ? BlackGoldTheme.Gold : team;
                slot.Vitals.text = p.HasSnapshot ? Mathf.Max(0, p.Health).ToString() : "--";
                float health = p.HasSnapshot ? Mathf.Clamp01((float)p.Health / Mathf.Max(1, p.MaxHealth)) : 0;
                slot.Health.rectTransform.sizeDelta = new Vector2(CardWidth * health, 4);
                slot.Health.color = health <= .25f ? LowHealth : team;
                slot.Status.text = !p.HasSnapshot ? "连接中" : !p.Connected ? "离线" : !alive ? "阵亡" : string.IsNullOrEmpty(p.Status) ? "存活" : p.Status;
                slot.Status.color = !p.HasSnapshot || !p.Connected || !alive ? BlackGoldTheme.Muted : p.Status == "换弹中" ? BlackGoldTheme.Gold : team;
            }
            allies.text = $"我方  {friendlyAlive} / {friendlyIndex}";
            opponents.text = $"敌方  {enemyAlive} / {enemyIndex}";
            RefreshMarkers();
        }

        public void SetObserver(CharacterVision vision, bool alive)
        {
            observerVision = vision;
            observerAlive = alive;
        }

        void LateUpdate() => RefreshMarkers();

        void RefreshMarkers()
        {
            // Turning, interpolation, smoke and spectating can change sight between network snapshots.
            foreach (var p in ordered)
            {
                var marker = slots[p.Id].Marker;
                bool friendly = p.Team == localTeam;
                Vector3 position = p.WorldTransform != null ? p.WorldTransform.position : p.Position;
                bool visible = layout != null && p.HasSnapshot && p.Connected && p.Health > 0 &&
                    (p.WorldTransform == null || p.WorldTransform.gameObject.activeInHierarchy) &&
                    (friendly || observerAlive && observerVision != null && observerVision.HasObserver &&
                        observerVision.CanSeeCharacter(position, p.WorldTransform));
                marker.gameObject.SetActive(visible);
                if (!visible) continue;
                marker.color = p.Id == localId ? BlackGoldTheme.Gold : friendly ? Friendly : Enemy;
                Place(marker.rectTransform, position);
                float yaw = p.WorldTransform != null ? p.WorldTransform.eulerAngles.y : p.Yaw;
                marker.rectTransform.localRotation = Quaternion.Euler(0, 0, -yaw);
            }
            if (slots.TryGetValue(localId, out var localSlot)) localSlot.Marker.transform.SetAsLastSibling();
        }

        Slot CreateSlot(int id)
        {
            var r = Rect(roster, "Player " + id, 0, 26, CardWidth, 112);
            Fill(r, "Card background", 0, 0, CardWidth, 112, new Color32(12, 18, 23, 222));
            var slot = new Slot { Root = r };
            slot.Accent = Fill(r, "Team accent", 0, 0, CardWidth, 2, Friendly);
            slot.Portrait = Fill(r, "Portrait", 6, 3, 56, 55, Color.white);
            slot.Portrait.preserveAspect = true;
            slot.Shade = Fill(r, "Unavailable shade", 0, 2, CardWidth, 56, Color.clear);
            Fill(r, "Health backing", 0, 58, CardWidth, 4, new Color32(44, 50, 57, 255));
            slot.Health = Fill(r, "Health fill", 0, 58, CardWidth, 4, Friendly);
            slot.Vitals = Label(r, "Health value", "--", 25, 28, 40, 30, 16, BlackGoldTheme.Text, TextAnchor.MiddleRight);
            slot.Vitals.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -1);
            slot.Self = Label(r, "Local player", "", 3, 4, 22, 21, 12, BlackGoldTheme.Gold);
            slot.Name = Label(r, "Name", "", 3, 65, 62, 20, 12, BlackGoldTheme.Text);
            slot.Name.resizeTextForBestFit = true; slot.Name.resizeTextMinSize = 9; slot.Name.resizeTextMaxSize = 12;
            slot.Status = Label(r, "Status", "", 2, 87, 64, 21, 12, Friendly);
            var marker = Rect(map, "Player marker " + id, 0, 0, 14, 16);
            slot.Marker = marker.gameObject.AddComponent<TacticalMapMarker>(); slot.Marker.raycastTarget = false;
            return slot;
        }

        void Place(RectTransform marker, Vector3 world)
        {
            if (layout == null) return;
            Vector2 uv = layout.Project(world);
            marker.pivot = new Vector2(.5f, .5f);
            marker.anchoredPosition = new Vector2(Mathf.Clamp(uv.x * MapSize, 8, MapSize - 8), -Mathf.Clamp((1 - uv.y) * MapSize, 8, MapSize - 8));
        }

        void Resize()
        {
            if (!(root.parent is RectTransform parent) || parent.rect.width <= 0) return;
            float scale = Mathf.Min(1, parent.rect.width / 1600f);
            root.localScale = Vector3.one * scale;
            root.sizeDelta = new Vector2(parent.rect.width / scale, 302);
        }

        static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return rt;
        }

        static Image Fill(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false; return image;
        }

        Text Label(Transform parent, string name, string text, float x, float y, float w, float h, int size, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = size; label.text = text; label.color = color; label.alignment = alignment;
            label.supportRichText = false; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow; label.verticalOverflow = VerticalWrapMode.Truncate;
            if (name == "Name") label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }
    }
}
