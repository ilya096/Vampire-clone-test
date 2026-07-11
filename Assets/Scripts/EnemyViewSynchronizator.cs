using System;
using System.Collections.Generic;
using System.Text;
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

            var viewPostion = new Vector3(position.x, position.y + _verticalOffset, position.z);
            view = GetEnemyView(position);

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
            agent.Warp(position);
        }

        private void ReturnToPool(Entity enemy)
        {
            if(_views.Remove(enemy, out GameObject view))
            {
                var agent = view.GetComponent<NavMeshAgent>();
                agent.ResetPath();

                view.SetActive(false);
                _pool.Push(view);
            }
        }
    }
}
