using Sandbox.ModAPI;

namespace TSUT.HeatManagement
{
    public interface IManagedStorage: IManagedBlock
    {
        float GetCurrentO2Storage();
        IMyTerminalBlock Block { get; }
        void ConsumeO2(float amount);
    }

    public class ManagedStorage : IManagedStorage
    {
        protected readonly IMyTerminalBlock _block;

        public ManagedStorage(IMyTerminalBlock block)
        {
            _block = block;
        }

        public bool IsWorking 
        {
            get
            {
                var tank = _block as IMyGasTank;
                return _block.IsWorking && tank != null && !tank.Stockpile;
            }
        }

        public IMyTerminalBlock Block => _block;

        public float GetCurrentO2Storage()
        {
            var tank = _block as IMyGasTank;
            if (tank != null)
            {
                var filledRatio = tank.FilledRatio;
                var capacity = tank.Capacity;
                var currentAmount = filledRatio * capacity;
                return (float)currentAmount;
            }
            return 0f;
        }

        public void ConsumeO2(float amount)
        {
            var tank = _block as IMyGasTank;
            if (tank != null)
            {
                var filledRatio = tank.FilledRatio;
                var capacity = tank.Capacity;
                var currentAmount = filledRatio * capacity;

                var newAmount = currentAmount - amount;
                if (newAmount < 0)
                    newAmount = 0;

                var newFilledRatio = newAmount / capacity;
                tank.ChangeFilledRatio(newFilledRatio, true);
            }
        }

        public void Enable()
        {
            (_block as IMyFunctionalBlock).Enabled = true;
        }

        public void Disable()
        {
            (_block as IMyFunctionalBlock).Enabled = false;
        }

        public void Dismiss()
        {
            // Nothing to clean up
        }
    }
}