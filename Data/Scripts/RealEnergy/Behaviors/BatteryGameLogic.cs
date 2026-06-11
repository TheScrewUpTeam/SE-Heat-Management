using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public static class BatteryTerminalControls
    {
        static bool _registered = false;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            HeatSession.Api.Utils.TryRegister<IMyBatteryBlock>();

            var checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBatteryBlock>("ShowHeatNetworks");
            checkbox.Title = MyStringId.GetOrCompute("Show Heat Networks");
            checkbox.Tooltip = MyStringId.GetOrCompute("Visualizes all heat pipe connections on this grid.");
            checkbox.SupportsMultipleBlocks = false;
            checkbox.Getter = b =>
            {
                GridHeatComponent gridManager;
                if (HeatSession.TryGetGridHeatManager(b.CubeGrid, out gridManager))
                    return gridManager.GetShowDebug();
                return false;
            };
            checkbox.Setter = (b, value) =>
            {
                GridHeatComponent gridManager;
                if (HeatSession.TryGetGridHeatManager(b.CubeGrid, out gridManager))
                    gridManager.SetShowDebug(value);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyBatteryBlock>(checkbox);
        }
    }
}
