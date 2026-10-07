using System;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

namespace OperationBlacktide.Client.Features.Training
{
    [DefaultExecutionOrder(-50)]
    public sealed class TrainingSceneController : MonoBehaviour
    {
        [SerializeField] private TrainingCharacterController playerPrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private TrainingCameraController followCamera;
        private UIRoot uiRoot;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool returning;
        private LobbyLoadoutCatalog loadoutCatalog;
        private LobbyLoadoutPreview appearance;
        private string agentId;
        public TrainingWeaponSelection Weapons { get; private set; }
        public TrainingCharacterController Player { get; private set; }
        public TrainingVitals Vitals { get; private set; }
        public LobbyTeam PlayerTeam { get; private set; } = LobbyTeam.CT;
        public float TrainingSeconds { get; private set; }
        public bool IsInputBlocked => returning || (GameSceneFlow.Instance != null && GameSceneFlow.Instance.IsLoading)
            || (UIManager.Instance != null && UIManager.Instance.GetStackCount(UILayer.Popup) > 0);

        private void Awake()
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            if (UIRoot.Instance == null) new GameObject("UIRoot").AddComponent<UIRoot>();
            uiRoot = UIRoot.Instance;
        }

        private void Start()
        {
            Player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
            Player.name = "Training Player";
            Vitals = Player.GetComponent<TrainingVitals>();
            if (Vitals == null) Vitals = Player.gameObject.AddComponent<TrainingVitals>();
            Vitals.Died += OnPlayerDied;
            Player.Initialize(followCamera.GetComponent<Camera>(), spawnPoint.position, spawnPoint.rotation);
            CreateCharacterVisual();
            TrainingTarget.CreateRange(Player.transform.position, loadoutCatalog, PlayerTeam);
            followCamera.Follow(Player.transform);
            UIManager.Instance.CloseAll(UILayer.Popup);
            UIManager.Instance.CloseAll(UILayer.Normal);
            UIManager.Instance.Push(UIPanelId.Training, this);
            uiRoot.UnhandledBackRequested += OpenTrainingMenu;
            RefreshInput();
        }

        private void CreateCharacterVisual()
        {
            var catalog = Resources.Load<LobbyLoadoutCatalog>("Loadout/LobbyCatalog");
            loadoutCatalog = catalog;
            var auth = GameSceneFlow.Instance != null ? GameSceneFlow.Instance.Auth : null;
            string agent = "ctm_sas";
            string weapon = "weapon_rif_m4a1_silencer";
            if (auth != null && auth.Session.IsLoggedIn)
            {
                var loadout = new LobbyLoadoutStore(catalog, auth.Session.UserId);
                PlayerTeam = loadout.Team;
                agent = loadout.Current.agentId;
                weapon = loadout.Equipped(loadout.Team, "rifle.main").id;
            }
            var visual = new GameObject("Character Visual");
            visual.transform.SetParent(Player.transform, false);
            // Reuse equipment attachments, then replace the menu animator with gameplay locomotion.
            agentId = agent;
            Weapons = new TrainingWeaponSelection(catalog.weapons, weapon);
            appearance = visual.AddComponent<LobbyLoadoutPreview>();
            appearance.Show(catalog, agent, weapon);
            Player.gameObject.AddComponent<TrainingCharacterAnimator>().Initialize(Player, appearance.Animator);
            Player.gameObject.AddComponent<TrainingFootstepAudio>().Initialize(Player);
            foreach (var child in Player.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 2; // Ignore Raycast: camera obstruction probes must ignore the player.
            Player.gameObject.AddComponent<TrainingWeaponController>().Initialize(Player, appearance.EquippedWeapon, weapon);
        }

        private void Update()
        {
            RefreshInput();
            if (Player != null && Player.InputEnabled && Application.isFocused && Time.timeScale > 0 &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                for (int slot = 1; slot <= 4; slot++)
                    if (Input.GetKeyDown(KeyCode.Alpha0 + slot) || Input.GetKeyDown(KeyCode.Keypad0 + slot)) { EquipSlot(slot); break; }
            }
            if (!IsInputBlocked && Application.isFocused) TrainingSeconds += Time.deltaTime;
            if (Vitals != null && !Vitals.IsAlive && !IsInputBlocked) Player.Respawn();
        }

        public bool EquipSlot(int slot)
        {
            if (IsInputBlocked || Player == null || !Player.InputEnabled || Weapons == null) return false;
            return EquipWeapon(Weapons.Next(slot));
        }

        public bool EquipFromMenu(string id)
        {
            if (returning || Player == null || Vitals == null || !Vitals.IsAlive ||
                (GameSceneFlow.Instance != null && GameSceneFlow.Instance.IsLoading) ||
                UIManager.Instance == null || !UIManager.Instance.TryGetTopPanelId(UILayer.Popup, out string top) ||
                top != PanelIds.Key(UIPanelId.TrainingMenu)) return false;
            return EquipWeapon(id);
        }

        private bool EquipWeapon(string id)
        {
            var item = loadoutCatalog.Weapon(id);
            if (item == null || item.prefab == null || TrainingWeaponSelection.SlotOf(item) == 0) return false;
            var gun = Player.GetComponent<TrainingWeaponController>();
            if (gun.WeaponId == id) return true;
            gun.PrepareEquipmentChange();
            appearance.Show(loadoutCatalog, agentId, id);
            bool hasFiring = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons").Find(id) != null;
            Player.GetComponent<TrainingCharacterAnimator>().Initialize(Player, appearance.Animator, !hasFiring);
            foreach (var child in Player.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
            Weapons.Select(id);
            gun.Initialize(Player, appearance.EquippedWeapon, id);
            return true;
        }

        private void OnPlayerDied() { RefreshInput(); }

        private void RefreshInput()
        {
            bool blocked = IsInputBlocked;
            if (Player != null) Player.InputEnabled = !blocked && (Vitals == null || Vitals.IsAlive);
            followCamera.InputEnabled = !blocked;
            Cursor.visible = blocked || !Application.isFocused || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
            Cursor.lockState = blocked || !Application.isFocused ? CursorLockMode.None : CursorLockMode.Confined;
        }

        public void OpenTrainingMenu()
        {
            if (IsInputBlocked) return;
            UIManager.Instance.Push(UIPanelId.TrainingMenu, this);
            RefreshInput();
        }

        public void RequestReturnToLobby()
        {
            if (returning || (GameSceneFlow.Instance != null && GameSceneFlow.Instance.IsLoading)) return;
            if (IsInputBlocked && (!UIManager.Instance.TryGetTopPanelId(UILayer.Popup, out string top) ||
                top != PanelIds.Key(UIPanelId.TrainingMenu))) return;
            UIManager.Instance.Push(UIPanelId.ReturnToLobby, (Action)ReturnToLobby);
            RefreshInput();
        }

        private void ReturnToLobby()
        {
            if (returning || (GameSceneFlow.Instance != null && GameSceneFlow.Instance.IsLoading)) return;
            if (GameSceneFlow.Instance != null)
            {
                GameSceneFlow.Instance.ReturnToLobby();
                returning = GameSceneFlow.Instance.IsLoading;
            }
            else
            {
                // Direct editor play has no account session; use the normal login entry.
                SceneManager.LoadSceneAsync(GameSceneFlow.LoginScenePath);
                returning = true;
            }
            RefreshInput();
        }

        private void OnDestroy()
        {
            if (uiRoot != null) uiRoot.UnhandledBackRequested -= OpenTrainingMenu;
            if (Vitals != null) Vitals.Died -= OnPlayerDied;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
    }
}
