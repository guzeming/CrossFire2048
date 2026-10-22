using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Gameplay.Cameras;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Client.Gameplay.Vision;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Client.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Gameplay.UI
{
    [DefaultExecutionOrder(220)]
    public class CombatHudPanel : UIPanel
    {
        [SerializeField] private Button returnButton;
        [SerializeField] private RectTransform safeArea, crosshair;
        [SerializeField] private Text healthText, armorText, magazineText, reserveText, weaponText;
        [SerializeField] private Text ammoStatus, timerText, slotText;
        [SerializeField] private GameObject ammoNumbers, sessionCard;
        [SerializeField] private Image healthFill, armorFill, weaponIcon, ammoFill, reloadFill;
        [SerializeField] private GameObject reloadTrack;
        private ICombatHudContext scene;
        private bool HasContext => scene != null && (!(scene is Object owner) || owner != null);
        private WeaponController weapon;
        private CharacterVitals vitals;
        private AmmoState ammo;
        private Canvas canvas;
        private CombatOverlay combat;
        private CombatTacticalHud tacticalHud;
        private Image smokeVeil, flashVeil;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;
        private int lastSecond = -1;
        private bool wasBlocked;
        private static readonly Color White = OperationBlacktide.Client.UI.BlackGoldTheme.Text;
        private static readonly Color Accent = OperationBlacktide.Client.UI.BlackGoldTheme.Gold;
        private static readonly Color Warning = new Color32(255, 102, 88, 255);

        protected override void OnOpen(object args)
        {
            scene = args as ICombatHudContext;
            if (!HasContext || scene.Player == null) return;
            vitals = scene.Vitals;
            weapon = scene.Player.GetComponent<WeaponController>();
            ammo = weapon != null ? weapon.Ammo : null;
            canvas = GetComponentInParent<Canvas>();
            if (tacticalHud == null) tacticalHud = CombatTacticalHud.Create(safeArea, healthText.font);
            tacticalHud.gameObject.SetActive(true);
            var oldLocation = safeArea.Find("Location");
            if (oldLocation != null) oldLocation.gameObject.SetActive(false);
            AddButton(returnButton, scene.OpenMenu);
            // Capture the subscribed source because this persistent panel is reused between visits.
            var healthSource = vitals;
            if (healthSource != null) AddEvent(h => healthSource.Changed += h, h => healthSource.Changed -= h, RefreshVitals);
            if (combat == null)
            {
                var go = new GameObject("Combat Overlay", typeof(RectTransform));
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false); go.transform.SetAsFirstSibling();
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                combat = go.AddComponent<CombatOverlay>(); combat.Initialize(healthText.font);
            }
            combat.gameObject.SetActive(true);
            if (flashVeil == null)
            {
                flashVeil = CreateVeil("Flash Blindness");
                smokeVeil = CreateVeil("Smoke Obscuration");
            }
            if (weapon != null) { weapon.WeaponChanged += BindWeapon; weapon.TargetHit += combat.ShowHit; }
            ammo = null;
            BindWeapon();
            lastSecond = -1;
            wasBlocked = !scene.IsInputBlocked;
            RefreshVitals();
            RefreshSafeArea();
        }

        private void BindWeapon()
        {
            if (ammo != null) ammo.Changed -= RefreshAmmo;
            ammo = weapon != null ? weapon.Ammo : null;
            if (ammo != null) ammo.Changed += RefreshAmmo;
            var catalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
            var equipped = weapon != null && catalog != null ? catalog.Weapon(weapon.WeaponId) : null;
            weaponText.text = equipped != null ? equipped.displayName : "未装备武器";
            weaponIcon.sprite = weapon != null ? weapon.HudIcon : null;
            weaponIcon.enabled = weaponIcon.sprite != null;
            if (slotText != null) slotText.text = weapon != null ? $"{weapon.Slot}  {WeaponSelection.SlotName(weapon.Slot)}" : "武器";
            RefreshAmmo();
        }

        private void RefreshVitals()
        {
            healthText.text = vitals != null ? vitals.Health.ToString() : "--";
            armorText.text = vitals != null ? vitals.Armor.ToString() : "--";
            float health = vitals != null ? (float)vitals.Health / Mathf.Max(1, vitals.MaxHealth) : 0;
            healthFill.fillAmount = health;
            armorFill.fillAmount = vitals != null ? (float)vitals.Armor / Mathf.Max(1, vitals.MaxArmor) : 0;
            healthText.color = healthFill.color = health <= .25f ? Warning : White;
        }

        private void RefreshAmmo()
        {
            if (ammoNumbers != null) ammoNumbers.SetActive(ammo != null);
            magazineText.text = ammo != null ? ammo.Magazine.ToString("00") : "--";
            reserveText.text = ammo != null ? (ammo.InfiniteReserve ? "∞" : ammo.Reserve.ToString()) : "--";
            magazineText.color = ammo != null && ammo.IsLow && !ammo.IsReloading ? Warning : White;
            ammoFill.fillAmount = ammo != null ? (float)ammo.Magazine / ammo.Capacity : 0;
            ammoFill.color = magazineText.color;
            reloadTrack.SetActive(ammo != null && ammo.IsReloading);
            ammoStatus.text = ammo == null ? "" : ammo.IsReloading ? (weapon.IsTaser ? "充能中" : "换弹中") :
                ammo.Magazine == 0 ? "弹匣已空" : ammo.IsLow ? "弹药不足" : "";
            ammoStatus.color = ammo != null && ammo.IsLow && !ammo.IsReloading ? Warning : Accent;
        }

        protected virtual void LateUpdate()
        {
            if (!IsOpen || !HasContext || scene.Player == null) return;
            if (lastSafeArea != Screen.safeArea || lastScreenSize != new Vector2Int(Screen.width, Screen.height)) RefreshSafeArea();
            bool blocked = scene.IsInputBlocked;
            tacticalHud.SetObserver(scene.Player.ViewCamera != null ? scene.Player.ViewCamera.GetComponent<CharacterVision>() : null,
                vitals != null && vitals.IsAlive);
            tacticalHud.Present(scene.HudParticipants, scene.LocalHudId);
            var grenades = weapon != null ? weapon.Throwables : null;
            var world = grenades != null ? grenades.World : null;
            smokeVeil.color = new Color(.42f, .43f, .42f, blocked || world == null ? 0 : world.SmokeOpacity);
            flashVeil.color = new Color(1, 1, 1, blocked || world == null ? 0 : world.FlashOpacity);
            int seconds = Mathf.FloorToInt(scene.ElapsedSeconds);
            if (seconds != lastSecond)
            {
                timerText.text = seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
                lastSecond = seconds;
            }
            if (blocked != wasBlocked)
            {
                if (sessionCard != null) sessionCard.SetActive(!blocked);
                wasBlocked = blocked;
            }
            if (ammo != null && ammo.IsReloading) reloadFill.fillAmount = ammo.ReloadProgress;
            bool showAim = !blocked && Application.isFocused && scene.Player.InputEnabled
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
            crosshair.gameObject.SetActive(false);
            if (weapon != null) combat.Present(scene.Player, weapon,
                scene.Player.ViewCamera.GetComponent<TopDownCameraController>().ScopeBlend, showAim,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera);
        }

        private void RefreshSafeArea()
        {
            lastSafeArea = Screen.safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            safeArea.anchorMin = new Vector2(lastSafeArea.xMin / Screen.width, lastSafeArea.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(lastSafeArea.xMax / Screen.width, lastSafeArea.yMax / Screen.height);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }

        protected override void OnClose()
        {
            crosshair.gameObject.SetActive(false);
            if (combat != null) combat.gameObject.SetActive(false);
            if (tacticalHud != null) tacticalHud.gameObject.SetActive(false);
            if (ammo != null) ammo.Changed -= RefreshAmmo;
            if (weapon != null) { weapon.WeaponChanged -= BindWeapon; weapon.TargetHit -= combat.ShowHit; }
            scene = null; weapon = null; vitals = null; ammo = null;
        }

        private Image CreateVeil(string label)
        {
            var go = new GameObject(label, typeof(RectTransform)); go.layer = gameObject.layer;
            go.transform.SetParent(transform, false); go.transform.SetSiblingIndex(1);
            var image = go.AddComponent<Image>(); image.raycastTarget = false; image.color = Color.clear;
            image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            return image;
        }
    }
}
