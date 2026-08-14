using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Circular boundary around the O boss arena. Opening deactivates every segment,
/// so the complete ring disappears rather than leaving a partial doorway.
/// </summary>
public sealed class ArenaBossBoundary : MonoBehaviour
{
    [SerializeField] private float _localRadius = 0.24f;
    [SerializeField] private float _localThickness = 0.025f;
    [SerializeField] private float _localHeight = 0.05f;
    [SerializeField, Range(12, 48)] private int _segmentCount = 24;
    [SerializeField] private Color _closedColor = new(0.95f, 0.16f, 0.12f, 0.8f);
    [SerializeField] private MeshFilter _logoWallFilter;
    [SerializeField] private Mesh _sourceLogoWallMesh;
    [SerializeField] private GameObject _authoredBoundaryRoot;

    private readonly List<GameObject> _segments = new();
    private readonly List<NavMeshLink> _links = new();
    private GameObject _importedBoundary;
    private Mesh _originalWallMesh;
    private Mesh _staticWallMesh;
    private Mesh _boundaryMesh;

    public bool IsOpen { get; private set; }
    public float LocalRadius => _localRadius;
    public float LocalThickness => _localThickness;
    public MeshFilter LogoWallFilter => _logoWallFilter;
    public Mesh SourceLogoWallMesh => _sourceLogoWallMesh;

    public void Configure(
        float localRadius,
        float localThickness,
        float localHeight,
        int segmentCount,
        MeshFilter logoWallFilter,
        Mesh sourceLogoWallMesh,
        GameObject authoredBoundaryRoot)
    {
        _localRadius = Mathf.Max(0.02f, localRadius);
        _localThickness = Mathf.Max(0.005f, localThickness);
        _localHeight = Mathf.Max(0.01f, localHeight);
        _segmentCount = Mathf.Clamp(segmentCount, 12, 48);
        _logoWallFilter = logoWallFilter;
        _sourceLogoWallMesh = sourceLogoWallMesh;
        _authoredBoundaryRoot = authoredBoundaryRoot;
    }

    public void SetAuthoredBoundary(
        MeshFilter logoWallFilter,
        Mesh sourceLogoWallMesh,
        GameObject authoredBoundaryRoot)
    {
        _logoWallFilter = logoWallFilter;
        _sourceLogoWallMesh = sourceLogoWallMesh;
        _authoredBoundaryRoot = authoredBoundaryRoot;
    }

    public int CountMatchingImportedTriangles()
    {
        Mesh source = _sourceLogoWallMesh != null
            ? _sourceLogoWallMesh
            : _logoWallFilter != null ? _logoWallFilter.sharedMesh : null;
        if (_logoWallFilter == null
            || source == null
            || source.isReadable == false
            || source.subMeshCount != 1)
        {
            return -1;
        }

        Vector3[] vertices = source.vertices;
        int[] triangles = source.triangles;
        float selectionTolerance = Mathf.Max(_localThickness * 2.5f, 0.015f);
        int matchCount = 0;
        for (int index = 0; index < triangles.Length; index += 3)
        {
            Vector3 a = WallVertexToBoundaryLocal(vertices[triangles[index]]);
            Vector3 b = WallVertexToBoundaryLocal(vertices[triangles[index + 1]]);
            Vector3 c = WallVertexToBoundaryLocal(vertices[triangles[index + 2]]);
            Vector3 centroid = (a + b + c) / 3f;
            float radius = new Vector2(centroid.x, centroid.z).magnitude;
            if (Mathf.Abs(radius - _localRadius) <= selectionTolerance)
            {
                matchCount++;
            }
        }
        return matchCount;
    }

    private void Awake()
    {
        bool importedBoundaryAvailable = _authoredBoundaryRoot != null || TryExtractImportedBoundary();
        BuildRuntimeSegments(showVisuals: importedBoundaryAvailable == false);
        BuildRuntimeLinks();
        SetOpen(false);
    }

