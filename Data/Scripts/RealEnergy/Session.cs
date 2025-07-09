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

        private Dictionary<IMyCubeGrid, GridHeatManager> _gridHeatManagers = new Dictionary<IMyCubeGrid, GridHeatManager>();

        private int _tickCount = 0;
        private int _lastNeighborsUpdateTick = 0;
        private int _lastMainUpdateTick = 0;

        private static Dictionary<long, IHeatBehavior> _trackedNetworkBlocks = new Dictionary<long, IHeatBehavior>();

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
            if (Config != null && Config.LIMIT_TO_PLAYER_GRIDS && !IsPlayrGrid(grid))
                return;

            _gridHeatManagers[grid] = new GridHeatManager(grid);
        }

        private static bool IsPlayrGrid(IMyCubeGrid grid)
        {
            var bigOwners = grid.BigOwners;
            if (bigOwners == null || bigOwners.Count == 0)
                return false;

            foreach (var ownerId in bigOwners)
            {
                var identity = MyAPIGateway.Players.TryGetIdentityId(ownerId);
                if (identity != null)
                {
                    return true;
                }
            }

            return false;
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
                    IHeatBehavior info;
                    if (!_trackedNetworkBlocks.TryGetValue(entityId, out info)){
                        if (block is IMyBatteryBlock) {
                            var battery = block as IMyBatteryBlock;
                            info = new BatteryHeatManager(battery);
                            _trackedNetworkBlocks[entityId] = info;
                        }
                        if (block is IMyAirVent) {
                            var vent = block as IMyAirVent;
                            info = new VentHeatManager(vent);
                            _trackedNetworkBlocks[entityId] = info;
                        }
                        if (block is IMyThrust) {
                            var thrust = block as IMyThrust;
                            info = new ThrusterHeatManager(thrust);
                            _trackedNetworkBlocks[entityId] = info;
                        }
                    } else {
                        info.ReactOnNewHeat(heat);
                    }
                }
            }
        }
    }
}