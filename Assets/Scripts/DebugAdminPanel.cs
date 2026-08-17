using Assets.Scripts.Ecs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Local-only tuning surface. It is hidden until Shift+Num0 is pressed inside pause.
/// </summary>
public class DebugAdminPanel : MonoBehaviour
{
    private World _world;
    private EntityManager _entityManager;
    private Entity _player;
    private WaveRuntimeController _waves;
    private MultiArenaWaveController _multiArenaWaves;
    private ArenaRouteController _arenaRoute;
    private FinalBossRuntimeController _finalBoss;
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
    private Vector2 _debugPanelScroll;
    private bool _showRuntimeLog;
    private Vector2 _runtimeLogScroll;
    private bool _scrollRuntimeLogToBottom;
    private GUIStyle _runtimeLogStyle;
    private int _observedRuntimeLogVersion;
    private bool _controlErrorArmed;

    public void Initialize(World world, Entity player)
    {
        _world = world;
        _entityManager = world.EntityManager;
        _player = player;
        _waves = GetComponent<WaveRuntimeController>();
        _multiArenaWaves = GetComponent<MultiArenaWaveController>();
        _arenaRoute = GetComponent<ArenaRouteController>();
        _finalBoss = GetComponent<FinalBossRuntimeController>();
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

        int previousDepth = GUI.depth;
        GUI.depth = -1000;
        try
        {
            DrawPauseInterface();
        }
        finally
        {
            GUI.depth = previousDepth;
        }
    }

