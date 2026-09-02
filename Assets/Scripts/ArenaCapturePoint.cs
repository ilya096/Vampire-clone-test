using UnityEngine;

/// <summary>
/// Five-second hold objective with a ten-second linear rollback. Completion is
/// locked until the route is explicitly reset (normally by scene restart).
/// </summary>
public sealed class ArenaCapturePoint : MonoBehaviour
{
    private const float FillSurfaceWorldY = 0.01f;
    private const float ContourWorldY = 0.0105f;
    private const float ProgressLabelWorldOffset = 0.003f;
    private const float FlagRootLocalY = -0.001f;
    private const float ActiveFillAlpha = 0.22f;
    private const float CompletedFillAlpha = 0.3f;
    private const int FillSortingOrder = -100;
    private const int FillDiscSegments = 48;

    [SerializeField] private float _localRadius = 0.055f;
    [SerializeField] private float _captureSeconds = 5f;
    [SerializeField] private float _rollbackSeconds = 10f;
    [SerializeField] private Color _idleColor = new(0.2f, 0.85f, 0.35f, 0.45f);
    [SerializeField] private Color _completeColor = new(0.2f, 1f, 0.35f, 0.85f);

    private Transform _fill;
    private Mesh _fillMesh;
    private Renderer _fillRenderer;
    private LineRenderer _contour;
    private GameObject _flagRoot;
    private bool _objectiveActive;

