#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Features.Demolition;
using OperationBlacktide.Client.Gameplay.Avatars;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Shared.Modes.Demolition;
using UnityEditor;
using UnityEngine;

public sealed class DemolitionPresentationVerificationDriver : MonoBehaviour
{
    string output;
    Camera view;
    DemolitionPresentationAssets data;
    IEnumerator Start()
    {
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--c4-output");
        output = index >= 0 ? args[index + 1] : Path.GetFullPath("../c4-presentation"); Directory.CreateDirectory(output);
        var routine = Verify();
        while (true)
        {
            object next;
            try { if (!routine.MoveNext()) break; next = routine.Current; }
            catch (Exception ex) { Debug.LogException(ex); File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); EditorApplication.Exit(1); yield break; }
            yield return next;
        }
        File.WriteAllText(Path.Combine(output, "result.txt"), "DEMOLITION_PRESENTATION_PASS");
        Debug.Log("DEMOLITION_PRESENTATION_PASS: plant/defuse bindings, models, cancel/rebind, kit timing, phase audio dedup, reconnect and reset.");
        EditorApplication.Exit(0);
    }
    IEnumerator Verify()
    {
        data = Resources.Load<DemolitionPresentationAssets>("Gameplay/Demolition/Presentation");
        var catalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.localScale = Vector3.one * 2;
        ground.GetComponent<Renderer>().material.color = new Color(.26f, .28f, .3f);
        RenderSettings.ambientLight = new Color(.65f, .65f, .65f);
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f; light.transform.rotation = Quaternion.Euler(45, -35, 0);
        view = new GameObject("Camera").AddComponent<Camera>(); view.tag = "MainCamera"; view.gameObject.AddComponent<AudioListener>();
        view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.12f, .15f, .18f);
        view.transform.position = new Vector3(2.6f, 2.5f, 3.4f); view.transform.LookAt(new Vector3(0, .85f, 0)); view.fieldOfView = 38;
        var pawn = new GameObject("C4 carrier"); var capsule = pawn.AddComponent<CharacterController>();
        capsule.height = 1.8f; capsule.radius = .3f; capsule.center = Vector3.up * .9f;
        Physics.SyncTransforms(); capsule.Move(Vector3.down * .1f);
        var pawnPosition = pawn.transform.position;
        var motor = pawn.AddComponent<CharacterMotor>(); motor.enabled = false; motor.InputEnabled = true;
        motor.BindPresentationCamera(view);
        var animatorDriver = pawn.AddComponent<CharacterAnimator>();
        var weapon = pawn.AddComponent<NetworkWeaponPresentation>();
        var visual = new GameObject("Avatar"); visual.transform.SetParent(pawn.transform, false);
        var avatar = visual.AddComponent<BattleAvatar>(); avatar.Show(catalog, "tm_phoenix", "weapon_pist_glock18");
        animatorDriver.Initialize(motor, avatar.Animator);
        weapon.Bind(motor, avatar.EquippedWeapon, "weapon_pist_glock18", true);
        var action = pawn.AddComponent<DemolitionActorPresentation>(); action.Bind(data, avatar.Animator, weapon, true);
        var world = new GameObject("Bomb").AddComponent<DemolitionBombPresentation>(); world.Initialize(data);
        var snapshot = new DemolitionSnapshot { Round = 1, Bomb = BombPhase.Carried, Phase = RoundPhase.Live, Remaining = 90 };
        world.Present(snapshot);
        action.Present(false, false, true, 0, 3);
        yield return null;
        Check(action.Bomb.activeInHierarchy && avatar.EquippedWeapon.gameObject.activeSelf, "Carried C4 should coexist with firearm");
        Check(Vector3.Distance(action.Bomb.transform.position, pawn.transform.position + Vector3.up) < 1, "Back attachment must use metres, not source centimetres");
        var originalView = view.transform.position;
        view.transform.position = new Vector3(-2.6f, 2.5f, -3.4f); view.transform.LookAt(new Vector3(0, .85f, 0));
        Capture("01-carried");
        view.transform.position = originalView; view.transform.LookAt(new Vector3(0, .85f, 0));
        action.Present(true, false, true, 0, 3);
        yield return null;
        Check(action.Interacting && !avatar.EquippedWeapon.gameObject.activeSelf, "Plant must hide the firearm");
        Check(!pawn.GetComponent<UpperBodyAim>().enabled && !pawn.GetComponent<AimLaser>().enabled, "Plant must suppress local aim and laser");
        action.Present(true, false, true, .42f, 3);
        yield return null;
        Check(action.CurrentClip == data.plant && pawn.transform.position == pawnPosition, "Plant pose/root");
        Capture("02-planting");
        action.Present(false, false, true, 0, 3);
        yield return null;
        Check(!action.Interacting && avatar.EquippedWeapon.gameObject.activeSelf && pawn.GetComponent<WeaponPresentation>().enabled, "Cancel must restore weapon actions");
        Check(pawn.GetComponent<UpperBodyAim>().enabled && pawn.GetComponent<AimLaser>().enabled, "Cancel must restore local aim and laser");
        snapshot.Bomb = BombPhase.Dropped; snapshot.BombPosition = new DemolitionPoint(0, 0, .65f); world.Present(snapshot);
        action.Present(false, false, false, 0, 3); yield return null;
        Check(world.Model.activeSelf && !action.Bomb.activeSelf, "Drop must show one world model");
        var bounds = ModelComponent.StaticMeshBounds(world.Model);
        Debug.Log("C4_GROUND_BOUNDS " + bounds);
        Check(bounds.min.y >= .01f && bounds.max.y < .7f && bounds.size.magnitude > .1f, "World C4 ground placement");
        snapshot.Bomb = BombPhase.Planted; snapshot.Phase = RoundPhase.Planted; snapshot.Remaining = 40; world.Present(snapshot);
        int sounds = world.PhaseSoundsPlayed; world.Present(snapshot); world.Present(snapshot);
        Check(sounds == 1 && world.PhaseSoundsPlayed == sounds, "Plant sound must play once");
        yield return new WaitForSeconds(.15f);
        Check(world.BeepsPlayed > 0, "Planted bomb must beep"); Capture("03-planted");
        action.Release(); weapon.PrepareChange(); avatar.Show(catalog, "ctm_sas", "weapon_pist_hkp2000");
        animatorDriver.Initialize(motor, avatar.Animator); weapon.Bind(motor, avatar.EquippedWeapon, "weapon_pist_hkp2000", true);
        action.Bind(data, avatar.Animator, weapon, true);
        action.SetObjectivePosition(new Vector3(0, 0, .65f));
        action.Present(false, true, false, 0, 10); yield return null;
        Check(action.CurrentClip == data.defuseEnter, "Defuse must enter crouch");
        action.Present(false, true, false, .45f, 10); yield return null;
        Check(action.CurrentClip == data.defuseLoop && !avatar.EquippedWeapon.gameObject.activeSelf, "Defuse loop must own pose");
        Capture("04-defusing");
        action.Present(false, false, false, 0, 10); yield return null;
        Check(avatar.EquippedWeapon.gameObject.activeSelf, "Defuse cancel restores firearm");
        int before = action.SoundsPlayed;
        action.Present(false, true, false, .5f, 5); yield return null;
        Check(action.CurrentClip == data.defuseLoop && action.SoundsPlayed == before, "Mid-defuse reconnect must skip start sound and respect kit duration");
        snapshot.Bomb = BombPhase.Defused; snapshot.Phase = RoundPhase.Result; world.Present(snapshot); world.Present(snapshot);
        action.Present(false, false, false, 0, 5); yield return null;
        Check(world.PhaseSoundsPlayed == 2 && !action.Interacting, "Defuse completion once");
        snapshot.Round = 2; snapshot.Bomb = BombPhase.Planted; snapshot.Phase = RoundPhase.Planted; world.Present(snapshot);
        Check(world.PhaseSoundsPlayed == 2, "Late join must not replay plant sound");
        snapshot.Bomb = BombPhase.Exploded; snapshot.Phase = RoundPhase.Result; world.Present(snapshot); world.Present(snapshot);
        Check(!world.Model.activeSelf && world.PhaseSoundsPlayed == 3, "Explosion hides model and sounds once");
        action.Present(true, false, true, .1f, 3); action.Release();
        Check(avatar.EquippedWeapon.gameObject.activeSelf && pawn.GetComponent<WeaponPresentation>().enabled, "Death/rebind release must restore firearm");
        snapshot.Round = 3; snapshot.Bomb = BombPhase.Carried; snapshot.Phase = RoundPhase.Freeze; world.Present(snapshot);
        Check(!world.Model.activeSelf, "Round reset hides world bomb");
        world.StopPresentation();
    }
    void Capture(string name)
    {
        var target = new RenderTexture(1200, 900, 24); view.targetTexture = target; view.Render();
        var old = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(1200, 900, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = old; view.targetTexture = null; Destroy(image); Destroy(target);
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
