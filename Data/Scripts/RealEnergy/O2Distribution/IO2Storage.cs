using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public interface IO2Storage
    {
        float GetCurrentO2Storage();
        void ConsumeO2(float amount);
        IMyCubeBlock Block { get; }
        bool IsWorking { get; }
    }
}
