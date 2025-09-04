using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Sandbox.Definitions;
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

    public class BatteryHeatManager : AHeatBehavior
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

            float powerToHeatRatio = Config.Instance.DISCHARGE_HEAT_FRACTION;

            if (!Config.Instance.DISCHARGE_HEAT_CONFIGURABLE)
            {
                var def = battery.SlimBlock.BlockDefinition as MyBatteryBlockDefinition;
                powerToHeatRatio = 1 - def.RechargeMultiplier;
            }

            float thermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(battery);
            float outputMW = Math.Abs(battery.CurrentOutput - battery.CurrentInput); // Total power output in MW

            float tNorm = MathHelper.Clamp(HeatSession.Api.Utils.GetHeat(battery) / Config.Instance.CRITICAL_TEMP, 0f, 1f);
            float resistanceMultiplier = MathHelper.Lerp(1f, 1.2f, tNorm * tNorm); // More exponential rise

            float internalResistance = powerToHeatRatio * resistanceMultiplier;

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

        public override float GetHeatChange(float deltaTime)
        {
            if (_battery == null)
                return 0f;

            float heatGain = CalculateInternalHeatGain(_battery, deltaTime);
            float heatLoss = CalculateAmbientHeatLoss(_battery, deltaTime);

            return heatGain - heatLoss;
        }

        public override void Cleanup()
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

            var neighborStringBuilder = new StringBuilder();
            float cumulativeNeighborHeatChange;
            float cumulativeNetworkHeatChange;
            AddNeighborAndNetworksInfo(_battery, neighborStringBuilder, out cumulativeNeighborHeatChange, out cumulativeNetworkHeatChange);

            var heat = HeatSession.Api.Utils.GetHeat(block);
            float heatChange = GetHeatChange(1f) - cumulativeNeighborHeatChange - cumulativeNetworkHeatChange; // Assuming deltaTime of 1 second for display purposes

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
            info.Append(neighborStringBuilder);
        }

        void ExplodeBatteryInstantly(IMyBatteryBlock battery)
        {
            var slimBlock = battery.SlimBlock;
            if (slimBlock == null)
                return;

            slimBlock.DoDamage(1000000f, MyDamageType.Explosion, true);
        }

        public override void SpreadHeat(float deltaTime)
        {
           SpreadHeatStandard(_battery, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
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