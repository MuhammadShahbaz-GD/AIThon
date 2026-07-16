#if UNITY_EDITOR
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    [CustomEditor(typeof(ElasticityController2D))]
    public sealed class ElasticityController2DEditor : UnityEditor.Editor
    {
        private SerializedProperty startPoint;
        private SerializedProperty endPoint;

        private void OnEnable()
        {
            startPoint = serializedObject.FindProperty("startPoint");
            endPoint = serializedObject.FindProperty("endPoint");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            ElasticityController2D controller = (ElasticityController2D)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick Setup", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Endpoint Handles")) CreateEndpointHandles(controller);
                if (GUILayout.Button("Snap Now"))
                {
                    controller.RefreshSpriteMetrics();
                    controller.SnapToConnection();
                    EditorUtility.SetDirty(controller);
                }
            }

            using (new EditorGUI.DisabledScope(Selection.activeTransform == null ||
                                                Selection.activeTransform == controller.transform))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Selected As Start")) AssignSelected(startPoint, controller);
                if (GUILayout.Button("Selected As End")) AssignSelected(endPoint, controller);
            }

            if (startPoint.objectReferenceValue == null || endPoint.objectReferenceValue == null)
                EditorGUILayout.HelpBox(
                    "Assign Start Point and End Point, or create endpoint handles. Keep the handles outside the stretching sprite hierarchy.",
                    MessageType.Info);
        }

        private void CreateEndpointHandles(ElasticityController2D controller)
        {
            Transform visual = controller.transform;
            Transform parent = visual.parent;
            Vector3 center = visual.position;
            Vector3 axis = controller.StretchAxis == ElasticitySpriteAxis.Horizontal
                ? visual.right
                : visual.up;
            SpriteRenderer renderer = controller.GetComponent<SpriteRenderer>();
            float halfLength = renderer != null && renderer.sprite != null
                ? Mathf.Max(.25f, renderer.sprite.bounds.extents.y)
                : .5f;

            GameObject start = new GameObject(visual.name + " Start Point");
            GameObject end = new GameObject(visual.name + " End Point");
            Undo.RegisterCreatedObjectUndo(start, "Create Elasticity Endpoints");
            Undo.RegisterCreatedObjectUndo(end, "Create Elasticity Endpoints");
            Undo.SetTransformParent(start.transform, parent, "Parent Elasticity Start");
            Undo.SetTransformParent(end.transform, parent, "Parent Elasticity End");
            start.transform.position = center - axis * halfLength;
            end.transform.position = center + axis * halfLength;

            serializedObject.Update();
            startPoint.objectReferenceValue = start.transform;
            endPoint.objectReferenceValue = end.transform;
            serializedObject.ApplyModifiedProperties();
            controller.RefreshSpriteMetrics();
            controller.SnapToConnection();
            EditorUtility.SetDirty(controller);
            Selection.activeGameObject = start;
        }

        private void AssignSelected(SerializedProperty property, ElasticityController2D controller)
        {
            serializedObject.Update();
            property.objectReferenceValue = Selection.activeTransform;
            serializedObject.ApplyModifiedProperties();
            controller.SnapToConnection();
            EditorUtility.SetDirty(controller);
        }
    }
}
#endif