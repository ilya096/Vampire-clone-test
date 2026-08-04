using Assets.Scripts.Ecs;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>Runtime card choices for the first playable progression slice.</summary>
public class PlayerProgressionController : MonoBehaviour
{
    private const string ProgressionLogPrefix = "[Logo Survivor][Progression]";

    private enum CardKind { PistolDamage, PistolFireRate, MachineGunDamage, MachineGunFireRate, MoveSpeed, ReactiveDash, ExperienceRadius, ExperienceValue, MaxHealth, HealthRegeneration }
    private enum Rarity { Green, Blue, Purple, Gold }
    private enum SpecialWeapon { None, Pistol, MachineGun }

    private readonly CardKind[] _allCards =
    {
        CardKind.PistolDamage, CardKind.PistolFireRate, CardKind.MachineGunDamage, CardKind.MachineGunFireRate,
        CardKind.MoveSpeed, CardKind.ReactiveDash, CardKind.ExperienceRadius, CardKind.ExperienceValue,
        CardKind.MaxHealth, CardKind.HealthRegeneration
    };

    private World _world;
    private EntityManager _entityManager;
    private Entity _playerEntity;
    private CardKind[] _offers = new CardKind[3];
    private Rarity[] _rarities = new Rarity[3];
    private bool _choiceOpen;
    private SpecialWeapon _specialWeapon;
    private int _specialTier;
    private GUIStyle _experienceBarLabelStyle;

    public void Initialize(World world, Entity playerEntity)
    {
        _world = world;
        _entityManager = world.EntityManager;
        _playerEntity = playerEntity;
    }

    private void Update()
    {
        if (_world == null || _world.IsCreated == false || _entityManager.Exists(_playerEntity) == false)
        {
            return;
        }

        if (_choiceOpen)
        {
            return;
        }

        PlayerCombatState combat = _entityManager.GetComponentData<PlayerCombatState>(_playerEntity);
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        if (combat.Experience >= progression.NextLevelExperience)
        {
            OpenNormalChoice();
        }
        else if (progression.HealthRegenerationPerSecond > 0f)
        {
            HealthComponent health = _entityManager.GetComponentData<HealthComponent>(_playerEntity);
            progression.HealthRegenerationAccumulator += progression.HealthRegenerationPerSecond * Time.deltaTime;
            int healed = Mathf.FloorToInt(progression.HealthRegenerationAccumulator);
            if (healed <= 0)
            {
                _entityManager.SetComponentData(_playerEntity, progression);
                return;
            }

            progression.HealthRegenerationAccumulator -= healed;
            health.Value = Mathf.Min(health.MaxValue, health.Value + healed);
            _entityManager.SetComponentData(_playerEntity, health);
            _entityManager.SetComponentData(_playerEntity, progression);
        }
    }

    private void OpenNormalChoice()
    {
        var random = new System.Random();
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        var availableCards = new List<CardKind>(_allCards.Length);
        foreach (CardKind card in _allCards)
        {
            if (IsCardAvailable(card, progression))
            {
                availableCards.Add(card);
            }
        }

        for (int index = 0; index < _offers.Length; index++)
        {
            int candidateIndex = random.Next(availableCards.Count);
            CardKind candidate = availableCards[candidateIndex];
            availableCards.RemoveAt(candidateIndex);
            _offers[index] = candidate;
            _rarities[index] = RollRarity(random);
        }

        _specialWeapon = SpecialWeapon.None;
        Debug.Log($"{ProgressionLogPrefix} Открыт обычный выбор: {GetNormalOfferLog()}");
        OpenChoice();
    }

    private static bool IsCardAvailable(CardKind card, PlayerProgressionState progression) => card switch
    {
        CardKind.PistolDamage or CardKind.PistolFireRate => progression.PistolUpgradeCount < 9,
        CardKind.MachineGunDamage or CardKind.MachineGunFireRate => progression.MachineGunUpgradeCount < 9,
        _ => true
    };

    private void OpenChoice()
    {
        _choiceOpen = true;
        Time.timeScale = 0f;
    }