    public void SetOpen(bool open)
    {
        IsOpen = open;
        foreach (GameObject segment in _segments)
        {
            if (segment != null)
            {
                segment.SetActive(open == false);
            }
        }

        if (_importedBoundary != null)
        {
            _importedBoundary.SetActive(open == false);
        }
        if (_authoredBoundaryRoot != null)
        {
            _authoredBoundaryRoot.SetActive(open == false);
        }
        foreach (NavMeshLink link in _links)
        {
            if (link != null)
            {
                link.enabled = open;
            }
        }
    }

    private void BuildRuntimeSegments(bool showVisuals)
    {
        if (_segments.Count > 0)
        {
            return;
        }

        float arcLength = Mathf.PI * 2f * _localRadius / _segmentCount * 1.08f;
        for (int index = 0; index < _segmentCount; index++)
        {
            float angle = index / (float)_segmentCount * Mathf.PI * 2f;
            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = $"BossBoundary_{index:00}";
            segment.transform.SetParent(transform, false);
            segment.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * _localRadius,
                _localHeight * 0.5f,
                Mathf.Sin(angle) * _localRadius);
            segment.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
            segment.transform.localScale = new Vector3(arcLength, _localHeight, _localThickness);
            Renderer renderer = segment.GetComponent<Renderer>();
            RuntimeRendererUtility.ConfigureMesh(renderer, _closedColor);
            renderer.enabled = showVisuals;

            BoxCollider boxCollider = segment.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                boxCollider.isTrigger = false;
            }

