using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public class ThrusterHeatManagerFactory : IHeatBehaviorFactory
    {
        const string THRUSTER_CODE = "AtmosphericThrust";

        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyThrust> thrusters = new List<IMyThrust>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(thrusters);

            foreach (var thruster in thrusters)
            {
                if (!behaviorMap.ContainsKey(thruster) && thruster.BlockDefinition.SubtypeName.Contains(THRUSTER_CODE))
                {
                    behaviorMap[thruster] = new ThrusterHeatManager(thruster, manager);
                }
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            result.AffectedBlocks = new List<IMyCubeBlock> { block };

            if (block is IMyThrust && block.BlockDefinition.SubtypeName.Contains(THRUSTER_CODE))
            {
                result.Behavior = new ThrusterHeatManager(block as IMyThrust, manager);
                return result;
            }
            return result; // No behavior created for non-thruster blocks
        }

        public int Priority => 30; // Thrusters are less critical than batteries and vents
    }

    public class ThrusterHeatManager : AHeatBehavior
    {
        private IGridHeatManager _gridManager;
        private IMyThrust _thruster;

        public ThrusterHeatManager(IMyThrust thruster, IGridHeatManager manager)
        {
            _thruster = thruster;
            _gridManager = manager;
            _thruster.AppendingCustomInfo += AppendThrusterHeatInfo;
        }

        private void AppendThrusterHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float heat = HeatSession.Api.Utils.GetHeat(block);
            float capacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float ambient = HeatSession.Api.Utils.CalculateAmbientTemperature(block);
            float outputRatio = (_thruster.MaxThrust > 0f) ? (_thruster.CurrentThrust / _thruster.MaxThrust) : 0f;

            var neighborStringBuilder = new StringBuilder();
            float cumulativeNeighborHeatChange;
            float cumulativeNetworkHeatChange;
            AddNeighborAndNetworksInfo(_thruster, neighborStringBuilder, out cumulativeNeighborHeatChange, out cumulativeNetworkHeatChange);
            float heatChange = GetHeatChange(1f) - cumulativeNeighborHeatChange - cumulativeNetworkHeatChange; // Assuming deltaTime of 1 second for display purposes

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

        public override float GetHeatChange(float deltaTime)
        {
            if (_thruster == null)
                return 0f;

            float change = HeatSession.Api.Utils.GetAmbientHeatLoss(_thruster, deltaTime);
            float thrustRatio = (_thruster.MaxThrust > 0f) ? (_thruster.CurrentThrust / _thruster.MaxThrust) : 0f;

            if (_thruster.IsFunctional && _thruster.Enabled && thrustRatio > 0f)
            {
                change = HeatSession.Api.Utils.GetActiveThrusterHeatLoss(_thruster, thrustRatio, deltaTime);
            }
            return -change;
        }

        public override void Cleanup()
        {
            if (_thruster != null)
            {
                _thruster.AppendingCustomInfo -= AppendThrusterHeatInfo;
                _thruster = null;
            }
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(_thruster, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            _thruster.RefreshCustomInfo();
            _thruster.SetDetailedInfoDirty();
        }
    }
}
