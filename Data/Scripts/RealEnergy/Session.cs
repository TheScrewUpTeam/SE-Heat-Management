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
        const int NEIGHBOT_UPDATE_INTERVAL = 100; // in ticks
        const int MAIN_UPDATE_INTERVAL = 30; // in ticks

        private static HeatApi _heatApi = new HeatApi();

        public static HeatApi Api
        {
            get { return _heatApi; }
        }

        public static Networking networking = new Networking(Config.HeatSyncMessageId);

        private static Dictionary<IMyCubeGrid, GridHeatManager> _gridHeatManagers = new Dictionary<IMyCubeGrid, GridHeatManager>();

        private static bool _initialized = false;
        private int _tickCount = 0;
        private int _lastNeighborsUpdateTick = 0;
        private int _lastMainUpdateTick = 0;

        private static Dictionary<long, IHeatBehavior> _trackedNetworkBlocks = new Dictionary<long, IHeatBehavior>();

        public static Config Config;

        private HashSet<IMyCubeGrid> _ownershipSubscribedGrids = new HashSet<IMyCubeGrid>();

        public override void LoadData()
        {
            // Load config (will use defaults if file doesn't exist)
            Config = Config.Instance;

            // Try load config
            Config.Load();

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
            networking?.Unregister();
            MyAPIGateway.Utilities.UnregisterMessageHandler(HmsApi.HeatProviderMesageId, OnHeatProviderRegister);
        }

        public override void BeforeStart()
        {
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
            _heatApi.Effects.UpdateLightsPosition();

            foreach (var manager in _gridHeatManagers.Values)
            {
                manager.UpdateVisuals(MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);
            }
            _tickCount++;

            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            if (_tickCount % MAIN_UPDATE_INTERVAL == 0)
            {
                float passedTicks = _tickCount - _lastMainUpdateTick;
                float passedTime = passedTicks * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

                foreach (var manager in _gridHeatManagers.Values)
                {
                    manager.UpdateBlocksTemp(passedTime);
                }

                _lastMainUpdateTick = _tickCount;
            }

            if (_tickCount % NEIGHBOT_UPDATE_INTERVAL == 0)
            {
                float passedTicks = _tickCount - _lastNeighborsUpdateTick;
                float passedTime = passedTicks * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

                foreach (var manager in _gridHeatManagers.Values)
                {
                    manager.UpdateNeighborsTemp(passedTime);
                }
                _lastNeighborsUpdateTick = _tickCount;

                // Notify all event controller events
                foreach (var eventControllerEvent in _heatApi.Registry.GetEventControllerEvents())
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
                { "UpdateLightsPosition", new Action(() => heatApi.Effects.UpdateLightsPosition()) }
            };
        }
    }
}