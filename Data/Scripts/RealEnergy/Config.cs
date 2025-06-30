namespace TSUT.HeatManagement
{
    public static class Config
    {
        public static float HEAT_COOLDOWN_COEFF = 20f; // tunable
        public static float DISCHARGE_HEAT_FRACTION = 0.20f; // 20% of power becomes heat
        public static float THERMAL_CONDUCTIVITY = 200f; // Arbitrary scaling factor for transfer rate
        public static float VENT_COOLING_RATE = 1000f; // in Watts or J/s, tune as needed
        public static float THRUSTER_COOLING_RATE = 25000f; // in Watts or J/s, tune as needed
        public static float CRITICAL_TEMP = 150f; // Critical temperature for battery
        public static float SMOKE_TRESHOLD = CRITICAL_TEMP * 0.9f; // Temperature at which smoke is emitted
    }
}