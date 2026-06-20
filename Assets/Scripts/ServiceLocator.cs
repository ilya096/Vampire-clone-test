using System;
using System.Collections.Generic;
using UnityEngine;

public static class ServiceLocator
{
    private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

    public static void Register<TService>(TService service)
    {
        _services[typeof(TService)] = service;
    }

    public static void Unregister<TService>()
    {
        if(_services.ContainsKey(typeof(TService)))
        {
            _services.Remove(typeof(TService));
        }
    }

    public static TService Get<TService>() where TService : class
    {
        if(_services.TryGetValue(typeof(TService), out var service) == false)
        {
            throw new InvalidOperationException($"service {typeof(TService).Name} not found");
        }

        return service as TService;
    }
}
