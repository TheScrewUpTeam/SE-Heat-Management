using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), false)]
    public class GridHeatComponent : MyGameLogicComponent, IGridHeatManager
    {
        private IMyCubeGrid _grid;
        private readonly Dictionary<IMyCubeBlock, IHeatBehavior> _heatBehaviors = new Dictionary<IMyCubeBlock, IHeatBehavior>();
        private readonly List<HeatPipeManager> _pipeNetworks = new List<HeatPipeManager>();
        private readonly HashSet<IMyTerminalBlock> _ownershipSubscribedBlocks = new HashSet<IMyTerminalBlock>();
        private bool _showDebug = false;
        private int _blockCallCount = 0;
        private int _neighborCallCount = 0;
        private float _blocksTimeAccumulator = 0f;
        private float _neighborsTimeAccumulator = 0f;
        private bool _active = false;
        private bool _isInitialized = false;
        private GridO2Manager _o2Manager = null;

        // ===== SE component lifecycle =====

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            var grid = Entity as IMyCubeGrid;
            if (grid == null || HeatSession.IsWheelGrid(grid)) return;
            Initialize(grid);
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
            UpdateVisuals(MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);

            if (!MyAPIGateway.Multiplayer.IsServer || !_active)
                return;

            if (HeatSession._tickCount % Config.Instance.MAIN_UPDATE_INTERVAL_TICKS != 0)
                return;

            float deltaTime = Config.Instance.MAIN_UPDATE_INTERVAL_TICKS * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
            UpdateBlocksTemp(deltaTime);
            UpdateNeighborsTemp(deltaTime);
        }

        public override void Close()
        {
            Cleanup();
            base.Close();
        }

        // ===== Initialization =====

        private void Initialize(IMyCubeGrid grid)
        {
            _grid = grid;

            _grid.OnBlockAdded += OnBlockAdded;
            _grid.OnBlockRemoved += OnBlockRemoved;
            _grid.OnBlockIntegrityChanged += OnBlockIntegrityChanged;

            SubscribeToOwnershipChanges();
            EvaluateActive();

            _o2Manager = Entity?.Components.Get<GridO2Manager>();
            HeatSession.RegisterGridComponent(grid.EntityId, this);
            _isInitialized = true;
            MyLog.Default.WriteLine($"[HeatManagement] GridHeatComponent initialized for {grid.DisplayName} ({grid.EntityId}). Active: {_active}");
        }

        private void SubscribeToOwnershipChanges()
        {
            if (_grid == null) return;
            var terminalBlocks = new List<IMyTerminalBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_grid)?.GetBlocksOfType(terminalBlocks);
            foreach (var block in terminalBlocks)
            {
                if (_ownershipSubscribedBlocks.Add(block))
                    block.OwnershipChanged += OnOwnershipChanged;
            }
        }

        private void OnOwnershipChanged(IMyTerminalBlock block)
        {
            EvaluateActive();
        }

        private void EvaluateActive()
        {
            MyLog.Default.WriteLine($"[HeatManagement] EvaluateActive: grid={_grid?.DisplayName}, LIMIT_TO_PLAYER_GRIDS={Config.Instance?.LIMIT_TO_PLAYER_GRIDS}, currently active={_active}");

            if (Config.Instance == null || !Config.Instance.LIMIT_TO_PLAYER_GRIDS)
            {
                if (!_active) Activate();
                return;
            }

            bool shouldBeActive = IsPlayerGrid();
            MyLog.Default.WriteLine($"[HeatManagement] EvaluateActive: IsPlayerGrid={shouldBeActive}");
            if (shouldBeActive && !_active)
                Activate();
            else if (!shouldBeActive && _active)
                Deactivate();
        }

        private bool IsPlayerGrid()
        {
            if (_grid == null) return false;
            var terminalBlocks = new List<IMyTerminalBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_grid)?.GetBlocks(terminalBlocks);
            foreach (var block in terminalBlocks)
            {
                if (block.OwnerId == 0) continue;
                if (MyAPIGateway.Players.TryGetIdentityId(block.OwnerId) != null)
                    return true;
            }
            return false;
        }

        private void Activate()
        {
            _active = true;
            CollectBehaviors();
            MyLog.Default.WriteLine($"[HeatManagement] GridHeatComponent activated for {_grid?.DisplayName}");
        }

        private void Deactivate()
        {
            _active = false;
            var toRemove = new List<IMyCubeBlock>();
            foreach (var kvp in _heatBehaviors)
            {
                if (kvp.Value is AHeatGameLogicComponent) continue;
                if (!(kvp.Value is HeatPipeManager))
                {
                    try { kvp.Value.Cleanup(); }
                    catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Behavior cleanup on deactivate: {ex}"); }
                }
                toRemove.Add(kvp.Key);
            }
            foreach (var key in toRemove)
                _heatBehaviors.Remove(key);
            foreach (var mgr in _pipeNetworks)
            {
                try { mgr.Cleanup(); }
                catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] PipeManager cleanup on deactivate: {ex}"); }
            }
            _pipeNetworks.Clear();
            MyLog.Default.WriteLine($"[HeatManagement] GridHeatComponent deactivated for {_grid?.DisplayName}");
        }

        private void CollectBehaviors()
        {
            int before = _heatBehaviors.Count;
            MyLog.Default.WriteLine($"[HeatManagement] CollectBehaviors start: grid={_grid?.DisplayName}, factories={HeatSession.Api.Registry.GetFactories().Count}");

            foreach (var factory in HeatSession.Api.Registry.GetFactories())
            {
                if (factory == null) continue;
                int beforeFactory = _heatBehaviors.Count;
                try { factory.CollectHeatBehaviors(_grid, this, _heatBehaviors); }
                catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Factory {factory.GetType().Name} threw on collect: {ex}"); }
                MyLog.Default.WriteLine($"[HeatManagement]   Factory {factory.GetType().Name}: +{_heatBehaviors.Count - beforeFactory} behaviors");
            }

            var gridId = _grid.EntityId;
            foreach (var provider in HeatSession.Api.Registry.GetHeatBehaviorProviders())
            {
                if (provider == null) continue;
                try
                {
                    var behaviors = provider(gridId);
                    if (behaviors == null || behaviors.Count == 0) continue;
                    foreach (var kvp in behaviors)
                    {
                        if (kvp.Value == null) continue;
                        var block = MyAPIGateway.Entities.GetEntityById(kvp.Key) as MyCubeBlock;
                        if (block != null && !_heatBehaviors.ContainsKey(block))
                            _heatBehaviors[block] = new DelegateHeatBehavior(kvp.Value, block);
                    }
                }
                catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Provider threw on collect: {ex}"); }
            }

            CollectPipeNetworks();

            var foreignBlocks = new List<IMyCubeBlock>();
            foreach (var b in _heatBehaviors.Keys)
                if (b.CubeGrid != _grid) foreignBlocks.Add(b);
            foreach (var fb in foreignBlocks)
            {
                try { _heatBehaviors[fb].Cleanup(); } catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Cleanup foreign block: {ex}"); }
                _heatBehaviors.Remove(fb);
            }

            MyLog.Default.WriteLine($"[HeatManagement] CollectBehaviors done: total behaviors={_heatBehaviors.Count} (+{_heatBehaviors.Count - before}), foreign removed={foreignBlocks.Count}");
        }

        // ===== Pipe network management =====

        private void CollectPipeNetworks()
        {
            var slimBlocks = new List<IMySlimBlock>();
            _grid.GetBlocks(slimBlocks);

            var pipeBlocks = slimBlocks
                .Where(s => s.FatBlock != null && HeatPipeManagerFactory.IsPipeCandidate(s.FatBlock))
                .Select(s => s.FatBlock)
                .ToList();

            if (_grid.CustomName.Contains(Config.HeatDebugString))
                MyLog.Default.WriteLine($"[HeatManagement] CollectPipeNetworks: {pipeBlocks.Count} pipe blocks on {_grid.DisplayName}");

            foreach (var pipe in pipeBlocks)
            {
                if (_heatBehaviors.ContainsKey(pipe)) continue;
                OnPipeBlockAdded(pipe);
            }

            if (_grid.CustomName.Contains(Config.HeatDebugString))
                MyLog.Default.WriteLine($"[HeatManagement] CollectPipeNetworks: {_pipeNetworks.Count} networks created");
        }

        private void OnPipeBlockAdded(IMyCubeBlock block)
        {
            bool debug = _grid.CustomName.Contains(Config.HeatDebugString);
            if (debug)
                MyLog.Default.WriteLine($"[HeatManagement] OnPipeBlockAdded: {block.BlockDefinition.SubtypeName} at {block.Position}, networks={_pipeNetworks.Count}");

            var connectedManagers = new List<HeatPipeManager>();
            var neighborNodes = new Dictionary<HeatPipeNode, HeatPipeManager>();
            var connectedNeighbors = HeatPipeManagerFactory.GetConnectedBlocks(block);

            if (debug)
                MyLog.Default.WriteLine($"[HeatManagement] OnPipeBlockAdded: {connectedNeighbors.Count} pipe neighbors");

            foreach (var neighbor in connectedNeighbors)
            {
                foreach (var manager in _pipeNetworks)
                {
                    var node = manager.TryGetNode(neighbor);
                    if (node == null) continue;

                    if (debug)
                        MyLog.Default.WriteLine($"[HeatManagement] OnPipeBlockAdded: neighbor {neighbor.DisplayNameText} in network #{manager.GetHashCode()}");

                    if (!connectedManagers.Contains(manager))
                        connectedManagers.Add(manager);
                    neighborNodes[node] = manager;
                }
            }

            if (debug)
                MyLog.Default.WriteLine($"[HeatManagement] OnPipeBlockAdded: {connectedManagers.Count} connected managers");

            var newNode = new HeatPipeNode { Block = block };

            if (connectedManagers.Count == 0)
            {
                var newManager = new HeatPipeManager(this);
                newManager.TryAddNode(newNode);
                _pipeNetworks.Add(newManager);
                _heatBehaviors[block] = newManager;
                if (debug) MyLog.Default.WriteLine($"[HeatManagement] OnPipeBlockAdded: new isolated network");
                return;
            }

            if (connectedManagers.Count == 1)
            {
                var manager = connectedManagers[0];
                foreach (var kvp in neighborNodes)
                    manager.TryConnectNodes(newNode, kvp.Key);
                _heatBehaviors[block] = manager;
                if (debug) MyLog.Default.WriteLine($"[HeatManagement] OnPipeBlockAdded: extended existing network");
                return;
            }

            // Multiple managers: merge
            var mergedManager = new HeatPipeManager(this);
            foreach (var mgr in connectedManagers)
            {
                foreach (var node in mgr.Nodes)
                {
                    mergedManager.TryAddNode(node);
                    foreach (var edge in node.Connections)
                    {
                        if (edge.A == node && mergedManager.ContainsNode(edge.B))
                            mergedManager.TryConnectNodes(edge.A, edge.B);
                    }
                }
                _pipeNetworks.Remove(mgr);
            }
            mergedManager.TryAddNode(newNode);
            foreach (var kvp in neighborNodes)
                mergedManager.TryConnectNodes(newNode, kvp.Key);

            _pipeNetworks.Add(mergedManager);
            foreach (var node in mergedManager.Nodes)
                _heatBehaviors[node.Block] = mergedManager;

            if (debug)
                MyLog.Default.WriteLine($"[HeatManagement] OnPipeBlockAdded: merged {connectedManagers.Count} networks → {mergedManager.Nodes.Count} nodes");
        }

        private void OnPipeBlockRemoved(IMyCubeBlock block)
        {
            IHeatBehavior behavior;
            if (!_heatBehaviors.TryGetValue(block, out behavior)) return;

            var pipeManager = behavior as HeatPipeManager;
            if (pipeManager == null) return;

            bool debug = _grid?.CustomName.Contains(Config.HeatDebugString) ?? false;
            if (debug)
                MyLog.Default.WriteLine($"[HeatManagement] OnPipeBlockRemoved: {block.DisplayNameText}");

            _pipeNetworks.Remove(pipeManager);
            _heatBehaviors.Remove(block);

            var survivors = pipeManager.RemoveNode(block);

            foreach (var mgr in survivors)
            {
                _pipeNetworks.Add(mgr);
                foreach (var node in mgr.Nodes)
                    _heatBehaviors[node.Block] = mgr;
            }

            if (debug)
                MyLog.Default.WriteLine($"[HeatManagement] OnPipeBlockRemoved: {survivors.Count} survivor networks");
        }

        // ===== Block lifecycle =====

        private void OnBlockIntegrityChanged(IMySlimBlock block)
        {
            if (block?.FatBlock == null) return;
            if (block.FatBlock.IsFunctional)
                OnBlockAdded(block);
            else
                OnBlockRemoved(block);
        }

        private void OnBlockRemoved(IMySlimBlock block)
        {
            if (block?.FatBlock == null) return;
            IHeatBehavior behavior;
            if (!_heatBehaviors.TryGetValue(block.FatBlock, out behavior)) return;

            MyLog.Default.WriteLine($"[HeatManagement] OnBlockRemoved: {block.FatBlock.DisplayNameText} ({block.FatBlock.GetType().Name})");

            if (behavior is HeatPipeManager)
                OnPipeBlockRemoved(block.FatBlock);
            else
            {
                try { behavior.Cleanup(); } catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Cleanup on remove: {ex}"); }
                _heatBehaviors.Remove(block.FatBlock);
            }
            HeatSession.Api.Utils.PurgeCaches();
        }

        private void OnBlockAdded(IMySlimBlock block)
        {
            if (block?.FatBlock == null) return;

            // Subscribe new terminal blocks for ownership tracking regardless of active state
            var termBlock = block.FatBlock as IMyTerminalBlock;
            if (termBlock != null && _ownershipSubscribedBlocks.Add(termBlock))
                termBlock.OwnershipChanged += OnOwnershipChanged;

            if (!_active || _heatBehaviors.ContainsKey(block.FatBlock) || !block.FatBlock.IsFunctional)
            {
                MyLog.Default.WriteLine($"[HeatManagement] OnBlockAdded SKIP: {block.FatBlock.DisplayNameText}, active={_active}, alreadyTracked={_heatBehaviors.ContainsKey(block.FatBlock)}, functional={block.FatBlock.IsFunctional}");
                return;
            }

            if (HeatPipeManagerFactory.IsPipeCandidate(block.FatBlock))
            {
                OnPipeBlockAdded(block.FatBlock);
                HeatSession.Api.Utils.PurgeCaches();
                return;
            }

            int before = _heatBehaviors.Count;
            foreach (var factory in HeatSession.Api.Registry.GetFactories())
            {
                if (factory == null) continue;
                try
                {
                    var result = factory.OnBlockAdded(block.FatBlock, this);
                    if (result?.Behavior != null)
                    {
                        foreach (var affected in result.AffectedBlocks)
                            _heatBehaviors[affected] = result.Behavior;
                    }
                }
                catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Factory {factory.GetType().Name} threw on block add: {ex}"); }
            }

            foreach (var mapper in HeatSession.Api.Registry.GetHeatMappers())
            {
                if (mapper == null) continue;
                try
                {
                    var logic = mapper(block.FatBlock.EntityId);
                    if (logic != null)
                        _heatBehaviors[block.FatBlock] = new DelegateHeatBehavior(logic, block.FatBlock);
                }
                catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Mapper threw on block add: {ex}"); }
            }

            MyLog.Default.WriteLine($"[HeatManagement] OnBlockAdded: {block.FatBlock.DisplayNameText} ({block.FatBlock.GetType().Name}), behavior attached={_heatBehaviors.Count > before}");
            HeatSession.Api.Utils.PurgeCaches();
        }

        // ===== IGridHeatManager =====

        public void Cleanup()
        {
            HeatSession.UnregisterGridComponent(Entity.EntityId);
            foreach (var kvp in _heatBehaviors)
            {
                try { kvp.Value.Cleanup(); }
                catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Behavior {kvp.Value.GetType().Name} threw on cleanup: {ex}"); }
            }
            _heatBehaviors.Clear();
            _pipeNetworks.Clear();

            if (_grid != null)
            {
                _grid.OnBlockAdded -= OnBlockAdded;
                _grid.OnBlockRemoved -= OnBlockRemoved;
                _grid.OnBlockIntegrityChanged -= OnBlockIntegrityChanged;
            }

            foreach (var block in _ownershipSubscribedBlocks)
            {
                try { block.OwnershipChanged -= OnOwnershipChanged; } catch { }
            }
            _ownershipSubscribedBlocks.Clear();
        }

        public IHeatBehavior TryGetHeatBehaviour(IMyCubeBlock block)
        {
            if (block == null) return null;
            IHeatBehavior behavior;
            _heatBehaviors.TryGetValue(block, out behavior);
            return behavior;
        }

        public void UpdateBlocksTemp(float deltaTime)
        {
            if (!_active) return;
            _blockCallCount++;
            _blocksTimeAccumulator += deltaTime;
            int scale = GetScaleBasedOnBlocksCount();
            if (_blockCallCount % scale != 0) return;

            var called = new HashSet<IHeatBehavior>();
            foreach (var kvp in new Dictionary<IMyCubeBlock, IHeatBehavior>(_heatBehaviors))
            {
                if (called.Contains(kvp.Value)) continue;
                called.Add(kvp.Value);
                if (kvp.Value is IMultiBlockHeatBehavior) continue;
                try
                {
                    float heatChange = kvp.Value.GetHeatChange(_blocksTimeAccumulator);
                    if (heatChange != 0f)
                    {
                        float newHeat = HeatSession.Api.Utils.ApplyHeatChange(kvp.Key, heatChange);
                        kvp.Value.ReactOnNewHeat(newHeat);
                    }
                }
                catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Behavior {kvp.Value.GetType().Name} threw on update: {ex}"); }
            }
            _blocksTimeAccumulator = 0;
        }

        public void UpdateNeighborsTemp(float deltaTime)
        {
            if (!_active) return;
            _neighborCallCount++;
            _neighborsTimeAccumulator += deltaTime;
            int scale = GetScaleBasedOnBlocksCount();
            UpdatePipeNetworks(deltaTime);
            if (_neighborCallCount % scale != 0) return;

            Dictionary<IMyCubeBlock, IHeatBehavior> snapshot;
            lock (_heatBehaviors) { snapshot = new Dictionary<IMyCubeBlock, IHeatBehavior>(_heatBehaviors); }

            var called = new HashSet<IHeatBehavior>();
            foreach (var kvp in snapshot)
            {
                if (called.Contains(kvp.Value)) continue;
                called.Add(kvp.Value);
                if (kvp.Value is IMultiBlockHeatBehavior) continue;
                try
                {
                    kvp.Value.SpreadHeat(_neighborsTimeAccumulator);
                    kvp.Value.ReactOnNewHeat(HeatSession.Api.Utils.GetHeat(kvp.Key));
                }
                catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] Behavior {kvp.Value.GetType().Name} threw on spread: {ex}"); }
            }
            _neighborsTimeAccumulator = 0;
        }

        private void UpdatePipeNetworks(float deltaTime)
        {
            var snapshot = new List<HeatPipeManager>(_pipeNetworks);
            foreach (var manager in snapshot)
            {
                try
                {
                    manager.SpreadHeat(deltaTime);
                    manager.ReactOnNewHeat(0f);
                }
                catch (Exception ex) { MyLog.Default.Warning($"[HeatManagement] HeatPipeManager threw on update: {ex}"); }
            }
        }

        public void UpdateVisuals(float deltaTime)
        {
            if (!_showDebug) return;
            Dictionary<IMyCubeBlock, IHeatBehavior> snapshot;
            lock (_heatBehaviors) { snapshot = new Dictionary<IMyCubeBlock, IHeatBehavior>(_heatBehaviors); }
            foreach (var behavior in snapshot.Values)
            {
                if (behavior is IMultiBlockHeatBehavior)
                    (behavior as IMultiBlockHeatBehavior).ShowDebugGraph(deltaTime);
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

        public int GetScaleBasedOnBlocksCount()
        {
            if (_heatBehaviors.Count <= 50)   return Config.Instance.UPDATE_INTERVAL_SCALE_50;
            if (_heatBehaviors.Count <= 100)  return Config.Instance.UPDATE_INTERVAL_SCALE_100;
            if (_heatBehaviors.Count <= 400)  return Config.Instance.UPDATE_INTERVAL_SCALE_400;
            if (_heatBehaviors.Count <= 1000) return Config.Instance.UPDATE_INTERVAL_SCALE_1000;
            if (_heatBehaviors.Count <= 1500) return Config.Instance.UPDATE_INTERVAL_SCALE_1500;
            if (_heatBehaviors.Count <= 2000) return Config.Instance.UPDATE_INTERVAL_SCALE_2000;
            return Config.Instance.UPDATE_INTERVAL_SCALE_ENOURMOUS;
        }

        public int GetTicksTillNextUpdate()
        {
            int scale = GetScaleBasedOnBlocksCount();
            int ticksPerUpdate = Config.Instance.MAIN_UPDATE_INTERVAL_TICKS * scale;
            int ticksSinceLastUpdate = (_blockCallCount % scale) * Config.Instance.MAIN_UPDATE_INTERVAL_TICKS;
            return ticksPerUpdate - ticksSinceLastUpdate;
        }

        public List<HeatPipeManager> GetHeatPipeManagers()
        {
            return new List<HeatPipeManager>(_pipeNetworks);
        }

        public List<T> GetSpecificManagers<T>()
        {
            return _heatBehaviors.Values.OfType<T>().Distinct().ToList();
        }

        public void SetShowDebug(bool flag)
        {
            _showDebug = flag;
        }
        public bool GetShowDebug() { return _showDebug; }

        public float ConsumeO2(float amount, float deltaTime, IMyCubeBlock block)
        {
            return _o2Manager != null ? _o2Manager.ConsumeO2(amount, deltaTime, block) : amount;
        }

        public bool HasEnoughO2(float amount, float deltaTime, IMyCubeBlock block)
        {
            return _o2Manager != null && _o2Manager.HasEnoughO2(amount, deltaTime, block);
        }

        // ===== Phase 1+ registration entry points =====

        public void RegisterBehavior(IMyCubeBlock block, IHeatBehavior behavior)
        {
            if (block != null && behavior != null)
            {
                _heatBehaviors[block] = behavior;
                MyLog.Default.WriteLine($"[HeatManagement] RegisterBehavior: {block.DisplayNameText} ({behavior.GetType().Name}), total={_heatBehaviors.Count}");
            }
        }

        public void UnregisterBehavior(IMyCubeBlock block)
        {
            if (block != null)
            {
                _heatBehaviors.Remove(block);
                MyLog.Default.WriteLine($"[HeatManagement] UnregisterBehavior: {block.DisplayNameText}, total={_heatBehaviors.Count}");
            }
        }

        // ===== Internal helpers =====

        internal bool TryGetBehaviorForBlock(IMyCubeBlock block, out IHeatBehavior behavior)
        {
            behavior = null;
            if (block == null) return false;
            return _heatBehaviors.TryGetValue(block, out behavior);
        }

        internal float GetMaxTemperature()
        {
            float maxTemp = float.MinValue;
            Dictionary<IMyCubeBlock, IHeatBehavior> snapshot;
            lock (_heatBehaviors) { snapshot = new Dictionary<IMyCubeBlock, IHeatBehavior>(_heatBehaviors); }
            foreach (var kvp in snapshot)
            {
                float temp = HeatSession.Api.Utils.GetHeat(kvp.Key);
                if (temp > maxTemp) maxTemp = temp;
            }
            return maxTemp;
        }

        public void DropAll()
        {
            if (_grid == null) return;
            foreach (var block in _grid.GetFatBlocks<IMyCubeBlock>())
                HeatSession.Api.Utils.DropTemperature(block);

            Dictionary<IMyCubeBlock, IHeatBehavior> snapshot;
            lock (_heatBehaviors) { snapshot = new Dictionary<IMyCubeBlock, IHeatBehavior>(_heatBehaviors); }
            foreach (var kvp in snapshot)
                kvp.Value.ReactOnNewHeat(HeatSession.Api.Utils.GetHeat(kvp.Key));
        }

        public void Rebuild()
        {
            Deactivate();
            EvaluateActive();
        }
    }
}
