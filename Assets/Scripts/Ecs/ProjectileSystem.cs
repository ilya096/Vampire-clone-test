using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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

                Entity target = GetCollidingEnemyAlongSegment(ref state, startPosition, endPosition, hits);
                if (target != Entity.Null)
                {
                    int damage = projectile.ValueRO.Damage;
                    EnemyArchetype archetype = SystemAPI.GetComponent<EnemyArchetypeComponent>(target).Value;
                    if (projectile.ValueRO.DoubleDamageAgainstHeavy && archetype == EnemyArchetype.Heavy)
                    {
                        damage *= 2;
                    }

                    CreateDamageRequest(commandBuffer, target, damage);
                    if (projectile.ValueRO.ExplosionRadius > 0f)
                    {
                        CreateAreaDamage(ref state, commandBuffer, target, transform.ValueRO.Position, projectile.ValueRO.ExplosionRadius, damage);
                    }
                    if (projectile.ValueRO.SlowSeconds > 0f)
                    {
                        EnemyBehaviourComponent behaviour = SystemAPI.GetComponent<EnemyBehaviourComponent>(target);
                        behaviour.SlowRemaining = math.max(behaviour.SlowRemaining, projectile.ValueRO.SlowSeconds);
                        state.EntityManager.SetComponentData(target, behaviour);
                    }
                    if (projectile.ValueRO.ChainLightningRemaining > 0)
                    {
                        CreateChainDamage(ref state, commandBuffer, target, transform.ValueRO.Position, projectile.ValueRO.ChainLightningRemaining, damage);
                    }
                    hits.Add(new ProjectileHit { Target = target });

                    if (projectile.ValueRO.RicochetRemaining > 0 && TryFindClosestEnemy(ref state, transform.ValueRO.Position, hits, out Entity ricochetTarget))
                    {
                        float3 ricochetPosition = SystemAPI.GetComponent<LocalTransform>(ricochetTarget).Position;
                        projectile.ValueRW.Direction = math.normalizesafe(ricochetPosition - transform.ValueRO.Position, projectile.ValueRO.Direction);
                        projectile.ValueRW.RicochetRemaining--;
                        continue;
                    }

                    bool weakTarget = archetype == EnemyArchetype.Normal || archetype == EnemyArchetype.Swarm;
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

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }

        private Entity GetCollidingEnemyAlongSegment(ref SystemState state, float3 start, float3 end, DynamicBuffer<ProjectileHit> hits)
        {
            float3 segment = end - start;
            float segmentLengthSquared = math.lengthsq(segment);
            Entity closestEnemy = Entity.Null;
            float closestProgress = float.MaxValue;

            foreach ((RefRO<LocalTransform> transform, Entity enemy) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<EnemyTag>().WithEntityAccess())
            {
                if (WasHit(hits, enemy))
                {
                    continue;
                }

                float progress = segmentLengthSquared <= 0.0001f ? 0f : math.saturate(math.dot(transform.ValueRO.Position - start, segment) / segmentLengthSquared);
                float3 closestPoint = start + segment * progress;
                if (math.distancesq(transform.ValueRO.Position, closestPoint) <= 0.55f * 0.55f && progress < closestProgress)
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

        private void CreateAreaDamage(ref SystemState state, EntityCommandBuffer commandBuffer, Entity directTarget, float3 center, float radius, int damage)
        {
            foreach ((RefRO<LocalTransform> enemyTransform, Entity enemy) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<EnemyTag>().WithEntityAccess())
            {
                if (enemy != directTarget && math.distancesq(enemyTransform.ValueRO.Position, center) <= radius * radius)
                {
                    CreateDamageRequest(commandBuffer, enemy, damage);
                }
            }
        }

        private void CreateChainDamage(ref SystemState state, EntityCommandBuffer commandBuffer, Entity directTarget, float3 center, int count, int damage)
        {
            int remaining = count;
            foreach ((RefRO<LocalTransform> enemyTransform, Entity enemy) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<EnemyTag>().WithEntityAccess())
            {
                if (remaining <= 0) break;
                if (enemy != directTarget && math.distancesq(enemyTransform.ValueRO.Position, center) <= 4f * 4f)
                {
                    CreateDamageRequest(commandBuffer, enemy, damage);
                    remaining--;
                }
            }
        }

        private bool TryFindClosestEnemy(ref SystemState state, float3 origin, DynamicBuffer<ProjectileHit> hits, out Entity result)
        {
            result = Entity.Null;
            float closestDistance = float.MaxValue;
            foreach ((RefRO<LocalTransform> enemyTransform, Entity enemy) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<EnemyTag>().WithEntityAccess())
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
            Entity player = SystemAPI.GetSingletonEntity<PlayerTag>();
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
