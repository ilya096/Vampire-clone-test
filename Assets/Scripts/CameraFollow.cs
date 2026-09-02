using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    private enum OverviewPhase
    {
        None,
        TravelOut,
        Hold,
        Return
    }

    private const float SkipUnlockSeconds = 0.5f;
    private const float SkipReturnSeconds = 0.25f;

    [SerializeField] private float _speed = 13f;
    [SerializeField] private Vector3 _offset = new(0f, 12f, -10f);

    private Transform _player;
    private Camera _camera;
    private ArenaCameraOverviewMarker _marker;
    private OverviewPhase _overviewPhase;
    private Action _overviewCompleted;
    private float _phaseElapsed;
    private float _phaseDuration;
    private float _shotElapsed;
    private Vector3 _phaseStartPosition;
    private Quaternion _phaseStartRotation;
    private Matrix4x4 _phaseStartProjection;
    private Vector3 _returnPosition;
    private Quaternion _returnRotation;
    private bool _baselineOrthographic;
    private float _baselineFieldOfView;
    private float _baselineOrthographicSize;
    private Matrix4x4 _baselineProjection;
    private bool _pauseOverlayActive;

    public bool IsOverviewActive => _overviewPhase != OverviewPhase.None;
    public bool CanSkipOverview => IsOverviewActive && _shotElapsed >= SkipUnlockSeconds;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    public void SetPlayer(Transform player)
    {
        _player = player;
    }

    public void SetPauseOverlayActive(bool active)
    {
        _pauseOverlayActive = active;
    }

    public bool TryPlayOverview(ArenaCameraOverviewShotId shotId, Action completed)
    {
        if (IsOverviewActive)
        {
            Debug.LogWarning($"[Logo Survivor][CameraOverview] Duplicate active request ignored: {shotId}.");
            return false;
        }

        if (_player == null || _camera == null)
        {
            Debug.LogError($"[Logo Survivor][CameraOverview] Shot {shotId} skipped: player or camera is unavailable.");
            return false;
        }

        if (TryFindMarker(shotId, out ArenaCameraOverviewMarker marker, out string error) == false)
        {
            Debug.LogError($"[Logo Survivor][CameraOverview] Shot {shotId} skipped: {error}");
            return false;
        }

        _marker = marker;
        _overviewCompleted = completed;
        _shotElapsed = 0f;
        _baselineOrthographic = _camera.orthographic;
        _baselineFieldOfView = _camera.fieldOfView;
        _baselineOrthographicSize = _camera.orthographicSize;
        _camera.ResetProjectionMatrix();
        _baselineProjection = _camera.projectionMatrix;
        _returnPosition = _player.position + _offset;
        _returnRotation = transform.rotation;

        BeginPhase(OverviewPhase.TravelOut, marker.TravelOutSeconds);
        Debug.Log($"[Logo Survivor][CameraOverview] Shot started: {shotId}.");
        return true;
    }

    public void CancelOverviewForResult()
    {
        if (IsOverviewActive == false)
        {
            return;
        }

        RestoreGameplayCamera();
        ClearOverviewState(invokeCompletion: false);
        Debug.Log("[Logo Survivor][CameraOverview] Active shot cancelled by final result.");
    }

    private bool TryFindMarker(
        ArenaCameraOverviewShotId shotId,
        out ArenaCameraOverviewMarker marker,
        out string error)
    {
        ArenaCameraOverviewMarker[] markers = FindObjectsByType<ArenaCameraOverviewMarker>(
            FindObjectsInactive.Include);

        marker = null;
        int matches = 0;
        foreach (ArenaCameraOverviewMarker candidate in markers)
        {
            if (candidate.ShotId != shotId)
            {
                continue;
            }

            marker = candidate;
            matches++;
        }

        if (matches == 0)
        {
            error = $"marker {shotId} was not found";
            return false;
        }

        if (matches > 1)
        {
            error = $"marker {shotId} is duplicated ({matches} instances)";
            return false;
        }

        return marker.ValidateForRuntime(out error);
    }

    private void Update()
    {
        if (_pauseOverlayActive || CanSkipOverview == false || Application.isFocused == false)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null
            && (keyboard.spaceKey.wasPressedThisFrame
                || keyboard.enterKey.wasPressedThisFrame
                || keyboard.numpadEnterKey.wasPressedThisFrame))
        {
            RequestSkip();
        }
    }

    private void LateUpdate()
    {
        if (_player == null || _pauseOverlayActive)
        {
            return;
        }

        if (IsOverviewActive)
        {
            TickOverview();
            return;
        }

        Vector3 newPosition = _player.position + _offset;
        float t = Mathf.Clamp01(_speed * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, newPosition, t);
    }

    private void TickOverview()
    {
        if (Application.isFocused == false)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        _shotElapsed += deltaTime;
        _phaseElapsed += deltaTime;
        float normalized = _phaseDuration <= 0f ? 1f : Mathf.Clamp01(_phaseElapsed / _phaseDuration);
        float eased = Mathf.SmoothStep(0f, 1f, normalized);

        switch (_overviewPhase)
        {
            case OverviewPhase.TravelOut:
                ApplyBlend(
                    _phaseStartPosition,
                    _marker.transform.position,
                    _phaseStartRotation,
                    _marker.transform.rotation,
                    _phaseStartProjection,
                    CreateMarkerProjection(_marker),
                    eased);
                if (normalized >= 1f)
                {
                    BeginPhase(OverviewPhase.Hold, _marker.HoldSeconds);
                }
                break;

            case OverviewPhase.Hold:
                ApplyMarkerCameraMode();
                transform.SetPositionAndRotation(_marker.transform.position, _marker.transform.rotation);
                _camera.projectionMatrix = CreateMarkerProjection(_marker);
                if (normalized >= 1f)
                {
                    BeginReturn(_marker.ReturnSeconds);
                }
                break;

            case OverviewPhase.Return:
                _returnPosition = _player.position + _offset;
                ApplyBlend(
                    _phaseStartPosition,
                    _returnPosition,
                    _phaseStartRotation,
                    _returnRotation,
                    _phaseStartProjection,
                    _baselineProjection,
                    eased);
                if (normalized >= 1f)
                {
                    CompleteOverview();
                }
                break;
        }
    }

    private void BeginPhase(OverviewPhase phase, float duration)
    {
        _overviewPhase = phase;
        _phaseElapsed = 0f;
        _phaseDuration = Mathf.Max(0f, duration);
        _phaseStartPosition = transform.position;
        _phaseStartRotation = transform.rotation;
        _phaseStartProjection = _camera.projectionMatrix;
    }

    private void BeginReturn(float duration)
    {
        BeginPhase(OverviewPhase.Return, Mathf.Max(0.1f, duration));
        Matrix4x4 returnStartProjection = _phaseStartProjection;
        _camera.orthographic = _baselineOrthographic;
        _camera.fieldOfView = _baselineFieldOfView;
        _camera.orthographicSize = _baselineOrthographicSize;
        _camera.projectionMatrix = returnStartProjection;
    }

    private void RequestSkip()
    {
        if (CanSkipOverview == false)
        {
            return;
        }

        if (_overviewPhase == OverviewPhase.Return
            && _phaseDuration - _phaseElapsed <= SkipReturnSeconds)
        {
            return;
        }

        BeginReturn(SkipReturnSeconds);
        Debug.Log($"[Logo Survivor][CameraOverview] Shot skipped: {_marker.ShotId}.");
    }

    private void CompleteOverview()
    {
        ArenaCameraOverviewShotId shotId = _marker.ShotId;
        RestoreGameplayCamera();
        ClearOverviewState(invokeCompletion: true);
        Debug.Log($"[Logo Survivor][CameraOverview] Shot completed: {shotId}.");
    }

    private void RestoreGameplayCamera()
    {
        transform.SetPositionAndRotation(_player.position + _offset, _returnRotation);
        _camera.orthographic = _baselineOrthographic;
        _camera.fieldOfView = _baselineFieldOfView;
        _camera.orthographicSize = _baselineOrthographicSize;
        _camera.ResetProjectionMatrix();
    }

    private void ClearOverviewState(bool invokeCompletion)
    {
        Action completed = _overviewCompleted;
        _marker = null;
        _overviewCompleted = null;
        _overviewPhase = OverviewPhase.None;
        _phaseElapsed = 0f;
        _phaseDuration = 0f;
        _shotElapsed = 0f;

        if (invokeCompletion)
        {
            completed?.Invoke();
        }
    }

    private void ApplyBlend(
        Vector3 fromPosition,
        Vector3 toPosition,
        Quaternion fromRotation,
        Quaternion toRotation,
        Matrix4x4 fromProjection,
        Matrix4x4 toProjection,
        float t)
    {
        transform.SetPositionAndRotation(
            Vector3.LerpUnclamped(fromPosition, toPosition, t),
            Quaternion.SlerpUnclamped(fromRotation, toRotation, t));
        _camera.projectionMatrix = LerpMatrix(fromProjection, toProjection, t);
    }

    private Matrix4x4 CreateMarkerProjection(ArenaCameraOverviewMarker marker)
    {
        float near = Mathf.Max(0.01f, _camera.nearClipPlane);
        float far = Mathf.Max(near + 0.01f, _camera.farClipPlane);
        float aspect = Mathf.Max(0.01f, _camera.aspect);

        if (marker.Projection == ArenaCameraOverviewProjection.Orthographic)
        {
            float vertical = marker.OrthographicSize;
            float horizontal = vertical * aspect;
            return Matrix4x4.Ortho(-horizontal, horizontal, -vertical, vertical, near, far);
        }

        return Matrix4x4.Perspective(marker.FieldOfView, aspect, near, far);
    }

    private void ApplyMarkerCameraMode()
    {
        bool orthographic = _marker.Projection == ArenaCameraOverviewProjection.Orthographic;
        _camera.orthographic = orthographic;
        if (orthographic)
        {
            _camera.orthographicSize = _marker.OrthographicSize;
        }
        else
        {
            _camera.fieldOfView = _marker.FieldOfView;
        }
    }

    private static Matrix4x4 LerpMatrix(Matrix4x4 from, Matrix4x4 to, float t)
    {
        Matrix4x4 result = default;
        for (int index = 0; index < 16; index++)
        {
            result[index] = Mathf.LerpUnclamped(from[index], to[index], t);
        }
        return result;
    }

    private void OnGUI()
    {
        if (IsOverviewActive == false || _pauseOverlayActive)
        {
            return;
        }

        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        GUI.depth = -950;
        GUI.Box(new Rect(Screen.width * 0.5f - 110f, 24f, 220f, 38f), _marker.RouteLabel);

        if (CanSkipOverview
            && GUI.Button(new Rect(Screen.width - 190f, Screen.height - 68f, 170f, 44f), "ПРОПУСТИТЬ"))
        {
            RequestSkip();
        }
    }
}
