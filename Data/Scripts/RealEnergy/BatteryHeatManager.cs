using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace TSUT.HeatManagement
{
    public class BatteryHeatManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(batteries);

            foreach (var battery in batteries)
            {
                if (!behaviorMap.ContainsKey(battery))
                {
                    behaviorMap[battery] = new BatteryHeatManager(battery);
                }
            }
        }

        public IHeatBehavior OnBlockAdded(IMyCubeBlock block)
        {
            if (block is IMyBatteryBlock)
            {
                return new BatteryHeatManager(block as IMyBatteryBlock);
            }
            return null; // No behavior created for non-battery blocks
        }

        public int Priority => 10; // Batteries first, since they are critical for heat management
    }
    
    public class BatteryHeatManager : IHeatBehavior
    {
        private IMyBatteryBlock _battery;

        public BatteryHeatManager(IMyBatteryBlock battery)
        {
            _battery = battery;
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

            // Energy loss due to ambient temperature difference
            float energyLoss = (currentHeat - ambientTemp) * surfaceArea * Config.Instance.HEAT_COOLDOWN_COEFF * deltaTime;
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
            var neighborWithChange = new Dictionary<IMySlimBlock, float>();
            var neighborList = new List<IMySlimBlock>();
            block.SlimBlock.GetNeighbours(neighborList);
            float cumulativeNeighborHeatChange = 0f;
            if (neighborList.Count > 0)
            {
                foreach (var neighbor in neighborList)
                {
                    neighborWithChange[neighbor] = CalculateExchangeWithNeighbor(neighbor, 1); // Assuming deltaTime of 1 second for display purposes
                    cumulativeNeighborHeatChange += neighborWithChange[neighbor];
                }
            }
            var heat = HeatSession.Api.Utils.GetHeat(block);
            float heatChange = GetHeatChange(1f) + cumulativeNeighborHeatChange; // Assuming deltaTime of 1 second for display purposes

            info.AppendLine($"--- Heat Management ---");
            info.AppendLine($"Temperature: {heat:F2} °C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            info.AppendLine($"Thermal Status: {heatStatus}");
            info.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            info.AppendLine($"Thermal Capacity: {HeatSession.Api.Utils.GetThermalCapacity(block) / 1000000:F1} MJ/°C");
            info.AppendLine($"Cooling Area: {HeatSession.Api.Utils.GetRealSurfaceArea(block):F1} m²");
            info.AppendLine($"Density: {HeatSession.Api.Utils.GetDensity(block):F1} kg/m³");
            if (heatChange > 0f)
            {
                float tempDiff = Config.Instance.CRITICAL_TEMP - heat;
                float timeToOverheat = tempDiff / heatChange;
                TimeSpan timeSpan = TimeSpan.FromSeconds(timeToOverheat);
                string formattedTime = timeSpan.ToString(@"hh\:mm\:ss");
                info.AppendLine($"Estimated Time to Overheat: {formattedTime}");
            }
            else
            {
                float tempDiff = heat - HeatSession.Api.Utils.CalculateAmbientTemperature(_battery);
                float timeToCoolDown = tempDiff / Math.Abs(heatChange);
                TimeSpan timeSpan = TimeSpan.FromSeconds(timeToCoolDown);
                string formattedTime = timeSpan.ToString(@"hh\:mm\:ss");
                info.AppendLine($"Estimated Time to cool down: {formattedTime}");
            }
            info.AppendLine("");
            info.AppendLine("Heat Sources:");
            info.AppendLine($"  Internal Use: {CalculateInternalHeatGain(block as IMyBatteryBlock, 1):F2} °C/s");
            info.AppendLine($"  Air Exchange: {-CalculateAmbientHeatLoss(block as IMyBatteryBlock, 1):+0.00;-0.00;0.00} °C/s");
            info.AppendLine($"  Neighbor Block: {cumulativeNeighborHeatChange:+0.00;-0.00;0.00} °C/s");

            if (neighborList.Count > 0)
            {
                info.AppendLine($"Neighbors:");
                foreach (var neighbor in neighborList)
                {
                    var neighborBlock = neighbor.FatBlock;
                    if (neighborBlock != null)
                    {
                        info.AppendLine($"- {neighborBlock.DisplayNameText} ({HeatSession.Api.Utils.GetHeat(neighborBlock):F2}°C) -> {neighborWithChange[neighbor]:F2} °C/s");
                    }
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

        private float CalculateExchangeWithNeighbor(IMySlimBlock neighborSlim, float deltaTime)
        {
            var neighborFat = neighborSlim.FatBlock;
            if (neighborFat == null)
                return 0f;

            float tempA = HeatSession.Api.Utils.GetHeat(_battery);
            float tempB = HeatSession.Api.Utils.GetHeat(neighborFat);
            float capacityA = HeatSession.Api.Utils.GetThermalCapacity(_battery);
            float capacityB = HeatSession.Api.Utils.GetThermalCapacity(neighborFat);

            float tempDiff = tempA - tempB;
            float contactArea = HeatSession.Api.Utils.GetLargestFaceArea(neighborSlim);
            float energyTransferred = tempDiff * Config.Instance.THERMAL_CONDUCTIVITY * contactArea * deltaTime; // Arbitrary scaling factor for transfer rate

            // Convert energy to delta-T for each block
            float deltaA = -energyTransferred / capacityA;
            float deltaB = energyTransferred / capacityB;

            HeatSession.Api.Utils.SetHeat(_battery, tempA + deltaA);
            HeatSession.Api.Utils.SetHeat(neighborFat, tempB + deltaB);

            return deltaA;
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

            foreach (var neighborSlim in neighborList)
            {
                var neighborFat = neighborSlim.FatBlock;
                if (neighborFat == null)
                    continue;

                float tempB = HeatSession.Api.Utils.GetHeat(neighborFat);
                float capacityB = HeatSession.Api.Utils.GetThermalCapacity(neighborFat);

                float tempDiff = tempA - tempB;

                float contactArea = HeatSession.Api.Utils.GetLargestFaceArea(neighborSlim);
                float energyTransferred = tempDiff * Config.Instance.THERMAL_CONDUCTIVITY * contactArea * deltaTime; // Arbitrary scaling factor for transfer rate

                // Convert energy to delta-T for each block
                float deltaA = -energyTransferred / capacityA;
                float deltaB = energyTransferred / capacityB;

                HeatSession.Api.Utils.SetHeat(_battery, tempA + deltaA);

                float ambientLoss = 0f;

                if (!(neighborFat is IMyAirVent) && !(neighborFat is IMyBatteryBlock))
                {
                    // Apply ambient heat exchange for non-vent, non-battery blocks
                    ambientLoss = HeatSession.Api.Utils.GetAmbientHeatLoss(neighborFat, deltaTime);
                }

                HeatSession.Api.Utils.SetHeat(neighborFat, tempB + deltaB - ambientLoss);

                if (_battery.CustomName.Contains("ShowHeat"))
                {
                    MyAPIGateway.Utilities.ShowNotification($"Neighbor: {neighborFat.DisplayNameText}, before: {tempB}, exp: {tempB + deltaB}, after: {HeatSession.Api.Utils.GetHeat(neighborFat)}", 1000);
                }

                cumulativeHeat += deltaA;
            }
        }

        public void ReactOnNewHeat(float heat)
        {
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
            if (heat > Config.Instance.CRITICAL_TEMP)
            {
                ExplodeBatteryInstantly(_battery);
            }
        }
    }
}