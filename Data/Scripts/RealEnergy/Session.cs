using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Utils;
using Sandbox.ModAPI.Interfaces.Terminal;

namespace TSUT.HeatManagement
{
    
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class HeatSession : MySessionComponentBase
    {
        private static readonly long ApiModId = 3513670949; // Replace with your actual mod ID

        const int NEIGHBOT_UPDATE_INTERVAL = 100; // in ticks
        const int MAIN_UPDATE_INTERVAL = 30; // in ticks

        private static HeatApi _heatApi = new HeatApi();

        public static HeatApi Api
        {
            get { return _heatApi; }
        }

        private static Dictionary<IMyCubeGrid, GridHeatManager> _gridHeatManagers = new Dictionary<IMyCubeGrid, GridHeatManager>();

        private static bool _initialized = false;
        private int _tickCount = 0;
        private int _lastNeighborsUpdateTick = 0;
        private int _lastMainUpdateTick = 0;

        public static Config Config;

        private HashSet<IMyCubeGrid> _ownershipSubscribedGrids = new HashSet<IMyCubeGrid>();

        public override void LoadData()
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            // Load config (will use defaults if file doesn't exist)
            Config = Config.Instance;

            // Try load config
            Config.Load();

            _heatApi.Registry.RegisterHeatBehaviorFactory(new BatteryHeatManagerFactory());
            _heatApi.Registry.RegisterHeatBehaviorFactory(new VentHeatManagerFactory());
            _heatApi.Registry.RegisterHeatBehaviorFactory(new ThrusterHeatManagerFactory());
            _heatApi.Registry.RegisterHeatBehaviorFactory(new HeatPipeManagerFactory());
            _heatApi.Registry.RegisterHeatBehaviorFactory(new HeatVentManagerFactory());
        }

        public override void BeforeStart()
        {
            RegisterDebugControl();
            MyLog.Default.WriteLine($"[HeatManagement] HeatAPI populated");
            MyAPIGateway.Utilities.SendModMessage(ApiModId, _heatApi);

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
            }

            // If config is set, only add grids owned by the local player
            if (Config != null && Config.LIMIT_TO_PLAYER_GRIDS)
            {
                var bigOwners = grid.BigOwners;
                if (bigOwners == null || bigOwners.Count == 0)
                    return;
                long myId = MyAPIGateway.Session?.Player?.IdentityId ?? 0;
                if (!bigOwners.Contains(myId))
                    return;
            }

            _gridHeatManagers[grid] = new GridHeatManager(grid);
        }

        private void OnAnyBlockOwnershipChanged(IMyTerminalBlock block)
        {
            OnGridOwnershipChanged(block.CubeGrid);
        }

        public override void UpdateBeforeSimulation()
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            _heatApi.Effects.UpdateLightsPosition();

            foreach (var manager in _gridHeatManagers.Values)
                {
                    manager.UpdateVisuals(MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);
                }

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
                }
            _tickCount++;
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
                var bigOwners = grid.BigOwners;
                long myId = MyAPIGateway.Session?.Player?.IdentityId ?? 0;
                bool shouldHave = (bigOwners != null && bigOwners.Contains(myId));
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

        public static void RegisterDebugControl()
        {
            if (_initialized) return;
            _initialized = true;

            var checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBatteryBlock>("ShowHeatNetworks");
            checkbox.Title = MyStringId.GetOrCompute("Show Heat Networks");
            checkbox.Tooltip = MyStringId.GetOrCompute("Visualizes all heat pipe connections on this grid.");
            checkbox.SupportsMultipleBlocks = false;

            checkbox.Getter = block =>
            {
                GridHeatManager gridManager;
                if (HeatSession._gridHeatManagers.TryGetValue(block.CubeGrid, out gridManager)){
                    return gridManager.GetShowDebug();
                }
                return false;
            };
            checkbox.Setter = (block, value) =>
            {
                GridHeatManager gridManager;
                if (HeatSession._gridHeatManagers.TryGetValue(block.CubeGrid, out gridManager)){
                    gridManager.SetShowDebug(value);
                }
            };

            MyAPIGateway.TerminalControls.AddControl<IMyBatteryBlock>(checkbox);
        }
    }
}