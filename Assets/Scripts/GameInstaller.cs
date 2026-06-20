using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameInstaller : MonoBehaviour
{
    [SerializeField] private float _playerSpeed = 5f;
    [SerializeField] private InputService _inputService;
    [SerializeField] private PlayerView _player;
    [SerializeField] private CameraFollow _cameraFollow;

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private Entity _playerInputEntity;

    private void Awake()
    {
        ServiceLocator.Register(_inputService);

        _world = World.DefaultGameObjectInjectionWorld;
        _entityManager = _world.EntityManager;

        _playerInputEntity = _entityManager.CreateEntity(typeof(PlayerMoveInput));

        CreatePlayer();

        _player.Initialize(_world, _playerEntity);
        _cameraFollow.SetPlayer(_player.transform);
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<InputService>();

        DestroyEntity(_playerEntity);
        DestroyEntity(_playerInputEntity);
    }

    private void CreatePlayer()
    {
        Vector3 playerPos = _player.transform.position;
        float3 initPos = new float3(playerPos.x, 0, playerPos.z);

        _playerEntity = _entityManager.CreateEntity(
            typeof(PlayerTag),
            typeof(MoveSpeed),
            typeof(LocalTransform)
            );

        _entityManager.SetComponentData(_playerEntity, new MoveSpeed()
        {
            Value = _playerSpeed
        });

        _entityManager.SetComponentData(_playerEntity, LocalTransform.FromPosition(initPos));
    }

    private void DestroyEntity(Entity entity)
    {
        if(_world == null || _world.IsCreated == false)
        {
            return;
        }

        if(entity != Entity.Null && _entityManager.Exists(entity))
        {
            _entityManager.DestroyEntity(entity);
        }
    }
}
