using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Gameplay.Cameras;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using System;
using System.Collections.Generic;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.GameLogic;
using OperationBlacktide.Simulation;
using OperationBlacktide.Client.Gameplay.UI;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Client.Gameplay.Avatars;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OperationBlacktide.Client.Features.Training
{
    [DefaultExecutionOrder(-50)]
    public sealed class TrainingSceneController : MonoBehaviour, ICombatHudContext
    {
        [SerializeField] private CharacterMotor playerPrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private TopDownCameraController followCamera;
        private UIRoot uiRoot;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool returning;
        private EquipmentCatalog loadoutCatalog;
        private BattleAvatar appearance;
        private string agentId;
        private TrainingTarget[] hudTargets = Array.Empty<TrainingTarget>();
        private readonly List<CombatHudParticipant> hudParticipants = new List<CombatHudParticipant>(4);
        public int LocalHudId => 0;
        public IReadOnlyList<CombatHudParticipant> HudParticipants
        {
            get
            {
                hudParticipants.Clear();
                if (Player == null) return hudParticipants;
                var gun = Player.GetComponent<WeaponController>();
                hudParticipants.Add(new CombatHudParticipant {
                    Id = LocalHudId, Name = "自己", Team = (int)PlayerTeam, Health = Vitals.Health, MaxHealth = Vitals.MaxHealth,
                    HasSnapshot = true, Connected = true, Portrait = loadoutCatalog?.Agent(agentId)?.portrait,
                    Position = Player.transform.position, Yaw = Player.transform.eulerAngles.y, WorldTransform = Player.transform,
                    Status = gun?.Ammo?.IsReloading == true ? "换弹中" : "存活"
                });
                for (int i = 0; i < hudTargets.Length; i++)
                {
                    var target = hudTargets[i]; if (target == null) continue;
                    hudParticipants.Add(new CombatHudParticipant {
                        Id = i + 1, Name = "靶兵 " + (i + 1), Team = (int)target.Team, Health = Mathf.CeilToInt(target.Health), MaxHealth = 100,
                        HasSnapshot = true, Connected = true, Portrait = loadoutCatalog?.Agent(target.Team == EquipmentTeam.CT ? "ctm_sas" : "tm_phoenix")?.portrait,
                        Position = target.transform.position, Yaw = target.transform.eulerAngles.y, WorldTransform = target.transform, Status = "训练靶兵"
                    });
                }
                return hudParticipants;
            }
        }
        public WeaponSelection Weapons { get; private set; }
        public CharacterMotor Player { get; private set; }
        public CharacterVitals Vitals { get; private set; }
        public EquipmentTeam PlayerTeam { get; private set; } = EquipmentTeam.CT;
        public GameLogic Logic { get; private set; }
        public Character Character { get; private set; }
        public float TrainingSeconds => (float)(Logic?.Clock.ElapsedSeconds ?? 0);
        float ICombatHudContext.ElapsedSeconds => TrainingSeconds;
        string ICombatHudContext.StatusText => IsInputBlocked ? "训练菜单" : "自由训练";
        void ICombatHudContext.OpenMenu() => OpenTrainingMenu();
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
            Logic = new GameLogic(GameConfig.LoadMode("training"), true);
            if (Logic.Respawn != null) Logic.Respawn.Requested += OnRespawnRequested;
            Player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
            Player.name = "Training Player";
            Vitals = Player.GetComponent<CharacterVitals>();
            if (Vitals == null) Vitals = Player.gameObject.AddComponent<CharacterVitals>();
            Character = Logic.World.Add(new Character(1, maxHealth: Vitals.MaxHealth, maxArmor: Vitals.MaxArmor));
            Player.Movement = Character.Movement;
            Vitals.Bind(Character.Vitals);
            Vitals.Died += OnPlayerDied;
            Player.Respawned += OnPlayerRespawned;
            Player.FellOutOfWorld += Player.Respawn;
            Player.Initialize(followCamera.GetComponent<Camera>(), spawnPoint.position, spawnPoint.rotation);
            CreateCharacterVisual();
            TrainingTarget.CreateRange(Player.transform.position, loadoutCatalog, PlayerTeam);
            hudTargets = FindObjectsOfType<TrainingTarget>();
            Array.Sort(hudTargets, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            followCamera.Follow(Player.transform);
            UIManager.Instance.CloseAll(UILayer.Popup);
            UIManager.Instance.CloseAll(UILayer.Normal);
            UIManager.Instance.Push(UIPanelId.Training, this);
            uiRoot.UnhandledBackRequested += OpenTrainingMenu;
            RefreshInput();
        }

        private void CreateCharacterVisual()
        {
            var catalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
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
            // Battle appearance owns its model components independently of lobby presentation.
            agentId = agent;
            Weapons = new WeaponSelection(catalog.weapons, weapon);
            appearance = visual.AddComponent<BattleAvatar>();
            appearance.Show(catalog, agent, weapon);
            Player.gameObject.AddComponent<CharacterAnimator>().Initialize(Player, appearance.Animator);
            Player.gameObject.AddComponent<FootstepAudio>().Initialize(Player);
            foreach (var child in Player.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 2; // Ignore Raycast: camera obstruction probes must ignore the player.
            var controller = Player.gameObject.AddComponent<WeaponController>();
            controller.BindEquipment(Character.Equipment);
            controller.InfiniteAmmoReserve = Logic.Supply?.InfiniteAmmoReserve ?? false;
            controller.InfiniteThrowables = Logic.Supply?.InfiniteThrowables ?? false;
            controller.AutoRechargeTaser = Logic.Supply?.AutoRechargeTaser ?? false;
            controller.Initialize(Player, appearance.EquippedWeapon, weapon);
            Vitals.RefreshRenderers();
        }

        private void Update()
        {
            RefreshInput();
            if (Player != null && Player.InputEnabled && Application.isFocused && Time.timeScale > 0 &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                bool selected = false;
                for (int slot = 1; slot <= 4; slot++)
                    if (Input.GetKeyDown(KeyCode.Alpha0 + slot) || Input.GetKeyDown(KeyCode.Keypad0 + slot))
                    { EquipSlot(slot); selected = true; break; }
                if (!selected) CycleWeapon(Input.mouseScrollDelta.y);
            }
            if (!IsInputBlocked && Application.isFocused) Logic.Tick(Time.deltaTime);
        }

        public bool EquipSlot(int slot)
        {
            if (IsInputBlocked || Player == null || !Player.InputEnabled || Weapons == null) return false;
            return EquipWeapon(Weapons.Next(slot));
        }

        public bool CycleWeapon(float scroll)
        {
            if (scroll == 0 || IsInputBlocked || Player == null || !Player.InputEnabled || Weapons == null ||
                !followCamera.IsFirstPerson || !followCamera.InputEnabled) return false;
            return EquipWeapon(Weapons.Cycle(scroll > 0 ? -1 : 1));
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
            if (item == null || item.prefab == null || WeaponSelection.SlotOf(item) == 0) return false;
            var gun = Player.GetComponent<WeaponController>();
            if (gun.WeaponId == id) return true;
            gun.PrepareEquipmentChange();
            appearance.Show(loadoutCatalog, agentId, id);
            bool hasFiring = Resources.Load<WeaponCatalog>("Gameplay/Weapons").Find(id) != null;
            Player.GetComponent<CharacterAnimator>().Initialize(Player, appearance.Animator, !hasFiring);
            foreach (var child in Player.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
            Weapons.Select(id);
            gun.Initialize(Player, appearance.EquippedWeapon, id);
            Vitals.RefreshRenderers();
            return true;
        }

        private void OnPlayerDied() { RefreshInput(); }
        private void OnRespawnRequested(Character character) { if (character == Character) Player.Respawn(); }

        private void OnPlayerRespawned()
        {
            Vitals.Restore();
            Player.GetComponent<WeaponController>()?.ResetEquipment();
            Player.GetComponent<FootstepAudio>()?.ResetMotion();
        }

        private void RefreshInput()
        {
            bool blocked = IsInputBlocked;
            if (Player != null) Player.InputEnabled = !blocked && (Vitals == null || Vitals.IsAlive);
            followCamera.InputEnabled = !blocked;
            Cursor.visible = blocked || !Application.isFocused || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
            Cursor.lockState = blocked || !Application.isFocused ? CursorLockMode.None : followCamera.GameplayCursorLock;
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
                var flow = new GameObject("Game").AddComponent<GameSceneFlow>();
                flow.ReturnToLobby();
                returning = flow.IsLoading;
            }
            RefreshInput();
        }

        private void OnDestroy()
        {
            if (uiRoot != null) uiRoot.UnhandledBackRequested -= OpenTrainingMenu;
            if (Vitals != null) Vitals.Died -= OnPlayerDied;
            if (Player != null)
            {
                Player.Respawned -= OnPlayerRespawned;
                Player.FellOutOfWorld -= Player.Respawn;
            }
            if (Logic != null)
            {
                if (Logic.Respawn != null) Logic.Respawn.Requested -= OnRespawnRequested;
                Logic.Dispose();
            }
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
    }
}
