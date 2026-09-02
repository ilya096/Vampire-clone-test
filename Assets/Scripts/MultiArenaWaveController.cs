using System;
using Assets.Scripts.Ecs;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Runs a timed first wave in arenas R and O, an objective-driven capture wave
/// in R, and the approved timed second wave in O. It preserves all living
/// enemies between phases and hands route transitions back to zoning.
/// </summary>
public sealed class MultiArenaWaveController : MonoBehaviour
{
    public enum ArenaWavePhase
    {
        Inactive,
        Preparation,
        FirstWave,
        Intermission,
        SecondWave,
        ObjectiveStarted,
        ContractFailed
    }

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private EntityQuery _spawnConfigQuery;
    private EntityQuery _spawnStateQuery;
    private WaveRuntimeController _waveSettings;
    private ArenaRouteController _arenaRoute;
    private float _phaseRemaining;
    private bool _initialized;
    private bool _arenaRStarted;
    private bool _arenaOStarted;
    private int _arenaRCompletedWaveMask;
    private int _arenaOCompletedWaveMask;

    public ArenaId ActiveArena { get; private set; }
    public ArenaWavePhase Phase { get; private set; } = ArenaWavePhase.Inactive;
    public float PhaseRemainingSeconds => Mathf.Max(0f, _phaseRemaining);
    public bool IsSequenceRunning => Phase is ArenaWavePhase.Preparation
        or ArenaWavePhase.FirstWave
        or ArenaWavePhase.Intermission
        or ArenaWavePhase.SecondWave;

    public event Action<ArenaId> ArenaWavesStarted;
    public event Action<ArenaId> ArenaWavesCompleted;
    public event Action<ArenaId, int> WaveCompleted;

    public static bool IsObjectiveDrivenSecondWave(ArenaId arena) => arena is ArenaId.P or ArenaId.R;

