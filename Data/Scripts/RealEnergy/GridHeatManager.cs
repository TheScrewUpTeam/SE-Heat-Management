using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.Utils;
using System.Linq;
using System.Reflection;

namespace TSUT.HeatManagement
{

    public class GridHeatManager : IGridHeatManager
    {

        private readonly Dictionary<IMyCubeBlock, IHeatBehavior> _heatBehaviors = new Dictionary<IMyCubeBlock, IHeatBehavior>();
        private bool _showDebug = false;

        public GridHeatManager(IMyCubeGrid grid, bool lazy = false)
        {
            foreach (var factory in HeatSession.Api.Registry.GetFactories())
            {
                if (factory == null)
                    continue;

                try
                {
                    factory.CollectHeatBehaviors(grid, this, _heatBehaviors);
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Factory {factory.GetType().Name} threw exception on collect behaviors: {ex}");
                }
            }

            var providers = HeatSession.Api.Registry.GetHeatBehaviorProviders().ToList();
            foreach (var provider in providers)
            {
                if (provider == null)
                    continue;

                try
                {
                    var methodInfo = provider.GetType().GetMethod("ProvideBehaviors");
                    if (methodInfo != null)
                    {
                        List<object> behaviors = methodInfo.Invoke(provider, new object[] { grid });
                        // var behaviors = provider.ProvideBehaviors(grid);
                        MyLog.Default.WriteLine($"[HeatManagement] HeatBehaviorProvider provided {behaviors.Count} behaviors for grid {grid.DisplayName}");
                        // foreach (var kvp in behaviors)
                        // {
                        //     if (kvp.Value != null && !_heatBehaviors.ContainsKey(kvp.Key))
                        //     {
                        //         _heatBehaviors[kvp.Key] = new DelegateHeatBehavior(kvp.Value);
                        //     }
                        // }
                    }
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Provider {provider.GetType().Name} threw exception on provide behaviors: {ex}");
                }
            }

            if (!lazy)
            {
                grid.OnBlockAdded += OnBlockAdded;
                grid.OnBlockRemoved += OnBlockRemoved;
            }
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
                    if (heatBehavior is IMultiBlockHeatBehavior)
                    {
                        var multi = heatBehavior as IMultiBlockHeatBehavior;
                        multi.RemoveBlock(block.FatBlock, this, _heatBehaviors);
                    }
                    else
                    {
                        heatBehavior.Cleanup();
                        _heatBehaviors.Remove(block.FatBlock);
                    }
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
                    HeatBehaviorAttachResult result = factory.OnBlockAdded(block.FatBlock, this);
                    if (result?.Behavior != null)
                    {
                        foreach (var affected in result.AffectedBlocks)
                        {
                            _heatBehaviors[affected] = result.Behavior;
                        }
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
            HashSet<IHeatBehavior> called = new HashSet<IHeatBehavior>();

            // Update heat for each block
            foreach (var kvp in new Dictionary<IMyCubeBlock, IHeatBehavior>(_heatBehaviors))
            {
                if (called.Contains(kvp.Value))
                    continue;
                called.Add(kvp.Value);

                IMyCubeBlock block = kvp.Key;
                IHeatBehavior behavior = kvp.Value;
                try
                {
                    if (behavior is IMultiBlockHeatBehavior)
                    {
                        behavior.SpreadHeat(deltaTime);
                        behavior.ReactOnNewHeat(0f);
                    }
                    else
                    {
                        float heatChange = behavior.GetHeatChange(deltaTime);
                        if (heatChange != 0f)
                        {
                            float newHeat = HeatSession.Api.Utils.ApplyHeatChange(block, heatChange);
                            behavior.ReactOnNewHeat(newHeat);
                        }
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
            HashSet<IHeatBehavior> called = new HashSet<IHeatBehavior>();

            // Spread heat between neighbors
            foreach (var kvp in _heatBehaviors)
            {
                if (called.Contains(kvp.Value))
                    continue;
                called.Add(kvp.Value);

                if (kvp.Value is IMultiBlockHeatBehavior)
                    continue;

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


        public IHeatBehavior TryGetHeatBehaviour(IMyCubeBlock block)
        {
            if (block == null)
                return null;

            IHeatBehavior behavior;
            if (_heatBehaviors.TryGetValue(block, out behavior))
                return behavior;

            return null;
        }

        public List<HeatPipeManager> GetHeatPipeManagers()
        {
            // Return all IHeatBehavior values that are HeatPipeManager
            return _heatBehaviors.Values
                .OfType<HeatPipeManager>()
                .Distinct()
                .ToList();
        }

        public void SetShowDebug(bool flag)
        {
            _showDebug = flag;
        }

        public bool GetShowDebug()
        {
            return _showDebug;
        }

        // WARNING!!! Performance intensive call, use carefully!
        public void UpdateVisuals(float deltaTime)
        {
            if (_showDebug)
            {
                foreach (var behavior in _heatBehaviors.Values)
                {
                    if (behavior is IMultiBlockHeatBehavior)
                    {
                        var multi = behavior as IMultiBlockHeatBehavior;
                        multi.ShowDebugGraph(deltaTime);
                    }
                }
            }
        }

        public bool TryReactOnHeat(IMyCubeBlock block, float heat)
        {
            IHeatBehavior behavior;
            if (_heatBehaviors.TryGetValue(block, out behavior))
            {
                behavior.ReactOnNewHeat(heat);
                return true;
            }
            return false;
        }

        internal bool TryGetBehaviorForBlock(IMyCubeBlock block, out IHeatBehavior behavior)
        {
            if (block == null)
            {
                behavior = null;
                return false;
            }

            return _heatBehaviors.TryGetValue(block, out behavior);
        }

        internal float GetMaxTemperature()
        {
            float maxTemp = float.MinValue;
            foreach (var kvp in _heatBehaviors)
            {
                IMyCubeBlock block = kvp.Key;
                float temp = HeatSession.Api.Utils.GetHeat(block);
                if (temp > maxTemp)
                {
                    maxTemp = temp;
                }
            }
            return maxTemp;
        }
    }

    public class HeatBehaviorAttachResult
    {
        public IHeatBehavior Behavior;
        public List<IMyCubeBlock> AffectedBlocks = new List<IMyCubeBlock>();
    }

    // public class DelegateHeatBehavior : IHeatBehavior
    // {
    //     private readonly ShareableApi.HeatBehaviorLogic _logic;

    //     public DelegateHeatBehavior(ShareableApi.HeatBehaviorLogic logic)
    //     {
    //         _logic = logic;
    //     }

    //     public float GetHeatChange(float deltaTime) => _logic.GetHeatChange?.Invoke(deltaTime) ?? 0f;
    //     public void ReactOnNewHeat(float heat) => _logic.ReactOnNewHeat?.Invoke(heat);
    //     public void SpreadHeat(float deltaTime) => _logic.SpreadHeat?.Invoke(deltaTime);
    //     public void Cleanup() => _logic.Cleanup?.Invoke();
    // }

    public class DelegateHeatBehavior : IHeatBehavior
    {
        private readonly object _logic;

        private readonly MethodInfo _getHeatChangeMethod;
        private readonly MethodInfo _reactOnNewHeatMethod;
        private readonly MethodInfo _spreadHeatMethod;
        private readonly MethodInfo _cleanupMethod;

        public DelegateHeatBehavior(object logic)
        {
            _logic = logic ?? throw new ArgumentNullException(nameof(logic));

            var type = _logic.GetType();
            _getHeatChangeMethod = type.GetMethod("GetHeatChange");
            _reactOnNewHeatMethod = type.GetMethod("ReactOnNewHeat");
            _spreadHeatMethod = type.GetMethod("SpreadHeat");
            _cleanupMethod = type.GetMethod("Cleanup");
        }

        public float GetHeatChange(float deltaTime)
        {
            if (_getHeatChangeMethod == null) return 0f;

            var result = _getHeatChangeMethod.Invoke(_logic, new object[] { deltaTime });
            if (result is float f) return f;
            if (result is double d) return (float)d;
            return 0f;
        }

        public void ReactOnNewHeat(float heat)
        {
            _reactOnNewHeatMethod?.Invoke(_logic, new object[] { heat });
        }

        public void SpreadHeat(float deltaTime)
        {
            _spreadHeatMethod?.Invoke(_logic, new object[] { deltaTime });
        }

        public void Cleanup()
        {
            _cleanupMethod?.Invoke(_logic, null);
        }
    }
}