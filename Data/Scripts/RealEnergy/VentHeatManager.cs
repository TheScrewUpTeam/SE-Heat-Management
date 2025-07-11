using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public class VentHeatManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyAirVent> vents = new List<IMyAirVent>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(vents);

            foreach (var vent in vents)
            {
                if (!behaviorMap.ContainsKey(vent))
                {
                    behaviorMap[vent] = new VentHeatManager(vent, manager);
                }
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            result.AffectedBlocks = new List<IMyCubeBlock> { block };

            if (block is IMyAirVent)
            {
                result.Behavior = new VentHeatManager(block as IMyAirVent, manager);
                return result;
            }
            return result; // No behavior created for non-vent blocks
        }

        public int Priority => 20; // Vents are less critical than batteries
    }
    
    public class VentHeatManager : IHeatBehavior
    {
        private IGridHeatManager _gridManager;
        private IMyAirVent _vent;

        public VentHeatManager(IMyAirVent vent, IGridHeatManager manager)
        {
            _vent = vent;
            _gridManager = manager;
            _vent.AppendingCustomInfo += AppendVentHeatInfo;
        }

        private void AppendVentHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);

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
                        if (HeatPipeManagerFactory.IsPipeConnectedToBlock(neighbor.FatBlock, _vent)){
                            if (!connectedPipeNetworks.Contains(network))
                            {
                                connectedPipeNetworks.Add(network);
                            }
                            neighborWithChange[neighbor] = network.GetHeatExchange(neighbor.FatBlock, _vent, 1) / ownThermalCapacity;
                            cumulativeNetworkHeatChange += neighborWithChange[neighbor];
                        } else {
                            insulatedNeihbors.Add(neighbor);
                        }
                    }
                    else
                    {
                        neighborWithChange[neighbor] = HeatSession.Api.Utils.GetExchangeWithNeighbor(_vent, neighbor.FatBlock, 1); // Assuming deltaTime of 1 second for display purposes
                        cumulativeNeighborHeatChange += neighborWithChange[neighbor];
                    }
                }
            }
            var heat = HeatSession.Api.Utils.GetHeat(block);
            float heatChange = GetHeatChange(1f) + cumulativeNeighborHeatChange + cumulativeNetworkHeatChange; // Assuming deltaTime of 1 second for display purposes

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {HeatSession.Api.Utils.GetHeat(block):F2} °C");
            builder.AppendLine($"Air Heat Change: {GetHeatChange(1):F2} °C/s");
            string exchangeMode = _vent.IsFunctional && _vent.Enabled ? "Active" : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Ambient temp: {HeatSession.Api.Utils.CalculateAmbientTemperature(block):F1} °C");
            builder.AppendLine($"Air density: {HeatSession.Api.Utils.GetAirDensity(_vent) * 100:F1} %");
            float windSpeed = HeatSession.Api.Utils.GetBlockWindSpeed(block);
            builder.AppendLine($"Wind Speed: {windSpeed:F2} m/s");
            builder.AppendLine($"------");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.AppendLine($"  Air Exchange: {GetHeatChange(1):+0.00;-0.00;0.00} °C/s");
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
            if (_vent == null)
                return 0f;

            float change = HeatSession.Api.Utils.GetAmbientHeatLoss(_vent, deltaTime);
            if (_vent.IsFunctional && _vent.Enabled)
            {
                change += HeatSession.Api.Utils.GetActiveVentHealLoss(_vent, deltaTime);
            }
            return -change;
        }

        public void Cleanup()
        {
            if (_vent != null)
            {
                _vent.AppendingCustomInfo -= AppendVentHeatInfo;
                _vent = null;
            }
        }

        public void SpreadHeat(float deltaTime)
        {
            if (_vent == null)
                return;

            float cumulativeHeat = 0f;
            float tempA = HeatSession.Api.Utils.GetHeat(_vent);
            float capacityA = HeatSession.Api.Utils.GetThermalCapacity(_vent);

            var neighborList = new List<IMySlimBlock>();
            _vent.SlimBlock.GetNeighbours(neighborList);

            float energyTransferred = 0f;

            foreach (var neighborSlim in neighborList)
            {
                var neighborFat = neighborSlim.FatBlock;
                if (neighborFat == null)
                    continue;
                float capacityB = HeatSession.Api.Utils.GetThermalCapacity(neighborFat);
                float tempB = HeatSession.Api.Utils.GetHeat(neighborFat);

                var behaviour = _gridManager.TryGetHeatBehaviour(neighborFat);
                if (behaviour != null && behaviour is HeatPipeManager)
                {
                    var network = behaviour as HeatPipeManager;
                    energyTransferred = -network.GetHeatExchange(neighborFat, _vent, deltaTime);
                }
                else
                {
                    float tempDiff = tempA - tempB;

                    float contactArea = HeatSession.Api.Utils.GetLargestFaceArea(neighborSlim);
                    energyTransferred = tempDiff * Config.Instance.THERMAL_CONDUCTIVITY * contactArea * deltaTime; // Arbitrary scaling factor for transfer rate
                }

                // Convert energy to delta-T for each block
                float deltaA = -energyTransferred / capacityA;
                float deltaB = energyTransferred / capacityB;

                HeatSession.Api.Utils.SetHeat(_vent, tempA + deltaA);

                float ambientLoss = 0f;

                if (!(neighborFat is IMyAirVent) && !(neighborFat is IMyBatteryBlock))
                {
                    // Apply ambient heat exchange for non-vent, non-battery blocks
                    ambientLoss = HeatSession.Api.Utils.GetAmbientHeatLoss(neighborFat, deltaTime);
                }

                float newTemp = tempB + deltaB - ambientLoss;
                HeatSession.Api.Utils.SetHeat(neighborFat, newTemp);
                HeatSession.Api.Effects.UpdateBlockHeatLight(neighborFat, newTemp);

                cumulativeHeat += deltaA;
            }
        }

        public void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(_vent, heat);
            this._vent.RefreshCustomInfo();
            return; // Vents do not react to new heat in this implementation
        }
    }
}