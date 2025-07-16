using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    // Factory for registering ExhaustHeatManager
    public class ExhaustHeatManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyExhaustBlock> exhausts = new List<IMyExhaustBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(exhausts);

            foreach (var exhaust in exhausts)
            {
                if (!behaviorMap.ContainsKey(exhaust))
                {
                    behaviorMap[exhaust] = new ExhaustHeatManager(exhaust, manager);
                }
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            result.AffectedBlocks = new List<IMyCubeBlock> { block };

            if (block is IMyExhaustBlock)
            {
                result.Behavior = new ExhaustHeatManager(block as IMyExhaustBlock, manager);
                return result;
            }
            return result; // No behavior created for non-exhaust blocks
        }

        public int Priority => 25; // Between vents and thrusters
    }

    // Manages heat for exhaust blocks 
    public class ExhaustHeatManager : IHeatBehavior
    {
        private IGridHeatManager _gridManager;
        private IMyExhaustBlock _exhaust;

        public ExhaustHeatManager(IMyExhaustBlock exhaust, IGridHeatManager manager)
        {
            _exhaust = exhaust;
            _gridManager = manager;
            _exhaust.AppendingCustomInfo += AppendExhaustHeatInfo;
        }

        private void AppendExhaustHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float heat = HeatSession.Api.Utils.GetHeat(block);
            float capacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float ambient = HeatSession.Api.Utils.CalculateAmbientTemperature(block);

            var connectedPipeNetworks = new List<HeatPipeManager>();
            var neighborWithChange = new Dictionary<IMySlimBlock, float>();
            var neighborList = new List<IMySlimBlock>();
            var insulatedNeihbors = new List<IMySlimBlock>();
            block.SlimBlock.GetNeighbours(neighborList);
            float cumulativeNeighborHeatChange = 0f;
            float cumulativeNetworkHeatChange = 0f;
            if (neighborList.Count > 0)
            {
                foreach (var neighbor in neighborList)
                {
                    var behavior = _gridManager.TryGetHeatBehaviour(neighbor.FatBlock);
                    if (behavior != null && behavior is HeatPipeManager)
                    {
                        var network = behavior as HeatPipeManager;
                        if (HeatPipeManagerFactory.IsPipeConnectedToBlock(neighbor.FatBlock, _exhaust)){
                            if (!connectedPipeNetworks.Contains(network))
                            {
                                connectedPipeNetworks.Add(network);
                            }
                            neighborWithChange[neighbor] = network.GetHeatExchange(neighbor.FatBlock, _exhaust, 1) / capacity;
                            cumulativeNetworkHeatChange += neighborWithChange[neighbor];
                        } else {
                            insulatedNeihbors.Add(neighbor);
                        }
                    }
                    else
                    {
                        neighborWithChange[neighbor] = HeatSession.Api.Utils.GetExchangeWithNeighbor(_exhaust, neighbor.FatBlock, 1); // Assuming deltaTime of 1 second for display purposes
                        cumulativeNeighborHeatChange += neighborWithChange[neighbor];
                    }
                }
            }
            float airDensity = HeatSession.Api.Utils.GetAirDensity(_exhaust);

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F2} °C");
            builder.AppendLine($"Air Heat Change: {GetHeatChange(1):F2} °C/s");
            string exchangeMode = _exhaust.IsFunctional && _exhaust.IsWorking ? "Active" : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {capacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Ambient temp: {ambient:F1} °C");
            builder.AppendLine($"Air density: {airDensity * 100:F1} %");
            builder.AppendLine($"------");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.AppendLine($"  Exhaust: {HeatSession.Api.Utils.GetActiveExhaustHeatLoss(_exhaust, 1):+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"  Air Exchange: {HeatSession.Api.Utils.GetAmbientHeatLoss(_exhaust, 1):+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"  Neighbor Block: {cumulativeNeighborHeatChange:+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"  Heat pipes: {cumulativeNetworkHeatChange:+0.00;-0.00;0.00} °C/s");

            if (neighborList.Count > 0)
            {
                builder.AppendLine("");
                builder.AppendLine($"Neighbors:");
                foreach (var neighbor in neighborList)
                {
                    var neighborBlock = neighbor.FatBlock;
                    if (neighborBlock != null)
                    {
                        if (!insulatedNeihbors.Contains(neighbor)) {
                            builder.AppendLine($"- {neighborBlock.DisplayNameText} ({HeatSession.Api.Utils.GetHeat(neighborBlock):F2}°C) -> {neighborWithChange[neighbor]:F4} °C/s");
                        } else {
                            builder.AppendLine($"- {neighborBlock.DisplayNameText} ({HeatSession.Api.Utils.GetHeat(neighborBlock):F2}°C) !! Insulated");
                        }  
                    }
                }
            }
            if (connectedPipeNetworks.Count > 0)
            {
                builder.AppendLine("");
                builder.AppendLine($"Pipe networks:");
                foreach (var network in connectedPipeNetworks)
                {
                    network.AppendNetworkInfo(builder);
                }
            }
            builder.AppendLine($"------");
        }

        public float GetHeatChange(float deltaTime)
        {
            if (_exhaust == null)
                return 0f;

            float change = HeatSession.Api.Utils.GetAmbientHeatLoss(_exhaust, deltaTime);

            // You may want to implement a custom method for exhausts here
            if (_exhaust.IsFunctional && _exhaust.Enabled)
            {
                change = HeatSession.Api.Utils.GetActiveExhaustHeatLoss(_exhaust, deltaTime);
            }
            return -change;
        }

        public void Cleanup()
        {
            if (_exhaust != null)
            {
                _exhaust.AppendingCustomInfo -= AppendExhaustHeatInfo;
                _exhaust = null;
            }
        }

        public void SpreadHeat(float deltaTime)
        {
            // Exhausts do not spread heat in this implementation
            return;
        }

        public void ReactOnNewHeat(float heat)
        {
            this._exhaust.RefreshCustomInfo();
            return; // No specific reaction needed for exhausts
        }
    }
} 