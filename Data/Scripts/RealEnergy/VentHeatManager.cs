using System.Collections.Generic;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public class VentHeatManagerFactory : IHeatBehaviorFactory
    {
        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMyAirVent> vents = new List<IMyAirVent>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(vents);

            foreach (var vent in vents)
            {
                if (!behaviorMap.ContainsKey(vent))
                {
                    behaviorMap[vent] = new VentHeatManager(vent, manager);
                }
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            result.AffectedBlocks = new List<IMyCubeBlock> { block };

            if (block is IMyAirVent)
            {
                result.Behavior = new VentHeatManager(block as IMyAirVent, manager);
                return result;
            }
            return result; // No behavior created for non-vent blocks
        }

        public int Priority => 20; // Vents are less critical than batteries
    }

    public class VentHeatManager : AHeatBehavior
    {
        private IGridHeatManager _gridManager;
        private IMyAirVent _vent;

        public VentHeatManager(IMyAirVent vent, IGridHeatManager manager)
        {
            _vent = vent;
            _gridManager = manager;
            _vent.AppendingCustomInfo += AppendVentHeatInfo;
            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;
        }

        private static void SetO2Turbo(IMyCubeBlock block, float o2turbo)
        {
            block.Storage[Config.O2TurboKey] = o2turbo.ToString();
        }

        private static float GetO2Turbo(IMyCubeBlock block)
        {
            if ((block as IMyAirVent).CanPressurize)
            {
                return 0f;
            }

            string turboStr;
            if (block.Storage.TryGetValue(Config.O2TurboKey, out turboStr))
            {
                float heat;
                if (float.TryParse(turboStr, out heat) && !float.IsNaN(heat) && !float.IsInfinity(heat))
                {
                    return heat;
                }
            }

            return 0f;
        }

        private void OnCustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block != _vent)
                return;

            var slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyAirVent>("ShowHeatNetworks");
            slider.Title = MyStringId.GetOrCompute("Turbo mode");
            slider.Tooltip = MyStringId.GetOrCompute("Select O2 usage for cooling");
            slider.SetLimits(0, _vent.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 500 : 50);
            slider.SupportsMultipleBlocks = false;
            slider.Enabled = b => !(b as IMyAirVent).CanPressurize;
            slider.Writer = (b, sb) =>
            {
                if (b == _vent)
                {
                    sb.Append($"{GetO2Turbo(b):F2} L/s");
                }
            };

            slider.Setter = (b, value) =>
            {
                SetO2Turbo(b, value);
            };

            slider.Getter = (b) =>
            {
                return GetO2Turbo(b);
            };

            controls.Add(slider);
        }

        private void AppendVentHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float ownThermalCapacity = HeatSession.Api.Utils.GetThermalCapacity(block);

            var neighborStringBuilder = new StringBuilder();
            float cumulativeNeighborHeatChange;
            float cumulativeNetworkHeatChange;
            AddNeighborAndNetworksInfo(_vent, neighborStringBuilder, out cumulativeNeighborHeatChange, out cumulativeNetworkHeatChange);

            var o2Exchange = 0f;

            var turboO2Usage = GetO2Turbo(_vent);
            var o2Needed = turboO2Usage;
            var o2avilable = GetO2Available();
                
            if (_vent.IsWorking && o2Needed <= o2avilable)
            {
                o2Exchange = o2Needed * Config.Instance.VENT_TURBO_COOLING_RATE / ownThermalCapacity;
            }

            float heatChange = -GetAmbientExchange(1f) - cumulativeNeighborHeatChange - cumulativeNetworkHeatChange - o2Exchange; // Assuming deltaTime of 1 second for display purposes

            builder.AppendLine($"--- Heat Management ---");
            builder.AppendLine($"Temperature: {HeatSession.Api.Utils.GetHeat(block):F2} °C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            string exchangeMode = _vent.IsWorking
            ? (turboO2Usage > 0 && o2Needed <= o2avilable ? "Turbo" : "Active")
            : "Passive";
            builder.AppendLine($"Exchange Mode: {exchangeMode}");
            builder.AppendLine($"Thermal Capacity: {ownThermalCapacity / 1000000:F1} MJ/°C");
            builder.AppendLine($"Ambient temp: {HeatSession.Api.Utils.CalculateAmbientTemperature(block):F1} °C");
            builder.AppendLine($"Air density: {HeatSession.Api.Utils.GetAirDensity(_vent) * 100:F1} %");
            float windSpeed = HeatSession.Api.Utils.GetBlockWindSpeed(block);
            builder.AppendLine($"Wind Speed: {windSpeed:F2} m/s");
            builder.AppendLine($"------");
            builder.AppendLine("");
            builder.AppendLine("Heat Sources:");
            builder.AppendLine($"  Air Exchange: {-GetAmbientExchange(1):+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"  Turbo Cooling: {-o2Exchange:+0.00;-0.00;0.00} °C/s");
            builder.Append(neighborStringBuilder);
        }

        public float GetAmbientExchange(float deltaTime)
        {
            if (_vent == null)
                return 0f;

            float change = HeatSession.Api.Utils.GetAmbientHeatLoss(_vent, deltaTime);
            if (_vent.IsWorking)
            {
                change += HeatSession.Api.Utils.GetActiveVentHealLoss(_vent, deltaTime);
            }

            return change;
        }

        public override float GetHeatChange(float deltaTime)
        {
            if (_vent == null)
                return 0f;

            float change = GetAmbientExchange(deltaTime);
            var turboO2Usage = GetO2Turbo(_vent);
            if (_vent.IsWorking && turboO2Usage > 0)
            {
                var o2Needed = turboO2Usage;
                var o2avilable = GetO2Available();
                if (o2Needed <= o2avilable)
                {
                    change += o2Needed * Config.Instance.VENT_TURBO_COOLING_RATE / HeatSession.Api.Utils.GetThermalCapacity(_vent);
                    ConsumeO2(o2Needed);
                    HeatSession.Api.Effects.InstantiateSteam(_vent);
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

        public override void Cleanup()
        {
            if (_vent != null)
            {
                _vent.AppendingCustomInfo -= AppendVentHeatInfo;
                HeatSession.Api.Effects.RemoveSmoke(_vent);
                _vent = null;
            }
            MyAPIGateway.TerminalControls.CustomControlGetter -= OnCustomControlGetter;
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(_vent, deltaTime);
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(_vent, heat);
            _vent.RefreshCustomInfo();
            return; // Vents do not react to new heat in this implementation
        }

        private List<IMyGasTank> FindConnectedO2TanksThroughConveyor()
        {
            var result = new List<IMyGasTank>();
            var candidates = new List<IMyGasTank>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_vent.CubeGrid).GetBlocksOfType(candidates);
            foreach (var candidate in candidates)
            {
                if (candidate.BlockDefinition.SubtypeName == "" || candidate.BlockDefinition.SubtypeName.Contains("Oxygen"))
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        double GetO2Available()
        {
            double total = 0;
            var tanks = FindConnectedO2TanksThroughConveyor();
            foreach (IMyGasTank tank in tanks)
            {
                total += tank.Capacity * tank.FilledRatio;
            }
            return total;
        }
        
        bool ConsumeO2(float shouldBeConsumed)
        {
            var tanks = FindConnectedO2TanksThroughConveyor();
            foreach (IMyGasTank tank in tanks)
            {
                if (tank.FilledRatio == 0)
                {
                    continue;
                }
                double currentVolume = tank.Capacity * tank.FilledRatio;

                if (currentVolume < shouldBeConsumed)
                {
                    tank.ChangeFilledRatio(0, true);
                    shouldBeConsumed -= (float)currentVolume;
                }
                else
                {
                    var newVolume = currentVolume - shouldBeConsumed;
                    tank.ChangeFilledRatio(newVolume / tank.Capacity, true);
                    shouldBeConsumed = 0;
                    return true;
                }
                tank.SetDetailedInfoDirty();
            }
            return false;
        }
    }
}