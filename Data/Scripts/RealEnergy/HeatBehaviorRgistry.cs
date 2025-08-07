using System.Collections.Generic;
using Sandbox.Game.Entities.Cube;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public class HeatBehaviorRegistry : IHeatRegistry
    {
        private readonly List<IHeatBehaviorFactory> _heatBehaviorFactories = new List<IHeatBehaviorFactory>();
        private readonly List<IEventControllerEvent> _eventControllerEvents = new List<IEventControllerEvent>();
        private readonly List<object> _heatBehaviorProviders = new List<object>();

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

        public void RegisterHeatBehaviorProvider(object provider)
        {
            MyLog.Default.WriteLine($"[HeatManagement] Registering HeatBehaviorProvider [{_heatBehaviorProviders.Count}]:  {provider == null}");
            if (provider == null || _heatBehaviorProviders.Contains(provider)) return;
            _heatBehaviorProviders.Add(provider);
            MyLog.Default.WriteLine($"[HeatManagement] HeatBehaviorProvider registered");
        }

        public IEnumerable<object> GetHeatBehaviorProviders()
        {
            return _heatBehaviorProviders;
        }
    }
}