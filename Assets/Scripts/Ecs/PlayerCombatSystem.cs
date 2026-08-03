using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Assets.Scripts.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EnemySpawnSystem))]
    [UpdateBefore(typeof(AttackSystem))]
    public partial struct PlayerCombatSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<PlayerCombatState>();
            state.RequireForUpdate<PlayerAimComponent>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity player = SystemAPI.GetSingletonEntity<PlayerTag>();
            RefRW<PlayerCombatState> combat = SystemAPI.GetComponentRW<PlayerCombatState>(player);
            PlayerAimComponent aim = SystemAPI.GetComponent<PlayerAimComponent>(player);
            PlayerProgressionState progression = SystemAPI.GetComponent<PlayerProgressionState>(player);
            GameplayTuningComponent tuning = SystemAPI.GetSingleton<GameplayTuningComponent>();
            LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(player);
            float deltaTime = SystemAPI.Time.DeltaTime;

            combat.ValueRW.PistolCooldown = math.max(0f, combat.ValueRO.PistolCooldown - deltaTime);
            combat.ValueRW.MachineGunCooldown = math.max(0f, combat.ValueRO.MachineGunCooldown - deltaTime);

            EntityCommandBuffer commandBuffer = new(Allocator.Temp);
            if (combat.ValueRO.SelectedWeapon == WeaponSlot.Pistol && combat.ValueRO.PistolCooldown <= 0f && HasEnemyInAimCone(ref state, playerTransform.Position, aim.Direction, 0.6f, CombatBalance.PistolRange))
            {
                CreatePistolProjectile(commandBuffer, playerTransform.Position, aim.Direction, tuning, progression);
                combat.ValueRW.PistolCooldown += tuning.PistolIntervalSeconds;
            }
            else if (combat.ValueRO.SelectedWeapon == WeaponSlot.MachineGun && combat.ValueRO.MachineGunCooldown <= 0f)
            {
                FireMachineGun(ref state, commandBuffer, playerTransform.Position, aim.Direction, tuning, progression);
                combat.ValueRW.MachineGunCooldown += tuning.MachineGunIntervalSeconds;
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }

        private bool HasEnemyInAimCone(ref SystemState state, float3 origin, float3 direction, float coneDot, float range)
        {
            foreach ((RefRO<LocalTransform> transform, Entity _) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<EnemyTag>().WithEntityAccess())
            {
                float3 toEnemy = transform.ValueRO.Position - origin;
                float distance = math.length(toEnemy);
                if (distance > 0.01f && distance <= range && math.dot(math.normalize(toEnemy), direction) >= coneDot)
                {
                    return true;
                }
            }

            return false;
        }

        private void CreatePistolProjectile(EntityCommandBuffer commandBuffer, float3 origin, float3 direction, GameplayTuningComponent tuning, PlayerProgressionState progression)
        {
            Entity projectile = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(projectile, LocalTransform.FromPosition(origin + direction * 0.6f));
            commandBuffer.AddComponent(projectile, new ProjectileComponent
            {
                Direction = direction,
                Speed = CombatBalance.PistolSpeed,
                RemainingDistance = CombatBalance.PistolRange,
                Damage = tuning.PistolDamage,
                PierceRemaining = 2,
                DoubleDamageAgainstHeavy = true,
                ExplosionRadius = progression.PistolExplosion ? 2f : 0f,
                RicochetRemaining = progression.PistolRicochet ? 2 : 0,
                Color = new float4(1f, 1f, 1f, 1f),
                VisualScale = 0.15f
            });
            commandBuffer.AddBuffer<ProjectileHit>(projectile);
        }

        private void FireMachineGun(ref SystemState state, EntityCommandBuffer commandBuffer, float3 origin, float3 aimDirection, GameplayTuningComponent tuning, PlayerProgressionState progression)
        {
            float radians = math.radians(CombatBalance.MachineGunSpreadDegrees);
            float spread = UnityEngine.Random.Range(-radians, radians);
            float3 direction = math.normalizesafe(math.mul(quaternion.RotateY(spread), aimDirection), aimDirection);
            Entity projectile = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(projectile, LocalTransform.FromPosition(origin + direction * 0.6f));
            commandBuffer.AddComponent(projectile, new ProjectileComponent
            {
                Direction = direction,
                Speed = CombatBalance.MachineGunSpeed,
                RemainingDistance = CombatBalance.MachineGunRange,
                Damage = tuning.MachineGunDamage,
                PierceRemaining = 0,
                DoubleDamageAgainstHeavy = false,
                SlowSeconds = progression.MachineGunSlow ? 1.5f : 0f,
                ChainLightningRemaining = progression.MachineGunChainLightning ? 2 : 0,
                Color = new float4(1f, 0.9f, 0.2f, 1f),
                VisualScale = 0.1f
            });
            commandBuffer.AddBuffer<ProjectileHit>(projectile);
        }

        private void CreateDamageRequest(EntityCommandBuffer commandBuffer, Entity target, int amount)
        {
            Entity request = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(request, new DamageRequest { Target = target, Amount = amount });
        }
    }
}
