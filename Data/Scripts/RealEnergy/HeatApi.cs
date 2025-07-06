using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace TSUT.HeatManagement
{
    public interface IHeatBehavior
    {
        float GetHeatChange(float deltaTime);
        void SpreadHeat(float deltaTime);
        void Cleanup();
        void ReactOnNewHeat(float heat);
    }

    public interface IHeatBehaviorFactory
    {
        void CollectHeatBehaviors(IMyCubeGrid grid, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap);
        IHeatBehavior OnBlockAdded(IMyCubeBlock block);
        int Priority { get; }
    }

    public interface IHeatRegistry
    {
        void RegisterHeatBehaviorFactory(IHeatBehaviorFactory factory);
        IReadOnlyList<IHeatBehaviorFactory> GetFactories();
    }

    public interface IHeatUtils
    {
        float CalculateAmbientTemperature(IMyCubeBlock block);
        float EstimateSpecificHeat(float density);
        float GetActiveThrusterHeatLoss(IMyThrust thruster, float thrustRatio, float deltaTime);
        float GetActiveVentHealLoss(IMyAirVent vent, float deltaTime);
        float GetAmbientHeatLoss(IMyCubeBlock block, float deltaTime);
        float GetDensity(IMyCubeBlock block);
        float GetHeat(IMyCubeBlock block);
        float GetLargestFaceArea(IMySlimBlock block);
        float GetMass(IMyCubeBlock block);
        float GetRealSurfaceArea(IMyCubeBlock battery);
        Vector3 GetSunDirection(IMyCubeBlock block, MyPlanet planet);
        float GetTemperatureOnPlanet(Vector3D position);
        float GetThermalCapacity(IMyCubeBlock block);
        bool IsBlockInPressurizedRoom(IMyCubeBlock block);
        void PurgeCaches();
        void SetHeat(IMyCubeBlock block, float heat);
        float ApplyHeatChange(IMyCubeBlock block, float heatChange);
        float GetBlockWindSpeed(IMyCubeBlock block);
    }

    public interface IHeatEffects
    {
        void Cleanup(List<IMyCubeBlock> blocks);
        void InstantiateSmoke(IMyCubeBlock battery);
        void RemoveSmoke(IMyCubeBlock battery);
        void UpdateBlockHeatLight(IMyCubeBlock block, float heat);
        void UpdateLightsPosition();
    }

    public interface IHeatApi
    {
        IHeatRegistry Registry { get; }
        IHeatUtils Utils { get; }
        IHeatEffects Effects { get; }
    }

    public class HeatApi : IHeatApi
    {
        public IHeatRegistry Registry { get; private set; }
        public IHeatUtils Utils { get; private set; }
        public IHeatEffects Effects { get; private set; }

        public HeatApi()
        {
            Registry = new HeatBehaviorRegistry();
            Utils = new HeatUtils();
            Effects = new HeatEffects();
        }
    }
}