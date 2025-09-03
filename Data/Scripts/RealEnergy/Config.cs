using Sandbox.ModAPI;
using System;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public class Config : IConfig
    {
        public static string Version = "1.2.3";
        public static ushort HeatSyncMessageId = 7949; // Choose any unused ushort value

        public static ushort BlockHeatEventUniqueId = 17949; // Unique ID for block heat events
        public static ushort GridHeatEventUniqueId = 27949; // Unique ID for block heat events

        public static readonly Guid O2TurboKey = new Guid("decafbad-0000-4c00-babe-c0ffee000002");

        public static string HeatDebugString = "HeatDebug";

        public string HEAT_SYSTEM_VERSION = "1.2.3";
        public bool HEAT_SYSTEM_AUTO_UPDATE = true;
        public float HEAT_COOLDOWN_COEFF { get; set; } = 20f;
        public float HEAT_RADIATION_COEFF { get; set; } = 5f;
        public float DISCHARGE_HEAT_FRACTION { get; set; } = 0.20f;
        public bool DISCHARGE_HEAT_CONFIGURABLE { get; set; } = false;
        public float THERMAL_CONDUCTIVITY { get; set; } = 500f;
        public float VENT_COOLING_RATE { get; set; } = 5000f;
        public float VENT_TURBO_COOLING_RATE { get; set; } = 500f;
        public float THRUSTER_COOLING_RATE { get; set; } = 35000f;
        public float CRITICAL_TEMP { get; set; } = 150f;
        public float SMOKE_TRESHOLD => CRITICAL_TEMP * 0.9f;
        public float WIND_COOLING_MULT { get; set; } = 0.1f;
        public bool LIMIT_TO_PLAYER_GRIDS { get; set; } = false;
        public float HEATPIPE_CONDUCTIVITY { get; set; } = 3000f;
        public float EXHAUST_HEAT_REJECTION_RATE { get; set; } = 5000f; // Used for exhaust block heat rejection rate
        public bool HEAT_GLOW_INDICATION { get; set; } = true;

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

                    // Check if version exists in the XML before deserializing
                    bool hasVersion = contents.Contains("<HEAT_SYSTEM_VERSION>");

                    config = MyAPIGateway.Utilities.SerializeFromXML<Config>(contents);

                    var defaultConfig = new Config();

                    var configUpdateNeeded = !hasVersion || config.HEAT_SYSTEM_AUTO_UPDATE && config.HEAT_SYSTEM_VERSION != defaultConfig.HEAT_SYSTEM_VERSION;

                    MyLog.Default.WriteLine($"[HeatManagement] AutoUpdate: {config.HEAT_SYSTEM_AUTO_UPDATE}, VersionMatches: {hasVersion && config.HEAT_SYSTEM_VERSION == defaultConfig.HEAT_SYSTEM_VERSION}, UpdateNeeded: {configUpdateNeeded}");

                    // Check version and auto-update if needed
                    if (configUpdateNeeded)
                    {
                        MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"Config version mismatch. Auto-updating from {(hasVersion ? config.HEAT_SYSTEM_VERSION : "Unknown")} to {defaultConfig.HEAT_SYSTEM_VERSION}");
                        // Keep auto-update setting but reset everything else to defaults
                        bool autoUpdate = config.HEAT_SYSTEM_AUTO_UPDATE;
                        config = new Config();
                        config.HEAT_SYSTEM_AUTO_UPDATE = autoUpdate;
                        return config;
                    }
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