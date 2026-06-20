using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public class PlayerView : MonoBehaviour
{
    [SerializeField] private float _offset = .75f;

    private World _world;
    private Entity _entity;
    private EntityManager _entityManager;

    public void Initialize(World world, Entity entity)
    { 
        _world = world;
        _entity = entity;
        _entityManager = _world.EntityManager;
    }

    private void LateUpdate()
    {
        if(_world == null || _world.IsCreated == false || _entity == Entity.Null)
        {
            return;
        }

        if(_entityManager.Exists(_entity) == false || _entityManager.HasComponent<LocalTransform>(_entity) == false)
        {
            return;
        }

        var localTransform = _entityManager.GetComponentData<LocalTransform>(_entity);
        transform.position = new Vector3(
            localTransform.Position.x, 
            localTransform.Position.y + _offset, 
            localTransform.Position.z);

    }
}
