using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public class RotorHeatManagerFactory : IHeatBehaviorFactory
    {
        private static bool _propertyAdded = false;

        public int Priority => 15;

        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            var stators = grid.GetFatBlocks<IMyMotorStator>();
            foreach (var stator in stators)
            {
                if (behaviorMap.ContainsKey(stator))
                    continue;
                var behavior = new MotorStatorHeatManager(stator);
                behaviorMap[stator] = behavior;
            }
            var rotors = grid.GetFatBlocks<IMyMotorRotor>();
            foreach (var rotor in rotors)
            {
                if (behaviorMap.ContainsKey(rotor))
                    continue;
                var behavior = new MotorRotorHeatManager(rotor);
                behaviorMap[rotor] = behavior;
            }
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager)
        {
            var result = new HeatBehaviorAttachResult();
            var existing = HeatSession.GetBehaviorForBlock(block);
            if (existing != null)
                return result;

            GridHeatManager oppositeGrid = null;
            if (block is IMyMotorStator && (block as IMyMotorStator).Top != null)
            {
                var grid = (block as IMyMotorStator).Top.CubeGrid;
                HeatSession.GetGridHeatManager(grid, out oppositeGrid);
            }
            else if (block is IMyMotorRotor && (block as IMyMotorRotor).Base != null)
            {
                var grid = (block as IMyMotorRotor).Base.CubeGrid;
                HeatSession.GetGridHeatManager(grid, out oppositeGrid);
            }
            if (oppositeGrid != null && oppositeGrid.TryGetHeatBehaviour(block) != null)
            {
                return result;
            }

            if (block is IMyMotorStator)
            {
                var stator = block as IMyMotorStator;
                var behavior = new MotorStatorHeatManager(stator);
                result.Behavior = behavior;
                result.AffectedBlocks = new List<IMyCubeBlock> { block };
            }
            else if (block is IMyMotorRotor)
            {
                var rotor = block as IMyMotorRotor;
                var behavior = new MotorRotorHeatManager(rotor);
                result.Behavior = behavior;
                result.AffectedBlocks = new List<IMyCubeBlock> { block };
            }
            return result;
        }

        public void RegisterCustomControls()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter += (block, controls) =>
            {
                // if (!(block is IMyMotorStator))
                //     return;
                // Only add if it doesn't already exist
                if (controls.Any(c => c.Id == "HeatTemperature") || _propertyAdded)
                    return;
                
                _propertyAdded = true;

                HeatSession.Api.Utils.TryRegister<IMyMotorStator>();
            };
        }
    }

    public class MotorStatorHeatManager : AMovingPartHeatManager<IMyMotorStator>
    {
        public MotorStatorHeatManager(IMyMotorStator part) : base(part)
        {
        }

        public override IMyCubeBlock Block => _part;

        protected override float HeatConductivity => _part is IMyMotorAdvancedStator ? Config.Instance.HEATPIPE_CONDUCTIVITY : Config.Instance.THERMAL_CONDUCTIVITY;

        protected override IHeatBehavior GetCounterpartyHeatManager()
        {
            var top = _part.Top as IMyMotorRotor;
            if (top != null)
            {
                return HeatSession.GetBehaviorForBlock(top);
            }
            return null;
        }
    }

    public class MotorRotorHeatManager : AMovingPartHeatManager<IMyMotorRotor>
    {
        public MotorRotorHeatManager(IMyMotorRotor part) : base(part, true)
        {
        }

        public override IMyCubeBlock Block => _part;

        protected override float HeatConductivity => _part is IMyMotorAdvancedRotor ? Config.Instance.HEATPIPE_CONDUCTIVITY : Config.Instance.THERMAL_CONDUCTIVITY;

        protected override IHeatBehavior GetCounterpartyHeatManager()
        {
            var basePart = _part.Base as IMyMotorStator;
            if (basePart != null)
            {
                return HeatSession.GetBehaviorForBlock(basePart);
            }
            return null;
        }
    }
}