using UnityEngine;
using UnityEngine.InputSystem;

public class InputService : MonoBehaviour
{
    private const string PlayerMap = "Player";
    private const string MoveMap = "Move";

    [SerializeField] private InputActionAsset _playerInput;

    private InputActionMap _playerMap;
    private InputAction _moveActon;

    public Vector2 Move => _moveActon?.ReadValue<Vector2>() ?? Vector2.zero;

    private void Awake()
    {
        _playerMap = _playerInput.FindActionMap(PlayerMap);
        _moveActon = _playerMap.FindAction(MoveMap);
    }

    private void OnEnable()
    {
        _playerMap?.Enable();
    }

    private void OnDisable()
    {
        _playerMap?.Disable();
    }
}
