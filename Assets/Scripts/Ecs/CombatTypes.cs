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
    }

    public struct PlayerCombatState : IComponentData
    {
        public WeaponSlot SelectedWeapon;
        public float PistolCooldown;
        public float MachineGunCooldown;
        public int Experience;
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