    private void CloseChoice()
    {
        _choiceOpen = false;
        Time.timeScale = 1f;
    }

    private void OnGUI()
    {
        if (_world == null || _world.IsCreated == false || _entityManager.Exists(_playerEntity) == false)
        {
            return;
        }

        DrawExperienceProgress();

        if (_choiceOpen == false)
        {
            return;
        }

        Rect panel = new(Screen.width * 0.5f - 330f, Screen.height * 0.5f - 120f, 660f, 240f);
        GUI.Box(panel, _specialWeapon == SpecialWeapon.None ? "ВЫБЕРИТЕ УСИЛЕНИЕ" : "ЗНАЧИМЫЙ ВЫБОР ОРУЖИЯ");
        for (int index = 0; index < (_specialWeapon == SpecialWeapon.None ? 3 : 2); index++)
        {
            Rect button = new(panel.x + 20f + index * 210f, panel.y + 60f, 190f, 130f);
            string label = _specialWeapon == SpecialWeapon.None ? GetCardLabel(_offers[index], _rarities[index]) : GetSpecialLabel(index);
            Color previousColor = GUI.color;
            GUI.color = _specialWeapon == SpecialWeapon.None ? GetRarityColor(_rarities[index]) : new Color(1f, 0.75f, 0.2f);
            if (GUI.Button(button, label))
            {
                Select(index);
            }
            GUI.color = previousColor;
        }
    }

    private void DrawExperienceProgress()
    {
        PlayerCombatState combat = _entityManager.GetComponentData<PlayerCombatState>(_playerEntity);
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        int experienceRequirement = Mathf.Max(1, progression.NextLevelExperience);
        float fill = Mathf.Clamp01(combat.Experience / (float)experienceRequirement);

        const float horizontalInset = 10f;
        const float bottomInset = 4f;
        const float frameHeight = 26f;
        Rect frame = new(horizontalInset, Screen.height - frameHeight - bottomInset, Screen.width - horizontalInset * 2f, frameHeight);
        Rect inner = new(frame.x + 3f, frame.y + 3f, frame.width - 6f, frame.height - 6f);
        Rect fillRect = new(inner.x, inner.y, inner.width * fill, inner.height);

        Color previousColor = GUI.color;
        GUI.color = new Color(0.01f, 0.04f, 0.08f, 0.94f);
        GUI.DrawTexture(frame, Texture2D.whiteTexture);
        GUI.color = new Color(0.04f, 0.16f, 0.21f, 1f);
        GUI.DrawTexture(inner, Texture2D.whiteTexture);
        GUI.color = new Color(0.05f, 0.78f, 0.95f, 1f);
        GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
        GUI.color = previousColor;

        if (_experienceBarLabelStyle == null)
        {
            _experienceBarLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            _experienceBarLabelStyle.normal.textColor = Color.white;
        }

        GUI.Label(frame, $"УРОВЕНЬ {progression.Level}    XP {combat.Experience} / {experienceRequirement}", _experienceBarLabelStyle);
    }

    private void Select(int index)
    {
        if (_specialWeapon != SpecialWeapon.None)
        {
            ApplySpecial(index);
            CloseChoice();
            return;
        }

        ApplyCard(_offers[index], _rarities[index], GetRarityMultiplier(_rarities[index]));
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        PlayerCombatState combat = _entityManager.GetComponentData<PlayerCombatState>(_playerEntity);
        int spentExperience = progression.NextLevelExperience;
        combat.Experience = Mathf.Max(0, combat.Experience - spentExperience);
        progression.Level++;
        progression.NextLevelExperience = Mathf.CeilToInt(progression.NextLevelExperience * 1.45f);
        _entityManager.SetComponentData(_playerEntity, combat);
        _entityManager.SetComponentData(_playerEntity, progression);
        Debug.Log($"{ProgressionLogPrefix} Уровень применён: потрачено XP={spentExperience}, остаток XP={combat.Experience}, level={progression.Level}, следующий XP-порог={progression.NextLevelExperience}, оружейные апгрейды: pistol={progression.PistolUpgradeCount}, machine gun={progression.MachineGunUpgradeCount}.");

        if (TryOpenSpecialChoice(_offers[index], progression))
        {
            return;
        }

        CloseChoice();
    }

