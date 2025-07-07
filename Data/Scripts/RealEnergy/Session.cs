using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class HeatSession : MySessionComponentBase
    {
        private static readonly long ApiModId = 1234567890; // Replace with your actual mod ID

        const int NEIGHBOT_UPDATE_INTERVAL = 100; // in ticks
        const int MAIN_UPDATE_INTERVAL = 30; // in ticks

        private static HeatApi _heatApi = new HeatApi();

        public static HeatApi Api
        {
            get { return _heatApi; }
        }

        private Dictionary<IMyCubeGrid, GridHeatManager> _gridHeatManagers = new Dictionary<IMyCubeGrid, GridHeatManager>();

        private int _tickCount = 0;
        private int _lastNeighborsUpdateTick = 0;
        private int _lastMainUpdateTick = 0;

        public static Config Config;

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

            //MyAPIGateway.Utilities.SendModMessage(ApiModId, _heatApi);
        }

        public override void BeforeStart()
        {
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

            _gridHeatManagers[grid].Cleanup();
            _gridHeatManagers.Remove(grid);
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
                if (grid == null) return;

            _gridHeatManagers[grid] = new GridHeatManager(grid);
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

                    //TestOutput();
                }
            _tickCount++;
        }

        // private void TestOutput()
        // {
        //     IMyPlayer me = MyAPIGateway.Session?.Player;
        //     float temp = HeatUtils.GetTemperatureForPlayer(me.GetPosition());
        // }

        public override void SaveData()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                Config.Save();
            }
        }
    }
}