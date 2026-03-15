using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public interface IManagedProducer: IManagedBlock
    {
        float GetCurrentO2Production(float deltaTime);
        IMyTerminalBlock Block { get; }
    }
    
    public class ManagedProducer : IManagedProducer
    {
        protected readonly IMyTerminalBlock _block;
        
        public ManagedProducer(IMyTerminalBlock block)
        {
            _block = block;
        }

        public bool IsWorking => _block.IsWorking;

        public IMyTerminalBlock Block => _block;

        public void Disable()
        {
            (_block as IMyFunctionalBlock).Enabled = false;
        }

        public void Dismiss()
        {
            // Nothing to clean up
        }

        public void Enable()
        {
            (_block as IMyFunctionalBlock).Enabled = true;
        }

        public float GetCurrentO2Production(float deltaTime)
        {
            if (Block.IsWorking == false)
                return 0f;
            if (Block is IMyAirVent)
            {
                var vent = Block as IMyAirVent;
                if (!vent.Depressurize)
                    return 0f;
            }
            if (Block is IMyOxygenFarm)
            {
                var farm = Block as IMyOxygenFarm;
                if (!farm.CanProduce)
                    return 0f;
            }
            var sourceComp = _block.Components.Get<MyResourceSourceComponent>();
            var resourceId = MyResourceDistributorComponent.OxygenId;
            var maxOutput = sourceComp.MaxOutputByType(resourceId);
            var currentOutput = sourceComp.CurrentOutputByType(resourceId);
            var availableOutput = maxOutput - currentOutput;
            // MyAPIGateway.Utilities.ShowMessage("O2Link", $"O2 Production ({Block.DisplayNameText}):{currentOutput:F2}/{maxOutput:F2} = {availableOutput:F2} L");
            return availableOutput * deltaTime;
        }
    }
}