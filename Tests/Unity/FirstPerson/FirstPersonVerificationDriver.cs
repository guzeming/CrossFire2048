#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.Gameplay.Cameras;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Client.Gameplay.Vision;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public sealed class FirstPersonVerificationDriver : MonoBehaviour
{
    private void Awake() { Application.logMessageReceived += OnLog; }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.WriteAllText("first-person-failure.txt", message + "\n" + trace); EditorApplication.Exit(1);
    }
    private IEnumerator Start()
    {
        yield return null; yield return null;
        var scene = FindObjectOfType<TrainingSceneController>();
        var player = scene.Player; var camera = player.ViewCamera;
        var follow = camera.GetComponent<TopDownCameraController>();
        scene.enabled = false; player.GetComponent<LocalPlayerInput>().enabled = false;
        player.InputEnabled = follow.InputEnabled = true;
        var catalog = Resources.Load<FirstPersonCatalog>("Gameplay/FirstPerson");
        foreach (var weapon in Resources.Load<WeaponCatalog>("Gameplay/Weapons").weapons)
        {
            var asset = catalog.Find(weapon.id);
            Check(asset != null && asset.prefab != null && asset.idle != null && asset.fire != null, "Missing FPS weapon " + weapon.id);
        }
        foreach (var asset in catalog.weapons)
        {
            var cue = Resources.Load<AudioClip>("Audio/WeaponDraw/" + asset.id);
            Check(cue != null && cue.samples > 0 && cue.length > 0, "Missing draw sound " + asset.id);
        }
        follow.UpdateView(Vector2.zero, false, true, 0, 1);
        var before = camera.transform.position; float beforeFov = camera.fieldOfView, beforeNear = camera.nearClipPlane;
        follow.SetFirstPerson(true);
        Equip(scene, "weapon_pist_glock18"); Equip(scene, "weapon_rif_ak47");
        var drawAudio = player.GetComponent<WeaponDrawAudio>();
        Check(drawAudio != null && drawAudio.LastClip.name == "weapon_rif_ak47", "Equip did not play matching draw cue");
        int drawCount = drawAudio.SoundsPlayed;
        Check(scene.CycleWeapon(-1) && scene.Weapons.CurrentId == "weapon_pist_glock18", "Wheel did not restore remembered secondary");
        Check(drawAudio.SoundsPlayed == drawCount + 1 && drawAudio.LastClip.name == "weapon_pist_glock18", "Wheel draw sound missing/duplicated");
        Check(scene.CycleWeapon(-1) && scene.Weapons.CurrentSlot == 3, "Wheel skipped knife");
        Check(scene.CycleWeapon(-1) && scene.Weapons.CurrentSlot == 4, "Wheel skipped grenade");
        Check(scene.CycleWeapon(-1) && scene.Weapons.CurrentId == "weapon_rif_ak47", "Wheel did not wrap to remembered primary");
        Check(scene.CycleWeapon(1) && scene.Weapons.CurrentSlot == 4, "Reverse wheel did not wrap");
        drawCount = drawAudio.SoundsPlayed;
        Equip(scene, scene.Weapons.CurrentId);
        Check(drawAudio.SoundsPlayed == drawCount && !scene.CycleWeapon(0), "No-op equip replayed draw audio");
        player.InputEnabled = false;
        Check(!scene.CycleWeapon(-1) && drawAudio.SoundsPlayed == drawCount, "Blocked wheel switched weapon");
        player.InputEnabled = true;
        follow.SetFirstPerson(false);
        Check(!scene.CycleWeapon(-1), "Top-down wheel stole zoom input");
        follow.SetFirstPerson(true);
        Check(drawAudio.SoundsPlayed == drawCount, "Camera toggle replayed draw audio");
        var equipment = Resources.Load<OperationBlacktide.Client.Content.EquipmentCatalog>("Loadout/EquipmentCatalog");
        var sparse = new WeaponSelection(new[] {equipment.Weapon("weapon_rif_ak47"), equipment.Weapon("weapon_knife_default_ct")}, "weapon_rif_ak47");
        Check(sparse.Cycle(1) == "weapon_knife_default_ct" && sparse.Cycle(-1) == "weapon_knife_default_ct", "Wheel failed to skip empty slots");
        var view = camera.GetComponent<FirstPersonView>();
        Check(!camera.GetComponent<CameraCutaway>().enabled && !camera.GetComponent<CharacterVision>().enabled, "Top-down occlusion still active in FPS");
        Check(Vector2.Distance(player.AimScreenPosition, camera.pixelRect.center) < .1f, "Reticle is not centred");
        float heading = player.transform.eulerAngles.y;
        player.UpdateAimInput(Vector2.zero, true, .016f);
        player.UpdateAimInput(new Vector2(12, 4), true, .016f);
        Check(Mathf.Abs(Mathf.DeltaAngle(heading, player.transform.eulerAngles.y) - 24) < .05f, "Mouse yaw lost");
        Check(Mathf.Abs(Mathf.DeltaAngle(0, player.transform.eulerAngles.x)) < .01f, "Pitch tilted player/movement");
        Check(Mathf.Abs(Mathf.DeltaAngle(0, camera.transform.eulerAngles.x) + 8) < .05f, "Mouse pitch lost");
        var rotation = camera.transform.rotation;
        player.UpdateAimInput(new Vector2(12, 4), false, .016f);
        Check(Quaternion.Angle(rotation, camera.transform.rotation) < .001f, "Blocked mouse rotated view");
        follow.UpdateView(Vector2.one, true, true, 999, .016f, true);
        Check(!follow.IsDetached && !follow.IsAimingView && camera.fieldOfView == 26, "FPS scope detached or failed");
        follow.SetFirstPerson(false);
        Check(camera.nearClipPlane == beforeNear && camera.fieldOfView == beforeFov, "Camera state not restored");
        Check(camera.GetComponent<CameraCutaway>().enabled && camera.GetComponent<CharacterVision>().enabled, "Top-down systems not restored");
        for (int i=0;i<5;i++) { follow.SetFirstPerson(true); follow.SetFirstPerson(false); }
        Check(camera.GetUniversalAdditionalCameraData().cameraStack.Count == 0, "Overlay cameras leaked");

        follow.SetFirstPerson(true);
        var motor = player.GetComponent<CharacterController>(); motor.enabled=false;
        player.transform.SetPositionAndRotation(new Vector3(-39, .2f, -3), Quaternion.Euler(0,90,0)); motor.enabled=true;
        follow.Snap(); follow.UpdateView(Vector2.zero,false,true,0,.016f);
        foreach (string id in new[]{"weapon_rif_ak47","weapon_rif_m4a1_silencer","weapon_snip_awp","weapon_pist_glock18","weapon_pist_elite","weapon_pist_usp_silencer","weapon_knife_default_ct","weapon_hegrenade"})
        {
            typeof(TrainingSceneController).GetMethod("EquipWeapon",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(scene,new object[]{id});
            if (id=="weapon_hegrenade") player.GetComponent<WeaponController>().Throwables.ProcessInput(false,false,false,true,3);
            view.Present(0); view.Present(5);
            Check(view.Model != null, "No visible hands for " + id);
            if (id=="weapon_rif_ak47")
            {
                var gunSkin=view.Model.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.name.Contains("body_hd"));
                var probe=new Mesh();gunSkin.BakeMesh(probe,true);
                var points=probe.vertices.Select(v=>camera.transform.InverseTransformPoint(gunSkin.transform.TransformPoint(v))).ToArray();
                float depth=points.Max(p=>p.z)-points.Min(p=>p.z),height=points.Max(p=>p.y)-points.Min(p=>p.y);
                Check(depth>height*2,"Weapon binding points barrel vertically instead of forward");Object.DestroyImmediate(probe);
            }
            Capture(camera, view.Model, "first-person-"+id+".png");
            var asset=catalog.Find(id);
            if(asset.reload!=null) {asset.reload.SampleAnimation(view.Model,asset.reload.length*.45f);Capture(camera,view.Model,"first-person-"+id+"-reload.png");}
            if(id=="weapon_hegrenade")
            {
                player.GetComponent<WeaponController>().Throwables.ProcessInput(true,true,false,true,.3f);view.Present(0);
                Check(view.PresentedAction=="Preparing"||view.PresentedAction=="Holding","Grenade prepare state missing");
                Capture(camera,view.Model,"first-person-grenade-prepare.png");
            }
        }
        typeof(TrainingSceneController).GetMethod("EquipWeapon",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(scene,new object[]{"weapon_rif_ak47"});
        view.Present(5);
        var gun=player.GetComponent<WeaponController>(); var actions=player.GetComponent<WeaponPresentation>();
        motor.enabled=false;player.transform.position+=Vector3.up*100;motor.enabled=true;
        follow.Snap();
        var target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.transform.position=camera.transform.position+camera.transform.forward*12;
        target.transform.localScale=Vector3.one*.25f;Physics.SyncTransforms();
        var restingRotation = camera.transform.rotation;
        gun.ProcessTrigger(false,false,true,100);gun.ProcessTrigger(true,true,true,100);
        Check(gun.LastShot.hasHit && gun.LastShot.hit.collider==target.GetComponent<Collider>(),"First-person shot misses centre target");
        view.Present(0); Check(view.PresentedAction=="fire","Shot did not drive FPS animation");
        Check(follow.FirstPersonRecoil.x > .5f && Quaternion.Angle(restingRotation, camera.transform.rotation) > .5f,
            "Real shot did not kick first-person camera");
        Check(view.Model.transform.localPosition.z < -.02f, "Viewmodel did not kick backwards");
        var firstKick = follow.FirstPersonRecoil;
        int fired = gun.ShotsFired;
        gun.ProcessTrigger(true, false, true, 100.001);
        gun.ProcessTrigger(true, false, false, 100.2);
        Check(gun.ShotsFired == fired && follow.FirstPersonRecoil == firstKick, "Rejected trigger produced recoil");
        gun.ProcessTrigger(false, false, true, 100.3); gun.ProcessTrigger(true, true, true, 100.3);
        Check(gun.ShotsFired == fired + 1 && follow.FirstPersonRecoil.x > firstKick.x, "Burst recoil did not accumulate");
        Check(!gun.LastShot.hasHit || gun.LastShot.hit.collider != target.GetComponent<Collider>(), "Following bullet ignored camera recoil");
        var pausedKick = follow.FirstPersonRecoil;
        follow.UpdateView(Vector2.zero, false, false, 0, 2);
        Check(follow.FirstPersonRecoil == pausedKick, "Blocked camera advanced recoil clock");
        follow.UpdateView(Vector2.zero, false, true, 0, 2);
        Check(Quaternion.Angle(restingRotation, camera.transform.rotation) < .01f, "Recoil did not recover to aim");
        follow.UpdateView(Vector2.zero, true, true, 0, 0);
        actions.ConfirmShot();
        Check(camera.fieldOfView == 26 && follow.FirstPersonRecoil.x > .5f, "Scoped shot has no recoil");
        follow.ResetFirstPersonRecoil(player);
        actions.ConfirmShot();
        for (int i=0;i<30;i++) follow.UpdateView(Vector2.zero, false, true, 0, 1f/30);
        var thirtyHz = follow.FirstPersonRecoil;
        follow.ResetFirstPersonRecoil(player); actions.ConfirmShot();
        for (int i=0;i<144;i++) follow.UpdateView(Vector2.zero, false, true, 0, 1f/144);
        Check(Vector2.Distance(thirtyHz, follow.FirstPersonRecoil) < .001f, "Recoil recovery depends on frame rate");
        actions.ConfirmShot(); Equip(scene, "weapon_pist_glock18");
        Check(follow.FirstPersonRecoil == Vector2.zero, "Switch retained previous gun recoil");
        Equip(scene, "weapon_rif_ak47");
        actions = player.GetComponent<WeaponPresentation>(); view.Present(0);
        gun.Ammo.TryConsume(); Check(gun.TryReload(),"Could not reload");
        actions.UpdatePresentation(0,true); view.Present(0); Check(view.PresentedAction=="reload","Reload clock did not drive FPS animation");
        int ammo=gun.Ammo.Magazine;
        actions.InitializeNetwork(player,Resources.Load<WeaponCatalog>("Gameplay/Weapons").Find(gun.WeaponId));
        actions.SetNetworkReload(actions.Definition.reloadDuration*.5f);actions.UpdatePresentation(0,true);view.Present(0);
        Check(view.PresentedAction=="reload" && Mathf.Abs(actions.PresentedReloadProgress-.5f)<.001f && gun.Ammo.Magazine==ammo,"Network viewmodel mutated ammunition or missed reload");
        actions.SetNetworkReload(0); actions.ConfirmShot();
        Check(follow.FirstPersonRecoil.x > .5f && gun.Ammo.Magazine == ammo, "Confirmed network shot failed recoil or changed local ammo");
        follow.SetFirstPerson(false);
        restingRotation = camera.transform.rotation; actions.ConfirmShot();
        Check(follow.FirstPersonRecoil == Vector2.zero && Quaternion.Angle(restingRotation, camera.transform.rotation) < .001f,
            "First-person recoil leaked into top-down camera");
        yield return null;
        Check(player.GetComponentsInChildren<Renderer>().Any(r=>r.shadowCastingMode!=ShadowCastingMode.ShadowsOnly),"Body not restored");
        Debug.Log("FIRST_PERSON_VERIFY_PASS"); EditorApplication.Exit(0);
    }

    private static void Equip(TrainingSceneController scene, string id)
    {
        Check((bool)typeof(TrainingSceneController).GetMethod("EquipWeapon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(scene, new object[] { id }), "Could not equip " + id);
    }

    private static void Capture(Camera camera,GameObject model,string path)
    {
        var output=new RenderTexture(1280,720,24); camera.targetTexture=output; camera.aspect=1280f/720;
        var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();
        var snapshots=new GameObject[skins.Length]; var meshes=new Mesh[skins.Length];
        for(int i=0;i<skins.Length;i++)
        {
            meshes[i]=new Mesh();skins[i].BakeMesh(meshes[i],true);
            snapshots[i]=new GameObject("Captured skin",typeof(MeshFilter),typeof(MeshRenderer));
            snapshots[i].layer=FirstPersonView.ViewmodelLayer;snapshots[i].transform.SetParent(skins[i].transform,false);
            snapshots[i].GetComponent<MeshFilter>().sharedMesh=meshes[i];snapshots[i].GetComponent<MeshRenderer>().sharedMaterials=skins[i].sharedMaterials;skins[i].enabled=false;
        }
        RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=output});
        var previous=RenderTexture.active;RenderTexture.active=output;
        var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
        File.WriteAllBytes(path,texture.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;
        for(int i=0;i<skins.Length;i++){skins[i].enabled=true;Object.DestroyImmediate(snapshots[i]);Object.DestroyImmediate(meshes[i]);}
        Object.DestroyImmediate(texture);Object.DestroyImmediate(output);
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
