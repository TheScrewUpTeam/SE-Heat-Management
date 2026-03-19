using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.Utils;
using System.Linq;
using Sandbox.ModAPI;
using Sandbox.Game.Entities;

namespace TSUT.HeatManagement
{

    public class GridHeatManager : IGridHeatManager
    {
        private readonly IMyCubeGrid _grid;
        private readonly Dictionary<IMyCubeBlock, IHeatBehavior> _heatBehaviors = new Dictionary<IMyCubeBlock, IHeatBehavior>();
        private bool _showDebug = false;
        private int blockCallCount = 0;
        private int neighborCallCount = 0;
        private float blocksTimeAccumulator = 0f;
        private float neighborsTimeAccumulator = 0f;
        private GridO2Manager _o2manager;

        public GridHeatManager(IMyCubeGrid grid, bool lazy = false)
        {
            _grid = grid;
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
                    MyLog.Default.Warning($"[HeatManagement] Factory {factory.GetType().Name} threw exception on collect behaviors: {ex}");
                }
            }

            MyLog.Default.WriteLine($"[HeatManagement.GridManager] Factories processed...");

            var gridId = grid.EntityId;
            var providers = HeatSession.Api.Registry.GetHeatBehaviorProviders().ToList();
            foreach (var provider in providers)
            {
                if (provider == null)
                    continue;

                try
                {
                    IDictionary<long, IDictionary<string, object>> behaviors = provider(gridId);
                    if (behaviors.Count > 0)
                    {
                        lock (_heatBehaviors)
                        {
                            foreach (var kvp in behaviors)
                            {
                                var blockId = kvp.Key;
                                var behavior = kvp.Value;
                                if (behavior == null)
                                    continue;
                                var block = MyAPIGateway.Entities.GetEntityById(blockId) as MyCubeBlock;
                                if (block != null && !_heatBehaviors.ContainsKey(block))
                                {
                                    _heatBehaviors[block] = new DelegateHeatBehavior(behavior, block);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MyLog.Default.Warning($"[HeatManagement] Provider {provider.GetType().Name} threw exception on provide behaviors: {ex}");
                }
            }

            MyLog.Default.WriteLine($"[HeatManagement.GridManager] Providers processed...");

            if (!lazy)
            {
                grid.OnBlockAdded += OnBlockAdded;
                grid.OnBlockRemoved += OnBlockRemoved;
                grid.OnBlockIntegrityChanged += OnBlockIntegrityChanged;
            }
        }

        private void OnBlockIntegrityChanged(IMySlimBlock block)
        {
            if (block.FatBlock == null)
                return;
            if (block.FatBlock != null && block.FatBlock.IsFunctional)
            {
                OnBlockAdded(block);
            }
            else
            {
                OnBlockRemoved(block);
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

            if (!block.FatBlock.IsFunctional)
            {
                return;
            }

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
                    MyLog.Default.Warning($"[HeatManagement] Factory {factory.GetType().Name} threw exception on block add: {ex}");
                }
            }

            var mappers = HeatSession.Api.Registry.GetHeatMappers();
            foreach (var mapper in mappers)
            {
                if (mapper == null)
                    continue;
                try
                {
                    var logic = mapper(block.FatBlock.EntityId);
                    if (logic == null)
                    {
                        continue;
                    }
                    IHeatBehavior behavior = new DelegateHeatBehavior(logic, block.FatBlock);
                    if (behavior != null)
                    {
                        _heatBehaviors[block.FatBlock] = behavior;
                    }
                }
                catch (Exception ex)
                {
                    MyLog.Default.Warning($"[HeatManagement] Factory {mapper.GetType().Name} threw exception on block add: {ex}");
                }
            }
            HeatSession.Api.Utils.PurgeCaches();
        }

        public void DropAll()
        {
            var blocks = _grid.GetFatBlocks<IMyCubeBlock>();
            foreach (var block in blocks)
            {
                HeatSession.Api.Utils.DropTemperature(block);
            }
            lock (_heatBehaviors)
            {
                foreach (var kvp in _heatBehaviors)
                {
                    var temp = HeatSession.Api.Utils.GetHeat(kvp.Key);
                    kvp.Value.ReactOnNewHeat(temp);
                }
            }
        }

        public void UpdateBlocksTemp(float deltaTime)
        {
            blockCallCount++;
            blocksTimeAccumulator += deltaTime;
            int scale = GetScaleBasedOnBlocksCount();
            if (blockCallCount % scale != 0)
            {
                return;
            }
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
                    if (!(behavior is IMultiBlockHeatBehavior))
                    {
                        float heatChange = behavior.GetHeatChange(blocksTimeAccumulator);
                        if (heatChange != 0f)
                        {
                            float newHeat = HeatSession.Api.Utils.ApplyHeatChange(block, heatChange);
                            behavior.ReactOnNewHeat(newHeat);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MyLog.Default.Warning($"[HeatManagement] Behavior {behavior.GetType().Name} threw exception on update: {ex}");
                }
            }
            blocksTimeAccumulator = 0;
        }

        public int GetScaleBasedOnBlocksCount()
        {
            if (_heatBehaviors.Count <= 50)
                return Config.Instance.UPDATE_INTERVAL_SCALE_50;
            else if (_heatBehaviors.Count <= 100)
                return Config.Instance.UPDATE_INTERVAL_SCALE_100;
            else if (_heatBehaviors.Count <= 400)
                return Config.Instance.UPDATE_INTERVAL_SCALE_400;
            else if (_heatBehaviors.Count <= 1000)
                return Config.Instance.UPDATE_INTERVAL_SCALE_1000;
            else if (_heatBehaviors.Count <= 1500)
                return Config.Instance.UPDATE_INTERVAL_SCALE_1500;
            else if (_heatBehaviors.Count <= 2000)
                return Config.Instance.UPDATE_INTERVAL_SCALE_2000;
            else
                return Config.Instance.UPDATE_INTERVAL_SCALE_ENOURMOUS;
        }

        public int GetTicksTillNextUpdate()
        {
            int scale = GetScaleBasedOnBlocksCount();
            int ticksPerUpdate = Config.Instance.MAIN_UPDATE_INTERVAL_TICKS * scale;
            int ticksSinceLastUpdate = (blockCallCount % scale) * Config.Instance.MAIN_UPDATE_INTERVAL_TICKS;
            return ticksPerUpdate - ticksSinceLastUpdate;
        }

        public void UpdateNeighborsTemp(float deltaTime)
        {
            neighborCallCount++;
            neighborsTimeAccumulator += deltaTime;
            int scale = GetScaleBasedOnBlocksCount();
            // We always update pipe networks, HeatPipeManager has it's own control system
            UpdatePipeNetworks(deltaTime);
            if (neighborCallCount % scale != 0)
            {
                return;
            }
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
                    behavior.SpreadHeat(neighborsTimeAccumulator);
                    behavior.ReactOnNewHeat(HeatSession.Api.Utils.GetHeat(kvp.Key));
                }
                catch (Exception ex)
                {
                    MyLog.Default.Warning($"[HeatManagement] Behavior {behavior.GetType().Name} threw exception on spread heat: {ex}");
                }
            }
            neighborsTimeAccumulator = 0;
        }

