using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public abstract class AHeatBehavior : IHeatBehavior
    {
        public abstract float GetHeatChange(float deltaTime);
        public abstract void SpreadHeat(float deltaTime);
        public abstract void Cleanup();
        public abstract void ReactOnNewHeat(float heat);

        public void SpreadHeatStandard(IMyCubeBlock block, float deltaTime)
        {
            if (block == null)
                return;

            float cumulativeHeat = 0f;
            float tempOwn = HeatSession.Api.Utils.GetHeat(block);
            float capacityOwn = HeatSession.Api.Utils.GetThermalCapacity(block);

            var neighborList = new List<IMySlimBlock>();
            block.SlimBlock.GetNeighbours(neighborList);

            foreach (var neighborSlim in neighborList)
            {
                var neighborFat = neighborSlim.FatBlock;
                if (neighborFat == null)
                    continue;
                float capacityNeighbot = HeatSession.Api.Utils.GetThermalCapacity(neighborFat);
                float tempNeighbor = HeatSession.Api.Utils.GetHeat(neighborFat);

                var behaviour = HeatSession.GetBehaviorForBlock(neighborFat);

                var energyTransferred = HeatSession.Api.Utils.GetExchangeUniversal(block, neighborFat, deltaTime); // Get energy transferred in Joules

                // Convert energy to delta-T for each block
                float deltaOwn = energyTransferred / capacityOwn;
                float deltaNeighbor = energyTransferred / capacityNeighbot;

                float ambientLoss = 0f;

                if (behaviour == null)
                {
                    // Apply ambient heat exchange for non-vent, non-battery blocks
                    ambientLoss = HeatSession.Api.Utils.GetAmbientHeatLoss(neighborFat, deltaTime);
                }

                float newTemp = tempNeighbor + deltaNeighbor - ambientLoss;
                HeatSession.Api.Utils.SetHeat(neighborFat, newTemp);
                HeatSession.Api.Effects.UpdateBlockHeatLight(neighborFat, newTemp);

                cumulativeHeat += deltaOwn;
            }
            HeatSession.Api.Utils.SetHeat(block, tempOwn - cumulativeHeat);
        }

        internal void AddNeighborAndNetworksInfo(IMyCubeBlock block, StringBuilder info, out float cumulativeNeighborHeatChange, out float cumulativeNetworkHeatChange)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);

            var connectedPipeNetworks = new List<HeatPipeManager>();
            var neighborWithChange = new Dictionary<IMySlimBlock, float>();
            var neighborList = new List<IMySlimBlock>();
            var insulatedNeihbors = new List<IMySlimBlock>();
            var networkedNeighbors = new List<IMySlimBlock>();
            block.SlimBlock.GetNeighbours(neighborList);
            cumulativeNeighborHeatChange = 0f;
            cumulativeNetworkHeatChange = 0f;
            if (neighborList.Count > 0)
            {
                foreach (var neighbor in neighborList)
                {
                    var behavior = HeatSession.GetBehaviorForBlock(neighbor.FatBlock);
                    if (behavior != null && behavior is HeatPipeManager)
                    {
                        var network = behavior as HeatPipeManager;
                        if (HeatPipeManagerFactory.IsPipeConnectedToBlock(neighbor.FatBlock, block))
                        {
                            if (!connectedPipeNetworks.Contains(network))
                            {
                                connectedPipeNetworks.Add(network);
                            }
                            neighborWithChange[neighbor] = network.GetHeatExchange(neighbor.FatBlock, block, 1f) / ownThermalCapacity;
                            cumulativeNetworkHeatChange += neighborWithChange[neighbor];
                            networkedNeighbors.Add(neighbor);
                        }
                        else
                        {
                            insulatedNeihbors.Add(neighbor);
                        }
                    }
                    else
                    {
                        neighborWithChange[neighbor] = HeatSession.Api.Utils.GetExchangeWithNeighbor(block, neighbor.FatBlock, 1) / ownThermalCapacity; // Assuming deltaTime of 1 second for display purposes
                        cumulativeNeighborHeatChange += neighborWithChange[neighbor];
                    }
                }
            }
            info.AppendLine($"  Neighbor Block: {-cumulativeNeighborHeatChange:+0.00;-0.00;0.00} °C/s");
            info.AppendLine($"  Heat pipes: {-cumulativeNetworkHeatChange:+0.00;-0.00;0.00} °C/s");

            if (neighborList.Count > 0)
            {
                info.AppendLine("");
                info.AppendLine($"Neighbors:");
                foreach (var neighbor in neighborList)
                {
                    var neighborBlock = neighbor.FatBlock;
                    if (neighborBlock != null)
                    {
                        if (!insulatedNeihbors.Contains(neighbor))
                        {
                            string networkText = networkedNeighbors.Contains(neighbor) ? "-NET-" : "";
                            info.AppendLine($"- {neighborBlock.DisplayNameText}{networkText} ({HeatSession.Api.Utils.GetHeat(neighborBlock):F2}°C) -> {-neighborWithChange[neighbor]:F4} °C/s");
                        }
                        else
                        {
                            info.AppendLine($"- {neighborBlock.DisplayNameText} ({HeatSession.Api.Utils.GetHeat(neighborBlock):F2}°C) !! Insulated");
                        }
                    }
                }
            }
            if (connectedPipeNetworks.Count > 0)
            {
                info.AppendLine("");
                info.AppendLine($"Pipe networks:");
                foreach (var network in connectedPipeNetworks)
                {
                    network.AppendNetworkInfo(info);
                }
            }
        }
    }
}