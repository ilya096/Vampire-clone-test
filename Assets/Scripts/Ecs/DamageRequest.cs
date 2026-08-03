using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;

namespace Assets.Scripts.Ecs
{
    public struct DamageRequest : IComponentData
    {
        public Entity Target;
        public int Amount;
        public DamageSource Source;
    }
}
