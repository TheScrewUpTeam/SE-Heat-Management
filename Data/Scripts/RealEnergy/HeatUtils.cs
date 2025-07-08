using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace TSUT.HeatManagement
{
    public class HeatUtils: IHeatUtils
    {
        public readonly Guid HeatKey = new Guid("decafbad-0000-4c00-babe-c0ffee000001");

        public readonly Dictionary<string, float> _cacheTermalCapacity = new Dictionary<string, float>();
        public readonly Dictionary<string, float> _cacheSurfaceArea = new Dictionary<string, float>();

        private readonly Dictionary<string, float> WeatherTemperatureModifiers = new Dictionary<string, float>
        {
            // Earth-like weather
            { "Clear", 0f },
            { "FogLight", -2f },
            { "FogHeavy", -5f },
            { "RainLight", -4f },
            { "RainHeavy", -7f },
            { "SnowLight", -6f },
            { "SnowHeavy", -10f },
            { "SandstormLight", +5f },
            { "SandstormHeavy", +10f },
            { "ThunderstormLight", -3f },
            { "ThunderstormHeavy", -6f },

            // Alien weather
            { "AlienFogLight", -5f },
            { "AlienFogHeavy", -8f },
            { "AlienRainLight", -6f },
            { "AlienRainHeavy", -10f },
            { "AlienThunderstormLight", -7f },
            { "AlienThunderstormHeavy", -12f },

            // Planet-specific weather
            { "MarsStormLight", +4f },
            { "MarsStormHeavy", +8f },
            { "MarsSnow", -15f },

            // Misc
            { "Dust", -2f },
            { "ElectricStorm", -10f }
        };

        private int tpm = 0;

        public void PurgeCaches()
        {
            _cacheSurfaceArea.Clear();
        }

        public float GetHeat(IMyCubeBlock block)
        {
            if (block.Storage == null)
            {
                block.Storage = new MyModStorageComponent();
            }
            string heatStr;
            if (block.Storage.TryGetValue(HeatKey, out heatStr))
            {
                float heat;
                if (float.TryParse(heatStr, out heat) && !float.IsNaN(heat) && !float.IsInfinity(heat))
                    return heat;
            }

            float fallbackAmbient = CalculateAmbientTemperature(block);
            block.Storage[HeatKey] = fallbackAmbient.ToString();
            return fallbackAmbient;
        }

        public void SetHeat(IMyCubeBlock block, float heat)
        {

            if (block.Storage == null)
            {
                block.Storage = new MyModStorageComponent();
            }

            if (float.IsNaN(heat) || float.IsInfinity(heat)){
                MyAPIGateway.Utilities.ShowNotification($"Wrong heat value for {block.DisplayNameText}: {heat}", 1000);
            }
            block.Storage[HeatKey] = heat.ToString();
        }

        public float GetRealSurfaceArea(IMyCubeBlock battery)
        {
            if (battery == null || battery.CubeGrid == null)
                return 0f;

            if (_cacheSurfaceArea.ContainsKey(battery.DisplayNameText))
            {
                return _cacheSurfaceArea[battery.DisplayNameText];
            }

            MyCubeBlockDefinition definition;
            // Get the block definition
            if (!MyDefinitionManager.Static.TryGetCubeBlockDefinition(battery.BlockDefinition, out definition))
                return 0f;

            // Calculate total surface area in block face units
            Vector3I size = definition.Size;
            float faceAreaX = size.Y * size.Z;
            float faceAreaY = size.X * size.Z;
            float faceAreaZ = size.X * size.Y;
            float maxFaceArea = Math.Max(faceAreaX, Math.Max(faceAreaY, faceAreaZ));
            float totalSurfaceArea = 2f * (faceAreaX + faceAreaY + faceAreaZ);

            float faceAreaInMeters = battery.CubeGrid.GridSize * battery.CubeGrid.GridSize;

            // Use GetNeighbours to find blocked faces
            float blockedArea = 0f;
            var slimBlock = battery.SlimBlock;
            if (slimBlock != null)
            {
                var neighbors = new List<IMySlimBlock>();
                slimBlock.GetNeighbours(neighbors);

                foreach (var neighbor in neighbors)
                {
                    if (neighbor == null || neighbor.FatBlock == null)
                        continue;

                    MyCubeBlockDefinition neighborDef;
                    // Get the biggest face of the neighbor
                    if (!MyDefinitionManager.Static.TryGetCubeBlockDefinition(neighbor.FatBlock.BlockDefinition, out neighborDef))
                        continue;

                    Vector3I nSize = neighborDef.Size;
                    float faceX = nSize.Y * nSize.Z;
                    float faceY = nSize.X * nSize.Z;
                    float faceZ = nSize.X * nSize.Y;

                    float maxFace = Math.Max(faceX, Math.Max(faceY, faceZ));
                    float blockedFace = Math.Min(maxFace, maxFaceArea);
                    blockedArea += blockedFace * faceAreaInMeters;
                }
            }

            float radiatingArea = (totalSurfaceArea * faceAreaInMeters) - blockedArea;
            float surfaceArea = Math.Max(radiatingArea, 0f);
            _cacheSurfaceArea[battery.DisplayNameText] = surfaceArea;
            return surfaceArea;
        }

        public float GetLargestFaceArea(IMySlimBlock block)
        {
            if (block.FatBlock == null)
                return 0f;

            var defId = block.FatBlock.BlockDefinition;
            MyCubeBlockDefinition definition;
            if (!MyDefinitionManager.Static.TryGetCubeBlockDefinition(defId, out definition))
                return 0f;

            var size = definition.Size; // In grid units
            float gridSize = block.CubeGrid.GridSize;

            // Calculate all three face areas
            float xy = size.X * size.Y;
            float xz = size.X * size.Z;
            float yz = size.Y * size.Z;

            float maxFaceArea = Math.Max(xy, Math.Max(xz, yz));

            return maxFaceArea * gridSize * gridSize; // Convert to m²
        }

        public bool IsBlockInPressurizedRoom(IMyCubeBlock block)
        {
            var grid = block.CubeGrid as MyCubeGrid;
            return grid.IsRoomAtPositionAirtight(block.Position);
        }

        public Vector3 GetSunDirection(IMyCubeBlock block, MyPlanet planet)
        {
            double daySeconds = MyAPIGateway.Session.GameDateTime.TimeOfDay.TotalSeconds;
            double dayFraction = (daySeconds % 86400) / 86400.0; // 0..1 for full day cycle

            // Approximate sun direction as a vector that rotates around the planet's up vector with dayFraction
            return Vector3D.Transform(Vector3D.UnitX, QuaternionD.CreateFromAxisAngle(planet.WorldMatrix.Up, dayFraction * Math.PI * 2));
        }

        public float CalculateAmbientTemperature(IMyCubeBlock block)
        {
            var position = block.GetPosition();

            if (IsBlockInPressurizedRoom(block))
            {
                return 20f;
            }

            return GetTemperatureOnPlanet(position);
        }

        public float GetWindSpeed(Vector3D position)
        {
            var planet = MyGamePruningStructure.GetClosestPlanet(position);
            if (planet == null)
                return 0f;
            return planet.GetWindSpeed(position);
        }

        public float GetGridSpeed(IMyCubeBlock block)
        {
            var grid = block.CubeGrid as MyCubeGrid;
            if (grid == null || grid.Physics == null)
                return 0f;

            // Return the magnitude of the grid's linear velocity (in m/s)
            return (float)grid.Physics.LinearVelocity.Length();
        }

        public float GetBlockWindSpeed(IMyCubeBlock block)
        {
            return GetWindSpeed(block.GetPosition()) + GetGridSpeed(block);
        }

        public float GetTemperatureOnPlanet(Vector3D position)
        {
            float ambientTemp = -180f; // Ambient space temperature

            var planet = MyGamePruningStructure.GetClosestPlanet(position);
            if (planet == null)
            {
                // No planet, use space ambient temperature
                return ambientTemp;
            }

            // Altitude-based temperature calculation
            float altitude = (float)(position - planet.PositionComp.WorldAABB.Center).Length() - (float)planet.AverageRadius;
            float normalizedAlt = MathHelper.Clamp(altitude / 10000f, 0f, 1f);
            ambientTemp = MathHelper.Lerp(25f, -50f, normalizedAlt);

            // Sunlight effect
            float fullDayNightTempSwing = 90f;
            float airDensity = planet.GetOxygenForPosition(position); // Based on oxygen, because GetDensity is broken
            float swingMultiplier = 1 - airDensity; // 0 at 100% oxygen, 1 at 0% oxygen
            Vector3 sunDirection = MyVisualScriptLogicProvider.GetSunDirection(); // Already normalized
            Vector3D gravityDirection = -planet.Components.Get<MyGravityProviderComponent>().GetWorldGravityNormalized(position);
            float dot = (float)Vector3D.Dot(gravityDirection, sunDirection); // dot of up and sun direction
            float dayNightFactor = MathHelper.Clamp(dot / 0.7f, -1f, 1f);
            float dayNightChange = dayNightFactor * fullDayNightTempSwing * swingMultiplier;
            ambientTemp += dayNightChange; // Add some heat from the sun

            // Weather effects
            string currentWeather = MyAPIGateway.Session.WeatherEffects.GetWeather(position);
            float weatherIntensity = MyAPIGateway.Session.WeatherEffects.GetWeatherIntensity(position);
            float weatherModifier;
            if (WeatherTemperatureModifiers.TryGetValue(currentWeather, out weatherModifier))
            {
                ambientTemp += weatherModifier * weatherIntensity; // Apply weather effect
            }

            return ambientTemp;
        }

        public float GetThermalCapacity(IMyCubeBlock block)
        {
            if (_cacheTermalCapacity.ContainsKey(block.DisplayNameText))
            {
                return _cacheTermalCapacity[block.DisplayNameText];
            }

            float mass = GetMass(block);
            float density = GetDensity(block);

            float specificHeatCapacity = EstimateSpecificHeat(density); // J/kg·°C

            // Total thermal capacity = mass * specific heat
            float capacity = mass * specificHeatCapacity; // J/°C

            _cacheTermalCapacity[block.DisplayNameText] = capacity;

            return capacity;
        }

        public float EstimateSpecificHeat(float density)
        {
            // Rough inverse curve — higher density -> lower specific heat
            // Tunable parameters:
            const float minSH = 300f; // Lower bound (very dense material like steel)
            const float maxSH = 1000f; // Upper bound (low-density material like composites)

            const float refLow = 1000f;   // low density ~ aluminum
            const float refHigh = 8000f;  // high density ~ steel

            float t = MathHelper.Clamp((density - refLow) / (refHigh - refLow), 0f, 1f);
            return maxSH - t * (maxSH - minSH);
        }

        public float GetMass(IMyCubeBlock block)
        {
            var defId = block.BlockDefinition;
            MyCubeBlockDefinition definition;

            if (!MyDefinitionManager.Static.TryGetCubeBlockDefinition(defId, out definition))
                return 0f;

            return definition.Mass; // kg
        }

        public float GetDensity(IMyCubeBlock block)
        {
            var defId = block.BlockDefinition;
            MyCubeBlockDefinition definition;

            if (!MyDefinitionManager.Static.TryGetCubeBlockDefinition(defId, out definition))
                return 0f;

            var slimBlock = block.SlimBlock;
            var grid = block.CubeGrid;

            if (slimBlock == null || grid == null)
                return 0f;

            // Get block size in meters
            Vector3I sizeInBlocks = slimBlock.Max - slimBlock.Min + Vector3I.One;
            float blockVolume = sizeInBlocks.X * sizeInBlocks.Y * sizeInBlocks.Z * grid.GridSize * grid.GridSize * grid.GridSize; // m³

            float mass = definition.Mass; // kg
            if (mass <= 0f || blockVolume <= 0f)
                return 0f;

            // Density = mass / volume
            return mass / blockVolume; // kg/m³
        }

        public float GetAmbientHeatLoss(IMyCubeBlock block, float deltaTime)
        {
            var ambientTemp = CalculateAmbientTemperature(block);
            var currentHeat = GetHeat(block);
            var thermalCapacity = GetThermalCapacity(block);
            var surfaceArea = GetRealSurfaceArea(block);

            float energyLoss = (currentHeat - ambientTemp) * surfaceArea * Config.Instance.HEAT_COOLDOWN_COEFF * deltaTime;
            float heatLoss = energyLoss / thermalCapacity; // °C lost

            return heatLoss;
        }
        public float GetActiveVentHealLoss(IMyAirVent vent, float deltaTime)
        {
            float currentTemp = GetHeat(vent);
            float ambientTemp = CalculateAmbientTemperature(vent) - 2f; // 2 degrees lower than ambient to simulate cooling effect
            float airDensity = vent.GetOxygenLevel();
            float coolingPower = Config.Instance.VENT_COOLING_RATE * airDensity;

            float tempDiff = currentTemp - ambientTemp;

            float heatRemoved = coolingPower * deltaTime * tempDiff / GetThermalCapacity(vent); // Joules removed

            return heatRemoved;
        }

        public float GetActiveThrusterHeatLoss(IMyThrust thruster, float thrustRatio, float deltaTime)
        {
            float baseCoolingRate = Config.Instance.THRUSTER_COOLING_RATE; // Tunable parameter
            float effectiveness = MathHelper.Clamp(thrustRatio, 0f, 1f);

            float ambientTemp = CalculateAmbientTemperature(thruster) - 2f; // 2 degrees lower than ambient to simulate cooling effect
            float currentTemp = GetHeat(thruster);
            float deltaT = currentTemp - ambientTemp;

            // Cooling scales with thrust output
            return deltaT * baseCoolingRate * effectiveness * deltaTime / GetThermalCapacity(thruster); // Joules removed
        }

        public float ApplyHeatChange(IMyCubeBlock block, float heatChange)
        {
            float currentHeat = GetHeat(block);
            float newHeat = currentHeat + heatChange;
            SetHeat(block, currentHeat + heatChange);
            return newHeat;
        }
    }
}