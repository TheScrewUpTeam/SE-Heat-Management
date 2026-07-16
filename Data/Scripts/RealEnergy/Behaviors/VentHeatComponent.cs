using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AirVent), false)]
    public class VentHeatComponent : AHeatGameLogicComponent
    {
        private static bool _controlsRegistered = false;

        private IMyAirVent _vent;
        private MyResourceSourceComponent _sourceComp;

        public override void UpdateOnceBeforeFrame()
        {
            _vent = (IMyAirVent)Entity;
            _sourceComp = _vent.Components.Get<MyResourceSourceComponent>();
            base.UpdateOnceBeforeFrame();
            RegisterTerminalControls();
        }

        protected override void OnAttachedToScene()
        {
            _vent.AppendingCustomInfo -= AppendVentHeatInfo;
            _vent.AppendingCustomInfo += AppendVentHeatInfo;
        }

        public override void Cleanup()
        {
            if (Entity != null)
            {
                _vent.AppendingCustomInfo -= AppendVentHeatInfo;
                HeatSession.Api.Effects.RemoveSmoke(_vent);
            }
        }

        public static void SetO2Turbo(IMyCubeBlock block, float o2turbo)
        {
            block.Storage[Config.O2TurboKey] = o2turbo.ToString();
        }

        public static float GetO2Turbo(IMyCubeBlock block)
        {
            var vent = block as IMyAirVent;
            if (vent == null || vent.CanPressurize)
                return 0f;

            string turboStr;
            if (block.Storage != null && block.Storage.TryGetValue(Config.O2TurboKey, out turboStr))
            {
                float value;
                if (float.TryParse(turboStr, out value) && !float.IsNaN(value) && !float.IsInfinity(value))
                    return value;
            }

            return 0f;
        }

        public static float GetO2TurboMax(IMyTerminalBlock block)
        {
            return block.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 500f : 50f;
        }

        public float GetAmbientExchange(float deltaTime)
        {
            float change = HeatSession.Api.Utils.GetAmbientHeatLoss(_vent, deltaTime);
            if (_vent.IsWorking)
                change += HeatSession.Api.Utils.GetActiveVentHealLoss(_vent, deltaTime);
            return change;
        }

        public override float GetHeatChange(float deltaTime)
        {
            float change = GetAmbientExchange(deltaTime);
            var turboO2Usage = GetO2Turbo(_vent);
            if (_vent.IsWorking && turboO2Usage > 0)
            {
                var leftOvers = _gridHeatComponent != null
                    ? _gridHeatComponent.ConsumeO2(turboO2Usage * deltaTime, deltaTime, Block)
                    : turboO2Usage * deltaTime;
                if (leftOvers <= 0)
                {
                    change += turboO2Usage * deltaTime * Config.Instance.VENT_TURBO_COOLING_RATE / HeatSession.Api.Utils.GetThermalCapacity(_vent);
                    float normalizedStrength = turboO2Usage / GetO2TurboMax(_vent);
                    HeatSession.Api.Effects.InstantiateSteam(_vent, normalizedStrength);
                }
                else
                {
                    HeatSession.Api.Effects.RemoveSmoke(_vent);
                }
            }
            else
            {
                HeatSession.Api.Effects.RemoveSmoke(_vent);
            }
            return -change;
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(_vent, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(_vent, heat);
            _vent.RefreshCustomInfo();
        }

        private void AppendVentHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);

            var neighborStringBuilder = new StringBuilder();
            float cumulativeNeighborHeatChange;
            float cumulativeNetworkHeatChange;
            AddNeighborAndNetworksInfo(_vent, neighborStringBuilder, out cumulativeNeighborHeatChange, out cumulativeNetworkHeatChange);

            var turboO2Usage = GetO2Turbo(_vent);
            var supplyGranted = turboO2Usage > 0
                && _gridHeatComponent != null
                && _gridHeatComponent.HasEnoughO2(turboO2Usage, 1, Block);

            var o2Exchange = 0f;
            if (_vent.IsWorking && supplyGranted)
                o2Exchange = turboO2Usage * Config.Instance.VENT_TURBO_COOLING_RATE / ownThermalCapacity;

            float heatChange = -GetAmbientExchange(1f) - cumulativeNeighborHeatChange - cumulativeNetworkHeatChange - o2Exchange;

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {HeatSession.Api.Utils.GetHeat(block):F2} °C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            string exchangeMode = _vent.IsWorking
                ? (turboO2Usage > 0 && supplyGranted ? "Turbo" : "Active")
                : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Ambient temp: {HeatSession.Api.Utils.CalculateAmbientTemperature(block):F1} °C");
            builder.AppendLine($"Air density: {HeatSession.Api.Utils.GetAirDensity(_vent) * 100:F1} %");
            float windSpeed = HeatSession.Api.Utils.GetBlockWindSpeed(block);
            if (turboO2Usage > 0 && !supplyGranted)
                builder.AppendLine($"WARNING: Not enough O2, required: {turboO2Usage}L");
            builder.AppendLine($"Wind Speed: {windSpeed:F2} m/s");
            builder.AppendLine($"------");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.AppendLine($"  Air Exchange: {-GetAmbientExchange(1):+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"  Turbo Cooling: {-o2Exchange:+0.00;-0.00;0.00} °C/s");
            builder.Append(neighborStringBuilder);
        }

        private static void RegisterTerminalControls()
        {
            if (_controlsRegistered) return;
            _controlsRegistered = true;

            HeatSession.Api.Utils.TryRegister<IMyAirVent>();

            var slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyAirVent>("O2Turbo");
            slider.Title = MyStringId.GetOrCompute("Turbo mode");
            slider.Tooltip = MyStringId.GetOrCompute("Select O2 usage for cooling");
            slider.SetLimits(b => 0f, b => GetO2TurboMax(b));
            slider.SupportsMultipleBlocks = false;
            slider.Enabled = b => !(b as IMyAirVent).CanPressurize;
            slider.Writer = (b, sb) => sb.Append($"{GetO2Turbo(b):F2} L/s");
            slider.Getter = b => GetO2Turbo(b);
            slider.Setter = (b, value) => SetO2Turbo(b, value);
            MyAPIGateway.TerminalControls.AddControl<IMyAirVent>(slider);

            var turboIncrease = MyAPIGateway.TerminalControls.CreateAction<IMyAirVent>("O2TurboIncrease");
            turboIncrease.Name = new StringBuilder("Increase O2 Usage For Cooling");
            turboIncrease.Enabled = b => !(b as IMyAirVent).CanPressurize;
            turboIncrease.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            turboIncrease.Action = b =>
            {
                var limit = GetO2TurboMax(b);
                float step = limit / 10f;
                float newValue = GetO2Turbo(b) + step;
                if (newValue > limit) newValue = limit;
                SetO2Turbo(b, newValue);
            };
            turboIncrease.Writer = (b, sb) => sb.Append($"{GetO2Turbo(b):F2}L/s");
            MyAPIGateway.TerminalControls.AddAction<IMyAirVent>(turboIncrease);

            var turboDecrease = MyAPIGateway.TerminalControls.CreateAction<IMyAirVent>("O2TurboDecrease");
            turboDecrease.Name = new StringBuilder("Decrease O2 Usage For Cooling");
            turboDecrease.Enabled = b => !(b as IMyAirVent).CanPressurize;
            turboDecrease.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            turboDecrease.Action = b =>
            {
                float step = GetO2TurboMax(b) / 10f;
                float newValue = GetO2Turbo(b) - step;
                if (newValue < 0f) newValue = 0f;
                SetO2Turbo(b, newValue);
            };
            turboDecrease.Writer = (b, sb) => sb.Append($"{GetO2Turbo(b):F2}L/s");
            MyAPIGateway.TerminalControls.AddAction<IMyAirVent>(turboDecrease);
        }
    }
}
