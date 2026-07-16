#if UNITY_EDITOR
using System.IO;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace KickTheBuddy.Editor
{
    public static class ElasticitySpringDemoBuilder
    {
        const string ScenePath="Assets/GameData/Scene/ElasticitySpringDemo.unity";
        const string ArtRoot="Assets/GameData/Art/ElsticityController/Demo";
        [MenuItem("Tools/Elasticity Controller/Create Spring Demo Scene")]
        public static void CreateDemoScene()
        {
            ImportSprites();
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            CreateCamera();
            GameObject title=new GameObject("Elasticity Spring Demo");
            Label(title.transform,"ELASTICITY CONTROLLER 2D",new Vector3(0,3.55f),.085f,new Color(.65f,.92f,1));
            Label(title.transform,"Press Play to preview stretch, compression and rotation",new Vector3(0,3.12f),.045f,new Color(.72f,.76f,.84f));
            Example("ARM SPRING","Arm Spring.png",new Vector3(-4,0),.55f,.28f,.34f,0,.82f);
            Example("LEG SPRING","Leg Spring.png",Vector3.zero,.82f,.52f,.42f,1.7f,.95f);
            Example("NECK SPRING","Neck Spring.png",new Vector3(4,0),.38f,.22f,.27f,3.2f,.9f);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene,ScenePath);
            Selection.activeGameObject=title;
            Debug.Log("Created spring demo scene at "+ScenePath+". Press Play to preview endpoint motion.");
        }
        static void ImportSprites()
        {
            foreach(string name in new[]{"Arm Spring.png","Leg Spring.png","Neck Spring.png"})
            {
                string path=ArtRoot+"/"+name;
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                TextureImporter importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null)continue;
                importer.textureType=TextureImporterType.Sprite;
                importer.spriteImportMode=SpriteImportMode.Single;
                importer.spritePixelsPerUnit=100;
                importer.alphaIsTransparency=true;
                importer.mipmapEnabled=false;
                importer.filterMode=FilterMode.Bilinear;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }
        static void CreateCamera()
        {
            GameObject go=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));
            go.tag="MainCamera"; go.transform.position=new Vector3(0,0,-10);
            Camera camera=go.GetComponent<Camera>();
            camera.orthographic=true; camera.orthographicSize=4.4f;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.035f,.045f,.065f);
        }
        static void Example(string title,string spriteName,Vector3 center,float horizontal,float vertical,float speed,float phase,float width)
        {
            GameObject root=new GameObject(title.Replace(" ","_")); root.transform.position=center;
            Transform start=Handle(root.transform,"Start Point",new Vector3(0,-1.25f),new Color(.25f,.95f,.75f));
            Transform end=Handle(root.transform,"End Point",new Vector3(0,1.25f),new Color(1,.55f,.25f));
            GameObject visual=new GameObject("Elastic Spring Visual",typeof(SpriteRenderer),typeof(ElasticityController2D));
            visual.transform.SetParent(root.transform,false);
            SpriteRenderer renderer=visual.GetComponent<SpriteRenderer>();
            renderer.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot+"/"+spriteName); renderer.sortingOrder=5;
            ElasticityController2D controller=visual.GetComponent<ElasticityController2D>();
            SerializedObject data=new SerializedObject(controller);
            data.FindProperty("startPoint").objectReferenceValue=start;
            data.FindProperty("endPoint").objectReferenceValue=end;
            data.FindProperty("targetRenderer").objectReferenceValue=renderer;
            data.FindProperty("stretchAxis").enumValueIndex=(int)ElasticitySpriteAxis.Vertical;
            data.FindProperty("widthMultiplier").floatValue=width;
            data.FindProperty("minimumLengthMultiplier").floatValue=.08f;
            data.FindProperty("maximumLengthMultiplier").floatValue=8;
            data.FindProperty("followSmoothness").floatValue=10;
            data.ApplyModifiedPropertiesWithoutUndo();
            controller.RefreshSpriteMetrics(); controller.SnapToConnection();
            ElasticitySpringDemoMotion2D motion=root.AddComponent<ElasticitySpringDemoMotion2D>();
            SerializedObject md=new SerializedObject(motion);
            md.FindProperty("startPoint").objectReferenceValue=start;
            md.FindProperty("endPoint").objectReferenceValue=end;
            md.FindProperty("horizontalAmplitude").floatValue=horizontal;
            md.FindProperty("verticalAmplitude").floatValue=vertical;
            md.FindProperty("cyclesPerSecond").floatValue=speed;
            md.FindProperty("phaseOffset").floatValue=phase;
            md.ApplyModifiedPropertiesWithoutUndo();
            Label(root.transform,title,new Vector3(0,-2.12f),.06f,Color.white);
            Label(root.transform,"START",new Vector3(-.62f,-1.25f),.032f,new Color(.25f,.95f,.75f));
            Label(root.transform,"END",new Vector3(.55f,1.25f),.032f,new Color(1,.55f,.25f));
        }
        static Transform Handle(Transform parent,string name,Vector3 position,Color color)
        {
            GameObject go=GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name=name; go.transform.SetParent(parent,false); go.transform.localPosition=position; go.transform.localScale=Vector3.one*.18f;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            MeshRenderer renderer=go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=new Material(Shader.Find("Sprites/Default")){color=color}; renderer.sortingOrder=10;
            return go.transform;
        }
        static void Label(Transform parent,string text,Vector3 position,float size,Color color)
        {
            GameObject go=new GameObject(text,typeof(TextMesh)); go.transform.SetParent(parent,false); go.transform.localPosition=position;
            TextMesh mesh=go.GetComponent<TextMesh>();
            mesh.text=text; mesh.anchor=TextAnchor.MiddleCenter; mesh.alignment=TextAlignment.Center;
            mesh.fontSize=64; mesh.characterSize=size; mesh.color=color;
            mesh.GetComponent<MeshRenderer>().sortingOrder=20;
        }
    }
}
#endif
