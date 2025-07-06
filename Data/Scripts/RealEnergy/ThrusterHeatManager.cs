using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public class ThrusterHeatManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyThrust> thrusters = new List<IMyThrust>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(thrusters);

            foreach (var thruster in thrusters)
            {
                if (!behaviorMap.ContainsKey(thruster))
                {
                    behaviorMap[thruster] = new ThrusterHeatManager(thruster);
                }
            }
        }

        public IHeatBehavior OnBlockAdded(IMyCubeBlock block)
        {
            if (block is IMyThrust)
            {
                return new ThrusterHeatManager(block as IMyThrust);
            }
            return null; // No behavior created for non-thruster blocks
        }

        public int Priority => 30; // Thrusters are less critical than batteries and vents
    }

    public class ThrusterHeatManager : IHeatBehavior
    {
        private IMyThrust _thruster;

        public ThrusterHeatManager(IMyThrust thruster)
        {
            _thruster = thruster;
            _thruster.AppendingCustomInfo += AppendThrusterHeatInfo;
        }

        private void AppendThrusterHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float heat = HeatSession.Api.Utils.GetHeat(block);
            float capacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float ambient = HeatSession.Api.Utils.CalculateAmbientTemperature(block);
            float outputRatio = (_thruster.MaxThrust > 0f) ? (_thruster.CurrentThrust / _thruster.MaxThrust) : 0f;

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F2} °C");
            builder.AppendLine($"Air Heat Change: {GetHeatChange(1):F2} °C/s");
            string exchangeMode = outputRatio > 0f ? "Active" : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {capacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Thrust output: {outputRatio * 100:F1} %");
            builder.AppendLine($"Ambient temp: {ambient:F1} °C");
            float windSpeed = HeatSession.Api.Utils.GetBlockWindSpeed(block);
            builder.AppendLine($"Wind Speed: {windSpeed:F2} m/s");
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
                if (_thruster.DisplayNameText.Contains("HeatDebug"))
                    MyAPIGateway.Utilities.ShowNotification($"Thruster: {_thruster.DisplayNameText}, thrustRatio: {thrustRatio}, change: {change}", 1000);
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
            return; // No specific reaction needed for thrusters
        }
    }
}
