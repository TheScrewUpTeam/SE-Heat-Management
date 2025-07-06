using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public class VentHeatManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyAirVent> vents = new List<IMyAirVent>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(vents);

            foreach (var vent in vents)
            {
                if (!behaviorMap.ContainsKey(vent))
                {
                    behaviorMap[vent] = new VentHeatManager(vent);
                }
            }
        }

        public IHeatBehavior OnBlockAdded(IMyCubeBlock block)
        {
            if (block is IMyAirVent)
            {
                return new VentHeatManager(block as IMyAirVent);
            }
            return null; // No behavior created for non-vent blocks
        }

        public int Priority => 20; // Vents are less critical than batteries
    }
    
    public class VentHeatManager : IHeatBehavior
    {
        private IMyAirVent _vent;

        public VentHeatManager(IMyAirVent vent)
        {
            _vent = vent;
            _vent.AppendingCustomInfo += AppendVentHeatInfo;
        }

        private void AppendVentHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            var heat = HeatSession.Api.Utils.GetHeat(block);
            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F2} °C");
            builder.AppendLine($"Air Heat Change: {GetHeatChange(1):F2} °C/s");
            string exchangeMode = _vent.IsFunctional && _vent.Enabled ? "Active" : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {HeatSession.Api.Utils.GetThermalCapacity(block) / 1000000:F1} MJ/°C");
            builder.AppendLine($"Ambient temp: {HeatSession.Api.Utils.CalculateAmbientTemperature(block):F1} °C");
            builder.AppendLine($"Air density: {(block as IMyAirVent).GetOxygenLevel() * 100:F1} %");
            float windSpeed = HeatSession.Api.Utils.GetBlockWindSpeed(block);
            builder.AppendLine($"Wind Speed: {windSpeed:F2} m/s");
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
            return; // Vents do not spread heat
        }

        public void ReactOnNewHeat(float heat)
        {
            return; // Vents do not react to new heat in this implementation
        }
    }
}