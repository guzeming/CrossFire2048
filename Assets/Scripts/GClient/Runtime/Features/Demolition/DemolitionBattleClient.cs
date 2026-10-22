using OperationBlacktide.Client.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Battle;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Gameplay.Avatars;
using OperationBlacktide.Client.Gameplay.Cameras;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Client.Gameplay.UI;
using OperationBlacktide.Client.Gameplay.Vision;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Modes.Demolition;
using OperationBlacktide.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Demolition
{
    [DefaultExecutionOrder(70)]
    public sealed class DemolitionBattleClient : MonoBehaviour, ISceneLoadParticipant
    {
        static readonly Unity.Profiling.ProfilerMarker SnapshotMarker = new Unity.Profiling.ProfilerMarker("Blacktide.Client.Snapshot");
        static readonly Unity.Profiling.ProfilerMarker PredictionMarker = new Unity.Profiling.ProfilerMarker("Blacktide.Client.Predict");
        [SerializeField] Font font;
        [SerializeField] Material shotMaterial;
        sealed class Actor
        {
            public CharacterController Motor;
            public CharacterMotor Animation;
            public CharacterAnimator Animator;
            public BattleAvatar Avatar;
            public NetworkWeaponPresentation Weapon;
            public DemolitionActorPresentation Objective;
            public DemolitionActorState State;
        }
        readonly Dictionary<int,Actor> actors=new Dictionary<int,Actor>();
        readonly List<BattleInput> pending=new List<BattleInput>();
        readonly List<GameObject> owned=new List<GameObject>();
        MatchAssignment match;
        DemolitionConnection connection;
        DemolitionSnapshot snapshot;
        Camera view;
        TopDownCameraController followCamera;
        Transform cameraTarget;
        CombatOverlay combat;
        bool canControl, fireNeedsRelease = true, aimNeedsRelease = true;
        bool firePressed, reloadPressed, jumpPressed;
        public struct Controls
        {
            public Vector2 Move, MouseDelta;
            public float WeaponScroll;
            public bool Fire, FirePressed, Reload, Jump, Sprint, Interact, Aim;
        }
        public Func<Controls> InputProvider { get; set; }
        Controls controls;
        public CharacterMotor LocalPlayer => connection != null && actors.TryGetValue(connection.PlayerId, out var actor) ? actor.Animation : null;
        public TopDownCameraController FollowCamera => followCamera;
        public CombatOverlay CombatOverlay => combat;
        EquipmentCatalog catalog;
        DemolitionPresentationAssets presentationAssets;
        DemolitionBombPresentation bombPresentation;
        DemolitionRules rules;
        BattleState predicted;
        Text heading,details,objective,scoreboard;
        Text timer, scoreA, scoreB, healthText, armorText, moneyText, ammoText, weaponText, buyBalance;
        Text feedback;
        float feedbackUntil;
        Image healthBar, weaponIcon;
        RectTransform hudSheet;
        CombatTacticalHud tacticalHud;
        readonly List<CombatHudParticipant> hudParticipants = new List<CombatHudParticipant>(10);
        readonly Dictionary<BuyItem,Button> shopButtons=new Dictionary<BuyItem,Button>();
        GameObject menu,buy,bombMarker;
        UIRoot uiRoot;
        uint sequence;
        float accumulator,connectedAt,lastSnapshot,endAt=-1;
        bool failed,returning;
        int observed;
        public bool Ready=>connection?.Ready==true&&snapshot!=null;
        public bool IsSceneReady=>Ready||failed||connection==null;
        public DemolitionSnapshot State=>snapshot;
        public void Configure(Font value,Material material){font=value;shotMaterial=material;}
        void Awake()
        {
            match=DemolitionGateway.Consume();
            if(match==null){enabled=false;return;}
            var test=FindObjectOfType<BattleArenaClient>();if(test!=null)test.enabled=false;
        }
        void Start()
        {
            if(match==null)return;
            Application.runInBackground=true;UIManager.Instance?.CloseAll(UILayer.Popup);UIManager.Instance?.CloseAll(UILayer.Normal);
            uiRoot=UIRoot.Instance;if(uiRoot!=null)uiRoot.UnhandledBackRequested+=ToggleMenu;
            ArenaMap.Initialize();var map=FindObjectOfType<DemolitionMap>();map.Validate();
            catalog=Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
            presentationAssets=Resources.Load<DemolitionPresentationAssets>("Gameplay/Demolition/Presentation");
            var ruleSource=Resources.Load<TextAsset>("Config/DemolitionRules");
            rules=ruleSource!=null?JsonUtility.FromJson<DemolitionRules>(ruleSource.text):new DemolitionRules();
            var bombObject=new GameObject("Demolition C4");owned.Add(bombObject);
            bombPresentation=bombObject.AddComponent<DemolitionBombPresentation>();bombPresentation.Initialize(presentationAssets);
            view=new GameObject("Demolition camera").AddComponent<Camera>();owned.Add(view.gameObject);
            view.tag="MainCamera";view.fieldOfView=48;view.nearClipPlane=.08f;view.farClipPlane=300;
            view.transform.rotation=Quaternion.Euler(60,0,0);view.gameObject.AddComponent<AudioListener>();
            followCamera=view.gameObject.AddComponent<TopDownCameraController>();followCamera.ExternalWeaponState=true;followCamera.InputEnabled=false;
            BuildHud();
            for(int i=0;i<map.Sites.Length;i++)Mark(map.Sites[i],i==0?"A":"B",new Color(1,.5f,.1f),3.5f);
            bombMarker=Mark(Vector3.zero,"C4",Color.red,.3f);bombMarker.SetActive(false);
            connection=new DemolitionConnection();connection.Snapshot+=OnSnapshot;connection.Shot+=OnShot;
            connection.Feedback+=text=>{feedback.text=text;feedbackUntil=Time.unscaledTime+3;feedback.transform.parent.gameObject.SetActive(true);};
            connection.Disconnected+=reason=>{failed=true;canControl=false;RefreshCursor();heading.text="战斗连接断开，可返回大厅重连";menu.SetActive(true);};
            try{connection.Connect(match);connectedAt=Time.unscaledTime;}catch(Exception){failed=true;heading.text="无法连接战斗服";menu.SetActive(true);}
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        GameObject Mark(Vector3 position,string label,Color color,float radius)
        {
            var go=new GameObject(label+" marker");go.transform.position=position+Vector3.up*.12f;owned.Add(go);
            var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=true;line.positionCount=40;line.startWidth=line.endWidth=.09f;
            line.sharedMaterial=shotMaterial;line.startColor=line.endColor=color;
            for(int i=0;i<40;i++)line.SetPosition(i,new Vector3(Mathf.Cos(i*Mathf.PI/20)*radius,0,Mathf.Sin(i*Mathf.PI/20)*radius));
            var textGo=new GameObject(label);textGo.transform.SetParent(go.transform,false);textGo.transform.localPosition=Vector3.up*.2f;
            textGo.transform.rotation=Quaternion.Euler(60,0,0);var text=textGo.AddComponent<TextMesh>();text.text=label;text.font=font;text.fontSize=64;text.characterSize=.1f;text.anchor=TextAnchor.MiddleCenter;text.color=color;
            text.GetComponent<MeshRenderer>().sharedMaterial=font.material;return go;
        }
        void Update()
        {
            if(hudSheet!=null)DemolitionUI.Fit(hudSheet,0);
            if(feedback!=null&&Time.unscaledTime>feedbackUntil)feedback.transform.parent.gameObject.SetActive(false);
            if(match==null||connection==null)return;connection.Poll();
            if(returning)return;
            if(!failed&&(!Ready?Time.unscaledTime-connectedAt>15:Time.unscaledTime-lastSnapshot>10))
            {failed=true;canControl=false;RefreshCursor();heading.text="战斗连接超时，请返回大厅重连";menu.SetActive(true);connection.Dispose();}
            if(!Ready||failed)return;
            bool loading=GameSceneFlow.Instance?.IsLoading==true;
            if(!loading&&Input.GetKeyDown(KeyCode.B)&&snapshot.Phase==RoundPhase.Freeze&&!menu.activeSelf)buy.SetActive(!buy.activeSelf);
            if(snapshot.Phase!=RoundPhase.Freeze)buy.SetActive(false);
            canControl=!loading&&(Application.isFocused||InputProvider!=null)&&Time.timeScale>0&&!menu.activeSelf&&!buy.activeSelf&&
                !(EventSystem.current?.IsPointerOverGameObject()??false)&&predicted.Health>0&&
                (snapshot.Phase==RoundPhase.Freeze||snapshot.Phase==RoundPhase.Live||snapshot.Phase==RoundPhase.Planted);
            controls=InputProvider!=null?InputProvider():new Controls {
                Move=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical")),
                MouseDelta=new Vector2(Input.GetAxisRaw("Mouse X"),Input.GetAxisRaw("Mouse Y")),
                WeaponScroll=Input.mouseScrollDelta.y,
                Fire=Input.GetMouseButton(0),FirePressed=Input.GetMouseButtonDown(0),Aim=Input.GetMouseButton(1),
                Reload=Input.GetKeyDown(KeyCode.R),Jump=Input.GetKeyDown(KeyCode.Space),
                Sprint=Input.GetKey(KeyCode.LeftShift),Interact=Input.GetKey(KeyCode.E)};
            bool activeRound=snapshot.Phase==RoundPhase.Live||snapshot.Phase==RoundPhase.Planted;
            if(!controls.Fire)fireNeedsRelease=false;
            if(!controls.Aim)aimNeedsRelease=false;
            if(predicted.ReloadTicks>0)aimNeedsRelease=true;
            if(!canControl){fireNeedsRelease=aimNeedsRelease=true;firePressed=reloadPressed=jumpPressed=false;}
            if(!activeRound||controls.Interact){fireNeedsRelease=true;firePressed=reloadPressed=jumpPressed=false;}
            else if(canControl){firePressed|=controls.FirePressed;reloadPressed|=controls.Reload;jumpPressed|=controls.Jump;}
            var player=LocalPlayer;player.InputEnabled=canControl;
            player.UpdateAimInput(controls.MouseDelta,canControl,Time.unscaledDeltaTime);
            followCamera.InputEnabled=canControl;
            bool objectiveBusy=actors[connection.PlayerId].Objective.Interacting;
            followCamera.ExternalAiming=canControl&&!objectiveBusy&&!controls.Interact&&controls.Aim&&!aimNeedsRelease&&predicted.ReloadTicks==0;
            followCamera.ExternalScoped=followCamera.ExternalAiming&&actors[connection.PlayerId].Weapon.IsSniper;
            RefreshCursor();
            if(canControl&&!objectiveBusy&&!controls.Interact)
            {
                if(Input.GetKeyDown(KeyCode.Alpha1))connection.Equip(1);
                else if(Input.GetKeyDown(KeyCode.Alpha2))connection.Equip(2);
                else if(followCamera.IsFirstPerson&&controls.WeaponScroll!=0)
                {
                    // This mode has two weapon slots; the server ignores an empty slot.
                    int slot=WeaponSelection.SlotOf(catalog.Weapon(actors[connection.PlayerId].State.Weapon));
                    connection.Equip(slot==1?2:1);
                }
            }
            if(Input.GetKeyDown(KeyCode.V)&&predicted.Health<=0)
            {var team=actors.Values.Where(a=>a.State.Motion.Health>0&&a.State.Motion.Team==predicted.Team).OrderBy(a=>a.State.Motion.Id).ToArray();if(team.Length>0)observed=team[(Array.FindIndex(team,a=>a.State.Motion.Id==observed)+1)%team.Length].State.Motion.Id;}

            foreach(var a in actors.Values.Where(a=>a.State.Motion.Id!=connection.PlayerId))
            {
                Vector3 before=a.Motor.transform.position;var motion=a.State.Motion;
                Vector3 pos=Vector3.Lerp(before,CharacterSimulation.Position(motion),1-Mathf.Exp(-20*Time.unscaledDeltaTime));
                motion.X=pos.x;motion.Y=pos.y;motion.Z=pos.z;CharacterSimulation.Restore(a.Motor,motion);a.Motor.enabled=motion.Health>0;
                a.Animation.SetPresentationVelocity((pos-before)/Mathf.Max(.001f,Time.unscaledDeltaTime));
            }
            var focus=actors[connection.PlayerId];
            if(predicted.Health<=0)
            {
                if(!actors.TryGetValue(observed,out var spectate)||spectate.State.Motion.Team!=predicted.Team||spectate.State.Motion.Health<=0)
                    spectate=actors.Values.FirstOrDefault(a=>a.State.Motion.Team==predicted.Team&&a.State.Motion.Health>0);
                if(spectate!=null){focus=spectate;observed=focus.State.Motion.Id;}
            }
            if(cameraTarget!=focus.Motor.transform){cameraTarget=focus.Motor.transform;followCamera.Follow(cameraTarget);}
            tacticalHud.SetObserver(view.GetComponent<CharacterVision>(), focus.State.Motion.Health > 0 && focus.State.Connected);
            scoreboard.transform.parent.gameObject.SetActive(Input.GetKey(KeyCode.Tab));
            if((snapshot.Phase==RoundPhase.Finished||snapshot.Phase==RoundPhase.Aborted)&&endAt<0)endAt=Time.unscaledTime+6;
            if(endAt>0&&Time.unscaledTime>=endAt)Return();
        }
        void RefreshCursor()
        {
            bool visible=!canControl||failed||returning;
            Cursor.visible=visible;Cursor.lockState=visible?CursorLockMode.None:followCamera.GameplayCursorLock;
            if(followCamera!=null){followCamera.InputEnabled=!visible;if(visible)followCamera.ExternalAiming=followCamera.ExternalScoped=false;}
        }
        void OnApplicationFocus(bool focused)
        {
            if(!focused){canControl=false;fireNeedsRelease=aimNeedsRelease=true;RefreshCursor();}
        }
        // Camera LateUpdate (50) resolves the final 3D aim ray before sending commands (70).
        void LateUpdate()
        {
            if(!Ready||failed||returning)
            {
                tacticalHud?.SetObserver(null, false);
                if(combat!=null)combat.PresentNetwork(null,0,0,false,null);
                foreach(var actor in actors.Values)actor.Objective.StopInteraction();
                bombPresentation?.StopPresentation();return;
            }
            accumulator+=Mathf.Min(.1f,Time.unscaledDeltaTime);
            while(accumulator>=BattleProtocol.Step){accumulator-=BattleProtocol.Step;Predict();}
            var local=actors[connection.PlayerId];
            combat.PresentNetwork(local.Animation,local.Weapon.LastShotTime,followCamera.ScopeBlend,canControl,
                combat.canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:combat.canvas.worldCamera);
        }
        void Predict()
        {
            using var profile = PredictionMarker.Auto();
            if(pending.Count>240){failed=true;canControl=false;RefreshCursor();menu.SetActive(true);heading.text="同步中断，请返回大厅重连";return;}
            var local=actors[connection.PlayerId];
            bool live=snapshot.Phase==RoundPhase.Live||snapshot.Phase==RoundPhase.Planted;
            bool interact=canControl&&live&&controls.Interact;
            InputButtons buttons=InputButtons.None;
            if(canControl&&live&&!interact)
            {
                if(!fireNeedsRelease&&(local.Weapon.Automatic?controls.Fire:firePressed))buttons|=InputButtons.Fire;
                if(reloadPressed)buttons|=InputButtons.Reload;
                if(jumpPressed)buttons|=InputButtons.Jump;
                if(controls.Sprint)buttons|=InputButtons.Sprint;
            }
            firePressed=reloadPressed=jumpPressed=false;
            var command=new BattleInput{Sequence=++sequence,Yaw=Mathf.DeltaAngle(0,local.Motor.transform.eulerAngles.y),
                X=canControl&&live&&!interact?controls.Move.x:0,Z=canControl&&live&&!interact?controls.Move.y:0,Buttons=buttons};
            pending.Add(command);Vector3 before=local.Motor.transform.position;
            // Dead characters remain immobile until the next authoritative life starts.
            if(predicted.Health>0)CharacterSimulation.Step(local.Motor,ref predicted,command);
            local.Animation.SetPresentationVelocity((local.Motor.transform.position-before)/BattleProtocol.Step);
            connection.Input(command,interact,DemolitionMap.Point(local.Animation.AimPoint));
        }
        void OnSnapshot(DemolitionSnapshot s)
        {
            using var profile = SnapshotMarker.Auto();
            if(connection.PlayerId==0||snapshot!=null&&s.Tick<=snapshot.Tick)return;
            snapshot=s;lastSnapshot=Time.unscaledTime;
            foreach(var state in s.Actors)
            {
                if(!actors.TryGetValue(state.Motion.Id,out var a))
                {
                    a=new Actor{Motor=CharacterSimulation.CreatePawn(state.Motion.Id)};actors.Add(state.Motion.Id,a);owned.Add(a.Motor.gameObject);
                    a.Animation=a.Motor.gameObject.AddComponent<CharacterMotor>();a.Animation.enabled=false;
                    a.Weapon=a.Motor.gameObject.AddComponent<NetworkWeaponPresentation>();
                    if(state.Motion.Id==connection.PlayerId)
                    {
                        a.Motor.gameObject.layer=2; // Exclude the local capsule from camera/weapon aim rays.
                        a.Motor.gameObject.AddComponent<FootstepAudio>().Initialize(a.Animation);
                    }
                    var visual=new GameObject("BattleAvatar");visual.transform.SetParent(a.Motor.transform,false);a.Avatar=visual.AddComponent<BattleAvatar>();
                    a.Animator=a.Motor.gameObject.AddComponent<CharacterAnimator>();CharacterSimulation.Restore(a.Motor,state.Motion);
                    a.Objective=a.Motor.gameObject.AddComponent<DemolitionActorPresentation>();
                }
                bool life=a.State==null||a.State.Motion.Life!=state.Motion.Life;
                if(a.State==null||a.State.Weapon!=state.Weapon||a.State.Motion.Team!=state.Motion.Team)
                {
                    if(state.Motion.Id==connection.PlayerId)fireNeedsRelease=aimNeedsRelease=true;
                    a.Objective.Release();a.Weapon.PrepareChange();
                    a.Avatar.Show(catalog,state.Motion.Team==BattleTeam.T?"tm_phoenix":"ctm_sas",state.Weapon.Length>0?state.Weapon:"weapon_pist_glock18");
                    if(a.Avatar.Animator!=null)a.Animator.Initialize(a.Animation,a.Avatar.Animator);
                    foreach(var collider in a.Avatar.GetComponentsInChildren<Collider>())collider.enabled=false;
                    a.Weapon.Bind(a.Animation,a.Avatar.EquippedWeapon,state.Weapon.Length>0?state.Weapon:"weapon_pist_glock18",state.Motion.Id==connection.PlayerId);
                    a.Objective.Bind(presentationAssets,a.Avatar.Animator,a.Weapon,state.Motion.Id==connection.PlayerId);
                }
                if(life)a.Objective.StopInteraction();
                a.State=state;a.Avatar.gameObject.SetActive(state.Motion.Health>0);
                if(life||state.Motion.Health<=0)a.Weapon.ResetPresentation();
                a.Weapon.PresentReload(state.Motion.ReloadTicks);
                a.Animation.InputEnabled=state.Motion.Health>0;
                if(state.Motion.Id==connection.PlayerId)
                {
                    pending.RemoveAll(c=>c.Sequence<=state.Motion.Ack);if(life)pending.Clear();
                    var aimRotation=a.Motor.transform.rotation;
                    sequence=Math.Max(sequence,Math.Max(state.Motion.Ack,connection.LastSequence));predicted=state.Motion;CharacterSimulation.Restore(a.Motor,predicted);
                    if(predicted.Health>0)foreach(var command in pending)CharacterSimulation.Step(a.Motor,ref predicted,command);
                    if(life)
                    {
                        a.Animation.BindPresentationCamera(view);cameraTarget=a.Motor.transform;followCamera.Follow(cameraTarget);
                        fireNeedsRelease=aimNeedsRelease=true;firePressed=reloadPressed=jumpPressed=false;accumulator=0;
                    }
                    else a.Motor.transform.rotation=aimRotation;
                }
                else if(life)CharacterSimulation.Restore(a.Motor,state.Motion);
                a.Motor.enabled=state.Motion.Health>0;
                bool active=state.Motion.Health>0&&state.Connected&&(s.Phase==RoundPhase.Live||s.Phase==RoundPhase.Planted);
                bool interacting=active&&s.Interactor==state.Motion.Id;
                bool planting=interacting&&s.Bomb==BombPhase.Carried;
                bool defusing=interacting&&s.Bomb==BombPhase.Planted;
                a.Objective.SetObjectivePosition(DemolitionMap.Vector(s.BombPosition));
                a.Objective.Present(planting,defusing,state.Motion.Health>0&&s.Carrier==state.Motion.Id&&s.Bomb==BombPhase.Carried,
                    interacting?s.Progress:0,planting?rules.PlantSeconds:state.Kit?rules.KitSeconds:rules.DefuseSeconds);
            }
            bombPresentation.Present(s);
            var me=s.Actors.First(a=>a.Motion.Id==connection.PlayerId);
            heading.text=$"第 {s.Round:00} 回合  /  {s.Reason}";
            bool localTeamA = Array.Find(match.Seats, seat => seat.Id == connection.PlayerId)?.Team == 0;
            scoreA.text=(localTeamA ? s.ScoreA : s.ScoreB).ToString("00");scoreB.text=(localTeamA ? s.ScoreB : s.ScoreA).ToString("00");
            timer.text=TimeSpan.FromSeconds(Mathf.Max(0,Mathf.CeilToInt(s.Remaining))).ToString(@"mm\:ss");
            timer.color=s.Bomb==BombPhase.Planted?DemolitionUI.Accent:DemolitionUI.White;
            healthText.text=me.Motion.Health.ToString();armorText.text=me.Armor.ToString();moneyText.text="$ "+me.Money.ToString("N0");
            ammoText.text=me.Motion.ReloadTicks>0?"换弹 "+(me.Motion.ReloadTicks/60f).ToString("0.0")+"s":me.Motion.Ammo+" / "+me.Motion.Reserve;weaponText.text=catalog?.Weapon(me.Weapon)?.displayName??me.Weapon;
            weaponIcon.sprite=catalog?.Weapon(me.Weapon)?.thumbnail;weaponIcon.enabled=weaponIcon.sprite!=null;
            healthBar.rectTransform.sizeDelta=new Vector2(244*Mathf.Clamp01(me.Motion.Health/100f),3);
            details.text=me.Motion.Health<=0?"已阵亡 · 等待下回合":"";
            buyBalance.text="$ "+me.Money.ToString("N0")+"   /   "+Mathf.CeilToInt(s.Remaining)+"s";
            foreach(var entry in shopButtons)
            {
                int price=entry.Key==BuyItem.AK47?2700:entry.Key==BuyItem.M4A1?2900:entry.Key==BuyItem.Deagle?700:entry.Key==BuyItem.Armor?650:400;
                bool side=entry.Key==BuyItem.AK47?me.Motion.Team==BattleTeam.T:entry.Key==BuyItem.M4A1||entry.Key==BuyItem.DefuseKit?me.Motion.Team==BattleTeam.CT:true;
                entry.Value.interactable=s.Phase==RoundPhase.Freeze&&me.Motion.Health>0&&me.Money>=price&&side&&!(entry.Key==BuyItem.Armor&&me.Armor>=100)&&!(entry.Key==BuyItem.DefuseKit&&me.Kit);
                entry.Value.GetComponent<CanvasGroup>().alpha=entry.Value.interactable?1:.42f;
            }
            objective.text=s.Progress>0?$"{(s.Bomb==BombPhase.Planted?"拆除":"安装")} {Mathf.RoundToInt(s.Progress*100)}%":s.Bomb==BombPhase.Planted?$"炸弹已安装 · {(s.Site==0?"A":"B")} 点":s.Carrier==connection.PlayerId?"携带炸弹":s.Bomb==BombPhase.Dropped?"炸弹已掉落":"";
            scoreboard.text=string.Join("\n",s.Actors.OrderBy(a=>match.Seats.First(x=>x.Id==a.Motion.Id).Team).Select(a=>
            {var seat=match.Seats.First(x=>x.Id==a.Motion.Id);return $"{(seat.Team==0?"A":"B")}  {seat.Name} {(seat.IsBot?"[AI]":"")}    {a.Kills}/{a.Deaths}    {(a.Motion.Health>0?"存活":"阵亡")}{(a.Connected?"":" [离线]")}";}));
            bombMarker.SetActive(s.Bomb==BombPhase.Planted||(s.Bomb==BombPhase.Dropped&&me.Motion.Team==BattleTeam.T));
            bombMarker.transform.position=DemolitionMap.Vector(s.BombPosition)+Vector3.up*.12f;
            RefreshTacticalHud(s, connection.PlayerId);
        }

        void RefreshTacticalHud(DemolitionSnapshot state, int localId)
        {
            hudParticipants.Clear();
            int localTeam = 0;
            foreach (var actor in state.Actors) if (actor.Motion.Id == localId) localTeam = (int)actor.Motion.Team;
            foreach (var seat in match.Seats)
            {
                var actor = Array.Find(state.Actors, a => a.Motion.Id == seat.Id);
                var motion = actor != null ? actor.Motion : default;
                string status = state.Interactor == seat.Id && state.Progress > 0 ? (state.Bomb == BombPhase.Planted ? "拆弹中" : "安装中") :
                    motion.ReloadTicks > 0 ? "换弹中" : state.Carrier == seat.Id ? "携带 C4" : actor != null && actor.Kit ? "拆弹器" : "存活";
                hudParticipants.Add(new CombatHudParticipant {
                    Id = seat.Id, Name = seat.Name, Team = seat.Team + 1, Health = motion.Health, MaxHealth = 100,
                    Connected = actor != null && actor.Connected, HasSnapshot = actor != null, Status = status,
                    Portrait = catalog?.Agent(motion.Team == BattleTeam.T ? "tm_phoenix" : "ctm_sas")?.portrait,
                    Position = new Vector3(motion.X, motion.Y, motion.Z), Yaw = motion.Yaw,
                    WorldTransform = actors.TryGetValue(seat.Id, out var presentation) ? presentation.Motor.transform : null
                });
            }
            tacticalHud.Present(hudParticipants, localId);
            bool bombVisible = state.Bomb == BombPhase.Planted || localTeam == (int)BattleTeam.T && state.Bomb == BombPhase.Dropped;
            tacticalHud.SetBomb(bombVisible, DemolitionMap.Vector(state.BombPosition), state.Bomb == BombPhase.Planted);
        }
        void OnShot(int shooter,int hit,DemolitionPoint from,DemolitionPoint to)
        {
            if(actors.TryGetValue(shooter,out var actor))actor.Weapon.ConfirmShot(DemolitionMap.Vector(from),DemolitionMap.Vector(to));
            if(shooter==connection.PlayerId&&hit>0)combat.ShowConfirmedHit();
        }
        void BuildHud()
        {
            var root=new GameObject("Demolition HUD",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));owned.Add(root);
            root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            var aimRoot=new GameObject("Shared combat reticle",typeof(RectTransform));aimRoot.transform.SetParent(root.transform,false);
            var aimRect=(RectTransform)aimRoot.transform;aimRect.anchorMin=Vector2.zero;aimRect.anchorMax=Vector2.one;aimRect.offsetMin=aimRect.offsetMax=Vector2.zero;
            combat=aimRoot.AddComponent<CombatOverlay>();combat.Initialize(font);
            hudSheet=DemolitionUI.Rect(root.transform,"Combat overlay",0,0,1600,900);DemolitionUI.Center(hudSheet);
            DemolitionUI.Fade(hudSheet,0,0,1600,150,new Color(0,0,0,.64f),Color.clear,true);
            DemolitionUI.Fade(hudSheet,0,710,1600,190,Color.clear,new Color(0,0,0,.78f),true);
            var scorePlate=DemolitionUI.Rect(hudSheet,"Round clock",668,14,264,104);
            scorePlate.gameObject.AddComponent<UIAngledGraphic>().color=new Color32(12,17,21,224);
            scorePlate.GetComponent<UIAngledGraphic>().raycastTarget=false;
            scoreA=HudLabel(scorePlate,"00",8,16,52,48,30,DemolitionUI.Blue);
            scoreB=HudLabel(scorePlate,"00",204,16,52,48,30,new Color32(235,131,123,255));
            timer=HudLabel(scorePlate,"00:00",64,12,136,54,34);
            heading=HudLabel(scorePlate,"正在连接",8,70,248,28,14,DemolitionUI.Muted);
            DemolitionUI.Fill(scorePlate,8,99,52,3,DemolitionUI.Blue);DemolitionUI.Fill(scorePlate,204,99,52,3,new Color32(235,131,123,255));
            tacticalHud = CombatTacticalHud.Create(hudSheet, font);
            var tacticalMap = FindObjectOfType<DemolitionMap>();
            if (tacticalMap != null) tacticalHud.SetSites(tacticalMap.Sites);
            objective=HudLabel(hudSheet,"",392,160,816,38,20,DemolitionUI.Accent);
            var health=DemolitionUI.Rect(hudSheet,"Vitals",44,776,280,104);
            HudLabel(health,"生命",0,0,60,26,15,DemolitionUI.Muted);
            healthText=HudLabel(health,"100",0,24,138,60,44);
            HudLabel(health,"护甲",158,0,74,26,15,DemolitionUI.Muted);
            armorText=HudLabel(health,"0",158,24,96,60,36,DemolitionUI.Blue);
            healthBar=DemolitionUI.Fill(health,0,91,244,3,DemolitionUI.White);
            moneyText=HudLabel(hudSheet,"$ 800",48,724,240,44,26,DemolitionUI.Accent);
            details=HudLabel(hudSheet,"",372,846,820,30,16,DemolitionUI.Muted);
            weaponIcon=DemolitionUI.Icon(hudSheet,null,1330,706,224,92);
            weaponIcon.enabled=false; // No white placeholder while waiting for the first equipment snapshot.
            weaponText=HudLabel(hudSheet,"",1218,804,338,28,17,DemolitionUI.Muted);
            ammoText=HudLabel(hudSheet,"0 / 0",1286,832,270,58,38);
            // Keep the reticle above informational HUD text/fades and below interactive menus.
            combat.transform.SetParent(hudSheet,false);
            var board=DemolitionUI.Rect(hudSheet,"Scoreboard",350,155,900,570);
            board.gameObject.AddComponent<UIAngledGraphic>().color=DemolitionUI.Ink;
            HudLabel(board,"对局战绩",34,24,832,52,32);
            DemolitionUI.Fill(board,34,94,832,2,DemolitionUI.Accent);
            scoreboard=DemolitionUI.Label(board,font,"",44,120,812,420,24);
            board.gameObject.SetActive(false);

            buy=DemolitionUI.Rect(hudSheet,"Buy menu",220,136,1160,644).gameObject;
            buy.AddComponent<UIAngledGraphic>().color=DemolitionUI.Ink;
            DemolitionUI.Fill(buy.transform,0,0,4,644,DemolitionUI.Accent);
            DemolitionUI.Label(buy.transform,font,"购买装备",34,22,690,68,42);
            buyBalance=DemolitionUI.Label(buy.transform,font,"",770,28,344,54,30);buyBalance.alignment=TextAnchor.MiddleRight;buyBalance.color=DemolitionUI.Accent;
            DemolitionUI.Button(buy.transform,font,"B  关闭",956,92,168,()=>buy.SetActive(false));
            ShopCard(BuyItem.AK47,"AK-47  $2700","weapon_rif_ak47","进攻方 / 步枪",34,160,350,236);
            ShopCard(BuyItem.M4A1,"M4A1-S  $2900","weapon_rif_m4a1_silencer","防守方 / 步枪",404,160,350,236);
            ShopCard(BuyItem.Deagle,"沙漠之鹰  $700","weapon_pist_deagle","手枪",774,160,350,236);
            ShopCard(BuyItem.Armor,"防弹衣  $650","w_eq_armor","护甲",34,420,536,172);
            ShopCard(BuyItem.DefuseKit,"拆弹器  $400","defuser","防守方 / 拆除时间减半",590,420,534,172);
            buy.SetActive(false);

            menu=DemolitionUI.Rect(hudSheet,"Pause menu",64,240,480,420).gameObject;
            menu.AddComponent<UIAngledGraphic>().color=DemolitionUI.Ink;
            BlackGoldTheme.SurfaceFor(menu.GetComponent<Graphic>(),UIGlassEffect.Kind.Rail);
            DemolitionUI.Fill(menu.transform,24,112,396,1,new Color(1,1,1,.3f));
            DemolitionUI.Label(menu.transform,font,"对局菜单",24,28,396,70,34);
            var resume=DemolitionUI.TextButton(menu.transform,font,"继续游戏",24,140,396,()=>menu.SetActive(false));
            BlackGoldTheme.Select(resume,true);
            DemolitionUI.TextButton(menu.transform,font,"投降",24,224,396,()=>connection.Surrender());
            bool confirm=false;Button leave=null;
            leave=DemolitionUI.TextButton(menu.transform,font,"返回大厅",24,306,396,()=>{if(confirm||snapshot?.Phase==RoundPhase.Finished){Return();return;}confirm=true;leave.GetComponentInChildren<Text>().text="确认离开（3 分钟内可重连）";});
            foreach(var label in menu.GetComponentsInChildren<Text>())label.alignment=TextAnchor.MiddleLeft;
            menu.SetActive(false);
            var notice=DemolitionUI.Rect(hudSheet,"Combat notice",460,790,680,46);
            notice.gameObject.AddComponent<UIAngledGraphic>().color=DemolitionUI.Ink;
            notice.GetComponent<UIAngledGraphic>().raycastTarget=false;
            feedback=HudLabel(notice,"",14,0,652,46,20,DemolitionUI.Accent);notice.gameObject.SetActive(false);
            DemolitionUI.Fit(hudSheet,0);
            BlackGoldTheme.Apply(hudSheet.gameObject);
        }
        Text HudLabel(Transform parent,string text,float x,float y,float w,float h,int size,Color? color=null)
        {
            var label=DemolitionUI.Label(parent,font,text,x,y,w,h,size);label.alignment=TextAnchor.MiddleCenter;
            label.color=color??DemolitionUI.White;return label;
        }
        void ShopCard(BuyItem item,string title,string id,string description,float x,float y,float width,float height)
        {
            var button=DemolitionUI.Button(buy.transform,font,title,x,y,width,()=>connection.Buy(item),false,height);
            button.gameObject.AddComponent<CanvasGroup>();
            shopButtons.Add(item,button);var label=button.GetComponentInChildren<Text>();
            label.rectTransform.anchoredPosition=new Vector2(18,-height+64);label.rectTransform.sizeDelta=new Vector2(width-36,48);
            label.alignment=TextAnchor.MiddleLeft;label.fontSize=23;
            DemolitionUI.Label(button.transform,font,description,18,12,width-36,32,16).color=DemolitionUI.Muted;
            float imageW=height>200?width-44:132;
            var icon=DemolitionUI.Icon(button.transform,catalog?.Weapon(id)?.thumbnail,height>200?22:width-166,height>200?44:14,imageW,height>200?114:136);
            icon.color=new Color(.86f,.9f,.92f,1);
            DemolitionUI.Fill(button.transform,18,height-8,width-36,2,DemolitionUI.Accent);
        }

        void ToggleMenu(){if(menu!=null){menu.SetActive(!menu.activeSelf);buy.SetActive(false);}}
        void Return(){if(returning)return;returning=true;canControl=false;RefreshCursor();connection?.Dispose();GameSceneFlow.Instance?.ReturnToLobby();}
        void OnDestroy()
        {
            if(uiRoot!=null)uiRoot.UnhandledBackRequested-=ToggleMenu;connection?.Dispose();
            Cursor.visible=true;Cursor.lockState=CursorLockMode.None;
            foreach(var go in owned)if(go!=null)Destroy(go);
        }
    }
}
