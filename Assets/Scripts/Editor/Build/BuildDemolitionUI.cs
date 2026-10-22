using OperationBlacktide.Client.Features.Demolition;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

namespace OperationBlacktide.EditorTools
{
    public static class BuildDemolitionUI
    {
        public const string Path="Assets/Resources/UI/Panels/DemolitionPanel.prefab";
        [MenuItem("Operation Blacktide/Demolition/Build lobby panel")]
        public static void Build()
        {
            const string previewPath="Assets/UI/Art/Demolition/DustIIOverview.png";
            if(!File.Exists(previewPath))RenderMapPreview(previewPath);
            var root=new GameObject("DemolitionPanel",typeof(RectTransform));
            try
            {
                var rt=root.GetComponent<RectTransform>();rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;
                var panel=root.AddComponent<DemolitionPanel>();var so=new SerializedObject(panel);
                so.FindProperty("panelId").stringValue="Demolition";so.FindProperty("layer").intValue=(int)UILayer.Popup;
                so.FindProperty("font").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BlacktideUI.ttf");
                so.FindProperty("displayFont").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/BarlowCondensed/BarlowCondensed-ExtraBoldItalic.ttf");
                so.FindProperty("mapPreview").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Texture2D>(previewPath);so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,Path);AssetDatabase.SaveAssets();Debug.Log("DEMOLITION_UI_BUILD_PASS");
            }
            finally{Object.DestroyImmediate(root);}
        }
        static void RenderMapPreview(string path)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var scene=EditorSceneManager.NewPreviewScene();
            var previousAmbient=RenderSettings.ambientLight;var previousMode=RenderSettings.ambientMode;
            RenderTexture target=null;Texture2D pixels=null;
            try
            {
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BuildBattleArena.MapPrefab),scene);
                var light=new GameObject("Preview sunlight").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,scene);light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(48,-28,0);
                RenderSettings.ambientLight=new Color(.58f,.62f,.66f);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
                var camera=new GameObject("Preview camera").AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.scene=scene;
                camera.transform.position=new Vector3(-38,27,-6);camera.transform.LookAt(new Vector3(-65,1,-8));
                camera.fieldOfView=64;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.38f,.47f,.54f);
                target=new RenderTexture(860,392,24);camera.targetTexture=target;camera.Render();
                RenderTexture.active=target;pixels=new Texture2D(860,392,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,860,392),0,0);pixels.Apply();
                File.WriteAllBytes(path,pixels.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=null;
            }
            finally
            {
                if(target!=null)Object.DestroyImmediate(target);if(pixels!=null)Object.DestroyImmediate(pixels);
                EditorSceneManager.ClosePreviewScene(scene);RenderSettings.ambientLight=previousAmbient;RenderSettings.ambientMode=previousMode;
            }
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;importer.mipmapEnabled=false;
            importer.npotScale=TextureImporterNPOTScale.None;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
    }
}
