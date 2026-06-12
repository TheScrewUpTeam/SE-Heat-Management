using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]
    public class ThrusterHeatComponent : AHeatGameLogicComponent
    {
        private const string THRUSTER_CODE = "AtmosphericThrust";
        private static bool _controlsRegistered = false;

        private IMyThrust Thruster => (IMyThrust)Entity;

        public override void UpdateOnceBeforeFrame()
        {
            if (!Thruster.BlockDefinition.SubtypeName.Contains(THRUSTER_CODE))
                return;

            base.UpdateOnceBeforeFrame();
            Thruster.SetDetailedInfoDirty();

            if (!_controlsRegistered)
            {
                _controlsRegistered = true;
                HeatSession.Api.Utils.TryRegister<IMyThrust>();
            }
        }

        protected override void OnAttachedToScene()
        {
            Thruster.AppendingCustomInfo -= AppendThrusterHeatInfo;
            Thruster.AppendingCustomInfo += AppendThrusterHeatInfo;
        }

        public override void Cleanup()
        {
            if (Entity != null)
                Thruster.AppendingCustomInfo -= AppendThrusterHeatInfo;
        }

        public override float GetHeatChange(float deltaTime)
        {
            float change = HeatSession.Api.Utils.GetAmbientHeatLoss(Thruster, deltaTime);
            float thrustRatio = (Thruster.MaxThrust > 0f) ? (Thruster.CurrentThrust / Thruster.MaxThrust) : 0f;

            if (Thruster.IsFunctional && Thruster.Enabled && thrustRatio > 0f)
                change = HeatSession.Api.Utils.GetActiveThrusterHeatLoss(Thruster, thrustRatio, deltaTime);

            return -change;
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(Thruster, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            Thruster.RefreshCustomInfo();
            Thruster.SetDetailedInfoDirty();
        }

        private void AppendThrusterHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float heat = HeatSession.Api.Utils.GetHeat(block);
            float capacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float ambient = HeatSession.Api.Utils.CalculateAmbientTemperature(block);
            float outputRatio = (Thruster.MaxThrust > 0f) ? (Thruster.CurrentThrust / Thruster.MaxThrust) : 0f;

            var neighborStringBuilder = new StringBuilder();
            float cumulativeNeighborHeatChange;
            float cumulativeNetworkHeatChange;
            AddNeighborAndNetworksInfo(Thruster, neighborStringBuilder, out cumulativeNeighborHeatChange, out cumulativeNetworkHeatChange);
            float heatChange = GetHeatChange(1f) - cumulativeNeighborHeatChange - cumulativeNetworkHeatChange;

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F2} °C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            string exchangeMode = outputRatio > 0f ? "Active" : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {capacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Thrust output: {outputRatio * 100:F1} %");
            builder.AppendLine($"Ambient temp: {ambient:F1} °C");
            float windSpeed = HeatSession.Api.Utils.GetBlockWindSpeed(block);
            builder.AppendLine($"Wind Speed: {windSpeed:F2} m/s");
            builder.AppendLine($"------");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.AppendLine($"  Air Exchange: {GetHeatChange(1):+0.00;-0.00;0.00} °C/s");
            builder.Append(neighborStringBuilder);
        }
    }
}
