using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Lobby
{
    // Legacy UGUI Dropdown does not expose its expanded state to the shared Esc router.
    public sealed class SettingsResolutionDropdown : Dropdown
    {
        public bool IsExpanded { get; private set; }
        protected override GameObject CreateDropdownList(GameObject template)
        {
            IsExpanded = true;
            var list = base.CreateDropdownList(template);
            // Keep other settings legible by preventing their text from showing through the option list.
            var background = list.GetComponent<Image>();
            background.material = null;
            background.color = new Color32(15, 18, 22, 255);
            return list;
        }
        protected override void DestroyDropdownList(GameObject dropdownList)
        {
            IsExpanded = false;
            base.DestroyDropdownList(dropdownList);
        }
    }
}
