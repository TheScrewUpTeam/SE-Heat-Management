using System.Collections.Generic;
using Sandbox.ModAPI;

namespace TSUT.HeatManagement
{
    public class HeatBehaviorRegistry: IHeatRegistry
    {
        private readonly List<IHeatBehaviorFactory> _heatBehaviorFactories = new List<IHeatBehaviorFactory>();

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
    }
}