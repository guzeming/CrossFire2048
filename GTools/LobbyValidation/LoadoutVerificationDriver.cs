#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-100)]
public sealed class LoadoutVerificationDriver : MonoBehaviour
{
    private string userId;
    private void Awake()
    {
        userId = "loadout-verification-" + Guid.NewGuid().ToString("N");
        FindObjectOfType<AuthClient>().Session.Set(userId, "测试账号", "local-verification");
    }

    private IEnumerator Start()
    {
        yield return null; yield return null;
        try
        {
            var panel = UIManager.Instance.GetPanel<LobbyPanel>(UIPanelId.Lobby);
            Check(panel != null && panel.IsOpen, "Lobby did not open with a valid session.");
            var view = panel.GetComponent<LobbyLoadoutView>();
            var preview = FindObjectOfType<LobbyLoadoutPreview>();
            var catalog = view.catalog;
            Check(catalog.agents.Length == 10 && catalog.weapons.Length == 47 && catalog.poses.Length == 26, "Incomplete catalog.");
            foreach (var item in catalog.weapons) Check(item.prefab != null && item.thumbnail != null, "Missing weapon asset " + item.id);
            CheckSniperCategories(view, preview);
            foreach (var agent in catalog.agents)
            {
                Check(agent.prefab != null && agent.portrait != null, "Missing agent asset " + agent.id);
                view.SelectTeam(agent.team); view.SelectAgent(agent.id);
                Check(preview.AgentId == agent.id, "Agent swap failed.");
                var skin = preview.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(s => s.sharedMesh.vertexCount).First();
                var mesh = new Mesh(); skin.BakeMesh(mesh, true);
                var points = mesh.vertices.Select(skin.transform.TransformPoint).ToArray();
                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (var point in points) bounds.Encapsulate(point);
                Check(bounds.size.y > 1.65f && bounds.size.y < 2.25f && bounds.size.x < 2, "Bad skin bounds for " + agent.id + " " + bounds);
                Object.DestroyImmediate(mesh);
            }
            Debug.Log("LOADOUT_VERIFY: all ten character skins render at valid scale");
            foreach (var team in new[] { LobbyTeam.CT, LobbyTeam.T })
            {
                view.SelectTeam(team); panel.transform.Find("TopBar/Navigation/Tab1").GetComponent<Button>().onClick.Invoke();
                foreach (var item in catalog.weapons.Where(w => w.Supports(team)))
                {
                    view.SelectCategory(item.category); view.SelectSlot(item.slot);
                    string before = JsonUtility.ToJson(view.Store.Data);
                    view.SelectWeapon(item.id);
                    if ((team == LobbyTeam.CT && (item.id == "weapon_rif_m4a1_silencer" || item.id == "weapon_pist_elite")) ||
                        (team == LobbyTeam.T && (item.id == "weapon_rif_ak47" || item.id == "weapon_molotov")))
                    { Canvas.ForceUpdateCanvases(); Capture("loadout-item-" + item.id + ".png"); }
                    Check(JsonUtility.ToJson(view.Store.Data) == before, "Preview changed saved loadout.");
                    Check(preview.WeaponId == item.id, "Preview model did not switch: " + item.id);
                    var meshes = preview.GetComponentsInChildren<MeshRenderer>().Where(r => r.gameObject.activeInHierarchy).ToArray();
                    Check(meshes.Length > 0, "No displayed weapon mesh: " + item.id);
                    var weaponBounds = LobbyLoadoutPreview.StaticMeshBounds(preview.gameObject);
                    Check(weaponBounds.size.magnitude < 3 && weaponBounds.size.magnitude > .02f,
                        "Weapon scale invalid: " + item.id + " " + weaponBounds);
                    Check(Vector3.Distance(weaponBounds.center, preview.transform.position) < 3,
                        "Weapon attachment outside character: " + item.id + " " + weaponBounds);
                    view.EquipSelected();
                    Check(view.Store.Equipped(team, item.slot).id == item.id, "Equip failed: " + item.id);
                    if (item.held)
                    {
                        var hand = preview.GetComponentsInChildren<Transform>().Single(t => t.name == "hand_R");
                        Check(Vector3.Distance(hand.position, weaponBounds.center) < .85f, "Weapon is not near its hand: " + item.id + " hand=" + hand.position + " bounds=" + weaponBounds);
                        foreach (var grip in preview.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Grip_")))
                        {
                            var gripHand = preview.GetComponentsInChildren<Transform>().Single(t => t.name == "hand_" + grip.name.Last());
                            Check(Vector3.Distance(grip.position, gripHand.position) < .05f, "Grip marker is outside palm: " + item.id + " " + grip.name + " error=" + Vector3.Distance(grip.position, gripHand.position));
                        }
                        var start = hand.position;
                        preview.Animator.Play("Inspect", 0, .5f); preview.Animator.Update(0);
                        Check(Vector3.Distance(start, hand.position) > .002f, "Animation does not bind: " + item.id);
                        Debug.Log("LOADOUT_WEAPON_OK " + team + " " + item.id + " size=" + weaponBounds.size + " center=" + weaponBounds.center);
                    }
                }
            }
            view.SelectTeam(LobbyTeam.CT);
            Check(!view.Store.EquipAgent("tm_phoenix") && !view.Store.EquipWeapon("weapon_rif_ak47"), "Cross-faction equipment allowed.");
            Check(view.Store.EquipForBothTeams("weapon_pist_revolver"), "Equip both teams failed.");
            Check(view.Store.Equipped(LobbyTeam.CT,"pistol.heavy").id == "weapon_pist_revolver" && view.Store.Equipped(LobbyTeam.T,"pistol.heavy").id == "weapon_pist_revolver", "Both-team persistence failed.");
            var restored = new LobbyLoadoutStore(catalog,userId);
            Check(JsonUtility.ToJson(restored.Data) == JsonUtility.ToJson(view.Store.Data), "Reloaded configuration differs.");
            var other = new LobbyLoadoutStore(catalog,userId+"-other");
            Check(other.Equipped(LobbyTeam.CT,"pistol.heavy").id == "weapon_pist_deagle", "Account loadouts leak.");
            PlayerPrefs.SetString(other.StorageKey,"{\"version\":1,\"activeTeam\":99,\"ct\":{\"agentId\":\"tm_phoenix\",\"displayWeaponId\":\"bogus\",\"slots\":[null,{\"slot\":\"rifle.main\",\"weaponId\":\"weapon_rif_ak47\"}]},\"t\":null}");
            other = new LobbyLoadoutStore(catalog,userId+"-other");
            Check(other.Team == LobbyTeam.CT && other.Current.agentId == "ctm_sas" && other.Equipped(LobbyTeam.CT,"rifle.main").id == "weapon_rif_m4a1_silencer", "Invalid save not repaired.");
            PlayerPrefs.DeleteKey(other.StorageKey);
            view.SelectAgent("ctm_sas"); view.SetTab(1); view.SelectCategory(LoadoutCategory.Rifles); view.SelectSlot("rifle.main"); view.SelectWeapon("weapon_rif_m4a1_silencer"); view.EquipSelected();
            panel.transform.Find("TopBar/Navigation/Tab1").GetComponent<Button>().onClick.Invoke();
            Canvas.ForceUpdateCanvases(); Capture("loadout-equipment.png");
            view.inspectButton.onClick.Invoke(); preview.Animator.Play("Inspect",0,.5f); preview.Animator.Update(0);
            Capture("loadout-inspect.png");
            panel.transform.Find("TopBar/Navigation/Tab0").GetComponent<Button>().onClick.Invoke();
            view.chooseAgentButton.onClick.Invoke(); Canvas.ForceUpdateCanvases(); Capture("loadout-agents-ct.png");
            view.tButton.onClick.Invoke(); view.SelectAgent("tm_professional_varf"); Canvas.ForceUpdateCanvases(); Capture("loadout-agents-t.png");
            view.closeAgentsButton.onClick.Invoke();
            foreach (var label in panel.GetComponentsInChildren<Text>())
                foreach (char c in label.text) Check(char.IsWhiteSpace(c) || label.font.HasCharacter(c), "Missing UI character: " + c);
            Time.timeScale = 0;
            Check(preview.Animator.updateMode == AnimatorUpdateMode.UnscaledTime, "Preview uses gameplay clock.");
            Time.timeScale = 1;
            PlayerPrefs.DeleteKey(view.Store.StorageKey); PlayerPrefs.Save();
            Debug.Log("LOADOUT_VERIFY_PASS: real lobby startup, 10 agents, all CT/T weapon slots and previews, inspection bindings, invalid choices, preview isolation, account persistence, invalid-save recovery and UI captures.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            try { Capture("loadout-failure.png"); } catch (Exception captureError) { Debug.LogException(captureError); }
            PlayerPrefs.DeleteKey("OperationBlacktide.Loadout.v1."+userId);
            PlayerPrefs.DeleteKey("OperationBlacktide.Loadout.v1."+userId+"-other");
            EditorApplication.Exit(1);
        }
    }

    private void CheckSniperCategories(LobbyLoadoutView view, LobbyLoadoutPreview preview)
    {
        var expected = new[] { "weapon_snip_awp", "weapon_snip_ssg08", "weapon_snip_g3sg1", "weapon_snip_scar20" };
        Check(view.catalog.weapons.Where(w => w.category == LoadoutCategory.Snipers).Select(w => w.id).OrderBy(id => id)
            .SequenceEqual(expected.OrderBy(id => id)), "Sniper classification is incomplete.");
        Check(view.categories.Length == 6, "Sniper category button missing.");
        var inventory = new TrainingWeaponSelection(view.catalog.weapons, "weapon_snip_awp");
        foreach (string id in expected)
            Check(inventory.Select(id) && inventory.CurrentSlot == 1 && inventory.CurrentId == id,
                "Separating the sniper category removed it from the training primary slot: " + id);
        foreach (var team in new[] { LobbyTeam.CT, LobbyTeam.T })
        {
            view.SelectTeam(team);
            view.transform.Find("TopBar/Navigation/Tab1").GetComponent<Button>().onClick.Invoke();
            foreach (LoadoutCategory category in Enum.GetValues(typeof(LoadoutCategory)))
            {
                // Exercise actual button wiring; tab order must not shift enum/category bindings.
                view.categories[(int)category].onClick.Invoke();
                var visible = view.slotContent.GetComponentsInChildren<LobbyLoadoutRow>().Where(row => row != view.slotTemplate).ToArray();
                var slots = view.catalog.weapons.Where(w => w.Supports(team) && w.category == category).Select(w => "Slot_" + w.slot).Distinct().OrderBy(s => s);
                Check(visible.Select(row => row.name).OrderBy(s => s).SequenceEqual(slots), "Category shows wrong slots: " + team + "/" + category);
                Check(view.categories[(int)category].GetComponentInChildren<Text>().color == (Color)new Color32(255, 164, 54, 255),
                    "Wrong category highlighted: " + category);
            }
            view.categories[(int)LoadoutCategory.Snipers].onClick.Invoke();
            foreach (string id in new[] { "weapon_snip_awp", "weapon_snip_ssg08" })
            {
                var item = view.catalog.Weapon(id);
                view.SelectSlot(item.slot); view.SelectWeapon(id); view.EquipSelected();
                Check(preview.WeaponId == id && view.Store.Current.displayWeaponId == id, "Sniper cannot be equipped / previewed.");
                var saved = new LobbyLoadoutStore(view.catalog, userId);
                Check(saved.Equipped(team, item.slot).id == id && saved.Current.displayWeaponId == id,
                    "Existing sniper slot IDs no longer round-trip.");
            }
            view.SelectSlot("rifle.awp");
            Canvas.ForceUpdateCanvases();
            foreach (var button in view.categories)
            {
                var label = button.GetComponentInChildren<Text>();
                Check(label.preferredWidth <= label.rectTransform.rect.width + 1 && label.preferredHeight <= label.rectTransform.rect.height + 1,
                    "Category label clipped: " + label.text);
                Check(label.font.HasCharacter('狙') && label.font.HasCharacter('击'), "Font lacks sniper tab characters.");
            }
            var ordered = view.categories.OrderBy(b => ((RectTransform)b.transform).anchoredPosition.x).ToArray();
            Check(ordered.Select(b => b.GetComponentInChildren<Text>().text).SequenceEqual(new[] { "手枪", "步枪", "狙击", "微冲", "重型", "装备" }),
                "Incorrect visible tab order.");
            for (int i = 1; i < ordered.Length; i++)
            {
                var left = (RectTransform)ordered[i-1].transform; var right = (RectTransform)ordered[i].transform;
                Check(left.anchoredPosition.x + left.rect.width / 2 <= right.anchoredPosition.x - right.rect.width / 2, "Category buttons overlap.");
            }
            Capture("loadout-snipers-" + team + ".png");
        }
        Debug.Log("LOADOUT_SNIPERS_PASS: six real tab buttons, separate sniper list, CT/T availability, AWP/SSG08 preview and persistence, labels/layout.");
    }

    private static void Capture(string path)
    {
        var skins = Object.FindObjectsOfType<SkinnedMeshRenderer>().Where(s => s.enabled).ToArray();
        var snapshots = new GameObject[skins.Length]; var baked = new Mesh[skins.Length];
        for(int i=0;i<skins.Length;i++)
        {
            baked[i]=new Mesh(); skins[i].BakeMesh(baked[i],true);
            snapshots[i]=new GameObject("Capture Skin",typeof(MeshFilter),typeof(MeshRenderer));
            snapshots[i].transform.SetParent(skins[i].transform,false);
            snapshots[i].GetComponent<MeshFilter>().sharedMesh=baked[i]; snapshots[i].GetComponent<MeshRenderer>().sharedMaterials=skins[i].sharedMaterials;
            skins[i].enabled=false;
        }
        const int width=1920,height=1080;
        var camera=Camera.main;
        var canvas=UIRoot.Instance.GetComponentInChildren<Canvas>();
        var sceneTarget=new RenderTexture(width,height,24); var target=new RenderTexture(width,height,24);
        var previousMode=canvas.renderMode; int previousMask=camera.cullingMask; int previousLayer=canvas.gameObject.layer;
        var cameraData=camera.GetUniversalAdditionalCameraData(); bool post=cameraData.renderPostProcessing;
        canvas.enabled=false; camera.targetTexture=sceneTarget; camera.aspect=(float)width/height;
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=sceneTarget});
        var backdrop=new GameObject("CaptureBackground",typeof(RectTransform)); backdrop.layer=5;
        backdrop.transform.SetParent(canvas.transform,false); backdrop.transform.SetAsFirstSibling();
        var rect=(RectTransform)backdrop.transform; rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero;
        backdrop.AddComponent<RawImage>().texture=sceneTarget; canvas.enabled=true; canvas.gameObject.layer=5;
        camera.cullingMask=1<<5; cameraData.renderPostProcessing=false; camera.targetTexture=target;
        canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=.5f; Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
        var previous=RenderTexture.active; RenderTexture.active=target;
        var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
        pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply(); File.WriteAllBytes(path,pixels.EncodeToPNG());
        Check(pixels.GetPixels32().Count(p => p.r > 30 || p.g > 30 || p.b > 30) > width*height/20, "Capture is blank: " + path);
        RenderTexture.active=previous; canvas.renderMode=previousMode; canvas.worldCamera=null; camera.targetTexture=null; camera.ResetAspect();
        camera.cullingMask=previousMask; canvas.gameObject.layer=previousLayer; cameraData.renderPostProcessing=post; backdrop.SetActive(false); Object.DestroyImmediate(backdrop);
        target.Release(); sceneTarget.Release(); Object.DestroyImmediate(pixels); Object.DestroyImmediate(target); Object.DestroyImmediate(sceneTarget);
        for(int i=0;i<skins.Length;i++){skins[i].enabled=true;Object.DestroyImmediate(snapshots[i]);Object.DestroyImmediate(baked[i]);}
    }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
}
#endif
