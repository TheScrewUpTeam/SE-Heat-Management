using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public class PistonHeatManagerFactory : IHeatBehaviorFactory
    {
        public int Priority => 15;

        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            var pistonBases = grid.GetFatBlocks<IMyPistonBase>();
            foreach (var @base in pistonBases)
            {
                if (behaviorMap.ContainsKey(@base))
                    continue;
                var behavior = new PistonBaseHeatManager(@base);
                behaviorMap[@base] = behavior;
            }
            var pistonTops = grid.GetFatBlocks<IMyPistonTop>();
            foreach (var top in pistonTops)
            {
                if (behaviorMap.ContainsKey(top))
                    continue;
                var behavior = new PistonHeadHeatManager(top);
                behaviorMap[top] = behavior;
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

            if (block is IMyPistonBase)
            {
                var @base = block as IMyPistonBase;
                var behavior = new PistonBaseHeatManager(@base);
                result.Behavior = behavior;
                result.AffectedBlocks = new List<IMyCubeBlock> { block };
            }
            else if (block is IMyPistonTop)
            {
                var top = block as IMyPistonTop;
                var behavior = new PistonHeadHeatManager(top);
                result.Behavior = behavior;
                result.AffectedBlocks = new List<IMyCubeBlock> { block };
            }
            return result;
        }
    }

    public class PistonBaseHeatManager : AMovingPartHeatManager<IMyPistonBase>
    {
        public PistonBaseHeatManager(IMyPistonBase part) : base(part)
        {
        }

        public override IMyCubeBlock Block => _part;

        protected override float HeatConductivity => Config.Instance.HEATPIPE_CONDUCTIVITY;

        protected override IHeatBehavior GetCounterpartyHeatManager()
        {
            var top = _part.Top as IMyPistonTop;
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

    public class PistonHeadHeatManager : AMovingPartHeatManager<IMyPistonTop>
    {
        public PistonHeadHeatManager(IMyPistonTop part) : base(part, true)
        {
        }

        public override IMyCubeBlock Block => _part;

        protected override float HeatConductivity => Config.Instance.HEATPIPE_CONDUCTIVITY;

        protected override IHeatBehavior GetCounterpartyHeatManager()
        {
            var basePart = _part.Base as IMyPistonBase;
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