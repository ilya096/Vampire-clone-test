using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Assets.Scripts.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerCombatSystem))]
    [UpdateBefore(typeof(DamageSystem))]
    public partial struct ProjectileSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            EntityCommandBuffer commandBuffer = new(Allocator.Temp);

            foreach ((RefRW<LocalTransform> transform, RefRW<ProjectileComponent> projectile, DynamicBuffer<ProjectileHit> hits, Entity entity) in
                SystemAPI.Query<RefRW<LocalTransform>, RefRW<ProjectileComponent>, DynamicBuffer<ProjectileHit>>().WithEntityAccess())
            {
                float distanceThisFrame = projectile.ValueRO.Speed * deltaTime;
                float3 startPosition = transform.ValueRO.Position;
                float3 endPosition = startPosition + projectile.ValueRO.Direction * distanceThisFrame;
                transform.ValueRW.Position = endPosition;
                projectile.ValueRW.RemainingDistance -= distanceThisFrame;

                Entity target = GetCollidingTargetAlongSegment(ref state, startPosition, endPosition, hits);
                if (target != Entity.Null)
                {
                    int damage = projectile.ValueRO.Damage;
                    bool isBoss = SystemAPI.HasComponent<BossTag>(target);
                    EnemyArchetype archetype = isBoss
                        ? EnemyArchetype.Normal
                        : SystemAPI.GetComponent<EnemyArchetypeComponent>(target).Value;
                    if (isBoss == false && projectile.ValueRO.DoubleDamageAgainstHeavy && archetype == EnemyArchetype.Heavy)
                    {
                        damage *= 2;
                    }

                    CreateDamageRequest(commandBuffer, target, damage);
                    if (projectile.ValueRO.ExplosionRadius > 0f)
                    {
                        CreateAreaDamage(ref state, commandBuffer, target, transform.ValueRO.Position, projectile.ValueRO.ExplosionRadius, damage);
                    }
                    if (isBoss == false && projectile.ValueRO.SlowSeconds > 0f)
                    {
                        EnemyBehaviourComponent behaviour = SystemAPI.GetComponent<EnemyBehaviourComponent>(target);
                        behaviour.SlowRemaining = math.max(behaviour.SlowRemaining, projectile.ValueRO.SlowSeconds);
                        state.EntityManager.SetComponentData(target, behaviour);
                    }
                    if (projectile.ValueRO.ChainLightningRemaining > 0)
                    {
                        CreateChainDamage(ref state, commandBuffer, target, transform.ValueRO.Position, projectile.ValueRO.ChainLightningRemaining, damage);
                    }
                    if (isBoss == false && projectile.ValueRO.BurnSeconds > 0f)
                    {
                        EnemyBehaviourComponent behaviour = SystemAPI.GetComponent<EnemyBehaviourComponent>(target);
                        bool wasBurning = behaviour.BurnRemaining > 0f;
                        behaviour.BurnRemaining = math.max(behaviour.BurnRemaining, projectile.ValueRO.BurnSeconds);
                        behaviour.BurnDamagePerTick = math.max(behaviour.BurnDamagePerTick, projectile.ValueRO.BurnDamagePerTick);
                        behaviour.BurnTickAccumulator = 0f;
                        state.EntityManager.SetComponentData(target, behaviour);
                        if (wasBurning == false)
                        {
                            Debug.Log($"[Logo Survivor][Progression] Горение применено: {projectile.ValueRO.BurnDamagePerTick} урона/с на {projectile.ValueRO.BurnSeconds:F1} с.");
                        }
                    }
                    if (projectile.ValueRO.ElectricStormRadius > 0f)
                    {
                        int stormDamage = math.max(1, (int)math.ceil(damage * projectile.ValueRO.ElectricStormDamageMultiplier));
                        CreateAreaDamage(ref state, commandBuffer, target, transform.ValueRO.Position, projectile.ValueRO.ElectricStormRadius, stormDamage, true);
                    }
                    hits.Add(new ProjectileHit { Target = target });

                    if (projectile.ValueRO.RicochetRemaining > 0 && TryFindClosestEnemy(ref state, transform.ValueRO.Position, hits, out Entity ricochetTarget))
                    {
                        float3 ricochetPosition = SystemAPI.GetComponent<LocalTransform>(ricochetTarget).Position;
                        projectile.ValueRW.Direction = math.normalizesafe(ricochetPosition - transform.ValueRO.Position, projectile.ValueRO.Direction);
                        projectile.ValueRW.RicochetRemaining--;
                        continue;
                    }

                    bool weakTarget = isBoss == false && (archetype == EnemyArchetype.Normal || archetype == EnemyArchetype.Swarm);
                    if (weakTarget && projectile.ValueRO.PierceRemaining > 0)
                    {
                        projectile.ValueRW.PierceRemaining--;
                        if (projectile.ValueRO.PierceRemaining <= 0)
                        {
                            commandBuffer.DestroyEntity(entity);
                        }
                    }
                    else
                    {
                        commandBuffer.DestroyEntity(entity);
                    }
                }

                if (projectile.ValueRO.RemainingDistance <= 0f)
                {
                    commandBuffer.DestroyEntity(entity);
                }
            }

            foreach ((RefRW<EnemyBehaviourComponent> behaviour, Entity enemy) in SystemAPI.Query<RefRW<EnemyBehaviourComponent>>().WithAll<EnemyTag>().WithEntityAccess())
            {
                if (behaviour.ValueRO.BurnRemaining <= 0f)
                {
                    continue;
                }

                behaviour.ValueRW.BurnRemaining = math.max(0f, behaviour.ValueRO.BurnRemaining - deltaTime);
                behaviour.ValueRW.BurnTickAccumulator += deltaTime;
                if (behaviour.ValueRO.BurnTickAccumulator >= 1f)
                {
                    int tickCount = (int)math.floor(behaviour.ValueRO.BurnTickAccumulator);
                    behaviour.ValueRW.BurnTickAccumulator -= tickCount;
                    CreateDamageRequest(commandBuffer, enemy, behaviour.ValueRO.BurnDamagePerTick * tickCount);
                }
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }

        private Entity GetCollidingTargetAlongSegment(ref SystemState state, float3 start, float3 end, DynamicBuffer<ProjectileHit> hits)
        {
            float3 segment = end - start;
            float segmentLengthSquared = math.lengthsq(segment);
            Entity closestEnemy = Entity.Null;
            float closestProgress = float.MaxValue;

            foreach ((RefRO<LocalTransform> transform, Entity enemy) in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<EnemyTag>()
                .WithNone<CombatDisabledTag>()
                .WithEntityAccess())
            {
                if (WasHit(hits, enemy))
                {
                    continue;
                }

                float progress = segmentLengthSquared <= 0.0001f ? 0f : math.saturate(math.dot(transform.ValueRO.Position - start, segment) / segmentLengthSquared);
                float3 closestPoint = start + segment * progress;
                float2 planarOffset = new(transform.ValueRO.Position.x - closestPoint.x, transform.ValueRO.Position.z - closestPoint.z);
                float collisionRadius = SystemAPI.HasComponent<BossTag>(enemy) ? 1.35f : 0.85f;
                if (math.lengthsq(planarOffset) <= collisionRadius * collisionRadius && progress < closestProgress)
                {
                    closestEnemy = enemy;
                    closestProgress = progress;
                }
            }

            return closestEnemy;
        }

        private bool WasHit(DynamicBuffer<ProjectileHit> hits, Entity target)
        {
            foreach (ProjectileHit hit in hits)
            {
                if (hit.Target == target)
                {
                    return true;
                }
            }

            return false;
        }

        private void CreateAreaDamage(ref SystemState state, EntityCommandBuffer commandBuffer, Entity directTarget, float3 center, float radius, int damage, bool showElectricStormTracer = false)
        {
            foreach ((RefRO<LocalTransform> enemyTransform, Entity enemy) in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<EnemyTag>()
                .WithNone<CombatDisabledTag>()
                .WithEntityAccess())
            {
                if (enemy != directTarget && math.distancesq(enemyTransform.ValueRO.Position, center) <= radius * radius)
                {
                    CreateDamageRequest(commandBuffer, enemy, damage);
                    if (showElectricStormTracer)
                    {
                        CreateTracerEvent(commandBuffer, center, enemyTransform.ValueRO.Position, CombatPresentationColors.ElectricStormTracer);
                    }
                }
            }
        }

        private void CreateChainDamage(ref SystemState state, EntityCommandBuffer commandBuffer, Entity directTarget, float3 center, int count, int damage)
        {
            int remaining = count;
            foreach ((RefRO<LocalTransform> enemyTransform, Entity enemy) in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<EnemyTag>()
                .WithNone<CombatDisabledTag>()
                .WithEntityAccess())
            {
                if (remaining <= 0) break;
                if (enemy != directTarget && math.distancesq(enemyTransform.ValueRO.Position, center) <= 4f * 4f)
                {
                    CreateDamageRequest(commandBuffer, enemy, damage);
                    CreateTracerEvent(commandBuffer, center, enemyTransform.ValueRO.Position, CombatPresentationColors.ChainLightningTracer);
                    remaining--;
                }
            }
        }

        private void CreateTracerEvent(EntityCommandBuffer commandBuffer, float3 start, float3 end, float4 color)
        {
            Entity tracer = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(tracer, new TracerEvent
            {
                Start = start,
                End = end,
                Color = color
            });
        }

        private bool TryFindClosestEnemy(ref SystemState state, float3 origin, DynamicBuffer<ProjectileHit> hits, out Entity result)
        {
            result = Entity.Null;
            float closestDistance = float.MaxValue;
            foreach ((RefRO<LocalTransform> enemyTransform, Entity enemy) in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<EnemyTag>()
                .WithNone<CombatDisabledTag>()
                .WithEntityAccess())
            {
                if (WasHit(hits, enemy)) continue;
                float distance = math.distancesq(enemyTransform.ValueRO.Position, origin);
                if (distance < closestDistance && distance <= 8f * 8f)
                {
                    closestDistance = distance;
                    result = enemy;
                }
            }
            return result != Entity.Null;
        }

        private void CreateDamageRequest(EntityCommandBuffer commandBuffer, Entity target, int amount)
        {
            Entity request = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(request, new DamageRequest { Target = target, Amount = amount, Source = DamageSource.None });
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AttackSystem))]
    [UpdateBefore(typeof(DamageSystem))]
    public partial struct RangedProjectileSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out Entity player) ||
                !SystemAPI.HasComponent<LocalTransform>(player))
            {
                return;
            }

            EntityCommandBuffer commandBuffer = new(Allocator.Temp);
            float deltaTime = SystemAPI.Time.DeltaTime;

            foreach ((RefRW<LocalTransform> transform, RefRW<RangedProjectileComponent> projectile, Entity entity) in
                SystemAPI.Query<RefRW<LocalTransform>, RefRW<RangedProjectileComponent>>().WithEntityAccess())
            {
                projectile.ValueRW.Elapsed += deltaTime;
                float progress = math.saturate(projectile.ValueRO.Elapsed / projectile.ValueRO.Duration);
                float3 position = math.lerp(projectile.ValueRO.Start, projectile.ValueRO.ImpactPoint, progress);
                position.y += 4f * projectile.ValueRO.ArcHeight * progress * (1f - progress);
                transform.ValueRW.Position = position;

                if (progress < 1f)
                {
                    continue;
                }

                LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(player);
                if (math.distancesq(playerTransform.Position, projectile.ValueRO.ImpactPoint) <= projectile.ValueRO.ImpactRadius * projectile.ValueRO.ImpactRadius)
                {
                    Entity request = commandBuffer.CreateEntity();
                    commandBuffer.AddComponent(request, new DamageRequest
                    {
                        Target = player,
                        Amount = projectile.ValueRO.Damage,
                        Source = DamageSource.EnemyRangedProjectile
                    });
                }

                commandBuffer.DestroyEntity(entity);
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }
    }
}
