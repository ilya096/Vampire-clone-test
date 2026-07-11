using Assets.Scripts;
using Assets.Scripts.Ecs;
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
    [SerializeField] private EnemyViewSynchronizator _enemyViewSynchronizator;
    [SerializeField] private EnemySpawnConfig _enemySpawnConfig;

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private Entity _playerInputEntity;
    private Entity _enemySpawnerEntity;

    private void Awake()
    {
        ServiceLocator.Register(_inputService);
        ServiceLocator.Register(_enemyViewSynchronizator);

        _world = World.DefaultGameObjectInjectionWorld;
        _entityManager = _world.EntityManager;

        _playerInputEntity = _entityManager.CreateEntity(typeof(PlayerMoveInput));

        CreatePlayer();
        CreateEnemySpawner();

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
            typeof(LocalTransform),
            typeof(HealthComponent)
            );

        _entityManager.SetComponentData(_playerEntity, new MoveSpeed()
        {
            Value = _playerSpeed
        });

        _entityManager.SetComponentData(_playerEntity, LocalTransform.FromPosition(initPos));
        _entityManager.SetComponentData(_playerEntity, new HealthComponent() { Value = 100});
    }

    private void CreateEnemySpawner()
    {
        _enemySpawnerEntity = _entityManager.CreateEntity(
            typeof(EnemySpawnConfigComponent),
            typeof(EnemySpawnStateComponent)
            );

        _entityManager.SetComponentData(_enemySpawnerEntity, new EnemySpawnConfigComponent()
        {
            EnemyHealth = _enemySpawnConfig.EnemyHealth,
            EnemySpeed = _enemySpawnConfig.EnemySpeed,
            Interval = _enemySpawnConfig.Interval,
            MaxEnemies = _enemySpawnConfig.MaxEnemies,
            SpawnRadius = _enemySpawnConfig.SpawnRadius,
            EnemyAttack = _enemySpawnConfig.EnemyAttack,
            EnemyAttackInterval = _enemySpawnConfig.EnemyAttackInterval,
            EnemyRange = _enemySpawnConfig.EnemyRange
        });

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
