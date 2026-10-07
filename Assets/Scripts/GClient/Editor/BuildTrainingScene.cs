using System;
using System.IO;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OperationBlacktide.Client.Editor
{
    /// <summary>Rebuilds the training player, UI prefabs and Dust II scene wiring.</summary>
    public static class BuildTrainingScene
    {
        private static readonly Color Ink = new Color32(17, 25, 31, 245);
        private static readonly Color Accent = new Color32(255, 164, 54, 255);
        private static Font font;

        [MenuItem("OperationBlacktide/Training/Build Dust II Training")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before building the training scene.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/Resources/Training");
            AssetDatabase.Refresh();
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
            BuildPanels();
            var playerObject = new GameObject("TrainingPlayer");
            playerObject.layer = 2;
            var motor = playerObject.AddComponent<CharacterController>();
            motor.height = 1.8f; motor.radius = .3f; motor.center = new Vector3(0, .9f, 0);
            motor.stepOffset = .3f; motor.slopeLimit = 45; motor.skinWidth = .035f; motor.minMoveDistance = 0;
            playerObject.AddComponent<TrainingCharacterController>();
            var player = PrefabUtility.SaveAsPrefabAsset(playerObject, "Assets/Resources/Training/TrainingPlayer.prefab");
            Object.DestroyImmediate(playerObject);

            var scene = EditorSceneManager.OpenScene(GameSceneFlow.TrainingScenePath);
            var previous = Object.FindObjectOfType<TrainingSceneController>();
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            Camera camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Dust II camera is missing.");
            var viewer = camera.GetComponent<DustIIViewer>();
            if (viewer != null) Object.DestroyImmediate(viewer);
            camera.name = "Training Camera";
            camera.fieldOfView = 48; camera.nearClipPlane = .08f; camera.farClipPlane = 300;
            var follow = camera.GetComponent<TrainingCameraController>();
            if (follow == null) follow = camera.gameObject.AddComponent<TrainingCameraController>();

            var root = new GameObject("Training");
            var training = root.AddComponent<TrainingSceneController>();
            var spawn = new GameObject("Player Spawn").transform;
            spawn.SetParent(root.transform, false);
            Physics.SyncTransforms();
            if (!Physics.Raycast(new Vector3(-10, 5, 0), Vector3.down, out var ground, 15,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || ground.normal.y < .85f)
                throw new InvalidOperationException("Training spawn has no walkable ground.");
            spawn.position = ground.point + Vector3.up * .08f;
            if (Physics.CheckCapsule(spawn.position + Vector3.up * .35f,
                spawn.position + Vector3.up * 1.5f, .3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Training spawn is obstructed.");
            var settings = new SerializedObject(training);
            settings.FindProperty("playerPrefab").objectReferenceValue = player.GetComponent<TrainingCharacterController>();
            settings.FindProperty("spawnPoint").objectReferenceValue = spawn;
            settings.FindProperty("followCamera").objectReferenceValue = follow;
            settings.ApplyModifiedPropertiesWithoutUndo();
            follow.Follow(spawn);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("TRAINING_BUILD_PASS spawn=" + spawn.position.ToString("F3"));
        }

        private static void BuildPanels()
        {
            BuildTrainingHud.Build();

            var dialog = Rect("ReturnToLobbyPanel", null, Vector2.zero, new Vector2(570, 294));
            Box(dialog, Ink, true);
            var confirmation = dialog.gameObject.AddComponent<ReturnToLobbyPanel>();
            Configure(confirmation, "ReturnToLobby", UILayer.Popup);
            var accent = Rect("Accent", dialog, new Vector2(0, 144), new Vector2(570, 6));
            Box(accent, Accent, false);
            Label("Title", dialog, "返回大厅？", new Vector2(0, 80), new Vector2(490, 52), 34, Color.white);
            Label("Message", dialog, "当前训练将结束，是否返回大厅？", new Vector2(0, 18), new Vector2(490, 46), 23,
                new Color32(174, 187, 198, 255));
            var cancel = Button("Continue", dialog, "继续训练", new Vector2(-128, -85), new Vector2(232, 60),
                new Color32(43, 54, 63, 255), Color.white);
            var confirm = Button("Confirm", dialog, "返回大厅", new Vector2(128, -85), new Vector2(232, 60), Accent, Ink);
            Assign(confirmation, "continueButton", cancel);
            Assign(confirmation, "confirmButton", confirm);
            PrefabUtility.SaveAsPrefabAsset(dialog.gameObject, "Assets/Resources/UI/Panels/ReturnToLobbyPanel.prefab");
            Object.DestroyImmediate(dialog.gameObject);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = size; rect.anchoredPosition = position;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Image Box(RectTransform rect, Color color, bool raycast)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = raycast;
            return image;
        }

        private static void Label(string name, Transform parent, string text, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            var label = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
            label.font = font; label.text = text; label.fontSize = fontSize; label.color = color;
            label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
        }

        private static Button Button(string name, Transform parent, string label, Vector2 position, Vector2 size, Color background, Color foreground)
        {
            var rect = Rect(name, parent, position, size);
            var image = Box(rect, background, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Label("Label", rect, label, Vector2.zero, size - new Vector2(16, 8), 23, foreground);
            return button;
        }

        private static void Configure(UIPanel panel, string id, UILayer layer)
        {
            var serialized = new SerializedObject(panel);
            serialized.FindProperty("panelId").stringValue = id;
            serialized.FindProperty("layer").intValue = (int)layer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Assign(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
