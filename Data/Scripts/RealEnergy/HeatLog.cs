using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public static class LS
    {
        public const int Grid     = 1 << 0;  //  1 — grid lifecycle
        public const int Behavior = 1 << 1;  //  2 — block behaviors
        public const int Pipe     = 1 << 2;  //  4 — pipe networks
        public const int O2       = 1 << 3;  //  8 — O2 distribution
        public const int Sync     = 1 << 4;  // 16 — network sync
        public const int Net      = 1 << 5;  // 32 — networking errors
    }

    public static class HeatLog
    {
        private static readonly Dictionary<int, string> _names = new Dictionary<int, string>
        {
            { LS.Grid,     "Grid"     },
            { LS.Behavior, "Behavior" },
            { LS.Pipe,     "Pipe"     },
            { LS.O2,       "O2"       },
            { LS.Sync,     "Sync"     },
            { LS.Net,      "Net"      },
        };

        private static string Prefix(int sub)
        {
            string name;
            return _names.TryGetValue(sub, out name) ? $"[HeatManagement.{name}]" : "[HeatManagement]";
        }

        private static bool Active(int sub, IMyCubeGrid grid = null)
        {
            int flags = Config.Instance?.LOG_FLAGS ?? 0;
            if (flags == 0) return false;
            return (flags & sub) != 0 || (grid != null && grid.CustomName.Contains(Config.HeatDebugString));
        }

        public static void Info(string msg, int sub, IMyCubeGrid grid = null)
        { if (Active(sub, grid)) MyLog.Default.WriteLine($"{Prefix(sub)} {msg}"); }

        public static void Warn(string msg, int sub, IMyCubeGrid grid = null)
        { if (Active(sub, grid)) MyLog.Default.Warning($"{Prefix(sub)} {msg}"); }
    }
}
