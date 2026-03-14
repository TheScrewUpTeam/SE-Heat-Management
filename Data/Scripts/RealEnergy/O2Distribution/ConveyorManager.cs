using Sandbox.Game;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public class ConveyorManager
    {
        private readonly List<ManagedProducer> producers = new List<ManagedProducer>();
        private readonly List<ManagedStorage> o2Storage = new List<ManagedStorage>();
        private readonly List<ManagedCustom> customBlocks = new List<ManagedCustom>();
        private bool isValid;
        private IMyCubeBlock _referenceBlock;
        private readonly GridO2Manager _gridManager;
        private float _productionUsed = 0f;

        public ConveyorManager(GridO2Manager gridManager)
        {
            _gridManager = gridManager;
            isValid = true;
        }

        private string GetBlockName(IMyCubeBlock block)
        {
            return block.Name;
        }

        public bool IsConveyorConnected(IMyCubeBlock block)
        {
            if (!isValid) return false;

            // Check if the new block is connected to our network (try both directions)
            string refName = GetBlockName(_referenceBlock);
            string blockName = GetBlockName(block);

            bool isConnected = MyVisualScriptLogicProvider.IsConveyorConnected(refName, blockName) ||
                             MyVisualScriptLogicProvider.IsConveyorConnected(blockName, refName);

            return isConnected;
        }

        public bool TryAddBlock(IMyCubeBlock block)
        {
            if (!isValid) return false;

            // If this is our first block, set it as reference and add it
            if (_referenceBlock == null)
            {
                _referenceBlock = block;
            }

            AddBlock(block);
            return true;
        }

        public void Refresh(float deltaTime)
        {
            _productionUsed = 0f;
        }

        public float Consume(float o2amount, float deltaTime)
        {
            var produced = CalculateO2Production(deltaTime);
            var availableProduction = CalculateO2Production(deltaTime) - _productionUsed;
            if (o2amount < availableProduction)
            {
                _productionUsed += o2amount;
                return 0f;
            } else
            {
                o2amount -= availableProduction;
                _productionUsed = produced;
            }
            foreach (var storage in o2Storage)
            {
                if (o2amount <= 0)
                    break;

                float currentStorage = storage.GetCurrentO2Storage();
                if (currentStorage <= 0)
                    continue;

                float amountToConsume = o2amount > currentStorage ? currentStorage : o2amount;
                storage.ConsumeO2(amountToConsume);
                o2amount -= amountToConsume;
            }
            return o2amount;
        }

        internal bool HasEnough(float amount, float deltaTime)
        {
            var available = CalculateO2Production(deltaTime) - _productionUsed + CalculateO2Storage();
            return available >= amount;
        }

        private float CalculateO2Production(float deltaTime)
        {
            return producers.Where(p => p != null && p.IsWorking)
                          .Sum(p => p.GetCurrentO2Production(deltaTime));
        }

        private float CalculateO2Storage()
        {
            return o2Storage.Where(p => p != null && p.IsWorking)
                          .Sum(p => p.GetCurrentO2Storage());
        }

        public void AddBlock(IMyCubeBlock block)
        {
            if (!isValid) return;

            var terminalBlock = block as IMyTerminalBlock;
            if (terminalBlock == null) return; // For now, we still need terminal blocks for functionality

            var generator = terminalBlock as IMyGasGenerator;
            if (generator != null)
            {
                var producer = _gridManager.GetOrCreateProducer(generator);
                if (!producers.Contains(producer))
                {
                    producers.Add(producer);
                }
                return;
            }

            var vent = terminalBlock as IMyAirVent;
            if (vent != null)
            {
                var producer = _gridManager.GetOrCreateProducer(vent);
                if (!producers.Contains(producer))
                {
                    producers.Add(producer);
                }
                return;
            }

            var farm = terminalBlock as IMyOxygenFarm;
            if (farm != null)
            {
                var producer = _gridManager.GetOrCreateProducer(farm);
                if (!producers.Contains(producer))
                {
                    producers.Add(producer);
                }
                return;
            }

            var tank = terminalBlock as IMyGasTank;
            if (tank != null && (tank.BlockDefinition.SubtypeName.Contains("Oxygen") || tank.BlockDefinition.SubtypeName == ""))
            {
                var storage = _gridManager.GetOrCreateStorage(tank);
                if (!o2Storage.Contains(storage))
                {
                    o2Storage.Add(storage);
                }
                return;
            }

            var custom = _gridManager.GetOrCreateCustom(terminalBlock);
            if (!customBlocks.Contains(custom))
            {
                customBlocks.Add(custom);
            }
        }

        public void RemoveBlock(IMyCubeBlock block)
        {
            if (!isValid) return;

            var terminalBlock = block as IMyTerminalBlock;
            if (terminalBlock == null) return; // For now, we still need terminal blocks for functionality

            if (terminalBlock is IMyGasGenerator)
            {
                producers.RemoveAll(p => p.Block == terminalBlock);
                return;
            }

            if (terminalBlock is IMyAirVent)
            {
                producers.RemoveAll(p => p.Block == terminalBlock);
                return;
            }

            if (terminalBlock is IMyOxygenFarm)
            {
                producers.RemoveAll(p => p.Block == terminalBlock);
                return;
            }

            var tank = terminalBlock as IMyGasTank;
            if (tank != null && tank.BlockDefinition.SubtypeName.Contains("Oxygen"))
            {
                o2Storage.RemoveAll(t => t.Block == terminalBlock);
                return;
            }

            if (terminalBlock is IManagedCustom)
            {
                var customsToRemove = customBlocks.Where(c => c.Block == terminalBlock).ToList();
                foreach (var custom in customsToRemove)
                {
                    if (custom != null)
                    {
                        custom.Dismiss();
                    }
                }
                customBlocks.RemoveAll(c => c.Block == terminalBlock);
                return;
            }

            // If this was our reference block, pick a new one if available
            if (block == _referenceBlock)
            {
                _referenceBlock = GetAnyRemainingBlock();
            }
            // MyAPIGateway.Utilities.ShowMessage("O2Link", $"Block removed: {terminalBlock?.CustomName ?? block.DisplayNameText}, Consumers: {consumers.Count}");
        }

        private IMyCubeBlock GetAnyRemainingBlock()
        {
            return producers.FirstOrDefault()?.Block ??
                   o2Storage.FirstOrDefault()?.Block ??
                   customBlocks.FirstOrDefault()?.Block;
        }

        public class NetworkSplitResult
        {
            public bool IsEmpty { get; set; }
            public bool IsSplit { get; set; }
            public List<IMyCubeBlock> DisconnectedBlocks { get; set; }

            public NetworkSplitResult()
            {
                IsEmpty = false;
                IsSplit = false;
                DisconnectedBlocks = new List<IMyCubeBlock>();
            }
        }

        public NetworkSplitResult CheckNetworkIntegrity()
        {
            if (!isValid || _referenceBlock == null)
                return new NetworkSplitResult() { IsEmpty = true };

            var result = new NetworkSplitResult();
            var allBlocks = GetAllBlocks();

            // If we only have 0-1 blocks, no split is possible
            if (allBlocks.Count <= 1)
            {
                result.IsEmpty = true;
                return result;
            }

            foreach (var block in allBlocks)
            {
                if (block == null)
                    continue;

                // Skip reference block and already identified disconnected blocks
                if (block == _referenceBlock || result.DisconnectedBlocks.Contains(block))
                    continue;

                bool isConnected = MyVisualScriptLogicProvider.IsConveyorConnected(_referenceBlock.Name, block.Name) ||
                                 MyVisualScriptLogicProvider.IsConveyorConnected(block.Name, _referenceBlock.Name);

                if (!isConnected)
                {
                    result.IsSplit = true;
                    result.DisconnectedBlocks.Add(block);
                }
            }

            return result;
        }

        private List<IMyCubeBlock> GetAllBlocks()
        {
            var blocks = new List<IMyCubeBlock>();
            lock (producers)
            {
                blocks.AddRange(producers.Where(b => b != null).Select(p => p.Block));
            }
            lock (o2Storage)
            {
                blocks.AddRange(o2Storage.Where(b => b != null).Select(s => s.Block));
            }
            lock (customBlocks)
            {
                blocks.AddRange(customBlocks.Where(b => b != null).Select(c => c.Block));
            }
            return blocks;
        }

        public void Invalidate()
        {
            isValid = false;
            producers.Clear();
            o2Storage.Clear();
            customBlocks.Clear();
        }

        public bool IsValid => isValid;
    }
}