    private void ApplyCard(CardKind kind, Rarity rarity, float multiplier)
    {
        GameplayTuningComponent tuning = _entityManager.GetComponentData<GameplayTuningComponent>(_entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameplayTuningComponent>()).GetSingletonEntity());
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        HealthComponent health = _entityManager.GetComponentData<HealthComponent>(_playerEntity);
        string result;
        switch (kind)
        {
            case CardKind.PistolDamage:
                int previousPistolDamage = tuning.PistolDamage;
                tuning.PistolDamage += Mathf.CeilToInt(5f * multiplier);
                progression.PistolUpgradeCount++;
                result = $"урон пистолета {previousPistolDamage} -> {tuning.PistolDamage}";
                break;
            case CardKind.PistolFireRate:
                float previousPistolInterval = tuning.PistolIntervalSeconds;
                tuning.PistolIntervalSeconds = Mathf.Max(0.1f, tuning.PistolIntervalSeconds - 0.05f * multiplier);
                progression.PistolUpgradeCount++;
                result = $"интервал пистолета {previousPistolInterval:F3}s -> {tuning.PistolIntervalSeconds:F3}s";
                break;
            case CardKind.MachineGunDamage:
                int previousMachineGunDamage = tuning.MachineGunDamage;
                tuning.MachineGunDamage += Mathf.CeilToInt(2f * multiplier);
                progression.MachineGunUpgradeCount++;
                result = $"урон пулемёта {previousMachineGunDamage} -> {tuning.MachineGunDamage}";
                break;
            case CardKind.MachineGunFireRate:
                float previousMachineGunInterval = tuning.MachineGunIntervalSeconds;
                tuning.MachineGunIntervalSeconds = Mathf.Max(0.04f, tuning.MachineGunIntervalSeconds - 0.02f * multiplier);
                progression.MachineGunUpgradeCount++;
                result = $"интервал пулемёта {previousMachineGunInterval:F3}s -> {tuning.MachineGunIntervalSeconds:F3}s";
                break;
            case CardKind.MoveSpeed:
                float previousMoveSpeed = progression.MoveSpeedMultiplier;
                progression.MoveSpeedMultiplier += 0.08f * multiplier;
                result = $"множитель скорости {previousMoveSpeed:F2} -> {progression.MoveSpeedMultiplier:F2}";
                break;
            case CardKind.ReactiveDash:
                bool dashWasUnlocked = progression.DashUnlocked;
                progression.DashUnlocked = true;
                if (dashWasUnlocked)
                {
                    float previousDashCooldown = tuning.DashCooldownSeconds;
                    tuning.DashCooldownSeconds = Mathf.Max(2f, tuning.DashCooldownSeconds * 0.85f);
                    result = $"cooldown реактивного рывка {previousDashCooldown:F2}s -> {tuning.DashCooldownSeconds:F2}s";
                }
                else
                {
                    result = "реактивный рывок открыт";
                }
                break;
            case CardKind.ExperienceRadius:
                float previousExperienceRadius = progression.ExperienceRadiusMultiplier;
                progression.ExperienceRadiusMultiplier += 0.2f * multiplier;
                result = $"множитель радиуса XP {previousExperienceRadius:F2} -> {progression.ExperienceRadiusMultiplier:F2}";
                break;
            case CardKind.ExperienceValue:
                float previousExperienceValue = progression.ExperienceValueMultiplier;
                progression.ExperienceValueMultiplier += 0.15f * multiplier;
                result = $"множитель ценности XP {previousExperienceValue:F2} -> {progression.ExperienceValueMultiplier:F2}";
                break;
            case CardKind.MaxHealth:
                int previousMaxHealth = health.MaxValue;
                health.MaxValue += Mathf.CeilToInt(15f * multiplier);
                result = $"макс. HP {previousMaxHealth} -> {health.MaxValue}; текущее HP не изменено ({health.Value})";
                break;
            case CardKind.HealthRegeneration:
                float previousRegeneration = progression.HealthRegenerationPerSecond;
                progression.HealthRegenerationPerSecond += 0.5f * multiplier;
                result = $"регенерация HP {previousRegeneration:F2}/s -> {progression.HealthRegenerationPerSecond:F2}/s";
                break;
            default:
                result = "неизвестный эффект";
                break;
        }

