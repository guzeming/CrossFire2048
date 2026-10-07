using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Lobby
{
    public sealed class LobbyLoadoutView : MonoBehaviour
    {
        public LobbyLoadoutCatalog catalog;
        public Text roleName, selectedName, selectionHint, statusText;
        public Button ctButton, tButton, chooseAgentButton, closeAgentsButton, equipButton, bothTeamsButton, displayButton, inspectButton;
        public Button[] categories;
        public GameObject teamControls, agentsPanel, equipmentPage, playControls;
        public RectTransform agentContent, slotContent, optionContent;
        public LobbyLoadoutRow agentTemplate, slotTemplate, optionTemplate;
        private LobbyLoadoutPreview preview;
        private LoadoutCategory category = LoadoutCategory.Rifles;
        private string selectedSlot, selectedWeapon;
        private int tab;
        private bool initialized;
        public LobbyLoadoutStore Store { get; private set; }
        public string SelectedWeaponId => selectedWeapon;

        public void Open(string userId)
        {
            if (catalog == null || string.IsNullOrEmpty(userId)) return;
            if (!initialized)
            {
                ctButton.onClick.AddListener(() => SelectTeam(LobbyTeam.CT));
                tButton.onClick.AddListener(() => SelectTeam(LobbyTeam.T));
                chooseAgentButton.onClick.AddListener(() => ToggleAgents(true));
                closeAgentsButton.onClick.AddListener(() => ToggleAgents(false));
                equipButton.onClick.AddListener(EquipSelected);
                bothTeamsButton.onClick.AddListener(EquipBoth);
                displayButton.onClick.AddListener(DisplaySelected);
                inspectButton.onClick.AddListener(() => preview?.Inspect());
                for (int i = 0; i < categories.Length; i++)
                {
                    int index = i;
                    categories[i].onClick.AddListener(() => SelectCategory((LoadoutCategory)index));
                }
                initialized = true;
            }
            Store = new LobbyLoadoutStore(catalog, userId);
            preview = FindObjectOfType<LobbyLoadoutPreview>();
            if (preview == null)
            {
                var character = GameObject.Find("CT_SAS_Character");
                if (character != null) preview = character.AddComponent<LobbyLoadoutPreview>();
            }
            selectedSlot = "rifle.main";
            category = LoadoutCategory.Rifles;
            selectedWeapon = Store.Equipped(Store.Team, selectedSlot).id;
            agentsPanel.SetActive(false);
            Refresh();
            SetTab(0);
        }

        public void Close()
        {
            agentsPanel.SetActive(false);
            Store = null;
            preview = null;
        }

        public void SetTab(int index)
        {
            tab = index;
            if (Store == null) return;
            equipmentPage.SetActive(index == 1);
            teamControls.SetActive(index < 2);
            chooseAgentButton.gameObject.SetActive(index == 0);
            if (index != 0) agentsPanel.SetActive(false);
            playControls.SetActive(index == 0);
            if (index == 1) PreviewSelection(); else PreviewSaved();
            Refresh();
        }

        public void SelectTeam(LobbyTeam team)
        {
            if (Store == null) return;
            Store.SelectTeam(team);
            var available = catalog.weapons.Where(w => w.Supports(team) && w.category == category).ToArray();
            if (!available.Any(w => w.slot == selectedSlot)) selectedSlot = available[0].slot;
            selectedWeapon = Store.Equipped(team, selectedSlot).id;
            statusText.text = "";
            Refresh();
            if (tab == 1) PreviewSelection(); else PreviewSaved();
        }

        public void SelectCategory(LoadoutCategory value)
        {
            category = value;
            selectedSlot = catalog.weapons.First(w => w.Supports(Store.Team) && w.category == category).slot;
            SelectSlot(selectedSlot);
        }

        public void SelectSlot(string slot)
        {
            selectedSlot = slot;
            selectedWeapon = Store.Equipped(Store.Team, slot).id;
            statusText.text = "";
            Refresh();
            PreviewSelection();
        }

        public void SelectWeapon(string id)
        {
            var weapon = catalog.Weapon(id);
            if (weapon == null || !weapon.Supports(Store.Team) || weapon.slot != selectedSlot) return;
            selectedWeapon = id;
            statusText.text = "预览中";
            RefreshOptions();
            PreviewSelection();
        }

        public void SelectAgent(string id)
        {
            if (!Store.EquipAgent(id)) return;
            Refresh();
            PreviewSaved();
        }

        public void EquipSelected()
        {
            if (!Store.EquipWeapon(selectedWeapon)) return;
            if (catalog.Weapon(selectedWeapon).held) Store.SetDisplayWeapon(selectedWeapon);
            statusText.text = "已装备至 " + Store.Team + "，配置已保存";
            Refresh();
            PreviewSelection();
        }

        private void EquipBoth()
        {
            if (!Store.EquipForBothTeams(selectedWeapon)) return;
            statusText.text = "已装备至 CT 和 T，配置已保存";
            Refresh();
        }

        private void DisplaySelected()
        {
            if (!Store.SetDisplayWeapon(selectedWeapon)) return;
            statusText.text = "已设为大厅展示武器";
            RefreshOptions();
        }

        private void ToggleAgents(bool show)
        {
            agentsPanel.SetActive(show);
            if (show) RefreshAgents();
            PreviewSaved();
        }

        private void Update()
        {
            if (Store != null && agentsPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) ToggleAgents(false);
        }

        private void PreviewSelection() => preview?.Show(catalog, Store.Current.agentId, selectedWeapon);
        private void PreviewSaved() => preview?.Show(catalog, Store.Current.agentId, Store.Current.displayWeaponId);

        private void Refresh()
        {
            roleName.text = Store.Team + " / " + catalog.Agent(Store.Current.agentId).displayName;
            Mark(ctButton, Store.Team == LobbyTeam.CT); Mark(tButton, Store.Team == LobbyTeam.T);
            for (int i = 0; i < categories.Length; i++) Mark(categories[i], i == (int)category);
            RefreshAgents();
            Clear(slotContent, slotTemplate);
            foreach (var slot in catalog.weapons.Where(w => w.Supports(Store.Team) && w.category == category).GroupBy(w => w.slot))
            {
                var equipped = Store.Equipped(Store.Team, slot.Key);
                var row = Instantiate(slotTemplate, slotContent);
                row.gameObject.SetActive(true);
                row.name = "Slot_" + slot.Key;
                row.Bind(equipped.displayName, slot.Count() > 1 ? "可替换 · " + slot.Count() + " 项" : "固定槽位", equipped.thumbnail, slot.Key == selectedSlot);
                string key = slot.Key;
                row.button.onClick.AddListener(() => SelectSlot(key));
            }
            RefreshOptions();
        }

        private void RefreshAgents()
        {
            Clear(agentContent, agentTemplate);
            foreach (var agent in catalog.agents.Where(a => a.team == Store.Team))
            {
                var row = Instantiate(agentTemplate, agentContent);
                row.gameObject.SetActive(true);
                row.name = "Agent_" + agent.id;
                row.Bind(agent.displayName, agent.id == Store.Current.agentId ? "已选择" : "选择角色", agent.portrait, agent.id == Store.Current.agentId);
                string id = agent.id;
                row.button.onClick.AddListener(() => SelectAgent(id));
            }
        }

        private void RefreshOptions()
        {
            Clear(optionContent, optionTemplate);
            foreach (var item in catalog.Options(Store.Team, selectedSlot))
            {
                var row = Instantiate(optionTemplate, optionContent);
                row.gameObject.SetActive(true);
                row.name = "Weapon_" + item.id;
                row.Bind(item.displayName, Store.IsEquipped(item) ? "已装备" : "点击预览", item.thumbnail, item.id == selectedWeapon);
                string id = item.id;
                row.button.onClick.AddListener(() => SelectWeapon(id));
            }
            var selected = catalog.Weapon(selectedWeapon);
            bool equipped = Store.IsEquipped(selected);
            selectedName.text = selected.displayName;
            bool replaceable = catalog.Options(Store.Team, selectedSlot).Length > 1;
            selectionHint.text = selectedSlot == "gear.c4" ? "T 方任务装备 · 对局中分配携带" :
                replaceable ? "替换同槽位武器 · CT / T 配置分别保存" : "此槽位固定可用 · 可在此预览装备";
            equipButton.interactable = !equipped;
            equipButton.GetComponentInChildren<Text>().text = equipped ? "已装备" : "装备至 " + Store.Team;
            bothTeamsButton.gameObject.SetActive(replaceable && selected.teams == 3);
            displayButton.gameObject.SetActive(selected.held);
            displayButton.interactable = equipped && Store.Current.displayWeaponId != selected.id;
            inspectButton.gameObject.SetActive(selected.held);
        }

        private static void Mark(Button button, bool selected) => button.GetComponentInChildren<Text>().color =
            selected ? new Color32(255, 164, 54, 255) : new Color32(169, 182, 191, 255);

        private static void Clear(Transform content, LobbyLoadoutRow template)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                if (child == template.gameObject) continue;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
        }
    }
}
