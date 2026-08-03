using UnityEngine;

/// <summary>
/// Editable first-arena escort path. Move Start and End child objects in Scene view.
/// </summary>
[ExecuteAlways]
public class EscortRoute : MonoBehaviour
{
    [SerializeField] private Transform _start;
    [SerializeField] private Transform _end;
    [SerializeField] private Color _gizmoColor = new(1f, 0.75f, 0.1f, 1f);

    public Vector3 StartPosition => _start != null ? _start.position : transform.position;
    public Vector3 EndPosition => _end != null ? _end.position : transform.position + Vector3.forward * 24f;
    public bool IsConfigured => _start != null && _end != null;

    public void Configure(Transform start, Transform end)
    {
        _start = start;
        _end = end;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = _gizmoColor;
        Gizmos.DrawLine(StartPosition, EndPosition);
        Gizmos.DrawWireSphere(StartPosition, 0.55f);
        Gizmos.DrawWireSphere(EndPosition, 0.75f);
    }
}