        Entity tuningEntity = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameplayTuningComponent>()).GetSingletonEntity();
        _entityManager.SetComponentData(tuningEntity, tuning);
        _entityManager.SetComponentData(_playerEntity, progression);
        _entityManager.SetComponentData(_playerEntity, health);
        Debug.Log($"{ProgressionLogPrefix} Получен апгрейд: {GetCardName(kind)} ({GetRarityName(rarity)}, x{multiplier:F1}) — {result}.");
    }

    private void ApplySpecial(int index)
    {
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        if (_specialWeapon == SpecialWeapon.Pistol)
        {
            switch (_specialTier)
            {
                case 1: progression.PistolExplosion = index == 0; progression.PistolRicochet = index == 1; break;
                case 2: progression.PistolPiercing = index == 0; progression.PistolSplitShot = index == 1; break;
                case 3: progression.PistolHeavyBullet = index == 0; progression.PistolElementalCharge = index == 1; break;
            }
        }
        else
        {
            switch (_specialTier)
            {
                case 1: progression.MachineGunSlow = index == 0; progression.MachineGunChainLightning = index == 1; break;
                case 2: progression.MachineGunPiercing = index == 0; progression.MachineGunScatter = index == 1; break;
                case 3: progression.MachineGunOverheat = index == 0; progression.MachineGunElectricStorm = index == 1; break;
            }
        }
        _entityManager.SetComponentData(_playerEntity, progression);
        Debug.Log($"{ProgressionLogPrefix} Получен special-апгрейд {GetSpecialWeaponName(_specialWeapon)} tier {_specialTier}: {GetSpecialLabel(index).Replace("\n", " — ")}." );
    }

    private bool TryOpenSpecialChoice(CardKind appliedCard, PlayerProgressionState progression)
    {
        if (IsPistolCard(appliedCard) && progression.PistolUpgradeCount > 0 && progression.PistolUpgradeCount % 3 == 0)
        {
            _specialWeapon = SpecialWeapon.Pistol;
            _specialTier = progression.PistolUpgradeCount / 3;
        }
        else if (IsMachineGunCard(appliedCard) && progression.MachineGunUpgradeCount > 0 && progression.MachineGunUpgradeCount % 3 == 0)
        {
            _specialWeapon = SpecialWeapon.MachineGun;
            _specialTier = progression.MachineGunUpgradeCount / 3;
        }
        else
        {
            return false;
        }

        Debug.Log($"{ProgressionLogPrefix} Открыт special-выбор для {GetSpecialWeaponName(_specialWeapon)} tier {_specialTier}/3. Варианты: 1) {GetSpecialLabel(0).Replace("\n", " — ")}; 2) {GetSpecialLabel(1).Replace("\n", " — ")}.");
        return true;
    }

    private static bool IsPistolCard(CardKind kind) => kind is CardKind.PistolDamage or CardKind.PistolFireRate;
    private static bool IsMachineGunCard(CardKind kind) => kind is CardKind.MachineGunDamage or CardKind.MachineGunFireRate;

    private string GetSpecialLabel(int index) => _specialWeapon switch
    {
        SpecialWeapon.Pistol => _specialTier switch
        {
            1 => index == 0 ? "ВЗРЫВ\nПистолет поражает группу" : "РИКОШЕТ\nПуля ищет новую цель",
            2 => index == 0 ? "ПРОБИТИЕ\nПуля проходит ещё через две цели" : "РАЗДВОЕННЫЙ ВЫСТРЕЛ\nТри пули веером",
            _ => index == 0 ? "ТЯЖЁЛАЯ ПУЛЯ\n×2,5 урона, медленнее" : "СТИХИЙНЫЙ ЗАРЯД\nГорение 3 секунды"
        },
        _ => _specialTier switch
        {
            1 => index == 0 ? "ЗАМЕДЛЕНИЕ\nОчередь тормозит врагов" : "ЦЕПНАЯ МОЛНИЯ\nВыстрел бьёт по соседям",
            2 => index == 0 ? "ПРОШИВАЮЩАЯ ОЧЕРЕДЬ\nПуля проходит ещё через три цели" : "КАРТЕЧЬ\nПять дробин веером",
            _ => index == 0 ? "ПЕРЕГРЕВ\nПосле 1,5 с ×1,5 урона и темпа" : "ЭЛЕКТРИЧЕСКАЯ БУРЯ\nРазряд вокруг попадания"
        }
    };

    private static string GetCardLabel(CardKind kind, Rarity rarity) => $"{rarity}\n{kind}";
    private string GetNormalOfferLog()
    {
        PlayerCombatState combat = _entityManager.GetComponentData<PlayerCombatState>(_playerEntity);
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        return $"level={progression.Level}, XP={combat.Experience}/{progression.NextLevelExperience}; " +
               $"1) {GetRarityName(_rarities[0])} {GetCardName(_offers[0])} (x{GetRarityMultiplier(_rarities[0]):F1}); " +
               $"2) {GetRarityName(_rarities[1])} {GetCardName(_offers[1])} (x{GetRarityMultiplier(_rarities[1]):F1}); " +
               $"3) {GetRarityName(_rarities[2])} {GetCardName(_offers[2])} (x{GetRarityMultiplier(_rarities[2]):F1}).";
    }

    private static string GetCardName(CardKind kind) => kind switch
    {
        CardKind.PistolDamage => "Урон пистолета",
        CardKind.PistolFireRate => "Скорострельность пистолета",
        CardKind.MachineGunDamage => "Урон пулемёта",
        CardKind.MachineGunFireRate => "Скорострельность пулемёта",
        CardKind.MoveSpeed => "Скорость бега",
        CardKind.ReactiveDash => "Реактивный рывок",
        CardKind.ExperienceRadius => "Радиус сбора XP",
        CardKind.ExperienceValue => "Ценность XP",
        CardKind.MaxHealth => "Максимум HP",
        CardKind.HealthRegeneration => "Регенерация HP",
        _ => kind.ToString()
    };

    private static string GetRarityName(Rarity rarity) => rarity switch
    {
        Rarity.Green => "Зелёный",
        Rarity.Blue => "Синий",
        Rarity.Purple => "Фиолетовый",
        Rarity.Gold => "Золотой",
        _ => rarity.ToString()
    };

    private static string GetSpecialWeaponName(SpecialWeapon weapon) => weapon switch
    {
        SpecialWeapon.Pistol => "пистолета",
        SpecialWeapon.MachineGun => "пулемёта",
        _ => "неизвестного оружия"
    };
    private static Rarity RollRarity(System.Random random) => random.Next(100) switch { < 70 => Rarity.Green, < 90 => Rarity.Blue, < 98 => Rarity.Purple, _ => Rarity.Gold };
    private static float GetRarityMultiplier(Rarity rarity) => rarity switch { Rarity.Blue => 1.5f, Rarity.Purple => 2f, Rarity.Gold => 3f, _ => 1f };
    private static Color GetRarityColor(Rarity rarity) => rarity switch { Rarity.Blue => new Color(0.35f, 0.65f, 1f), Rarity.Purple => new Color(0.75f, 0.4f, 1f), Rarity.Gold => new Color(1f, 0.8f, 0.2f), _ => new Color(0.4f, 1f, 0.4f) };

    private void OnDestroy()
    {
        if (_choiceOpen) Time.timeScale = 1f;
    }
}
