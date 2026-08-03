using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Editable first-arena escort path. Move Start and End child objects in Scene view.
/// </summary>
[ExecuteAlways]
public class EscortRoute : MonoBehaviour
{
    [SerializeField] private Transform _start;
    [SerializeField] private Transform _end;
    [SerializeField] private List<Transform> _intermediatePoints = new();
    [SerializeField] private Color _gizmoColor = new(1f, 0.75f, 0.1f, 1f);

    public Vector3 StartPosition => _start != null ? _start.position : transform.position;
    public Vector3 EndPosition => _end != null ? _end.position : transform.position + Vector3.forward * 24f;
    public bool IsConfigured => _start != null && _end != null;

    public void Configure(Transform start, Transform end)
    {
        _start = start;
        _end = end;
    }

    public void AddIntermediatePoint(Transform point)
    {
        if (point != null && _intermediatePoints.Contains(point) == false)
        {
            _intermediatePoints.Add(point);
        }
    }

    public Transform RemoveLastIntermediatePoint()
    {
        if (_intermediatePoints.Count == 0)
        {
            return null;
        }

        int lastIndex = _intermediatePoints.Count - 1;
        Transform point = _intermediatePoints[lastIndex];
        _intermediatePoints.RemoveAt(lastIndex);
        return point;
    }

    public void AppendWorldPoints(List<Vector3> destination)
    {
        destination.Clear();
        destination.Add(StartPosition);
        foreach (Transform point in _intermediatePoints)
        {
            if (point != null)
            {
                destination.Add(point.position);
            }
        }
        destination.Add(EndPosition);
    }

    private void OnDrawGizmos()
    {
        var points = new List<Vector3>();
        AppendWorldPoints(points);
        Gizmos.color = _gizmoColor;
        for (int index = 1; index < points.Count; index++)
        {
            Gizmos.DrawLine(points[index - 1], points[index]);
        }
        for (int index = 0; index < points.Count; index++)
        {
            Gizmos.DrawWireSphere(points[index], index == 0 || index == points.Count - 1 ? 0.65f : 0.45f);
        }
    }
}
