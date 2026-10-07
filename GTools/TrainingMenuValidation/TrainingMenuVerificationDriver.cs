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
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class TrainingMenuVerificationDriver : MonoBehaviour
{
    private void Awake() { Application.logMessageReceived += OnLog; }
    private void OnDestroy() { Application.logMessageReceived -= OnLog; }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        { File.WriteAllText("training-menu-failure.txt", message + "\n" + trace); EditorApplication.Exit(1); }
    }

    private IEnumerator Start()
    {
        yield return null; yield return null;
        var scene = FindObjectOfType<TrainingSceneController>();
        var hud = FindObjectOfType<TrainingPanel>();
        var gun = scene.Player.GetComponent<TrainingWeaponController>();
        string initial = gun.WeaponId;
        var ammo = gun.Ammo;
        Check(ammo != null && gun.CanFire && gun.Slot == 1, "Initial primary missing.");
        Check(scene.Weapons.Options(1).Any(w => w.id == "weapon_snip_awp"), "Sniper not in primary category.");
        Check(scene.Weapons.Options(2).All(w => w.category == OperationBlacktide.Client.Features.Lobby.LoadoutCategory.Pistols || w.slot == "gear.taser"), "Secondary contains primary.");
        Check(!scene.EquipSlot(0) && !scene.EquipSlot(5), "Invalid slot succeeded.");
        gun.ProcessTrigger(false, false, true, 100);
        gun.ProcessTrigger(true, true, true, 100);
        int rounds = ammo.Magazine;
        Check(rounds == ammo.Capacity - 1 && gun.TryReload(), "Initial fire/reload regression.");
        Check(scene.EquipSlot(4) && !ammo.IsReloading && gun.Ammo == null && !gun.IsScoped, "Utility switch retained gun state.");
        string firstGrenade = gun.WeaponId;
        int shots = gun.ShotsFired;
        gun.ProcessTrigger(false, false, true, 101); gun.ProcessTrigger(true, true, true, 102);
        Check(gun.ShotsFired == shots && !gun.TryReload(), "Utility fired previous gun.");
        Check(scene.EquipSlot(4) && gun.WeaponId != firstGrenade, "Repeated slot did not advance.");
        string remembered = gun.WeaponId;
        Check(scene.EquipSlot(2) && gun.Slot == 2, "Secondary slot failed.");
        Check(scene.EquipSlot(4) && gun.WeaponId == remembered, "Category did not remember previous choice.");
        Check(scene.EquipSlot(1) && gun.WeaponId == initial && gun.Ammo == ammo && ammo.Magazine == rounds, "Primary ammo was refilled during switch.");
        gun.ProcessTrigger(true, true, true, 103);
        Check(gun.ShotsFired == shots, "Held fire leaked through equipment switch.");
        gun.ProcessTrigger(false, false, true, 104); gun.ProcessTrigger(true, true, true, 104);
        Check(gun.ShotsFired == shots + 1, "Re-equipped firearm stopped firing.");
        Check(gun.TryReload(), "Re-equipped firearm stopped reloading.");
        gun.AdvanceReload(10); Check(!ammo.IsReloading && ammo.Magazine == ammo.Capacity, "Reload failed after utility switch.");

        UIRoot.Instance.HandleBackInput();
        var menu = FindObjectOfType<TrainingMenuPanel>();
        Check(menu != null && scene.IsInputBlocked && !scene.Player.InputEnabled, "Esc did not open training menu.");
        Check(!scene.EquipSlot(2), "Gameplay hotkey bypassed menu.");
        float time = scene.TrainingSeconds;
        yield return null;
        Check(scene.TrainingSeconds == time, "Training timer advances in menu.");
        Canvas.ForceUpdateCanvases();
        var menuHits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width * .5f, Screen.height * .5f) }, menuHits);
        Check(menuHits.Count > 0 && menuHits.Any(h => h.gameObject.transform.IsChildOf(menu.transform)), "Menu did not block live-screen clicks.");
        Capture("training-menu-1920.png", 1920, 1080);
        Capture("training-menu-1280.png", 1280, 720);
        Capture("training-menu-4x3.png", 1280, 960);
        menu.GetComponentsInChildren<Button>().Single(b => b.name == "Weapons").onClick.Invoke();
        Check(menu.transform.Find("Sheet/WeaponPicker").gameObject.activeSelf, "Weapon picker button failed.");
        menu.ShowWeapons(4);
        string selectedGrenade = scene.Weapons.Options(4).Last().id;
        menu.GetComponentsInChildren<Button>().Single(b => b.name == selectedGrenade).onClick.Invoke();
        Check(gun.WeaponId == selectedGrenade && !scene.Player.InputEnabled, "Menu equip did not work while paused.");
        Capture("training-weapons-grenades.png", 1920, 1080);
        Capture("training-weapons-1280.png", 1280, 720);

        foreach (int slot in new[] { 1, 2, 3, 4 })
        {
            foreach (var item in scene.Weapons.Options(slot))
            {
                Check(scene.EquipFromMenu(item.id), "Menu equip rejected " + item.id);
                yield return null;
                Check(gun.WeaponId == item.id && gun.Slot == slot && gun.HudIcon != null, "HUD equipment mismatch " + item.id);
                Check(scene.Player.GetComponentsInChildren<Transform>().Any(t => t.name == "Equipped_" + item.id), "Held model absent " + item.id);
                Check(hud.GetComponentsInChildren<Text>(true).Single(t => t.name == "WeaponName").text == item.displayName, "HUD name stale.");
                if (!gun.CanFire) Check(gun.Ammo == null && !gun.IsSniper && !gun.IsScoped, "Unsupported combat profile leaked state.");
            }
        }
        menu.ShowWeapons(1); Capture("training-weapons-primary.png", 1920, 1080);
        Check(scene.EquipFromMenu(initial), "Could not restore primary.");
        menu.GetComponentsInChildren<Button>().Single(b => b.name == "Back").onClick.Invoke();
        menu.GetComponentsInChildren<Button>().Single(b => b.name == "Return").onClick.Invoke();
        Check(FindObjectOfType<ReturnToLobbyPanel>() != null && !scene.EquipFromMenu(selectedGrenade), "Confirmation allowed equipment changes.");
        UIRoot.Instance.HandleBackInput();
        Check(FindObjectOfType<TrainingMenuPanel>() != null && scene.IsInputBlocked, "Esc from confirmation lost menu.");
        scene.RequestReturnToLobby();
        FindObjectOfType<ReturnToLobbyPanel>().GetComponentsInChildren<Button>().Single(b => b.name == "Continue").onClick.Invoke();
        yield return null;
        Check(!scene.IsInputBlocked && scene.Player.InputEnabled && UIManager.Instance.GetStackCount(UILayer.Popup) == 0, "Continue training left a popup.");
        for (int slot = 1; slot <= 4; slot++)
        {
            Check(scene.EquipSlot(slot), "Keyboard category entry failed.");
            string first = gun.WeaponId;
            int count = scene.Weapons.Options(slot).Count;
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                seen.Add(gun.WeaponId); Check(scene.EquipSlot(slot), "Category cycle failed.");
                yield return null;
            }
            Check(gun.WeaponId == first && seen.Count == count, "Category skipped/duplicated weapons.");
        }
        scene.OpenTrainingMenu(); UIRoot.Instance.HandleBackInput(); yield return null;
        Check(!scene.IsInputBlocked && FindObjectOfType<TrainingMenuPanel>() == null, "Esc did not resume training.");
        Debug.Log("TRAINING_MENU_RUNTIME_PASS: all categories, full cycles, every model, menu, modal guards, ammo and firing regression.");
        Debug.Log("TRAINING_MENU_VERIFY_PASS");
        EditorApplication.Exit(0);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static void Capture(string path, int width, int height)
    {
        var camera = Camera.main; var canvas = UIRoot.Instance.GetComponentInChildren<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.enabled = false;
        float previousScale = canvas.scaleFactor; canvas.scaleFactor = Mathf.Sqrt((float)width / 1920 * height / 1080);
        var sceneTarget = new RenderTexture(width, height, 24); var target = new RenderTexture(width, height, 24);
        int previousMask = camera.cullingMask; canvas.enabled = false; camera.targetTexture = sceneTarget; camera.aspect = (float)width / height;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = sceneTarget });
        var backdrop = new GameObject("CaptureBackground", typeof(RectTransform), typeof(RawImage)); backdrop.layer = 5;
        backdrop.transform.SetParent(canvas.transform, false); backdrop.transform.SetAsFirstSibling();
        var rect = (RectTransform)backdrop.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        backdrop.GetComponent<RawImage>().texture = sceneTarget; backdrop.GetComponent<RawImage>().raycastTarget = false;
        canvas.gameObject.layer = 5; canvas.enabled = true; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .2f;
        camera.cullingMask = 1 << 5; camera.targetTexture = target; Canvas.ForceUpdateCanvases();
        foreach (var text in FindObjectOfType<TrainingMenuPanel>().GetComponentsInChildren<Text>())
            Check(text.preferredHeight <= text.rectTransform.rect.height + 1, "Menu text clipped: " + text.name + " at " + path);
        var corners = new Vector3[4]; ((RectTransform)FindObjectOfType<TrainingMenuPanel>().transform.Find("Sheet")).GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            var screen = camera.WorldToScreenPoint(corner);
            Check(screen.x >= -1 && screen.x <= width + 1 && screen.y >= -1 && screen.y <= height + 1, "Menu is off screen at " + path);
        }
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; RenderTexture.active = target;
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
        File.WriteAllBytes(path, pixels.EncodeToPNG()); RenderTexture.active = previous;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; canvas.scaleFactor = previousScale; scaler.enabled = true;
        camera.targetTexture = null; camera.cullingMask = previousMask; camera.ResetAspect();
        Object.DestroyImmediate(backdrop); target.Release(); sceneTarget.Release();
        Object.DestroyImmediate(pixels); Object.DestroyImmediate(target); Object.DestroyImmediate(sceneTarget);
    }
}
#endif
