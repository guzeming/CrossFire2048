#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Account;
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

[DefaultExecutionOrder(-100)]
public sealed class TrainingVerificationDriver : MonoBehaviour
{
    private const string UserId = "training-local-verification";
    private static TrainingVerificationDriver instance;
    private void Awake()
    {
        // Unity reloads the initial scene from its Play Mode backup, including this test object.
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        FindObjectOfType<AuthClient>().Session.Set(UserId, "训练测试", "local-verification");
        Application.logMessageReceived += OnLog;
    }

    private void OnLog(string text, string trace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
        {
            File.AppendAllText("training-failure.txt", text + "\n" + trace + "\n");
            EditorApplication.Exit(1);
        }
    }

    private IEnumerator Start()
    {
        if (instance != this) yield break;
        yield return null; yield return null;
        var flow = GameSceneFlow.Instance;
        var ui = UIManager.Instance;
        yield return ClickTraining();
        yield return WaitForScene(GameSceneFlow.TrainingScenePath);
        var training = FindObjectOfType<TrainingSceneController>();
        Check(training != null && training.Player != null, "Training player was not spawned.");
        Check(ui.GetPanel<TrainingPanel>(UIPanelId.Training).IsOpen, "Training HUD did not open.");
        Check(!ui.GetPanel<LobbyPanel>(UIPanelId.Lobby).IsOpen, "Lobby remains over the training scene.");
        Check(FindObjectOfType<OperationBlacktide.Maps.DustIIViewer>() == null, "Overview camera still controls training.");
        Check(training.Player.GetComponentsInChildren<SkinnedMeshRenderer>().Length > 0, "Player has no character visual.");
        Check(Camera.main.GetComponent<TrainingCameraController>() != null, "Camera controller is missing.");

        var player = training.Player;
        player.enabled = false;
        var characterAnimation = player.GetComponent<TrainingCharacterAnimator>();
        Check(characterAnimation != null, "Training animation driver is missing.");
        characterAnimation.enabled = false;
        var animator = player.GetComponentInChildren<Animator>();
        Settle(player);
        Check(player.IsGrounded, "Player falls through Dust II spawn ground.");
        Check(animator.GetFloat("Speed") < .05f && animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"),
            "Standing character did not return to idle.");
        Vector3 start = player.transform.position;
        Vector2 bestInput = Vector2.zero;
        float bestDistance = 0;
        foreach (Vector2 input in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
        {
            player.Respawn(); Settle(player);
            Vector3 before = player.transform.position;
            for (int i = 0; i < 30; i++) MoveAnimated(player, input, false, false, 1f / 60);
            float moved = Vector3.ProjectOnPlane(player.transform.position - before, Vector3.up).magnitude;
            if (moved > bestDistance) { bestDistance = moved; bestInput = input; }
        }
        Check(bestDistance > 1.5f, "WASD movement is blocked at the spawn.");
        player.Respawn(); Settle(player);
        for (int i = 0; i < 30; i++) MoveAnimated(player, bestInput, false, false, 1f / 60);
        Check(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.weight > .1f && c.clip.name.StartsWith("walk_")),
            "Walking does not play walking animation.");
        Camera.main.GetComponent<TrainingCameraController>().Snap();
        Capture("training-walk.png");
        player.Respawn(); Settle(player);
        for (int i = 0; i < 30; i++) MoveAnimated(player, bestInput, true, false, 1f / 60);
        Check(Vector3.ProjectOnPlane(player.transform.position - start, Vector3.up).magnitude > bestDistance + .3f,
            "Sprint does not increase movement speed.");
        Check(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.weight > .1f && c.clip.name.StartsWith("run_")),
            "Sprinting does not play running animation.");
        Camera.main.GetComponent<TrainingCameraController>().Snap();
        Capture("training-run.png");

