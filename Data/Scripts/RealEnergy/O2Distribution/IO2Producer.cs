using VRage.Game.ModAPI;

namespace TSUT.HeatManagement
{
    public interface IO2Producer
    {
        float GetO2Production(float deltaTime);
        IMyCubeBlock Block { get; }
        bool IsWorking { get; }
    }
}
