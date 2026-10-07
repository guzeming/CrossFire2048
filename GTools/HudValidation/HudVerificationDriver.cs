#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class HudVerificationDriver : MonoBehaviour
{
    private static HudVerificationDriver instance;
    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += OnLog;
    }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        { File.WriteAllText("hud-failure.txt", message + "\n" + trace); EditorApplication.Exit(1); }
    }

    private IEnumerator Start()
    {
        if (instance != this) yield break;
        yield return null; yield return null;
        var scene = FindObjectOfType<TrainingSceneController>();
        var panel = FindObjectOfType<TrainingPanel>();
        var gun = scene.Player.GetComponent<TrainingWeaponController>();
        var ammo = gun.Ammo;
        Check(ammo.Capacity == 20 && ammo.Magazine == 20, "M4A1-S profile missing.");
        Check(FindText(panel,"Magazine").text == "20" && FindText(panel,"WeaponName").text == "M4A1-S", "HUD binding failed.");
        Check(gun.HudIcon != null && panel.GetComponentsInChildren<Image>().Any(i => i.sprite == gun.HudIcon), "HUD silhouette missing.");
        Check(panel.GetComponentsInChildren<Graphic>(true).Count(g => g.raycastTarget) == 1, "Noninteractive HUD blocks aiming.");
        Capture(panel, "hud-1920x1080.png",1920,1080);
        Capture(panel, "hud-1280x720.png",1280,720);
        Capture(panel, "hud-1280x960.png",1280,960);
        Capture(panel, "hud-2560x1080.png",2560,1080);

        scene.Player.enabled = false;
        scene.Player.InputEnabled = true;
        gun.ProcessTrigger(false,false,true,100);
        for (int i = 0; i < 30; i++) gun.ProcessTrigger(true,i == 0,true,100 + i*.2);
        Check(gun.ShotsFired == 20 && ammo.Magazine == 0 && FindText(panel,"Magazine").text == "00", "Empty magazine / low-FPS burst count is wrong.");
        Check(FindText(panel,"Status").text.Contains("弹匣已空"), "Empty-magazine warning absent.");
        Capture(panel,"hud-empty.png",1920,1080);
        Check(gun.TryReload(), "Reload failed.");
        int fired = gun.ShotsFired;
        gun.ProcessTrigger(true,true,true,110);
        Check(gun.ShotsFired == fired, "Weapon fired during reload.");
        gun.AdvanceReload(1.55f);
        Check(Mathf.Abs(ammo.ReloadProgress-.5f) < .001f, "Reload progress incorrect.");
        panel.SendMessage("LateUpdate");
        Capture(panel,"hud-reload.png",1920,1080);
        scene.RequestReturnToLobby();
        Check(!scene.Player.InputEnabled, "Popup did not block player.");
        gun.AdvanceReload(10);
        Check(ammo.IsReloading && Mathf.Abs(ammo.ReloadProgress-.5f) < .001f, "Popup advanced reload.");
        panel.SendMessage("LateUpdate");
        Check(!panel.transform.Find("Crosshair").gameObject.activeSelf, "Popup kept crosshair.");
        Capture(panel,"hud-menu.png",1920,1080);
        UIRoot.Instance.HandleBackInput(); scene.Player.InputEnabled = true;
        gun.AdvanceReload(10);
        Check(!ammo.IsReloading && ammo.Magazine == 20, "Reload did not complete.");
        gun.ProcessTrigger(true,false,true,112);
        Check(gun.ShotsFired == fired, "Held click leaked through reload.");
        gun.ProcessTrigger(false,false,true,112);
        gun.ProcessTrigger(true,true,true,113);
        Check(gun.ShotsFired == fired+1 && ammo.Magazine == 19, "Fire did not resume after release.");

        var vitals = scene.Vitals;
        vitals.SetArmor(100); vitals.ApplyDamage(40);
        Check(vitals.Health == 80 && vitals.Armor == 80 && FindText(panel,"Health").text == "80", "Health / armor binding failed.");
        vitals.ApplyDamage(60,true);
        Check(FindText(panel,"Health").text == "20" && FindText(panel,"Health").color.g < .5f, "Low-health warning absent.");
        Capture(panel,"hud-low-health.png",1920,1080);
        vitals.ApplyDamage(-1); Check(vitals.Health == 20, "Negative damage healed.");
        scene.Player.Respawn();
        Check(vitals.Health == 100 && vitals.Armor == 0 && ammo.Magazine == 20, "Respawn did not reset HUD state.");

        UIManager.Instance.Close(UIPanelId.Training);
        string closedText = FindText(panel,"Health").text;
        vitals.ApplyDamage(10,true);
        Check(FindText(panel,"Health").text == closedText, "Closed panel retained health subscription.");
        UIManager.Instance.Push(UIPanelId.Training,scene);
        Check(FindText(panel,"Health").text == "90", "Reopened HUD did not bind current state.");
        vitals.ApplyDamage(999,true); Check(!scene.Player.InputEnabled, "Death allows firing.");
        yield return null; yield return null;
        Check(vitals.IsAlive && vitals.Health == 100, "Training death did not respawn.");
        Debug.Log("HUD_RUNTIME_STATE_PASS");

        // Persistent UI must detach the previous character and bind the next training session.
        UIManager.Instance.CloseAll(UILayer.Normal);
        SceneManager.LoadScene("Assets/Scenes/DustII.unity");
        yield return null; yield return null; yield return null;
        var nextScene = FindObjectOfType<TrainingSceneController>();
        var nextPanel = FindObjectOfType<TrainingPanel>();
        Check(nextPanel == panel && nextScene != scene, "Training panel cache / scene re-entry failed.");
        nextScene.Vitals.ApplyDamage(30,true);
        Check(FindText(nextPanel,"Health").text == "70" && FindText(nextPanel,"Magazine").text == "20", "HUD kept old player bindings.");
        UIManager.Instance.CloseAll(UILayer.Normal);
        SceneManager.LoadScene("Assets/Scenes/SampleScene.unity");
        yield return null; yield return null;
        Check(FindObjectOfType<TrainingPanel>() == null && FindObjectOfType<TrainingVitals>() == null, "Training HUD survived scene exit.");
        Debug.Log("HUD_VERIFY_PASS");
        EditorApplication.Exit(0);
    }

    private static Text FindText(TrainingPanel panel, string name) => panel.GetComponentsInChildren<Text>(true).Single(t => t.name == name);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static void Capture(TrainingPanel panel, string path, int width, int height)
    {
        var skins = Object.FindObjectsOfType<SkinnedMeshRenderer>().Where(s => s.enabled).ToArray();
        var snapshots = new GameObject[skins.Length]; var baked = new Mesh[skins.Length];
        for (int i = 0; i < skins.Length; i++)
        {
            baked[i] = new Mesh(); skins[i].BakeMesh(baked[i],true);
            snapshots[i] = new GameObject("Capture Skin",typeof(MeshFilter),typeof(MeshRenderer));
            snapshots[i].transform.SetParent(skins[i].transform,false);
            snapshots[i].GetComponent<MeshFilter>().sharedMesh = baked[i];
            snapshots[i].GetComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials; skins[i].enabled = false;
        }
        var camera = Camera.main; var canvas = UIRoot.Instance.GetComponentInChildren<Canvas>();
        int previousCanvasLayer = canvas.gameObject.layer; canvas.gameObject.layer = 5;
        var scaler = canvas.GetComponent<CanvasScaler>(); bool previousScaler = scaler.enabled; scaler.enabled = false;
        float previousScale = canvas.scaleFactor; canvas.scaleFactor = Mathf.Sqrt((float)width/1920*height/1080);
        var sceneTarget = new RenderTexture(width,height,24); var target = new RenderTexture(width,height,24);
        int previousMask = camera.cullingMask; canvas.enabled = false; camera.targetTexture = sceneTarget; camera.aspect = (float)width/height;
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = sceneTarget });
        var backdrop = new GameObject("CaptureBackground",typeof(RectTransform),typeof(RawImage)); backdrop.layer = 5;
        backdrop.transform.SetParent(canvas.transform,false); backdrop.transform.SetAsFirstSibling();
        var rect = (RectTransform)backdrop.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        backdrop.GetComponent<RawImage>().texture = sceneTarget; backdrop.GetComponent<RawImage>().raycastTarget = false;
        canvas.enabled = true; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .2f;
        camera.cullingMask = 1 << 5; camera.targetTexture = target; Canvas.ForceUpdateCanvases();
        panel.SendMessage("RefreshSafeArea"); Canvas.ForceUpdateCanvases();
        var aim = panel.transform.Find("Crosshair"); bool aimActive = aim.gameObject.activeSelf;
        if (FindObjectOfType<ReturnToLobbyPanel>() == null) { aim.gameObject.SetActive(true); ((RectTransform)aim).anchoredPosition = new Vector2(100,75); }
        foreach (var text in panel.GetComponentsInChildren<Text>())
            Check(text.preferredHeight <= text.rectTransform.rect.height + 1, "Text clipped in " + path + ": " + text.name);
        var safe = panel.transform.Find("SafeArea");
        var corners = new Vector3[4];
        foreach (RectTransform child in safe)
        {
            child.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var screen = camera.WorldToScreenPoint(corner);
                Check(screen.x >= -1 && screen.x <= width+1 && screen.y >= -1 && screen.y <= height+1, "HUD off-screen: " + child.name + " at " + path);
            }
        }
        var eventData = new PointerEventData(EventSystem.current) { position = new Vector2(width*.5f,height*.5f) };
        var hits = new System.Collections.Generic.List<RaycastResult>(); canvas.GetComponent<GraphicRaycaster>().Raycast(eventData,hits);
        if (FindObjectOfType<ReturnToLobbyPanel>() == null) Check(hits.Count == 0, "HUD blocks center pointer.");
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; RenderTexture.active = target;
        var pixels = new Texture2D(width,height,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
        File.WriteAllBytes(path,pixels.EncodeToPNG()); RenderTexture.active = previous;
        Check(pixels.GetPixels32().Distinct().Take(64).Count() == 64,"Blank HUD capture: " + path);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; canvas.scaleFactor = previousScale; scaler.enabled = previousScaler;
        camera.targetTexture = null; camera.cullingMask = previousMask; camera.ResetAspect(); aim.gameObject.SetActive(aimActive);
        canvas.gameObject.layer = previousCanvasLayer;
        Object.DestroyImmediate(backdrop); target.Release(); sceneTarget.Release();
        Object.DestroyImmediate(pixels); Object.DestroyImmediate(target); Object.DestroyImmediate(sceneTarget);
        for (int i = 0; i < skins.Length; i++) { skins[i].enabled = true; Object.DestroyImmediate(snapshots[i]); Object.DestroyImmediate(baked[i]); }
        Debug.Log("HUD_CAPTURE_PASS " + path);
    }
}
#endif
