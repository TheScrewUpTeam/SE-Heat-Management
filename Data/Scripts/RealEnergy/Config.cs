using Sandbox.ModAPI;
using System;

namespace TSUT.HeatManagement
{
    public class Config
    {
        public float HEAT_COOLDOWN_COEFF { get; set; } = 20f;
        public float DISCHARGE_HEAT_FRACTION { get; set; } = 0.20f;
        public float THERMAL_CONDUCTIVITY { get; set; } = 200f;
        public float VENT_COOLING_RATE { get; set; } = 1000f;
        public float THRUSTER_COOLING_RATE { get; set; } = 25000f;
        public float CRITICAL_TEMP { get; set; } = 150f;
        public float SMOKE_TRESHOLD => CRITICAL_TEMP * 0.9f;
        public float WIND_COOLING_MULT { get; set; } = 0.1f;
        public bool LIMIT_TO_PLAYER_GRIDS { get; set; } = false;

        private static Config _instance;
        private const string CONFIG_FILE = "TSUT_HeatManagement_Config.xml";

        public static Config Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        public static Config Load()
        {
            Config config = new Config();
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage(CONFIG_FILE, typeof(Config)))
            {
                try
                {
                    string contents;
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(CONFIG_FILE, typeof(Config)))
                    {
                        contents = reader.ReadToEnd();
                    }
                    config = MyAPIGateway.Utilities.SerializeFromXML<Config>(contents);
                }
                catch (Exception e)
                {
                    MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"Failed to load config, using defaults. {e.Message}");
                }
            }
            return config;
        }

        public void Save()
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(CONFIG_FILE, typeof(Config)))
                {
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(this));
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"Failed to save config: {e.Message}");
            }
        }
    }
}