    public float Progress { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool IsObjectiveActive => _objectiveActive;
    public float LocalRadius => _localRadius;
    public Vector3 GuidanceWorldPosition => transform.TransformPoint(Vector3.up * (_localRadius * 2.4f));
    public Vector3 ProgressWorldPosition =>
        transform.TransformPoint(Vector3.up * FlagRootLocalY) + Vector3.up * ProgressLabelWorldOffset;

    public void Configure(float localRadius, float captureSeconds, float rollbackSeconds)
    {
        _localRadius = Mathf.Max(0.005f, localRadius);
        _captureSeconds = Mathf.Max(0.1f, captureSeconds);
        _rollbackSeconds = Mathf.Max(0.1f, rollbackSeconds);
    }

    private void Awake()
    {
        EnsurePresentation();
        RefreshPresentation();
    }

    public void ResetProgress()
    {
        Progress = 0f;
        IsCompleted = false;
        _objectiveActive = false;
        RefreshPresentation();
    }

    public void ActivateObjective()
    {
        _objectiveActive = true;
        RefreshPresentation();
    }

    public void CompleteForDebug()
    {
        _objectiveActive = true;
        Progress = 1f;
        IsCompleted = true;
        RefreshPresentation();
    }

    public void Tick(Vector3 playerWorldPosition, float deltaTime)
    {
        if (_objectiveActive == false || IsCompleted || deltaTime <= 0f)
        {
            return;
        }

        Vector3 localPlayer = transform.InverseTransformPoint(playerWorldPosition);
        bool occupied = new Vector2(localPlayer.x, localPlayer.z).sqrMagnitude <= _localRadius * _localRadius;
        float rate = occupied ? 1f / _captureSeconds : -1f / _rollbackSeconds;
        Progress = Mathf.Clamp01(Progress + rate * deltaTime);
        if (Progress >= 1f)
        {
            Progress = 1f;
            IsCompleted = true;
        }

        RefreshPresentation();
    }

    private void EnsurePresentation()
    {
        if (_fill == null)
        {
            GameObject fillObject = new("CaptureFill");
            fillObject.name = "CaptureFill";
            fillObject.transform.SetParent(transform, false);
            _fill = fillObject.transform;
            _fillMesh = CreateFlatDiscMesh();
            fillObject.AddComponent<MeshFilter>().sharedMesh = _fillMesh;
            _fillRenderer = fillObject.AddComponent<MeshRenderer>();
            _fillRenderer.sortingOrder = FillSortingOrder;
            RuntimeRendererUtility.ConfigureMesh(_fillRenderer, _idleColor);
        }

        if (_contour == null)
        {
            GameObject contourObject = new("CaptureContour");
            contourObject.transform.SetParent(transform, false);
            _contour = contourObject.AddComponent<LineRenderer>();
            _contour.useWorldSpace = false;
            _contour.loop = true;
            _contour.widthMultiplier = 0.008f;
            _contour.positionCount = 48;
            for (int index = 0; index < _contour.positionCount; index++)
            {
                float angle = index / (float)_contour.positionCount * Mathf.PI * 2f;
                _contour.SetPosition(index, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _localRadius);
            }
            RuntimeRendererUtility.ConfigureLine(_contour);
        }

        if (_flagRoot == null)
        {
            _flagRoot = new GameObject("CaptureFlag");
            _flagRoot.transform.SetParent(transform, false);
            _flagRoot.transform.localPosition = Vector3.up * FlagRootLocalY;

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pole.name = "FlagPole";
            pole.transform.SetParent(_flagRoot.transform, false);
            pole.transform.localPosition = Vector3.up * (_localRadius * 0.75f);
            pole.transform.localScale = new Vector3(_localRadius * 0.08f, _localRadius * 1.5f, _localRadius * 0.08f);
            Destroy(pole.GetComponent<Collider>());
            RuntimeRendererUtility.ConfigureMesh(pole.GetComponent<Renderer>(), new Color(0.78f, 0.82f, 0.88f));

            GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "FlagCloth";
            flag.transform.SetParent(_flagRoot.transform, false);
            flag.transform.localPosition = new Vector3(_localRadius * 0.42f, _localRadius * 1.32f, 0f);
            flag.transform.localScale = new Vector3(_localRadius * 0.78f, _localRadius * 0.42f, _localRadius * 0.05f);
            Destroy(flag.GetComponent<Collider>());
            RuntimeRendererUtility.ConfigureMesh(flag.GetComponent<Renderer>(), _idleColor);
        }
    }

    private void RefreshPresentation()
    {
        if (_fill == null || _fillRenderer == null || _contour == null)
        {
            return;
        }

        float radius = _localRadius * 2f * Mathf.Sqrt(Mathf.Max(Progress, 0.0001f));
        float inverseWorldYScale = 1f / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        float fillWorldOffset = FillSurfaceWorldY - transform.position.y;
        _fill.localPosition = Vector3.up * (fillWorldOffset * inverseWorldYScale);
        _fill.localScale = new Vector3(radius, 1f, radius);
        float contourWorldOffset = Mathf.Max(0.0005f, ContourWorldY - transform.position.y);
        _contour.transform.localPosition = Vector3.up * (contourWorldOffset * inverseWorldYScale);
        _fillRenderer.enabled = _objectiveActive && Progress > 0f;
        Color color = IsCompleted ? _completeColor : _idleColor;
        Color fillColor = color;
        fillColor.a = Mathf.Min(fillColor.a, IsCompleted ? CompletedFillAlpha : ActiveFillAlpha);
        RuntimeRendererUtility.SetColor(_fillRenderer, fillColor);
        _contour.enabled = _objectiveActive;
        _contour.startColor = color;
        _contour.endColor = color;
        _contour.widthMultiplier = IsCompleted ? 0.014f : 0.008f;
        if (_flagRoot != null)
        {
            _flagRoot.SetActive(_objectiveActive);
            Renderer flagRenderer = _flagRoot.transform.Find("FlagCloth")?.GetComponent<Renderer>();
            RuntimeRendererUtility.SetColor(flagRenderer, color);
        }
    }

    private static Mesh CreateFlatDiscMesh()
    {
        Vector3[] vertices = new Vector3[FillDiscSegments + 1];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[FillDiscSegments * 3];
        vertices[0] = Vector3.zero;
        normals[0] = Vector3.up;
        uv[0] = new Vector2(0.5f, 0.5f);

        for (int index = 0; index < FillDiscSegments; index++)
        {
            float angle = index / (float)FillDiscSegments * Mathf.PI * 2f;
            Vector3 vertex = new(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f);
            vertices[index + 1] = vertex;
            normals[index + 1] = Vector3.up;
            uv[index + 1] = new Vector2(vertex.x + 0.5f, vertex.z + 0.5f);

            int triangleOffset = index * 3;
            triangles[triangleOffset] = 0;
            triangles[triangleOffset + 1] = (index + 1) % FillDiscSegments + 1;
            triangles[triangleOffset + 2] = index + 1;
        }

        Mesh mesh = new()
        {
            name = "CaptureFillFlatDisc",
            vertices = vertices,
            normals = normals,
            uv = uv,
            triangles = triangles
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (_fillMesh != null)
        {
            Destroy(_fillMesh);
            _fillMesh = null;
        }
    }

    private void OnGUI()
    {
        if (_objectiveActive == false || Time.timeScale <= 0f)
        {
            return;
        }

        string status = IsCompleted ? "ГОТОВО · 100%" : $"ЗАХВАТ  {Progress:P0}";
        ObjectiveGuidanceGui.DrawWorldProgress(
            Camera.main,
            ProgressWorldPosition,
            status,
            IsCompleted ? _completeColor : new Color(0.35f, 1f, 0.48f));
    }

    private void OnDrawGizmos()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = IsCompleted ? _completeColor : _idleColor;
        const int segments = 48;
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
}
