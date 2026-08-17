using System;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Runtime state machine for the continuous P -> R -> O route. Enemy waves and
/// the boss remain separate features and advance this controller through the
/// explicit BeginCaptureObjective and OpenBossArena integration points.
/// </summary>
public sealed class ArenaRouteController : MonoBehaviour
{
    public enum RoutePhase
    {
        FirstArena,
        MovingToR,
        WaitingForRWaves,
        CapturingR,
        MovingToO,
        WaitingForOWaves,
        BossArena
    }

    [SerializeField] private ArenaRouteLayout _layout;
    [SerializeField] private float _overviewSeconds = 1.6f;
    [SerializeField] private float _overviewZoomMultiplier = 1.45f;

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private WaveRuntimeController _firstArenaWaves;
    private CameraFollow _cameraFollow;
    private bool _initialized;
    private string _announcement = string.Empty;
    private float _announcementUntil;

    public RoutePhase Phase { get; private set; } = RoutePhase.FirstArena;
    public ArenaRouteLayout Layout => _layout;
    public event Action<ArenaId> ArenaEntered;
    public event Action CaptureObjectiveCompleted;
    public event Action BossArenaOpened;

    public void Initialize(
        World world,
        Entity playerEntity,
        WaveRuntimeController firstArenaWaves,
        CameraFollow cameraFollow)
    {
        _layout = _layout != null ? _layout : FindAnyObjectByType<ArenaRouteLayout>();
        if (_layout == null)
        {
            Debug.LogWarning("Arena route zoning is disabled: ArenaRouteLayout was not found.");
            enabled = false;
            return;
        }

        if (_layout.ValidateConfiguration(out string error) == false)
        {
            Debug.LogWarning($"Arena route zoning is disabled: {error}");
            enabled = false;
            return;
        }

        _world = world;
        _entityManager = world.EntityManager;
        _playerEntity = playerEntity;
        _firstArenaWaves = firstArenaWaves;
        _cameraFollow = cameraFollow;
        _firstArenaWaves.FirstArenaCompleted += HandleFirstArenaCompleted;
        ResetRoute();
        _initialized = true;

        if (_firstArenaWaves.Phase == WaveRuntimeController.FirstArenaPhase.Complete)
        {
            HandleFirstArenaCompleted();
        }
    }

    public void ResetRoute()
    {
        Phase = RoutePhase.FirstArena;
        _layout.GatePToR.SetOpen(false);
        _layout.GateRToO.SetOpen(false);
        _layout.BossBoundaryO.SetOpen(false);
        foreach (ArenaCapturePoint capturePoint in _layout.CapturePointsR)
        {
            capturePoint.ResetProgress();
        }
        _announcement = string.Empty;
        _announcementUntil = 0f;
    }

    /// <summary>Called by enemy_waves after both waves in arena R.</summary>
    public bool BeginCaptureObjective()
    {
        if (Phase != RoutePhase.WaitingForRWaves)
        {
            return false;
        }

        Phase = RoutePhase.CapturingR;
        Announce("ЗАХВАТИТЕ ТРИ ТОЧКИ", 2.5f);
        return true;
    }

    /// <summary>Called by enemy_waves after both outer-ring waves in arena O.</summary>
    public bool OpenBossArena()
    {
        if (Phase != RoutePhase.WaitingForOWaves)
        {
            return false;
        }

        _layout.BossBoundaryO.SetOpen(true);
        Phase = RoutePhase.BossArena;
        Announce("ЦЕНТР ОТКРЫТ · БОСС", 3f);
        BossArenaOpened?.Invoke();
        return true;
    }

    private void Update()
    {
        if (_initialized == false
            || Time.timeScale <= 0f
            || Application.isFocused == false
            || _world == null
            || _world.IsCreated == false
            || _entityManager.Exists(_playerEntity) == false)
        {
            return;
        }

        LocalTransform playerTransform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);
        Vector3 playerPosition = new(playerTransform.Position.x, playerTransform.Position.y, playerTransform.Position.z);

