using OperationBlacktide.Client.Gameplay.Combat;
using System.Collections.Generic;
using OperationBlacktide.Client.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Training
{
    public sealed class TrainingMenuPanel : UIPanel
    {
        [SerializeField] private GameObject home, weaponPicker;
        [SerializeField] private Button continueButton, weaponsButton, returnButton, backButton;
        [SerializeField] private Button[] categoryButtons;
        [SerializeField] private RectTransform content;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private Button optionTemplate;
        private readonly List<Button> options = new List<Button>();
        private TrainingSceneController scene;
        private int category = 1;

        protected override void OnOpen(object args)
        {
            if (args is TrainingSceneController owner) scene = owner;
            if (scene == null) return;
            SetPage(false);
            AddButton(continueButton, () => UIManager.Instance.Close(this));
            AddButton(weaponsButton, () => ShowWeapons(scene.Weapons.CurrentSlot));
            AddButton(returnButton, scene.RequestReturnToLobby);
            AddButton(backButton, () => { SetPage(false); ClearOptions(); });
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                int slot = i + 1;
                AddButton(categoryButtons[i], () => ShowWeapons(slot));
            }
        }

        public void ShowWeapons(int slot)
        {
            if (scene == null || slot < 1 || slot > 4) return;
            category = slot;
            SetPage(true);
            ClearOptions();
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                bool active = i + 1 == slot;
                BlackGoldTheme.Select(categoryButtons[i], active);
                categoryButtons[i].GetComponentInChildren<Text>().color = active ? BlackGoldTheme.GoldLight : BlackGoldTheme.Muted;
            }
            var gun = scene.Player.GetComponent<WeaponController>();
            var catalog = Resources.Load<WeaponCatalog>("Gameplay/Weapons");
            foreach (var item in scene.Weapons.Options(slot))
            {
                var button = Instantiate(optionTemplate, content);
                button.name = item.id;
                button.gameObject.SetActive(true);
                button.transform.Find("Name").GetComponent<Text>().text = item.displayName;
                var icon = button.transform.Find("Icon").GetComponent<Image>();
                var profile = catalog.Find(item.id);
                icon.sprite = profile != null && profile.hudIcon != null ? profile.hudIcon : item.thumbnail;
                icon.color = profile != null && profile.hudIcon != null ? Color.white : new Color(1, 1, 1, .95f);
                icon.enabled = icon.sprite != null;
                bool current = gun.WeaponId == item.id;
                BlackGoldTheme.Select(button, current);
                button.transform.Find("State").GetComponent<Text>().text = current ? "已装备" : "";
                string id = item.id;
                button.onClick.AddListener(() =>
                {
                    float position = scroll.verticalNormalizedPosition;
                    if (scene.EquipFromMenu(id)) { ShowWeapons(category); scroll.verticalNormalizedPosition = position; }
                });
                options.Add(button);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 1;
        }

        private void SetPage(bool selectingWeapon)
        {
            home.SetActive(!selectingWeapon); weaponPicker.SetActive(selectingWeapon);
            var sheet = (RectTransform)home.transform.parent;
            sheet.anchorMin = sheet.anchorMax = new Vector2(selectingWeapon ? .5f : 0, .5f);
            sheet.anchoredPosition = selectingWeapon ? Vector2.zero : new Vector2(324, 0);
            sheet.sizeDelta = selectingWeapon ? new Vector2(680, 760) : new Vector2(520, 408);
            sheet.GetComponent<UIGlassEffect>().SetKind(selectingWeapon ? UIGlassEffect.Kind.Panel : UIGlassEffect.Kind.Rail);
            var title = sheet.Find("Title").GetComponent<Text>();
            title.text = selectingWeapon ? "切换武器" : "训练菜单";
            title.rectTransform.sizeDelta = new Vector2(selectingWeapon ? 580 : 416, 54);
            var accent = (RectTransform)sheet.Find("Accent");
            accent.sizeDelta = new Vector2(selectingWeapon ? 580 : 416, 1);
            BlackGoldTheme.Select(continueButton, !selectingWeapon);
        }

        private void ClearOptions()
        {
            foreach (var button in options)
            {
                button.onClick.RemoveAllListeners();
                button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }
            options.Clear();
        }

        protected override void OnClose() { ClearOptions(); }
    }
}
