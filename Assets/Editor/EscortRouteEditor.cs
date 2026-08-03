#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EscortRoute))]
public class EscortRouteEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EscortRoute route = (EscortRoute)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Add intermediate point"))
        {
            Undo.RecordObject(route, "Add escort route point");
            GameObject point = new($"Point {route.transform.childCount - 1}");
            Undo.RegisterCreatedObjectUndo(point, "Add escort route point");
            point.transform.SetParent(route.transform);
            point.transform.position = Vector3.Lerp(route.StartPosition, route.EndPosition, 0.5f);
            route.AddIntermediatePoint(point.transform);
            EditorUtility.SetDirty(route);
            Selection.activeGameObject = point;
        }

        if (GUILayout.Button("Remove last intermediate point"))
        {
            Undo.RecordObject(route, "Remove escort route point");
            Transform removed = route.RemoveLastIntermediatePoint();
            if (removed != null)
            {
                Undo.DestroyObjectImmediate(removed.gameObject);
            }
            EditorUtility.SetDirty(route);
        }
    }
}
#endif
