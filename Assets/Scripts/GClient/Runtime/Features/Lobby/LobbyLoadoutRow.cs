using UnityEngine;
using UnityEngine.UI;

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
            icon.sprite = image;
            icon.enabled = image != null;
            accent.enabled = selected;
            title.color = selected ? new Color32(255, 164, 54, 255) : Color.white;
        }
    }
}
