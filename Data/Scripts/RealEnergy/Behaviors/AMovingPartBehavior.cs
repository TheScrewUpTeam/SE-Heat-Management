using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public abstract class AMovingPartHeatManager<T> : AHeatBehavior, IDirectHeatAcceptor where T : IMyCubeBlock
    {
        protected T _part;
        private float _lastHeatChange = 0f;

        public AMovingPartHeatManager(T part, bool SkipCustomInfoRegistration = false)
        {
            _part = part;
            if (_part is IMyTerminalBlock && !SkipCustomInfoRegistration)
            {
                (_part as IMyTerminalBlock).AppendingCustomInfo += GetCustomInfo;
            }
        }

        private void GetCustomInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            
            float heat = HeatSession.Api.Utils.GetHeat(_part);
            var neighborStringBuilder = new StringBuilder();
            float neighborCum;
            float networkCum;
            AddNeighborAndNetworksInfo(_part, neighborStringBuilder, out neighborCum, out networkCum);
            float heatChange = GetHeatChange(1f) - neighborCum - networkCum; // Assuming deltaTime of 1 second for display purposes

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F1} °C");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.Append(neighborStringBuilder);
        }

        public override void Cleanup()
        {
            if (_part is IMyTerminalBlock)
                (_part as IMyTerminalBlock).AppendingCustomInfo -= GetCustomInfo;
            _part = default(T);
        }

        public override float GetHeatChange(float deltaTime) {
            if (_lastHeatChange != 0f)
            {
                var temp = _lastHeatChange;
                _lastHeatChange = 0f;
                return temp;
            }
            var counterpartyBehavior = GetCounterpartyHeatManager();
            if (counterpartyBehavior != null)
            {
                var energyTransferred = HeatSession.Api.Utils.GetExchangeWithNeighbor(_part, counterpartyBehavior.Block, deltaTime, HeatConductivity);

                var capacityOwn = HeatSession.Api.Utils.GetThermalCapacity(_part);
                var counterCapacity = HeatSession.Api.Utils.GetThermalCapacity(counterpartyBehavior.Block);
                var tempDiff = HeatSession.Api.Utils.GetHeat(_part) - HeatSession.Api.Utils.GetHeat(counterpartyBehavior.Block);

                energyTransferred = HeatSession.Api.Utils.ApplyExchangeLimit(energyTransferred, capacityOwn, counterCapacity, tempDiff);
                
                float deltaOwn = energyTransferred / capacityOwn;
                float deltaNeighbor = energyTransferred / counterCapacity;

                if (counterpartyBehavior is IDirectHeatAcceptor)
                {
                    (counterpartyBehavior as IDirectHeatAcceptor).ApplyHeatChange(-deltaNeighbor);
                }
                return deltaOwn;
            }
            return 0f;
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(_part, HeatSession.Api.Utils.GetHeat(_part));
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(_part, deltaTime);
        }

        public void ApplyHeatChange(float heatChange)
        {
            _lastHeatChange += heatChange;
        }

        abstract protected IHeatBehavior GetCounterpartyHeatManager();

        abstract protected float HeatConductivity { get; }
    }
}