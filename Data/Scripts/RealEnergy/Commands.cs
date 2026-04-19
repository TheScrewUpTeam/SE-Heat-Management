using System;
using Sandbox.ModAPI;

namespace TSUT.HeatManagement
{
    public class HeatCommands
    {
        private static HeatCommands _instance;

        public static HeatCommands Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HeatCommands();
                }
                return _instance;
            }
        }

        private HeatCommands()
        {
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText.Equals("/HMS.refreshNetworks", StringComparison.OrdinalIgnoreCase))
            {
                HeatSession.RebuildEverything();
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", "Heat networks refreshed.");
                sendToOthers = false; // Prevent the message from being sent to other players
                return;
            }
            if (messageText.Equals("/HMS.dropTemps", StringComparison.OrdinalIgnoreCase))
            {
                HeatSession.DropAllTemperatures();
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", "All temperatures dropped to initial.");
                sendToOthers = false; // Prevent the message from being sent to other players
                return;
            }
            if (messageText.Equals("/HMS.toggleInspector", StringComparison.OrdinalIgnoreCase))
            {
                HeatSession.isInspectorActive = !HeatSession.isInspectorActive;
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", $"Heat Inspector: {(HeatSession.isInspectorActive ? "Activated" : "Deactivated")}");
                sendToOthers = false; // Prevent the message from being sent to other players
                return;
            }
            sendToOthers = true; // Allow other messages to be sent to other players
        }

        public void Unload()
        {
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
        }
    }
}