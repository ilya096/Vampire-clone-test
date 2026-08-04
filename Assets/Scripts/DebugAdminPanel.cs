using Assets.Scripts.Ecs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

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
    private bool _showSpecialCards;
    private GameplayTuningComponent _initialTuning;
    private PlayerProgressionState _initialProgression;
    private HealthComponent _initialHealth;
    private float _initialFirstWave;
    private float _initialSecondWave;
    private float _initialEscortSpeed;
    private float _initialEscortRadius;
    private readonly Dictionary<string, string> _valueInputs = new();

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
        if (GUI.Button(new Rect(panel.x + 12f, y, 170f, 24f), "Сброс игрока/оружия")) { ResetPlayerAndWeapons(); return; } y += 32f;

        GUI.Label(new Rect(panel.x + 12f, y, 350f, 20f), "Прогрессия"); y += 22f;
        tuning.ExperienceRadius = DrawValue(panel.x, y, "XP radius", tuning.ExperienceRadius, 0.5f, 12f); y += 25f;
        progression.ExperienceValueMultiplier = DrawValue(panel.x, y, "XP value", progression.ExperienceValueMultiplier, 0.5f, 5f); y += 25f;
        progression.NextLevelExperience = Mathf.RoundToInt(DrawValue(panel.x, y, "Next XP", progression.NextLevelExperience, 1f, 500f)); y += 28f;
        if (GUI.Button(new Rect(panel.x + 12f, y, 170f, 24f), "Сброс прогрессии")) { ResetProgression(); return; }
        if (GUI.Button(new Rect(panel.x + 195f, y, 177f, 24f), _showSpecialCards ? "Скрыть special-карты" : "Special-карты...")) _showSpecialCards = !_showSpecialCards;
        y += 32f;

        GUI.Label(new Rect(panel.x + 12f, y, 350f, 20f), "Волны и вагонетка"); y += 22f;
        if (_waves != null)
        {
            _waves.FirstWaveSeconds = DrawValue(panel.x, y, "Wave 1 sec", _waves.FirstWaveSeconds, 5f, 90f); y += 25f;
            _waves.SecondWaveSeconds = DrawValue(panel.x, y, "Wave 2 sec", _waves.SecondWaveSeconds, 5f, 120f); y += 25f;
            _waves.EscortSpeed = DrawValue(panel.x, y, "Cart speed", _waves.EscortSpeed, 0.1f, 8f); y += 25f;
            _waves.EscortPlayerRadius = DrawValue(panel.x, y, "Cart radius", _waves.EscortPlayerRadius, 0.5f, 10f); y += 28f;
            if (GUI.Button(new Rect(panel.x + 12f, y, 170f, 24f), "Сброс волн/вагонетки")) { ResetWaves(); return; }
        }

        SetTuning(tuning);
        _entityManager.SetComponentData(_player, progression);
        _entityManager.SetComponentData(_player, health);

        if (_showSpecialCards)
        {
            DrawSpecialCardsPanel(progression);
        }
    }

    private void DrawSpecialCardsPanel(PlayerProgressionState progression)
    {
        Rect panel = new(418f, 70f, 370f, 330f);
        GUI.Box(panel, "SPECIAL-КАРТЫ · прямое включение");
        float y = panel.y + 30f;

        GUI.Label(new Rect(panel.x + 12f, y, 340f, 20f), "Пистолет"); y += 20f;
        DrawSpecialPair(panel.x, ref y, "T1", "ВЗРЫВ", progression.PistolExplosion, "РИКОШЕТ", progression.PistolRicochet, out bool pistolExplosion, out bool pistolRicochet);
        DrawSpecialPair(panel.x, ref y, "T2", "ПРОБИТИЕ", progression.PistolPiercing, "РАЗДВОЕНИЕ", progression.PistolSplitShot, out bool pistolPiercing, out bool pistolSplitShot);
        DrawSpecialPair(panel.x, ref y, "T3", "ТЯЖЁЛАЯ ПУЛЯ", progression.PistolHeavyBullet, "СТИХИЙНЫЙ ЗАРЯД", progression.PistolElementalCharge, out bool pistolHeavyBullet, out bool pistolElementalCharge);

        GUI.Label(new Rect(panel.x + 12f, y, 340f, 20f), "Пулемёт"); y += 20f;
        DrawSpecialPair(panel.x, ref y, "T1", "ЗАМЕДЛЕНИЕ", progression.MachineGunSlow, "ЦЕПНАЯ МОЛНИЯ", progression.MachineGunChainLightning, out bool machineGunSlow, out bool machineGunChainLightning);
        DrawSpecialPair(panel.x, ref y, "T2", "ПРОШИВАНИЕ", progression.MachineGunPiercing, "КАРТЕЧЬ", progression.MachineGunScatter, out bool machineGunPiercing, out bool machineGunScatter);
        DrawSpecialPair(panel.x, ref y, "T3", "ПЕРЕГРЕВ", progression.MachineGunOverheat, "ЭЛЕКТРО-БУРЯ", progression.MachineGunElectricStorm, out bool machineGunOverheat, out bool machineGunElectricStorm);

        if (GUI.Button(new Rect(panel.x + 12f, y + 2f, 346f, 24f), "Сброс special-карт"))
        {
            pistolExplosion = pistolRicochet = pistolPiercing = pistolSplitShot = pistolHeavyBullet = pistolElementalCharge = false;
            machineGunSlow = machineGunChainLightning = machineGunPiercing = machineGunScatter = machineGunOverheat = machineGunElectricStorm = false;
        }

        progression.PistolExplosion = pistolExplosion;
        progression.PistolRicochet = pistolRicochet;
        progression.PistolPiercing = pistolPiercing;
        progression.PistolSplitShot = pistolSplitShot;
        progression.PistolHeavyBullet = pistolHeavyBullet;
        progression.PistolElementalCharge = pistolElementalCharge;
        progression.MachineGunSlow = machineGunSlow;
        progression.MachineGunChainLightning = machineGunChainLightning;
        progression.MachineGunPiercing = machineGunPiercing;
        progression.MachineGunScatter = machineGunScatter;
        progression.MachineGunOverheat = machineGunOverheat;
        progression.MachineGunElectricStorm = machineGunElectricStorm;
        _entityManager.SetComponentData(_player, progression);
    }

    private static void DrawSpecialPair(float x, ref float y, string tier, string firstLabel, bool firstActive, string secondLabel, bool secondActive, out bool firstResult, out bool secondResult)
    {
        GUI.Label(new Rect(x + 12f, y + 3f, 24f, 20f), tier);
        firstResult = DrawSpecialButton(new Rect(x + 40f, y, 150f, 24f), firstLabel, firstActive) ? !firstActive : firstActive;
        secondResult = DrawSpecialButton(new Rect(x + 202f, y, 156f, 24f), secondLabel, secondActive) ? !secondActive : secondActive;
        y += 28f;
    }

    private static bool DrawSpecialButton(Rect rect, string label, bool active)
    {
        Color previousColor = GUI.color;
        GUI.color = active ? new Color(0.25f, 0.95f, 0.35f) : Color.white;
        bool clicked = GUI.Button(rect, label);
        GUI.color = previousColor;
        return clicked;
    }

    private float DrawValue(float x, float y, string label, float value, float min, float max)
    {
        GUI.Label(new Rect(x + 12f, y, 120f, 20f), label);
        float slider = GUI.HorizontalSlider(new Rect(x + 135f, y + 5f, 150f, 20f), value, min, max);
        if (_valueInputs.TryGetValue(label, out string stored) == false || Mathf.Abs(slider - value) > 0.0001f)
        {
            stored = slider.ToString("0.##");
        }

        string input = GUI.TextField(new Rect(x + 292f, y, 80f, 20f), stored);
        _valueInputs[label] = input;
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