        switch (Phase)
        {
            case RoutePhase.MovingToR:
                if (_layout.ArenaR.Contains(playerPosition))
                {
                    _layout.GatePToR.SetOpen(false);
                    Phase = RoutePhase.WaitingForRWaves;
                    Announce("АРЕНА Р", 2f);
                    ArenaEntered?.Invoke(ArenaId.R);
                }
                break;

            case RoutePhase.CapturingR:
                UpdateCaptureObjective(playerPosition);
                break;

            case RoutePhase.MovingToO:
                if (_layout.ArenaO.Contains(playerPosition))
                {
                    _layout.GateRToO.SetOpen(false);
                    Phase = RoutePhase.WaitingForOWaves;
                    Announce("АРЕНА О", 2f);
                    ArenaEntered?.Invoke(ArenaId.O);
                }
                break;
        }
    }

    private void HandleFirstArenaCompleted()
    {
        if (Phase != RoutePhase.FirstArena)
        {
            return;
        }

        _layout.GatePToR.SetOpen(true);
        Phase = RoutePhase.MovingToR;
        Announce("ПРОХОД В АРЕНУ Р ОТКРЫТ", 2.5f);
        PlayOverview(_layout.ArenaR.transform.position);
    }

    private void UpdateCaptureObjective(Vector3 playerPosition)
    {
        bool allCompleted = true;
        foreach (ArenaCapturePoint capturePoint in _layout.CapturePointsR)
        {
            capturePoint.Tick(playerPosition, Time.deltaTime);
            allCompleted &= capturePoint.IsCompleted;
        }

        if (allCompleted == false)
        {
            return;
        }

        _layout.GateRToO.SetOpen(true);
        Phase = RoutePhase.MovingToO;
        Announce("ПРОХОД В АРЕНУ О ОТКРЫТ", 2.5f);
        PlayOverview(_layout.ArenaO.transform.position);
        CaptureObjectiveCompleted?.Invoke();
    }

    private void PlayOverview(Vector3 target)
    {
        if (_cameraFollow != null)
        {
            _cameraFollow.PlayOverview(target, _overviewSeconds, _overviewZoomMultiplier);
        }
    }

    private void Announce(string text, float seconds)
    {
        _announcement = text;
        _announcementUntil = Time.unscaledTime + seconds;
    }

    private void OnGUI()
    {
        if (_initialized == false || Time.timeScale <= 0f)
        {
            return;
        }

        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        string objective = GetObjectiveText();
        if (string.IsNullOrEmpty(objective) == false)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 170f, 52f, 340f, 30f), objective);
        }

        if (Phase == RoutePhase.CapturingR)
        {
            for (int index = 0; index < _layout.CapturePointsR.Length; index++)
            {
                ArenaCapturePoint point = _layout.CapturePointsR[index];
                string status = point.IsCompleted ? "ГОТОВО" : $"{point.Progress:P0}";
                GUI.Box(new Rect(Screen.width * 0.5f - 150f + index * 105f, 88f, 90f, 28f), $"{index + 1}: {status}");
            }
        }

        if (Time.unscaledTime < _announcementUntil)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.28f, 440f, 44f), _announcement);
        }
    }

    private string GetObjectiveText()
    {
        return Phase switch
        {
            RoutePhase.MovingToR => "ПЕРЕЙДИТЕ В АРЕНУ Р",
            RoutePhase.WaitingForRWaves => "АРЕНА Р · ДВЕ ВОЛНЫ",
            RoutePhase.CapturingR => "УДЕРЖИВАЙТЕ ТОЧКИ ПО 5 СЕКУНД",
            RoutePhase.MovingToO => "ПЕРЕЙДИТЕ В АРЕНУ О",
            RoutePhase.WaitingForOWaves => "АРЕНА О · ВНЕШНЕЕ КОЛЬЦО",
            RoutePhase.BossArena => string.Empty,
            _ => string.Empty
        };
    }

    private void OnDestroy()
    {
        if (_firstArenaWaves != null)
        {
            _firstArenaWaves.FirstArenaCompleted -= HandleFirstArenaCompleted;
        }
    }
}
