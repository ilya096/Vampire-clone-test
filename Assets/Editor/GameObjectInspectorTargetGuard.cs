using UnityEditor;
using UnityEngine;

/// <summary>
/// Prevents Unity's built-in GameObjectInspector from retaining a scene object
/// after that object is destroyed or its Play Mode scene is torn down.
/// </summary>
[InitializeOnLoad]
internal static class GameObjectInspectorTargetGuard
{
    private static Object s_lastSelection;
    private static bool s_lastSelectionWasSceneObject;

    static GameObjectInspectorTargetGuard()
    {
        Selection.selectionChanged -= CacheSelection;
        Selection.selectionChanged += CacheSelection;
        EditorApplication.update -= ClearDestroyedSelection;
        EditorApplication.update += ClearDestroyedSelection;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        CacheSelection();
        EditorApplication.delayCall -= ResetInspectorTarget;
        EditorApplication.delayCall += ResetInspectorTarget;
    }

    private static void CacheSelection()
    {
        s_lastSelection = Selection.activeObject;
        s_lastSelectionWasSceneObject = IsSceneObject(s_lastSelection);
    }

    private static void ClearDestroyedSelection()
    {
        if (s_lastSelectionWasSceneObject && s_lastSelection == null)
        {
            ResetInspectorTarget();
        }
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state is PlayModeStateChange.ExitingEditMode or PlayModeStateChange.ExitingPlayMode
            && s_lastSelectionWasSceneObject)
        {
            ResetInspectorTarget();
        }
    }

    private static bool IsSceneObject(Object candidate)
    {
        return candidate != null
            && candidate is GameObject or Component
            && EditorUtility.IsPersistent(candidate) == false;
    }

    private static void ResetInspectorTarget()
    {
        Selection.activeObject = null;
        s_lastSelection = null;
        s_lastSelectionWasSceneObject = false;

        ActiveEditorTracker tracker = ActiveEditorTracker.sharedTracker;
        if (tracker != null)
        {
            tracker.isLocked = false;
            tracker.ForceRebuild();
        }
    }
}
