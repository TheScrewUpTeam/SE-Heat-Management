using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public class ConveyorNetworkBehaviorFactory : IHeatBehaviorFactory
    {
        public int Priority => 25; // Run after vent managers

        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager,
                                        IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            if (grid == null || manager == null || behaviorMap == null) return;

            MyAPIGateway.Utilities.ShowMessage("HeatManagement", "[O2 DEBUG] CollectHeatBehaviors START");

            var o2Blocks = new List<IMyCubeBlock>();
            var tempBlocks = new List<IMySlimBlock>();
            grid.GetBlocks(tempBlocks);
            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2 DEBUG] Scanning {tempBlocks.Count} blocks");

            int foundCount = 0;
            foreach (var slimBlock in tempBlocks)
            {
                var block = slimBlock.FatBlock;
                if (block == null) continue;

                // Get the BEHAVIOR for this block (C# 6.0 compatible syntax)
                IHeatBehavior behavior;
                if (behaviorMap.TryGetValue(block, out behavior))
                {
                    // Check if BEHAVIOR implements O2 interfaces (not the block!)
                    if (behavior is IO2Producer || behavior is IO2Consumer || behavior is IO2Storage)
                    {
                        o2Blocks.Add(block);
                        foundCount++;
                    }
                }
            }
            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2 DEBUG] Found {foundCount} O2 blocks");

            foreach (var block in o2Blocks)
            {
                TryAddToNetwork(block, manager, behaviorMap);
            }

            MyAPIGateway.Utilities.ShowMessage("HeatManagement", "[O2 DEBUG] CollectHeatBehaviors FINISH");
        }

        private void TryAddToNetwork(IMyCubeBlock block, IGridHeatManager manager,
                                   IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            // Check if block is already in a network
            foreach (var existingNetwork in behaviorMap.Values.OfType<ConveyorNetworkBehavior>())
            {
                if (existingNetwork.IsConveyorConnected(block))
                {
                    existingNetwork.TryAddBlock(block);
                    behaviorMap[block] = existingNetwork;
                    return;
                }
            }

            // Create new network
            var newNetwork = new ConveyorNetworkBehavior();
            newNetwork.TryAddBlock(block);
            behaviorMap[block] = newNetwork;
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();

            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2 NETWORK] OnBlockAdded: {block.DisplayNameText}");

            // Get behavior from manager's map
            IHeatBehavior behavior = manager.TryGetHeatBehaviour(block);
            if (behavior == null)
            {
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", "[O2 NETWORK]   ERROR: No behavior found for block!");
                return result;
            }

            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2 NETWORK]   Behavior: {behavior.GetType().Name}");

            // Check O2 capability
            if (!(behavior is IO2Producer || behavior is IO2Consumer || behavior is IO2Storage))
            {
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", "[O2 NETWORK]   Not an O2 block, ignoring");
                return result;
            }

            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2 NETWORK]   Role: {GetRoleString(behavior)}");

            // Create new network for this block
            var newNetwork = new ConveyorNetworkBehavior();
            newNetwork.TryAddBlock(block);
            result.Behavior = newNetwork;
            result.AffectedBlocks = new List<IMyCubeBlock> { block };

            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2 NETWORK]   Created new network (ID: {newNetwork.GetHashCode()})");

            return result;
        }

        private string GetRoleString(IHeatBehavior behavior)
        {
            var roles = new List<string>();
            if (behavior is IO2Producer) roles.Add("Producer");
            if (behavior is IO2Consumer) roles.Add("Consumer");
            if (behavior is IO2Storage) roles.Add("Storage");
            return string.Join("+", roles);
        }
    }

    public class ConveyorNetworkBehavior : IMultiBlockHeatBehavior
    {
        private readonly List<IO2Producer> _producers = new List<IO2Producer>();
        private readonly List<IO2Storage> _storage = new List<IO2Storage>();
        private readonly List<IO2Consumer> _consumers = new List<IO2Consumer>();
        private bool _isValid;
        private IMyCubeBlock _referenceBlock;

        public IMyCubeBlock Block => _referenceBlock;

        public ConveyorNetworkBehavior()
        {
            _isValid = true;
        }

        public void Update(float deltaTime)
        {
            if (!_isValid) return;

            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution] ---- Update START (NetID={GetHashCode()}) ----");

            float o2Production = CalculateO2Production(deltaTime);
            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   Production: {o2Production:F2} L");

            float o2FromStorage = CalculateO2Storage();
            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   Storage: {o2FromStorage:F2} L");

            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   Consumers: {_consumers.Count}, Producers: {_producers.Count}, Storage: {_storage.Count}");

            float o2FromStorageConsumed = 0f;

            foreach (var consumer in _consumers)
            {
                if (!consumer.IsWorking)
                {
                    MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   SKIP (Not working): {consumer.Block?.DisplayNameText}");
                    continue;
                }

                float o2Needed = consumer.GetO2Consumption(deltaTime);
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   Consumer {consumer.Block?.DisplayNameText}: Needs {o2Needed:F2} L");

                if (o2Production >= o2Needed)
                {
                    MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   --> Satisfied from production");
                    o2Production -= o2Needed;
                    o2Needed = 0;
                }
                else
                {
                    o2Needed -= o2Production;
                    MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   --> Used production, still needs {o2Needed:F2} L");
                    o2Production = 0;
                }

                if (o2Needed > 0 && o2FromStorage > o2Needed)
                {
                    MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   --> Remaining need satisfied from storage consuming {o2Needed:F2} L");
                    o2FromStorageConsumed += o2Needed;
                    o2FromStorage -= o2Needed;
                    o2Needed = 0;
                }
                else if (o2Needed > 0)
                {
                    MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   --> INSUFFICIENT: Need {o2Needed:F2} L, have {o2FromStorage:F2} L");
                }
            }

            if (o2FromStorageConsumed > 0)
            {
                ConsumeFromStorage(o2FromStorageConsumed);
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution] Total consumed: {o2FromStorageConsumed:F2} L from storage");
            }

            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution] ---- Update FINISH ----");
        }

        private void ConsumeFromStorage(float amount)
        {
            foreach (var storage in _storage)
            {
                if (amount <= 0) break;
                if (!storage.IsWorking) continue;

                float currentStorage = storage.GetCurrentO2Storage();
                if (currentStorage <= 0) continue;

                float amountToConsume = amount > currentStorage ? currentStorage : amount;
                storage.ConsumeO2(amountToConsume);
                amount -= amountToConsume;
            }
        }

        private float CalculateO2Production(float deltaTime)
        {
            return _producers.Where(p => p != null && p.IsWorking)
                           .Sum(p => p.GetO2Production(deltaTime));
        }

        private float CalculateO2Storage()
        {
            return _storage.Where(s => s != null && s.IsWorking)
                          .Sum(s => s.GetCurrentO2Storage());
        }

        public bool TryAddBlock(IMyCubeBlock block)
        {
            if (!_isValid) return false;

            if (_referenceBlock == null)
            {
                _referenceBlock = block;
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution] Set reference block: {block.DisplayNameText}");
            }

            AddBlock(block);
            MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution] Added block to network: {block.DisplayNameText}");
            return true;
        }

        private void AddBlock(IMyCubeBlock block)
        {
            var producer = block as IO2Producer;
            if (producer != null && !_producers.Contains(producer))
            {
                _producers.Add(producer);
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   - Added as producer");
                return;
            }

            var storage = block as IO2Storage;
            if (storage != null && !_storage.Contains(storage))
            {
                _storage.Add(storage);
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   - Added as storage");
                return;
            }

            var consumer = block as IO2Consumer;
            if (consumer != null && !_consumers.Contains(consumer))
            {
                _consumers.Add(consumer);
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"[O2Distribution]   - Added as consumer");
                return;
            }
        }

        public void RemoveBlock(IMyCubeBlock block)
        {
            if (!_isValid) return;

            var producer = block as IO2Producer;
            if (producer != null)
            {
                _producers.Remove(producer);
            }

            var storage = block as IO2Storage;
            if (storage != null)
            {
                _storage.Remove(storage);
            }

            var consumer = block as IO2Consumer;
            if (consumer != null)
            {
                _consumers.Remove(consumer);
            }

            if (block == _referenceBlock)
            {
                _referenceBlock = GetAnyRemainingBlock();
            }
        }

        public bool IsConveyorConnected(IMyCubeBlock block)
        {
            if (!_isValid || _referenceBlock == null) return false;

            string refName = GetBlockName(_referenceBlock);
            string blockName = GetBlockName(block);

            bool isConnected = MyVisualScriptLogicProvider.IsConveyorConnected(refName, blockName) ||
                             MyVisualScriptLogicProvider.IsConveyorConnected(blockName, refName);

            return isConnected;
        }

        private string GetBlockName(IMyCubeBlock block)
        {
            return block.Name;
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
            if (!_isValid || _referenceBlock == null)
                return new NetworkSplitResult() { IsEmpty = true };

            var result = new NetworkSplitResult();
            var allBlocks = GetAllBlocks();

            if (allBlocks.Count <= 1)
            {
                result.IsEmpty = true;
                return result;
            }

            foreach (var block in allBlocks)
            {
                if (block == null || block == _referenceBlock) continue;

                bool isConnected = IsConveyorConnected(block);
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
            lock (_producers)
            {
                blocks.AddRange(_producers.Where(p => p != null).Select(p => p.Block));
            }
            lock (_storage)
            {
                blocks.AddRange(_storage.Where(s => s != null).Select(s => s.Block));
            }
            lock (_consumers)
            {
                blocks.AddRange(_consumers.Where(c => c != null).Select(c => c.Block));
            }
            return blocks;
        }

        private IMyCubeBlock GetAnyRemainingBlock()
        {
            return _producers.FirstOrDefault()?.Block ??
                   _storage.FirstOrDefault()?.Block ??
                   _consumers.FirstOrDefault()?.Block;
        }

        public void Invalidate()
        {
            _isValid = false;
            _producers.Clear();
            _storage.Clear();
            _consumers.Clear();
        }

        public bool IsValid => _isValid;
        public bool HasConsumers => _consumers.Any();
        public bool HasProducers => _producers.Any();

        // IMultiBlockHeatBehavior implementation
        public void RemoveBlock(IMyCubeBlock block, IGridHeatManager gridManager, Dictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            RemoveBlock(block);
        }

        public void ReactOnNewHeat(float heat) { }

        public void Cleanup()
        {
            Invalidate();
        }

        public float GetHeatChange(float deltaTime) => 0f;

        public void SpreadHeat(float deltaTime) { }

        public void AppendNetworkInfo(StringBuilder info) { }

        public void MarkDirty() { }

        public void ShowDebugGraph(float deltaTime) { }
    }
}
