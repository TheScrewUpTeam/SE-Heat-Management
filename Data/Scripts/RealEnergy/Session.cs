using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Utils;
using Sandbox.ModAPI.Interfaces.Terminal;
using System.Linq;
using System;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game.Entities;


namespace TSUT.HeatManagement
{

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class HeatSession : MySessionComponentBase
    {
        const int MAIN_UPDATE_INTERVAL = 30; // in ticks
        const int NEIGHBOR_UPDATE_INTERVAL = 120; // in ticks

        private static HeatApi _heatApi = new HeatApi();

        public static HeatApi Api
        {
            get { return _heatApi; }
        }

        public static Networking networking = new Networking(Config.HeatSyncMessageId);

        private static Dictionary<IMyCubeGrid, GridHeatManager> _gridHeatManagers = new Dictionary<IMyCubeGrid, GridHeatManager>();

        private static bool _initialized = false;
        public static int _tickCount = 0;
        private int _lastMainUpdateTick = 0;
        private int _lastNeighborUpdateTick = 0;

        private static Dictionary<long, IHeatBehavior> _trackedNetworkBlocks = new Dictionary<long, IHeatBehavior>();

        public static Config Config;

        private static HashSet<IMyCubeGrid> _ownershipSubscribedGrids = new HashSet<IMyCubeGrid>();

        private HeatCommands _commandsInstance;
        public static HeatSession Instance { get; private set; }

        public override void LoadData()
        {
            // Load config (will use defaults if file doesn't exist)
            Config = Config.Instance;
            Instance = this;
            MyLog.Default.WriteLine($"[HeatManagement] HeatSession instance created.");

            _heatApi.Registry.RegisterHeatBehaviorFactory(new BatteryHeatManagerFactory());
            _heatApi.Registry.RegisterHeatBehaviorFactory(new VentHeatManagerFactory());
            _heatApi.Registry.RegisterHeatBehaviorFactory(new ExhaustHeatManagerFactory());
            _heatApi.Registry.RegisterHeatBehaviorFactory(new ThrusterHeatManagerFactory());
            _heatApi.Registry.RegisterHeatBehaviorFactory(new HeatPipeManagerFactory());
            _heatApi.Registry.RegisterHeatBehaviorFactory(new HeatVentManagerFactory());

            MyAPIGateway.Utilities.RegisterMessageHandler(HmsApi.HeatProviderMesageId, OnHeatProviderRegister);
            var shareable = ConvertApiToShareable(_heatApi);
            MyAPIGateway.Utilities.SendModMessage(HmsApi.HeatApiMessageId, shareable);
            MyLog.Default.WriteLine($"[HeatManagement] HeatAPI populated");
            _commandsInstance = HeatCommands.Instance; // Initialize commands
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
        }

        public static IHeatBehavior GetBehaviorForBlock(IMyCubeBlock block)
        {
            if (block == null)
                return null;

            IHeatBehavior behavior;
            if (_trackedNetworkBlocks.TryGetValue(block.EntityId, out behavior))
            {
                return behavior;
            }

            GridHeatManager manager;
            if (_gridHeatManagers.TryGetValue(block.CubeGrid, out manager))
            {
                manager.TryGetBehaviorForBlock(block, out behavior);
            }

            return behavior;
        }

        protected override void UnloadData()
        {
            _commandsInstance?.Unload();
            networking?.Unregister();
            MyAPIGateway.Utilities.UnregisterMessageHandler(HmsApi.HeatProviderMesageId, OnHeatProviderRegister);
        }

        public override void BeforeStart()
        {
            var shareable = ConvertApiToShareable(_heatApi);
            MyAPIGateway.Utilities.SendModMessage(HmsApi.HeatApiMessageId, shareable);
            MyLog.Default.WriteLine($"[HeatManagement] HeatAPI populated late");
            networking.Register();
            RegisterDebugControl();

            HashSet<IMyEntity> allEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(allEntities);
            foreach (var entity in allEntities)
            {
                OnEntityAdd(entity);
            }

            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
            MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;

            networking.SendToServer(new RequestHeatConfig());
        }

        private void OnEntityRemove(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid == null) return;
            GridHeatManager manager;
            if (_gridHeatManagers.TryGetValue(grid, out manager))
            {
                manager.Cleanup();
                _gridHeatManagers.Remove(grid);
                MyLog.Default.WriteLine($"[HeatManagement] Grid added. Total grids with heat management: {_gridHeatManagers.Count}");
            }
            if (_ownershipSubscribedGrids.Contains(grid))
            {
                grid.OnBlockAdded -= OnBlockAdded;
                _ownershipSubscribedGrids.Remove(grid);
            }
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid == null) return;
            if (IsWheelGrid(grid))
                return;
            if (_gridHeatManagers.ContainsKey(grid))
                return;
            if (_ownershipSubscribedGrids.Contains(grid))
                return;

