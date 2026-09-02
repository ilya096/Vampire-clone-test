using UnityEngine;

public enum ArenaCameraOverviewShotId
{
    IntroOverview,
    TransitionPToR,
    TransitionRToO
}

public enum ArenaCameraOverviewProjection
{
    Perspective,
    Orthographic
}

[DisallowMultipleComponent]
public sealed class ArenaCameraOverviewMarker : MonoBehaviour
{
    [SerializeField] private ArenaCameraOverviewShotId _shotId;
    [SerializeField] private ArenaCameraOverviewProjection _projection = ArenaCameraOverviewProjection.Perspective;
    [SerializeField, Range(10f, 120f)] private float _fieldOfView = 60f;
    [SerializeField, Min(0.01f)] private float _orthographicSize = 10f;
    [SerializeField, Min(0.1f)] private float _travelOutSeconds = 1f;
    [SerializeField, Min(0f)] private float _holdSeconds = 2f;
    [SerializeField, Min(0.1f)] private float _returnSeconds = 1f;
    [SerializeField, HideInInspector] private bool _ownerApproved;
    [SerializeField, HideInInspector] private Vector3 _approvedPosition;
    [SerializeField, HideInInspector] private Quaternion _approvedRotation;
    [SerializeField, HideInInspector] private ArenaCameraOverviewProjection _approvedProjection;
    [SerializeField, HideInInspector] private float _approvedFieldOfView;
    [SerializeField, HideInInspector] private float _approvedOrthographicSize;
    [SerializeField, HideInInspector] private float _approvedTravelOutSeconds;
    [SerializeField, HideInInspector] private float _approvedHoldSeconds;
    [SerializeField, HideInInspector] private float _approvedReturnSeconds;

    public ArenaCameraOverviewShotId ShotId => _shotId;
    public ArenaCameraOverviewProjection Projection => _projection;
    public float FieldOfView => _fieldOfView;
    public float OrthographicSize => _orthographicSize;
    public float TravelOutSeconds => _travelOutSeconds;
    public float HoldSeconds => _holdSeconds;
    public float ReturnSeconds => _returnSeconds;
    public bool OwnerApproved => _ownerApproved;

    public string RouteLabel => _shotId switch
    {
        ArenaCameraOverviewShotId.IntroOverview => "П → Р → О",
        ArenaCameraOverviewShotId.TransitionPToR => "П → Р",
        ArenaCameraOverviewShotId.TransitionRToO => "Р → О",
        _ => string.Empty
    };

    public void ConfigureDefaults(ArenaCameraOverviewShotId shotId)
    {
        _shotId = shotId;
        _ownerApproved = false;

        if (shotId == ArenaCameraOverviewShotId.IntroOverview)
        {
            _projection = ArenaCameraOverviewProjection.Orthographic;
            _travelOutSeconds = 1.5f;
            _holdSeconds = 3f;
            _returnSeconds = 1.5f;
            _orthographicSize = Mathf.Max(0.01f, _orthographicSize);
        }
        else
        {
            _projection = ArenaCameraOverviewProjection.Perspective;
            _travelOutSeconds = 1f;
            _holdSeconds = 2f;
            _returnSeconds = 1f;
            _fieldOfView = Mathf.Clamp(_fieldOfView, 10f, 120f);
        }
    }

    public void SetOwnerApproved(bool approved)
    {
        _ownerApproved = approved;
        if (approved == false)
        {
            return;
        }

        _approvedPosition = transform.position;
        _approvedRotation = transform.rotation;
        _approvedProjection = _projection;
        _approvedFieldOfView = _fieldOfView;
        _approvedOrthographicSize = _orthographicSize;
        _approvedTravelOutSeconds = _travelOutSeconds;
        _approvedHoldSeconds = _holdSeconds;
        _approvedReturnSeconds = _returnSeconds;
    }

    public bool ValidateForRuntime(out string error)
    {
        ArenaCameraOverviewProjection expectedProjection = _shotId == ArenaCameraOverviewShotId.IntroOverview
            ? ArenaCameraOverviewProjection.Orthographic
            : ArenaCameraOverviewProjection.Perspective;

        if (_projection != expectedProjection)
        {
            error = $"Marker {_shotId} must use {expectedProjection} projection.";
            return false;
        }

        if (_travelOutSeconds < 0.1f || _travelOutSeconds > 10f
            || _holdSeconds < 0f || _holdSeconds > 15f
            || _returnSeconds < 0.1f || _returnSeconds > 10f)
        {
            error = $"Marker {_shotId} has timing outside the approved ranges.";
            return false;
        }

        if (_projection == ArenaCameraOverviewProjection.Perspective
            && (_fieldOfView < 10f || _fieldOfView > 120f))
        {
            error = $"Marker {_shotId} field of view must be between 10 and 120 degrees.";
            return false;
        }

        if (_projection == ArenaCameraOverviewProjection.Orthographic && _orthographicSize <= 0f)
        {
            error = $"Marker {_shotId} orthographic size must be greater than zero.";
            return false;
        }

        if (_ownerApproved == false)
        {
            error = $"Marker {_shotId} has not been approved by the owner.";
            return false;
        }

        if (MatchesApprovedState() == false)
        {
            error = $"Marker {_shotId} changed after owner approval and must be previewed and approved again.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool MatchesApprovedState()
    {
        return Vector3.SqrMagnitude(transform.position - _approvedPosition) <= 0.000001f
            && Quaternion.Angle(transform.rotation, _approvedRotation) <= 0.001f
            && _projection == _approvedProjection
            && Mathf.Approximately(_fieldOfView, _approvedFieldOfView)
            && Mathf.Approximately(_orthographicSize, _approvedOrthographicSize)
            && Mathf.Approximately(_travelOutSeconds, _approvedTravelOutSeconds)
            && Mathf.Approximately(_holdSeconds, _approvedHoldSeconds)
            && Mathf.Approximately(_returnSeconds, _approvedReturnSeconds);
    }

    private void OnDrawGizmos()
    {
        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.color = _ownerApproved ? new Color(0.2f, 1f, 0.45f, 0.9f) : new Color(1f, 0.72f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.35f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);

        if (_projection == ArenaCameraOverviewProjection.Perspective)
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, _fieldOfView, 4f, 0.1f, 16f / 9f);
            Gizmos.matrix = previousMatrix;
        }

        Gizmos.color = previousColor;
    }
}
