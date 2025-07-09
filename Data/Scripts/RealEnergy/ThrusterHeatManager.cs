using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public class ThrusterHeatManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyThrust> thrusters = new List<IMyThrust>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(thrusters);

            foreach (var thruster in thrusters)
            {
                if (!behaviorMap.ContainsKey(thruster))
                {
                    behaviorMap[thruster] = new ThrusterHeatManager(thruster, manager);
                }
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            result.AffectedBlocks = new List<IMyCubeBlock> { block };

            if (block is IMyThrust)
            {
                result.Behavior = new ThrusterHeatManager(block as IMyThrust, manager);
                return result;
            }
            return result; // No behavior created for non-thruster blocks
        }

        public int Priority => 30; // Thrusters are less critical than batteries and vents
    }

    public class ThrusterHeatManager : IHeatBehavior
    {
        private IGridHeatManager _gridManager;
        private IMyThrust _thruster;

        public ThrusterHeatManager(IMyThrust thruster, IGridHeatManager manager)
        {
            _thruster = thruster;
            _gridManager = manager;
            _thruster.AppendingCustomInfo += AppendThrusterHeatInfo;
        }

        private void AppendThrusterHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float heat = HeatSession.Api.Utils.GetHeat(block);
            float capacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float ambient = HeatSession.Api.Utils.CalculateAmbientTemperature(block);
            float outputRatio = (_thruster.MaxThrust > 0f) ? (_thruster.CurrentThrust / _thruster.MaxThrust) : 0f;

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
                        if (HeatPipeManagerFactory.IsPipeConnectedToBlock(neighbor.FatBlock, _thruster)){
                            connectedPipeNetworks.Add(network);
                            neighborWithChange[neighbor] = network.GetHeatExchange(neighbor.FatBlock, _thruster, 1) / capacity;
                            cumulativeNetworkHeatChange += neighborWithChange[neighbor];
                        } else {
                            insulatedNeihbors.Add(neighbor);
                        }
                    }
                    else
                    {
                        neighborWithChange[neighbor] = HeatSession.Api.Utils.GetExchangeWithNeighbor(_thruster, neighbor.FatBlock, 1); // Assuming deltaTime of 1 second for display purposes
                        cumulativeNeighborHeatChange += neighborWithChange[neighbor];
                    }
                }
            }

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F2} °C");
            builder.AppendLine($"Air Heat Change: {GetHeatChange(1):F2} °C/s");
            string exchangeMode = outputRatio > 0f ? "Active" : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {capacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Thrust output: {outputRatio * 100:F1} %");
            builder.AppendLine($"Ambient temp: {ambient:F1} °C");
            float windSpeed = HeatSession.Api.Utils.GetBlockWindSpeed(block);
            builder.AppendLine($"Wind Speed: {windSpeed:F2} m/s");if (neighborList.Count > 0)
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
                        }                    }
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
            if (_thruster == null)
                return 0f;

            float change = HeatSession.Api.Utils.GetAmbientHeatLoss(_thruster, deltaTime);
            float thrustRatio = (_thruster.MaxThrust > 0f) ? (_thruster.CurrentThrust / _thruster.MaxThrust) : 0f;

            if (_thruster.IsFunctional && _thruster.Enabled && thrustRatio > 0f)
            {
                change = HeatSession.Api.Utils.GetActiveThrusterHeatLoss(_thruster, thrustRatio, deltaTime);
            }
            return -change;
        }

        public void Cleanup()
        {
            if (_thruster != null)
            {
                _thruster.AppendingCustomInfo -= AppendThrusterHeatInfo;
                _thruster = null;
            }
        }

        public void SpreadHeat(float deltaTime)
        {
            return; // Thrusters do not spread heat in this implementation
        }

        public void ReactOnNewHeat(float heat)
        {
            this._thruster.RefreshCustomInfo();
            return; // No specific reaction needed for thrusters
        }
    }
}
