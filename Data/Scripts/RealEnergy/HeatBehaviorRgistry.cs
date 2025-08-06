using System.Collections.Generic;

namespace TSUT.HeatManagement
{
    public class HeatBehaviorRegistry : IHeatRegistry
    {
        private readonly List<IHeatBehaviorFactory> _heatBehaviorFactories = new List<IHeatBehaviorFactory>();
        private readonly List<IEventControllerEvent> _eventControllerEvents = new List<IEventControllerEvent>();

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
    }
}