using OperationBlacktide.Client.Features.Lobby;
#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Features.Demolition;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Modes.Demolition;
using OperationBlacktide.Shared.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Client.Gameplay.Characters;

public sealed class DemolitionVerificationDriver:MonoBehaviour
{
    string output;float deadline;bool done;int port;
    void Awake()
    {
        DontDestroyOnLoad(gameObject);var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"--test-output")+1];
        port=int.Parse(File.ReadAllText(Path.Combine(output,"account-port.txt")));deadline=Time.realtimeSinceStartup+180;
        Application.logMessageReceived+=Log;Application.runInBackground=true;
    }
    void Log(string message,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)Fail(message+"\n"+trace);}
    void Update(){if(!done&&Time.realtimeSinceStartup>deadline)Fail("Client flow timeout");}
    void Fail(string text){if(done)return;done=true;File.WriteAllText(Path.Combine(output,"failure.txt"),text);EditorApplication.Exit(1);}
    void Check(bool value,string text){if(!value){Fail(text);throw new InvalidOperationException(text);}Debug.Log("PASS "+text);}
    static FieldInfo Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
    static void Click(string text)
    {
        var button=FindObjectsOfType<Button>().FirstOrDefault(b=>b.GetComponentInChildren<Text>()?.text==text);
        if(button==null||!button.interactable)throw new InvalidOperationException("Missing/interlocked button: "+text);
        Canvas.ForceUpdateCanvases();
        var rect=(RectTransform)button.transform;
        var point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
        var data=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
        var hits=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        if(hits.Count==0||ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)!=button.gameObject)
            throw new InvalidOperationException("Button raycast blocked: "+text+" at "+point+" depth="+button.targetGraphic.depth+" by "+string.Join(",",hits.Select(h=>h.gameObject.name)));
        ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerClickHandler);
    }
    IEnumerator Capture(string filename)
    {
        var camera=Camera.main;var canvases=FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        var texture=new RenderTexture(1600,900,24);camera.targetTexture=texture;
        foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=.5f;}
        Canvas.ForceUpdateCanvases();yield return null;
        camera.Render();RenderTexture.active=texture;var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
        pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,filename),pixels.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=null;foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceOverlay;c.worldCamera=null;}
        Destroy(texture);Destroy(pixels);yield return null;
    }
    IEnumerator Start()
    {
        yield return null;yield return null;var flow=GameSceneFlow.Instance;var tcp=flow.GetComponent<TcpGameClient>();
        Field(tcp,"appConfig").SetValue(tcp,null);Field(tcp,"serverPort").SetValue(tcp,port);
        bool registered=false;flow.Auth.RegisterCompleted+=r=>{Check(r.Code==AuthResultCode.Ok,"registration");registered=true;};
        var task=flow.Auth.RegisterAsync("DemoVisual","VisualTest_628");yield return new WaitUntil(()=>registered||task.IsFaulted);Check(!task.IsFaulted,"register request");
        task=flow.Auth.LoginAsync("DemoVisual","VisualTest_628");yield return new WaitUntil(()=>SceneManager.GetActiveScene().path==GameSceneFlow.LobbyScenePath&&!flow.IsLoading);
        yield return new WaitForSecondsRealtime(3);
        var lobby=FindObjectOfType<LobbyPanel>();typeof(LobbyPanel).GetMethod("OnModeClicked",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lobby,new object[]{0});
        ((Button)Field(lobby,"enterGameButton").GetValue(lobby)).onClick.Invoke();yield return new WaitForSecondsRealtime(1);
        Check(FindObjectOfType<DemolitionPanel>()?.IsOpen==true,"lobby demolition entry");yield return Capture("demolition-lobby.png");
        Check(lobby.GetComponent<CanvasGroup>().alpha==0,"deployment replaces underlying lobby controls");
        var gateway=DemolitionGateway.Get();Click("房间大厅");yield return Capture("demolition-browser.png");
        Click("开设房间");yield return Capture("demolition-create.png");Click("AI 补齐：关闭");Click("创建房间");yield return new WaitUntil(()=>gateway.State.Room!=null);yield return null;
        Click("准备");yield return new WaitUntil(()=>gateway.State.Room.Members[0].Ready);Click("开始对局");yield return new WaitUntil(()=>gateway.State.Room.StartsIn>=0);
        yield return Capture("demolition-room.png");Click("取消准备");yield return new WaitUntil(()=>gateway.State.Room.StartsIn<0);Click("离开房间");yield return new WaitUntil(()=>gateway.State.Room==null);yield return null;
        Click("快速匹配");yield return null;
        if(PlayerPrefs.GetInt("Demolition.AllowAI",0)==0)Click("允许 AI 补齐");
        Click("开始匹配");Check(gateway.Busy,"match click immediately enters pending state");
        Check(FindObjectsOfType<Text>().Any(t=>t.text=="正在提交…"),"immediate visible request feedback");
        int pending=(int)Field(gateway,"pendingId").GetValue(gateway);
        gateway.Send(new DemolitionRequest{Action=LobbyAction.Queue});
        Check((int)Field(gateway,"pendingId").GetValue(gateway)==pending,"duplicate queue click suppressed");
        var stale=new DemolitionResponse{RequestId=pending-1};
        typeof(DemolitionGateway).GetMethod("Receive",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gateway,new object[]{new NetworkMessage(OperationBlacktide.Shared.Protocol.MessageType.DemolitionResponse,JsonUtility.ToJson(stale))});
        Check(gateway.Busy,"earlier polling reply cannot complete pending queue request");
        Field(gateway,"pendingAt").SetValue(gateway,Time.unscaledTime-9);
        typeof(DemolitionGateway).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gateway,null);
        Check(!gateway.Busy&&gateway.Error.Contains("未响应"),"silent service gets explicit timeout feedback");
        yield return new WaitUntil(()=>gateway.State.Phase==LobbyPhase.Queued);yield return Capture("demolition-queue.png");
        float started=Time.realtimeSinceStartup;
        yield return new WaitUntil(()=>SceneManager.GetActiveScene().path==GameSceneFlow.BattleScenePath&&!flow.IsLoading);yield return null;
        Check(Time.realtimeSinceStartup-started>=28,"30-second queue was not bypassed");
        var battle=FindObjectOfType<DemolitionBattleClient>();yield return new WaitUntil(()=>battle.Ready);
        Check(battle.State.Actors.Length==10&&battle.State.Actors.Count(a=>a.Motion.IsBot)==9,"ten actual network actors with nine AI");
        Check(UIManager.Instance.GetStackCount(UILayer.Popup)==0&&UIManager.Instance.GetStackCount(UILayer.Normal)==0,"lobby UI released on battle entry");
        DemolitionBattleClient.Controls controls=default;battle.InputProvider=()=>controls;
        yield return new WaitForSecondsRealtime(1);
        Check(battle.CombatOverlay.ReticleVisible,"shared crosshair visible after battle entry");
        Check(battle.LocalPlayer.GetComponent<LocalPlayerInput>()==null&&battle.LocalPlayer.GetComponent<WeaponController>()==null,"network actor has no duplicate local authority");
        var player=battle.LocalPlayer;var camera=battle.FollowCamera;
        Vector2 cursorBefore=player.AimScreenPosition;
        controls.MouseDelta=new Vector2(12,2);yield return null;controls.MouseDelta=Vector2.zero;yield return null;
        Check(Vector2.Distance(cursorBefore,player.AimScreenPosition)>4,"mouse displacement moves shared reticle");
        Vector3 aimBefore=player.AimPoint;Quaternion beforeOrbit=Camera.main.transform.rotation;
        camera.ProcessOrbit(true,true,Vector2.zero,true);camera.ProcessOrbit(true,false,new Vector2(4,2),true);
        camera.UpdateView(Vector2.zero,false,true,0,.1f);
        Check(Quaternion.Angle(beforeOrbit,Camera.main.transform.rotation)>1,"shared middle-button camera orbit");
        Check(Vector3.Distance(aimBefore,player.AimPoint)<.02f,"orbit preserves ballistic aim");
        camera.ProcessOrbit(false,false,Vector2.zero,true);
        float zoomBefore=Vector3.Distance(Camera.main.transform.position,camera.Focus);
        camera.UpdateView(Vector2.zero,false,true,1,.1f);
        Check(Vector3.Distance(Camera.main.transform.position,camera.Focus)<zoomBefore-.5f,"shared scroll zoom");
        camera.UpdateView(new Vector2(.95f,0),false,true,0,.1f,true);
        Check(camera.IsAimingView&&camera.IsDetached,"shared right-aim edge panning");
        camera.UpdateView(Vector2.zero,false,true,0,.1f,false);
        Check(!camera.IsAimingView&&!camera.IsDetached,"releasing aim restores follow view");
        player.Aim(Camera.main.ViewportToScreenPoint(new Vector3(.65f,.45f)),1f);
        yield return new WaitForSecondsRealtime(.3f);
        var reticleMesh=battle.CombatOverlay.canvasRenderer.GetMesh();
        Check(reticleMesh.vertexCount>=36,"reticle actually generates renderable geometry");
        yield return Capture("demolition-battle.png");
        ((GameObject)Field(battle,"buy").GetValue(battle)).SetActive(true);yield return null;Click("防弹衣  $650");yield return new WaitForSecondsRealtime(.5f);yield return Capture("demolition-buy.png");
        Check(battle.State.Actors.Any(a=>!a.Motion.IsBot&&a.Money==150&&a.Armor==100),"UI purchase reaches authority");
        Check(!battle.CombatOverlay.ReticleVisible,"buy UI hides combat reticle");
        ((GameObject)Field(battle,"buy").GetValue(battle)).SetActive(false);
        yield return new WaitUntil(()=>battle.State.Phase==RoundPhase.Live);yield return null;
        var weapon=player.GetComponent<NetworkWeaponPresentation>();var actions=player.GetComponent<WeaponPresentation>();
        int ammoBefore=battle.State.Actors.Single(a=>!a.Motion.IsBot).Motion.Ammo;
        controls.Fire=true;controls.FirePressed=true;yield return null;controls.FirePressed=false;
        yield return new WaitForSecondsRealtime(.7f);
        Check(battle.State.Actors.Single(a=>!a.Motion.IsBot).Motion.Ammo==ammoBefore-1,"semi-auto held trigger consumes one authoritative round");
        Check(weapon.LastShotTime>0&&actions.FireAnimationsPlayed>0,"confirmed shot drives shared weapon effects and animation");
        controls.Fire=false;controls.Reload=true;yield return null;controls.Reload=false;
        yield return new WaitUntil(()=>battle.State.Actors.Single(a=>!a.Motion.IsBot).Motion.ReloadTicks>0);
        yield return new WaitForSecondsRealtime(.3f);
        Check(actions.PresentedReloadProgress>0,"server reload drives shared reload pose");
        yield return Capture("demolition-reload.png");
        yield return new WaitUntil(()=>battle.State.Actors.Single(a=>!a.Motion.IsBot).Motion.ReloadTicks==0);
        Check(battle.State.Actors.Single(a=>!a.Motion.IsBot).Motion.Ammo==ammoBefore,"authoritative reload restores magazine: "+JsonUtility.ToJson(battle.State.Actors.Single(a=>!a.Motion.IsBot)));
        var position=player.transform.position;controls.Move=Vector2.up;yield return new WaitForSecondsRealtime(.35f);controls.Move=Vector2.zero;
        Check(Vector3.Distance(position,player.transform.position)>.3f,"shared movement responds in live round");
        UIRoot.Instance.HandleBackInput();yield return new WaitForSecondsRealtime(.1f);
        Check(!battle.CombatOverlay.ReticleVisible&&Cursor.visible,"escape menu unlocks cursor and hides reticle: visible="+Cursor.visible+", reticle="+battle.CombatOverlay.ReticleVisible+", menu="+((GameObject)Field(battle,"menu").GetValue(battle)).activeSelf);Click("返回大厅");Click("确定离开？席位保留 3 分钟供重连");
        yield return new WaitUntil(()=>SceneManager.GetActiveScene().path==GameSceneFlow.LobbyScenePath&&!flow.IsLoading);yield return new WaitForSecondsRealtime(1);
        Check(FindObjectOfType<DemolitionPanel>()?.IsOpen==true,"return opens existing party/room view");Click("重连原对局");
        yield return new WaitUntil(()=>SceneManager.GetActiveScene().path==GameSceneFlow.BattleScenePath&&!flow.IsLoading);yield return null;
        battle=FindObjectOfType<DemolitionBattleClient>();yield return new WaitUntil(()=>battle.Ready);
        Check(battle.State.Actors.Length==10,"reconnect does not create extra actors");
        LoginCredentialStore.Forget("127.0.0.1",port);File.WriteAllText(Path.Combine(output,"result.txt"),"DEMOLITION_CLIENT_PASS");Debug.Log("DEMOLITION_CLIENT_PASS");done=true;EditorApplication.Exit(0);
    }
    void OnDestroy(){Application.logMessageReceived-=Log;}
}
#endif
