#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoomsScenePlacement
{
    private const string RoomsAssetPath = "Assets/Meshes/Rooms.glb";
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private const string RoomsRootName = "Rooms";

    [MenuItem("Tools/Logo Survivor/Place Rooms In Game")]
    public static void PlaceRoomsInGame()
    {
        var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

        if (scene.GetRootGameObjects().Any(gameObject => gameObject.name == RoomsRootName))
        {
            Debug.Log($"{RoomsRootName} is already present in {GameScenePath}; no duplicate was created.");
            return;
        }

        var roomsAsset = AssetDatabase.LoadMainAssetAtPath(RoomsAssetPath) as GameObject;
        if (roomsAsset == null)
        {
            Debug.LogError($"Unable to load imported Rooms asset at {RoomsAssetPath}.");
            return;
        }

        var instance = PrefabUtility.InstantiatePrefab(roomsAsset, scene) as GameObject;
        if (instance == null)
        {
            Debug.LogError("Unable to instantiate the imported Rooms asset.");
            return;
        }

        instance.name = RoomsRootName;
        instance.transform.SetPositionAndRotation(new Vector3(0f, 0.02f, 0f), Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        foreach (var importedCamera in instance.GetComponentsInChildren<Camera>(true))
        {
            importedCamera.gameObject.SetActive(false);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Placed {RoomsRootName} in {GameScenePath} at {instance.transform.position}.");
    }
}
#endif
