using Unity.Entities;
using Unity.Mathematics;

namespace Assets.Scripts.Ecs
{
    public enum EnemyArchetype : byte
    {
        Normal,
        Swarm,
        Heavy,
        Ranged
    }

    public enum WeaponSlot : byte
    {
        Pistol = 1,
        MachineGun = 2
    }

    public enum DamageSource : byte
    {
        None,
        EnemyContact,
        EnemyRangedProjectile
    }

    public struct EnemyArchetypeComponent : IComponentData
    {
        public EnemyArchetype Value;
    }

    public struct EnemyBehaviourComponent : IComponentData
    {
        public float BaseSpeed;
        public float PreferredDistance;
        public float DashCooldown;
        public float DashRemaining;
        public float SlowRemaining;
    }

    public struct PlayerCombatState : IComponentData
    {
        public WeaponSlot SelectedWeapon;
        public float PistolCooldown;
        public float MachineGunCooldown;
        public int Experience;
    }

    public struct PlayerProgressionState : IComponentData
    {
        public int Level;
        public int NextLevelExperience;
        public int PistolUpgradeCount;
        public int MachineGunUpgradeCount;
        public float MoveSpeedMultiplier;
        public float ExperienceRadiusMultiplier;
        public float ExperienceValueMultiplier;
        public float HealthRegenerationPerSecond;
        public float HealthRegenerationAccumulator;
        public bool DashUnlocked;
        public float DashCooldownRemaining;
        public float DashRemaining;
        public float InvulnerabilityRemaining;
        public bool PistolExplosion;
        public bool PistolRicochet;
        public bool MachineGunSlow;
        public bool MachineGunChainLightning;
    }

    public struct GameplayTuningComponent : IComponentData
    {
        public int PistolDamage;
        public float PistolIntervalSeconds;
        public int MachineGunDamage;
        public float MachineGunIntervalSeconds;
        public float PlayerBaseSpeed;
        public float ExperienceRadius;
        public float ExperienceValueMultiplier;
        public float DashCooldownSeconds;
        public float DashDurationSeconds;
        public float DashSpeedMultiplier;
        public float DashInvulnerabilitySeconds;
    }

    public struct PlayerAimComponent : IComponentData
    {
        public float3 Position;
        public float3 Direction;
    }

    public struct ProjectileComponent : IComponentData
    {
        public float3 Direction;
        public float Speed;
        public float RemainingDistance;
        public int Damage;
        public int PierceRemaining;
        public bool DoubleDamageAgainstHeavy;
        public float ExplosionRadius;
        public int RicochetRemaining;
        public float SlowSeconds;
        public int ChainLightningRemaining;
        public float4 Color;
        public float VisualScale;
    }

    public struct RangedProjectileComponent : IComponentData
    {
        public float3 Start;
        public float3 ImpactPoint;
        public float Duration;
        public float Elapsed;
        public float ArcHeight;
        public float ImpactRadius;
        public int Damage;
    }

    public struct PlayerDefeatInfo : IComponentData
    {
        public DamageSource LastDamageSource;
    }

    public struct ProjectileHit : IBufferElementData
    {
        public Entity Target;
    }

    public struct ExperiencePickupComponent : IComponentData
    {
        public int Value;
        public float AttractionRadius;
        public float AttractionSpeed;
    }

    public struct TracerEvent : IComponentData
    {
        public float3 Start;
        public float3 End;
        public float4 Color;
    }
}
