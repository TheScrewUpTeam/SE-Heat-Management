using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public class HeatVentManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyHeatVent> vents = new List<IMyHeatVent>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(vents);

            foreach (var vent in vents)
            {
                if (!behaviorMap.ContainsKey(vent))
                {
                    behaviorMap[vent] = new HeatVentManager(vent, manager);
                }
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            result.AffectedBlocks = new List<IMyCubeBlock> { block };

            if (block is IMyHeatVent)
            {
                result.Behavior = new HeatVentManager(block as IMyHeatVent, manager);
                return result;
            }
            return result; // No behavior created for non-heat-vent blocks
        }

        public int Priority => 20; // Heat vents are less critical than batteries
    }
    
    public class HeatVentManager : AHeatBehavior
    {
        private IGridHeatManager _gridManager;
        private IMyHeatVent _vent;

        public HeatVentManager(IMyHeatVent vent, IGridHeatManager manager)
        {
            _vent = vent;
            _gridManager = manager;
            _vent.AppendingCustomInfo += AppendVentHeatInfo;
        }

        private void AppendVentHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);

            var neighborStringBuilder = new StringBuilder();
            float cumulativeNeighborHeatChange;
            float cumulativeNetworkHeatChange;
            AddNeighborAndNetworksInfo(_vent, neighborStringBuilder, out cumulativeNeighborHeatChange, out cumulativeNetworkHeatChange);

            var heat = HeatSession.Api.Utils.GetHeat(block);
            float heatChange = GetHeatChange(1f) - cumulativeNeighborHeatChange - cumulativeNetworkHeatChange; // Assuming deltaTime of 1 second for display purposes

            float airDensity = HeatSession.Api.Utils.GetAirDensity(_vent);

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {HeatSession.Api.Utils.GetHeat(block):F2} °C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            string exchangeMode = _vent.IsWorking ? "Active" : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Ambient temp: {HeatSession.Api.Utils.CalculateAmbientTemperature(block):F1} °C");
            builder.AppendLine($"Air density: {airDensity * 100:F1} %");
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
            if (_vent == null)
                return 0f;

            float change = HeatSession.Api.Utils.GetAmbientHeatLoss(_vent, deltaTime);
            if (_vent.IsWorking)
            {
                change += HeatSession.Api.Utils.GetActiveHeatVentLoss(_vent, deltaTime); // You may need to implement a custom method for IMyHeatVent
            }
            return -change;
        }

        public override void Cleanup()
        {
            if (_vent != null)
            {
                _vent.AppendingCustomInfo -= AppendVentHeatInfo;
                _vent = null;
            }
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(_vent, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            _vent.RefreshCustomInfo();
            _vent.SetDetailedInfoDirty();
        }
    }
} 