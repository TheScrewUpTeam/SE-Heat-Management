using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    
    public class GridHeatManager
    {

        private readonly Dictionary<IMyCubeBlock, IHeatBehavior> _heatBehaviors = new Dictionary<IMyCubeBlock, IHeatBehavior>();

        public GridHeatManager(IMyCubeGrid grid)
        {
            foreach (var factory in HeatSession.Api.Registry.GetFactories())
            {
                if (factory == null)
                    continue;

                try
                {
                    factory.CollectHeatBehaviors(grid, _heatBehaviors);
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Factory {factory.GetType().Name} threw exception on collect behaviors: {ex}");
                }
            }

            grid.OnBlockAdded += OnBlockAdded;
            grid.OnBlockRemoved += OnBlockRemoved;
        }

        private void OnBlockRemoved(IMySlimBlock block)
        {
            if (block == null || block.FatBlock == null)
                return;
            if (_heatBehaviors.ContainsKey(block.FatBlock))
            {
                IHeatBehavior heatBehavior = _heatBehaviors[block.FatBlock];
                if (heatBehavior != null)
                {
                    heatBehavior.Cleanup();
                    _heatBehaviors.Remove(block.FatBlock);
                }
            }
            HeatSession.Api.Utils.PurgeCaches();
        }

        private void OnBlockAdded(IMySlimBlock block)
        {
            if (block == null || block.FatBlock == null || _heatBehaviors.ContainsKey(block.FatBlock))
                return;
                
            foreach (var factory in HeatSession.Api.Registry.GetFactories())
            {
                if (factory == null)
                    continue;
                try
                {
                    IHeatBehavior behavior = factory.OnBlockAdded(block.FatBlock);
                    if (behavior != null)
                    {
                        _heatBehaviors[block.FatBlock] = behavior;
                    }
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Factory {factory.GetType().Name} threw exception on block add: {ex}");
                }
            }
            HeatSession.Api.Utils.PurgeCaches();
        }

        public void UpdateBlocksTemp(float deltaTime)
        {
            // Update heat for each block
            foreach (var kvp in new Dictionary<IMyCubeBlock, IHeatBehavior>(_heatBehaviors))
            {
                IMyCubeBlock block = kvp.Key;
                IHeatBehavior behavior = kvp.Value;
                try
                {
                    float heatChange = behavior.GetHeatChange(deltaTime);
                    if (heatChange != 0f)
                    {
                        float newHeat =  HeatSession.Api.Utils.ApplyHeatChange(block, heatChange);
                        behavior.ReactOnNewHeat(newHeat);
                    }
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Behavior {behavior.GetType().Name} threw exception on update: {ex}");
                }
            }
        }

        public void UpdateNeighborsTemp(float deltaTime)
        {
            // Spread heat between neighbors
            foreach (var kvp in _heatBehaviors)
            {
                IHeatBehavior behavior = kvp.Value;
                try
                {
                    behavior.SpreadHeat(deltaTime);
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Behavior {behavior.GetType().Name} threw exception on spread heat: {ex}");
                }
            }
        }

        public void Cleanup()
        {
            // Cleanup all heat behaviors
            foreach (var kvp in _heatBehaviors)
            {
                IHeatBehavior behavior = kvp.Value;
                try
                {
                    behavior.Cleanup();
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Behavior {behavior.GetType().Name} threw exception on cleanup: {ex}");
                }
            }
            _heatBehaviors.Clear();
        }
    }
}