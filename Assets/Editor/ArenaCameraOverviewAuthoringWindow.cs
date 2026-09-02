using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class ArenaCameraOverviewAuthoringWindow : EditorWindow
{
    private const string RootName = "ArenaCameraOverviewMarkers";

    private bool _hasSceneViewRestore;
    private SceneView _previewSceneView;
    private Vector3 _restorePivot;
    private Quaternion _restoreRotation;
    private float _restoreSize;
    private bool _restoreOrthographic;
    private SceneView.CameraSettings _restoreCameraSettings;
    private Vector2 _scroll;

    [MenuItem("Tools/Logo Survivor/Camera Overview Authoring")]
    public static void Open()
    {
        GetWindow<ArenaCameraOverviewAuthoringWindow>("Camera Overview");
    }

    [MenuItem("Tools/Logo Survivor/Validate Camera Overview Markers")]
    public static void ValidateFromMenu()
    {
        ValidateSceneMarkers(logSuccess: true);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Logo Survivor · Camera Overview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Create or find the three approved shot markers. Move and rotate markers with normal Unity gizmos, edit lens/timing in the Inspector, preview each shot, then approve it.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create / Find Markers", GUILayout.Height(30f)))
            {
                CreateOrRepairMarkers();
            }

            if (GUILayout.Button("Validate All", GUILayout.Height(30f)))
            {
                ValidateSceneMarkers(logSuccess: true);
            }

            using (new EditorGUI.DisabledScope(_hasSceneViewRestore == false))
            {
                if (GUILayout.Button("Restore Scene View", GUILayout.Height(30f)))
                {
                    RestoreSceneView();
                }
            }
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (ArenaCameraOverviewShotId shotId in Enum.GetValues(typeof(ArenaCameraOverviewShotId)))
        {
            DrawMarkerCard(shotId);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawMarkerCard(ArenaCameraOverviewShotId shotId)
    {
        List<ArenaCameraOverviewMarker> matches = FindMarkers(shotId);
        ArenaCameraOverviewMarker marker = matches.Count == 1 ? matches[0] : null;

        EditorGUILayout.Space(8f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(shotId.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.ObjectField("Marker", marker, typeof(ArenaCameraOverviewMarker), true);

            if (marker == null)
            {
                EditorGUILayout.HelpBox("Marker is missing or duplicated. Use Create / Find and Validate All.", MessageType.Warning);
                return;
            }

            if (marker.ValidateForRuntime(out string validationError))
            {
                EditorGUILayout.HelpBox("Valid and owner-approved.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(validationError, MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select"))
                {
                    Selection.activeGameObject = marker.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }

                if (GUILayout.Button("Preview"))
                {
                    PreviewMarker(marker);
                }

                if (GUILayout.Button("Approve Current Shot"))
                {
                    Undo.RecordObject(marker, $"Approve {shotId} camera overview");
                    marker.SetOwnerApproved(true);
                    EditorUtility.SetDirty(marker);
                }

                using (new EditorGUI.DisabledScope(marker.OwnerApproved == false))
                {
                    if (GUILayout.Button("Clear Approval"))
                    {
                        Undo.RecordObject(marker, $"Clear {shotId} camera overview approval");
                        marker.SetOwnerApproved(false);
                        EditorUtility.SetDirty(marker);
                    }
                }
            }
        }
    }

    private static void CreateOrRepairMarkers()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create camera overview marker root");
        }

        Camera mainCamera = Camera.main;
        foreach (ArenaCameraOverviewShotId shotId in Enum.GetValues(typeof(ArenaCameraOverviewShotId)))
        {
            List<ArenaCameraOverviewMarker> matches = FindMarkers(shotId);
            if (matches.Count > 0)
            {
                continue;
            }

            GameObject markerObject = new($"CameraOverview_{shotId}");
            Undo.RegisterCreatedObjectUndo(markerObject, $"Create {shotId} camera overview marker");
            markerObject.transform.SetParent(root.transform, worldPositionStays: true);

            if (mainCamera != null)
            {
                markerObject.transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
            }

            ArenaCameraOverviewMarker marker = Undo.AddComponent<ArenaCameraOverviewMarker>(markerObject);
            marker.ConfigureDefaults(shotId);
            EditorUtility.SetDirty(marker);
        }

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        Debug.Log("[Logo Survivor][CameraOverview] Marker create/repair completed. Scene changes remain unsaved until the owner saves explicitly.");
    }

    public static bool ValidateSceneMarkers(bool logSuccess)
    {
        bool valid = true;
        foreach (ArenaCameraOverviewShotId shotId in Enum.GetValues(typeof(ArenaCameraOverviewShotId)))
        {
            List<ArenaCameraOverviewMarker> matches = FindMarkers(shotId);
            if (matches.Count != 1)
            {
                Debug.LogError($"[Logo Survivor][CameraOverview] Expected exactly one {shotId} marker, found {matches.Count}.");
                valid = false;
                continue;
            }

            if (matches[0].ValidateForRuntime(out string error) == false)
            {
                Debug.LogError($"[Logo Survivor][CameraOverview] {error}", matches[0]);
                valid = false;
            }
        }

        if (valid && logSuccess)
        {
            Debug.Log("[Logo Survivor][CameraOverview] All three markers are unique, valid and owner-approved.");
        }

        return valid;
    }

    private void PreviewMarker(ArenaCameraOverviewMarker marker)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            Debug.LogWarning("[Logo Survivor][CameraOverview] Open a Scene view before previewing a marker.");
            return;
        }

        if (_hasSceneViewRestore == false)
        {
            _previewSceneView = sceneView;
            _restorePivot = sceneView.pivot;
            _restoreRotation = sceneView.rotation;
            _restoreSize = sceneView.size;
            _restoreOrthographic = sceneView.orthographic;
            _restoreCameraSettings = sceneView.cameraSettings;
            _hasSceneViewRestore = true;
        }

        sceneView.AlignViewToObject(marker.transform);
        sceneView.orthographic = marker.Projection == ArenaCameraOverviewProjection.Orthographic;
        if (sceneView.orthographic)
        {
            sceneView.size = marker.OrthographicSize;
        }
        else
        {
            SceneView.CameraSettings settings = sceneView.cameraSettings;
            settings.fieldOfView = marker.FieldOfView;
            sceneView.cameraSettings = settings;
        }
        sceneView.Repaint();
    }

    private void RestoreSceneView()
    {
        if (_hasSceneViewRestore == false || _previewSceneView == null)
        {
            _hasSceneViewRestore = false;
            return;
        }

        _previewSceneView.pivot = _restorePivot;
        _previewSceneView.rotation = _restoreRotation;
        _previewSceneView.size = _restoreSize;
        _previewSceneView.orthographic = _restoreOrthographic;
        _previewSceneView.cameraSettings = _restoreCameraSettings;
        _previewSceneView.Repaint();
        _previewSceneView = null;
        _hasSceneViewRestore = false;
    }

    private void OnDisable()
    {
        RestoreSceneView();
    }

    private static List<ArenaCameraOverviewMarker> FindMarkers(ArenaCameraOverviewShotId shotId)
    {
        ArenaCameraOverviewMarker[] markers = UnityEngine.Object.FindObjectsByType<ArenaCameraOverviewMarker>(
            FindObjectsInactive.Include);
        List<ArenaCameraOverviewMarker> matches = new();
        foreach (ArenaCameraOverviewMarker marker in markers)
        {
            if (marker.ShotId == shotId)
            {
                matches.Add(marker);
            }
        }
        return matches;
    }
}
