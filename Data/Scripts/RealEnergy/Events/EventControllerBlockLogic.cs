using Sandbox.Common.ObjectBuilders;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace TSUT.HeatManagement
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_EventControllerBlock), false)]
    public class EventControllerBlockLogic : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            var block = Entity as IMyCubeBlock;
            if (block == null) return;

            BlockTemperatureChanged blockComp = block.Components.Get<BlockTemperatureChanged>();
            if (blockComp == null)
            {
                blockComp = new BlockTemperatureChanged();
                block.Components.Add(blockComp);
            }
            blockComp.LoadThreshold();

            GridMaxTemperatureChanged gridComp = block.Components.Get<GridMaxTemperatureChanged>();
            if (gridComp == null)
            {
                gridComp = new GridMaxTemperatureChanged();
                block.Components.Add(gridComp);
            }
            gridComp.LoadThreshold();
        }
    }
}
