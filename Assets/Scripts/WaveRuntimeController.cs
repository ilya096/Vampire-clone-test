using Assets.Scripts.Ecs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Runs the validated first-arena vertical slice: preparation, two fixed waves,
/// then the cart escort that opens the gate. The values are intentionally local
/// defaults and are exposed for the later debug panel and balance pass.
/// </summary>
public class WaveRuntimeController : MonoBehaviour
{
    public enum FirstArenaPhase
    {
        Preparation,
        FirstWave,
        Intermission,
        SecondWave,
        Escort,
        Complete
    }

    [Header("First letter P timeline")]
    [SerializeField] private float _preparationSeconds = 5f;
    [SerializeField] private float _firstWaveSeconds = 45f;
    [SerializeField] private float _intermissionSeconds = 5f;
    [SerializeField] private float _secondWaveSeconds = 60f;

    [Header("Spawn pressure")]
    [SerializeField] private float _firstWaveSpawnInterval = 0.55f;
    [SerializeField] private int _firstWaveMaxEnemies = 26;
    [SerializeField] private float _secondWaveSpawnInterval = 0.38f;
    [SerializeField] private int _secondWaveMaxEnemies = 36;
    [SerializeField] private float _escortSpawnInterval = 1.1f;
    [SerializeField] private int _escortMaxEnemies = 20;

    [Header("Escort cart")]
    [SerializeField] private float _escortDistance = 24f;
    [SerializeField] private float _escortPlayerRadius = 3f;
    [SerializeField] private float _escortSpeed = 2.2f;
    [SerializeField] private float _escortRollbackSpeed = 1f;

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private EntityQuery _spawnConfigQuery;
    private EntityQuery _spawnStateQuery;
    private Transform _playerVisual;
    private GameObject _cart;
    private GameObject _gate;
    private Vector3 _cartStart;
    private Vector3 _cartEnd;
    private float _phaseRemaining;
    private bool _initialized;

    public FirstArenaPhase Phase { get; private set; }
    public float FirstWaveSeconds { get => _firstWaveSeconds; set => _firstWaveSeconds = Mathf.Max(1f, value); }
    public float SecondWaveSeconds { get => _secondWaveSeconds; set => _secondWaveSeconds = Mathf.Max(1f, value); }
    public float FirstWaveSpawnInterval { get => _firstWaveSpawnInterval; set => _firstWaveSpawnInterval = Mathf.Max(0.05f, value); }
    public float SecondWaveSpawnInterval { get => _secondWaveSpawnInterval; set => _secondWaveSpawnInterval = Mathf.Max(0.05f, value); }
    public float EscortSpawnInterval { get => _escortSpawnInterval; set => _escortSpawnInterval = Mathf.Max(0.05f, value); }
    public float EscortSpeed { get => _escortSpeed; set => _escortSpeed = Mathf.Max(0.1f, value); }
    public float EscortPlayerRadius { get => _escortPlayerRadius; set => _escortPlayerRadius = Mathf.Max(0.5f, value); }
    public float EscortRollbackSpeed { get => _escortRollbackSpeed; set => _escortRollbackSpeed = Mathf.Max(0.1f, value); }
    public float PhaseRemainingSeconds => Mathf.Max(0f, _phaseRemaining);
    public float EscortProgress => _cart == null || _escortDistance <= 0f
        ? 0f
        : Mathf.Clamp01(Vector3.Distance(_cartStart, _cart.transform.position) / _escortDistance);