            NavMeshObstacle obstacle = segment.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            _segments.Add(segment);
        }
    }

    private void BuildRuntimeLinks()
    {
        if (_links.Count > 0)
        {
            return;
        }

        const int linkCount = 8;
        float halfSpan = _localThickness * 2.5f;
        for (int index = 0; index < linkCount; index++)
        {
            float angle = index / (float)linkCount * Mathf.PI * 2f;
            Vector3 radial = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 desiredStart = transform.TransformPoint(radial * (_localRadius + halfSpan));
            Vector3 desiredEnd = transform.TransformPoint(radial * Mathf.Max(0.01f, _localRadius - halfSpan));
            if (NavMesh.SamplePosition(desiredStart, out NavMeshHit startHit, 5f, NavMesh.AllAreas) == false
                || NavMesh.SamplePosition(desiredEnd, out NavMeshHit endHit, 5f, NavMesh.AllAreas) == false)
            {
                continue;
            }

            Vector3 localStart = transform.InverseTransformPoint(startHit.position);
            Vector3 localEnd = transform.InverseTransformPoint(endHit.position);
            if (new Vector2(localStart.x, localStart.z).sqrMagnitude
                <= new Vector2(localEnd.x, localEnd.z).sqrMagnitude)
            {
                continue;
            }

            GameObject linkObject = new($"BossArenaLink_{index:00}");
            linkObject.transform.SetParent(transform, false);
            NavMeshLink link = linkObject.AddComponent<NavMeshLink>();
            link.startPoint = localStart;
            link.endPoint = localEnd;
            link.width = Mathf.Max(_localThickness * 2f, 0.02f);
            link.bidirectional = true;
            link.autoUpdate = false;
            link.enabled = false;
            _links.Add(link);
        }

        if (_links.Count == 0)
        {
            Debug.LogWarning("Boss arena boundary has no valid NavMesh link pairs. Rebuild or adjust the arena NavMesh before runtime acceptance.");
        }
        else
        {
            Debug.Log($"Boss arena boundary prepared {_links.Count}/{linkCount} validated NavMesh links.");
        }
    }

    private bool TryExtractImportedBoundary()
    {
        if (_logoWallFilter == null || _logoWallFilter.sharedMesh == null)
        {
            Debug.LogWarning("Boss boundary uses generated fallback visuals because the imported logo wall mesh was not assigned.");
            return false;
        }

        Mesh source = _sourceLogoWallMesh != null ? _sourceLogoWallMesh : _logoWallFilter.sharedMesh;
        if (source.isReadable == false || source.subMeshCount != 1)
        {
            Debug.LogWarning("Boss boundary uses generated fallback visuals because the imported logo wall mesh is not a readable single-submesh mesh.");
            return false;
        }

        Vector3[] vertices = source.vertices;
        int[] triangles = source.triangles;
        var staticTriangles = new List<int>(triangles.Length);
        var boundaryTriangles = new List<int>();
        float selectionTolerance = Mathf.Max(_localThickness * 2.5f, 0.015f);
        for (int index = 0; index < triangles.Length; index += 3)
        {
            Vector3 a = WallVertexToBoundaryLocal(vertices[triangles[index]]);
            Vector3 b = WallVertexToBoundaryLocal(vertices[triangles[index + 1]]);
            Vector3 c = WallVertexToBoundaryLocal(vertices[triangles[index + 2]]);
            Vector3 centroid = (a + b + c) / 3f;
            float radius = new Vector2(centroid.x, centroid.z).magnitude;
            List<int> destination = Mathf.Abs(radius - _localRadius) <= selectionTolerance
                ? boundaryTriangles
                : staticTriangles;
            destination.Add(triangles[index]);
            destination.Add(triangles[index + 1]);
            destination.Add(triangles[index + 2]);
        }

        if (boundaryTriangles.Count < 12)
        {
            Debug.LogWarning($"Boss boundary extraction found only {boundaryTriangles.Count / 3} triangles; generated fallback visuals will be used.");
            return false;
        }

        _originalWallMesh = source;
        _staticWallMesh = Instantiate(source);
        _staticWallMesh.name = $"{source.name}_WithoutBossBoundary";
        _staticWallMesh.triangles = staticTriangles.ToArray();
        _staticWallMesh.RecalculateBounds();
        _logoWallFilter.sharedMesh = _staticWallMesh;

        _boundaryMesh = Instantiate(source);
        _boundaryMesh.name = $"{source.name}_BossBoundary";
        _boundaryMesh.triangles = boundaryTriangles.ToArray();
        _boundaryMesh.RecalculateBounds();

        _importedBoundary = new GameObject("ImportedBossBoundary_O");
        _importedBoundary.transform.SetParent(_logoWallFilter.transform, false);
        MeshFilter boundaryFilter = _importedBoundary.AddComponent<MeshFilter>();
        boundaryFilter.sharedMesh = _boundaryMesh;
        MeshRenderer boundaryRenderer = _importedBoundary.AddComponent<MeshRenderer>();
        MeshRenderer sourceRenderer = _logoWallFilter.GetComponent<MeshRenderer>();
        if (sourceRenderer != null)
        {
            boundaryRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            boundaryRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            boundaryRenderer.receiveShadows = sourceRenderer.receiveShadows;
        }

        Debug.Log($"Boss boundary extracted {boundaryTriangles.Count / 3} imported wall triangles without modifying the GLB asset.");
        return true;
    }

    private Vector3 WallVertexToBoundaryLocal(Vector3 wallLocalVertex)
    {
        Vector3 world = _logoWallFilter.transform.TransformPoint(wallLocalVertex);
        return transform.InverseTransformPoint(world);
    }

    private void OnDrawGizmos()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = _closedColor;
        const int segments = 64;
        Vector3 previous = new(_localRadius, 0f, 0f);
        for (int index = 1; index <= segments; index++)
        {
            float angle = index / (float)segments * Mathf.PI * 2f;
            Vector3 next = new(Mathf.Cos(angle) * _localRadius, 0f, Mathf.Sin(angle) * _localRadius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.matrix = previousMatrix;
    }

    private void OnDestroy()
    {
        if (_logoWallFilter != null && _originalWallMesh != null)
        {
            _logoWallFilter.sharedMesh = _originalWallMesh;
        }
        if (_staticWallMesh != null) Destroy(_staticWallMesh);
        if (_boundaryMesh != null) Destroy(_boundaryMesh);
    }
}
