using UnityEngine;
using UnityEngine.UI;
using OperationBlacktide.Client.UI;

namespace OperationBlacktide.Client.Features.Lobby
{
    public sealed class LobbyLoadoutRow : MonoBehaviour
    {
        public Button button;
        public Image icon, accent;
        public Text title, subtitle;
        public void Bind(string name, string detail, Sprite image, bool selected)
        {
            title.text = name;
            subtitle.text = detail;
            bool hasDetail = !string.IsNullOrEmpty(detail);
            subtitle.gameObject.SetActive(hasDetail);
            var titlePosition = title.rectTransform.anchoredPosition;
            title.rectTransform.anchoredPosition = new Vector2(titlePosition.x, hasDetail ? 13 : 0);
            icon.sprite = image;
            icon.enabled = image != null;
            accent.enabled = selected;
            title.color = selected ? BlackGoldTheme.GoldLight : BlackGoldTheme.Text;
            BlackGoldTheme.Select(button, selected);
        }
    }
}