        private void UpdatePipeNetworks(float deltaTime)
        {
            HashSet<IHeatBehavior> called = new HashSet<IHeatBehavior>();
            foreach (var kvp in _heatBehaviors)
            {
                if (called.Contains(kvp.Value))
                    continue;
                called.Add(kvp.Value);

                if (kvp.Value is HeatPipeManager)
                {
                    var behavior = kvp.Value;
                    try
                    {
                        behavior.SpreadHeat(deltaTime); // Pass raw deltaTime, not accumulated
                        behavior.ReactOnNewHeat(0f);
                    }
                    catch (Exception ex)
                    {
                        MyLog.Default.Warning($"[HeatManagement] Behavior {behavior.GetType().Name} threw exception on update: {ex}");
                    }
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
                    MyLog.Default.Warning($"[HeatManagement] Behavior {behavior.GetType().Name} threw exception on cleanup: {ex}");
                }
            }
            _heatBehaviors.Clear();
            _grid.OnBlockAdded -= OnBlockAdded;
            _grid.OnBlockRemoved -= OnBlockRemoved;
            _grid.OnBlockIntegrityChanged -= OnBlockIntegrityChanged;
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

        public List<T> GetSpecificManagers<T>()
        {
            // Return all IHeatBehavior values that are HeatPipeManager
            return _heatBehaviors.Values
                .OfType<T>()
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

        public void AttachO2Manager(GridO2Manager manager)
        {
            _o2manager = manager;
        }

        public float ConsumeO2(float amount, float deltaTime, IMyCubeBlock block)
        {
            if (_o2manager == null)
            {
                return amount;
            }
            return _o2manager.ConsumeO2(amount, deltaTime, block);
        }

        public bool HasEnoughO2(float amount, float deltaTime, IMyCubeBlock block)
        {
            if (_o2manager == null)
            {
                return false;
            }
            return _o2manager.HasEnoughO2(amount, deltaTime, block);
        }
    }

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