            MyLog.Default.WriteLine($"[HeatManagement] Processing grid {grid.DisplayName} ({grid.EntityId})");

            // Always subscribe to ownership changes for all terminal blocks
            if (!_ownershipSubscribedGrids.Contains(grid))
            {
                var terminalBlocks = new List<IMyTerminalBlock>();
                MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(terminalBlocks);
                foreach (var block in terminalBlocks)
                {
                    block.OwnershipChanged += OnAnyBlockOwnershipChanged;
                }
                _ownershipSubscribedGrids.Add(grid);
                grid.OnBlockAdded += OnBlockAdded;
            }

            // If config is set, only add grids owned by the local player
            if (Config != null && Config.LIMIT_TO_PLAYER_GRIDS && !IsPlayrGrid(grid))
                return;

            _gridHeatManagers[grid] = new GridHeatManager(grid);

            MyLog.Default.WriteLine($"[HeatManagement] Grid added. Total grids with heat management: {_gridHeatManagers.Count}");
        }

        private static bool IsPlayrGrid(IMyCubeGrid grid)
        {
            if (grid == null)
                return false;

            var terminalBlocks = new List<IMyTerminalBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid)?.GetBlocks(terminalBlocks);
            foreach (var block in terminalBlocks)
            {
                if (block.OwnerId == 0)
                    continue;

                var identity = MyAPIGateway.Players.TryGetIdentityId(block.OwnerId);
                if (identity != null)
                {
                    return true; // At least one terminal block is owned by a player
                }
            }

            return false;
        }

        private void OnBlockAdded(IMySlimBlock block)
        {
            if (block.FatBlock == null)
            {
                return;
            }
            if (block.FatBlock is IMyTerminalBlock)
            {
                OnAnyBlockOwnershipChanged(block.FatBlock as IMyTerminalBlock);
            }
        }

        private void OnAnyBlockOwnershipChanged(IMyTerminalBlock block)
        {
            OnGridOwnershipChanged(block.CubeGrid);
        }

        public override void UpdateBeforeSimulation()
        {
            ClientSideUpdates();

            if (MyAPIGateway.Multiplayer.IsServer)
                ServerSideUpdates();

            _tickCount++;
        }

        private void ServerSideUpdates()
        {
            if (_tickCount % MAIN_UPDATE_INTERVAL == 0)
            {
                float passedTicks = _tickCount - _lastMainUpdateTick;
                float passedTime = passedTicks * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

                foreach (var manager in _gridHeatManagers.Values)
                {
                    manager.UpdateBlocksTemp(passedTime);
                }
                foreach (var manager in _gridHeatManagers.Values)
                {
                    manager.UpdateNeighborsTemp(passedTime);
                }
                _lastMainUpdateTick = _tickCount;
            }                
        }

        private void ClientSideUpdates()
        {
            _heatApi.Effects.UpdateLightsPosition();

            var list = _gridHeatManagers.Values.ToList();

            foreach (var manager in list)
            {
                manager.UpdateVisuals(MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);
            }

            if (_tickCount % MAIN_UPDATE_INTERVAL == 0)
            {
                var eventControllers = _heatApi.Registry.GetEventControllerEvents();
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

        public void OnGridOwnershipChanged(IMyCubeGrid grid)
        {
            if (Config != null && Config.LIMIT_TO_PLAYER_GRIDS)
            {
                bool shouldHave = IsPlayrGrid(grid);
                bool has = _gridHeatManagers.ContainsKey(grid);
                if (shouldHave && !has)
                {
                    _gridHeatManagers[grid] = new GridHeatManager(grid);
                }
                else if (!shouldHave && has)
                {
                    _gridHeatManagers[grid].Cleanup();
                    _gridHeatManagers.Remove(grid);
                }
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
                    foreach (var gridManager in _gridHeatManagers.Values)
                    {
                        if (gridManager.TryReactOnHeat(block, heat))
                        {
                            return;
                        }
                    }
                }
            }
        }

        internal static void UpdateNetowkrsUI(long gridId, List<HeatValuePair> heats)
        {
            try
            {
                MyLog.Default.WriteLine($"[HeatManagement] Received network heat update for grid {gridId} with {heats.Count} entries.");
                IMyEntity entity = MyAPIGateway.Entities.GetEntityById(gridId);
                if (entity == null)
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Could not find grid with ID {gridId}.");
                    return;
                }
                var grid = entity as IMyCubeGrid;
                if (grid == null)
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Entity with ID {gridId} is not a grid.");
                    return;
                }
                MyLog.Default.WriteLine($"[HeatManagement] Found grid {grid.DisplayName}.");
                GridHeatManager manager;
                if (!_gridHeatManagers.TryGetValue(grid as IMyCubeGrid, out manager))
                {
                    return;
                }
                MyLog.Default.WriteLine($"[HeatManagement] Found grid manager.");
                foreach (var heatPair in heats)
                {
                    var block = MyAPIGateway.Entities.GetEntityById(heatPair.BlockId) as IMyCubeBlock;
                    if (block == null)
                    {
                        continue;
                    }
                    var heat = heatPair.Heat;
                    _heatApi.Utils.SetHeat(block, heat, true);
                    manager.TryReactOnHeat(block, heat);
                }
                MyLog.Default.WriteLine($"[HeatManagement] Updated {heats.Count} blocks.");
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"[HeatManagement] Exception in UpdateNetowkrsUI: {e}");
            }
        }

        public static void RegisterDebugControl()
        {
            if (_initialized)
                return;

            _initialized = true;

            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;
        }

        public static void GetGridHeatManager(IMyCubeGrid grid, out GridHeatManager manager)
        {
            if (_gridHeatManagers.TryGetValue(grid, out manager))
                return;

            manager = new GridHeatManager(grid);
            _gridHeatManagers[grid] = manager;
        }

        public static void RebuildEverything()
        {
            lock (_gridHeatManagers)
            {
                foreach (var manager in _gridHeatManagers.Values)
                {
                    manager.Cleanup();
                }
                _gridHeatManagers.Clear();
            }

            lock (_ownershipSubscribedGrids)
            {
                foreach (var grid in _ownershipSubscribedGrids)
                {
                    grid.OnBlockAdded -= Instance.OnBlockAdded;
                }
                _ownershipSubscribedGrids.Clear();
            }
            HashSet<IMyEntity> allEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(allEntities);
            foreach (var entity in allEntities)
            {
                Instance.OnEntityAdd(entity);
            }
        }

        private static bool IsWheelGrid(IMyCubeGrid grid)
        {
            var slimBlocks = new List<IMySlimBlock>();
            grid.GetBlocks(slimBlocks);

            // wheel grids have exactly one block and it's a wheel part
            return slimBlocks.Count == 1 && slimBlocks[0].FatBlock is IMyWheel;
        }

        private static void OnCustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyBatteryBlock))
                return;

            // Only add if it doesn't already exist
            if (controls.Any(c => c.Id == "ShowHeatNetworks"))
                return;

            var checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBatteryBlock>("ShowHeatNetworks");
            checkbox.Title = MyStringId.GetOrCompute("Show Heat Networks");
            checkbox.Tooltip = MyStringId.GetOrCompute("Visualizes all heat pipe connections on this grid.");
            checkbox.SupportsMultipleBlocks = false;

            checkbox.Getter = b =>
            {
                GridHeatManager gridManager;
                if (_gridHeatManagers.TryGetValue(b.CubeGrid, out gridManager))
                    return gridManager.GetShowDebug();
                return false;
            };

            checkbox.Setter = (b, value) =>
            {
                GridHeatManager gridManager;
                if (_gridHeatManagers.TryGetValue(b.CubeGrid, out gridManager))
                    gridManager.SetShowDebug(value);
            };

            controls.Add(checkbox);
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
                { "GetActiveVentHeatLoss", new Func<long, float, float>((ventId, dt) =>
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