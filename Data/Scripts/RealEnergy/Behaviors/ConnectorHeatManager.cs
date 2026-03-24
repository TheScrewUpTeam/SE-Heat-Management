using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public class ConnectorHeatManagerFactory : IHeatBehaviorFactory
    {
        private static bool _propertyAdded = false;

        public int Priority => 15;

        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            var connectors = grid.GetFatBlocks<IMyShipConnector>();
            foreach (var connector in connectors)
            {
                if (behaviorMap.ContainsKey(connector))
                    continue;
                var behavior = new ConnectorHeatManager(connector);
                behaviorMap[connector] = behavior;
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            if (!(block is IMyShipConnector))
                return result;

            var existing = HeatSession.GetBehaviorForBlock(block);
            if (existing != null)
                return result;

            result.Behavior = new ConnectorHeatManager(block as IMyShipConnector);
            result.AffectedBlocks = new List<IMyCubeBlock> { block };
            return result;
        }

        public void RegisterCustomControls()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter += (block, controls) =>
            {
                // if (!(block is IMyShipConnector))
                //     return;

                // Only add if it doesn't already exist
                if (controls.Any(c => c.Id == "HeatTemperature") || _propertyAdded)
                    return;
                
                _propertyAdded = true;

                HeatSession.Api.Utils.TryRegister<IMyShipConnector>();
            };
        }
    }

    public class ConnectorHeatManager : AHeatBehavior
    {
        private IMyShipConnector _block;
        private float _cachedTempChange;
        private bool _hasCachedValue = false;
        public override IMyCubeBlock Block
        {
            get { return _block; }
        }
        
        public ConnectorHeatManager (IMyShipConnector block)
        {
            _block = block;
            _block.AppendingCustomInfo += GetCustomInfo;
            _block.SetDetailedInfoDirty();
        }

        public override void Cleanup()
        {
            _block.AppendingCustomInfo -= GetCustomInfo;
            _block = null;
        }

        private void GetCustomInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);

            float heat = HeatSession.Api.Utils.GetHeat(_block);
            var neighborStringBuilder = new StringBuilder();
            var ownChange = CalculateHeatChange(1f, false);
            float neighborCum;
            float networkCum;
            AddNeighborAndNetworksInfo(_block, neighborStringBuilder, out neighborCum, out networkCum);
            float heatChange = ownChange - neighborCum - networkCum; // Assuming deltaTime of 1 second for display purposes

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F1} °C");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.AppendLine($"  Connected grid: {ownChange:+0.00;-0.00;0.00} °C/s");
            builder.Append(neighborStringBuilder);
        }
        
        public float CalculateHeatChange(float deltaTime, bool save = true)
        {
            if (!_block.IsConnected)
                return 0f;
            var counterConnector = _block.OtherConnector;
            var counterManager = HeatSession.GetBehaviorForBlock(counterConnector);
            var energyTransferred = HeatSession.Api.Utils.GetExchangeWithNeighbor(_block, counterConnector, deltaTime, Config.Instance.HEATPIPE_CONDUCTIVITY);

            var capacityOwn = HeatSession.Api.Utils.GetThermalCapacity(_block);
            var counterCapacity = HeatSession.Api.Utils.GetThermalCapacity(counterConnector);
            var tempDiff = HeatSession.Api.Utils.GetHeat(_block) - HeatSession.Api.Utils.GetHeat(counterConnector);

            energyTransferred = HeatSession.Api.Utils.ApplyExchangeLimit(energyTransferred, capacityOwn, counterCapacity, tempDiff);
            
            float deltaOwn = energyTransferred / capacityOwn;
            float deltaNeighbor = energyTransferred / counterCapacity;

            if (counterManager is ConnectorHeatManager && save)
            {
                (counterManager as ConnectorHeatManager).ApplyHeatDirectly(deltaNeighbor);
            }

            return -deltaOwn;
        }

        public override float GetHeatChange(float deltaTime)
        {
            if (_hasCachedValue)
            {
                _hasCachedValue = false;
                return _cachedTempChange;
            }

            return CalculateHeatChange(deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(_block, HeatSession.Api.Utils.GetHeat(_block));
            _block.SetDetailedInfoDirty();
            _block.RefreshCustomInfo();
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(_block, deltaTime);
        }

        public void ApplyHeatDirectly(float tempChange)
        {
            _cachedTempChange = tempChange;
            _hasCachedValue = true;
        }
    }
}