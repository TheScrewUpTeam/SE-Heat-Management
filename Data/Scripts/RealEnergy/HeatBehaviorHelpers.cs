using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public class HeatBehaviorAttachResult
    {
        public IHeatBehavior Behavior;
        public List<IMyCubeBlock> AffectedBlocks = new List<IMyCubeBlock>();
    }

    public class DelegateHeatBehavior : IHeatBehavior
    {
        private readonly IDictionary<string, object> _logic;
        private IMyCubeBlock _block;

        public IMyCubeBlock Block => _block;

        public DelegateHeatBehavior(IDictionary<string, object> logic, IMyCubeBlock block = null)
        {
            _logic = logic;
            _block = block;
        }

        public float GetHeatChange(float deltaTime)
        {
            object getHeatChange;
            if (_logic != null && _logic.TryGetValue("GetHeatChange", out getHeatChange) && getHeatChange is Func<float, float>)
                return (getHeatChange as Func<float, float>)(deltaTime);
            return 0f;
        }

        public void ReactOnNewHeat(float heat)
        {
            object reactOnNewHeat;
            if (_logic != null && _logic.TryGetValue("ReactOnNewHeat", out reactOnNewHeat) && reactOnNewHeat is Action<float>)
                (reactOnNewHeat as Action<float>)(heat);
        }

        public void SpreadHeat(float deltaTime)
        {
            object spreadHeat;
            if (_logic != null && _logic.TryGetValue("SpreadHeat", out spreadHeat) && spreadHeat is Action<float>)
                (spreadHeat as Action<float>)(deltaTime);
        }

        public void Cleanup()
        {
            object cleanup;
            if (_logic != null && _logic.TryGetValue("Cleanup", out cleanup) && cleanup is Action)
                (cleanup as Action)();
        }
    }
}