        player.Respawn(); Settle(player);
        player.transform.rotation = Quaternion.Euler(0, 90, 0);
        for (int i = 0; i < 30; i++) MoveAnimated(player, bestInput, false, false, 1f / 60);
        Check(Vector2.Distance(new Vector2(animator.GetFloat("MoveX"), animator.GetFloat("MoveZ")),
            bestInput) < .05f, "Locomotion animation does not follow character-relative input.");

        player.Respawn(); Settle(player);
        Vector3 direction = player.transform.TransformDirection(new Vector3(bestInput.x, 0, bestInput.y));
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Verification Wall";
        wall.transform.position = start + direction * 1.5f + Vector3.up * 1.5f;
        wall.transform.rotation = Quaternion.LookRotation(direction);
        wall.transform.localScale = new Vector3(3, 3, .3f);
        Physics.SyncTransforms();
        for (int i = 0; i < 90; i++) MoveAnimated(player, bestInput, false, false, 1f / 60);
        float wallMovement = Vector3.Dot(player.transform.position - start, direction);
        Check(wallMovement > .2f && wallMovement < 1.2f, "Character did not stop against a solid wall.");
        Check(animator.GetFloat("Speed") < .05f, "Character keeps running while blocked against a wall.");
        Object.Destroy(wall);
        yield return null;

