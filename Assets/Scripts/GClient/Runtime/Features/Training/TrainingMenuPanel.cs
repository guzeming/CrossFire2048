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
        [SerializeField] private Text selectedText;
        private readonly List<Button> options = new List<Button>();
        private TrainingSceneController scene;
        private int category = 1;
        private static readonly Color Accent = new Color32(255, 164, 54, 255);
        private static readonly Color Ink = new Color32(17, 25, 31, 255);
        private static readonly Color Row = new Color32(43, 54, 63, 245);

        protected override void OnOpen(object args)
        {
            if (args is TrainingSceneController owner) scene = owner;
            if (scene == null) return;
            home.SetActive(true); weaponPicker.SetActive(false);
            AddButton(continueButton, () => UIManager.Instance.Close(this));
            AddButton(weaponsButton, () => ShowWeapons(scene.Weapons.CurrentSlot));
            AddButton(returnButton, scene.RequestReturnToLobby);
            AddButton(backButton, () => { home.SetActive(true); weaponPicker.SetActive(false); ClearOptions(); });
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
            home.SetActive(false); weaponPicker.SetActive(true);
            ClearOptions();
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                bool active = i + 1 == slot;
                categoryButtons[i].image.color = active ? Accent : Row;
                categoryButtons[i].GetComponentInChildren<Text>().color = active ? Ink : Color.white;
            }
            var gun = scene.Player.GetComponent<TrainingWeaponController>();
            var catalog = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons");
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
                button.image.color = current ? new Color32(80, 64, 42, 255) : Row;
                button.transform.Find("State").GetComponent<Text>().text = current ? "已装备" : "装备";
                string id = item.id;
                button.onClick.AddListener(() =>
                {
                    float position = scroll.verticalNormalizedPosition;
                    if (scene.EquipFromMenu(id)) { ShowWeapons(category); scroll.verticalNormalizedPosition = position; }
                });
                options.Add(button);
            }
            var loadout = Resources.Load<OperationBlacktide.Client.Features.Lobby.LobbyLoadoutCatalog>("Loadout/LobbyCatalog");
            var equipped = loadout.Weapon(gun.WeaponId);
            selectedText.text = "当前装备  " + equipped.displayName + (gun.CanAttack ? "" : "  ·  攻击功能待开放");
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 1;
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
