using System;
using System.Collections.Generic;
using Assets.Scripts.Ecs;
using LogoSurvivor.AchievementCards;
using LogoSurvivor.ClassLoadout;
using LogoSurvivor.QrContent;
using LogoSurvivor.SessionResults;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class ContentFinaleRuntimeController : MonoBehaviour
{
    private enum AchievementOverlay
    {
        None,
        Intro,
        Card
    }

    private const string LogPrefix = "[Logo Survivor][ContentFinale]";
    private const string MasterVolumeKey = "session_shell.master_volume";
    private const string FullscreenKey = "session_shell.fullscreen";
    private const string AchievementPhotoLibraryResourcePath = "AchievementCardPhotoLibrary";

    private static bool s_autoStartAfterReload;

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private WaveRuntimeController _firstArenaWaves;
    private MultiArenaWaveController _multiArenaWaves;
    private ArenaRouteController _arenaRoute;
    private CameraFollow _cameraFollow;
    private PlayerProgressionController _progression;
    private CombatRuntimeController _combat;
    private FinalBossRuntimeController _boss;
    private DebugAdminPanel _debugAdmin;
    private AchievementCardsSession _achievementCards;
    private AchievementCardPhotoLibrary _achievementPhotos;
    private ClassLoadoutSession _classLoadout;
    private QrContentSnapshot _qrContent;
    private readonly SessionResultsCoordinator _results = new();
    private readonly SessionShellStateMachine _shell = new();
    private AchievementOverlay _achievementOverlay;
    private AchievementCardDefinition _presentedCard;
    private AchievementCardDefinition _expandedCard;
    private float _overlayElapsedSeconds;
    private Vector2 _resultScroll;
    private bool _initialized;
    private bool _outcomePending;
    private ArenaCameraOverviewShotId? _pendingOverview;
    private bool _overviewActive;
    private float _masterVolume;
    private bool _fullscreenPreference;

    public void Initialize(
        World world,
        Entity playerEntity,
        WaveRuntimeController firstArenaWaves,
        MultiArenaWaveController multiArenaWaves,
        ArenaRouteController arenaRoute,
        CameraFollow cameraFollow,
        PlayerProgressionController progression,
        CombatRuntimeController combat,
        FinalBossRuntimeController boss,
        DebugAdminPanel debugAdmin)
    {
        _world = world;
        _entityManager = world.EntityManager;
        _playerEntity = playerEntity;
        _firstArenaWaves = firstArenaWaves;
        _multiArenaWaves = multiArenaWaves;
        _arenaRoute = arenaRoute;
        _cameraFollow = cameraFollow;
        _progression = progression;
        _combat = combat;
        _boss = boss;
        _debugAdmin = debugAdmin;

        _achievementCards = new AchievementCardsSession(
            AchievementCardsConfig.CreateDefault(),
            AchievementCardsCatalog.CreateDefault());
        _classLoadout = new ClassLoadoutSession(ClassLoadoutCatalog.CreateDefault());
        _combat.BindClassLoadout(_classLoadout);
        _achievementPhotos = Resources.Load<AchievementCardPhotoLibrary>(AchievementPhotoLibraryResourcePath);
        if (_achievementPhotos == null)
        {
            Debug.LogWarning($"{LogPrefix} Achievement photos are unavailable; safe placeholders remain active.");
        }
        else if (_achievementPhotos.Validate(out string photoLibraryError) == false)
        {
            Debug.LogError($"{LogPrefix} Invalid achievement photo library: {photoLibraryError} Safe placeholders remain active.");
            _achievementPhotos = null;
        }
        _qrContent = QrContentCatalog.CreateCanonicalFallback().CreateSnapshot();

        _firstArenaWaves.WaveCompleted += HandleFirstArenaWaveCompleted;
        _multiArenaWaves.WaveCompleted += HandleArenaWaveCompleted;
        _arenaRoute.ArenaEntered += HandleArenaEnteredForLoadout;
        _arenaRoute.BossArenaOpened += HandleBossSpawnedForLoadout;
        _arenaRoute.OverviewRequested += HandleOverviewRequested;
        _progression.ChoiceClosed += HandleProgressionChoiceClosed;
        _combat.DefeatPublished += HandleDefeatPublished;
        _boss.VictoryPublished += HandleVictoryPublished;
        _boss.VictoryCleanupCompleted += HandleVictoryCleanupCompleted;
        _boss.DefeatCleanupCompleted += HandleDefeatCleanupCompleted;

        LoadSettings();
        _progression.ExternalModalActive = true;
        SetGameplayPresentation(false);
        PauseGameplay();
        _initialized = true;

        if (s_autoStartAfterReload)
        {
            s_autoStartAfterReload = false;
            BeginNewSession();
        }
    }

    private void Update()
    {
        if (_initialized == false)
        {
            return;
        }

        bool gameplayActive = _shell.Current == SessionShellState.Gameplay
            && _achievementOverlay == AchievementOverlay.None
            && _progression.ChoiceOpen == false
            && _outcomePending == false
            && Time.timeScale > 0f;
        _results.AddActiveTime(Time.unscaledDeltaTime, gameplayActive, Application.isFocused);

        if (Application.isFocused
            && _shell.IsPauseLayerActive == false
            && _achievementOverlay != AchievementOverlay.None)
        {
            _overlayElapsedSeconds += Time.unscaledDeltaTime;
        }

        if (_achievementOverlay == AchievementOverlay.Intro
            && _overlayElapsedSeconds >= _achievementCards.Config.IntroAutoCloseSeconds)
        {
            CloseIntro();
            return;
        }

        HandleKeyboardInput();
    }

    private void HandleKeyboardInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        bool confirmPressed = keyboard.enterKey.wasPressedThisFrame
            || keyboard.numpadEnterKey.wasPressedThisFrame
            || keyboard.spaceKey.wasPressedThisFrame;
        bool pausePressed = keyboard.escapeKey.wasPressedThisFrame
            || keyboard.f10Key.wasPressedThisFrame;

        if (_shell.IsPauseLayerActive)
        {
            if (_shell.Current == SessionShellState.Pause
                && keyboard.shiftKey.isPressed
                && keyboard.numpad0Key.wasPressedThisFrame)
            {
                _debugAdmin?.TryUnlockDevelopmentTools();
            }

            if (pausePressed == false)
            {
                return;
            }

            if (_shell.Current == SessionShellState.Pause)
            {
                ContinueFromPause();
            }
            else
            {
                CancelShellOverlay();
            }
            return;
        }

        if (pausePressed && _shell.Current is SessionShellState.Gameplay or SessionShellState.Result)
        {
            OpenPause();
            return;
        }

        if (_shell.Current == SessionShellState.Gameplay
            && _classLoadout.HasMandatoryChoice)
        {
            int optionIndex = GetLoadoutChoiceHotkey(keyboard);
            if (optionIndex > 0)
            {
                if (_classLoadout.ClassChoicePending)
                {
                    TrySelectClass((PlayerClassId)optionIndex);
                }
                else
                {
                    TrySelectLoadoutWeapon(optionIndex);
                }
            }
            return;
        }

        if (_achievementOverlay == AchievementOverlay.Intro)
        {
            if (confirmPressed)
            {
                CloseIntro();
            }
            return;
        }

        if (_achievementOverlay == AchievementOverlay.Card)
        {
            if (confirmPressed
                && _overlayElapsedSeconds >= _achievementCards.Config.CardContinueDelaySeconds)
            {
                ClosePresentedCard();
            }
            return;
        }

        if (pausePressed == false)
        {
            return;
        }

        switch (_shell.Current)
        {
            case SessionShellState.Settings:
            case SessionShellState.Credits:
            case SessionShellState.DevelopmentHub:
            case SessionShellState.DevelopmentParameters:
            case SessionShellState.DevelopmentSpecialCards:
            case SessionShellState.DevelopmentLog:
            case SessionShellState.ExpandedCard:
            case SessionShellState.ConfirmRestart:
            case SessionShellState.ConfirmExit:
                CancelShellOverlay();
                break;
        }
    }

    private void HandleFirstArenaWaveCompleted(int waveNumber)
    {
        HandleWaveCompleted(waveNumber == 1
            ? AchievementWaveSlot.ArenaPWave1
            : AchievementWaveSlot.ArenaPWave2);
    }

    private void HandleArenaWaveCompleted(ArenaId arena, int waveNumber)
    {
        AchievementWaveSlot? slot = (arena, waveNumber) switch
        {
            (ArenaId.R, 1) => AchievementWaveSlot.ArenaRWave1,
            (ArenaId.R, 2) => AchievementWaveSlot.ArenaRWave2,
            (ArenaId.O, 1) => AchievementWaveSlot.ArenaOWave1,
            (ArenaId.O, 2) => AchievementWaveSlot.ArenaOWave2,
            _ => null
        };

        if (slot.HasValue)
        {
            HandleWaveCompleted(slot.Value);
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} Unknown arena wave completion ignored: arena={arena}, wave={waveNumber}.");
        }
    }

    private void HandleWaveCompleted(AchievementWaveSlot slot)
    {
        _results.RegisterCompletedWave((int)slot);
        AchievementAwardResult award = _achievementCards.RegisterWaveCompleted(slot);
        Debug.Log($"{LogPrefix} {award.DiagnosticMessage}");

        if (award.Status == AchievementAwardStatus.Awarded
            && _progression.HasPendingChoice == false)
        {
            TryPresentNextAchievementCard();
        }
    }

    private void HandleArenaEnteredForLoadout(ArenaId arena)
    {
        LoadoutWeaponSlot? slot = arena switch
        {
            ArenaId.R => LoadoutWeaponSlot.Slot2,
            ArenaId.O => LoadoutWeaponSlot.Slot3,
            _ => null
        };

        if (slot.HasValue)
        {
            UnlockLoadoutSlot(slot.Value);
        }
    }

    private void HandleBossSpawnedForLoadout()
    {
        UnlockLoadoutSlot(LoadoutWeaponSlot.Slot4);
    }

    private void UnlockLoadoutSlot(LoadoutWeaponSlot slot)
    {
        ClassLoadoutOperationResult result = _classLoadout.TryUnlockSlot(slot);
        Debug.Log($"{LogPrefix} {result.DiagnosticMessage}");
        if (result.ChangedState || _classLoadout.HasMandatoryChoice)
        {
            _progression.ExternalModalActive = true;
            SetGameplayPresentation(false);
            PauseGameplay();
        }
    }

    private void HandleProgressionChoiceClosed()
    {
        if (_outcomePending == false)
        {
            TryPresentNextAchievementCard();
        }
    }

    private void HandleDefeatPublished()
    {
        if (_outcomePending || _results.HasSnapshot)
        {
            return;
        }

        CancelOverviewForResult();
        _outcomePending = true;
        _classLoadout.TryFinish();
        _achievementCards.TryFinish(AchievementSessionState.Defeat);
        _combat.HideDefeatPanel();
        _progression.ExternalModalActive = true;
        SetGameplayPresentation(false);

        if (_boss.StopForPlayerDefeat() == false)
        {
            FinalizeOutcome(SessionOutcome.Defeat);
        }
    }

    private void HandleVictoryPublished()
    {
        if (_outcomePending || _results.HasSnapshot)
        {
            return;
        }

        CancelOverviewForResult();
        _outcomePending = true;
        _classLoadout.TryFinish();
        _achievementCards.TryFinish(AchievementSessionState.Victory);
        _progression.ExternalModalActive = true;
        SetGameplayPresentation(false);
    }

    private void HandleVictoryCleanupCompleted()
    {
        FinalizeOutcome(SessionOutcome.Victory);
    }

    private void HandleDefeatCleanupCompleted()
    {
        FinalizeOutcome(SessionOutcome.Defeat);
    }

    private void FinalizeOutcome(SessionOutcome outcome)
    {
        if (_results.HasSnapshot
            || _world == null
            || _world.IsCreated == false
            || _entityManager.Exists(_playerEntity) == false)
        {
            return;
        }

        SessionCombatStats combatStats = _entityManager.GetComponentData<SessionCombatStats>(_playerEntity);
        bool created = _results.TryCreateSnapshot(
            outcome,
            combatStats.ConfirmedKills,
            combatStats.ActualDamage,
            _progression.CurrentLevel,
            _progression.CreateUpgradeSnapshot(),
            _achievementCards.CreateSnapshot(),
            _qrContent,
            out SessionResultSnapshot snapshot);

        if (created == false)
        {
            Debug.LogWarning($"{LogPrefix} Duplicate outcome ignored: {outcome}.");
            return;
        }

        _achievementOverlay = AchievementOverlay.None;
        _presentedCard = null;
        _expandedCard = null;
        _resultScroll = Vector2.zero;
        _progression.ExternalModalActive = true;
        _shell.ShowResult();
        SetGameplayPresentation(false);
        PauseGameplay();
        Debug.Log($"{LogPrefix} Immutable {snapshot.Outcome} snapshot created: waves={snapshot.CompletedWaveCount}/6, cards={snapshot.AchievementCards.ObtainedCount}/6, kills={snapshot.ConfirmedKills}, damage={snapshot.ActualDamage}.");
    }

    private void BeginNewSession()
    {
        if (_shell.StartGame() == false)
        {
            return;
        }

        _achievementCards.Reset();
        _classLoadout.Reset();
        _results.Reset();
        _outcomePending = false;
        _pendingOverview = null;
        _overviewActive = false;
        _achievementOverlay = AchievementOverlay.None;
        _presentedCard = null;
        _expandedCard = null;
        _cameraFollow?.SetPauseOverlayActive(false);
        _resultScroll = Vector2.zero;
        if (Screen.fullScreen != _fullscreenPreference)
        {
            Screen.fullScreen = _fullscreenPreference;
        }
        _progression.ExternalModalActive = true;
        SetGameplayPresentation(false);
        PauseGameplay();

        ResumeGameplayAfterModalQueue();
    }

    private void CloseIntro()
    {
        if (_achievementOverlay != AchievementOverlay.Intro)
        {
            return;
        }

        _achievementCards.DismissIntro();
        _achievementOverlay = AchievementOverlay.None;
        RegisterPendingOverview(ArenaCameraOverviewShotId.IntroOverview);
        ResumeGameplayAfterModalQueue();
    }

    private void TryPresentNextAchievementCard()
    {
        if (_shell.Current != SessionShellState.Gameplay
            || _outcomePending
            || _classLoadout.HasMandatoryChoice)
        {
            return;
        }

        if (_achievementCards.TryTakeNextForPresentation(
            _progression.HasPendingChoice,
            out AchievementCardDefinition card))
        {
            _presentedCard = card;
            _achievementOverlay = AchievementOverlay.Card;
            _overlayElapsedSeconds = 0f;
            _progression.ExternalModalActive = true;
            SetGameplayPresentation(false);
            PauseGameplay();
            return;
        }

        if (_progression.HasPendingChoice == false)
        {
            ResumeGameplayAfterModalQueue();
        }
    }

    private void ClosePresentedCard()
    {
        if (_achievementOverlay != AchievementOverlay.Card)
        {
            return;
        }

        _achievementOverlay = AchievementOverlay.None;
        _presentedCard = null;
        TryPresentNextAchievementCard();
    }

    private void ResumeGameplayAfterModalQueue()
    {
        if (_shell.Current != SessionShellState.Gameplay
            || _outcomePending
            || _overviewActive)
        {
            return;
        }

        if (_classLoadout.HasMandatoryChoice)
        {
            _progression.ExternalModalActive = true;
            SetGameplayPresentation(false);
            PauseGameplay();
            return;
        }

        if (_achievementCards.IntroPending
            && _achievementOverlay == AchievementOverlay.None)
        {
            _achievementOverlay = AchievementOverlay.Intro;
            _overlayElapsedSeconds = 0f;
            _progression.ExternalModalActive = true;
            SetGameplayPresentation(false);
            PauseGameplay();
            return;
        }

        if (_achievementOverlay != AchievementOverlay.None)
        {
            return;
        }

        if (_progression.ChoiceOpen)
        {
            _progression.ExternalModalActive = false;
            SetGameplayPresentation(true);
            PauseGameplay();
            return;
        }

        if (_progression.HasPendingChoice)
        {
            _progression.ExternalModalActive = false;
            SetGameplayPresentation(true);
            Time.timeScale = 1f;
            return;
        }

        if (TryStartPendingOverview())
        {
            return;
        }

        _progression.ExternalModalActive = false;
        SetGameplayPresentation(true);
        Time.timeScale = 1f;
    }

    private void HandleOverviewRequested(ArenaCameraOverviewShotId shotId)
    {
        RegisterPendingOverview(shotId);
        ResumeGameplayAfterModalQueue();
    }

    private void RegisterPendingOverview(ArenaCameraOverviewShotId shotId)
    {
        if (_outcomePending || _results.HasSnapshot)
        {
            return;
        }

        if (_overviewActive || _pendingOverview.HasValue)
        {
            Debug.LogWarning($"{LogPrefix} Duplicate camera overview request ignored: {shotId}.");
            return;
        }

        _pendingOverview = shotId;
        Debug.Log($"{LogPrefix} Camera overview queued: {shotId}.");
    }

    private bool TryStartPendingOverview()
    {
        if (_pendingOverview.HasValue == false
            || _overviewActive
            || _achievementOverlay != AchievementOverlay.None
            || _progression.ChoiceOpen
            || _outcomePending
            || _shell.Current != SessionShellState.Gameplay)
        {
            return false;
        }

        ArenaCameraOverviewShotId shotId = _pendingOverview.Value;
        _pendingOverview = null;
        _overviewActive = true;
        _progression.ExternalModalActive = true;
        SetGameplayPresentation(false);
        PauseGameplay();

        if (_cameraFollow != null
            && _cameraFollow.TryPlayOverview(shotId, HandleOverviewCompleted))
        {
            return true;
        }

        _overviewActive = false;
        if (_cameraFollow == null)
        {
            Debug.LogError($"{LogPrefix} Camera overview skipped: CameraFollow is unavailable.");
        }
        Debug.LogWarning($"{LogPrefix} Camera overview safely skipped: {shotId}.");
        return false;
    }

    private void HandleOverviewCompleted()
    {
        if (_overviewActive == false)
        {
            return;
        }

        _overviewActive = false;
        ResumeGameplayAfterModalQueue();
    }

    private void CancelOverviewForResult()
    {
        _pendingOverview = null;
        if (_overviewActive)
        {
            _cameraFollow.CancelOverviewForResult();
            _overviewActive = false;
        }
    }

    private void OpenPause()
    {
        if (_shell.Pause() == false)
        {
            return;
        }

        _cameraFollow?.SetPauseOverlayActive(true);
        _progression.ExternalModalActive = true;
        SetGameplayPresentation(false);
        PauseGameplay();
    }

    private void ContinueFromPause()
    {
        if (_shell.Continue() == false)
        {
            return;
        }

        _cameraFollow?.SetPauseOverlayActive(false);
        RestorePauseCaller();
    }

    private void RestorePauseCaller()
    {
        if (_shell.Current == SessionShellState.Result)
        {
            _progression.ExternalModalActive = true;
            SetGameplayPresentation(false);
            PauseGameplay();
            return;
        }

        if (_classLoadout.HasMandatoryChoice)
        {
            _progression.ExternalModalActive = true;
            SetGameplayPresentation(false);
            PauseGameplay();
            return;
        }

        if (_achievementOverlay != AchievementOverlay.None || _overviewActive)
        {
            _progression.ExternalModalActive = true;
            SetGameplayPresentation(false);
            PauseGameplay();
            return;
        }

        if (_progression.ChoiceOpen)
        {
            _progression.ExternalModalActive = false;
            SetGameplayPresentation(true);
            PauseGameplay();
            return;
        }

        TryPresentNextAchievementCard();
    }

    private void CancelShellOverlay()
    {
        if (_shell.CancelOverlay() == false)
        {
            return;
        }

        _expandedCard = null;
        if (_shell.Current == SessionShellState.Gameplay)
        {
            ResumeGameplayAfterModalQueue();
        }
        else
        {
            _progression.ExternalModalActive = true;
            SetGameplayPresentation(false);
            PauseGameplay();
        }
    }

    private void LoadSettings()
    {
        _masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
        _fullscreenPreference = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
        AudioListener.volume = _masterVolume;
    }

    private void SaveSettings()
    {
        PlayerPrefs.SetFloat(MasterVolumeKey, _masterVolume);
        PlayerPrefs.SetInt(FullscreenKey, _fullscreenPreference ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void RestartSession(bool autoStart)
    {
        _debugAdmin?.ResetAllDebugValues();
        s_autoStartAfterReload = autoStart;
        Time.timeScale = 1f;
        SceneManager.LoadScene("Game");
    }

    private void ConfirmExit()
    {
        _classLoadout.Reset();
        _achievementCards.Reset();
        _results.Reset();
        Time.timeScale = 1f;

#if UNITY_WEBGL && !UNITY_EDITOR
        Screen.fullScreen = false;
        RestartSession(autoStart: false);
#else
        Application.Quit();
#endif
    }

    private void SetGameplayPresentation(bool visible)
    {
        _combat.SetPresentationVisible(visible);
        _progression.PresentationVisible = visible;
    }

    private static void PauseGameplay()
    {
        Time.timeScale = 0f;
    }

    private void OnGUI()
    {
        if (_initialized == false)
        {
            return;
        }

        RuntimeGuiPresentation.ApplyFontToCurrentSkin();
        GUI.depth = -1000;

        if (_shell.IsPauseLayerActive == false
            && _shell.Current == SessionShellState.Gameplay
            && _classLoadout.HasMandatoryChoice)
        {
            DrawClassLoadoutChoice();
            return;
        }

        if (_shell.IsPauseLayerActive == false
            && _achievementOverlay == AchievementOverlay.Intro)
        {
            DrawAchievementIntro();
            return;
        }

        if (_shell.IsPauseLayerActive == false
            && _achievementOverlay == AchievementOverlay.Card)
        {
            DrawAchievementCard(_presentedCard, allowContinue: true);
            return;
        }

        switch (_shell.Current)
        {
            case SessionShellState.Start:
                DrawStartScreen();
                break;
            case SessionShellState.Pause:
                DrawPauseMenu();
                break;
            case SessionShellState.Settings:
                DrawSettings();
                break;
            case SessionShellState.Credits:
                DrawCredits();
                break;
            case SessionShellState.DevelopmentHub:
                DrawDevelopmentHub();
                break;
            case SessionShellState.DevelopmentParameters:
                DrawDevelopmentParameters();
                break;
            case SessionShellState.DevelopmentSpecialCards:
                DrawDevelopmentSpecialCards();
                break;
            case SessionShellState.DevelopmentLog:
                DrawDevelopmentLog();
                break;
            case SessionShellState.Result:
                DrawResult();
                break;
            case SessionShellState.ExpandedCard:
                DrawAchievementCard(_expandedCard, allowContinue: false);
                break;
            case SessionShellState.ConfirmRestart:
                DrawConfirmation("НАЧАТЬ ЗАНОВО?", "Текущий прогресс будет потерян", confirmRestart: true);
                break;
            case SessionShellState.ConfirmExit:
                DrawConfirmation(
#if UNITY_WEBGL && !UNITY_EDITOR
                    "ВЫЙТИ ИЗ ПОЛНОЭКРАННОГО РЕЖИМА?",
#else
                    "ВЫЙТИ ИЗ ИГРЫ?",
#endif
                    "Текущая сессия завершится",
                    confirmRestart: false);
                break;
        }
    }

    private static void DrawBackdrop()
    {
        Color previous = GUI.color;
        GUI.color = new Color(0.015f, 0.025f, 0.045f, 0.98f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void DrawStartScreen()
    {
        DrawBackdrop();
        Rect panel = CenteredPanel(440f, 420f);
        GUI.Box(panel, "LOGO SURVIVOR");
        if (GUI.Button(new Rect(panel.x + 70f, panel.y + 90f, 300f, 64f), "ИГРАТЬ"))
        {
            BeginNewSession();
        }
        if (GUI.Button(new Rect(panel.x + 70f, panel.y + 174f, 300f, 54f), "НАСТРОЙКИ") && _shell.OpenSettings())
        {
            PauseGameplay();
        }
        if (GUI.Button(new Rect(panel.x + 70f, panel.y + 248f, 300f, 54f), "ТИТРЫ") && _shell.OpenCredits())
        {
            PauseGameplay();
        }
        GUI.Label(new Rect(panel.x + 40f, panel.y + 330f, 360f, 48f), "Выставочный WebGL MVP · рабочее название");
    }

    private void DrawAchievementIntro()
    {
        DrawBackdrop();
        Rect panel = CenteredPanel(Mathf.Min(760f, Screen.width - 40f), 360f);
        GUI.Box(panel, "КАРТОЧКИ ГЕЙМДЕВА");
        GUI.Label(
            new Rect(panel.x + 50f, panel.y + 80f, panel.width - 100f, 150f),
            "Познакомься с тремя направлениями клуба и тремя этапами создания игры.\n\nВсе направления работают вместе на каждом этапе.");
        if (GUI.Button(new Rect(panel.x + panel.width * 0.5f - 130f, panel.y + 260f, 260f, 56f), "НАЧАТЬ ИГРУ"))
        {
            CloseIntro();
        }
    }

    private void DrawClassLoadoutChoice()
    {
        if (_classLoadout.ClassChoicePending)
        {
            DrawClassChoice();
            return;
        }

        if (_classLoadout.PendingSlot.HasValue)
        {
            DrawWeaponChoice(_classLoadout.PendingSlot.Value, _classLoadout.PendingChoices);
        }
    }

    private void DrawClassChoice()
    {
        DrawBackdrop();
        float panelWidth = Mathf.Min(900f, Screen.width - 30f);
        Rect panel = CenteredPanel(panelWidth, Mathf.Min(430f, Screen.height - 30f));
        GUI.Box(panel, "ВЫБЕРИТЕ ПРОФЕССИЮ");
        GUI.Label(
            new Rect(panel.x + 40f, panel.y + 52f, panel.width - 80f, 55f),
            "Класс определяет четыре тематических набора оружейных карточек на эту сессию.");

        IReadOnlyList<PlayerClassDefinition> classes = _classLoadout.Catalog.Classes;
        bool useCompactLayout = panel.width < 620f || panel.height < 390f;
        float gap = useCompactLayout ? 8f : 12f;
        float buttonWidth = useCompactLayout
            ? panel.width - 60f
            : (panel.width - 80f - gap * 2f) / 3f;
        float buttonHeight = useCompactLayout
            ? Mathf.Max(44f, (panel.height - 180f - gap * 2f) / 3f)
            : Mathf.Min(190f, panel.yMax - 75f - (panel.y + 125f));
        for (int index = 0; index < classes.Count; index++)
        {
            PlayerClassDefinition definition = classes[index];
            Rect button = new(
                useCompactLayout ? panel.x + 30f : panel.x + 40f + index * (buttonWidth + gap),
                useCompactLayout ? panel.y + 112f + index * (buttonHeight + gap) : panel.y + 125f,
                buttonWidth,
                buttonHeight);
            Color previousColor = GUI.color;
            GUI.color = GetClassColor(definition.Id);
            string label = useCompactLayout
                ? $"{index + 1} · {definition.DisplayName} · 4 слота"
                : $"{index + 1}\n\n{definition.DisplayName}\n\n4 оружейных слота";
            if (GUI.Button(button, label))
            {
                TrySelectClass(definition.Id);
            }
            GUI.color = previousColor;
        }

        GUI.Label(
            new Rect(panel.x + 40f, panel.yMax - 52f, panel.width - 80f, 30f),
            "Выбор действует до конца сессии · клавиши 1–3");
    }

    private void DrawWeaponChoice(
        LoadoutWeaponSlot slot,
        IReadOnlyList<LoadoutWeaponDefinition> choices)
    {
        DrawBackdrop();
        float panelWidth = Mathf.Min(900f, Screen.width - 30f);
        Rect panel = CenteredPanel(panelWidth, Mathf.Min(430f, Screen.height - 30f));
        GUI.Box(panel, $"{_classLoadout.SelectedClass.DisplayName.ToUpperInvariant()} · СЛОТ {(int)slot}");
        GUI.Label(
            new Rect(panel.x + 40f, panel.y + 52f, panel.width - 80f, 55f),
            $"{GetMilestoneLabel(slot)}. Выберите одну из трёх фиксированных карточек.");

        bool useCompactLayout = panel.width < 620f || panel.height < 390f;
        float gap = useCompactLayout ? 8f : 12f;
        float buttonWidth = useCompactLayout
            ? panel.width - 60f
            : (panel.width - 80f - gap * 2f) / 3f;
        float buttonHeight = useCompactLayout
            ? Mathf.Max(44f, (panel.height - 180f - gap * 2f) / 3f)
            : Mathf.Min(190f, panel.yMax - 75f - (panel.y + 125f));
        for (int index = 0; index < choices.Count; index++)
        {
            LoadoutWeaponDefinition choice = choices[index];
            Rect button = new(
                useCompactLayout ? panel.x + 30f : panel.x + 40f + index * (buttonWidth + gap),
                useCompactLayout ? panel.y + 112f + index * (buttonHeight + gap) : panel.y + 125f,
                buttonWidth,
                buttonHeight);
            Color previousColor = GUI.color;
            GUI.color = GetClassColor(choice.PlayerClass);
            string label = useCompactLayout
                ? $"{index + 1} · {choice.DisplayName} · УРОВЕНЬ 0 / 9"
                : $"{index + 1}\n\n{choice.DisplayName}\n\nУРОВЕНЬ 0 / 9";
            if (GUI.Button(button, label))
            {
                TrySelectLoadoutWeapon(index + 1);
            }
            GUI.color = previousColor;
        }

        GUI.Label(
            new Rect(panel.x + 40f, panel.yMax - 52f, panel.width - 80f, 30f),
            "Выбор нельзя заменить до конца сессии · клавиши 1–3");
    }

    private void TrySelectClass(PlayerClassId playerClass)
    {
        ClassLoadoutOperationResult result = _classLoadout.TrySelectClass(playerClass);
        Debug.Log($"{LogPrefix} {result.DiagnosticMessage}");
        if (result.ChangedState)
        {
            ResumeGameplayAfterModalQueue();
        }
    }

    private void TrySelectLoadoutWeapon(int optionIndex)
    {
        ClassLoadoutOperationResult result = _classLoadout.TrySelectPendingWeapon(optionIndex);
        Debug.Log($"{LogPrefix} {result.DiagnosticMessage}");
        if (result.ChangedState)
        {
            ResumeGameplayAfterModalQueue();
        }
    }

    private static int GetLoadoutChoiceHotkey(Keyboard keyboard)
    {
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) return 1;
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) return 2;
        if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) return 3;
        return 0;
    }

    private static Color GetClassColor(PlayerClassId playerClass)
    {
        return playerClass switch
        {
            PlayerClassId.GameDesigner => new Color(0.35f, 0.78f, 0.42f, 1f),
            PlayerClassId.Artist => new Color(0.78f, 0.42f, 0.92f, 1f),
            PlayerClassId.Programmer => new Color(0.25f, 0.72f, 0.92f, 1f),
            _ => Color.white
        };
    }

    private static string GetMilestoneLabel(LoadoutWeaponSlot slot)
    {
        return slot switch
        {
            LoadoutWeaponSlot.Slot1 => "СТАРТ СЕССИИ",
            LoadoutWeaponSlot.Slot2 => "АРЕНА Р ОТКРЫТА",
            LoadoutWeaponSlot.Slot3 => "АРЕНА О ОТКРЫТА",
            LoadoutWeaponSlot.Slot4 => "БОСС ПОЯВИЛСЯ",
            _ => "НОВЫЙ ЭТАП"
        };
    }

    private void DrawAchievementCard(AchievementCardDefinition card, bool allowContinue)
    {
        DrawBackdrop();
        if (card == null)
        {
            return;
        }

        (string title, string body) = GetAchievementCopy(card.Id);
        string type = card.Kind == AchievementCardKind.Direction ? "НАПРАВЛЕНИЕ" : "ЭТАП";
        Rect panel = CenteredPanel(Mathf.Min(860f, Screen.width - 30f), Mathf.Min(560f, Screen.height - 30f));
        GUI.Box(panel, $"{type} · {title}");
        float imageSide = Mathf.Min(panel.width * 0.34f, panel.height - 180f);
        Rect imageRect = new(
            panel.x + 35f,
            panel.y + 70f + (panel.height - 150f - imageSide) * 0.5f,
            imageSide,
            imageSide);
        if (_achievementPhotos != null && _achievementPhotos.TryGetPhoto(card.Id, out Texture2D photo))
        {
            GUI.Box(imageRect, GUIContent.none);
            GUI.DrawTexture(imageRect, photo, ScaleMode.ScaleToFit, true);
        }
        else
        {
            GUI.Box(imageRect, $"БЕЗОПАСНЫЙ\nПЛЕЙСХОЛДЕР\n\n{card.Image.Resolve().AssetId}");
        }
        GUI.Label(new Rect(panel.x + panel.width * 0.4f, panel.y + 85f, panel.width * 0.54f, panel.height - 180f), body);

        if (allowContinue)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = _overlayElapsedSeconds >= _achievementCards.Config.CardContinueDelaySeconds;
            if (GUI.Button(new Rect(panel.x + panel.width * 0.5f - 140f, panel.yMax - 70f, 280f, 48f), "ПРОДОЛЖИТЬ"))
            {
                ClosePresentedCard();
            }
            GUI.enabled = previousEnabled;
        }
        else if (GUI.Button(new Rect(panel.x + panel.width * 0.5f - 140f, panel.yMax - 70f, 280f, 48f), "НАЗАД"))
        {
            CancelShellOverlay();
        }
    }

    private void DrawPauseMenu()
    {
        DrawBackdrop();
        bool developmentBuild = Debug.isDebugBuild && _debugAdmin != null;
        Rect panel = CenteredPanel(440f, developmentBuild ? 520f : 450f);
        GUI.Box(panel, "ПАУЗА");
        if (GUI.Button(new Rect(panel.x + 70f, panel.y + 70f, 300f, 54f), "ПРОДОЛЖИТЬ")) ContinueFromPause();
        if (GUI.Button(new Rect(panel.x + 70f, panel.y + 134f, 300f, 54f), "НАСТРОЙКИ")) _shell.OpenSettings();

        float y = panel.y + 198f;
        if (developmentBuild)
        {
            string label = _debugAdmin.DebugUnlocked
                ? "РАЗРАБОТКА"
                : $"ЖУРНАЛ ({_debugAdmin.DevelopmentLogErrorCount})";
            if (GUI.Button(new Rect(panel.x + 70f, y, 300f, 54f), label))
            {
                if (_debugAdmin.DebugUnlocked) _shell.OpenDevelopmentHub();
                else _shell.OpenDevelopmentLog();
            }
            y += 64f;
        }

        if (GUI.Button(new Rect(panel.x + 70f, y, 300f, 54f), "НАЧАТЬ ЗАНОВО")) _shell.OpenRestartConfirmation();
        y += 64f;
        if (GUI.Button(new Rect(panel.x + 70f, y, 300f, 54f), "ВЫЙТИ ИЗ ИГРЫ")) _shell.OpenExitConfirmation();
    }

    private void DrawDevelopmentHub()
    {
        DrawBackdrop();
        Rect panel = CenteredPanel(460f, 390f);
        GUI.Box(panel, "РАЗРАБОТКА");
        if (GUI.Button(new Rect(panel.x + 70f, panel.y + 70f, 320f, 54f), "ПАРАМЕТРЫ")) _shell.OpenDevelopmentParameters();
        if (GUI.Button(new Rect(panel.x + 70f, panel.y + 134f, 320f, 54f), "SPECIAL-КАРТЫ")) _shell.OpenDevelopmentSpecialCards();
        if (GUI.Button(new Rect(panel.x + 70f, panel.y + 198f, 320f, 54f), $"ЖУРНАЛ ({_debugAdmin.DevelopmentLogErrorCount})")) _shell.OpenDevelopmentLog();
        if (GUI.Button(new Rect(panel.x + 100f, panel.y + 286f, 260f, 48f), "НАЗАД")) CancelShellOverlay();
    }

    private void DrawDevelopmentParameters()
    {
        DrawBackdrop();
        _debugAdmin.DrawParametersPage();
        DrawDevelopmentBackButton();
    }

    private void DrawDevelopmentSpecialCards()
    {
        DrawBackdrop();
        _debugAdmin.DrawSpecialCardsPage();
        DrawDevelopmentBackButton();
    }

    private void DrawDevelopmentLog()
    {
        DrawBackdrop();
        if (_debugAdmin.DrawRuntimeLogPage())
        {
            CancelShellOverlay();
        }
    }

    private void DrawDevelopmentBackButton()
    {
        if (GUI.Button(new Rect(Screen.width - 156f, 20f, 136f, 36f), "НАЗАД"))
        {
            CancelShellOverlay();
        }
    }

    private void DrawSettings()
    {
        DrawBackdrop();
        Rect panel = CenteredPanel(520f, 360f);
        GUI.Box(panel, "НАСТРОЙКИ");
        GUI.Label(new Rect(panel.x + 55f, panel.y + 85f, 410f, 28f), $"ОБЩАЯ ГРОМКОСТЬ: {Mathf.RoundToInt(_masterVolume * 100f)}%");
        float volume = GUI.HorizontalSlider(new Rect(panel.x + 55f, panel.y + 125f, 410f, 24f), _masterVolume, 0f, 1f);
        if (Mathf.Approximately(volume, _masterVolume) == false)
        {
            _masterVolume = volume;
            AudioListener.volume = volume;
            SaveSettings();
        }

        bool fullscreen = GUI.Toggle(new Rect(panel.x + 55f, panel.y + 180f, 410f, 36f), _fullscreenPreference, "ПОЛНОЭКРАННЫЙ РЕЖИМ");
        if (fullscreen != _fullscreenPreference)
        {
            _fullscreenPreference = fullscreen;
            Screen.fullScreen = fullscreen;
            SaveSettings();
        }

        if (GUI.Button(new Rect(panel.x + 130f, panel.y + 270f, 260f, 48f), "НАЗАД"))
        {
            CancelShellOverlay();
        }
    }

    private void DrawCredits()
    {
        DrawBackdrop();
        Rect panel = CenteredPanel(600f, 390f);
        GUI.Box(panel, "ТИТРЫ");
        GUI.Label(
            new Rect(panel.x + 55f, panel.y + 80f, 490f, 220f),
            "LOGO SURVIVOR — рабочее название\n\nСтуденческий кружок разработки игр Respawn\n\nПерсональные имена и сторонние лицензии появятся только после owner approval.");
        if (GUI.Button(new Rect(panel.x + 170f, panel.y + 315f, 260f, 48f), "НАЗАД"))
        {
            CancelShellOverlay();
        }
    }

    private void DrawResult()
    {
        DrawBackdrop();
        SessionResultSnapshot snapshot = _results.Snapshot;
        if (snapshot == null)
        {
            return;
        }

        float panelWidth = Mathf.Min(920f, Screen.width - 24f);
        Rect panel = new((Screen.width - panelWidth) * 0.5f, 10f, panelWidth, Screen.height - 20f);
        GUI.Box(panel, snapshot.Outcome == SessionOutcome.Victory ? "ПОБЕДА" : "ПОРАЖЕНИЕ");

        Rect scrollRect = new(panel.x + 18f, panel.y + 48f, panel.width - 36f, panel.height - 128f);
        Rect contentRect = new(0f, 0f, scrollRect.width - 22f, 1080f + snapshot.Upgrades.Count * 28f);
        _resultScroll = GUI.BeginScrollView(scrollRect, _resultScroll, contentRect);
        float y = 8f;

        string time = TimeSpan.FromSeconds(snapshot.ActiveGameplayTimeSeconds).ToString(@"mm\:ss");
        GUI.Box(new Rect(0f, y, contentRect.width, 120f),
            $"ВРЕМЯ  {time}     ВРАГИ  {snapshot.ConfirmedKills}     УРОН  {snapshot.ActualDamage}\nУРОВЕНЬ  {snapshot.FinalLevel}     ВОЛНЫ  {snapshot.CompletedWaveCount}/6");
        y += 138f;

        GUI.Box(new Rect(0f, y, contentRect.width, 42f), "ИТОГОВЫЙ БИЛД");
        y += 50f;
        if (snapshot.Upgrades.Count == 0)
        {
            GUI.Label(new Rect(16f, y, contentRect.width - 32f, 30f), "Улучшения не получены");
            y += 36f;
        }
        else
        {
            foreach (SessionUpgradeSnapshot upgrade in snapshot.Upgrades)
            {
                GUI.Label(new Rect(16f, y, contentRect.width - 32f, 26f), $"• {upgrade.Title} · уровень {upgrade.Level}");
                y += 28f;
            }
        }

        y += 10f;
        GUI.Box(new Rect(0f, y, contentRect.width, 42f), $"КАРТОЧКИ ГЕЙМДЕВА · ПОЛУЧЕНО: {snapshot.AchievementCards.ObtainedCount}/6");
        y += 54f;
        float cardGap = 10f;
        float cardWidth = (contentRect.width - cardGap * 2f) / 3f;
        for (int index = 0; index < snapshot.AchievementCards.Slots.Count; index++)
        {
            AchievementCardSlotState slot = snapshot.AchievementCards.Slots[index];
            int row = index / 3;
            int column = index % 3;
            Rect cardRect = new(column * (cardWidth + cardGap), y + row * 92f, cardWidth, 78f);
            (string title, _) = GetAchievementCopy(slot.Definition.Id);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = slot.Obtained;
            if (GUI.Button(cardRect, slot.Obtained ? title : $"НЕ ПОЛУЧЕНО\n{slot.Definition.Order}/6"))
            {
                _expandedCard = slot.Definition;
                _shell.OpenExpandedCard();
            }
            GUI.enabled = previousEnabled;
        }
        y += 200f;

        GUI.Box(new Rect(0f, y, contentRect.width, 42f), "ПРОДОЛЖИТЬ ЗНАКОМСТВО");
        y += 54f;
        DrawQrDestinations(snapshot.QrContent, contentRect.width, ref y);
        GUI.EndScrollView();

        if (GUI.Button(new Rect(panel.x + 34f, panel.yMax - 64f, panel.width * 0.58f, 48f), "ИГРАТЬ СНОВА"))
        {
            RestartSession(autoStart: true);
        }
        if (GUI.Button(new Rect(panel.x + panel.width * 0.65f, panel.yMax - 64f, panel.width * 0.31f, 48f), "ВЫЙТИ ИЗ ИГРЫ"))
        {
            _shell.OpenExitConfirmation();
        }
    }

    private static void DrawQrDestinations(QrContentSnapshot qrContent, float width, ref float y)
    {
        if (qrContent.HasVisibleDestinations == false)
        {
            GUI.Label(new Rect(10f, y, width - 20f, 30f), "QR-контент отключён");
            y += 40f;
            return;
        }

        bool narrow = width < 650f;
        float gap = 12f;
        float cardWidth = narrow ? width : (width - gap) * 0.5f;
        for (int index = 0; index < qrContent.Destinations.Count; index++)
        {
            QrDestinationDefinition destination = qrContent.Destinations[index];
            float x = narrow ? 0f : index * (cardWidth + gap);
            float cardY = narrow ? y + index * 150f : y;
            string qrStatus = destination.State == QrDestinationState.Available
                ? "QR ПРОВЕРЕН"
                : "QR ВРЕМЕННО НЕДОСТУПЕН · ТЕКСТОВЫЙ FALLBACK";
            if (GUI.Button(
                new Rect(x, cardY, cardWidth, 136f),
                $"{destination.Title}\n{destination.Description}\n{qrStatus}\n{destination.EffectiveUrl}\nОТКРЫТЬ"))
            {
                Application.OpenURL(destination.EffectiveUrl);
            }
        }

        y += narrow ? qrContent.Destinations.Count * 150f : 150f;
    }

    private void DrawConfirmation(string title, string body, bool confirmRestart)
    {
        DrawBackdrop();
        Rect panel = CenteredPanel(520f, 300f);
        GUI.Box(panel, title);
        GUI.Label(new Rect(panel.x + 60f, panel.y + 90f, 400f, 60f), body);
        if (GUI.Button(new Rect(panel.x + 45f, panel.y + 205f, 200f, 48f), "ПОДТВЕРДИТЬ"))
        {
            if (confirmRestart)
            {
                RestartSession(autoStart: true);
            }
            else
            {
                ConfirmExit();
            }
        }
        if (GUI.Button(new Rect(panel.x + 275f, panel.y + 205f, 200f, 48f), "ОТМЕНА"))
        {
            CancelShellOverlay();
        }
    }

    private static Rect CenteredPanel(float width, float height)
    {
        width = Mathf.Min(width, Screen.width - 20f);
        height = Mathf.Min(height, Screen.height - 20f);
        return new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
    }

    private static (string title, string body) GetAchievementCopy(string cardId)
    {
        return cardId switch
        {
            "direction_game_design_guild" => ("Гильдия Геймдизайна", "Здесь идеи превращаются в игровой опыт и задачи геймдизайнера.\n\n• Продумываем правила, механики и путь игрока.\n• Создаём прототипы, настраиваем баланс и описываем решения для команды."),
            "stage_preproduction" => ("Пре-продакшен", "На этом этапе команда проверяет идею и планирует будущую игру.\n\n• Уточняем аудиторию, правила и риски.\n• Собираем прототип и оцениваем объём работы."),
            "direction_art_club" => ("Арт-Клуб", "Здесь игровой мир получает визуальный язык и задачи игровых художников.\n\n• Создаём 2D/3D-графику, интерфейсы и эффекты.\n• Помогаем игроку понимать пространство, действия и настроение."),
            "stage_production" => ("Продакшн", "На этом этапе специалисты разных направлений собирают игру вместе.\n\n• Создаём контент и игровые системы.\n• Интегрируем, тестируем и улучшаем результат итерациями."),
            "direction_programmers_club" => ("Клуб Программистов", "Здесь проект превращается в работающую игру и задачи разработчиков.\n\n• Создаём игровые системы, инструменты и интеграции.\n• Следим за стабильностью, производительностью и сборками."),
            _ => ("Релиз + Постпродакшен", "На этом этапе команда готовит стабильную версию и развивает её после показа.\n\n• Проверяем сборку и исправляем критические проблемы.\n• Собираем обратную связь и планируем улучшения.")
        };
    }

    private void OnDestroy()
    {
        if (_firstArenaWaves != null) _firstArenaWaves.WaveCompleted -= HandleFirstArenaWaveCompleted;
        if (_multiArenaWaves != null) _multiArenaWaves.WaveCompleted -= HandleArenaWaveCompleted;
        if (_arenaRoute != null)
        {
            _arenaRoute.ArenaEntered -= HandleArenaEnteredForLoadout;
            _arenaRoute.BossArenaOpened -= HandleBossSpawnedForLoadout;
            _arenaRoute.OverviewRequested -= HandleOverviewRequested;
        }
        if (_progression != null) _progression.ChoiceClosed -= HandleProgressionChoiceClosed;
        if (_combat != null) _combat.DefeatPublished -= HandleDefeatPublished;
        if (_boss != null)
        {
            _boss.VictoryPublished -= HandleVictoryPublished;
            _boss.VictoryCleanupCompleted -= HandleVictoryCleanupCompleted;
            _boss.DefeatCleanupCompleted -= HandleDefeatCleanupCompleted;
        }
        _cameraFollow?.SetPauseOverlayActive(false);
        Time.timeScale = 1f;
    }
}