    private void DrawPauseInterface()
    {

        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        GUI.Box(new Rect(Screen.width * 0.5f - 160f, 20f, 320f, 34f), _debugEnabled ? "ПАУЗА  ·  DEBUG" : "ПАУЗА  ·  Shift+Num 0: debug");

        if (Debug.isDebugBuild)
        {
            int errorCount = DevelopmentLogBuffer.ErrorCount;
            string logButtonLabel = _showRuntimeLog ? "СКРЫТЬ ЖУРНАЛ" : $"ЖУРНАЛ ({errorCount})";
            if (GUI.Button(new Rect(Screen.width - 176f, 20f, 160f, 34f), logButtonLabel))
            {
                _showRuntimeLog = !_showRuntimeLog;
                _scrollRuntimeLogToBottom = _showRuntimeLog;
            }

            if (_showRuntimeLog)
            {
                DrawRuntimeLogPanel();
                return;
            }
        }

        if (_debugEnabled == false)
        {
            return;
        }

        float panelHeight = Mathf.Clamp(Screen.height - 86f, 220f, 560f);
        Rect panel = new(16f, 70f, 402f, panelHeight);
        DrawOpaquePanel(panel, "DEBUG ADMIN PANEL");

        Rect viewport = new(panel.x + 8f, panel.y + 28f, panel.width - 16f, panel.height - 36f);
        Rect content = new(0f, 0f, 380f, 790f);
        _debugPanelScroll = GUI.BeginScrollView(viewport, _debugPanelScroll, content, false, true);

        float x = 0f;
        float y = 4f;
        GameplayTuningComponent tuning = GetTuning();
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_player);
        HealthComponent health = _entityManager.GetComponentData<HealthComponent>(_player);
        try
        {
            GUI.Label(new Rect(x + 12f, y, 350f, 20f), "Игрок и оружие"); y += 22f;
            tuning.PistolDamage = Mathf.RoundToInt(DrawValue(x, y, "Pistol damage", tuning.PistolDamage, 1f, 200f)); y += 25f;
            tuning.MachineGunDamage = Mathf.RoundToInt(DrawValue(x, y, "MG damage", tuning.MachineGunDamage, 1f, 100f)); y += 25f;
            tuning.PlayerBaseSpeed = DrawValue(x, y, "Move speed", tuning.PlayerBaseSpeed, 1f, 15f); y += 25f;
            health.Value = Mathf.RoundToInt(DrawValue(x, y, "Current HP", health.Value, 1f, health.MaxValue)); y += 28f;
            if (GUI.Button(new Rect(x + 12f, y, 170f, 24f), "Сброс игрока/оружия")) { ResetPlayerAndWeapons(); return; } y += 32f;

            GUI.Label(new Rect(x + 12f, y, 350f, 20f), "Прогрессия"); y += 22f;
            tuning.ExperienceRadius = DrawValue(x, y, "XP radius", tuning.ExperienceRadius, 0.5f, 12f); y += 25f;
            progression.ExperienceValueMultiplier = DrawValue(x, y, "XP value", progression.ExperienceValueMultiplier, 0.5f, 5f); y += 25f;
            progression.NextLevelExperience = Mathf.RoundToInt(DrawValue(x, y, "Next XP", progression.NextLevelExperience, 1f, 500f)); y += 28f;
            if (GUI.Button(new Rect(x + 12f, y, 170f, 24f), "Сброс прогрессии")) { ResetProgression(); return; }
            if (GUI.Button(new Rect(x + 195f, y, 177f, 24f), _showSpecialCards ? "Скрыть special-карты" : "Special-карты...")) _showSpecialCards = !_showSpecialCards;
            y += 32f;

            GUI.Label(new Rect(x + 12f, y, 350f, 20f), "Волны и вагонетка"); y += 22f;
            if (_waves != null)
            {
                _waves.FirstWaveSeconds = DrawValue(x, y, "Wave 1 sec (all)", _waves.FirstWaveSeconds, 5f, 90f); y += 25f;
                _waves.SecondWaveSeconds = DrawValue(x, y, "Wave 2 sec (all)", _waves.SecondWaveSeconds, 5f, 120f); y += 25f;
                _waves.EscortSpeed = DrawValue(x, y, "Cart speed", _waves.EscortSpeed, 0.1f, 8f); y += 25f;
                _waves.EscortPlayerRadius = DrawValue(x, y, "Cart radius", _waves.EscortPlayerRadius, 0.5f, 10f); y += 28f;
                if (GUI.Button(new Rect(x + 12f, y, 170f, 24f), "Сброс волн/вагонетки")) { ResetWaves(); return; }
                bool previousEnabled = GUI.enabled;
                GUI.enabled = _waves.Phase != WaveRuntimeController.FirstArenaPhase.Complete;
                if (GUI.Button(new Rect(x + 195f, y, 177f, 24f), "Следующий этап П")) _waves.AdvanceCurrentPhaseForDebug();
                GUI.enabled = previousEnabled;
                y += 32f;
            }

            if (_multiArenaWaves != null)
            {
                string activeArena = _multiArenaWaves.Phase == MultiArenaWaveController.ArenaWavePhase.Inactive
                    ? "—"
                    : _multiArenaWaves.ActiveArena.ToString();
                GUI.Label(new Rect(x + 12f, y, 350f, 20f), $"Арена Р/О: {activeArena} · {_multiArenaWaves.Phase}"); y += 22f;
                bool previousEnabled = GUI.enabled;
                GUI.enabled = _multiArenaWaves.IsSequenceRunning;
                if (GUI.Button(new Rect(x + 12f, y, 360f, 24f), "Следующий этап волн Р/О")) _multiArenaWaves.AdvanceCurrentPhaseForDebug();
                GUI.enabled = previousEnabled;
                y += 32f;
            }

            GUI.Label(new Rect(x + 12f, y, 350f, 20f), "Arena geometry acceptance"); y += 22f;
            if (_arenaRoute != null)
            {
                GUI.Label(new Rect(x + 12f, y, 350f, 20f), $"Route phase: {_arenaRoute.Phase}"); y += 22f;
                bool previousEnabled = GUI.enabled;
                bool secondaryWavesRunning = _multiArenaWaves != null && _multiArenaWaves.IsSequenceRunning;
                GUI.enabled = _arenaRoute.Phase == ArenaRouteController.RoutePhase.WaitingForRWaves && secondaryWavesRunning == false;
                if (GUI.Button(new Rect(x + 12f, y, 170f, 24f), "Начать захват Р")) _arenaRoute.BeginCaptureObjective();
                GUI.enabled = _arenaRoute.Phase == ArenaRouteController.RoutePhase.WaitingForOWaves && secondaryWavesRunning == false;
                if (GUI.Button(new Rect(x + 195f, y, 177f, 24f), "Открыть центр О")) _arenaRoute.OpenBossArena();
                GUI.enabled = previousEnabled;
                y += 34f;
            }

            GUI.Label(new Rect(x + 12f, y, 350f, 20f), "Final boss acceptance"); y += 22f;
            if (_finalBoss != null)
            {
                string bossHealth = _finalBoss.State == FinalBossRuntimeController.EncounterState.Dormant
                    ? "—"
                    : _finalBoss.CurrentHealth.ToString();
                GUI.Label(new Rect(x + 12f, y, 350f, 20f), $"Boss: {_finalBoss.State} · HP {bossHealth}"); y += 22f;
                bool previousEnabled = GUI.enabled;
                GUI.enabled = _finalBoss.IsActive;
                if (GUI.Button(new Rect(x + 12f, y, 170f, 24f), "−4000 HP босса")) _finalBoss.DamageBossForDebug(4000);
                if (GUI.Button(new Rect(x + 195f, y, 177f, 24f), "Следующий этап босса")) _finalBoss.AdvanceForDebug();
                GUI.enabled = previousEnabled;
            }

            SetTuning(tuning);
            _entityManager.SetComponentData(_player, progression);
            _entityManager.SetComponentData(_player, health);
        }
        finally
        {
            GUI.EndScrollView();
        }

