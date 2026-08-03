#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EscortRouteSceneSetup
{
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private const string RouteRootName = "FirstLetterPEscortRoute";

    [MenuItem("Tools/Logo Survivor/Setup First Letter Escort Route")]
    public static void SetupFirstLetterEscortRoute()
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        EscortRoute route = Object.FindAnyObjectByType<EscortRoute>();
        if (route != null)
        {
            Selection.activeGameObject = route.gameObject;
            Debug.Log("First-letter escort route already exists. Move its Start and End children in Scene view.");
            return;
        }

        PlayerView player = Object.FindAnyObjectByType<PlayerView>();
        Vector3 startPosition = player != null ? player.transform.position : Vector3.zero;
        GameObject root = new(RouteRootName);
        route = root.AddComponent<EscortRoute>();
        Transform start = CreateAnchor("Start", root.transform, startPosition);
        Transform end = CreateAnchor("End", root.transform, startPosition + Vector3.forward * 24f);
        route.Configure(start, end);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Debug.Log("Saved editable first-letter escort route. Move Start and End child objects in Scene view.");
    }

    private static Transform CreateAnchor(string name, Transform parent, Vector3 position)
    {
        GameObject anchor = new(name);
        anchor.transform.SetParent(parent);
        anchor.transform.position = position;
        return anchor.transform;
    }
}
#endif
