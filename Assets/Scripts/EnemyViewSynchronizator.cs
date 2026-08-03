using System;
using System.Collections.Generic;
using System.Text;
using Assets.Scripts.Ecs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;

namespace Assets.Scripts
{
    class EnemyViewSynchronizator : MonoBehaviour
    {
        [SerializeField] private GameObject _enemyPrefab;
        [SerializeField] private float _verticalOffset = .5f;
        [SerializeField] private float _navMeshSampleDistance = 2f;

        private readonly Dictionary<Entity, GameObject> _views = new();
        private readonly Stack<GameObject> _pool = new();

        public NavMeshAgent CreateEnemyView(Entity enemy, Vector3 position)
        {
            if(_views.TryGetValue(enemy, out GameObject view))
            {
                return view.GetComponent<NavMeshAgent>();
            }

            Vector3 viewPosition = new(position.x, position.y + _verticalOffset, position.z);
            view = GetEnemyView(viewPosition);

            _views.Add(enemy, view);

            return view.GetComponent<NavMeshAgent>();
        }

        private GameObject GetEnemyView(Vector3 position)
        {
            if(_pool.TryPop(out GameObject view))
            {
                view.SetActive(true);
                SetPostion(view, position);

                return view;
            }

            view = Instantiate(_enemyPrefab, position, Quaternion.identity);
            SetPostion(view, position);

            return view;
        }

        private void SetPostion(GameObject view, Vector3 position)
        {
            view.transform.rotation = Quaternion.identity;

            var agent = view.GetComponent<NavMeshAgent>();
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, _navMeshSampleDistance, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        public bool TryPlaceOnNavMesh(NavMeshAgent agent, Vector3 position)
        {
            if (agent.isOnNavMesh)
            {
                return true;
            }

            if (NavMesh.SamplePosition(position, out NavMeshHit hit, _navMeshSampleDistance, NavMesh.AllAreas) == false)
            {
                return false;
            }

            return agent.Warp(hit.position);
        }

        public void ConfigureEnemyView(Entity enemy, EnemyArchetype archetype)
        {
            if (_views.TryGetValue(enemy, out GameObject view) == false)
            {
                return;
            }

            var (color, scale) = archetype switch
            {
                EnemyArchetype.Swarm => (new Color(1f, 0.8f, 0.15f), 0.7f),
                EnemyArchetype.Heavy => (new Color(0.85f, 0.2f, 0.2f), 1.4f),
                EnemyArchetype.Ranged => (new Color(0.2f, 0.8f, 1f), 0.9f),
                _ => (new Color(0.8f, 0.8f, 0.8f), 1f)
            };

            view.transform.localScale = Vector3.one * scale;
            foreach (var renderer in view.GetComponentsInChildren<Renderer>())
            {
                renderer.material.color = color;
            }
        }

        public void ReturnToPool(Entity enemy)
        {
            if(_views.Remove(enemy, out GameObject view))
            {
                var agent = view.GetComponent<NavMeshAgent>();
                if (agent.isOnNavMesh)
                {
                    agent.ResetPath();
                }

                view.SetActive(false);
                _pool.Push(view);
            }
        }
    }
}
