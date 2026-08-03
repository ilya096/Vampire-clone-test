using Assets.Scripts.Ecs;
using Unity.Entities;
using UnityEngine;

/// <summary>Runtime card choices for the first playable progression slice.</summary>
public class PlayerProgressionController : MonoBehaviour
{
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
        for (int index = 0; index < _offers.Length; index++)
        {
            CardKind candidate;
            do candidate = _allCards[random.Next(_allCards.Length)]; while (ContainsBefore(index, candidate));
            _offers[index] = candidate;
            _rarities[index] = RollRarity(random);
        }

        _specialWeapon = SpecialWeapon.None;
        OpenChoice();
    }

    private bool ContainsBefore(int exclusiveIndex, CardKind candidate)
    {
        for (int index = 0; index < exclusiveIndex; index++) if (_offers[index] == candidate) return true;
        return false;
    }

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

    private void Select(int index)
    {
        if (_specialWeapon != SpecialWeapon.None)
        {
            ApplySpecial(index);
            CloseChoice();
            return;
        }

        ApplyCard(_offers[index], GetRarityMultiplier(_rarities[index]));
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        progression.Level++;
        progression.NextLevelExperience = Mathf.CeilToInt(progression.NextLevelExperience * 1.45f);
        _entityManager.SetComponentData(_playerEntity, progression);

        if ((progression.PistolUpgradeCount > 0 && progression.PistolUpgradeCount % 3 == 0) || (progression.MachineGunUpgradeCount > 0 && progression.MachineGunUpgradeCount % 3 == 0))
        {
            _specialWeapon = progression.PistolUpgradeCount % 3 == 0 ? SpecialWeapon.Pistol : SpecialWeapon.MachineGun;
            return;
        }

        CloseChoice();
    }

    private void ApplyCard(CardKind kind, float multiplier)
    {
        GameplayTuningComponent tuning = _entityManager.GetComponentData<GameplayTuningComponent>(_entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameplayTuningComponent>()).GetSingletonEntity());
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        HealthComponent health = _entityManager.GetComponentData<HealthComponent>(_playerEntity);
        switch (kind)
        {
            case CardKind.PistolDamage: tuning.PistolDamage += Mathf.CeilToInt(5f * multiplier); progression.PistolUpgradeCount++; break;
            case CardKind.PistolFireRate: tuning.PistolIntervalSeconds = Mathf.Max(0.1f, tuning.PistolIntervalSeconds - 0.05f * multiplier); progression.PistolUpgradeCount++; break;
            case CardKind.MachineGunDamage: tuning.MachineGunDamage += Mathf.CeilToInt(2f * multiplier); progression.MachineGunUpgradeCount++; break;
            case CardKind.MachineGunFireRate: tuning.MachineGunIntervalSeconds = Mathf.Max(0.04f, tuning.MachineGunIntervalSeconds - 0.02f * multiplier); progression.MachineGunUpgradeCount++; break;
            case CardKind.MoveSpeed: progression.MoveSpeedMultiplier += 0.08f * multiplier; break;
            case CardKind.ReactiveDash: progression.DashUnlocked = true; break;
            case CardKind.ExperienceRadius: progression.ExperienceRadiusMultiplier += 0.2f * multiplier; break;
            case CardKind.ExperienceValue: progression.ExperienceValueMultiplier += 0.15f * multiplier; break;
            case CardKind.MaxHealth: health.MaxValue += Mathf.CeilToInt(15f * multiplier); health.Value = health.MaxValue; break;
            case CardKind.HealthRegeneration: progression.HealthRegenerationPerSecond += 0.5f * multiplier; break;
        }

        Entity tuningEntity = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameplayTuningComponent>()).GetSingletonEntity();
        _entityManager.SetComponentData(tuningEntity, tuning);
        _entityManager.SetComponentData(_playerEntity, progression);
        _entityManager.SetComponentData(_playerEntity, health);
    }

    private void ApplySpecial(int index)
    {
        PlayerProgressionState progression = _entityManager.GetComponentData<PlayerProgressionState>(_playerEntity);
        if (_specialWeapon == SpecialWeapon.Pistol)
        {
            progression.PistolExplosion = index == 0;
            progression.PistolRicochet = index == 1;
        }
        else
        {
            progression.MachineGunSlow = index == 0;
            progression.MachineGunChainLightning = index == 1;
        }
        _entityManager.SetComponentData(_playerEntity, progression);
    }

    private string GetSpecialLabel(int index) => _specialWeapon switch
    {
        SpecialWeapon.Pistol => index == 0 ? "ВЗРЫВ\nПистолет поражает группу" : "РИКОШЕТ\nПуля ищет новую цель",
        _ => index == 0 ? "ЗАМЕДЛЕНИЕ\nОчередь тормозит врагов" : "ЦЕПНАЯ МОЛНИЯ\nВыстрел бьёт по соседям"
    };

    private static string GetCardLabel(CardKind kind, Rarity rarity) => $"{rarity}\n{kind}";
    private static Rarity RollRarity(System.Random random) => random.Next(100) switch { < 70 => Rarity.Green, < 90 => Rarity.Blue, < 98 => Rarity.Purple, _ => Rarity.Gold };
    private static float GetRarityMultiplier(Rarity rarity) => rarity switch { Rarity.Blue => 1.5f, Rarity.Purple => 2f, Rarity.Gold => 3f, _ => 1f };
    private static Color GetRarityColor(Rarity rarity) => rarity switch { Rarity.Blue => new Color(0.35f, 0.65f, 1f), Rarity.Purple => new Color(0.75f, 0.4f, 1f), Rarity.Gold => new Color(1f, 0.8f, 0.2f), _ => new Color(0.4f, 1f, 0.4f) };

    private void OnDestroy()
    {
        if (_choiceOpen) Time.timeScale = 1f;
    }
}
