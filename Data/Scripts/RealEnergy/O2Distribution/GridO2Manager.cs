using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using System.Linq;
using VRage.Game.Components;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]
    public class GridO2Manager : MyGameLogicComponent
    {
        private IMyCubeGrid _grid;
        private readonly Dictionary<IMyCubeBlock, ConveyorManager> blockToManager = new Dictionary<IMyCubeBlock, ConveyorManager>();
        private readonly Dictionary<IMyCubeBlock, IManagedBlock> managedBlocks = new Dictionary<IMyCubeBlock, IManagedBlock>();
        private bool _isInitialized = false;
        private int updateCounter = 0;
        private int scheduledProcess = 0;
        private int lastUpdate = 0;
        private readonly List<IMyCubeBlock> blocksToProcess = new List<IMyCubeBlock>();

        public ManagedProducer GetOrCreateProducer(IMyTerminalBlock block)
        {
            IManagedBlock existing;
            if (managedBlocks.TryGetValue(block, out existing))
            {
                return existing as ManagedProducer;
            }

            var producer = new ManagedProducer(block);
            managedBlocks[block] = producer;
            return producer;
        }

        public ManagedStorage GetOrCreateStorage(IMyGasTank block)
        {
            IManagedBlock existing;
            if (managedBlocks.TryGetValue(block, out existing))
            {
                return existing as ManagedStorage;
            }

            var storage = new ManagedStorage(block);
            managedBlocks[block] = storage;
            return storage;
        }

        public ManagedCustom GetOrCreateCustom(IMyCubeBlock block)
        {
            IManagedBlock existing;
            if (managedBlocks.TryGetValue(block, out existing))
            {
                return existing as ManagedCustom;
            }

            var custom = new ManagedCustom(block);
            managedBlocks[block] = custom;
            return custom;
        }

        public void ReleaseManagedBlock(IMyCubeBlock block)
        {
            IManagedBlock managed;
            if (managedBlocks.TryGetValue(block, out managed))
            {
                managed.Dismiss();
                managedBlocks.Remove(block);
            }
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void Close()
        {
            Invalidate();
            base.Close();
        }

        private void Initialize(IMyCubeGrid grid)
        {
            _grid = grid;

            _grid.OnBlockAdded += OnBlockAdded;
            _grid.OnBlockRemoved += OnBlockRemoved;

            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_grid);
            if (terminalSystem == null)
                return;

            var generators = new List<IMyTerminalBlock>();
            var tanks = new List<IMyTerminalBlock>();
            var thrusters = new List<IMyTerminalBlock>();
            var engines = new List<IMyTerminalBlock>();
            var vents = new List<IMyTerminalBlock>();
            var farms = new List<IMyTerminalBlock>();

            // Get each block type separately
            terminalSystem.GetBlocksOfType<IMyGasGenerator>(generators);
            terminalSystem.GetBlocksOfType<IMyAirVent>(vents);
            terminalSystem.GetBlocksOfType<IMyOxygenFarm>(farms);
            terminalSystem.GetBlocksOfType<IMyGasTank>(tanks/*, b => b.BlockDefinition.SubtypeName.Contains("Oxygen") || b.BlockDefinition.SubtypeName == ""*/);
            terminalSystem.GetBlocksOfType<IMyThrust>(thrusters, b => b.BlockDefinition.SubtypeName.Contains("HydrogenThrust"));
            terminalSystem.GetBlocksOfType<IMyPowerProducer>(engines, b => b.BlockDefinition.SubtypeName.Contains("HydrogenEngine"));

            // Combine all blocks into one list
            var relevantBlocks = new List<IMyTerminalBlock>();
            relevantBlocks.AddRange(generators);
            relevantBlocks.AddRange(vents);
            relevantBlocks.AddRange(farms);
            relevantBlocks.AddRange(tanks);
            relevantBlocks.AddRange(thrusters);
            relevantBlocks.AddRange(engines);

            // Create initial conveyor networks
            foreach (var block in relevantBlocks)
            {
                OnBlockAdded(block.SlimBlock);
            }
            
            _isInitialized = true;

            HeatSession.AttachO2GridManager(this, _grid);
        }

        public override void UpdateAfterSimulation()
        {
            if (HeatSession.IsWheelGrid(Entity as IMyCubeGrid))
            {
                return;
            }
            if (!_isInitialized)
            {
                Initialize(Entity as IMyCubeGrid);
                return;
            }
            if (scheduledProcess > 0 && updateCounter >= scheduledProcess)
            {
                ProcessScheduledBlocks();
                blocksToProcess.Clear();
                scheduledProcess = 0;
            }
            updateCounter++;
            if (updateCounter % Config.Instance.MAIN_UPDATE_INTERVAL_TICKS != 0) return;
            var deltaTime = (updateCounter - lastUpdate) * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
            Update(deltaTime);
            lastUpdate = updateCounter;
        }

        private void OnBlockAdded(IMySlimBlock block)
        {
            if (block?.FatBlock == null)
                return;

            var cubeBlock = block.FatBlock;
            // Processing should be postponed to let the conveyors initialize
            scheduledProcess = updateCounter + 20;
            blocksToProcess.Add(cubeBlock);
        }

        private void ProcessScheduledBlocks()
        {
            if (!_isInitialized) return;
            foreach (var cubeBlock in blocksToProcess)
            {
                if (blockToManager.ContainsKey(cubeBlock))
                    continue;

                // Try to add to existing networks first
                List<ConveyorManager> connectedManagers = new List<ConveyorManager>();

                foreach (var manager in blockToManager.Values.Distinct())
                {
                    if (manager.IsConveyorConnected(cubeBlock))
                    {
                        connectedManagers.Add(manager);
                    }
                }

                // MyAPIGateway.Utilities.ShowMessage("O2Link", $"Adding block: {cubeBlock.DisplayNameText}, Belongs to {connectedManagers.Count} networks");

                if (connectedManagers.Count == 0)
                {
                    // No existing networks found, create new one
                    var newManager = new ConveyorManager(this);
                    newManager.TryAddBlock(cubeBlock);
                    blockToManager[cubeBlock] = newManager;
                    // MyAPIGateway.Utilities.ShowMessage("O2Link", $" -> New network created, Networks Total: {blockToManager.Values.Distinct().Count()}");
                }
                else if (connectedManagers.Count == 1)
                {
                    // Add to single existing network
                    connectedManagers[0].TryAddBlock(cubeBlock);
                    blockToManager[cubeBlock] = connectedManagers[0];
                    // MyAPIGateway.Utilities.ShowMessage("O2Link", $" -> Added to existing network, Networks Total: {blockToManager.Values.Distinct().Count()}");
                }
                else
                {
                    // Multiple networks found, need to merge them
                    var targetManager = connectedManagers[0];
                    targetManager.TryAddBlock(cubeBlock);
                    blockToManager[cubeBlock] = targetManager;

                    // Merge other networks into the target
                    foreach (var manager in connectedManagers.Skip(1))
                    {
                        MergeNetworks(manager, targetManager);
                    }
                    // MyAPIGateway.Utilities.ShowMessage("O2Link", $" -> merged {connectedManagers.Count} networks, Networks Total: {blockToManager.Values.Distinct().Count()}");
                }
            }
            // MyAPIGateway.Utilities.ShowMessage("O2Link", $"Blocks added: {blocksToProcess.Count}, Conveyor Networks: {blockToManager.Values.Distinct().Count()}");
        }

        private void OnBlockRemoved(IMySlimBlock block)
        {
            if (!_isInitialized || block?.FatBlock == null)
                return;

            var cubeBlock = block.FatBlock;
            if (!blockToManager.ContainsKey(cubeBlock))
                return;
            
            var oldManager = blockToManager[cubeBlock];

            var terminalBlock = cubeBlock as IMyTerminalBlock;
            blockToManager.Remove(cubeBlock);
            
            if (terminalBlock != null)
            {
                oldManager.RemoveBlock(terminalBlock);
            }

            // Check if network needs to be split
            CheckNetworkSplit(oldManager);
            // MyAPIGateway.Utilities.ShowMessage("O2Link", $"Block removed: {terminalBlock?.CustomName ?? cubeBlock.DisplayNameText}, Conveyor Networks: {blockToManager.Values.Distinct().Count()}");
            ReleaseManagedBlock(cubeBlock);
        }

        private void CheckNetworkSplit(ConveyorManager manager)
        {
            var splitResult = manager.CheckNetworkIntegrity();

            if (!splitResult.IsSplit)
                return;

            // Create a new network for disconnected blocks
            var newManager = new ConveyorManager(this);

            // MyAPIGateway.Utilities.ShowMessage("O2Link", $"Split found. {splitResult.DisconnectedBlocks.Count} blocks disconnected.");

            // Move disconnected blocks to the new network
            foreach (var block in splitResult.DisconnectedBlocks)
            {
                // MyAPIGateway.Utilities.ShowMessage("O2Link", $" -> Moving block: {block.DisplayNameText} to new network.");
                blockToManager[block] = newManager;
                newManager.TryAddBlock(block);
                manager.RemoveBlock(block);
            }
        }

        private void MergeNetworks(ConveyorManager source, ConveyorManager target)
        {
            // Update block-to-manager mapping
            foreach (var kvp in blockToManager.ToList())
            {
                if (kvp.Value == source)
                {
                    source.RemoveBlock(kvp.Key as IMyTerminalBlock);
                    target.TryAddBlock(kvp.Key as IMyTerminalBlock);
                    blockToManager[kvp.Key] = target;
                }
            }

            // Let the source manager clean up
            source.Invalidate();
        }

        public void Update(float deltaTime)
        {
            if (!_isInitialized) return;

            foreach (var manager in blockToManager.Values.Distinct())
            {
                if (manager.IsValid)
                {
                    manager.Refresh(deltaTime);
                }
            }
        }

        public void Invalidate()
        {
            if (!_isInitialized) return;

            _grid.OnBlockAdded -= OnBlockAdded;
            _grid.OnBlockRemoved -= OnBlockRemoved;

            foreach (var manager in blockToManager.Values)
            {
                manager.Invalidate();
            }
            blockToManager.Clear();
        }

        internal float ConsumeO2(float amount, float deltaTime, IMyCubeBlock block)
        {
            ConveyorManager manager;
            if (!blockToManager.TryGetValue(block, out manager))
            {
                return amount;
            }
            return manager.Consume(amount, deltaTime);
        }

        internal bool HasEnoughO2(float amount, float deltaTime, IMyCubeBlock block)
        {
            ConveyorManager manager;
            if (!blockToManager.TryGetValue(block, out manager))
            {
                return false;
            }
            return manager.HasEnough(amount, deltaTime);
        }

        public bool IsValid => _isInitialized;
        public IMyCubeGrid Grid => _grid;
    }
}