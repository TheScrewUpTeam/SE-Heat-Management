using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public interface IManagedCustom : IManagedBlock
    {
        IMyCubeBlock Block { get; }
    }
    
    public class ManagedCustom : IManagedCustom
    {
        protected readonly IMyCubeBlock _block;

        public ManagedCustom(IMyCubeBlock block)
        {
            _block = block;
        }

        public bool IsWorking => _block.IsWorking;

        public IMyCubeBlock Block => _block;

        public void Disable()
        {
            if (_block is IMyFunctionalBlock)
                (_block as IMyFunctionalBlock).Enabled = false;
        }

        public void Dismiss()
        {
            // Nothing to clean up
        }

        public void Enable()
        {
            if (_block is IMyFunctionalBlock)
                (_block as IMyFunctionalBlock).Enabled = true;
        }
    }
}