using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public class RotorHeatManagerFactory : IHeatBehaviorFactory
    {
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
            // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] OnBlockAdded called for block {block.DisplayNameText} on {block.CubeGrid.DisplayName}");
            var result = new HeatBehaviorAttachResult();
            var existing = manager.TryGetHeatBehaviour(block);
            if (existing != null)
                return result;

            // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] => Current grid: no existing behavior");

            GridHeatManager oppositeGrid = null;
            if (block is IMyMotorStator && (block as IMyMotorStator).Top != null)
            {
                // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] => It's stator");
                var grid = (block as IMyMotorStator).Top.CubeGrid;
                // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] => Grid defined {grid.DisplayName}");
                HeatSession.GetGridHeatManager(grid, out oppositeGrid);
                // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] => Behavior found");
            }
            else if (block is IMyMotorRotor && (block as IMyMotorRotor).Base != null)
            {
                // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] => It's rotor");
                var grid = (block as IMyMotorRotor).Base.CubeGrid;
                // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] => Grid defined {grid.DisplayName}");
                HeatSession.GetGridHeatManager(grid, out oppositeGrid);
                // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] => Behavior found");
            }
            if (oppositeGrid != null) 
                // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] => Opposite grid found");
            if (oppositeGrid != null && oppositeGrid.TryGetHeatBehaviour(block) != null)
            {
                return result;
            }

            // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Motors] => No existing behavior");

            if (block is IMyMotorStator)
            {
                var stator = block as IMyMotorStator;
                var behavior = new MotorStatorHeatManager(stator);
                result.Behavior = behavior;
                result.AffectedBlocks = new List<IMyCubeBlock> { block };
                // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Stator] => Block added");
            }
            else if (block is IMyMotorRotor)
            {
                var rotor = block as IMyMotorRotor;
                var behavior = new MotorRotorHeatManager(rotor);
                result.Behavior = behavior;
                result.AffectedBlocks = new List<IMyCubeBlock> { block };
                // MyLog.Default.WriteLineAndConsole($"[HeatManagement,Rotor] => Block added");
            }
            return result;
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
                GridHeatManager gridManager;
                if (HeatSession.GetGridHeatManager(top.CubeGrid, out gridManager))
                {
                    return gridManager.TryGetHeatBehaviour(_part);
                }
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
                GridHeatManager gridManager;
                if (HeatSession.GetGridHeatManager(basePart.CubeGrid, out gridManager))
                {
                    return gridManager.TryGetHeatBehaviour(_part);
                }
            }
            return null;
        }
    }
}