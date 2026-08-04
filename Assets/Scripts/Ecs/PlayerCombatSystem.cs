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
            float machineGunContinuousFireSeconds = combat.ValueRO.SelectedWeapon == WeaponSlot.MachineGun
                ? combat.ValueRO.MachineGunContinuousFireSeconds + deltaTime
                : 0f;
            combat.ValueRW.MachineGunContinuousFireSeconds = machineGunContinuousFireSeconds;

            EntityCommandBuffer commandBuffer = new(Allocator.Temp);
            if (combat.ValueRO.SelectedWeapon == WeaponSlot.Pistol && combat.ValueRO.PistolCooldown <= 0f && HasEnemyInAimCone(ref state, playerTransform.Position, aim.Direction, 0.6f, CombatBalance.PistolRange))
            {
                CreatePistolProjectile(commandBuffer, playerTransform.Position, aim.Direction, tuning, progression);
                combat.ValueRW.PistolCooldown += tuning.PistolIntervalSeconds;
            }
            else if (combat.ValueRO.SelectedWeapon == WeaponSlot.MachineGun && combat.ValueRO.MachineGunCooldown <= 0f)
            {
                bool overheatActive = progression.MachineGunOverheat && machineGunContinuousFireSeconds >= 1.5f;
                FireMachineGun(ref state, commandBuffer, playerTransform.Position, aim.Direction, tuning, progression, overheatActive);
                combat.ValueRW.MachineGunCooldown += tuning.MachineGunIntervalSeconds / (overheatActive ? 1.5f : 1f);
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
            int damage = (int)math.ceil(tuning.PistolDamage * (progression.PistolHeavyBullet ? 2.5f : 1f));
            if (progression.PistolSplitShot)
            {
                const float splitSpreadDegrees = 12f;
                for (int index = -1; index <= 1; index++)
                {
                    float3 splitDirection = math.normalizesafe(math.mul(quaternion.RotateY(math.radians(splitSpreadDegrees * index)), direction), direction);
                    CreateSinglePistolProjectile(commandBuffer, origin, splitDirection, progression, (int)math.ceil(damage * 0.65f));
                }
                return;
            }

            CreateSinglePistolProjectile(commandBuffer, origin, direction, progression, damage);
        }

        private void CreateSinglePistolProjectile(EntityCommandBuffer commandBuffer, float3 origin, float3 direction, PlayerProgressionState progression, int damage)
        {
            Entity projectile = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(projectile, LocalTransform.FromPosition(origin + direction * 0.6f));
            commandBuffer.AddComponent(projectile, new ProjectileComponent
            {
                Direction = direction,
                Speed = CombatBalance.PistolSpeed * (progression.PistolHeavyBullet ? 0.65f : 1f),
                RemainingDistance = CombatBalance.PistolRange,
                Damage = damage,
                PierceRemaining = progression.PistolPiercing ? 4 : 2,
                DoubleDamageAgainstHeavy = true,
                ExplosionRadius = progression.PistolExplosion ? 2f : 0f,
                RicochetRemaining = progression.PistolRicochet ? 2 : 0,
                BurnSeconds = progression.PistolElementalCharge ? 3f : 0f,
                BurnDamagePerTick = progression.PistolElementalCharge ? math.max(1, (int)math.ceil(damage * 0.35f)) : 0,
                Color = new float4(1f, 1f, 1f, 1f),
                VisualScale = progression.PistolHeavyBullet ? 0.23f : 0.15f
            });
            commandBuffer.AddBuffer<ProjectileHit>(projectile);
        }

        private void FireMachineGun(ref SystemState state, EntityCommandBuffer commandBuffer, float3 origin, float3 aimDirection, GameplayTuningComponent tuning, PlayerProgressionState progression, bool overheatActive)
        {
            int pelletCount = progression.MachineGunScatter ? 5 : 1;
            float spreadDegrees = progression.MachineGunScatter ? 18f : CombatBalance.MachineGunSpreadDegrees;
            int damage = (int)math.ceil(tuning.MachineGunDamage * (overheatActive ? 1.5f : 1f) * (progression.MachineGunScatter ? 0.45f : 1f));
            for (int pelletIndex = 0; pelletIndex < pelletCount; pelletIndex++)
            {
                float normalizedPellet = pelletCount == 1 ? 0f : pelletIndex / (float)(pelletCount - 1) * 2f - 1f;
                float randomOffset = progression.MachineGunScatter ? 0f : UnityEngine.Random.Range(-spreadDegrees, spreadDegrees);
                float3 direction = math.normalizesafe(math.mul(quaternion.RotateY(math.radians(normalizedPellet * spreadDegrees + randomOffset)), aimDirection), aimDirection);
                CreateMachineGunProjectile(commandBuffer, origin, direction, progression, damage);
            }
        }

        private void CreateMachineGunProjectile(EntityCommandBuffer commandBuffer, float3 origin, float3 direction, PlayerProgressionState progression, int damage)
        {
            Entity projectile = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(projectile, LocalTransform.FromPosition(origin + direction * 0.6f));
            commandBuffer.AddComponent(projectile, new ProjectileComponent
            {
                Direction = direction,
                Speed = CombatBalance.MachineGunSpeed,
                RemainingDistance = CombatBalance.MachineGunRange,
                Damage = damage,
                PierceRemaining = progression.MachineGunPiercing ? 4 : 0,
                DoubleDamageAgainstHeavy = false,
                SlowSeconds = progression.MachineGunSlow ? 1.5f : 0f,
                ChainLightningRemaining = progression.MachineGunChainLightning ? 2 : 0,
                ElectricStormRadius = progression.MachineGunElectricStorm ? 2.5f : 0f,
                ElectricStormDamageMultiplier = progression.MachineGunElectricStorm ? 0.5f : 0f,
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
