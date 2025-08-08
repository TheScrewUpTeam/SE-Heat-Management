using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game.Entities;

namespace TSUT.HeatManagement
{
    public class ShareableApi
    {
        public string ApiVersion = "1.0.0";
        public static long HeatApiMessageId = 35136709491; // Unique message ID for heat API
        public static long HeatProviderMesageId = 35136709492; // Unique message ID for heat provider

        public HeatUtils Utils;
        public HeatEffects Effects;

        public class HeatBehaviorProvider
        {
            public Func<IMyCubeGrid, Dictionary<IMyCubeBlock, HeatBehaviorLogic>> ProvideBehaviors;
        }

        // === External-safe data type ===
        public class HeatBehaviorLogic
        {
            public Func<float, float> GetHeatChange;
            public Action<float> ReactOnNewHeat;
            public Action<float> SpreadHeat;
            public Action Cleanup;
        }

        public class HeatUtils
        {
            public Func<IMyCubeBlock, float> CalculateAmbientTemperature;
            public Func<float, float> EstimateSpecificHeat;
            public Func<IMyThrust, float, float, float> GetActiveThrusterHeatLoss;
            public Func<IMyAirVent, float, float> GetActiveVentHeatLoss;
            public Func<IMyHeatVent, float, float> GetActiveHeatVentLoss;
            public Func<IMyCubeBlock, float, float> GetAmbientHeatLoss;
            public Func<IMyCubeBlock, float> GetDensity;
            public Func<IMyCubeBlock, float> GetHeat;
            public Func<IMySlimBlock, float> GetLargestFaceArea;
            public Func<IMyCubeBlock, float> GetMass;
            public Func<IMyCubeBlock, float> GetRealSurfaceArea;
            public Func<IMyCubeBlock, MyPlanet, Vector3> GetSunDirection;
            public Func<Vector3D, float> GetTemperatureOnPlanet;
            public Func<IMyCubeBlock, float> GetThermalCapacity;
            public Func<IMyCubeBlock, bool> IsBlockInPressurizedRoom;
            public Action PurgeCaches;
            public Action<IMyCubeBlock, float, bool> SetHeat;
            public Func<IMyCubeBlock, float, float> ApplyHeatChange;
            public Func<IMyCubeBlock, float> GetBlockWindSpeed;
            public Func<IMyCubeBlock, IMyCubeBlock, float, float> GetExchangeWithNeighbor;
            public Func<IMyCubeBlock, float> GetAirDensity;
            public Func<IMyExhaustBlock, float, float> GetActiveExhaustHeatLoss;
        }

        public class HeatEffects
        {
            public Action<List<IMyCubeBlock>> Cleanup;
            public Action<IMyCubeBlock> InstantiateSmoke;
            public Action<IMyCubeBlock> RemoveSmoke;
            public Action<IMyCubeBlock, float> UpdateBlockHeatLight;
            public Action UpdateLightsPosition;
        }
    }
}
