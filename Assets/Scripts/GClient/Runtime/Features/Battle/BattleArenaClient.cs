using OperationBlacktide.Client.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Cameras;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Protocol;
using OperationBlacktide.Simulation;
using OperationBlacktide.Client.Gameplay.Avatars;
using OperationBlacktide.Client.Gameplay.UI;
using OperationBlacktide.Client.Gameplay.Vision;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Battle
{
    public sealed class BattleArenaClient : MonoBehaviour, ISceneLoadParticipant
    {
        [SerializeField] private Font font;
        [SerializeField] private Material shotMaterial;
        private Material missMaterial, hitMaterial;
        private sealed class Actor
        {
            public CharacterController Motor;
            public CharacterMotor AnimationMotor;
            public GameObject Visual;
            public BattleState State;
            public readonly List<(int tick, BattleState state)> Samples = new List<(int, BattleState)>();
        }
        private readonly Dictionary<int, Actor> actors = new Dictionary<int, Actor>();
        private readonly List<BattleInput> pending = new List<BattleInput>();
        private BattleConnection connection;
        private Camera cameraView;
        private CharacterVision vision;
        private CameraCutaway cutaway;
        private UIRoot uiRoot;
        private static readonly Vector3 CameraOffset = Quaternion.Euler(60, 0, 0) * Vector3.back * 16;
        private Text status;
        private CombatTacticalHud tacticalHud;
        private readonly List<CombatHudParticipant> hudParticipants = new List<CombatHudParticipant>();
        private EquipmentCatalog hudCatalog;
        private BattleState predicted;
        private uint sequence;
        private int latestTick;
        private float accumulator, snapshotAt, connectAt;
        private InputButtons edges;
        private bool failed;
        private string message = "正在连接战斗服…";
        public bool Ready => connection?.Ready == true && actors.ContainsKey(connection.PlayerId);
        public BattleState LocalState => predicted;
        public bool IsSceneReady => Ready || failed || connection == null;
        public BattleState AuthoritativeState => Ready ? actors[connection.PlayerId].State : default;
        public int SnapshotCount { get; private set; }
        public int ShotCount { get; private set; }
        public int PlayerCount => actors.Count;
        public int BotCount => actors.Values.Count(a => a.State.IsBot);
        public bool ShotPresentationReady => shotMaterial != null && shotMaterial.shader != null && shotMaterial.shader.isSupported;
        // Harness may provide commands; gameplay still uses the normal network/prediction path.
        public Func<BattleInput> InputProvider { get; set; }
        public void SetFont(Font value) { font = value; }
        public void SetShotMaterial(Material value) { shotMaterial = value; }

        private void Start()
        {
            Application.runInBackground = true;
            // UIRoot survives scene loads; the lobby must release both its visuals and input blockers.
            UIManager.Instance?.CloseAll(UILayer.Popup);
            UIManager.Instance?.CloseAll(UILayer.Normal);
            uiRoot = UIRoot.Instance;
            if (uiRoot != null) uiRoot.UnhandledBackRequested += ReturnToLobby;
            var map = ArenaMap.Initialize();
            cameraView = new GameObject("Battle Camera").AddComponent<Camera>();
            cameraView.tag = "MainCamera"; cameraView.fieldOfView = 48;
            cameraView.nearClipPlane = .08f; cameraView.farClipPlane = 300;
            cameraView.transform.SetPositionAndRotation(map.Spawn(0) + Vector3.up + CameraOffset, Quaternion.Euler(60, 0, 0));
            cameraView.backgroundColor = new Color(.035f, .05f, .07f); cameraView.clearFlags = CameraClearFlags.SolidColor;
            cameraView.gameObject.AddComponent<AudioListener>();
            vision = cameraView.gameObject.AddComponent<CharacterVision>();
            cutaway = cameraView.gameObject.AddComponent<CameraCutaway>();
            if (ShotPresentationReady)
            {
                missMaterial = new Material(shotMaterial) { color = new Color(1, .8f, .35f) };
                hitMaterial = new Material(shotMaterial) { color = new Color(1, .35f, .1f) };
            }
            else Debug.LogError("[Battle] Shot material is missing from the client build.");
            BuildHud(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            BattleJoinResponse ticket = BattleGateway.ConsumeTicket();
            if (ticket != null) Connect(ticket);
            else { message = "请从大厅进入联机测试场"; }
        }
        public void Connect(BattleJoinResponse ticket)
        {
            connection?.Dispose(); connection = new BattleConnection(); connectAt = Time.unscaledTime; failed = false;
            connection.Snapshot += OnSnapshot;
            connection.Shot += OnShot;
            connection.Disconnected += reason =>
            {
                if (failed) return;
                failed = true;
                Debug.LogWarning("[Battle] Disconnected: " + reason);
                message = "战斗连接已断开，请按 Esc 返回大厅重新进入";
            };
            try { connection.Connect(ticket.Host, ticket.Port, ticket.Ticket); }
            catch (Exception) { failed = true; message = "无法连接战斗服。按 Esc 返回大厅"; }
        }
        private void Update()
        {
            tacticalHud?.SetObserver(null, false);
            connection?.Poll();
            if (connection != null && !Ready && !failed && Time.unscaledTime - connectAt > 10)
            { failed = true; message = "战斗服连接超时，请检查服务器。按 Esc 返回大厅"; connection.Dispose(); }
            if (uiRoot == null && Input.GetKeyDown(KeyCode.Escape)) ReturnToLobby();
            if (Ready && !failed)
            {
                if (GameSceneFlow.Instance?.IsLoading != true)
                {
                    if (Input.GetKeyDown(KeyCode.R)) edges |= InputButtons.Reload;
                    if (Input.GetKeyDown(KeyCode.Space)) edges |= InputButtons.Jump;
                }
                accumulator += Mathf.Min(Time.unscaledDeltaTime, .1f);
                while (!failed && accumulator >= BattleProtocol.Step)
                { accumulator -= BattleProtocol.Step; PredictInput(); }
                // PredictInput may close a stalled connection and clear PlayerId in this frame.
                if (failed || !Ready) { if (status != null) status.text = message; return; }
                InterpolateRemote();
                var actor = actors[connection.PlayerId];
                Vector3 focus = actor.Motor.transform.position;
                cameraView.transform.position = Vector3.Lerp(cameraView.transform.position, focus + Vector3.up + CameraOffset, 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime));
                var state = actor.State;
                message = "沙 2 · 联机测试场  /  真人 " + (actors.Count - BotCount) + " · 人机 " + BotCount + "  /  " + state.Team + "  /  " + connection.Ping + " ms\n" +
                    "生命 " + state.Health + "     M4  " + state.Ammo + " / " + state.Reserve +
                    (state.Health <= 0 ? "    等待重生" : state.ReloadTicks > 0 ? "    换弹中" : "");
            }
            if (status != null) status.text = message;
            tacticalHud?.SetObserver(vision, Ready && !failed && AuthoritativeState.Health > 0);
        }
        private void PredictInput()
        {
            Actor local = actors[connection.PlayerId];
            BattleInput command = InputProvider != null ? InputProvider() : SampleInput(local);
            command.Sequence = ++sequence; pending.Add(command);
            if (pending.Count > 256) { failed = true; message = "同步超时，请返回大厅重新入房"; connection.Dispose(); return; }
            Vector3 before = local.Motor.transform.position;
            CharacterSimulation.Step(local.Motor, ref predicted, command);
            local.AnimationMotor.SetPresentationVelocity((local.Motor.transform.position - before) / BattleProtocol.Step);
            connection.Send(pending.Skip(Math.Max(0, pending.Count - 3)).ToArray());
        }
        private BattleInput SampleInput(Actor local)
        {
            bool focused = GameSceneFlow.Instance?.IsLoading != true && Application.isFocused && !(EventSystem.current?.IsPointerOverGameObject() ?? false);
            float yaw = predicted.Yaw;
            if (focused)
            {
                Ray ray = cameraView.ScreenPointToRay(Input.mousePosition);
                var plane = new Plane(Vector3.up, local.Motor.transform.position + Vector3.up * 1.25f);
                if (plane.Raycast(ray, out float distance))
                { Vector3 d = ray.GetPoint(distance) - local.Motor.transform.position; yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
            }
            var buttons = focused ? edges : InputButtons.None; edges = InputButtons.None;
            if (focused && Input.GetMouseButton(0)) buttons |= InputButtons.Fire;
            if (focused && Input.GetKey(KeyCode.LeftShift)) buttons |= InputButtons.Sprint;
            return new BattleInput { X = focused ? Input.GetAxisRaw("Horizontal") : 0, Z = focused ? Input.GetAxisRaw("Vertical") : 0, Yaw = yaw, Buttons = buttons };
        }
        private void OnSnapshot(int tick, BattleState[] states)
        {
            if (tick <= latestTick || connection.PlayerId == 0) return;
            latestTick = tick; snapshotAt = Time.unscaledTime; SnapshotCount++;
            var present = new HashSet<int>();
            foreach (BattleState state in states)
            {
                present.Add(state.Id);
                if (!actors.TryGetValue(state.Id, out Actor actor)) actors[state.Id] = actor = CreateActor(state);
                bool newLife = actor.State.Life != state.Life;
                actor.State = state; actor.Visual.SetActive(state.Health > 0);
                if (state.Id == connection.PlayerId)
                {
                    pending.RemoveAll(c => c.Sequence <= state.Ack);
                    if (newLife) pending.Clear();
                    sequence = Math.Max(sequence, state.Ack);
                    predicted = state; CharacterSimulation.Restore(actor.Motor, state);
                    foreach (BattleInput input in pending) CharacterSimulation.Step(actor.Motor, ref predicted, input);
                }
                else
                {
                    if (newLife) { actor.Samples.Clear(); CharacterSimulation.Restore(actor.Motor, state); }
                    actor.Samples.Add((tick, state)); if (actor.Samples.Count > 16) actor.Samples.RemoveAt(0);
                }
            }
            foreach (int id in actors.Keys.Where(id => !present.Contains(id)).ToArray()) { Destroy(actors[id].Motor.gameObject); actors.Remove(id); }
            hudParticipants.Clear();
            foreach (var state in states)
                hudParticipants.Add(new CombatHudParticipant {
                    Id = state.Id, Name = (state.IsBot ? "AI " : "玩家 ") + state.Id, Team = (int)state.Team,
                    Health = state.Health, MaxHealth = 100, HasSnapshot = true, Connected = true,
                    Status = state.ReloadTicks > 0 ? "换弹中" : "存活",
                    Portrait = hudCatalog?.Agent(state.Team == BattleTeam.T ? "tm_phoenix" : "ctm_sas")?.portrait,
                    Position = new Vector3(state.X, state.Y, state.Z), Yaw = state.Yaw, WorldTransform = actors[state.Id].Motor.transform
                });
            tacticalHud.Present(hudParticipants, connection.PlayerId);
        }
        private Actor CreateActor(BattleState state)
        {
            var actor = new Actor { Motor = CharacterSimulation.CreatePawn(state.Id), State = state };
            actor.Motor.name = (state.IsBot ? "BOT " : "Player ") + state.Id + " (" + state.Team + ")";
            CharacterSimulation.Restore(actor.Motor, state);
            actor.AnimationMotor = actor.Motor.gameObject.AddComponent<CharacterMotor>(); actor.AnimationMotor.enabled = false;
            actor.Visual = new GameObject("Appearance"); actor.Visual.transform.SetParent(actor.Motor.transform, false);
            var catalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
            var appearance = actor.Visual.AddComponent<BattleAvatar>();
            appearance.Show(catalog, state.Team == BattleTeam.T ? "tm_phoenix" : "ctm_sas", "weapon_rif_m4a1_silencer");
            if (appearance.Animator != null) actor.Motor.gameObject.AddComponent<CharacterAnimator>().Initialize(actor.AnimationMotor, appearance.Animator);
            foreach (Collider collider in actor.Visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
            if (state.Id == connection.PlayerId)
            {
                cutaway.Follow(actor.Motor.transform);
                vision.Follow(actor.Motor.transform);
                cameraView.transform.position = actor.Motor.transform.position + Vector3.up + CameraOffset;
            }
            return actor;
        }
        private void InterpolateRemote()
        {
            float renderTick = latestTick + (Time.unscaledTime - snapshotAt) * 60 - 6;
            foreach (var pair in actors)
            {
                if (pair.Key == connection.PlayerId || pair.Value.Samples.Count == 0) continue;
                Actor actor = pair.Value; var samples = actor.Samples;
                var a = samples[0]; var b = samples[samples.Count - 1];
                for (int i = 1; i < samples.Count; i++) if (samples[i].tick >= renderTick) { a = samples[i - 1]; b = samples[i]; break; }
                float t = a.tick == b.tick ? 1 : Mathf.InverseLerp(a.tick, b.tick, renderTick);
                BattleState view = b.state;
                Vector3 position = Vector3.Lerp(CharacterSimulation.Position(a.state), CharacterSimulation.Position(b.state), t);
                view.X = position.x; view.Y = position.y; view.Z = position.z; view.Yaw = Mathf.LerpAngle(a.state.Yaw, b.state.Yaw, t);
                Vector3 before = actor.Motor.transform.position; CharacterSimulation.Restore(actor.Motor, view);
                actor.AnimationMotor.SetPresentationVelocity((position - before) / Mathf.Max(.001f, Time.unscaledDeltaTime));
            }
        }
        private void OnShot(int shooter, int hit, float[] points)
        {
            ShotCount++;
            if (missMaterial == null || hitMaterial == null) return;
            var go = new GameObject("Server confirmed shot"); var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2; line.SetPositions(new[] { new Vector3(points[0], points[1], points[2]), new Vector3(points[3], points[4], points[5]) });
            line.startWidth = .035f; line.endWidth = .012f;
            line.sharedMaterial = hit != 0 ? hitMaterial : missMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            Destroy(go, .08f);
        }
        private void BuildHud()
        {
            var canvas = new GameObject("Battle HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var surface = canvas.GetComponent<Canvas>(); surface.renderMode = RenderMode.ScreenSpaceCamera;
            surface.worldCamera = cameraView; surface.planeDistance = 1;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            hudCatalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
            tacticalHud = CombatTacticalHud.Create(canvas.transform, font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var map = FindObjectOfType<DemolitionMap>(); if (map != null) tacticalHud.SetSites(map.Sites);
            status = Label(canvas.transform, "Status", new Vector2(280, -170), new Vector2(1100, 90), 20);
        }
        private Text Label(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = size;
            var label = go.GetComponent<Text>(); label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize; label.color = Color.white; label.raycastTarget = false; return label;
        }
        private void OnDestroy()
        {
            if (uiRoot != null) uiRoot.UnhandledBackRequested -= ReturnToLobby;
            connection?.Dispose();
            if (missMaterial != null) Destroy(missMaterial);
            if (hitMaterial != null) Destroy(hitMaterial);
        }
        private void ReturnToLobby() { GameSceneFlow.Instance?.ReturnToLobby(); }
    }
}
