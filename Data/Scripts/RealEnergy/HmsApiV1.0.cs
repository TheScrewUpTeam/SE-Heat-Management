using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TSUT.HeatManagement
{
    public class HmsApi
    {
        public string ApiVersion = "1.0.0";
        public static long HeatApiMessageId = 35136709491; // Unique message ID for heat API
        public static long HeatProviderMesageId = 35136709492; // Unique message ID for heat provider

        private static HmsApi _instance;
        private Action _onReady;
        public IHeatUtils Utils;
        public IHeatEffects Effects;

        public HmsApi(Action onReady)
        {
            if (_instance != null)
            {
                return; // Prevent multiple instances
            }
            _instance = this;
            _onReady = onReady;
            MyAPIGateway.Utilities.RegisterMessageHandler(HeatApiMessageId, OnApiReceived);
        }

        private void OnApiReceived(object obj)
        {
            Utils = new HmsUtils(obj);
            Effects = new HmsEffects(obj);
            _onReady?.Invoke();
        }

        public void RegisterHeatBehaviorFactory(
            Func<MyCubeGrid, List<IMyCubeBlock>> blockSelector,
            Func<IMyCubeBlock, AHeatBehavior> behaviorCreator
        )
        {
            MyAPIGateway.Utilities.SendModMessage(HeatProviderMesageId, new Dictionary<string, object>
            {
                { "factory", new Func<long, IDictionary<long, IDictionary<string, object>>>((gridId) =>
                    {
                        MyCubeGrid grid = MyAPIGateway.Entities.GetEntityById(gridId) as MyCubeGrid;
                        var selectedBlocks = blockSelector.Invoke(grid);
                        Dictionary<IMyCubeBlock, AHeatBehavior> behaviors = new Dictionary<IMyCubeBlock, AHeatBehavior>();
                        foreach (var block in selectedBlocks) {
                            behaviors.Add(block, behaviorCreator(block));
                        }
                        return MapBehaviors(behaviors);
                    })
                },
                { "creator", new Func<long, IDictionary<string, object>>((blockId) =>
                    {
                        MyCubeBlock block = MyAPIGateway.Entities.GetEntityById(blockId) as MyCubeBlock;
                        var behavior = behaviorCreator(block);
                        if (behavior == null) {
                            return null;
                        }
                        return MapBehavior(behavior);
                    })
                }
            });
        }

        public interface IHeatUtils
        {
            /// <summary>
            /// Calculates the ambient temperature for a given block, considering its environment.
            /// </summary>
            float CalculateAmbientTemperature(IMyCubeBlock block);

            /// <summary>
            /// Estimates the specific heat capacity based on the provided density.
            /// </summary>
            float EstimateSpecificHeat(float density);

            /// <summary>
            /// Calculates the heat loss from an active thruster over a given time interval.
            /// </summary>
            float GetActiveThrusterHeatLoss(IMyThrust thruster, float thrustRatio, float deltaTime);

            /// <summary>
            /// Calculates the heat loss from an active air vent over a given time interval.
            /// </summary>
            float GetActiveVentHealLoss(IMyAirVent vent, float deltaTime);

            /// <summary>
            /// Calculates the heat loss from an active heat vent over a given time interval.
            /// </summary>
            float GetActiveHeatVentLoss(IMyHeatVent vent, float deltaTime);

            /// <summary>
            /// Calculates the ambient heat loss for a block over a given time interval.
            /// </summary>
            float GetAmbientHeatLoss(IMyCubeBlock block, float deltaTime);

            /// <summary>
            /// Returns the density of the specified block.
            /// </summary>
            float GetDensity(IMyCubeBlock block);

            /// <summary>
            /// Gets the current heat value of the specified block.
            /// </summary>
            float GetHeat(IMyCubeBlock block);

            /// <summary>
            /// Returns the area of the largest face of the specified slim block.
            /// </summary>
            float GetLargestFaceArea(IMySlimBlock block);

            /// <summary>
            /// Returns the mass of the specified block.
            /// </summary>
            float GetMass(IMyCubeBlock block);

            /// <summary>
            /// Returns the real surface area of the specified block (e.g., battery).
            /// </summary>
            float GetRealSurfaceArea(IMyCubeBlock battery);

            /// <summary>
            /// Gets the sun direction vector for a block on a given planet.
            /// </summary>
            Vector3 GetSunDirection(IMyCubeBlock block, MyPlanet planet);

            /// <summary>
            /// Returns the temperature at a specific position on a planet.
            /// </summary>
            float GetTemperatureOnPlanet(Vector3D position);

            /// <summary>
            /// Returns the thermal capacity of the specified block.
            /// </summary>
            float GetThermalCapacity(IMyCubeBlock block);

            /// <summary>
            /// Determines if the block is in a pressurized room.
            /// </summary>
            bool IsBlockInPressurizedRoom(IMyCubeBlock block);

            /// <summary>
            /// Sets the heat value of a block. Optionally, the operation can be silent.
            /// </summary>
            void SetHeat(IMyCubeBlock block, float heat, bool silent = true);

            /// <summary>
            /// Applies a heat change to a block and returns the new heat value.
            /// </summary>
            float ApplyHeatChange(IMyCubeBlock block, float heatChange);

            /// <summary>
            /// Returns the wind speed affecting the specified block.
            /// </summary>
            float GetBlockWindSpeed(IMyCubeBlock block);

            /// <summary>
            /// Calculates the heat exchange between two neighboring blocks over a given time interval.
            /// </summary>
            float GetExchangeWithNeighbor(IMyCubeBlock block, IMyCubeBlock neighbor, float deltaTime);

            /// <summary>
            /// Returns the air density at the specified block's location.
            /// </summary>
            float GetAirDensity(IMyCubeBlock block);

            /// <summary>
            /// Calculates the heat loss from an active exhaust block over a given time interval.
            /// </summary>
            float GetActiveExhaustHeatLoss(IMyExhaustBlock exhaust, float deltaTime);
        }

        public interface IHeatEffects
        {
            /// <summary>
            /// Instantiates a smoke effect on the specified battery block to indicate overheating or damage.
            /// </summary>
            void InstantiateSmoke(IMyCubeBlock battery);

            /// <summary>
            /// Removes the smoke effect from the specified battery block.
            /// </summary>
            void RemoveSmoke(IMyCubeBlock battery);

            /// <summary>
            /// Updates the heat-related lighting effect on the specified block.
            /// </summary>
            void UpdateBlockHeatLight(IMyCubeBlock block, float heat);

            /// <summary>
            /// Updates the positions of all heat-related lights in the scene.
            /// </summary>
            void UpdateLightsPosition();
        }

        public abstract class AHeatBehavior
        {
            protected IMyCubeBlock Block;

            public AHeatBehavior(IMyCubeBlock block)
            {
                Block = block;
            }

            // public static IDictionary<IMyCubeBlock, AHeatBehavior> CollectHeatBehaviors(MyCubeGrid grid)
            // {
            //     Replace 'AHeatBehaviorDerived' with your actual logic, the rest is here for demonstration.
            //     var behaviors = new Dictionary<IMyCubeBlock, AHeatBehavior>();
            //     foreach (var block in grid.GetFatBlocks<MyCubeBlock>())
            //         {
            //             if (block is IMyCubeBlock)
            //             {
            //                 var cubeBlock = block as IMyCubeBlock;
            //                 behaviors[cubeBlock] = new AHeatBehaviorDerived(cubeBlock);
            //             }
            //         }
            //     return behaviors;
            // }

            /// <summary>
            /// Called every simulation tick to calculate the heat change for this block.
            /// Return the amount of heat to add (positive) or remove (negative) for the given deltaTime.
            /// This is the main entry point for your custom heat logic.
            /// </summary>
            /// <param name="deltaTime">The time in seconds since the last update.</param>
            /// <returns>The heat change to apply to the block.</returns>
            abstract public float GetHeatChange(float deltaTime);

            /// <summary>
            /// Called every simulation tick to allow this block to exchange heat with its neighbors.
            /// Use this to implement heat spreading or networked heat transfer.
            /// </summary>
            /// <param name="deltaTime">The time in seconds since the last update.</param>
            abstract public void SpreadHeat(float deltaTime);

            /// <summary>
            /// Called when the block or its heat behavior is being removed from the grid (e.g., block destroyed or grid unloaded).
            /// Use this to clean up any resources, detach events, or perform finalization logic.
            /// </summary>
            abstract public void Cleanup();

            /// <summary>
            /// Called after the block's heat value has been updated (e.g., after ApplyHeatChange or external heat set).
            /// Use this to react to the new heat value, such as triggering effects, damage, or state changes.
            /// </summary>
            /// <param name="heat">The new heat value of the block.</param>
            abstract public void ReactOnNewHeat(float heat);

            internal void SpreadHeatStandard(float deltaTime, IMyCubeBlock block, HmsApi api)
            {
                var temperatureChange = 0f;
                var heatExchangeWithNeighbors = CalculateNeighborExchangeStandard(deltaTime, block, api, ref temperatureChange);
                foreach (var kvm in heatExchangeWithNeighbors)
                {
                    var neighbor = kvm.Key;
                    var tempChange = kvm.Value;
                    var neighborTemp = api.Utils.GetHeat(neighbor);
                    api.Utils.SetHeat(neighbor, neighborTemp + tempChange);
                }

                var ownTemperature = api.Utils.GetHeat(block);
                api.Utils.SetHeat(block, ownTemperature + temperatureChange);
            }

            public Dictionary<IMyCubeBlock, float> CalculateNeighborExchangeStandard(float deltaTime, IMyCubeBlock block, HmsApi api, ref float ownHeatChange)
            {
                var neighborList = new List<IMySlimBlock>();
                block.SlimBlock.GetNeighbours(neighborList);

                var result = new Dictionary<IMyCubeBlock, float>();
                float energyTransferred = 0f;

                foreach (var neighborSlim in neighborList)
                {
                    var neighborFat = neighborSlim.FatBlock;
                    if (neighborFat == null)
                        continue;
                    var capacity = api.Utils.GetThermalCapacity(neighborFat);
                    var transfer = api.Utils.GetExchangeWithNeighbor(block, neighborFat, deltaTime) * capacity;
                    result.Add(neighborFat, -transfer / capacity);
                    energyTransferred += transfer;
                }

                var ownCapacity = api.Utils.GetThermalCapacity(block);
                ownHeatChange = energyTransferred / ownCapacity;
                return result;
            }
        }

        private IDictionary<long, IDictionary<string, object>> MapBehaviors(Dictionary<IMyCubeBlock, AHeatBehavior> behaviors)
        {
            var result = new Dictionary<long, IDictionary<string, object>>();
            foreach (var kvp in behaviors)
            {
                var block = kvp.Key;
                var behavior = kvp.Value;
                if (block == null || behavior == null)
                    continue;

                var blockId = block.EntityId;
                if (!result.ContainsKey(blockId))
                {
                    result[blockId] = new Dictionary<string, object>();
                }
                result[blockId] = MapBehavior(behavior);
            }
            return result;
        }

        private IDictionary<string, object> MapBehavior(AHeatBehavior behavior)
        {
            return new Dictionary<string, object>
                {
                    { "GetHeatChange", new Func<float, float>((deltaTime) => behavior.GetHeatChange(deltaTime)) },
                    { "ReactOnNewHeat", new Action<float>(heat => behavior.ReactOnNewHeat(heat)) },
                    { "SpreadHeat", new Action<float>(deltaTime => behavior.SpreadHeat(deltaTime)) },
                    { "Cleanup", new Action(() => behavior.Cleanup()) }
                };
        }

        internal void Cleanup()
        {
            MyAPIGateway.Utilities.UnregisterMessageHandler(HeatApiMessageId, OnApiReceived);
            Utils = null;
            Effects = null;
            _instance = null;
        }

        internal class HmsUtils : IHeatUtils
        {
            private IDictionary<string, object> client;

            public HmsUtils(object client)
            {
                this.client = client as IDictionary<string, object>;
            }

            public float ApplyHeatChange(IMyCubeBlock block, float heatChange)
            {
                object method;
                if (client.TryGetValue("ApplyHeatChange", out method) && method is Func<long, float, float>)
                {
                    var fn = (Func<long, float, float>)method;
                    return fn(block?.EntityId ?? 0, heatChange);
                }
                return 0f;
            }


            public float CalculateAmbientTemperature(IMyCubeBlock block)
            {
                object method;
                if (client.TryGetValue("CalculateAmbientTemperature", out method) && method is Func<long, float>)
                {
                    var fn = (Func<long, float>)method;
                    return fn(block?.EntityId ?? 0);
                }
                return 0f;
            }


            public float EstimateSpecificHeat(float density)
            {
                object method;
                if (client.TryGetValue("EstimateSpecificHeat", out method) && method is Func<float, float>)
                {
                    var fn = (Func<float, float>)method;
                    return fn(density);
                }
                return 0f;
            }


            public float GetActiveExhaustHeatLoss(IMyExhaustBlock exhaust, float deltaTime)
            {
                object method;
                if (client.TryGetValue("GetActiveExhaustHeatLoss", out method) && method is Func<long, float, float>)
                {
                    var fn = (Func<long, float, float>)method;
                    return fn(exhaust.EntityId, deltaTime);
                }
                return 0f;
            }


            public float GetActiveHeatVentLoss(IMyHeatVent vent, float deltaTime)
            {
                object method;
                if (client.TryGetValue("GetActiveHeatVentLoss", out method) && method is Func<long, float, float>)
                {
                    var fn = (Func<long, float, float>)method;
                    return fn(vent.EntityId, deltaTime);
                }
                return 0f;
            }


            public float GetActiveThrusterHeatLoss(IMyThrust thruster, float thrustRatio, float deltaTime)
            {
                object method;
                if (client.TryGetValue("GetActiveThrusterHeatLoss", out method) && method is Func<long, float, float, float>)
                {
                    var fn = (Func<long, float, float, float>)method;
                    return fn(thruster?.EntityId ?? 0, thrustRatio, deltaTime);
                }
                return 0f;
            }


            public float GetActiveVentHealLoss(IMyAirVent vent, float deltaTime)
            {
                object method;
                if (client.TryGetValue("GetActiveVentHealLoss", out method) && method is Func<long, float, float>)
                {
                    var fn = (Func<long, float, float>)method;
                    return fn(vent?.EntityId ?? 0, deltaTime);
                }
                return 0f;
            }


            public float GetAirDensity(IMyCubeBlock block)
            {
                object method;
                if (client.TryGetValue("GetAirDensity", out method) && method is Func<long, float>)
                {
                    var fn = (Func<long, float>)method;
                    return fn(block.EntityId);
                }
                return 0f;
            }


            public float GetAmbientHeatLoss(IMyCubeBlock block, float deltaTime)
            {
                object method;
                if (client.TryGetValue("GetAmbientHeatLoss", out method) && method is Func<long, float, float>)
                {
                    var fn = (Func<long, float, float>)method;
                    return fn(block?.EntityId ?? 0, deltaTime);
                }
                return 0f;
            }


            public float GetBlockWindSpeed(IMyCubeBlock block)
            {
                object method;
                if (client.TryGetValue("GetBlockWindSpeed", out method) && method is Func<long, float>)
                {
                    var fn = (Func<long, float>)method;
                    return fn(block.EntityId);
                }
                return 0f;
            }


            public float GetDensity(IMyCubeBlock block)
            {
                object method;
                if (client.TryGetValue("GetDensity", out method) && method is Func<long, float>)
                {
                    var fn = (Func<long, float>)method;
                    return fn(block?.EntityId ?? 0);
                }
                return 0f;
            }


            public float GetExchangeWithNeighbor(IMyCubeBlock block, IMyCubeBlock neighbor, float deltaTime)
            {
                object method;
                if (client.TryGetValue("GetExchangeWithNeighbor", out method) && method is Func<long, long, float, float>)
                {
                    var fn = (Func<long, long, float, float>)method;
                    return fn(block?.EntityId ?? 0, neighbor?.EntityId ?? 0, deltaTime);
                }
                MyLog.Default.Warning($"[H2Real] No method GetExchangeWithNeighbor found {client.Count}");
                return 0f;
            }


            public float GetHeat(IMyCubeBlock block)
            {
                object method;
                if (client.TryGetValue("GetHeat", out method) && method is Func<long, float>)
                {
                    var fn = (Func<long, float>)method;
                    return fn(block?.EntityId ?? 0);
                }
                return 0f;
            }


            public float GetLargestFaceArea(IMySlimBlock block)
            {
                object method;
                if (client.TryGetValue("GetLargestFaceArea", out method) && method is Func<long, float>)
                {
                    var fn = (Func<long, float>)method;
                    return fn((block as IMyCubeBlock)?.EntityId ?? 0);
                }
                return 0f;
            }


            public float GetMass(IMyCubeBlock block)
            {
                object method;
                if (client.TryGetValue("GetMass", out method) && method is Func<long, float>)
                {
                    var fn = (Func<long, float>)method;
                    return fn(block?.EntityId ?? 0);
                }
                return 0f;
            }


            public float GetRealSurfaceArea(IMyCubeBlock battery)
            {
                object method;
                if (client.TryGetValue("GetRealSurfaceArea", out method) && method is Func<long, float>)
                {
                    var fn = (Func<long, float>)method;
                    return fn(battery?.EntityId ?? 0);
                }
                return 0f;
            }


            public Vector3 GetSunDirection(IMyCubeBlock block, MyPlanet planet)
            {
                object method;
                if (client.TryGetValue("GetSunDirection", out method) && method is Func<long, long, Vector3>)
                {
                    var fn = (Func<long, long, Vector3>)method;
                    return fn(block?.EntityId ?? 0, planet?.EntityId ?? 0);
                }
                return Vector3.Zero;
            }


            public float GetTemperatureOnPlanet(Vector3D position)
            {
                object method;
                if (client.TryGetValue("GetTemperatureOnPlanet", out method) && method is Func<Vector3D, float>)
                {
                    var fn = (Func<Vector3D, float>)method;
                    return fn(position);
                }
                return 0f;
            }


            public float GetThermalCapacity(IMyCubeBlock block)
            {
                object method;
                if (client.TryGetValue("GetThermalCapacity", out method) && method is Func<long, float>)
                {
                    var fn = (Func<long, float>)method;
                    return fn(block?.EntityId ?? 0);
                }
                return 0f;
            }


            public bool IsBlockInPressurizedRoom(IMyCubeBlock block)
            {
                object method;
                if (client.TryGetValue("IsBlockInPressurizedRoom", out method) && method is Func<long, bool>)
                {
                    var fn = (Func<long, bool>)method;
                    return fn(block?.EntityId ?? 0);
                }
                return false;
            }


            public void SetHeat(IMyCubeBlock block, float heat, bool silent = true)
            {
                object method;
                if (client.TryGetValue("SetHeat", out method) && method is Action<long, float, bool>)
                {
                    var fn = (Action<long, float, bool>)method;
                    fn(block?.EntityId ?? 0, heat, silent);
                    return;
                }
            }
        }

        internal class HmsEffects : IHeatEffects
        {
            private IDictionary<string, object> client;

            public HmsEffects(object client)
            {
                this.client = client as IDictionary<string, object>;
            }

            public void InstantiateSmoke(IMyCubeBlock battery)
            {
                object method;
                if (client.TryGetValue("InstantiateSmoke", out method) && method is Action<long>)
                {
                    var fn = (Action<long>)method;
                    fn(battery?.EntityId ?? 0);
                    return;
                }
            }

            public void RemoveSmoke(IMyCubeBlock battery)
            {
                object method;
                if (client.TryGetValue("RemoveSmoke", out method) && method is Action<long>)
                {
                    var fn = (Action<long>)method;
                    fn(battery?.EntityId ?? 0);
                    return;
                }
            }

            public void UpdateBlockHeatLight(IMyCubeBlock block, float heat)
            {
                object method;
                if (client.TryGetValue("UpdateBlockHeatLight", out method) && method is Action<long, float>)
                {
                    var fn = (Action<long, float>)method;
                    fn(block?.EntityId ?? 0, heat);
                    return;
                }
            }

            public void UpdateLightsPosition()
            {
                object method;
                if (client.TryGetValue("UpdateLightsPosition", out method) && method is Action)
                {
                    var fn = (Action)method;
                    fn();
                    return;
                }
            }
        }
    }
}