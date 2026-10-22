using UnityEngine;

namespace OperationBlacktide.Client.Features.Demolition
{
    public sealed class DemolitionPresentationAssets : ScriptableObject
    {
        public GameObject bombPrefab;
        public AnimationClip plant, defuseEnter, defuseLoop;
        public AudioClip draw, initiate, click, planted, defuseStart, defused, beep, urgentBeep, explode;
        public Vector3 groundEuler = new Vector3(-90, 0, 0);
        public Vector3 worldScale = Vector3.one * 100;
        public Vector3 carryPosition = new Vector3(0, -.08f, -.18f);
        public Vector3 carryEuler = new Vector3(0, 0, 180);
    }
}
