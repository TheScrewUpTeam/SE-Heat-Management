using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public interface IO2Consumer
    {
        float GetO2Consumption(float deltaTime);
        IMyCubeBlock Block { get; }
        bool IsWorking { get; }
    }
}
