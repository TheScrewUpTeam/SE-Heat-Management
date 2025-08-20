using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TSUT.HeatManagement
{
    public class BatteryHeatManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(batteries);

            foreach (var battery in batteries)
            {
                if (!behaviorMap.ContainsKey(battery))
                {
                    behaviorMap[battery] = new BatteryHeatManager(battery, manager);
                }
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            result.AffectedBlocks = new List<IMyCubeBlock> { block };

            if (block is IMyBatteryBlock)
            {
                result.Behavior = new BatteryHeatManager(block as IMyBatteryBlock, manager);
                return result;
            }
            return result; // No behavior created for non-battery blocks
        }

        public int Priority => 10; // Batteries first, since they are critical for heat management
    }

    public class BatteryHeatManager : IHeatBehavior
    {
        private IMyBatteryBlock _battery;
        private IGridHeatManager _gridManager;

        public BatteryHeatManager(IMyBatteryBlock battery, IGridHeatManager manager)
        {
            _battery = battery;
            _gridManager = manager;
            _battery.AppendingCustomInfo += AppendBatteryHeatInfo;
        }

        private float CalculateInternalHeatGain(IMyBatteryBlock battery, float deltaTime)
        {
            if (battery == null)
                return 0f;

            float thermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(battery);
            float outputMW = battery.CurrentOutput + battery.CurrentInput; // Total power output in MW

            float tNorm = MathHelper.Clamp(HeatSession.Api.Utils.GetHeat(battery) / Config.Instance.CRITICAL_TEMP, 0f, 1f);
            float resistanceMultiplier = MathHelper.Lerp(1f, 2f, tNorm * tNorm); // More exponential rise

            float internalResistance = Config.Instance.DISCHARGE_HEAT_FRACTION * resistanceMultiplier;

            float heatEnergy = outputMW * 1000000f * deltaTime * internalResistance; // MW to Watts (J/s) * seconds = Joules
            float heatGain = heatEnergy / thermalCapacity; // J / (J/°C) = °C

            return heatGain;
        }

        private float CalculateAmbientHeatLoss(IMyBatteryBlock battery, float deltaTime)
        {
            if (battery == null)
                return 0f;

            float thermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(battery);
            float surfaceArea = HeatSession.Api.Utils.GetRealSurfaceArea(battery);
            float currentHeat = HeatSession.Api.Utils.GetHeat(battery);
            float ambientTemp = HeatSession.Api.Utils.CalculateAmbientTemperature(battery);
            float windSpeed = HeatSession.Api.Utils.GetBlockWindSpeed(battery);
            float windMultiplier = 1f + Config.Instance.WIND_COOLING_MULT * windSpeed;

            // Energy loss due to ambient temperature difference, increased by wind
            float energyLoss = (currentHeat - ambientTemp) * surfaceArea * Config.Instance.HEAT_COOLDOWN_COEFF * windMultiplier * deltaTime;
            return energyLoss / thermalCapacity; // °C lost
        }

        public float GetHeatChange(float deltaTime)
        {
            if (_battery == null)
                return 0f;

            float heatGain = CalculateInternalHeatGain(_battery, deltaTime);
            float heatLoss = CalculateAmbientHeatLoss(_battery, deltaTime);

            return heatGain - heatLoss;
        }

        public void Cleanup()
        {
            if (_battery != null)
            {
                _battery.AppendingCustomInfo -= AppendBatteryHeatInfo;
                _battery = null;
            }
        }

        private void AppendBatteryHeatInfo(IMyTerminalBlock block, StringBuilder info)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);

            var connectedPipeNetworks = new List<HeatPipeManager>();
            var neighborWithChange = new Dictionary<IMySlimBlock, float>();
            var neighborList = new List<IMySlimBlock>();
            var insulatedNeihbors = new List<IMySlimBlock>();
            var networkedNeighbors = new List<IMySlimBlock>();
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
                        if (HeatPipeManagerFactory.IsPipeConnectedToBlock(neighbor.FatBlock, _battery))
                        {
                            if (!connectedPipeNetworks.Contains(network))
                            {
                                connectedPipeNetworks.Add(network);
                            }
                            neighborWithChange[neighbor] = network.GetHeatExchange(neighbor.FatBlock, _battery, 1f) / ownThermalCapacity;
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
                        neighborWithChange[neighbor] = HeatSession.Api.Utils.GetExchangeWithNeighbor(_battery, neighbor.FatBlock, 1); // Assuming deltaTime of 1 second for display purposes
                        cumulativeNeighborHeatChange += neighborWithChange[neighbor];
                    }
                }
            }
            var heat = HeatSession.Api.Utils.GetHeat(block);
            float heatChange = GetHeatChange(1f) + cumulativeNeighborHeatChange + cumulativeNetworkHeatChange; // Assuming deltaTime of 1 second for display purposes

            info.AppendLine($"--- Heat Management ---");
            info.AppendLine($"Temperature: {heat:F2} °C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            info.AppendLine($"Thermal Status: {heatStatus}");
            info.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            info.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            info.AppendLine($"Cooling Area: {HeatSession.Api.Utils.GetRealSurfaceArea(block):F1} m²");
            info.AppendLine($"Density: {HeatSession.Api.Utils.GetDensity(block):F1} kg/m³");
            float windSpeed = HeatSession.Api.Utils.GetBlockWindSpeed(block);
            info.AppendLine($"Wind Speed: {windSpeed:F2} m/s");
            if (heatChange > 0f)
            {
                float tempDiff = Config.Instance.CRITICAL_TEMP - heat;
                float timeToOverheat = tempDiff / heatChange;
                if (!float.IsNaN(timeToOverheat) && !float.IsInfinity(timeToOverheat))
                {
                    TimeSpan timeSpan = TimeSpan.FromSeconds(timeToOverheat);
                    string formattedTime = timeSpan.ToString(@"hh\:mm\:ss");
                    info.AppendLine($"Estimated Time to Overheat: {formattedTime}");
                }
            }
            else
            {
                float tempDiff = heat - HeatSession.Api.Utils.CalculateAmbientTemperature(_battery);
                float timeToCoolDown = tempDiff / Math.Abs(heatChange);
                if (!float.IsNaN(timeToCoolDown) && !float.IsInfinity(timeToCoolDown))
                {
                    TimeSpan timeSpan = TimeSpan.FromSeconds(timeToCoolDown);
                    string formattedTime = timeSpan.ToString(@"hh\:mm\:ss");
                    info.AppendLine($"Estimated Time to cool down: {formattedTime}");
                }
            }
            info.AppendLine("");
            info.AppendLine("Heat Sources:");
            info.AppendLine($"  Internal Use: {CalculateInternalHeatGain(block as IMyBatteryBlock, 1):F2} °C/s");
            info.AppendLine($"  Air Exchange: {-CalculateAmbientHeatLoss(block as IMyBatteryBlock, 1):+0.00;-0.00;0.00} °C/s");
            info.AppendLine($"  Neighbor Block: {cumulativeNeighborHeatChange:+0.00;-0.00;0.00} °C/s");
            info.AppendLine($"  Heat pipes: {cumulativeNetworkHeatChange:+0.00;-0.00;0.00} °C/s");

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
                            info.AppendLine($"- {neighborBlock.DisplayNameText}{networkText} ({HeatSession.Api.Utils.GetHeat(neighborBlock):F2}°C) -> {neighborWithChange[neighbor]:F4} °C/s");
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
            info.AppendLine($"------");
        }

        void ExplodeBatteryInstantly(IMyBatteryBlock battery)
        {
            var slimBlock = battery.SlimBlock;
            if (slimBlock == null)
                return;

            slimBlock.DoDamage(1000000f, MyDamageType.Explosion, true);
        }

        public void SpreadHeat(float deltaTime)
        {
            if (_battery == null)
                return;

            float cumulativeHeat = 0f;
            float tempA = HeatSession.Api.Utils.GetHeat(_battery);
            float capacityA = HeatSession.Api.Utils.GetThermalCapacity(_battery);

            var neighborList = new List<IMySlimBlock>();
            _battery.SlimBlock.GetNeighbours(neighborList);

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
                    energyTransferred = -network.GetHeatExchange(neighborFat, _battery, deltaTime);
                }
                else if (behaviour != null)
                {
                    continue;
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

                float ambientLoss = 0f;

                if (behaviour == null)
                {
                    // Apply ambient heat exchange for non-vent, non-battery blocks
                    ambientLoss = HeatSession.Api.Utils.GetAmbientHeatLoss(neighborFat, deltaTime);
                }

                float newTemp = tempB + deltaB - ambientLoss;
                HeatSession.Api.Utils.SetHeat(neighborFat, newTemp);
                HeatSession.Api.Effects.UpdateBlockHeatLight(neighborFat, newTemp);

                cumulativeHeat += deltaA;
            }
            HeatSession.Api.Utils.SetHeat(_battery, tempA + cumulativeHeat);
        }

        public void ReactOnNewHeat(float heat)
        {
            _battery.SetDetailedInfoDirty();
            _battery.RefreshCustomInfo();
            HeatSession.Api.Effects.UpdateBlockHeatLight(_battery, heat);
            // Check if we need to instantiate or remove smoke effects
            if (heat > Config.Instance.SMOKE_TRESHOLD)
            {
                HeatSession.Api.Effects.InstantiateSmoke(_battery);
            }
            else
            {
                HeatSession.Api.Effects.RemoveSmoke(_battery);
            }
            // Check if we need to explode the battery
            if (heat > Config.Instance.CRITICAL_TEMP && MyAPIGateway.Multiplayer.IsServer)
            {
                ExplodeBatteryInstantly(_battery);
            }
        }
    }
}