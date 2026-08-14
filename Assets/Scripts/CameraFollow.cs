using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private float _speed = 13f;
    [SerializeField] private Vector3 _offset = new(0f, 12f, -10f);

    private Transform _player;
    private Vector3 _overviewTarget;
    private float _overviewRemaining;
    private float _overviewZoomMultiplier = 1f;

    public void SetPlayer(Transform player)
    {
        _player = player;
    }

    public void PlayOverview(Vector3 target, float duration, float zoomMultiplier)
    {
        _overviewTarget = target;
        _overviewRemaining = Mathf.Max(0f, duration);
        _overviewZoomMultiplier = Mathf.Max(1f, zoomMultiplier);
    }

    private void LateUpdate()
    {
        if(_player == null)
        {
            return;
        }

        bool overviewActive = _overviewRemaining > 0f;
        Vector3 focus = overviewActive ? _overviewTarget : _player.position;
        if (overviewActive)
        {
            focus.y = _player.position.y;
            _overviewRemaining = Mathf.Max(0f, _overviewRemaining - Time.deltaTime);
        }

        Vector3 newPosition = focus + _offset * (overviewActive ? _overviewZoomMultiplier : 1f);
        float t = Mathf.Clamp01(_speed * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, newPosition, t);
    }
}
