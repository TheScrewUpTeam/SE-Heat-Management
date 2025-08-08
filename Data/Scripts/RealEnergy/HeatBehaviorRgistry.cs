using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Sandbox.Game.Entities.Cube;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public class HeatBehaviorRegistry : IHeatRegistry
    {
        private readonly List<IHeatBehaviorFactory> _heatBehaviorFactories = new List<IHeatBehaviorFactory>();
        private readonly List<IEventControllerEvent> _eventControllerEvents = new List<IEventControllerEvent>();
        private readonly List<Func<long, IDictionary<long, IDictionary<string, object>>>> _heatBehaviorProviders = new List<Func<long, IDictionary<long, IDictionary<string, object>>>>();
        private readonly List<Func<long, IDictionary<string, object>>> _heatMappers = new List<Func<long, IDictionary<string, object>>>();

        public void RegisterHeatBehaviorFactory(IHeatBehaviorFactory factory)
        {
            if (factory == null || _heatBehaviorFactories.Contains(factory)) return;
            _heatBehaviorFactories.Add(factory);
            _heatBehaviorFactories.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public IReadOnlyList<IHeatBehaviorFactory> GetFactories()
        {
            return _heatBehaviorFactories.AsReadOnly();
        }

        public void RegisterEventControllerEvent(IEventControllerEvent eventControllerEvent)
        {
            if (eventControllerEvent == null || _eventControllerEvents.Contains(eventControllerEvent)) return;
            _eventControllerEvents.Add(eventControllerEvent);
        }

        public IReadOnlyList<IEventControllerEvent> GetEventControllerEvents()
        {
            return _eventControllerEvents.AsReadOnly();
        }

        public void RemoveEventControllerEvent(IEventControllerEvent eventControllerEvent)
        {
            if (eventControllerEvent == null) return;
            if (_eventControllerEvents.Contains(eventControllerEvent))
            {
                _eventControllerEvents.Remove(eventControllerEvent);
            }
        }

        public void RegisterHeatBehaviorProvider(Func<long, IDictionary<long, IDictionary<string, object>>> provider)
        {
            if (provider == null || _heatBehaviorProviders.Contains(provider)) return;
            _heatBehaviorProviders.Add(provider);
        }

        public IEnumerable<Func<long, IDictionary<long, IDictionary<string, object>>>> GetHeatBehaviorProviders()
        {
            return _heatBehaviorProviders;
        }

        public void RegisterHeatMapper(Func<long, IDictionary<string, object>> mapper)
        {
            if (mapper == null || _heatMappers.Contains(mapper)) return;
            _heatMappers.Add(mapper);
        }

        public IEnumerable<Func<long, IDictionary<string, object>>> GetHeatMappers() {
            return _heatMappers;
        }
    }
}