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
            if (messageText.Equals("/refreshNetworks", StringComparison.OrdinalIgnoreCase))
            {
                HeatSession.RebuildEverything();
                MyAPIGateway.Utilities.ShowMessage("HeatManagement", "Heat networks refreshed.");
            }
            sendToOthers = false; // Prevent the message from being sent to other players
        }
        
        public void Unload()
        {
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
        }
    }
}