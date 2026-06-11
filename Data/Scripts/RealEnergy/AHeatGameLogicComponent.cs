using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public abstract class AHeatGameLogicComponent : MyGameLogicComponent, IHeatBehavior
    {
        protected GridHeatComponent _gridHeatComponent;

        public IMyCubeBlock Block => (IMyCubeBlock)Entity;

        public override void UpdateOnceBeforeFrame()
        {
            _gridHeatComponent = ((IMyCubeBlock)Entity).CubeGrid.Components.Get<GridHeatComponent>();
            _gridHeatComponent?.RegisterBehavior((IMyCubeBlock)Entity, this);
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