        player.Respawn(); Settle(player);
        float groundY = player.transform.position.y;
        MoveAnimated(player, Vector2.zero, false, true, 1f / 60);
        float peak = player.transform.position.y;
        for (int i = 0; i < 120; i++)
        {
            MoveAnimated(player, Vector2.zero, false, false, 1f / 60);
            peak = Mathf.Max(peak, player.transform.position.y);
            if (i == 10)
            {
                Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Airborne"), "Jump does not enter airborne animation.");
                Check(animator.GetCurrentAnimatorClipInfo(0).Any(c => c.weight > .8f && c.clip.name == "jump_stand_rifle"),
                    "In-place jump incorrectly uses the previous walking direction.");
                Camera.main.GetComponent<TrainingCameraController>().Snap();
                Capture("training-jump.png");
            }
        }
        Check(peak > groundY + .6f && player.IsGrounded, "Jump/gravity did not return the character to the ground.");
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "Landing did not restore ground animation.");
        var camera = Camera.main.GetComponent<TrainingCameraController>();
        camera.Snap();
        var screenTarget = Camera.main.WorldToScreenPoint(player.transform.position + Vector3.right * 3 + Vector3.up);
        player.Aim(screenTarget, 1f);
        Check(Vector3.Dot(player.transform.forward, Vector3.right) > .95f, "Character does not face the mouse target.");
        Capture("training-character.png");

        UIRoot.Instance.HandleBackInput(); // Same dispatch path used by Esc.
        yield return null;
        Check(ui.GetStackCount(UILayer.Popup) == 1 && training.IsInputBlocked, "Esc did not open a blocking menu.");
        Vector3 paused = player.transform.position;
        for (int i = 0; i < 30; i++) MoveAnimated(player, bestInput, true, true, 1f / 60);
        Check(Vector3.ProjectOnPlane(player.transform.position - paused, Vector3.up).magnitude < .01f,
            "Player moves while the confirmation is open.");
        Check(!camera.InputEnabled, "Camera accepts input while the confirmation is open.");
        CheckHit(ui.GetPanel<TrainingMenuPanel>(UIPanelId.TrainingMenu).transform.Find("Sheet/Home/Return").GetComponent<Button>());
        var hudReturn = ui.GetPanel<TrainingPanel>(UIPanelId.Training).transform.Find("SafeArea/Return").GetComponent<Button>();
        Check(RaycastAt(hudReturn).gameObject.GetComponent<UIModalBlocker>() != null,
            "Confirmation allows clicks through to the HUD.");
        Check(animator.GetFloat("Speed") < .05f, "Paused input does not return the character to idle.");
        Capture("training-return-confirmation.png");
        UIRoot.Instance.HandleBackInput();
        yield return null;
        Check(ui.GetStackCount(UILayer.Popup) == 0 && !training.IsInputBlocked && player.InputEnabled,
            "Second Esc did not close the dialog and restore input.");
        UIRoot.Instance.HandleBackInput();
        yield return null;
        Click(ui.GetPanel<TrainingMenuPanel>(UIPanelId.TrainingMenu).transform.Find("Sheet/Home/Continue").GetComponent<Button>());
        yield return null;
        Check(!training.IsInputBlocked, "Continue Training did not resume input.");
        UIRoot.Instance.HandleBackInput();
        yield return null;
        Click(ui.GetPanel<TrainingMenuPanel>(UIPanelId.TrainingMenu).transform.Find("Sheet/Home/Return").GetComponent<Button>());
        yield return null;
        Click(ui.GetPanel<ReturnToLobbyPanel>(UIPanelId.ReturnToLobby).transform.Find("Confirm").GetComponent<Button>());
        yield return WaitForScene(GameSceneFlow.LobbyScenePath);
        Check(GameSceneFlow.Instance == flow && flow.Auth.Session.UserId == UserId && flow.Auth.Session.IsLoggedIn,
            "Return to lobby lost the account session.");
        Check(ui.GetPanel<LobbyPanel>(UIPanelId.Lobby).IsOpen && ui.GetStackCount(UILayer.Popup) == 0,
            "Lobby UI did not recover after returning.");
        Check(FindObjectOfType<TrainingCharacterController>() == null, "Player survived leaving training.");
        Check(Cursor.lockState == CursorLockMode.None && Cursor.visible, "Lobby cursor was not restored.");
        var returnedLobby = ui.GetPanel<LobbyPanel>(UIPanelId.Lobby);
        Check(returnedLobby.SelectedModeIndex == 2, "Returning to lobby lost the selected training mode.");
        Click((Button)typeof(LobbyPanel).GetField("enterGameButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(returnedLobby));
        yield return WaitForScene(GameSceneFlow.TrainingScenePath);
        Check(FindObjectsOfType<TrainingCharacterController>().Length == 1, "Reentry duplicated or lost the player.");
        Check(ui.GetPanel<TrainingPanel>(UIPanelId.Training).IsOpen, "Reentry lost the training HUD.");
        Click(ui.GetPanel<TrainingPanel>(UIPanelId.Training).transform.Find("SafeArea/Return").GetComponent<Button>());
        yield return null;
        Click(ui.GetPanel<TrainingMenuPanel>(UIPanelId.TrainingMenu).transform.Find("Sheet/Home/Continue").GetComponent<Button>());
        yield return null;
        Check(!FindObjectOfType<TrainingSceneController>().IsInputBlocked, "Reentry confirmation did not resume input.");
        Debug.Log("TRAINING_VERIFY_PASS: lobby entry, animated walk/sprint/jump/landing, idle when blocked or paused, wall collision, mouse aim, centered camera, Esc and raycast continue/confirm, retained session, reentry.");
        EditorApplication.Exit(0);
    }

    private static IEnumerator ClickTraining()
    {
        var lobby = UIManager.Instance.GetPanel<LobbyPanel>(UIPanelId.Lobby);
        Check(lobby != null && lobby.IsOpen, "Lobby is not ready.");
        Click((Button)typeof(LobbyPanel).GetField("modeButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lobby));
        yield return null; // Let the newly enabled picker register its graphics before pointer hit testing.
        var options = (Button[])typeof(LobbyPanel).GetField("modeOptions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lobby);
        Click(options[2]);
    }

    private static RaycastResult RaycastAt(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        var pointer = new PointerEventData(EventSystem.current) { position = position };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0, "No raycast hit at " + button.name + " active=" + button.gameObject.activeInHierarchy
            + " depth=" + button.targetGraphic.depth + " position=" + position + " screen=" + Screen.width + "x" + Screen.height);
        return hits[0];
    }

    private static void CheckHit(Button button)
    {
        RaycastResult hit = RaycastAt(button);
        Check(button.IsInteractable() && hit.gameObject.GetComponentInParent<Button>() == button,
            "Button " + button.name + " is blocked by " + hit.gameObject.name);
    }

    private static void Click(Button button)
    {
        CheckHit(button);
        RaycastResult hit = RaycastAt(button);
        var pointer = new PointerEventData(EventSystem.current)
        {
            position = hit.screenPosition, button = PointerEventData.InputButton.Left, pointerCurrentRaycast = hit
        };
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }

    private static IEnumerator WaitForScene(string path)
    {
        float deadline = Time.realtimeSinceStartup + 120;
        while (SceneManager.GetActiveScene().path != path || GameSceneFlow.Instance.IsLoading)
        {
            Check(Time.realtimeSinceStartup < deadline, "Timed out loading " + path);
            yield return null;
        }
        yield return null; yield return null;
    }

    private static void Settle(TrainingCharacterController player)
    {
        for (int i = 0; i < 60; i++) MoveAnimated(player, Vector2.zero, false, false, 1f / 60);
    }

    private static void MoveAnimated(TrainingCharacterController player, Vector2 input, bool sprint, bool jump, float deltaTime)
    {
        player.Move(input, sprint, jump, deltaTime);
        player.GetComponent<TrainingCharacterAnimator>().UpdateAnimation(deltaTime);
        player.GetComponentInChildren<Animator>().Update(deltaTime);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("TRAINING_VERIFY_FAILED: " + message);
    }

    private static void Capture(string path)
    {
        const int width = 1920, height = 1080;
        var skins = Object.FindObjectsOfType<SkinnedMeshRenderer>().Where(s => s.enabled).ToArray();
        var snapshots = new GameObject[skins.Length];
        var baked = new Mesh[skins.Length];
        for (int i = 0; i < skins.Length; i++)
        {
            baked[i] = new Mesh(); skins[i].BakeMesh(baked[i], true);
            snapshots[i] = new GameObject("Capture Skin", typeof(MeshFilter), typeof(MeshRenderer));
            snapshots[i].transform.SetParent(skins[i].transform, false);
            snapshots[i].GetComponent<MeshFilter>().sharedMesh = baked[i];
            snapshots[i].GetComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials;
            skins[i].enabled = false;
        }
        var camera = Camera.main;
        var canvas = UIRoot.Instance.GetComponentInChildren<Canvas>();
        int previousCanvasLayer = canvas.gameObject.layer;
        var sceneTarget = new RenderTexture(width, height, 24);
        var target = new RenderTexture(width, height, 24);
        int previousMask = camera.cullingMask;
        canvas.enabled = false; camera.targetTexture = sceneTarget; camera.aspect = (float)width / height;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = sceneTarget });
        var backdrop = new GameObject("CaptureBackground", typeof(RectTransform), typeof(RawImage)); backdrop.layer = 5;
        backdrop.transform.SetParent(canvas.transform, false); backdrop.transform.SetAsFirstSibling();
        var rect = (RectTransform)backdrop.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        backdrop.GetComponent<RawImage>().texture = sceneTarget;
        canvas.enabled = true; canvas.gameObject.layer = 5;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .2f;
        camera.cullingMask = 1 << 5; camera.targetTexture = target; Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; RenderTexture.active = target;
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
        Check(pixels.GetPixels32().Distinct().Take(64).Count() == 64, "Training capture is blank: " + path);
        RenderTexture.active = previous; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
        camera.targetTexture = null; camera.cullingMask = previousMask; camera.ResetAspect();
        canvas.gameObject.layer = previousCanvasLayer;
        Object.DestroyImmediate(backdrop); target.Release(); sceneTarget.Release();
        Object.DestroyImmediate(pixels); Object.DestroyImmediate(target); Object.DestroyImmediate(sceneTarget);
        for (int i = 0; i < skins.Length; i++)
        {
            skins[i].enabled = true; Object.DestroyImmediate(snapshots[i]); Object.DestroyImmediate(baked[i]);
        }
    }
}
#endif
