#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Idempotent scene setup for the logical P -> R -> O zoning. The imported GLB
/// is never edited; all authored objects are scene overrides and movable markers.
/// </summary>
public static class ArenaRouteSceneSetup
{
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private const string LayoutRootName = "ArenaRoute_PRO";
    private const string GeneratedFolder = "Assets/Generated/ArenaGeometry";
    private const string StaticWallAssetPath = GeneratedFolder + "/RoomsUV2_WallsWithoutBossBoundary.asset";
    private const string BossBoundaryAssetPath = GeneratedFolder + "/RoomsUV2_BossBoundaryO.asset";
    private const string AuthoredBoundaryName = "ImportedBossBoundary_O_Authored";

    [MenuItem("Tools/Logo Survivor/Setup P-R-O Arena Zoning")]
    public static void SetupArenaRoute()
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        ArenaRouteLayout existing = FindInScene<ArenaRouteLayout>(scene);
        GameObject roomsRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "RoomsUV2" && root.activeInHierarchy);
        if (roomsRoot == null)
        {
            Debug.LogError("Active RoomsUV2 instance was not found in Game.unity.");
            return;
        }

        MeshFilter floor = FindLogoFloor(roomsRoot);
        if (floor == null || floor.sharedMesh == null)
        {
            Debug.LogError("Unable to identify the full-logo floor mesh under RoomsUV2.");
            return;
        }
        MeshFilter walls = FindLogoWalls(roomsRoot);

        Bounds bounds = floor.sharedMesh.bounds;
        float width = bounds.size.x;
        float depth = bounds.size.z;
        float markerY = bounds.max.y + Mathf.Max(0.002f, bounds.size.y * 0.02f);

        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            if (existing.ValidateConfiguration(out string existingError) == false)
            {
                Debug.LogError($"Existing P-R-O arena zoning is incomplete: {existingError}");
                return;
            }

            if (existing.SetupVersion < ArenaRouteSceneVersion.Current)
            {
                ApplyDefaultMarkerPositions(existing, bounds, markerY);
                Mesh sourceWallMesh = existing.BossBoundaryO.SourceLogoWallMesh != null
                    ? existing.BossBoundaryO.SourceLogoWallMesh
                    : walls != null ? walls.sharedMesh : null;
                GameObject authoredBoundary = EnsureDerivedBossBoundaryAssets(existing.BossBoundaryO, walls, sourceWallMesh);
                existing.BossBoundaryO.SetAuthoredBoundary(walls, sourceWallMesh, authoredBoundary);
                SnapCapturePointsToNavMesh(existing);
                existing.MarkSetupVersion(ArenaRouteSceneVersion.Current);
                EditorUtility.SetDirty(existing);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"Upgraded P-R-O arena zoning to layout version {ArenaRouteSceneVersion.Current}; no duplicate was created.");
            }
            else
            {
                Debug.Log("P-R-O arena zoning already exists; no duplicate was created.");
            }
            return;
        }

        GameObject layoutRoot = new(LayoutRootName);
        layoutRoot.transform.SetParent(floor.transform, false);
        layoutRoot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        layoutRoot.transform.localScale = Vector3.one;
        ArenaRouteLayout layout = layoutRoot.AddComponent<ArenaRouteLayout>();

        ArenaZone arenaP = CreateZone(
            "Arena_P",
            layoutRoot.transform,
            ArenaId.P,
            new Vector3(bounds.max.x - width * 0.045f, markerY, bounds.center.z),
            new Vector2(width * 0.09f, depth * 0.95f),
            new Color(0.2f, 0.65f, 1f, 0.28f));
        ArenaZone arenaR = CreateZone(
            "Arena_R",
            layoutRoot.transform,
            ArenaId.R,
            new Vector3(bounds.max.x - width * 0.125f, markerY, bounds.center.z),
            new Vector2(width * 0.085f, depth * 0.85f),
            new Color(0.2f, 1f, 0.45f, 0.28f));
        ArenaZone arenaO = CreateZone(
            "Arena_O",
            layoutRoot.transform,
            ArenaId.O,
            new Vector3(bounds.max.x - width * 0.21f, markerY, bounds.center.z),
            new Vector2(width * 0.115f, depth * 0.9f),
            new Color(1f, 0.35f, 0.2f, 0.28f));

        Transform door1 = FindDescendant(roomsRoot.transform, "Door1");
        Transform door2 = FindDescendant(roomsRoot.transform, "Door2");
        ArenaGate gatePToR = CreateGate(
            "Gate_P_to_R",
            door1,
            layoutRoot.transform,
            new Vector3(bounds.max.x - width * 0.087f, markerY, bounds.center.z),
            width,
            depth);
        ArenaGate gateRToO = CreateGate(
            "Gate_R_to_O",
            door2,
            layoutRoot.transform,
            new Vector3(bounds.max.x - width * 0.17f, markerY, bounds.center.z),
            width,
            depth);

        float captureRadius = width * 0.009f;
        Vector3 rCenter = arenaR.transform.localPosition;
        ArenaCapturePoint[] capturePoints =
        {
            CreateCapturePoint("Capture_R_1", layoutRoot.transform, rCenter + new Vector3(-width * 0.018f, 0f, -depth * 0.18f), captureRadius),
            CreateCapturePoint("Capture_R_2", layoutRoot.transform, rCenter + new Vector3(width * 0.018f, 0f, 0f), captureRadius),
            CreateCapturePoint("Capture_R_3", layoutRoot.transform, rCenter + new Vector3(-width * 0.012f, 0f, depth * 0.18f), captureRadius)
        };

        GameObject bossBoundaryObject = new("BossBoundary_O");
        bossBoundaryObject.transform.SetParent(layoutRoot.transform, false);
        bossBoundaryObject.transform.localPosition = arenaO.transform.localPosition;
        ArenaBossBoundary bossBoundary = bossBoundaryObject.AddComponent<ArenaBossBoundary>();
        Mesh sourceWall = walls != null ? walls.sharedMesh : null;
        bossBoundary.Configure(width * 0.038f, width * 0.0045f, width * 0.009f, 24, walls, sourceWall, null);
        GameObject authoredBoundaryRoot = EnsureDerivedBossBoundaryAssets(bossBoundary, walls, sourceWall);
        bossBoundary.SetAuthoredBoundary(walls, sourceWall, authoredBoundaryRoot);

        layout.Configure(arenaP, arenaR, arenaO, gatePToR, gateRToO, capturePoints, bossBoundary);
        SnapCapturePointsToNavMesh(layout);
        EditorUtility.SetDirty(layout);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = layoutRoot;

        string doorEvidence = door1 != null && door2 != null
            ? "Existing Door1 and Door2 visuals were bound as physical gates."
            : "One or more fallback gate markers were created because an existing door visual was not found.";
        Debug.Log($"Saved editable P-R-O arena zoning in Game.unity. {doorEvidence} Verify all markers in Scene view and rebuild NavMesh before runtime acceptance.");
    }

    [MenuItem("Tools/Logo Survivor/Validate P-R-O Arena Zoning")]
    public static void ValidateArenaRoute()
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        ArenaRouteLayout layout = FindInScene<ArenaRouteLayout>(scene);
        if (layout == null)
        {
            Debug.LogError("Arena zoning validation failed: layout not found.");
            return;
        }

        if (layout.ValidateConfiguration(out string error) == false)
        {
            Debug.LogError($"Arena zoning validation failed: {error}");
            return;
        }

        Debug.Log($"Arena zoning layout valid. P={layout.ArenaP.transform.position}, R={layout.ArenaR.transform.position}, O={layout.ArenaO.transform.position}.");
        foreach (ArenaCapturePoint capturePoint in layout.CapturePointsR)
        {
            bool onNavMesh = NavMesh.SamplePosition(capturePoint.transform.position, out NavMeshHit hit, 8f, NavMesh.AllAreas);
            Debug.Log($"Capture point {capturePoint.name}: world={capturePoint.transform.position}, navMesh={onNavMesh}, sampled={(onNavMesh ? hit.position.ToString() : "N/A")}.");
        }

        ArenaBossBoundary bossBoundary = layout.BossBoundaryO;
        int matchingTriangles = bossBoundary.CountMatchingImportedTriangles();
        Debug.Log($"Boss boundary imported-wall match: triangles={matchingTriangles}, readable={bossBoundary.SourceLogoWallMesh != null && bossBoundary.SourceLogoWallMesh.isReadable}.");
        int validLinkEndpoints = 0;
        const int linkCount = 8;
        for (int index = 0; index < linkCount; index++)
        {
            float angle = index / (float)linkCount * Mathf.PI * 2f;
            Vector3 radial = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float span = bossBoundary.LocalThickness * 2.5f;
            Vector3 outside = bossBoundary.transform.TransformPoint(radial * (bossBoundary.LocalRadius + span));
            Vector3 inside = bossBoundary.transform.TransformPoint(radial * Mathf.Max(0.01f, bossBoundary.LocalRadius - span));
            bool outsideValid = NavMesh.SamplePosition(outside, out _, 5f, NavMesh.AllAreas);
            bool insideValid = NavMesh.SamplePosition(inside, out _, 5f, NavMesh.AllAreas);
            if (outsideValid && insideValid)
            {
                validLinkEndpoints++;
            }
        }
        Debug.Log($"Boss boundary planned NavMesh links: validEndpointPairs={validLinkEndpoints}/{linkCount}.");
        ValidateCaptureProgressBehavior();
        ValidateFinalBossBehavior();

        GameObject roomsRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "RoomsUV2" && root.activeInHierarchy);
        if (roomsRoot == null)
        {
            Debug.LogError("Arena zoning validation failed: active RoomsUV2 not found.");
            return;
        }

        Renderer[] doorRenderers = roomsRoot.GetComponentsInChildren<Renderer>(true)
            .Where(IsDoorRenderer)
            .ToArray();
        foreach (Renderer renderer in doorRenderers)
        {
            Debug.Log($"Door candidate {GetHierarchyPath(renderer.transform)}: center={renderer.bounds.center}, size={renderer.bounds.size}.");
        }
        Debug.Log($"Arena zoning validation completed with {doorRenderers.Length} door renderer candidates.");
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .FirstOrDefault();
    }

    private static MeshFilter FindLogoFloor(GameObject roomsRoot)
    {
        MeshFilter[] filters = roomsRoot.GetComponentsInChildren<MeshFilter>(true)
            .Where(filter => filter.sharedMesh != null && filter.gameObject.activeInHierarchy)
            .ToArray();
        MeshFilter namedFloor = filters.FirstOrDefault(filter =>
            filter.name.IndexOf("трасса-1", StringComparison.OrdinalIgnoreCase) >= 0);
        return namedFloor != null
            ? namedFloor
            : filters.OrderByDescending(filter => filter.sharedMesh.bounds.size.x * filter.sharedMesh.bounds.size.z).FirstOrDefault();
    }

    private static MeshFilter FindLogoWalls(GameObject roomsRoot)
    {
        return roomsRoot.GetComponentsInChildren<MeshFilter>(true)
            .FirstOrDefault(filter => filter.sharedMesh != null
                && filter.gameObject.activeInHierarchy
                && filter.name.IndexOf("стенки", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static GameObject EnsureDerivedBossBoundaryAssets(
        ArenaBossBoundary bossBoundary,
        MeshFilter wallFilter,
        Mesh sourceMesh)
    {
        if (bossBoundary == null || wallFilter == null || sourceMesh == null)
        {
            Debug.LogWarning("Derived boss boundary assets were not generated because the wall source is incomplete.");
            return null;
        }
        if (sourceMesh.isReadable == false || sourceMesh.subMeshCount != 1)
        {
            Debug.LogWarning("Derived boss boundary assets require a readable single-submesh wall mesh.");
            return null;
        }

        Vector3[] vertices = sourceMesh.vertices;
        int[] triangles = sourceMesh.triangles;
        var staticTriangles = new List<int>(triangles.Length);
        var boundaryTriangles = new List<int>();
        float tolerance = Mathf.Max(bossBoundary.LocalThickness * 2.5f, 0.015f);
        for (int index = 0; index < triangles.Length; index += 3)
        {
            Vector3 a = bossBoundary.transform.InverseTransformPoint(wallFilter.transform.TransformPoint(vertices[triangles[index]]));
            Vector3 b = bossBoundary.transform.InverseTransformPoint(wallFilter.transform.TransformPoint(vertices[triangles[index + 1]]));
            Vector3 c = bossBoundary.transform.InverseTransformPoint(wallFilter.transform.TransformPoint(vertices[triangles[index + 2]]));
            Vector3 centroid = (a + b + c) / 3f;
            float radius = new Vector2(centroid.x, centroid.z).magnitude;
            List<int> destination = Mathf.Abs(radius - bossBoundary.LocalRadius) <= tolerance
                ? boundaryTriangles
                : staticTriangles;
            destination.Add(triangles[index]);
            destination.Add(triangles[index + 1]);
            destination.Add(triangles[index + 2]);
        }

        if (boundaryTriangles.Count < 12)
        {
            Debug.LogError($"Derived boss boundary generation found only {boundaryTriangles.Count / 3} triangles.");
            return null;
        }

        EnsureGeneratedFolder();
        Mesh staticMesh = InstantiateMeshWithTriangles(sourceMesh, staticTriangles, "RoomsUV2_WallsWithoutBossBoundary");
        Mesh boundaryMesh = InstantiateMeshWithTriangles(sourceMesh, boundaryTriangles, "RoomsUV2_BossBoundaryO");
        Mesh staticAsset = SaveOrUpdateMeshAsset(staticMesh, StaticWallAssetPath);
        Mesh boundaryAsset = SaveOrUpdateMeshAsset(boundaryMesh, BossBoundaryAssetPath);
        wallFilter.sharedMesh = staticAsset;
        EditorUtility.SetDirty(wallFilter);

        Transform authoredTransform = wallFilter.transform.Find(AuthoredBoundaryName);
        GameObject authoredRoot = authoredTransform != null ? authoredTransform.gameObject : new GameObject(AuthoredBoundaryName);
        authoredRoot.transform.SetParent(wallFilter.transform, false);
        authoredRoot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        authoredRoot.transform.localScale = Vector3.one;
        MeshFilter authoredFilter = authoredRoot.GetComponent<MeshFilter>();
        if (authoredFilter == null) authoredFilter = authoredRoot.AddComponent<MeshFilter>();
        authoredFilter.sharedMesh = boundaryAsset;
        MeshRenderer authoredRenderer = authoredRoot.GetComponent<MeshRenderer>();
        if (authoredRenderer == null) authoredRenderer = authoredRoot.AddComponent<MeshRenderer>();
        MeshRenderer sourceRenderer = wallFilter.GetComponent<MeshRenderer>();
        if (sourceRenderer != null)
        {
            authoredRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            authoredRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            authoredRenderer.receiveShadows = sourceRenderer.receiveShadows;
        }
        authoredRoot.SetActive(true);
        EditorUtility.SetDirty(authoredRoot);
        Debug.Log($"Derived boss boundary assets saved: staticTriangles={staticTriangles.Count / 3}, boundaryTriangles={boundaryTriangles.Count / 3}.");
        return authoredRoot;
    }

    private static Mesh InstantiateMeshWithTriangles(Mesh source, List<int> triangles, string name)
    {
        Mesh mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = name;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh SaveOrUpdateMeshAsset(Mesh generated, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        EditorUtility.CopySerialized(generated, existing);
        existing.name = generated.name;
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(generated);
        return existing;
    }

    private static void EnsureGeneratedFolder()
    {
        if (AssetDatabase.IsValidFolder("Assets/Generated") == false)
        {
            AssetDatabase.CreateFolder("Assets", "Generated");
        }
        if (AssetDatabase.IsValidFolder(GeneratedFolder) == false)
        {
            AssetDatabase.CreateFolder("Assets/Generated", "ArenaGeometry");
        }
    }

    private static ArenaZone CreateZone(
        string name,
        Transform parent,
        ArenaId arena,
        Vector3 localPosition,
        Vector2 localSize,
        Color color)
    {
        GameObject zoneObject = new(name);
        zoneObject.transform.SetParent(parent, false);
        zoneObject.transform.localPosition = localPosition;
        ArenaZone zone = zoneObject.AddComponent<ArenaZone>();
        zone.Configure(arena, localSize, color);
        return zone;
    }

    private static void ApplyDefaultMarkerPositions(ArenaRouteLayout layout, Bounds bounds, float markerY)
    {
        float width = bounds.size.x;
        float depth = bounds.size.z;
        layout.ArenaP.transform.localPosition = new Vector3(bounds.max.x - width * 0.045f, markerY, bounds.center.z);
        layout.ArenaR.transform.localPosition = new Vector3(bounds.max.x - width * 0.125f, markerY, bounds.center.z);
        layout.ArenaO.transform.localPosition = new Vector3(bounds.max.x - width * 0.21f, markerY, bounds.center.z);

        Vector3 rCenter = layout.ArenaR.transform.localPosition;
        layout.CapturePointsR[0].transform.localPosition = rCenter + new Vector3(-width * 0.018f, 0f, -depth * 0.14f);
        layout.CapturePointsR[1].transform.localPosition = rCenter + new Vector3(width * 0.018f, 0f, 0f);
        layout.CapturePointsR[2].transform.localPosition = rCenter + new Vector3(-width * 0.012f, 0f, depth * 0.14f);
        layout.BossBoundaryO.transform.localPosition = layout.ArenaO.transform.localPosition;
    }

    private static void SnapCapturePointsToNavMesh(ArenaRouteLayout layout)
    {
        var reservedPositions = new List<Vector3>();
        foreach (ArenaCapturePoint capturePoint in layout.CapturePointsR)
        {
            Vector3 requested = capturePoint.transform.position;
            float minimumSeparation = capturePoint.LocalRadius
                * Mathf.Max(capturePoint.transform.lossyScale.x, capturePoint.transform.lossyScale.z)
                * 1.5f;
            if (TryFindNavMeshPositionInsideZone(
                layout.ArenaR,
                requested,
                reservedPositions,
                minimumSeparation,
                out Vector3 resolved))
            {
                capturePoint.transform.position = resolved + Vector3.up * 0.03f;
                reservedPositions.Add(resolved);
                EditorUtility.SetDirty(capturePoint.transform);
            }
            else
            {
                Debug.LogWarning($"Capture point {capturePoint.name} could not be placed on NavMesh inside arena R; adjust it manually before runtime acceptance.");
            }
        }
    }

    private static bool TryFindNavMeshPositionInsideZone(
        ArenaZone zone,
        Vector3 requested,
        IReadOnlyList<Vector3> reservedPositions,
        float minimumSeparation,
        out Vector3 resolved)
    {
        Vector2 size = zone.LocalSize;
        bool found = false;
        float bestDistance = float.PositiveInfinity;
        Vector3 bestPosition = requested;

        ConsiderCandidate(requested, 10f);
        for (int xIndex = -6; xIndex <= 6; xIndex++)
        {
            for (int zIndex = -6; zIndex <= 6; zIndex++)
            {
                Vector3 localCandidate = new(
                    xIndex / 12f * size.x,
                    0f,
                    zIndex / 12f * size.y);
                Vector3 worldCandidate = zone.transform.TransformPoint(localCandidate);
                ConsiderCandidate(worldCandidate, 4f);
            }
        }

        resolved = bestPosition;
        return found;

        void ConsiderCandidate(Vector3 candidate, float sampleRadius)
        {
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas) == false
                || zone.Contains(hit.position) == false
                || IsReserved(hit.position))
            {
                return;
            }

            float distance = (hit.position - requested).sqrMagnitude;
            if (distance >= bestDistance)
            {
                return;
            }

            bestDistance = distance;
            bestPosition = hit.position;
            found = true;
        }

        bool IsReserved(Vector3 candidate)
        {
            foreach (Vector3 reserved in reservedPositions)
            {
                Vector2 offset = new(candidate.x - reserved.x, candidate.z - reserved.z);
                if (offset.sqrMagnitude < minimumSeparation * minimumSeparation)
                {
                    return true;
                }
            }
            return false;
        }
    }

    private static ArenaCapturePoint CreateCapturePoint(
        string name,
        Transform parent,
        Vector3 localPosition,
        float localRadius)
    {
        GameObject pointObject = new(name);
        pointObject.transform.SetParent(parent, false);
        pointObject.transform.localPosition = localPosition;
        ArenaCapturePoint capturePoint = pointObject.AddComponent<ArenaCapturePoint>();
        capturePoint.Configure(localRadius, 5f, 10f);
        return capturePoint;
    }

    private static ArenaGate CreateGate(
        string fallbackName,
        Transform visualRoot,
        Transform layoutRoot,
        Vector3 fallbackLocalPosition,
        float logoWidth,
        float logoDepth)
    {
        if (visualRoot == null)
        {
            GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallback.name = fallbackName;
            fallback.transform.SetParent(layoutRoot, false);
            fallback.transform.localPosition = fallbackLocalPosition;
            fallback.transform.localScale = new Vector3(logoWidth * 0.012f, logoWidth * 0.012f, logoDepth * 0.35f);
            Renderer fallbackRenderer = fallback.GetComponent<Renderer>();
            BoxCollider fallbackCollider = fallback.GetComponent<BoxCollider>();
            NavMeshObstacle fallbackObstacle = fallback.AddComponent<NavMeshObstacle>();
            fallbackObstacle.shape = NavMeshObstacleShape.Box;
            fallbackObstacle.size = Vector3.one;
            fallbackObstacle.carving = true;
            fallbackObstacle.carveOnlyStationary = true;
            ArenaGate fallbackGate = fallback.AddComponent<ArenaGate>();
            fallbackGate.Configure(
                new[] { fallbackRenderer },
                new Collider[] { fallbackCollider },
                new[] { fallbackObstacle },
                initiallyClosed: true);
            return fallbackGate;
        }

        ArenaGate gate = visualRoot.GetComponent<ArenaGate>();
        if (gate == null)
        {
            gate = visualRoot.gameObject.AddComponent<ArenaGate>();
        }

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        var colliders = new List<Collider>();
        var obstacles = new List<NavMeshObstacle>();
        foreach (Renderer renderer in renderers)
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            Vector3 blockerSize = meshBounds.size;
            blockerSize.x = Mathf.Max(blockerSize.x, 0.02f);
            blockerSize.y = Mathf.Max(blockerSize.y, 0.04f);
            blockerSize.z = Mathf.Max(blockerSize.z, 0.02f);

            BoxCollider boxCollider = renderer.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = renderer.gameObject.AddComponent<BoxCollider>();
            }
            boxCollider.center = meshBounds.center;
            boxCollider.size = blockerSize;
            boxCollider.isTrigger = false;
            colliders.Add(boxCollider);

            NavMeshObstacle obstacle = renderer.GetComponent<NavMeshObstacle>();
            if (obstacle == null)
            {
                obstacle = renderer.gameObject.AddComponent<NavMeshObstacle>();
            }
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = meshBounds.center;
            obstacle.size = blockerSize;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacles.Add(obstacle);
        }

        gate.Configure(renderers, colliders.ToArray(), obstacles.ToArray(), initiallyClosed: true);
        EditorUtility.SetDirty(gate);
        return gate;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate.name == name);
    }

    private static bool IsDoorRenderer(Renderer renderer)
    {
        Transform current = renderer.transform;
        while (current != null)
        {
            if (current.name == "Door1" || current.name == "Door2")
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private static void ValidateCaptureProgressBehavior()
    {
        GameObject testObject = new("ArenaCapturePoint_ValidationOnly")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        try
        {
            ArenaCapturePoint point = testObject.AddComponent<ArenaCapturePoint>();
            point.Configure(1f, 5f, 10f);
            point.ResetProgress();
            point.Tick(Vector3.zero, 5f);
            bool inactiveBeforeSecondWave = Mathf.Approximately(point.Progress, 0f) && point.IsObjectiveActive == false;
            point.ActivateObjective();
            point.Tick(Vector3.zero, 2.5f);
            bool halfCaptured = Mathf.Approximately(point.Progress, 0.5f) && point.IsCompleted == false;
            point.Tick(Vector3.right * 2f, 5f);
            bool rolledBack = Mathf.Approximately(point.Progress, 0f) && point.IsCompleted == false;
            point.Tick(Vector3.zero, 5f);
            point.Tick(Vector3.right * 2f, 20f);
            bool completionLocked = Mathf.Approximately(point.Progress, 1f) && point.IsCompleted;
            bool objectiveRulesValid = MultiArenaWaveController.IsObjectiveDrivenSecondWave(ArenaId.P)
                && MultiArenaWaveController.IsObjectiveDrivenSecondWave(ArenaId.R)
                && MultiArenaWaveController.IsObjectiveDrivenSecondWave(ArenaId.O) == false;
            if (inactiveBeforeSecondWave && halfCaptured && rolledBack && completionLocked && objectiveRulesValid)
            {
                Debug.Log("Capture progress behavior valid: inactive before wave 2, 5s capture, 10s rollback, completed state locked; P/R objective-driven and O timed.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Capture progress behavior invalid: inactive={inactiveBeforeSecondWave}, half={halfCaptured}, rollback={rolledBack}, locked={completionLocked}, objectiveRules={objectiveRulesValid}.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(testObject);
        }
    }

    private static void ValidateFinalBossBehavior()
    {
        if (FinalBossRuntimeController.ValidateDefaults(out string error) == false)
        {
            throw new InvalidOperationException($"Final boss defaults are invalid: {error}");
        }

        bool sectorInside = FinalBossAttackMath.IsInsideSector(
            Vector3.zero,
            Vector3.forward,
            new Vector3(0f, 0f, 6f),
            FinalBossRuntimeController.SectorAngleDegrees,
            FinalBossRuntimeController.SectorRange);
        bool sectorOutside = FinalBossAttackMath.IsInsideSector(
            Vector3.zero,
            Vector3.forward,
            new Vector3(7f, 0f, 0f),
            FinalBossRuntimeController.SectorAngleDegrees,
            FinalBossRuntimeController.SectorRange);
        float halfRotation = FinalBossAttackMath.GetBeamAngle(
            FinalBossRuntimeController.BeamRotationSeconds * 0.5f,
            FinalBossRuntimeController.BeamRotationSeconds);
        float segmentDistance = FinalBossAttackMath.DistanceToSegmentXZ(
            new Vector3(2f, 0f, 1f),
            Vector3.zero,
            new Vector3(4f, 0f, 0f));
        Vector3 clampedDestination = FinalBossAttackMath.ClampToCircleXZ(
            new Vector3(12f, 0f, 0f),
            Vector3.zero,
            5f);

        if (sectorInside == false
            || sectorOutside
            || Mathf.Approximately(halfRotation, 180f) == false
            || Mathf.Approximately(segmentDistance, 1f) == false
            || Mathf.Approximately(clampedDestination.x, 5f) == false)
        {
            throw new InvalidOperationException(
                $"Final boss math validation failed: sectorInside={sectorInside}, sectorOutside={sectorOutside}, halfRotation={halfRotation}, segmentDistance={segmentDistance}, clampedDestination={clampedDestination}.");
        }

        Debug.Log("Final boss defaults and deterministic attack math are valid.");
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var names = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }
}
#endif
