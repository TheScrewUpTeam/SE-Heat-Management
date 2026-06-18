using System.Text;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;

namespace TSUT.HeatManagement
{
    public abstract class AHeatGameLogicComponent : MyGameLogicComponent, IHeatBehavior
    {
        protected GridHeatComponent _gridHeatComponent;
        private bool _initialized = false;

        public IMyCubeBlock Block => (IMyCubeBlock)Entity;

        // Bridge to AHeatBehavior utilities without modifying that class
        private sealed class BehaviorBridge : AHeatBehavior
        {
            private readonly AHeatGameLogicComponent _owner;
            internal BehaviorBridge(AHeatGameLogicComponent owner) { _owner = owner; }
            public override IMyCubeBlock Block => _owner.Block;
            public override float GetHeatChange(float dt) => _owner.GetHeatChange(dt);
            public override void SpreadHeat(float dt) => _owner.SpreadHeat(dt);
            public override void Cleanup() => _owner.Cleanup();
            public override void ReactOnNewHeat(float heat) => _owner.ReactOnNewHeat(heat);
        }
        private readonly BehaviorBridge _bridge;

        protected AHeatGameLogicComponent() { _bridge = new BehaviorBridge(this); }

        protected void SpreadHeatStandard(IMyCubeBlock block, float deltaTime)
            => _bridge.SpreadHeatStandard(block, deltaTime);

        protected void AddNeighborAndNetworksInfo(IMyCubeBlock block, StringBuilder info,
            out float cumulativeNeighbor, out float cumulativeNetwork)
            => _bridge.AddNeighborAndNetworksInfo(block, info, out cumulativeNeighbor, out cumulativeNetwork);

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            _gridHeatComponent = ((IMyCubeBlock)Entity).CubeGrid.GameLogic.GetAs<GridHeatComponent>();
            _gridHeatComponent?.RegisterBehavior((IMyCubeBlock)Entity, this);
            _initialized = true;
            OnAttachedToScene();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!_initialized) return;
            var newGrid = ((IMyCubeBlock)Entity).CubeGrid.GameLogic.GetAs<GridHeatComponent>();
            if (newGrid == _gridHeatComponent) return;
            _gridHeatComponent?.UnregisterBehavior((IMyCubeBlock)Entity);
            _gridHeatComponent = newGrid;
            _gridHeatComponent?.RegisterBehavior((IMyCubeBlock)Entity, this);
            OnAttachedToScene();
        }

        protected virtual void OnAttachedToScene() { }

        public bool IsInitialized => _initialized;

        public void ReattachToGrid(GridHeatComponent grid)
        {
            _gridHeatComponent = grid;
            OnAttachedToScene();
        }

        public override void Close()
        {
            _gridHeatComponent?.UnregisterBehavior((IMyCubeBlock)Entity);
            base.Close();
        }

        public abstract float GetHeatChange(float deltaTime);
        public abstract void SpreadHeat(float deltaTime);
        public abstract void Cleanup();
        public abstract void ReactOnNewHeat(float heat);
    }
}
