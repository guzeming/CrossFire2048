#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class WeaponActionVerificationDriver : MonoBehaviour
{
    public bool primaryOnly;
    private static WeaponActionVerificationDriver instance;
    private void Awake()
    {
        if (instance != null) { Destroy(gameObject); return; }
        instance = this; DontDestroyOnLoad(gameObject); Application.logMessageReceived += OnLog;
    }
    private void OnLog(string message,string trace,LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        { File.WriteAllText("weapon-actions-failure.txt",message+"\n"+trace); EditorApplication.Exit(1); }
    }
    private IEnumerator Start()
    {
        if (instance != this) yield break;
        yield return null; yield return null;
        var scene = FindObjectOfType<TrainingSceneController>();
        var catalog = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons");
        var loadout = Resources.Load<LobbyLoadoutCatalog>("Loadout/LobbyCatalog");
        scene.Player.gameObject.SetActive(false); scene.enabled = false;
        foreach (var definition in catalog.weapons.Where(w => !primaryOnly && !w.IsPistol && !w.IsMelee && w.idleAnimation == null))
        {
            var player = Instantiate(Resources.Load<TrainingCharacterController>("Training/TrainingPlayer"));
            player.Initialize(Camera.main,scene.Player.transform.position,Quaternion.identity); player.enabled = false;
            var visual = new GameObject("Test Appearance"); visual.transform.SetParent(player.transform,false);
            var appearance = visual.AddComponent<LobbyLoadoutPreview>(); appearance.Show(loadout,definition.id.Contains("ak47") ? "tm_phoenix" : "ctm_sas",definition.id);
            var motion = player.gameObject.AddComponent<TrainingCharacterAnimator>(); motion.Initialize(player,appearance.Animator); motion.enabled = false;
            var gun = player.gameObject.AddComponent<TrainingWeaponController>(); gun.Initialize(player,appearance.EquippedWeapon,definition.id);
            var presentation = player.GetComponent<TrainingWeaponPresentation>(); var animator = appearance.Animator;
            var bones = player.GetComponentsInChildren<Transform>();
            var hand = bones.Single(b => b.name == "hand_L"); var ankle = bones.Single(b => b.name == "ankle_L");
            int fireLayer = animator.GetLayerIndex(TrainingWeaponPresentation.FireLayer), reloadLayer = animator.GetLayerIndex(TrainingWeaponPresentation.ReloadLayer);
            Check(fireLayer > 0 && reloadLayer > 0,"Weapon layers missing.");
            animator.Update(0); Vector3 idleHand = hand.position;
            gun.ProcessTrigger(false,false,true,100); gun.ProcessTrigger(true,true,true,100);
            Check(presentation.FireAnimationsPlayed == 1 && gun.ShotsFired == 1,"Fire animation not driven by real shot.");
            presentation.UpdatePresentation(.07f,true); presentation.UpdatePresentation(0,true);
            Check(animator.GetLayerWeight(fireLayer) == 1 && Vector3.Distance(idleHand,hand.position) > .001f,"Recoil layer has no visible motion: " + definition.id);
            Capture(player,"weapon-"+definition.id+"-fire.png");
            presentation.UpdatePresentation(1,true); presentation.UpdatePresentation(0,true);
            Check(animator.GetLayerWeight(fireLayer) == 0,"Fire action never settles.");

            Check(gun.TryReload(),"Reload could not start.");
            Check(!gun.TryReload(),"Repeated R restarts reload.");
            presentation.UpdatePresentation(0,true);
            Vector3 footBefore = ankle.position; Quaternion footRotation = ankle.rotation;
            gun.AdvanceReload(definition.reloadDuration*.2f); presentation.UpdatePresentation(0,true);
            Check(presentation.ReloadSoundsPlayed == 1 && presentation.LastReloadSound == definition.reloadSounds[0].clip,"Magazine-out sound not played once.");
            Check(player.transform.Find("Reload Audio").GetComponent<AudioSource>().isPlaying,"Reload source is silent.");
            presentation.UpdatePresentation(0,true); Check(presentation.ReloadSoundsPlayed == 1,"Repeated sound cue.");
            gun.AdvanceReload(definition.reloadDuration*.25f); presentation.UpdatePresentation(0,true);
            Check(Vector3.Distance(idleHand,hand.position) > .05f,"Reload does not move support hand: " + definition.id);
            Check(Vector3.Distance(footBefore,ankle.position) < .001f && Quaternion.Angle(footRotation,ankle.rotation) < .1f,"Reload overrides lower body.");
            Capture(player,"weapon-"+definition.id+"-reload.png");
            var reloadHand = hand.position;
            player.InputEnabled = false; gun.AdvanceReload(1); presentation.UpdatePresentation(1,true);
            Check(Mathf.Abs(gun.Ammo.ReloadProgress-.45f) < .001f && presentation.AudioPaused,"Menu did not pause reload audio / clock.");
            Check(Vector3.Distance(reloadHand,hand.position) < .001f,"Paused reload animation moved.");
            player.InputEnabled = true; presentation.UpdatePresentation(0,true); Check(!presentation.AudioPaused,"Reload audio did not resume.");
            int beforeDisable = presentation.ReloadSoundsPlayed;
            gun.enabled = false; presentation.UpdatePresentation(0,true);
            Check(animator.GetLayerWeight(reloadLayer) == 0,"Disabled weapon kept an action layer.");
            gun.enabled = true; presentation.UpdatePresentation(0,true);
            Check(presentation.ReloadSoundsPlayed == beforeDisable && Mathf.Abs(presentation.PresentedReloadProgress-.45f) < .001f,"Re-enabled weapon repeated reload sounds.");
            gun.ProcessTrigger(true,true,true,102); Check(gun.ShotsFired == 1 && presentation.FireAnimationsPlayed == 1,"Reload permits recoil or fire.");
            gun.AdvanceReload(definition.reloadDuration*.15f); presentation.UpdatePresentation(0,true);
            Capture(player,"weapon-"+definition.id+"-insert.png");
            gun.AdvanceReload(definition.reloadDuration*.22f); presentation.UpdatePresentation(0,true);
            Capture(player,"weapon-"+definition.id+"-bolt.png");
            gun.AdvanceReload(20); presentation.UpdatePresentation(0,true);
            Check(gun.Ammo.Magazine == gun.Ammo.Capacity && animator.GetLayerWeight(reloadLayer) == 0,"Reload animation / ammunition completion mismatch.");
            Check(presentation.ReloadSoundsPlayed == definition.reloadSounds.Length,"Reload skipped / duplicated sound cues.");
            Check(!gun.TryReload(),"Full magazine reloads.");
            gun.ProcessTrigger(false,false,true,104); gun.ProcessTrigger(true,true,true,104);
            Check(presentation.FireAnimationsPlayed == 2,"Next shot has no recoil.");
            gun.TryReload(); gun.AdvanceReload(definition.reloadDuration*.3f); presentation.UpdatePresentation(0,true);
            player.Respawn(); presentation.UpdatePresentation(0,true);
            Check(!gun.Ammo.IsReloading && animator.GetLayerWeight(reloadLayer) == 0 && !player.transform.Find("Reload Audio").GetComponent<AudioSource>().isPlaying,"Respawn left reload animation or sound playing.");
            // Action deltas leave directional lower-body locomotion untouched.
            animator.SetFloat("Speed",4.5f); animator.SetFloat("MoveX",1); animator.SetFloat("MoveZ",0);
            animator.Play("Locomotion",0,.35f); animator.Update(0);
            footBefore = ankle.position; footRotation = ankle.rotation;
            gun.Ammo.TryConsume(); gun.TryReload(); gun.AdvanceReload(definition.reloadDuration*.5f); presentation.UpdatePresentation(0,true);
            Check(Vector3.Distance(footBefore,ankle.position) < .001f && Quaternion.Angle(footRotation,ankle.rotation) < .1f,"Action corrupts strafe animation.");
            Capture(player,"weapon-"+definition.id+"-moving-reload.png");
            Debug.Log("WEAPON_ACTION_RUNTIME_PASS " + definition.id);
            Destroy(player.gameObject); yield return null;
        }
        yield return PrimaryWeaponVerification.Run(loadout, catalog);
        if (!primaryOnly) yield return SidearmVerification.Run(scene, loadout, catalog);
        SceneManager.LoadScene("Assets/Scenes/SampleScene.unity"); yield return null; yield return null;
        Check(FindObjectOfType<TrainingWeaponPresentation>() == null && GameObject.Find("Reload Audio") == null,"Reload presentation leaked across scene exit.");
        Debug.Log("WEAPON_ACTIONS_VERIFY_PASS"); EditorApplication.Exit(0);
    }

    public static void Capture(TrainingCharacterController player,string path)
    {
        var skins = player.GetComponentsInChildren<SkinnedMeshRenderer>(); var snapshots = new GameObject[skins.Length]; var meshes = new Mesh[skins.Length];
        for (int i=0;i<skins.Length;i++)
        {
            meshes[i] = new Mesh(); skins[i].BakeMesh(meshes[i],true);
            snapshots[i] = new GameObject("Action Capture",typeof(MeshFilter),typeof(MeshRenderer)); snapshots[i].transform.SetParent(skins[i].transform,false);
            snapshots[i].GetComponent<MeshFilter>().sharedMesh = meshes[i]; snapshots[i].GetComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials; skins[i].enabled = false;
        }
        var camera = Camera.main; var position = camera.transform.position; var rotation = camera.transform.rotation; float field = camera.fieldOfView;
        camera.transform.position = player.transform.position + new Vector3(2.3f,2.2f,3.1f);
        camera.transform.LookAt(player.transform.position+Vector3.up*1.1f); camera.fieldOfView = 34;
        var target = new RenderTexture(960,960,24); camera.targetTexture = target;
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; RenderTexture.active = target;
        var pixels = new Texture2D(960,960,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,960,960),0,0); pixels.Apply(); File.WriteAllBytes(path,pixels.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null; camera.transform.SetPositionAndRotation(position,rotation); camera.fieldOfView = field;
        target.Release(); DestroyImmediate(target); DestroyImmediate(pixels);
        for (int i=0;i<skins.Length;i++) { skins[i].enabled = true; DestroyImmediate(snapshots[i]); DestroyImmediate(meshes[i]); }
    }
    private static void Check(bool value,string message) { if (!value) throw new Exception(message); }
}
#endif