    public void Initialize(
        World world,
        Entity playerEntity,
        WaveRuntimeController waveSettings,
        ArenaRouteController arenaRoute)
    {
        _world = world;
        _entityManager = world.EntityManager;
        _playerEntity = playerEntity;
        _waveSettings = waveSettings;
        _arenaRoute = arenaRoute;
        _spawnConfigQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<EnemySpawnConfigComponent>());
        _spawnStateQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<EnemySpawnStateComponent>());
        _arenaRoute.ArenaEntered += HandleArenaEntered;
        _arenaRoute.CaptureObjectiveCompleted += HandleCaptureObjectiveCompleted;
        _arenaRCompletedWaveMask = 0;
        _arenaOCompletedWaveMask = 0;
        _initialized = true;
    }

    /// <summary>
    /// Hidden acceptance helper. It advances the active R/O phase without
    /// destroying enemies or bypassing the zoning integration contract.
    /// </summary>
    public bool AdvanceCurrentPhaseForDebug()
    {
        if (_initialized == false || IsSequenceRunning == false)
        {
            return false;
        }

        switch (Phase)
        {
            case ArenaWavePhase.Preparation:
                EnterPhase(ArenaWavePhase.FirstWave);
                break;
            case ArenaWavePhase.FirstWave:
                PublishWaveCompleted(ActiveArena, 1);
                EnterPhase(ArenaWavePhase.Intermission);
                break;
            case ArenaWavePhase.Intermission:
                EnterPhase(ArenaWavePhase.SecondWave);
                break;
            case ArenaWavePhase.SecondWave:
                if (ActiveArena == ArenaId.R)
                {
                    return _arenaRoute.CompleteCaptureObjectiveForDebug();
                }
                PublishWaveCompleted(ActiveArena, 2);
                CompleteTimedSequence();
                break;
        }

        return true;
    }

    private void HandleArenaEntered(ArenaId arena)
    {
        if (arena is not (ArenaId.R or ArenaId.O))
        {
            return;
        }

        if ((arena == ArenaId.R && _arenaRStarted) || (arena == ArenaId.O && _arenaOStarted))
        {
            return;
        }

        if (IsSequenceRunning)
        {
            Debug.LogError($"Cannot start arena {arena} waves while arena {ActiveArena} is still running.");
            return;
        }

        ActiveArena = arena;
        if (arena == ArenaId.R)
        {
            _arenaRStarted = true;
        }
        else
        {
            _arenaOStarted = true;
        }

        EnterPhase(ArenaWavePhase.Preparation);
        ArenaWavesStarted?.Invoke(arena);
    }

    private void Update()
    {
        if (_initialized == false
            || IsSequenceRunning == false
            || Time.timeScale <= 0f
            || Application.isFocused == false
            || _world == null
            || _world.IsCreated == false
            || _entityManager.Exists(_playerEntity) == false)
        {
            return;
        }

        if (ActiveArena == ArenaId.R && Phase == ArenaWavePhase.SecondWave)
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
            case ArenaWavePhase.Preparation:
                EnterPhase(ArenaWavePhase.FirstWave);
                break;
            case ArenaWavePhase.FirstWave:
                PublishWaveCompleted(ActiveArena, 1);
                EnterPhase(ArenaWavePhase.Intermission);
                break;
            case ArenaWavePhase.Intermission:
                EnterPhase(ArenaWavePhase.SecondWave);
                break;
            case ArenaWavePhase.SecondWave:
                PublishWaveCompleted(ActiveArena, 2);
                CompleteTimedSequence();
                break;
        }
    }

    private void PublishWaveCompleted(ArenaId arena, int waveNumber)
    {
        int bit = 1 << (waveNumber - 1);
        int mask = arena == ArenaId.R ? _arenaRCompletedWaveMask : _arenaOCompletedWaveMask;
        if ((mask & bit) != 0)
        {
            Debug.LogWarning($"Duplicate arena wave completion ignored: arena={arena}, wave={waveNumber}.");
            return;
        }

        mask |= bit;
        if (arena == ArenaId.R)
        {
            _arenaRCompletedWaveMask = mask;
        }
        else
        {
            _arenaOCompletedWaveMask = mask;
        }

        WaveCompleted?.Invoke(arena, waveNumber);
    }

    private void EnterPhase(ArenaWavePhase phase)
    {
        Phase = phase;
        switch (phase)
        {
            case ArenaWavePhase.Preparation:
                _phaseRemaining = _waveSettings.PreparationSeconds;
                SetSpawning(false, 0f, 0);
                GameStateTransitionBanner.Show($"АРЕНА {GetArenaLabel()} · ПОДГОТОВКА");
                break;
            case ArenaWavePhase.FirstWave:
                _phaseRemaining = _waveSettings.FirstWaveSeconds;
                SetSpawning(true, _waveSettings.FirstWaveSpawnInterval, _waveSettings.FirstWaveMaxEnemies);
                GameStateTransitionBanner.Show($"АРЕНА {GetArenaLabel()} · ПЕРВАЯ ВОЛНА");
                break;
            case ArenaWavePhase.Intermission:
                _phaseRemaining = _waveSettings.IntermissionSeconds;
                SetSpawning(false, 0f, 0);
                GameStateTransitionBanner.Show($"АРЕНА {GetArenaLabel()} · ПЕРЕДЫШКА");
                break;
            case ArenaWavePhase.SecondWave:
                _phaseRemaining = ActiveArena == ArenaId.R ? 0f : _waveSettings.SecondWaveSeconds;
                SetSpawning(true, _waveSettings.SecondWaveSpawnInterval, _waveSettings.SecondWaveMaxEnemies);
                if (ActiveArena == ArenaId.R)
                {
                    if (_arenaRoute.BeginCaptureObjective() == false)
                    {
                        Phase = ArenaWavePhase.ContractFailed;
                        SetSpawning(false, 0f, 0);
                        Debug.LogError("Arena R rejected the second-wave capture contract.");
                        return;
                    }
                    GameStateTransitionBanner.Show("ВТОРАЯ ВОЛНА: ЗАХВАТ ТОЧЕК");
                }
                else
                {
                    GameStateTransitionBanner.Show("АРЕНА О · ВТОРАЯ ВОЛНА");
                }
                break;
        }
    }

    private void CompleteTimedSequence()
    {
        _phaseRemaining = 0f;
        SetSpawning(false, 0f, 0);
        Phase = ArenaWavePhase.ObjectiveStarted;

        bool contractAccepted = ActiveArena == ArenaId.O && _arenaRoute.OpenBossArena();

        if (contractAccepted == false)
        {
            Phase = ArenaWavePhase.ContractFailed;
            Debug.LogError($"Arena {ActiveArena} rejected the completed wave-sequence contract.");
            return;
        }

        ArenaWavesCompleted?.Invoke(ActiveArena);
    }

    private void HandleCaptureObjectiveCompleted()
    {
        if (ActiveArena != ArenaId.R || Phase != ArenaWavePhase.SecondWave)
        {
            return;
        }

        PublishWaveCompleted(ArenaId.R, 2);
        _phaseRemaining = 0f;
        SetSpawning(false, 0f, 0);
        Phase = ArenaWavePhase.ObjectiveStarted;
        ArenaWavesCompleted?.Invoke(ArenaId.R);
    }

    private string GetArenaLabel() => ActiveArena == ArenaId.R ? "Р" : "О";

    private void SetSpawning(bool enabled, float interval, int maxEnemies)
    {
        if (_spawnConfigQuery.CalculateEntityCount() != 1 || _spawnStateQuery.CalculateEntityCount() != 1)
        {
            Debug.LogError("Arena waves require exactly one enemy spawn config and state entity.");
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

    private void OnGUI()
    {
        if (_initialized == false || IsSequenceRunning == false || Time.timeScale <= 0f)
        {
            return;
        }

        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        string arenaLabel = GetArenaLabel();
        string phaseLabel = Phase switch
        {
            ArenaWavePhase.Preparation => "ПОДГОТОВКА",
            ArenaWavePhase.FirstWave => "ВОЛНА 1",
            ArenaWavePhase.Intermission => "ПЕРЕДЫШКА",
            ArenaWavePhase.SecondWave => "ВОЛНА 2",
            _ => string.Empty
        };
        string suffix = ActiveArena == ArenaId.R && Phase == ArenaWavePhase.SecondWave
            ? string.Empty
            : $"  {Mathf.CeilToInt(PhaseRemainingSeconds)} c";
        if (ActiveArena == ArenaId.R && Phase == ArenaWavePhase.SecondWave)
        {
            phaseLabel = "ВОЛНА 2 · ЗАХВАТ ТОЧЕК";
        }
        GUI.Box(
            new Rect(Screen.width * 0.5f - 160f, 16f, 320f, 30f),
            $"АРЕНА {arenaLabel} · {phaseLabel}{suffix}");
    }

    private void OnDestroy()
    {
        if (_arenaRoute != null)
        {
            _arenaRoute.ArenaEntered -= HandleArenaEntered;
            _arenaRoute.CaptureObjectiveCompleted -= HandleCaptureObjectiveCompleted;
        }
    }
}
