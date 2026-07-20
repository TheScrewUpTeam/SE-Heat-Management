using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using System.Collections.Generic;
using System.Collections.Concurrent;
using VRage.Game;
using VRage.Utils;
using Sandbox.ModAPI.Interfaces.Terminal;
using System.Linq;
using System;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game.Entities;
using VRageMath;


namespace TSUT.HeatManagement
{

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class HeatSession : MySessionComponentBase
    {
        private static HeatApi _heatApi = new HeatApi();

        public static HeatApi Api
        {
            get { return _heatApi; }
        }

        public static bool isInspectorActive;

        public static Networking networking = new Networking(Config.HeatSyncMessageId);

        private static bool _initialized = false;
        public static int _tickCount = 0;

        private static ConcurrentDictionary<long, IHeatBehavior> _trackedNetworkBlocks = new ConcurrentDictionary<long, IHeatBehavior>();
        private static readonly ConcurrentDictionary<long, GridHeatComponent> _gridComponentCache = new ConcurrentDictionary<long, GridHeatComponent>();

        // Fallback cache for when another mod clobbers Entity.GameLogic on a grid (e.g. via raw
        // Components.Add() instead of the composite-safe registration path), which makes
        // grid.GameLogic.GetAs<GridO2Manager>() permanently return null even though the component
        // is alive and ticking.
        private static readonly ConcurrentDictionary<long, GridO2Manager> _o2ManagerCache = new ConcurrentDictionary<long, GridO2Manager>();

        public static void RegisterGridComponent(long entityId, GridHeatComponent component)
        {
            _gridComponentCache[entityId] = component;
        }

        public static void UnregisterGridComponent(long entityId)
        {
            GridHeatComponent removed;
            _gridComponentCache.TryRemove(entityId, out removed);
        }

        public static void RegisterO2Manager(long entityId, GridO2Manager component)
        {
            _o2ManagerCache[entityId] = component;
        }

        public static void UnregisterO2Manager(long entityId)
        {
            GridO2Manager removed;
            _o2ManagerCache.TryRemove(entityId, out removed);
        }

        public static bool TryGetO2Manager(IMyCubeGrid grid, out GridO2Manager manager)
        {
            manager = null;
            return grid != null && _o2ManagerCache.TryGetValue(grid.EntityId, out manager);
        }

        public static Config Config;

        private HeatCommands _commandsInstance;
        public static HeatSession Instance { get; private set; }

        public override void LoadData()
        {
            // Load config (will use defaults if file doesn't exist)
            Config = Config.Instance;
            Instance = this;
            HeatLog.Info("HeatSession instance created.", LS.Grid);

            MyAPIGateway.Utilities.RegisterMessageHandler(HmsApi.HeatProviderMesageId, OnHeatProviderRegister);
            MyAPIGateway.Utilities.RegisterMessageHandler(HmsApi.HeatApiRequestMessageId, OnHeatApiRequested);
            var shareable = ConvertApiToShareable(_heatApi);
            MyAPIGateway.Utilities.SendModMessage(HmsApi.HeatApiMessageId, shareable);
            HeatLog.Info("HeatAPI populated.", LS.Grid);
            _commandsInstance = HeatCommands.Instance; // Initialize commands

            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
        }

        private void OnHeatApiRequested(object obj)
        {
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                var shareable = ConvertApiToShareable(_heatApi);
                MyAPIGateway.Utilities.SendModMessage(HmsApi.HeatApiMessageId, shareable);
                HeatLog.Info("HeatAPI resent on request.", LS.Grid);
            });
        }

        private void OnHeatProviderRegister(object obj)
        {
            Dictionary<string, object> call = obj as Dictionary<string, object>;
            object method;
            if (call.TryGetValue("factory", out method) && method is Func<long, IDictionary<long, IDictionary<string, object>>>)
            {
                var factory = (Func<long, IDictionary<long, IDictionary<string, object>>>)method;
                _heatApi.Registry.RegisterHeatBehaviorProvider(factory);
            }
            if (call.TryGetValue("creator", out method) && method is Func<long, IDictionary<string, object>>)
            {
                var mapper = (Func<long, IDictionary<string, object>>)method;
                _heatApi.Registry.RegisterHeatMapper(mapper);
            }
            if (call.TryGetValue("blockId", out method) && method is long)
            {
                object behaviorObj;
                if (call.TryGetValue("behavior", out behaviorObj) && behaviorObj is IDictionary<string, object>)
                {
                    var blockId = (long)method;
                    var behavior = (IDictionary<string, object>)behaviorObj;
                    _heatApi.Registry.RegisterDirectBlockBehavior(blockId, behavior);

                    var block = MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock;
                    GridHeatComponent gridComp;
                    if (block?.CubeGrid != null && _gridComponentCache.TryGetValue(block.CubeGrid.EntityId, out gridComp))
                        if (!(gridComp.TryGetHeatBehaviour(block) is AHeatGameLogicComponent))
                            gridComp.RegisterBehavior(block, new DelegateHeatBehavior(behavior, block as MyCubeBlock));
                }
            }
        }

        public static IHeatBehavior GetBehaviorForBlock(IMyCubeBlock block)
        {
            if (block == null)
                return null;

            IHeatBehavior behavior;
            if (_trackedNetworkBlocks.TryGetValue(block.EntityId, out behavior))
                return behavior;

            GridHeatComponent component;
            if (block.CubeGrid != null && _gridComponentCache.TryGetValue(block.CubeGrid.EntityId, out component))
                component.TryGetBehaviorForBlock(block, out behavior);
            return behavior;
        }

        protected override void UnloadData()
        {
            _commandsInstance?.Unload();
            networking?.Unregister();
            MyAPIGateway.Utilities.UnregisterMessageHandler(HmsApi.HeatProviderMesageId, OnHeatProviderRegister);
            MyAPIGateway.Utilities.UnregisterMessageHandler(HmsApi.HeatApiRequestMessageId, OnHeatApiRequested);
            MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
            _gridComponentCache.Clear();
        }

        public override void BeforeStart()
        {
            var shareable = ConvertApiToShareable(_heatApi);
            MyAPIGateway.Utilities.SendModMessage(HmsApi.HeatApiMessageId, shareable);
            HeatLog.Info("HeatAPI populated late.", LS.Grid);
            networking.Register();
            RegisterCustomControls();

            networking.SendToServer(new RequestHeatConfig());

            // Catch grids that were already created before our OnEntityAdd subscription went live.
            var existingEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(existingEntities, e => e is IMyCubeGrid);
            foreach (var entity in existingEntities)
            {
                var grid = (IMyCubeGrid)entity;
                EnsureGridComponent(grid);
                EnsureO2Manager(grid);
            }
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid == null)
                return;
            EnsureGridComponent(grid);
            EnsureO2Manager(grid);
        }

        internal static void EnsureGridComponent(IMyCubeGrid grid)
        {
            if (grid == null || _gridComponentCache.ContainsKey(grid.EntityId))
                return;
            var component = new GridHeatComponent();
            component.AttachTo(grid);
        }

        internal static void EnsureO2Manager(IMyCubeGrid grid)
        {
            if (grid == null || _o2ManagerCache.ContainsKey(grid.EntityId))
                return;
            var component = new GridO2Manager();
            component.AttachTo(grid);
        }


        public override void UpdateBeforeSimulation()
        {
            ClientSideUpdates();
            TickGridComponents();
            TickO2Managers();
            _tickCount++;
        }

        private void TickGridComponents()
        {
            foreach (var component in new List<GridHeatComponent>(_gridComponentCache.Values))
            {
                if (!component.IsInitialized)
                    component.TickInitialize();
                else if (!component.IsSkipped)
                    component.TickAfterSimulation();
            }
        }

        private void TickO2Managers()
        {
            foreach (var component in new List<GridO2Manager>(_o2ManagerCache.Values))
            {
                if (!component.IsInitialized)
                    component.TickInitialize();
                else if (!component.IsSkipped)
                    component.TickAfterSimulation();
            }
        }

        private IMyHudNotification _debugHud;
        private IMySlimBlock _lastDetected;
        private float _lastTemp;

        private void DrawDebug()
        {
            if (!isInspectorActive)
                return;
            if (_debugHud == null)
                _debugHud = MyAPIGateway.Utilities.CreateNotification("", 1000, "White");

            var cameraPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            var forward = MyAPIGateway.Session.Camera.WorldMatrix.Forward;

            IHitInfo hit;
            MyAPIGateway.Physics.CastRay(cameraPos, cameraPos + forward * 50, out hit);

            if (hit?.HitEntity is IMyCubeGrid)
            {
                var grid = hit?.HitEntity as IMyCubeGrid;
                Vector3I cell = grid.WorldToGridInteger(hit.Position);
                var block = grid.GetCubeBlock(cell);

                if (block != null && block.FatBlock != null)
                {
                    float temp = _heatApi.Utils.GetHeat(block.FatBlock);

                    if (temp != _lastTemp || _lastDetected != block) {
                        _debugHud.Hide();
                        _debugHud.Text = $"[>HMS<] {block.FatBlock.DisplayNameText}: {temp:F2} °C";
                        _debugHud.Show();
                        _lastTemp = temp;
                        _lastDetected = block;
                    } else
                    {
                        _debugHud.Show();
                    }
                }
            } else
            {
                _debugHud.Hide();
            }
        }

        private void ClientSideUpdates()
        {
            _heatApi.Effects.UpdateLightsPosition();
            DrawDebug();

            if (_tickCount % Config.MAIN_UPDATE_INTERVAL_TICKS == 0)
            {
                var eventControllers = new List<IEventControllerEvent>(_heatApi.Registry.GetEventControllerEvents());
                // Notify all event controller events
                foreach (var eventControllerEvent in eventControllers)
                {
                    if (eventControllerEvent != null && eventControllerEvent is BlockTemperatureChanged)
                    {
                        var heatEvent = eventControllerEvent as BlockTemperatureChanged;
                        heatEvent.NotifyValuesChanged();
                    }
                    else if (eventControllerEvent != null && eventControllerEvent is GridMaxTemperatureChanged)
                    {
                        var heatEvent = eventControllerEvent as GridMaxTemperatureChanged;
                        heatEvent.NotifyValuesChanged();
                    }
                }
            }
        }

        public static void UpdateEventControllers(long entityId)
        {
            foreach (var eventController in _heatApi.Registry.GetEventControllerEvents())
            {
                if (eventController != null)
                {
                    eventController.UpdateDetailedInfo(entityId);
                }
            }
        }

        internal static void UpdateEventControllerSettings(long entityId, float threshold)
        {
            foreach (var eventController in _heatApi.Registry.GetEventControllerEvents())
            {
                if (eventController != null)
                {
                    eventController.UpdateSettings(entityId, threshold);
                }
            }
        }

        public override void SaveData()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                Config.Save();
            }
        }


        internal static void UpdateUI(long entityId, float heat)
        {
            IMyEntity ent;
            if (MyAPIGateway.Entities.TryGetEntityById(entityId, out ent))
            {
                var block = ent as IMyCubeBlock;
                if (block != null)
                {
                    _heatApi.Utils.SetHeat(block, heat, true);
                    GridHeatComponent comp;
                    if (block.CubeGrid != null && _gridComponentCache.TryGetValue(block.CubeGrid.EntityId, out comp))
                        comp.TryReactOnHeat(block, heat);
                }
            }
        }

        internal static void UpdateNetowkrsUI(long gridId, List<HeatValuePair> heats)
        {
            try
            {
                HeatLog.Info($"Received network heat update for grid {gridId} with {heats.Count} entries.", LS.Sync);
                var grid = MyAPIGateway.Entities.GetEntityById(gridId) as IMyCubeGrid;
                if (grid == null)
                {
                    HeatLog.Warn($"Could not find grid with ID {gridId}.", LS.Sync);
                    return;
                }
                GridHeatComponent component;
                _gridComponentCache.TryGetValue(grid.EntityId, out component);
                if (component == null)
                {
                    HeatLog.Warn($"No GridHeatComponent on grid {grid.DisplayName}.", LS.Sync);
                    return;
                }
                HeatLog.Info($"Found grid {grid.DisplayName}.", LS.Sync);
                foreach (var heatPair in heats)
                {
                    var block = MyAPIGateway.Entities.GetEntityById(heatPair.BlockId) as IMyCubeBlock;
                    if (block == null) continue;
                    _heatApi.Utils.SetHeat(block, heatPair.Heat, true);
                    component.TryReactOnHeat(block, heatPair.Heat);
                }
                HeatLog.Info($"Updated {heats.Count} blocks.", LS.Sync);
            }
            catch (Exception e)
            {
                HeatLog.Warn($"Exception in UpdateNetowkrsUI: {e}", LS.Sync);
            }
        }

        public static bool TryGetGridHeatManager(IMyCubeGrid grid, out GridHeatComponent manager)
        {
            manager = null;
            return grid != null && _gridComponentCache.TryGetValue(grid.EntityId, out manager);
        }

        public static void RebuildEverything()
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                networking.SendToServer(new RebuildNetworks());
                return;
            }
            var allEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(allEntities);
            foreach (var entity in allEntities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null) continue;
                GridHeatComponent comp;
                if (_gridComponentCache.TryGetValue(grid.EntityId, out comp)) comp.Rebuild();
            }
        }

        public static void DropAllTemperatures()
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                networking.SendToServer(new RequestTempDrop());
                return;
            }
            var allEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(allEntities);
            foreach (var entity in allEntities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null) continue;
                GridHeatComponent comp;
                if (_gridComponentCache.TryGetValue(grid.EntityId, out comp)) comp.DropAll();
            }
        }

        // grid.GetBlocks enumerates the grid's internal block collection directly. If the grid is
        // being mutated at the same time (e.g. a large blueprint paste still adding blocks), the
        // enumerator throws InvalidOperationException. Retry a few times since the mutation window
        // is transient; if it never settles, let the exception propagate so the caller can skip
        // this tick and retry later, rather than treating it as "not a wheel grid".
        public static void GetBlocksSafe(IMyCubeGrid grid, List<IMySlimBlock> blocks, int maxAttempts = 5)
        {
            for (int attempt = 0; ; attempt++)
            {
                blocks.Clear();
                try
                {
                    grid.GetBlocks(blocks);
                    return;
                }
                catch (InvalidOperationException)
                {
                    if (attempt >= maxAttempts - 1)
                        throw;
                }
            }
        }

        public static bool IsWheelGrid(IMyCubeGrid grid)
        {
            var slimBlocks = new List<IMySlimBlock>();
            GetBlocksSafe(grid, slimBlocks);

            // wheel grids have exactly one block and it's a wheel part
            return slimBlocks.Count == 1 && slimBlocks[0].FatBlock is IMyWheel;
        }

        public static void RegisterCustomControls()
        {
            foreach (var factory in _heatApi.Registry.GetFactories())
            {
                factory.RegisterCustomControls();
            }
            if (_initialized)
                return;

            _initialized = true;

        }

        private Dictionary<string, object> ConvertApiToShareable(HeatApi heatApi)
        {
            return new Dictionary<string, object>
            {
                { "CalculateAmbientTemperature", new Func<long, float>(blockId =>
                    heatApi.Utils.CalculateAmbientTemperature(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "EstimateSpecificHeat", new Func<float, float>(density =>
                heatApi.Utils.EstimateSpecificHeat(density)) },
                { "GetActiveThrusterHeatLoss", new Func<long, float, float, float>((thrusterId, ratio, dt) =>
                    heatApi.Utils.GetActiveThrusterHeatLoss(MyAPIGateway.Entities.GetEntityById(thrusterId) as IMyThrust, ratio, dt)) },
                { "GetActiveVentHealLoss", new Func<long, float, float>((ventId, dt) =>
                    heatApi.Utils.GetActiveVentHealLoss(MyAPIGateway.Entities.GetEntityById(ventId) as IMyAirVent, dt)) },
                { "GetActiveHeatVentLoss", new Func<long, float, float>((ventId, dt) =>
                    heatApi.Utils.GetActiveHeatVentLoss(MyAPIGateway.Entities.GetEntityById(ventId) as IMyHeatVent, dt)) },
                { "GetAmbientHeatLoss", new Func<long, float, float>((blockId, dt) =>
                    heatApi.Utils.GetAmbientHeatLoss(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock, dt)) },
                { "GetDensity", new Func<long, float>(blockId =>
                    heatApi.Utils.GetDensity(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "GetHeat", new Func<long, float>(blockId =>
                    heatApi.Utils.GetHeat(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "GetLargestFaceArea", new Func<long, float>(slimId =>
                    heatApi.Utils.GetLargestFaceArea(MyAPIGateway.Entities.GetEntityById(slimId) as IMySlimBlock)) },
                { "GetMass", new Func<long, float>(blockId =>
                    heatApi.Utils.GetMass(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "GetRealSurfaceArea", new Func<long, float>(blockId =>
                    heatApi.Utils.GetRealSurfaceArea(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "GetSunDirection", new Func<long, long, VRageMath.Vector3D>((blockId, planetId) =>
                    heatApi.Utils.GetSunDirection(
                        MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock,
                        MyAPIGateway.Entities.GetEntityById(planetId) as MyPlanet)) },
                { "GetTemperatureOnPlanet", new Func<VRageMath.Vector3D, float>(pos =>
                    heatApi.Utils.GetTemperatureOnPlanet(pos)) },
                { "GetThermalCapacity", new Func<long, float>(blockId =>
                    heatApi.Utils.GetThermalCapacity(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "IsBlockInPressurizedRoom", new Func<long, bool>(blockId =>
                    heatApi.Utils.IsBlockInPressurizedRoom(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "PurgeCaches", new Action(() => heatApi.Utils.PurgeCaches()) },
                { "SetHeat", new Action<long, float, bool>((blockId, heat, silent) =>
                    heatApi.Utils.SetHeat(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock, heat, silent)) },
                { "ApplyHeatChange", new Func<long, float, float>((blockId, heat) =>
                    heatApi.Utils.ApplyHeatChange(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock, heat)) },
                { "GetBlockWindSpeed", new Func<long, float>(blockId =>
                    heatApi.Utils.GetBlockWindSpeed(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "GetExchangeWithNeighbor", new Func<long, long, float, float>((blockId, neighborId, dt) =>
                    heatApi.Utils.GetExchangeWithNeighbor(
                        MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock,
                        MyAPIGateway.Entities.GetEntityById(neighborId) as IMyCubeBlock,
                        dt)) },
                { "GetAirDensity", new Func<long, float>(blockId =>
                    heatApi.Utils.GetAirDensity(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "GetActiveExhaustHeatLoss", new Func<long, float, float>((exhaustId, dt) =>
                    heatApi.Utils.GetActiveExhaustHeatLoss(MyAPIGateway.Entities.GetEntityById(exhaustId) as IMyExhaustBlock, dt)) },
                { "InstantiateSmoke", new Action<long>(blockId => heatApi.Effects.InstantiateSmoke(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock))},
                { "RemoveSmoke", new Action<long>(blockId => heatApi.Effects.RemoveSmoke(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock)) },
                { "UpdateBlockHeatLight", new Action<long, float>((blockId, heat) => heatApi.Effects.UpdateBlockHeatLight(MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock, heat)) },
                { "UpdateLightsPosition", new Action(() => heatApi.Effects.UpdateLightsPosition()) },
                { "GetNetworkData", new Func<long, object>(blockId =>
                    {
                        var block = MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock;
                        if (block == null)
                            return null;

                        var behavior = GetBehaviorForBlock(block);

                        if (behavior == null || !(behavior is HeatPipeManager))
                            return null;

                        var heatPipeManager = behavior as HeatPipeManager;
                        return new Dictionary<string, object>(3)
                        {
                            { "hash", heatPipeManager.GetNetworkHash() },
                            { "length", heatPipeManager.GetNetworkSize() },
                            { "averageTemperature", heatPipeManager.GetAverageTemperature() }
                        };
                    })
                },
                {
                    "GetExchangeWithNetwork", new Func<long, long, float, float>((blockId, networkBlockId, dt) =>
                        heatApi.Utils.GetExchangeWithNetwork(
                            MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock,
                            MyAPIGateway.Entities.GetEntityById(networkBlockId) as IMyCubeBlock,
                            dt))
                },
                {
                    "GetExchangeUniversal", new Func<long, long, float, float>((blockId, neighborBlockId, dt) =>
                        heatApi.Utils.GetExchangeUniversal(
                            MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock,
                            MyAPIGateway.Entities.GetEntityById(neighborBlockId) as IMyCubeBlock,
                            dt))
                },
                {
                    "ConsumeO2", new Func<float, float, long, float>((amount, deltaTime, blockId) =>
                        heatApi.Utils.ConsumeO2(
                            amount,
                            deltaTime,
                            MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock
                            ))
                },
                {
                    "HasEnoughO2", new Func<float, float, long, bool>((amount, deltaTime, blockId) =>
                        heatApi.Utils.HasEnoughO2(
                            amount,
                            deltaTime,
                            MyAPIGateway.Entities.GetEntityById(blockId) as IMyCubeBlock
                            ))
                },
                {
                    "GetHmsConfig", new Func<object>(() =>
                        new Dictionary<string, object> {
                            { "HEAT_COOLDOWN_COEFF", Config.Instance.HEAT_COOLDOWN_COEFF },
                            { "HEAT_RADIATION_COEFF", Config.Instance.HEAT_RADIATION_COEFF },
                            { "DISCHARGE_HEAT_FRACTION", Config.Instance.DISCHARGE_HEAT_FRACTION },
                            { "THERMAL_CONDUCTIVITY", Config.Instance.THERMAL_CONDUCTIVITY },
                            { "VENT_COOLING_RATE", Config.Instance.VENT_COOLING_RATE },
                            { "THRUSTER_COOLING_RATE", Config.Instance.THRUSTER_COOLING_RATE },
                            { "CRITICAL_TEMP", Config.Instance.CRITICAL_TEMP },
                            { "WIND_COOLING_MULT", Config.Instance.WIND_COOLING_MULT },
                            { "HEATPIPE_CONDUCTIVITY", Config.Instance.HEATPIPE_CONDUCTIVITY },
                            { "EXHAUST_HEAT_REJECTION_RATE", Config.Instance.EXHAUST_HEAT_REJECTION_RATE },
                            { "LIMIT_TO_PLAYER_GRIDS", Config.Instance.LIMIT_TO_PLAYER_GRIDS },
                            { "HEAT_GLOW_INDICATION", Config.Instance.HEAT_GLOW_INDICATION },
                            { "HEAT_SYSTEM_VERSION", Config.Instance.HEAT_SYSTEM_VERSION },
                            { "HEAT_SYSTEM_AUTO_UPDATE", Config.Instance.HEAT_SYSTEM_AUTO_UPDATE }
                        }
                    )
                }
            };
        }

        internal static void OnHeatConfigRequested(RequestHeatConfig request)
        {
            var message = new HeatConfigResponse
            {
                HEAT_COOLDOWN_COEFF = Config.Instance.HEAT_COOLDOWN_COEFF,
                HEAT_RADIATION_COEFF = Config.Instance.HEAT_RADIATION_COEFF,
                DISCHARGE_HEAT_FRACTION = Config.Instance.DISCHARGE_HEAT_FRACTION,
                THERMAL_CONDUCTIVITY = Config.Instance.THERMAL_CONDUCTIVITY,
                VENT_COOLING_RATE = Config.Instance.VENT_COOLING_RATE,
                THRUSTER_COOLING_RATE = Config.Instance.THRUSTER_COOLING_RATE,
                CRITICAL_TEMP = Config.Instance.CRITICAL_TEMP,
                WIND_COOLING_MULT = Config.Instance.WIND_COOLING_MULT,
                HEATPIPE_CONDUCTIVITY = Config.Instance.HEATPIPE_CONDUCTIVITY,
                EXHAUST_HEAT_REJECTION_RATE = Config.Instance.EXHAUST_HEAT_REJECTION_RATE,
                LIMIT_TO_PLAYER_GRIDS = Config.Instance.LIMIT_TO_PLAYER_GRIDS,
                HEAT_GLOW_INDICATION = Config.Instance.HEAT_GLOW_INDICATION,
                HEAT_SYSTEM_VERSION = Config.Instance.HEAT_SYSTEM_VERSION,
                HEAT_SYSTEM_AUTO_UPDATE = Config.Instance.HEAT_SYSTEM_AUTO_UPDATE
            };

            networking.SendToPlayer(message, request.SenderId);
        }

        internal static void UpdateHeatConfig(HeatConfigResponse heatConfigResponse)
        {
            Config.Instance.HEAT_COOLDOWN_COEFF = heatConfigResponse.HEAT_COOLDOWN_COEFF;
            Config.Instance.HEAT_RADIATION_COEFF = heatConfigResponse.HEAT_RADIATION_COEFF;
            Config.Instance.DISCHARGE_HEAT_FRACTION = heatConfigResponse.DISCHARGE_HEAT_FRACTION;
            Config.Instance.THERMAL_CONDUCTIVITY = heatConfigResponse.THERMAL_CONDUCTIVITY;
            Config.Instance.VENT_COOLING_RATE = heatConfigResponse.VENT_COOLING_RATE;
            Config.Instance.THRUSTER_COOLING_RATE = heatConfigResponse.THRUSTER_COOLING_RATE;
            Config.Instance.CRITICAL_TEMP = heatConfigResponse.CRITICAL_TEMP;
            Config.Instance.WIND_COOLING_MULT = heatConfigResponse.WIND_COOLING_MULT;
            Config.Instance.HEATPIPE_CONDUCTIVITY = heatConfigResponse.HEATPIPE_CONDUCTIVITY;
            Config.Instance.EXHAUST_HEAT_REJECTION_RATE = heatConfigResponse.EXHAUST_HEAT_REJECTION_RATE;
            Config.Instance.LIMIT_TO_PLAYER_GRIDS = heatConfigResponse.LIMIT_TO_PLAYER_GRIDS;
            Config.Instance.HEAT_GLOW_INDICATION = heatConfigResponse.HEAT_GLOW_INDICATION;
            Config.Instance.HEAT_SYSTEM_VERSION = heatConfigResponse.HEAT_SYSTEM_VERSION;
            Config.Instance.HEAT_SYSTEM_AUTO_UPDATE = heatConfigResponse.HEAT_SYSTEM_AUTO_UPDATE;
        }
    }
}