        if (_showSpecialCards)
        {
            DrawSpecialCardsPanel(progression);
        }
    }

    private void DrawRuntimeLogPanel()
    {
        if (_observedRuntimeLogVersion != DevelopmentLogBuffer.Version)
        {
            _observedRuntimeLogVersion = DevelopmentLogBuffer.Version;
            _scrollRuntimeLogToBottom = true;
        }

        Rect panel = new(16f, 62f, Screen.width - 32f, Screen.height - 78f);
        DrawOpaquePanel(panel, "DEVELOPMENT LOG");

        float buttonY = panel.y + 26f;
        if (GUI.Button(new Rect(panel.x + 12f, buttonY, 150f, 26f), "КОПИРОВАТЬ ВСЁ"))
        {
            GUIUtility.systemCopyBuffer = DevelopmentLogBuffer.BuildText();
        }

        if (GUI.Button(new Rect(panel.x + 170f, buttonY, 120f, 26f), "ОЧИСТИТЬ"))
        {
            DevelopmentLogBuffer.Clear();
            _runtimeLogScroll = Vector2.zero;
        }

        string controlErrorLabel = _controlErrorArmed ? "ПОДТВЕРДИТЬ ERROR" : "КОНТРОЛЬНАЯ ОШИБКА";
        if (GUI.Button(new Rect(panel.x + 298f, buttonY, 190f, 26f), controlErrorLabel))
        {
            if (_controlErrorArmed)
            {
                DevelopmentLogBuffer.EmitControlledError();
                _controlErrorArmed = false;
            }
            else
            {
                _controlErrorArmed = true;
            }
        }

        if (GUI.Button(new Rect(panel.xMax - 132f, buttonY, 120f, 26f), "ЗАКРЫТЬ"))
        {
            _controlErrorArmed = false;
            _showRuntimeLog = false;
            return;
        }

        string logText = DevelopmentLogBuffer.BuildText();
        if (string.IsNullOrEmpty(logText))
        {
            logText = "Журнал пуст.";
        }

        _runtimeLogStyle ??= new GUIStyle(GUI.skin.textArea)
        {
            fontSize = 12,
            wordWrap = false
        };
        RuntimeGuiPresentation.ApplyFont(_runtimeLogStyle);

        Rect viewport = new(panel.x + 12f, buttonY + 34f, panel.width - 24f, panel.height - 72f);
        float contentWidth = Mathf.Max(viewport.width - 20f, 1400f);
        int lineCount = 1;
        foreach (char character in logText)
        {
            if (character == '\n') lineCount++;
        }
        float contentHeight = Mathf.Max(viewport.height - 20f, lineCount * 18f + 12f);
        Rect content = new(0f, 0f, contentWidth, contentHeight);

        _runtimeLogScroll = GUI.BeginScrollView(viewport, _runtimeLogScroll, content);
        GUI.TextArea(new Rect(0f, 0f, contentWidth, contentHeight), logText, _runtimeLogStyle);
        GUI.EndScrollView();

        if (_scrollRuntimeLogToBottom)
        {
            _runtimeLogScroll.y = contentHeight;
            _scrollRuntimeLogToBottom = false;
        }
    }

    private void DrawSpecialCardsPanel(PlayerProgressionState progression)
    {
        Rect panel = new(418f, 70f, 370f, 330f);
        DrawOpaquePanel(panel, "SPECIAL-КАРТЫ · прямое включение");
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

    private static void DrawOpaquePanel(Rect panel, string title)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0.035f, 0.045f, 0.055f, 0.96f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = previousColor;
        GUI.Box(panel, title);
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

internal static class DevelopmentLogBuffer
{
    internal const string ControlledErrorMarker = "[Logo Survivor][DevelopmentLog] CONTROL_THREADED_CAPTURE";
    private const int MaxEntries = 64;
    private static readonly List<Entry> Entries = new();
    private static readonly object SyncRoot = new();
    private static readonly System.Diagnostics.Stopwatch RuntimeClock = System.Diagnostics.Stopwatch.StartNew();
    private static int _version;

    public static int Version => System.Threading.Volatile.Read(ref _version);

    public static int ErrorCount
    {
        get
        {
            lock (SyncRoot)
            {
                int count = 0;
                foreach (Entry entry in Entries)
                {
                    if (entry.Type is LogType.Error or LogType.Assert or LogType.Exception)
                    {
                        count += entry.RepeatCount;
                    }
                }
                return count;
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        lock (SyncRoot)
        {
            Entries.Clear();
            RuntimeClock.Restart();
            _version = 0;
        }

        Application.logMessageReceived -= HandleLog;
        Application.logMessageReceivedThreaded -= HandleLog;
        if (Debug.isDebugBuild)
        {
            Application.logMessageReceivedThreaded += HandleLog;
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            Entries.Clear();
            _version++;
        }
    }

    public static void EmitControlledError()
    {
        if (Debug.isDebugBuild)
        {
            Debug.LogError(ControlledErrorMarker);
        }
    }

    public static string BuildText()
    {
        lock (SyncRoot)
        {
            StringBuilder builder = new();
            foreach (Entry entry in Entries)
            {
                builder.Append('[')
                    .Append(entry.StartedAt.ToString("F2"))
                    .Append("] [")
                    .Append(entry.Type)
                    .Append(']');
                if (entry.RepeatCount > 1)
                {
                    builder.Append(" x").Append(entry.RepeatCount);
                }
                builder.AppendLine().AppendLine(entry.Text).AppendLine();
            }
            return builder.ToString();
        }
    }

    private static void HandleLog(string condition, string stackTrace, LogType type)
    {
        string text = string.IsNullOrWhiteSpace(stackTrace)
            ? condition
            : $"{condition}\n{stackTrace}";

        lock (SyncRoot)
        {
            if (Entries.Count > 0)
            {
                Entry lastEntry = Entries[Entries.Count - 1];
                if (lastEntry.Type == type && lastEntry.Text == text)
                {
                    lastEntry.RepeatCount++;
                    _version++;
                    return;
                }
            }

            Entries.Add(new Entry
            {
                Type = type,
                Text = text,
                StartedAt = (float)RuntimeClock.Elapsed.TotalSeconds,
                RepeatCount = 1
            });

            if (Entries.Count > MaxEntries)
            {
                Entries.RemoveAt(0);
            }
            _version++;
        }
    }

    private sealed class Entry
    {
        public LogType Type;
        public string Text;
        public float StartedAt;
        public int RepeatCount;
    }
}
