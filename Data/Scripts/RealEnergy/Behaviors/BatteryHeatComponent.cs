using System;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_BatteryBlock), false)]
    public class BatteryHeatComponent : AHeatGameLogicComponent
    {
        private IMyBatteryBlock Battery => (IMyBatteryBlock)Entity;

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            Battery.AppendingCustomInfo += AppendBatteryHeatInfo;
            BatteryTerminalControls.Register();
        }

        public override void Cleanup()
        {
            if (Entity != null)
                Battery.AppendingCustomInfo -= AppendBatteryHeatInfo;
        }

        public override float GetHeatChange(float deltaTime)
        {
            if (Entity == null) return 0f;
            return CalculateInternalHeatGain(Battery, deltaTime) - CalculateAmbientHeatLoss(Battery, deltaTime);
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(Battery, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            Battery.SetDetailedInfoDirty();
            Battery.RefreshCustomInfo();
            HeatSession.Api.Effects.UpdateBlockHeatLight(Battery, heat);
            if (heat > Config.Instance.SMOKE_TRESHOLD)
                HeatSession.Api.Effects.InstantiateSmoke(Battery);
            else
                HeatSession.Api.Effects.RemoveSmoke(Battery);

            if (heat > Config.Instance.CRITICAL_TEMP && MyAPIGateway.Multiplayer.IsServer)
                Battery.SlimBlock?.DoDamage(1000000f, MyDamageType.Explosion, true);
        }

        private float CalculateInternalHeatGain(IMyBatteryBlock battery, float deltaTime)
        {
            float powerToHeatRatio = Config.Instance.DISCHARGE_HEAT_FRACTION;
            if (!Config.Instance.DISCHARGE_HEAT_CONFIGURABLE)
            {
                var def = battery.SlimBlock.BlockDefinition as MyBatteryBlockDefinition;
                powerToHeatRatio = 1 - def.RechargeMultiplier;
            }

            float thermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(battery);
            float outputMW = Math.Abs(battery.CurrentOutput - battery.CurrentInput);
            float tNorm = MathHelper.Clamp(HeatSession.Api.Utils.GetHeat(battery) / Config.Instance.CRITICAL_TEMP, 0f, 1f);
            float internalResistance = powerToHeatRatio * MathHelper.Lerp(1f, 1.2f, tNorm * tNorm);

            return outputMW * 1000000f * deltaTime * internalResistance / thermalCapacity;
        }

        private float CalculateAmbientHeatLoss(IMyBatteryBlock battery, float deltaTime)
        {
            float thermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(battery);
            float currentHeat = HeatSession.Api.Utils.GetHeat(battery);
            float ambientTemp = HeatSession.Api.Utils.CalculateAmbientTemperature(battery);
            float windMultiplier = 1f + Config.Instance.WIND_COOLING_MULT * HeatSession.Api.Utils.GetBlockWindSpeed(battery);

            float energyLoss = (currentHeat - ambientTemp) * HeatSession.Api.Utils.GetRealSurfaceArea(battery)
                               * Config.Instance.HEAT_COOLDOWN_COEFF * windMultiplier * deltaTime;
            return energyLoss / thermalCapacity;
        }

        private void AppendBatteryHeatInfo(IMyTerminalBlock block, StringBuilder info)
        {
            var battery = block as IMyBatteryBlock;
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float heat = HeatSession.Api.Utils.GetHeat(block);

            var neighborInfo = new StringBuilder();
            float cumulativeNeighbor, cumulativeNetwork;
            AddNeighborAndNetworksInfo(Battery, neighborInfo, out cumulativeNeighbor, out cumulativeNetwork);

            float heatChange = GetHeatChange(1f) - cumulativeNeighbor - cumulativeNetwork;
            int mode = _gridHeatComponent?.GetScaleBasedOnBlocksCount() ?? 1;

            info.AppendLine($"--- Heat Management ---");
            if (mode > 1)
                info.AppendLine($"(Slow Mode x{mode}) Update in: {_gridHeatComponent.GetTicksTillNextUpdate() * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS} seconds");

            info.AppendLine($"Temperature: {heat:F2} °C");
            info.AppendLine($"Thermal Status: {(heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable")}");
            info.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            info.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            info.AppendLine($"Cooling Area: {HeatSession.Api.Utils.GetRealSurfaceArea(block):F1} m²");
            info.AppendLine($"Density: {HeatSession.Api.Utils.GetDensity(block):F1} kg/m³");
            info.AppendLine($"Wind Speed: {HeatSession.Api.Utils.GetBlockWindSpeed(block):F2} m/s");

            if (heatChange > 0f)
            {
                float timeToOverheat = (Config.Instance.CRITICAL_TEMP - heat) / heatChange;
                if (!float.IsNaN(timeToOverheat) && !float.IsInfinity(timeToOverheat))
                    info.AppendLine($"Estimated Time to Overheat: {TimeSpan.FromSeconds(timeToOverheat):hh\\:mm\\:ss}");
            }
            else
            {
                float timeToCoolDown = (heat - HeatSession.Api.Utils.CalculateAmbientTemperature(Battery)) / Math.Abs(heatChange);
                if (!float.IsNaN(timeToCoolDown) && !float.IsInfinity(timeToCoolDown))
                    info.AppendLine($"Estimated Time to cool down: {TimeSpan.FromSeconds(timeToCoolDown):hh\\:mm\\:ss}");
            }

            info.AppendLine("");
            info.AppendLine("Heat Sources:");
            info.AppendLine($"  Internal Use: {CalculateInternalHeatGain(battery, 1):F2} °C/s");
            info.AppendLine($"  Air Exchange: {-CalculateAmbientHeatLoss(battery, 1):+0.00;-0.00;0.00} °C/s");
            info.Append(neighborInfo);
        }
    }
}
