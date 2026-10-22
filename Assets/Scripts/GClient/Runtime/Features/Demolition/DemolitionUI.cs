using System;
using OperationBlacktide.Client.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Demolition
{
    public static class DemolitionUI
    {
        public static readonly Color Ink = BlackGoldTheme.Ink;
        public static readonly Color Row = BlackGoldTheme.Surface;
        public static readonly Color Accent = BlackGoldTheme.Gold;
        public static readonly Color Muted = BlackGoldTheme.Muted;
        public static readonly Color Blue = new Color32(115,192,220,255);
        public static readonly Color White = BlackGoldTheme.Text;
        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0,1);
            rt.anchoredPosition = new Vector2(x,-y); rt.sizeDelta = new Vector2(w,h); return rt;
        }
        public static Text Label(Transform parent, Font font, string text, float x, float y, float w, float h, int size = 22)
        {
            var rt = Rect(parent, "Label", x,y,w,Mathf.Max(h,size*1.5f)); var label = rt.gameObject.AddComponent<Text>(); label.font = font;
            label.text = text; label.fontSize = size; label.color = White; label.raycastTarget = false; label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        public static Image Fill(Transform parent,float x,float y,float w,float h,Color color)
        {
            var image=Rect(parent,"Plate",x,y,w,h).gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;return image;
        }
        public static Image Icon(Transform parent,Sprite sprite,float x,float y,float w,float h,Color? tint=null)
        {
            var image=Fill(parent,x,y,w,h,tint??Color.white);image.sprite=sprite;image.preserveAspect=true;return image;
        }
        public static void Fade(Transform parent,float x,float y,float w,float h,Color from,Color to,bool vertical=false)
        {
            var graphic=Rect(parent,"Shade",x,y,w,h).gameObject.AddComponent<DemolitionGradient>();
            graphic.From=from;graphic.To=to;graphic.Vertical=vertical;graphic.raycastTarget=false;graphic.SetAllDirty();
        }
        public static Button Button(Transform parent,Font font,string text,float x,float y,float w,Action action,bool accent=false,float height=44)
        {
            var rt=Rect(parent,text,x,y,w,height);var graphic=rt.gameObject.AddComponent<UIAngledGraphic>();graphic.color=accent?Accent:Row;
            var button=rt.gameObject.AddComponent<Button>();button.targetGraphic=graphic;
            UISoundFeedback.Attach(button);button.onClick.AddListener(()=>action());
            var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.25f,1.25f,1.25f);
            colors.pressedColor=new Color(.75f,.75f,.75f);colors.disabledColor=new Color(.45f,.45f,.45f,.55f);colors.fadeDuration=.12f;button.colors=colors;
            var label=Label(rt,font,text,16,0,w-32,height,20);label.alignment=TextAnchor.MiddleCenter;
            BlackGoldTheme.Style(button,accent);return button;
        }
        public static Button TextButton(Transform parent,Font font,string text,float x,float y,float w,Action action)
        {
            var button=Button(parent,font,text,x,y,w,action);
            button.GetComponent<UIGlassEffect>().SetKind(UIGlassEffect.Kind.Tab);return button;
        }
        public static InputField Input(Transform parent,Font font,string placeholder,float x,float y,float w,bool password=false)
        {
            var rt=Rect(parent,placeholder,x,y,w,52);var image=rt.gameObject.AddComponent<UIAngledGraphic>();image.color=new Color32(6,10,13,240);
            Fill(rt,12,50,w-24,2,new Color(1,1,1,.22f));
            var input=rt.gameObject.AddComponent<InputField>();input.targetGraphic=image;
            var text=Label(rt,font,"",18,0,w-36,52,22);text.alignment=TextAnchor.MiddleLeft;
            var hint=Label(rt,font,placeholder,18,0,w-36,52,20);hint.alignment=TextAnchor.MiddleLeft;hint.color=Muted;
            input.textComponent=text;input.placeholder=hint;input.characterLimit=32;input.caretColor=Accent;input.customCaretColor=true;
            if(password)input.contentType=InputField.ContentType.Password;
            BlackGoldTheme.Style(input);return input;
        }
        public static void Fit(RectTransform sheet,float margin=40)
        {
            if(!(sheet.parent is RectTransform parent))return;
            float scale=Mathf.Min((parent.rect.width-margin)/sheet.rect.width,(parent.rect.height-margin)/sheet.rect.height);
            sheet.localScale=Vector3.one*Mathf.Max(.2f,scale);
        }
        public static void Center(RectTransform rect)
        {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;}
    }
}
