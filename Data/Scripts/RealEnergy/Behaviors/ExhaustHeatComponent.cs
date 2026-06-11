using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ExhaustBlock), false)]
    public class ExhaustHeatComponent : AHeatGameLogicComponent
    {
        private static bool _controlsRegistered = false;

        private IMyExhaustBlock _exhaust;

        public override void UpdateOnceBeforeFrame()
        {
            _exhaust = (IMyExhaustBlock)Entity;
            _exhaust.AppendingCustomInfo += AppendExhaustHeatInfo;

            base.UpdateOnceBeforeFrame();

            RegisterTerminalControls();
        }

        public override void Cleanup()
        {
            if (Entity != null)
                _exhaust.AppendingCustomInfo -= AppendExhaustHeatInfo;
        }

        public override float GetHeatChange(float deltaTime)
        {
            if (_exhaust == null)
                return 0f;

            float change = -HeatSession.Api.Utils.GetAmbientHeatLoss(_exhaust, deltaTime);

            if (_exhaust.IsWorking)
                change -= HeatSession.Api.Utils.GetActiveExhaustHeatLoss(_exhaust, deltaTime);

            return change;
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(_exhaust, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            _exhaust.RefreshCustomInfo();
            _exhaust.SetDetailedInfoDirty();
        }

        private void AppendExhaustHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float heat = HeatSession.Api.Utils.GetHeat(block);
            float capacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float ambient = HeatSession.Api.Utils.CalculateAmbientTemperature(block);

            var neighborStringBuilder = new StringBuilder();
            float cumulativeNeighborHeatChange;
            float cumulativeNetworkHeatChange;
            AddNeighborAndNetworksInfo(_exhaust, neighborStringBuilder, out cumulativeNeighborHeatChange, out cumulativeNetworkHeatChange);

            float airDensity = HeatSession.Api.Utils.GetAirDensity(_exhaust);
            float heatChange = GetHeatChange(1f) - cumulativeNeighborHeatChange - cumulativeNetworkHeatChange;

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F2} °C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            string exchangeMode = _exhaust.IsWorking ? "Active" : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {capacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Ambient temp: {ambient:F1} °C");
            builder.AppendLine($"Air density: {airDensity * 100:F1} %");
            builder.AppendLine($"------");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.AppendLine($"  Exhaust: {-HeatSession.Api.Utils.GetActiveExhaustHeatLoss(_exhaust, 1):+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"  Air Exchange: {-HeatSession.Api.Utils.GetAmbientHeatLoss(_exhaust, 1):+0.00;-0.00;0.00} °C/s");
            builder.Append(neighborStringBuilder);
        }

        private static void RegisterTerminalControls()
        {
            if (_controlsRegistered) return;
            _controlsRegistered = true;

            HeatSession.Api.Utils.TryRegister<IMyExhaustBlock>();
        }
    }
}
