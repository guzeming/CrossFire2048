using System;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Training
{
    public sealed class ReturnToLobbyPanel : UIPanel
    {
        [SerializeField] private Button continueButton;
        [SerializeField] private Button confirmButton;

        protected override void OnOpen(object args)
        {
            AddButton(continueButton, () => UIManager.Instance.CloseAll(UILayer.Popup));
            if (args is Action confirm) AddButton(confirmButton, () => confirm());
        }

        private void Update()
        {
            bool loading = GameSceneFlow.Instance != null && GameSceneFlow.Instance.IsLoading;
            continueButton.interactable = confirmButton.interactable = !loading;
        }
    }
}
