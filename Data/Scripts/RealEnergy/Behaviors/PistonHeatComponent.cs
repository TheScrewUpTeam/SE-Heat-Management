using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_PistonBase), false)]
    public class PistonBaseHeatComponent : AHeatGameLogicComponent, IDirectHeatAcceptor
    {
        private static bool _controlsRegistered = false;

        private IMyPistonBase PistonBase => (IMyPistonBase)Entity;

        private float _lastHeatChange = 0f;

        private float HeatConductivity => Config.Instance.HEATPIPE_CONDUCTIVITY;

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            PistonBase.AppendingCustomInfo += GetCustomInfo;
            PistonBase.SetDetailedInfoDirty();
            PistonBase.RefreshCustomInfo();

            if (!_controlsRegistered)
            {
                _controlsRegistered = true;
                HeatSession.Api.Utils.TryRegister<IMyPistonBase>();
            }
        }

        public override void Cleanup()
        {
            if (Entity != null)
                PistonBase.AppendingCustomInfo -= GetCustomInfo;
        }

        public override float GetHeatChange(float deltaTime)
        {
            if (_lastHeatChange != 0f)
            {
                var temp = _lastHeatChange;
                _lastHeatChange = 0f;
                return temp;
            }
            return CalculateHeatChange(deltaTime);
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(PistonBase, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(PistonBase, HeatSession.Api.Utils.GetHeat(PistonBase));
            PistonBase.SetDetailedInfoDirty();
            PistonBase.RefreshCustomInfo();
        }

        public void ApplyHeatChange(float heatChange)
        {
            _lastHeatChange += heatChange;
        }

        private float CalculateHeatChange(float deltaTime)
        {
            var counterparty = GetCounterpartyBehavior();
            if (counterparty == null) return 0f;

            var energyTransferred = HeatSession.Api.Utils.GetExchangeWithNeighbor(PistonBase, counterparty.Block, deltaTime, HeatConductivity);
            var capacityOwn = HeatSession.Api.Utils.GetThermalCapacity(PistonBase);
            var counterCapacity = HeatSession.Api.Utils.GetThermalCapacity(counterparty.Block);
            var tempDiff = HeatSession.Api.Utils.GetHeat(PistonBase) - HeatSession.Api.Utils.GetHeat(counterparty.Block);

            energyTransferred = HeatSession.Api.Utils.ApplyExchangeLimit(energyTransferred, capacityOwn, counterCapacity, tempDiff);

            float deltaOwn = energyTransferred / capacityOwn;
            float deltaNeighbor = energyTransferred / counterCapacity;

            (counterparty as IDirectHeatAcceptor)?.ApplyHeatChange(deltaNeighbor);

            return -deltaOwn;
        }

        private IHeatBehavior GetCounterpartyBehavior()
        {
            var top = PistonBase.Top as IMyPistonTop;
            return top != null ? HeatSession.GetBehaviorForBlock(top) : null;
        }

        private void GetCustomInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float heat = HeatSession.Api.Utils.GetHeat(PistonBase);

            var neighborStringBuilder = new StringBuilder();
            float neighborCum, networkCum;
            AddNeighborAndNetworksInfo(PistonBase, neighborStringBuilder, out neighborCum, out networkCum);

            float heatChange = CalculateHeatChange(1f) - neighborCum - networkCum;
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {heat:F1} °C");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.AppendLine($"  Connected grid: {CalculateHeatChange(1f):F1} °C");
            builder.Append(neighborStringBuilder);
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_PistonTop), false)]
    public class PistonHeadHeatComponent : AHeatGameLogicComponent, IDirectHeatAcceptor
    {
        private IMyPistonTop PistonTop => (IMyPistonTop)Entity;

        private float _lastHeatChange = 0f;

        private float HeatConductivity => Config.Instance.HEATPIPE_CONDUCTIVITY;

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
        }

        public override void Cleanup() { }

        public override float GetHeatChange(float deltaTime)
        {
            var temp = _lastHeatChange;
            _lastHeatChange = 0f;
            return temp;
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(PistonTop, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(PistonTop, HeatSession.Api.Utils.GetHeat(PistonTop));
        }

        public void ApplyHeatChange(float heatChange)
        {
            _lastHeatChange += heatChange;
        }

        private float CalculateHeatChange(float deltaTime)
        {
            var counterparty = GetCounterpartyBehavior();
            if (counterparty == null) return 0f;

            var energyTransferred = HeatSession.Api.Utils.GetExchangeWithNeighbor(PistonTop, counterparty.Block, deltaTime, HeatConductivity);
            var capacityOwn = HeatSession.Api.Utils.GetThermalCapacity(PistonTop);
            var counterCapacity = HeatSession.Api.Utils.GetThermalCapacity(counterparty.Block);
            var tempDiff = HeatSession.Api.Utils.GetHeat(PistonTop) - HeatSession.Api.Utils.GetHeat(counterparty.Block);

            energyTransferred = HeatSession.Api.Utils.ApplyExchangeLimit(energyTransferred, capacityOwn, counterCapacity, tempDiff);

            float deltaOwn = energyTransferred / capacityOwn;
            float deltaNeighbor = energyTransferred / counterCapacity;

            (counterparty as IDirectHeatAcceptor)?.ApplyHeatChange(deltaNeighbor);

            return -deltaOwn;
        }

        private IHeatBehavior GetCounterpartyBehavior()
        {
            var basePart = PistonTop.Base as IMyPistonBase;
            return basePart != null ? HeatSession.GetBehaviorForBlock(basePart) : null;
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ExtendedPistonBase), false)]
    public class ExtendedPistonBaseHeatComponent : PistonBaseHeatComponent { }
}
