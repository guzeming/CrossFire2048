using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildLobbyAssistant
    {
        public const string PrefabPath = "Assets/Resources/UI/LobbyAssistant.prefab";
        [MenuItem("OperationBlacktide/Lobby/Build Offline Voice Assistant")]
        public static void Build()
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
            if (font == null) throw new System.InvalidOperationException("Lobby UI font is missing.");
            var root = new GameObject("LobbyAssistant", typeof(RectTransform), typeof(CanvasGroup), typeof(AudioSource));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.53f, 0); rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, 26); rect.sizeDelta = new Vector2(310, 46);
            var plate = Box("VoiceBackground", root.transform, new Color32(9, 13, 16, 218), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            plate.raycastTarget = false;
            Box("TopRule", root.transform, new Color32(255, 203, 94, 115), new Vector2(0, 1), Vector2.one, new Vector2(16, -1), new Vector2(-16, 0));
            Box("Accent", root.transform, BlackGoldTheme.Gold, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -26), new Vector2(2, -12));
            var status = Label("Status", root.transform, font, "大厅助手", 15, BlackGoldTheme.Text,
                new Vector2(0, 1), Vector2.one, new Vector2(16, -35), new Vector2(-90, -9));
            var waveformObject = new GameObject("Waveform", typeof(RectTransform), typeof(CanvasRenderer), typeof(LobbyVoiceWaveform));
            waveformObject.transform.SetParent(root.transform, false);
            SetRect((RectTransform)waveformObject.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-16, -35), new Vector2(120, -11));
            var waveform = waveformObject.GetComponent<LobbyVoiceWaveform>(); waveform.color = BlackGoldTheme.Gold; waveform.raycastTarget = false;
            var buttonImage = Box("Talk", root.transform, new Color(0, 0, 0, 0), Vector2.one, Vector2.one, new Vector2(-83, -38), new Vector2(-10, -7));
            buttonImage.raycastTarget = true;
            var button = buttonImage.gameObject.AddComponent<Button>(); button.targetGraphic = buttonImage;
            var key = Box("Key", button.transform, new Color32(255, 203, 94, 26), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(2, -11), new Vector2(24, 11));
            Label("T", key.transform, font, "T", 13, BlackGoldTheme.GoldLight, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
            var action = Label("Action", button.transform, font, "对话", 12, BlackGoldTheme.Muted, Vector2.zero, Vector2.one, new Vector2(31, 0), Vector2.zero);
            var body = new GameObject("Conversation", typeof(RectTransform)); body.transform.SetParent(root.transform, false);
            SetRect((RectTransform)body.transform, Vector2.zero, Vector2.one, new Vector2(16, 10), new Vector2(-16, -45));
            Box("Divider", body.transform, new Color32(255, 255, 255, 20), new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero);
            Label("PlayerName", body.transform, font, "你", 13, new Color32(130, 215, 221, 255), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -37), new Vector2(36, -9));
            var player = Label("PlayerText", body.transform, font, "", 17, BlackGoldTheme.Text, new Vector2(0, 1), Vector2.one, new Vector2(44, -48), new Vector2(0, -9), TextAnchor.UpperLeft);
            Label("AssistantName", body.transform, font, "小潮", 13, BlackGoldTheme.Gold, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -85), new Vector2(36, -57));
            var reply = Label("AssistantText", body.transform, font, "", 17, BlackGoldTheme.GoldLight, Vector2.zero, Vector2.one, new Vector2(44, 0), new Vector2(0, -57), TextAnchor.UpperLeft);
            reply.resizeTextForBestFit = true; reply.resizeTextMinSize = 14; reply.resizeTextMaxSize = 17;
            body.SetActive(false);
            var assistant = root.AddComponent<LobbyVoiceAssistant>();
            var serialized = new SerializedObject(assistant);
            Bind(serialized, "statusText", status); Bind(serialized, "playerText", player); Bind(serialized, "assistantText", reply);
            Bind(serialized, "actionText", action); Bind(serialized, "conversation", body); Bind(serialized, "talkButton", button);
            Bind(serialized, "waveform", waveform); Bind(serialized, "voiceSource", root.GetComponent<AudioSource>()); Bind(serialized, "group", root.GetComponent<CanvasGroup>());
            serialized.ApplyModifiedPropertiesWithoutUndo();
            BlackGoldTheme.Style(button);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root); AssetDatabase.SaveAssets();
            Debug.Log("LOBBY_ASSISTANT_PREFAB_READY");
        }
        private static void Bind(SerializedObject obj, string name, Object value) => obj.FindProperty(name).objectReferenceValue = value;
        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = low; rect.offsetMax = high; }
        private static Image Box(string name, Transform parent, Color color, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); obj.transform.SetParent(parent, false);
            SetRect((RectTransform)obj.transform, min, max, low, high); var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        private static Text Label(string name, Transform parent, Font font, string text, int size, Color color, Vector2 min, Vector2 max, Vector2 low, Vector2 high, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)); obj.transform.SetParent(parent, false);
            SetRect((RectTransform)obj.transform, min, max, low, high);
            var label = obj.GetComponent<Text>(); label.font = font; label.fontSize = size; label.text = text; label.color = color;
            label.alignment = alignment; label.supportRichText = false; label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate; label.raycastTarget = false; return label;
        }
    }
}
