using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OperationBlacktide.Client.UI
{
    /// <summary>One binding per control, including template clones. Values changed by code stay silent.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Selectable))]
    public sealed class UISoundFeedback : MonoBehaviour, IPointerClickHandler, ISubmitHandler,
        IPointerDownHandler, IPointerUpHandler, IMoveHandler
    {
        [SerializeField] private UISoundCue cue = UISoundCue.Auto;
        private Selectable control;
        private Button button;
        private Slider slider;
        private Scrollbar scrollbar;
        private bool bound, dragging;
        private int valueChangedFrame = -1;

        public static void Attach(Selectable target, UISoundCue sound = UISoundCue.Auto)
        {
            if (target == null || target.name.Contains("Blocker")) return;
            var feedback = target.GetComponent<UISoundFeedback>();
            if (feedback == null) feedback = target.gameObject.AddComponent<UISoundFeedback>();
            if (sound != UISoundCue.Auto) feedback.cue = sound;
            feedback.Bind();
        }

        public static void BindAll(GameObject root)
        {
            foreach (var target in root.GetComponentsInChildren<Selectable>(true)) Attach(target);
        }

        private void OnEnable() { dragging = false; valueChangedFrame = -1; Bind(); }
        private void OnDisable() { dragging = false; }
        private void Bind()
        {
            if (!Application.isPlaying || bound) return;
            control = GetComponent<Selectable>();
            button = control as Button; slider = control as Slider; scrollbar = control as Scrollbar;
            // Button validates left-click/submit/interactability before invoking this event.
            // Keep the listener through OnDisable: an action may close its own panel first.
            if (button != null) button.onClick.AddListener(PlayButton);
            if (slider != null) slider.onValueChanged.AddListener(OnValueChanged);
            if (scrollbar != null) scrollbar.onValueChanged.AddListener(OnValueChanged);
            bound = true;
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(PlayButton);
            if (slider != null) slider.onValueChanged.RemoveListener(OnValueChanged);
            if (scrollbar != null) scrollbar.onValueChanged.RemoveListener(OnValueChanged);
        }

        private bool CanInteract => control != null && control.IsActive() && control.IsInteractable();
        private void PlayButton() => UISoundPlayer.Play(ResolveCue());

        public void OnPointerClick(PointerEventData data)
        {
            if (button != null || slider != null || scrollbar != null || data.button != PointerEventData.InputButton.Left || !CanInteract) return;
            UISoundPlayer.Play(ResolveCue());
        }

        public void OnSubmit(BaseEventData data)
        {
            if (button == null && (control is Toggle || control is Dropdown) && CanInteract)
                UISoundPlayer.Play(ResolveCue());
        }

        public void OnPointerDown(PointerEventData data)
        {
            if ((slider == null && scrollbar == null) || data.button != PointerEventData.InputButton.Left || !CanInteract) return;
            dragging = true;
            UISoundPlayer.Play(UISoundCue.Slider);
        }
        public void OnPointerUp(PointerEventData data) { dragging = false; }
        private void OnValueChanged(float value)
        {
            valueChangedFrame = Time.frameCount;
            if (dragging && CanInteract) UISoundPlayer.Play(UISoundCue.Slider);
        }
        public void OnMove(AxisEventData data)
        {
            if ((slider != null || scrollbar != null) && valueChangedFrame == Time.frameCount && CanInteract)
                UISoundPlayer.Play(UISoundCue.Slider);
        }

        public UISoundCue ResolveCue()
        {
            if (cue != UISoundCue.Auto) return cue;
            if (control == null) control = GetComponent<Selectable>();
            if (control is InputField) return UISoundCue.Input;
            if (control is Dropdown) return UISoundCue.Open;
            if (control is Toggle toggle)
                return GetComponentInParent<Dropdown>() != null ? UISoundCue.Option : toggle.isOn ? UISoundCue.ToggleOn : UISoundCue.ToggleOff;
            if (control is Slider || control is Scrollbar) return UISoundCue.Slider;
            string id = name.ToLowerInvariant();
            if (transform.parent != null && transform.parent.name == "Navigation")
            {
                switch (id)
                {
                    case "tab0": return UISoundCue.Home;
                    case "tab3": return UISoundCue.Agents;
                    case "tab1": return UISoundCue.Loadout;
                    case "tab2": return UISoundCue.Career;
                }
            }
            if (id.StartsWith("agent_", StringComparison.Ordinal)) return UISoundCue.AgentSelect;
            if (id.StartsWith("weapon_", StringComparison.Ordinal) || id.StartsWith("slot_", StringComparison.Ordinal)) return UISoundCue.Select;
            if (id == "entergamebutton") return UISoundCue.Start;
            if (id.Contains("settings")) return UISoundCue.Settings;
            if (id.Contains("equip")) return UISoundCue.Equip;
            if (id == "modebutton") return UISoundCue.Open;
            if (id == "dismiss" || id.Contains("back") || id.Contains("cancel") || id.Contains("close") ||
                id.Contains("return") || id.Contains("logout") || id == "continue" || id == "discard") return UISoundCue.Back;
            var label = GetComponentInChildren<Text>(true);
            string text = label != null ? label.text : "";
            if (text.Contains("展示") || text.Contains("双方")) return UISoundCue.Equip;
            if (text.Contains("设置")) return UISoundCue.Settings;
            if (text.Contains("返回") || text.Contains("关闭") || text.Contains("取消") || text.Contains("退出") || text.StartsWith("继续", StringComparison.Ordinal)) return UISoundCue.Back;
            // Button-based toggles bind audio before the action, so this is the destination state.
            if (text.Contains("AI 补齐") || text.StartsWith("公开", StringComparison.Ordinal))
                return text.Contains("开启") || text.Contains("是") || text == "公开房间" ? UISoundCue.ToggleOff : UISoundCue.ToggleOn;
            if (id.Contains("save") || id.Contains("confirm") || id == "loginbutton" || text.Contains("保存") ||
                text.Contains("确认") || text.Contains("准备") || text.Contains("创建房间") || text.Contains("加入")) return UISoundCue.Confirm;
            if (text.Contains("开始") || text.Contains("进入训练")) return UISoundCue.Start;
            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.name == "WeaponPicker" && !id.StartsWith("category", StringComparison.Ordinal)) return UISoundCue.Equip;
                if (parent.name == "Buy menu") return UISoundCue.Purchase;
            }
            if (id.StartsWith("tab", StringComparison.Ordinal) || id.StartsWith("category", StringComparison.Ordinal) ||
                GetComponent<UIGlassEffect>()?.Style == UIGlassEffect.Kind.Tab) return UISoundCue.Tab;
            if (GetComponent<UIGlassEffect>()?.Style == UIGlassEffect.Kind.Primary) return UISoundCue.Confirm;
            return UISoundCue.Click;
        }
    }
}