    public void Initialize(World world, Entity playerEntity, Transform playerVisual)
    {
        _world = world;
        _entityManager = world.EntityManager;
        _playerEntity = playerEntity;
        _playerVisual = playerVisual;
        _spawnConfigQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<EnemySpawnConfigComponent>());
        _spawnStateQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<EnemySpawnStateComponent>());
        _initialized = true;
        EnterPhase(FirstArenaPhase.Preparation);
    }

    private void Update()
    {
        if (_initialized == false || _world == null || _world.IsCreated == false || _entityManager.Exists(_playerEntity) == false)
        {
            return;
        }

        if (Phase == FirstArenaPhase.Escort)
        {
            UpdateEscort();
            return;
        }

        if (Phase == FirstArenaPhase.Complete)
        {
            return;
        }

        _phaseRemaining -= Time.deltaTime;
        if (_phaseRemaining > 0f)
        {
            return;
        }

        switch (Phase)
        {
            case FirstArenaPhase.Preparation:
                EnterPhase(FirstArenaPhase.FirstWave);
                break;
            case FirstArenaPhase.FirstWave:
                EnterPhase(FirstArenaPhase.Intermission);
                break;
            case FirstArenaPhase.Intermission:
                EnterPhase(FirstArenaPhase.SecondWave);
                break;
            case FirstArenaPhase.SecondWave:
                EnterPhase(FirstArenaPhase.Escort);
                break;
        }
    }

    private void EnterPhase(FirstArenaPhase phase)
    {
        Phase = phase;
        switch (phase)
        {
            case FirstArenaPhase.Preparation:
                _phaseRemaining = _preparationSeconds;
                SetSpawning(false, 0f, 0);
                break;
            case FirstArenaPhase.FirstWave:
                _phaseRemaining = _firstWaveSeconds;
                SetSpawning(true, _firstWaveSpawnInterval, _firstWaveMaxEnemies);
                break;
            case FirstArenaPhase.Intermission:
                _phaseRemaining = _intermissionSeconds;
                SetSpawning(false, 0f, 0);
                break;
            case FirstArenaPhase.SecondWave:
                _phaseRemaining = _secondWaveSeconds;
                SetSpawning(true, _secondWaveSpawnInterval, _secondWaveMaxEnemies);
                break;
            case FirstArenaPhase.Escort:
                _phaseRemaining = 0f;
                SetSpawning(true, _escortSpawnInterval, _escortMaxEnemies);
                CreateEscortPresentation();
                break;
            case FirstArenaPhase.Complete:
                _phaseRemaining = 0f;
                SetSpawning(false, 0f, 0);
                if (_gate != null)
                {
                    _gate.GetComponent<Renderer>().material.color = new Color(0.25f, 1f, 0.35f);
                }
                break;
        }
    }

    private void SetSpawning(bool enabled, float interval, int maxEnemies)
    {
        if (_spawnConfigQuery.CalculateEntityCount() != 1 || _spawnStateQuery.CalculateEntityCount() != 1)
        {
            return;
        }

        Entity configEntity = _spawnConfigQuery.GetSingletonEntity();
        EnemySpawnConfigComponent config = _entityManager.GetComponentData<EnemySpawnConfigComponent>(configEntity);
        config.Interval = enabled ? interval : float.PositiveInfinity;
        if (enabled)
        {
            config.MaxEnemies = maxEnemies;
        }
        _entityManager.SetComponentData(configEntity, config);

        Entity stateEntity = _spawnStateQuery.GetSingletonEntity();
        EnemySpawnStateComponent state = _entityManager.GetComponentData<EnemySpawnStateComponent>(stateEntity);
        state.TimeToNextSpawn = enabled ? 0f : float.PositiveInfinity;
        _entityManager.SetComponentData(stateEntity, state);
    }

    private void CreateEscortPresentation()
    {
        if (_cart != null)
        {
            return;
        }

        Vector3 playerPosition = _playerVisual != null ? _playerVisual.position : Vector3.zero;
        _cartStart = SampleGround(playerPosition);
        _cartEnd = SampleGround(_cartStart + Vector3.forward * _escortDistance);

        _cart = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cart.name = "EscortCart_FirstLetterP";
        _cart.transform.position = _cartStart + Vector3.up * 0.35f;
        _cart.transform.localScale = new Vector3(1.4f, 0.7f, 1f);
        Destroy(_cart.GetComponent<Collider>());
        _cart.GetComponent<Renderer>().material.color = new Color(1f, 0.7f, 0.15f);

        _gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _gate.name = "FirstLetterPExitGate";
        _gate.transform.position = _cartEnd + Vector3.up * 1f;
        _gate.transform.localScale = new Vector3(3.2f, 2f, 0.25f);
        Destroy(_gate.GetComponent<Collider>());
        _gate.GetComponent<Renderer>().material.color = new Color(0.9f, 0.2f, 0.2f);
    }

    private void UpdateEscort()
    {
        if (_cart == null)
        {
            return;
        }

        Vector3 playerPosition = _playerVisual != null ? _playerVisual.position : Vector3.zero;
        float distanceToCart = Vector3.Distance(playerPosition, _cart.transform.position);
        Vector3 target = distanceToCart <= _escortPlayerRadius ? _cartEnd : _cartStart;
        float speed = target == _cartEnd ? _escortSpeed : _escortRollbackSpeed;
        Vector3 nextPosition = Vector3.MoveTowards(_cart.transform.position, target + Vector3.up * 0.35f, speed * Time.deltaTime);
        _cart.transform.position = nextPosition;

        if (Vector3.Distance(nextPosition, _cartEnd + Vector3.up * 0.35f) <= 0.01f)
        {
            EnterPhase(FirstArenaPhase.Complete);
        }
    }

    private static Vector3 SampleGround(Vector3 position)
    {
        if (NavMesh.SamplePosition(position + Vector3.up * 2f, out NavMeshHit hit, 8f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return new Vector3(position.x, 0f, position.z);
    }

    private void OnGUI()
    {
        if (_initialized == false || Phase == FirstArenaPhase.Complete || Time.timeScale == 0f)
        {
            return;
        }

        string text = Phase == FirstArenaPhase.Escort
            ? $"ЭСКОРТ ВАГОНЕТКИ  {EscortProgress:P0}"
            : $"{GetPhaseLabel(Phase)}  {Mathf.CeilToInt(PhaseRemainingSeconds)} c";
        GUI.Box(new Rect(Screen.width * 0.5f - 140f, 16f, 280f, 30f), text);
    }

    private static string GetPhaseLabel(FirstArenaPhase phase)
    {
        return phase switch
        {
            FirstArenaPhase.Preparation => "ПОДГОТОВКА",
            FirstArenaPhase.FirstWave => "ВОЛНА 1",
            FirstArenaPhase.Intermission => "ПЕРЕДЫШКА",
            FirstArenaPhase.SecondWave => "ВОЛНА 2",
            _ => string.Empty
        };
    }

    private void OnDestroy()
    {
        if (_cart != null) Destroy(_cart);
        if (_gate != null) Destroy(_gate);
    }
}
