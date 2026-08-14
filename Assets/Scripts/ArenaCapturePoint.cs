using UnityEngine;

/// <summary>
/// Five-second hold objective with a ten-second linear rollback. Completion is
/// locked until the route is explicitly reset (normally by scene restart).
/// </summary>
public sealed class ArenaCapturePoint : MonoBehaviour
{
    [SerializeField] private float _localRadius = 0.055f;
    [SerializeField] private float _captureSeconds = 5f;
    [SerializeField] private float _rollbackSeconds = 10f;
    [SerializeField] private Color _idleColor = new(0.2f, 0.85f, 0.35f, 0.45f);
    [SerializeField] private Color _completeColor = new(0.2f, 1f, 0.35f, 0.85f);

    private Transform _fill;
    private Renderer _fillRenderer;
    private LineRenderer _contour;

    public float Progress { get; private set; }
    public bool IsCompleted { get; private set; }
    public float LocalRadius => _localRadius;

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
        RefreshPresentation();
    }

    public void Tick(Vector3 playerWorldPosition, float deltaTime)
    {
        if (IsCompleted || deltaTime <= 0f)
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
            GameObject fillObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fillObject.name = "CaptureFill";
            fillObject.transform.SetParent(transform, false);
            fillObject.transform.localPosition = Vector3.up * 0.008f;
            Destroy(fillObject.GetComponent<Collider>());
            _fill = fillObject.transform;
            _fillRenderer = fillObject.GetComponent<Renderer>();
            RuntimeRendererUtility.ConfigureMesh(_fillRenderer, _idleColor);
        }

        if (_contour == null)
        {
            GameObject contourObject = new("CaptureContour");
            contourObject.transform.SetParent(transform, false);
            contourObject.transform.localPosition = Vector3.up * 0.012f;
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
    }

    private void RefreshPresentation()
    {
        if (_fill == null || _fillRenderer == null || _contour == null)
        {
            return;
        }

        float radius = _localRadius * 2f * Mathf.Sqrt(Mathf.Max(Progress, 0.0001f));
        _fill.localScale = new Vector3(radius, 0.004f, radius);
        _fillRenderer.enabled = Progress > 0f;
        Color color = IsCompleted ? _completeColor : _idleColor;
        RuntimeRendererUtility.SetColor(_fillRenderer, color);
        _contour.startColor = color;
        _contour.endColor = color;
        _contour.widthMultiplier = IsCompleted ? 0.014f : 0.008f;
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
