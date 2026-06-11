using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorStator), false)]
    public class MotorStatorHeatComponent : AHeatGameLogicComponent, IDirectHeatAcceptor
    {
        private static bool _controlsRegistered = false;

        private IMyMotorStator Stator => (IMyMotorStator)Entity;

        private float _lastHeatChange = 0f;

        private float HeatConductivity =>
            Stator is IMyMotorAdvancedStator
                ? Config.Instance.HEATPIPE_CONDUCTIVITY
                : Config.Instance.THERMAL_CONDUCTIVITY;

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            Stator.AppendingCustomInfo += GetCustomInfo;
            Stator.SetDetailedInfoDirty();
            Stator.RefreshCustomInfo();

            if (!_controlsRegistered)
            {
                _controlsRegistered = true;
                HeatSession.Api.Utils.TryRegister<IMyMotorStator>();
            }
        }

        public override void Cleanup()
        {
            if (Entity != null)
                Stator.AppendingCustomInfo -= GetCustomInfo;
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
            SpreadHeatStandard(Stator, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(Stator, HeatSession.Api.Utils.GetHeat(Stator));
            Stator.SetDetailedInfoDirty();
            Stator.RefreshCustomInfo();
        }

        public void ApplyHeatChange(float heatChange)
        {
            _lastHeatChange += heatChange;
        }

        private float CalculateHeatChange(float deltaTime)
        {
            var counterparty = GetCounterpartyBehavior();
            if (counterparty == null) return 0f;

            var energyTransferred = HeatSession.Api.Utils.GetExchangeWithNeighbor(Stator, counterparty.Block, deltaTime, HeatConductivity);
            var capacityOwn = HeatSession.Api.Utils.GetThermalCapacity(Stator);
            var counterCapacity = HeatSession.Api.Utils.GetThermalCapacity(counterparty.Block);
            var tempDiff = HeatSession.Api.Utils.GetHeat(Stator) - HeatSession.Api.Utils.GetHeat(counterparty.Block);

            energyTransferred = HeatSession.Api.Utils.ApplyExchangeLimit(energyTransferred, capacityOwn, counterCapacity, tempDiff);

            float deltaOwn = energyTransferred / capacityOwn;
            float deltaNeighbor = energyTransferred / counterCapacity;

            (counterparty as IDirectHeatAcceptor)?.ApplyHeatChange(deltaNeighbor);

            return -deltaOwn;
        }

        private IHeatBehavior GetCounterpartyBehavior()
        {
            var top = Stator.Top as IMyMotorRotor;
            return top != null ? HeatSession.GetBehaviorForBlock(top) : null;
        }

        private void GetCustomInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);
            float heat = HeatSession.Api.Utils.GetHeat(Stator);

            var neighborStringBuilder = new StringBuilder();
            float neighborCum, networkCum;
            AddNeighborAndNetworksInfo(Stator, neighborStringBuilder, out neighborCum, out networkCum);

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

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorRotor), false)]
    public class MotorRotorHeatComponent : AHeatGameLogicComponent, IDirectHeatAcceptor
    {
        private IMyMotorRotor Rotor => (IMyMotorRotor)Entity;

        private float _lastHeatChange = 0f;

        private float HeatConductivity =>
            Rotor is IMyMotorAdvancedRotor
                ? Config.Instance.HEATPIPE_CONDUCTIVITY
                : Config.Instance.THERMAL_CONDUCTIVITY;

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
            SpreadHeatStandard(Rotor, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(Rotor, HeatSession.Api.Utils.GetHeat(Rotor));
        }

        public void ApplyHeatChange(float heatChange)
        {
            _lastHeatChange += heatChange;
        }

        private float CalculateHeatChange(float deltaTime)
        {
            var counterparty = GetCounterpartyBehavior();
            if (counterparty == null) return 0f;

            var energyTransferred = HeatSession.Api.Utils.GetExchangeWithNeighbor(Rotor, counterparty.Block, deltaTime, HeatConductivity);
            var capacityOwn = HeatSession.Api.Utils.GetThermalCapacity(Rotor);
            var counterCapacity = HeatSession.Api.Utils.GetThermalCapacity(counterparty.Block);
            var tempDiff = HeatSession.Api.Utils.GetHeat(Rotor) - HeatSession.Api.Utils.GetHeat(counterparty.Block);

            energyTransferred = HeatSession.Api.Utils.ApplyExchangeLimit(energyTransferred, capacityOwn, counterCapacity, tempDiff);

            float deltaOwn = energyTransferred / capacityOwn;
            float deltaNeighbor = energyTransferred / counterCapacity;

            (counterparty as IDirectHeatAcceptor)?.ApplyHeatChange(deltaNeighbor);

            return -deltaOwn;
        }

        private IHeatBehavior GetCounterpartyBehavior()
        {
            var basePart = Rotor.Base as IMyMotorStator;
            return basePart != null ? HeatSession.GetBehaviorForBlock(basePart) : null;
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorAdvancedStator), false)]
    public class MotorAdvancedStatorHeatComponent : MotorStatorHeatComponent { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MotorAdvancedRotor), false)]
    public class MotorAdvancedRotorHeatComponent : MotorRotorHeatComponent { }
}
