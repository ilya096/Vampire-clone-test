using Assets.Scripts.Ecs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;

/// <summary>
/// Runs the first-arena vertical slice: preparation, a fixed first wave, then
/// an objective-driven second wave where the cart escort unlocks the P -> R route gate. The values are intentionally local
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
    private EscortRoute _escortRoute;
    private GameObject _cart;
    private Vector3 _cartStart;
    private Vector3 _cartEnd;
    private readonly List<Vector3> _escortPoints = new();
    private readonly List<Renderer> _routeArrowRenderers = new();
    private readonly List<float> _routeArrowDistances = new();
    private GameObject _routeArrowRoot;
    private Mesh _routeArrowMesh;
    private float _escortPathLength;
    private float _escortDistanceTravelled;
    private float _phaseRemaining;
    private bool _initialized;
    private bool _completionRaised;
    private int _completedWaveMask;

    public FirstArenaPhase Phase { get; private set; }
    public float PreparationSeconds => _preparationSeconds;
    public float FirstWaveSeconds { get => _firstWaveSeconds; set => _firstWaveSeconds = Mathf.Max(1f, value); }
    public int FirstWaveMaxEnemies => _firstWaveMaxEnemies;
    public float SecondWaveSeconds { get => _secondWaveSeconds; set => _secondWaveSeconds = Mathf.Max(1f, value); }
    public int SecondWaveMaxEnemies => _secondWaveMaxEnemies;
    public float IntermissionSeconds => _intermissionSeconds;
    public float FirstWaveSpawnInterval { get => _firstWaveSpawnInterval; set => _firstWaveSpawnInterval = Mathf.Max(0.05f, value); }
    public float SecondWaveSpawnInterval { get => _secondWaveSpawnInterval; set => _secondWaveSpawnInterval = Mathf.Max(0.05f, value); }
    public float EscortSpawnInterval { get => _escortSpawnInterval; set => _escortSpawnInterval = Mathf.Max(0.05f, value); }
    public float EscortSpeed { get => _escortSpeed; set => _escortSpeed = Mathf.Max(0.1f, value); }
    public float EscortPlayerRadius { get => _escortPlayerRadius; set => _escortPlayerRadius = Mathf.Max(0.5f, value); }
    public float EscortRollbackSpeed { get => _escortRollbackSpeed; set => _escortRollbackSpeed = Mathf.Max(0.1f, value); }
    public float PhaseRemainingSeconds => Mathf.Max(0f, _phaseRemaining);
    public float EscortProgress => _cart == null || _escortPathLength <= 0f
        ? 0f
        : Mathf.Clamp01(_escortDistanceTravelled / _escortPathLength);
    public event Action FirstArenaCompleted;
    public event Action<int> WaveCompleted;

    /// <summary>
    /// Hidden acceptance helper used by DebugAdminPanel. It advances only the
    /// current first-arena phase and never removes carry-over enemies.
    /// </summary>
    public bool AdvanceCurrentPhaseForDebug()
    {
        if (_initialized == false)
        {
            return false;
        }

        switch (Phase)
        {
            case FirstArenaPhase.Preparation:
                EnterPhase(FirstArenaPhase.FirstWave);
                return true;
            case FirstArenaPhase.FirstWave:
                PublishWaveCompleted(1);
                EnterPhase(FirstArenaPhase.Intermission);
                return true;
            case FirstArenaPhase.Intermission:
                EnterPhase(FirstArenaPhase.SecondWave);
                return true;
            case FirstArenaPhase.SecondWave:
            case FirstArenaPhase.Escort:
                PublishWaveCompleted(2);
                EnterPhase(FirstArenaPhase.Complete);
                return true;
            default:
                return false;
        }
    }

    public void Initialize(World world, Entity playerEntity, Transform playerVisual)
    {
        _world = world;
        _entityManager = world.EntityManager;
        _playerEntity = playerEntity;
        _playerVisual = playerVisual;
        _escortRoute = FindAnyObjectByType<EscortRoute>();
        _spawnConfigQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<EnemySpawnConfigComponent>());
        _spawnStateQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<EnemySpawnStateComponent>());
        _completionRaised = false;
        _completedWaveMask = 0;
        _initialized = true;
        EnterPhase(FirstArenaPhase.Preparation);
    }

    private void Update()
    {
        if (_initialized == false || _world == null || _world.IsCreated == false || _entityManager.Exists(_playerEntity) == false)
        {
            return;
        }

        if (Phase is FirstArenaPhase.SecondWave or FirstArenaPhase.Escort)
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
                PublishWaveCompleted(1);
                EnterPhase(FirstArenaPhase.Intermission);
                break;
            case FirstArenaPhase.Intermission:
                EnterPhase(FirstArenaPhase.SecondWave);
                break;
            case FirstArenaPhase.SecondWave:
                break;
        }
    }

    private void PublishWaveCompleted(int waveNumber)
    {
        int bit = 1 << (waveNumber - 1);
        if ((_completedWaveMask & bit) != 0)
        {
            Debug.LogWarning($"Duplicate first-arena wave completion ignored: wave={waveNumber}.");
            return;
        }

        _completedWaveMask |= bit;
        WaveCompleted?.Invoke(waveNumber);
    }

    private void EnterPhase(FirstArenaPhase phase)
    {
        Phase = phase;
        switch (phase)
        {
            case FirstArenaPhase.Preparation:
                _phaseRemaining = _preparationSeconds;
                SetSpawning(false, 0f, 0);
                GameStateTransitionBanner.Show("ПОДГОТОВКА");
                break;
            case FirstArenaPhase.FirstWave:
                _phaseRemaining = _firstWaveSeconds;
                SetSpawning(true, _firstWaveSpawnInterval, _firstWaveMaxEnemies);
                GameStateTransitionBanner.Show("ПЕРВАЯ ВОЛНА");
                break;
            case FirstArenaPhase.Intermission:
                _phaseRemaining = _intermissionSeconds;
                SetSpawning(false, 0f, 0);
                GameStateTransitionBanner.Show("ПЕРЕДЫШКА");
                break;
            case FirstArenaPhase.SecondWave:
                _phaseRemaining = 0f;
                SetSpawning(true, _secondWaveSpawnInterval, _secondWaveMaxEnemies);
                CreateEscortPresentation();
                GameStateTransitionBanner.Show("ВТОРАЯ ВОЛНА: СОПРОВОЖДЕНИЕ");
                break;
            case FirstArenaPhase.Escort:
                _phaseRemaining = 0f;
                SetSpawning(true, _escortSpawnInterval, _escortMaxEnemies);
                CreateEscortPresentation();
                break;
            case FirstArenaPhase.Complete:
                _phaseRemaining = 0f;
                SetSpawning(false, 0f, 0);
                SetRouteArrowsVisible(false);
                if (_completionRaised == false)
                {
                    _completionRaised = true;
                    FirstArenaCompleted?.Invoke();
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
        if (_escortRoute != null && _escortRoute.IsConfigured)
        {
            _escortRoute.AppendWorldPoints(_escortPoints);
        }
        else
        {
            _escortPoints.Clear();
            _escortPoints.Add(playerPosition);
            _escortPoints.Add(playerPosition + Vector3.forward * _escortDistance);
        }

        for (int index = 0; index < _escortPoints.Count; index++) _escortPoints[index] = SampleGround(_escortPoints[index]);
        _cartStart = _escortPoints[0];
        _cartEnd = _escortPoints[_escortPoints.Count - 1];
        _escortPathLength = GetEscortPathLength();
        _escortDistanceTravelled = 0f;

        _cart = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cart.name = "EscortCart_FirstLetterP";
        _cart.transform.position = _cartStart + Vector3.up * 0.35f;
        _cart.transform.localScale = new Vector3(1.4f, 0.7f, 1f);
        Destroy(_cart.GetComponent<Collider>());
        RuntimeRendererUtility.ConfigureMesh(_cart.GetComponent<Renderer>(), new Color(1f, 0.7f, 0.15f));
        CreateRouteArrows();
    }

    private void UpdateEscort()
    {
        if (_cart == null)
        {
            return;
        }

        Vector3 playerPosition = _playerVisual != null ? _playerVisual.position : Vector3.zero;
        float distanceToCart = Vector3.Distance(playerPosition, _cart.transform.position);
        float deltaDistance = (distanceToCart <= _escortPlayerRadius ? _escortSpeed : -_escortRollbackSpeed) * Time.deltaTime;
        _escortDistanceTravelled = Mathf.Clamp(_escortDistanceTravelled + deltaDistance, 0f, _escortPathLength);
        Vector3 nextPosition = EvaluateEscortPath(_escortDistanceTravelled);
        _cart.transform.position = nextPosition + Vector3.up * 0.35f;
        UpdateRouteArrows();

        if (_escortDistanceTravelled >= _escortPathLength)
        {
            PublishWaveCompleted(2);
            EnterPhase(FirstArenaPhase.Complete);
        }
    }

    private void CreateRouteArrows()
    {
        if (_routeArrowRoot != null || _escortPathLength <= 0f)
        {
            return;
        }

        _routeArrowRoot = new GameObject("EscortRouteArrows");
        _routeArrowMesh = CreateRouteArrowMesh();
        const float spacing = 2.25f;
        for (float distance = spacing * 0.5f; distance < _escortPathLength; distance += spacing)
        {
            Vector3 position = EvaluateEscortPath(distance);
            Vector3 next = EvaluateEscortPath(Mathf.Min(distance + 0.25f, _escortPathLength));
            Vector3 direction = next - position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                continue;
            }

            GameObject arrow = new($"RouteArrow_{_routeArrowRenderers.Count + 1:00}");
            arrow.transform.SetParent(_routeArrowRoot.transform, false);
            arrow.transform.position = position + Vector3.up * 0.035f;
            arrow.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            arrow.transform.localScale = Vector3.one * 0.85f;
            arrow.AddComponent<MeshFilter>().sharedMesh = _routeArrowMesh;
            MeshRenderer renderer = arrow.AddComponent<MeshRenderer>();
            RuntimeRendererUtility.ConfigureMesh(renderer, new Color(1f, 0.72f, 0.18f, 0.18f));
            _routeArrowRenderers.Add(renderer);
            _routeArrowDistances.Add(distance);
        }
    }

    private void UpdateRouteArrows()
    {
        for (int index = 0; index < _routeArrowRenderers.Count; index++)
        {
            float flow = Mathf.Repeat(Time.time * 0.9f - _routeArrowDistances[index] * 0.18f, 1f);
            float pulse = Mathf.SmoothStep(0.08f, 0.28f, 1f - Mathf.Abs(flow * 2f - 1f));
            RuntimeRendererUtility.SetColor(
                _routeArrowRenderers[index],
                new Color(1f, 0.72f, 0.18f, pulse));
        }
    }

    private void SetRouteArrowsVisible(bool visible)
    {
        if (_routeArrowRoot != null)
        {
            _routeArrowRoot.SetActive(visible);
        }
    }

    private static Mesh CreateRouteArrowMesh()
    {
        Mesh mesh = new() { name = "EscortRouteArrow" };
        mesh.vertices = new[]
        {
            new Vector3(-0.14f, 0f, -0.48f),
            new Vector3(0.14f, 0f, -0.48f),
            new Vector3(-0.14f, 0f, 0.12f),
            new Vector3(0.14f, 0f, 0.12f),
            new Vector3(-0.36f, 0f, 0.08f),
            new Vector3(0.36f, 0f, 0.08f),
            new Vector3(0f, 0f, 0.58f)
        };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5 };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private float GetEscortPathLength()
    {
        float length = 0f;
        for (int index = 1; index < _escortPoints.Count; index++) length += Vector3.Distance(_escortPoints[index - 1], _escortPoints[index]);
        return length;
    }

    private Vector3 EvaluateEscortPath(float distance)
    {
        float remaining = distance;
        for (int index = 1; index < _escortPoints.Count; index++)
        {
            Vector3 from = _escortPoints[index - 1];
            Vector3 to = _escortPoints[index];
            float segmentLength = Vector3.Distance(from, to);
            if (remaining <= segmentLength || index == _escortPoints.Count - 1)
            {
                return Vector3.Lerp(from, to, segmentLength <= 0f ? 1f : remaining / segmentLength);
            }
            remaining -= segmentLength;
        }
        return _cartEnd;
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

        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        string text = Phase is FirstArenaPhase.SecondWave or FirstArenaPhase.Escort
            ? $"ВОЛНА 2 · СОПРОВОЖДЕНИЕ  {EscortProgress:P0}"
            : $"{GetPhaseLabel(Phase)}  {Mathf.CeilToInt(PhaseRemainingSeconds)} c";
        GUI.Box(new Rect(Screen.width * 0.5f - 140f, 16f, 280f, 30f), text);

        if (_cart != null && (Phase is FirstArenaPhase.SecondWave or FirstArenaPhase.Escort))
        {
            Camera camera = Camera.main;
            Vector3 labelPosition = _cart.transform.position + Vector3.up * 1.2f;
            ObjectiveGuidanceGui.DrawWorldProgress(camera, labelPosition, $"ВАГОНЕТКА  {EscortProgress:P0}", new Color(1f, 0.76f, 0.2f));
            ObjectiveGuidanceGui.DrawOffscreenIndicator(camera, labelPosition, "ВАГОНЕТКА", new Color(1f, 0.76f, 0.2f));
        }
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
        if (_routeArrowRoot != null) Destroy(_routeArrowRoot);
        if (_routeArrowMesh != null) Destroy(_routeArrowMesh);
    }
}
