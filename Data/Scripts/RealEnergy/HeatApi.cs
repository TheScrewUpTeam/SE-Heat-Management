using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace TSUT.HeatManagement
{
    public interface IGridHeatManager
    {
        void Cleanup();
        IHeatBehavior TryGetHeatBehaviour(IMyCubeBlock block);
        void UpdateBlocksTemp(float deltaTime);
        void UpdateNeighborsTemp(float deltaTime);
        void UpdateVisuals(float deltaTime);
        List<HeatPipeManager> GetHeatPipeManagers();
        void SetShowDebug(bool flag);
        bool GetShowDebug();
        bool TryReactOnHeat(IMyCubeBlock block, float heat);
    }

    public interface IHeatBehavior
    {
        float GetHeatChange(float deltaTime);
        void SpreadHeat(float deltaTime);
        void Cleanup();
        void ReactOnNewHeat(float heat);
    }

    public interface IMultiBlockHeatBehavior : IHeatBehavior
    {
        void RemoveBlock(IMyCubeBlock block, IGridHeatManager gridManager, Dictionary<IMyCubeBlock, IHeatBehavior> behaviorMap);
        void ShowDebugGraph(float deltaTime);
        void MarkDirty();
    }

    public interface IHeatBehaviorFactory
    {
        void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager manager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap);
        HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager manager);
        int Priority { get; }
    }

    public interface IHeatRegistry
    {
        void RegisterHeatBehaviorFactory(IHeatBehaviorFactory factory);
        IReadOnlyList<IHeatBehaviorFactory> GetFactories();
        void RegisterEventControllerEvent(IEventControllerEvent eventControllerEvent);
        IReadOnlyList<IEventControllerEvent> GetEventControllerEvents();
        void RemoveEventControllerEvent(IEventControllerEvent eventControllerEvent);
        void RegisterHeatBehaviorProvider(Func<long, IDictionary<long, IDictionary<string, object>>> provider);
        IEnumerable<Func<long, IDictionary<long, IDictionary<string, object>>>> GetHeatBehaviorProviders();
        void RegisterHeatMapper(Func<long, IDictionary<string, object>> mapper);
        IEnumerable<Func<long, IDictionary<string, object>>> GetHeatMappers();
    }

    public interface IHeatUtils
    {
        float CalculateAmbientTemperature(IMyCubeBlock block);
        float EstimateSpecificHeat(float density);
        float GetActiveThrusterHeatLoss(IMyThrust thruster, float thrustRatio, float deltaTime);
        float GetActiveVentHealLoss(IMyAirVent vent, float deltaTime);
        float GetActiveHeatVentLoss(IMyHeatVent vent, float deltaTime);
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
        void SetHeat(IMyCubeBlock block, float heat, bool silent = false);
        float ApplyHeatChange(IMyCubeBlock block, float heatChange, bool silent = false);
        float GetBlockWindSpeed(IMyCubeBlock block);
        float GetExchangeWithNeighbor(IMyCubeBlock block, IMyCubeBlock neighbor, float deltaTime);
        float GetAirDensity(IMyCubeBlock block);
        float GetActiveExhaustHeatLoss(IMyExhaustBlock exhaust, float deltaTime);
        float GetExchangeWithNetwork(IMyCubeBlock block, IMyCubeBlock networkBlock, float deltaTime);
        float GetExchangeUniversal(IMyCubeBlock block, IMyCubeBlock neighborBlock, float deltaTime);
        float GetHeatToDissipate(IMyCubeBlock block, float deltaTime);
        float ApplyExchangeLimit(float energyDelta, float capA, float capB, float tempDiff);
    }

    public interface IHeatEffects
    {
        void Cleanup(List<IMyCubeBlock> blocks);
        void InstantiateSmoke(IMyCubeBlock battery);
        void RemoveSmoke(IMyCubeBlock battery);
        void UpdateBlockHeatLight(IMyCubeBlock block, float heat);
        void UpdateLightsPosition();
        void InstantiateSteam(IMyCubeBlock battery);
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

    public interface IConfig
    {
        float HEAT_COOLDOWN_COEFF { get; set; }
        float HEAT_RADIATION_COEFF { get; set; }
        float DISCHARGE_HEAT_FRACTION { get; set; }
        bool DISCHARGE_HEAT_CONFIGURABLE { get; set; }
        float THERMAL_CONDUCTIVITY { get; set; }
        float VENT_COOLING_RATE { get; set; }
        float THRUSTER_COOLING_RATE { get; set; }
        float CRITICAL_TEMP { get; set; }
        float SMOKE_TRESHOLD { get; }
        float WIND_COOLING_MULT { get; set; }
        bool LIMIT_TO_PLAYER_GRIDS { get; set; }
        float HEATPIPE_CONDUCTIVITY { get; set; }
        float EXHAUST_HEAT_REJECTION_RATE { get; set; }
        bool HEAT_GLOW_INDICATION { get; set; }
    }
}