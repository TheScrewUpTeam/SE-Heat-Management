using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Utils;
using System;
using VRage.Network;
using VRage.GameServices;
using SpaceEngineers.Game.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.Game;

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

        public static Networking networking = new Networking(Config.HeatSyncMessageId);

        private static Dictionary<IMyCubeGrid, GridHeatManager> _gridHeatManagers = new Dictionary<IMyCubeGrid, GridHeatManager>();

        private static bool _initialized = false;
        private int _tickCount = 0;
        private int _lastNeighborsUpdateTick = 0;
        private int _lastMainUpdateTick = 0;

        private static Dictionary<long, IHeatBehavior> _trackedNetworkBlocks = new Dictionary<long, IHeatBehavior>();
        private static Dictionary<IMyCubeGrid, IGridHeatManager> _trachecdGrids = new Dictionary<IMyCubeGrid, IGridHeatManager>();

        public static Config Config;

        private HashSet<IMyCubeGrid> _ownershipSubscribedGrids = new HashSet<IMyCubeGrid>();

        public override void LoadData()
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                return;
            }

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

        protected override void UnloadData()
        {
            networking?.Unregister();
        }

        private void OnHeatMessageReceived(ushort channel, byte[] data, ulong senderSteamId, bool fromServer)
        {
            MyLog.Default.WriteLine($"[Network] Message received: ch {channel}, s: {senderSteamId}, serv: {fromServer}");
            if (!fromServer) return; // extra safety: ignore client-sent data

            var msg = MyAPIGateway.Utilities.SerializeFromBinary<HeatSyncMessage>(data);
            IMyEntity ent;
            if (MyAPIGateway.Entities.TryGetEntityById(msg.EntityId, out ent))
            {
                var block = ent as IMyCubeBlock;
                if (block != null)
                {
                    // Do something with block + msg.Heat
                    MyLog.Default.WriteLine($"[Heat] {block.DisplayNameText}: {msg.Heat} °C");
                }
            }
        }

        public override void BeforeStart()
        {
            networking.Register();
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
            MyLog.Default.WriteLine($"[HMS.IsPlayerGrid] Working on {grid.DisplayName}...");
            if (grid == null)
                return false;

            var terminalBlocks = new List<IMyTerminalBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid)?.GetBlocks(terminalBlocks);
            MyLog.Default.WriteLine($"[HMS.IsPlayerGrid] Got {terminalBlocks.Count} blocks...");
            foreach (var block in terminalBlocks)
            {
                MyLog.Default.WriteLine($"[HMS.IsPlayerGrid] Checking {block.CustomName}...");
                if (block.OwnerId == 0)
                    continue;

                MyLog.Default.WriteLine($"[HMS.IsPlayerGrid] Has owner ID {block.OwnerId}");

                var identity = MyAPIGateway.Players.TryGetIdentityId(block.OwnerId);
                if (identity != null)
                {
                    MyLog.Default.WriteLine($"[HMS.IsPlayerGrid] Identity found");
                    return true; // At least one terminal block is owned by a player
                }
            }

            MyLog.Default.WriteLine($"[HMS.IsPlayerGrid] No suitable block found");

            return false;
        }

        private void OnBlockAdded(IMySlimBlock block) {
            MyLog.Default.WriteLine($"[HMS.OnBlockAdd] checking {block.BlockDefinition.DisplayNameText}");
            if (block.FatBlock == null) {
                return;
            }
            if (block.FatBlock is IMyTerminalBlock) {
                MyLog.Default.WriteLine($"[HMS.OnBlockAdd] It is terminal block...");
                OnAnyBlockOwnershipChanged(block.FatBlock as IMyTerminalBlock);
            }
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
                bool shouldHave = IsPlayrGrid(grid);
                bool has = _gridHeatManagers.ContainsKey(grid);
                MyLog.Default.WriteLine($"[HMS.OnGridOwnershipChanged] ShouldHave: {shouldHave}, has: {has}");
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

        internal static IGridHeatManager GetGridHeatManager(IMyCubeGrid grid)
        {
            if (!_trachecdGrids.ContainsKey(grid))
            {
                _trachecdGrids[grid] = new GridHeatManager(grid, true);
            }
            return _trachecdGrids[grid];
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
                    IHeatBehavior info;
                    if (!_trackedNetworkBlocks.TryGetValue(entityId, out info))
                    {
                        if (block is IMyBatteryBlock)
                        {
                            var battery = block as IMyBatteryBlock;
                            var gridManager = GetGridHeatManager(block.CubeGrid);
                            info = new BatteryHeatManager(battery, gridManager);
                            _trackedNetworkBlocks[entityId] = info;
                        }
                        if (block is IMyAirVent)
                        {
                            var vent = block as IMyAirVent;
                            var gridManager = GetGridHeatManager(block.CubeGrid);
                            info = new VentHeatManager(vent, gridManager);
                            _trackedNetworkBlocks[entityId] = info;
                        }
                        if (block is IMyThrust)
                        {
                            var thrust = block as IMyThrust;
                            var gridManager = GetGridHeatManager(block.CubeGrid);
                            info = new ThrusterHeatManager(thrust, gridManager);
                            _trackedNetworkBlocks[entityId] = info;
                        }
                        if (block is IMyHeatVent)
                        {
                            var vent = block as IMyHeatVent;
                            var gridManager = GetGridHeatManager(block.CubeGrid);
                            info = new HeatVentManager(vent, gridManager);
                            _trackedNetworkBlocks[entityId] = info;
                        }
                    }
                    else
                    {
                        info.ReactOnNewHeat(heat);
                    }
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
                if (HeatSession._gridHeatManagers.TryGetValue(block.CubeGrid, out gridManager))
                {
                    return gridManager.GetShowDebug();
                }
                return false;
            };
            checkbox.Setter = (block, value) =>
            {
                GridHeatManager gridManager;
                if (HeatSession._gridHeatManagers.TryGetValue(block.CubeGrid, out gridManager))
                {
                    gridManager.SetShowDebug(value);
                }
            };

            MyAPIGateway.TerminalControls.AddControl<IMyBatteryBlock>(checkbox);
        }
    }
}