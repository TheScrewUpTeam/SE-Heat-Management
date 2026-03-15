namespace TSUT.HeatManagement
{
    public interface IManagedBlock
    {
        void Enable();
        void Disable();
        bool IsWorking { get; }
        void Dismiss();
    }
}