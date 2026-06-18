using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipConnector), false)]
    public class ConnectorHeatComponent : AHeatGameLogicComponent
    {
        private static bool _controlsRegistered = false;

        private IMyShipConnector Connector => (IMyShipConnector)Entity;

        private float _cachedTempChange;
        private bool _hasCachedValue = false;

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            Connector.SetDetailedInfoDirty();

            if (!_controlsRegistered)
            {
                _controlsRegistered = true;
                HeatSession.Api.Utils.TryRegister<IMyShipConnector>();
            }
        }

        protected override void OnAttachedToScene()
        {
            Connector.AppendingCustomInfo -= GetCustomInfo;
            Connector.AppendingCustomInfo += GetCustomInfo;
        }

        public override void Cleanup()
        {
            if (Entity != null)
                Connector.AppendingCustomInfo -= GetCustomInfo;
        }

        public override float GetHeatChange(float deltaTime)
        {
            if (_hasCachedValue)
            {
                _hasCachedValue = false;
                return _cachedTempChange;
            }
            return CalculateHeatChange(deltaTime, true);
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(Connector, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(Connector, HeatSession.Api.Utils.GetHeat(Connector));
            Connector.SetDetailedInfoDirty();
            Connector.RefreshCustomInfo();
        }

        public void ApplyHeatDirectly(float tempChange)
        {
            _cachedTempChange = tempChange;
            _hasCachedValue = true;
        }

        private float CalculateHeatChange(float deltaTime, bool save)
        {
            if (!Connector.IsConnected)
                return 0f;

            var counterConnector = Connector.OtherConnector;
            var counterManager = HeatSession.GetBehaviorForBlock(counterConnector);

            var energyTransferred = HeatSession.Api.Utils.GetExchangeWithNeighbor(Connector, counterConnector, deltaTime, Config.Instance.HEATPIPE_CONDUCTIVITY);

            var capacityOwn = HeatSession.Api.Utils.GetThermalCapacity(Connector);
            var counterCapacity = HeatSession.Api.Utils.GetThermalCapacity(counterConnector);
            var tempDiff = HeatSession.Api.Utils.GetHeat(Connector) - HeatSession.Api.Utils.GetHeat(counterConnector);

            energyTransferred = HeatSession.Api.Utils.ApplyExchangeLimit(energyTransferred, capacityOwn, counterCapacity, tempDiff);

            float deltaOwn = energyTransferred / capacityOwn;
            float deltaNeighbor = energyTransferred / counterCapacity;

            if (save)
            {
                var counterComponent = counterManager as ConnectorHeatComponent;
                if (counterComponent != null)
                    counterComponent.ApplyHeatDirectly(deltaNeighbor);
            }

            return -deltaOwn;
        }

        private void GetCustomInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float heat = HeatSession.Api.Utils.GetHeat(Connector);

            var neighborStringBuilder = new StringBuilder();
            float neighborCum, networkCum;
            AddNeighborAndNetworksInfo(Connector, neighborStringBuilder, out neighborCum, out networkCum);

            float ownChange = CalculateHeatChange(1f, false);
            float heatChange = ownChange - neighborCum - networkCum;

            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F1} °C");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.AppendLine($"  Connected grid: {ownChange:+0.00;-0.00;0.00} °C/s");
            builder.Append(neighborStringBuilder);
        }
    }
}
