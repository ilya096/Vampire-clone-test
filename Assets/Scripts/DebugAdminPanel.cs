using Assets.Scripts.Ecs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Local-only tuning surface. It is hidden until Shift+Num0 is pressed inside pause.
/// </summary>
public class DebugAdminPanel : MonoBehaviour
{
    private World _world;
    private EntityManager _entityManager;
    private Entity _player;
    private WaveRuntimeController _waves;
    private bool _paused;
    private bool _debugEnabled;
    private GameplayTuningComponent _initialTuning;
    private PlayerProgressionState _initialProgression;
    private HealthComponent _initialHealth;
    private float _initialFirstWave;
    private float _initialSecondWave;
    private float _initialEscortSpeed;
    private float _initialEscortRadius;

    public void Initialize(World world, Entity player)
    {
        _world = world;
        _entityManager = world.EntityManager;
        _player = player;
        _waves = GetComponent<WaveRuntimeController>();
        CaptureInitialValues();
    }

    private void Update()
    {
        if (_world == null || _world.IsCreated == false || _entityManager.Exists(_player) == false || Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.f10Key.wasPressedThisFrame)
        {
            SetPaused(!_paused);
        }

        if (_paused && Keyboard.current.shiftKey.isPressed && Keyboard.current.numpad0Key.wasPressedThisFrame)
        {
            _debugEnabled = !_debugEnabled;
        }
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        Time.timeScale = paused ? 0f : 1f;
    }

    private void OnGUI()
    {
        if (_paused == false)
        {
            return;
        }

        GUI.Box(new Rect(Screen.width * 0.5f - 160f, 20f, 320f, 34f), _debugEnabled ? "ПАУЗА  ·  DEBUG" : "ПАУЗА  ·  Shift+Num 0: debug");
        if (_debugEnabled == false)
        {
            return;
        }

        Rect panel = new(16f, 70f, 390f, Screen.height - 90f);
        GUI.Box(panel, "DEBUG ADMIN PANEL");
        float y = panel.y + 30f;
        GameplayTuningComponent tuning = GetTuning();
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_player);
        HealthComponent health = _entityManager.GetComponentData<HealthComponent>(_player);

        GUI.Label(new Rect(panel.x + 12f, y, 350f, 20f), "Игрок и оружие"); y += 22f;
        tuning.PistolDamage = Mathf.RoundToInt(DrawValue(panel.x, y, "Pistol damage", tuning.PistolDamage, 1f, 200f)); y += 25f;
        tuning.MachineGunDamage = Mathf.RoundToInt(DrawValue(panel.x, y, "MG damage", tuning.MachineGunDamage, 1f, 100f)); y += 25f;
        tuning.PlayerBaseSpeed = DrawValue(panel.x, y, "Move speed", tuning.PlayerBaseSpeed, 1f, 15f); y += 25f;
        health.Value = Mathf.RoundToInt(DrawValue(panel.x, y, "Current HP", health.Value, 1f, health.MaxValue)); y += 28f;
        if (GUI.Button(new Rect(panel.x + 12f, y, 170f, 24f), "Сброс игрока/оружия")) ResetPlayerAndWeapons(); y += 32f;

        GUI.Label(new Rect(panel.x + 12f, y, 350f, 20f), "Прогрессия"); y += 22f;
        tuning.ExperienceRadius = DrawValue(panel.x, y, "XP radius", tuning.ExperienceRadius, 0.5f, 12f); y += 25f;
        progression.ExperienceValueMultiplier = DrawValue(panel.x, y, "XP value", progression.ExperienceValueMultiplier, 0.5f, 5f); y += 25f;
        progression.NextLevelExperience = Mathf.RoundToInt(DrawValue(panel.x, y, "Next XP", progression.NextLevelExperience, 1f, 500f)); y += 28f;
        if (GUI.Button(new Rect(panel.x + 12f, y, 170f, 24f), "Сброс прогрессии")) ResetProgression(); y += 32f;

        GUI.Label(new Rect(panel.x + 12f, y, 350f, 20f), "Волны и вагонетка"); y += 22f;
        if (_waves != null)
        {
            _waves.FirstWaveSeconds = DrawValue(panel.x, y, "Wave 1 sec", _waves.FirstWaveSeconds, 5f, 90f); y += 25f;
            _waves.SecondWaveSeconds = DrawValue(panel.x, y, "Wave 2 sec", _waves.SecondWaveSeconds, 5f, 120f); y += 25f;
            _waves.EscortSpeed = DrawValue(panel.x, y, "Cart speed", _waves.EscortSpeed, 0.1f, 8f); y += 25f;
            _waves.EscortPlayerRadius = DrawValue(panel.x, y, "Cart radius", _waves.EscortPlayerRadius, 0.5f, 10f); y += 28f;
            if (GUI.Button(new Rect(panel.x + 12f, y, 170f, 24f), "Сброс волн/вагонетки")) ResetWaves();
        }

        SetTuning(tuning);
        _entityManager.SetComponentData(_player, progression);
        _entityManager.SetComponentData(_player, health);
    }

    private float DrawValue(float x, float y, string label, float value, float min, float max)
    {
        GUI.Label(new Rect(x + 12f, y, 120f, 20f), label);
        float slider = GUI.HorizontalSlider(new Rect(x + 135f, y + 5f, 150f, 20f), value, min, max);
        string input = GUI.TextField(new Rect(x + 292f, y, 80f, 20f), slider.ToString("0.##"));
        return float.TryParse(input, out float exact) ? Mathf.Clamp(exact, min, max) : slider;
    }

    private GameplayTuningComponent GetTuning() => _entityManager.GetComponentData<GameplayTuningComponent>(_entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameplayTuningComponent>()).GetSingletonEntity());
    private void SetTuning(GameplayTuningComponent tuning) => _entityManager.SetComponentData(_entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameplayTuningComponent>()).GetSingletonEntity(), tuning);

    private void CaptureInitialValues()
    {
        _initialTuning = GetTuning();
        _initialProgression = _entityManager.GetComponentData<PlayerProgressionState>(_player);
        _initialHealth = _entityManager.GetComponentData<HealthComponent>(_player);
        if (_waves == null) return;
        _initialFirstWave = _waves.FirstWaveSeconds;
        _initialSecondWave = _waves.SecondWaveSeconds;
        _initialEscortSpeed = _waves.EscortSpeed;
        _initialEscortRadius = _waves.EscortPlayerRadius;
    }

    private void ResetPlayerAndWeapons() { SetTuning(_initialTuning); _entityManager.SetComponentData(_player, _initialHealth); }
    private void ResetProgression() => _entityManager.SetComponentData(_player, _initialProgression);
    private void ResetWaves()
    {
        if (_waves == null) return;
        _waves.FirstWaveSeconds = _initialFirstWave;
        _waves.SecondWaveSeconds = _initialSecondWave;
        _waves.EscortSpeed = _initialEscortSpeed;
        _waves.EscortPlayerRadius = _initialEscortRadius;
    }

    private void OnDestroy()
    {
        if (_paused) Time.timeScale = 1f;
    }
}
