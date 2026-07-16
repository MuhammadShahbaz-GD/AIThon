#if UNITY_EDITOR
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Wires the separated damage/attack architecture into the playable sandbox.</summary>
    public static class RagdollDamageSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";

        [MenuItem("Tools/Ragdoll/Setup Damage And Attack Managers")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            RagdollController controller = Object.FindObjectOfType<RagdollController>();
            if (controller == null)
                throw new System.InvalidOperationException("RagdollController was not found.");

            if (controller.GetComponent<RagdollDamageManager>() == null)
                controller.gameObject.AddComponent<RagdollDamageManager>();

            GameObject room = GameObject.Find("Room");
            if (room == null)
                throw new System.InvalidOperationException("Room was not found.");

            Collider2D[] boundaries = room.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < boundaries.Length; i++)
            {
                GameObject boundary = boundaries[i].gameObject;
                RagdollAttackManager2D attack = boundary.GetComponent<RagdollAttackManager2D>();
                if (attack == null) attack = boundary.AddComponent<RagdollAttackManager2D>();

                // Boundaries deal damage only from meaningful impact speed.
                attack.Configure(RagdollAttackType.Wall, 0f, 2.5f, 3.5f, 35f);
                EditorUtility.SetDirty(boundary);
            }

            EditorUtility.SetDirty(controller.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Damage manager installed on Buddy; attack managers installed on room boundaries.");
        }
    }
}
#endif
