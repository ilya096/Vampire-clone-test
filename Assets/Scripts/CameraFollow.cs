using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private float _speed = 13f;
    [SerializeField] private Vector3 _offset = new(0f, 12f, -10f);

    private Transform _player;

    public void SetPlayer(Transform player)
    {
        _player = player;
    }

    private void LateUpdate()
    {
        if(_player == null)
        {
            return;
        }

        Vector3 newPosition = _player.position + _offset;
        float t = Mathf.Clamp01(_speed * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, newPosition, t);
    }
}
