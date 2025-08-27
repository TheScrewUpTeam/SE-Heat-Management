using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    // Factory for registering ExhaustHeatManager
    public class ExhaustHeatManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyExhaustBlock> exhausts = new List<IMyExhaustBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(exhausts);

            foreach (var exhaust in exhausts)
            {
                if (!behaviorMap.ContainsKey(exhaust))
                {
                    behaviorMap[exhaust] = new ExhaustHeatManager(exhaust, manager);
                }
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            result.AffectedBlocks = new List<IMyCubeBlock> { block };

            if (block is IMyExhaustBlock)
            {
                result.Behavior = new ExhaustHeatManager(block as IMyExhaustBlock, manager);
                return result;
            }
            return result; // No behavior created for non-exhaust blocks
        }

        public int Priority => 25; // Between vents and thrusters
    }

    // Manages heat for exhaust blocks 
    public class ExhaustHeatManager : AHeatBehavior
    {
        private IGridHeatManager _gridManager;
        private IMyExhaustBlock _exhaust;

        public ExhaustHeatManager(IMyExhaustBlock exhaust, IGridHeatManager manager)
        {
            _exhaust = exhaust;
            _gridManager = manager;
            _exhaust.AppendingCustomInfo += AppendExhaustHeatInfo;
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
            float heatChange = GetHeatChange(1f) - cumulativeNeighborHeatChange - cumulativeNetworkHeatChange; // Assuming deltaTime of 1 second for display purposes

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

        public override float GetHeatChange(float deltaTime)
        {
            if (_exhaust == null)
                return 0f;

            float change = HeatSession.Api.Utils.GetAmbientHeatLoss(_exhaust, deltaTime);

            // You may want to implement a custom method for exhausts here
            if (_exhaust.IsWorking)
            {
                change = HeatSession.Api.Utils.GetActiveExhaustHeatLoss(_exhaust, deltaTime);
            }
            return -change;
        }

        public override void Cleanup()
        {
            if (_exhaust != null)
            {
                _exhaust.AppendingCustomInfo -= AppendExhaustHeatInfo;
                _exhaust = null;
            }
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
    }
} 