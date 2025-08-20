using Sandbox.Game.EntityComponents; // Example base namespace
using VRage.Game.Components;
using Sandbox.ModAPI;
using VRage.Utils;
using System.Collections.Generic;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.Game.Localization;
using VRage;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;

namespace TSUT.HeatManagement
{
    [MyComponentBuilder(typeof(ObjectBuilderGridHeat))]
    [MyComponentType(typeof(GridMaxTemperatureChanged))]
    [MyEntityDependencyType(typeof(IMyEventControllerBlock))]
    public class GridMaxTemperatureChanged : MyEventProxyEntityComponent, IMyEventComponentWithGui, IEventControllerEvent
    {
        private float _temperatureThreshold = 600f; // Default threshold value

        private bool _isSelected;

        public bool IsThresholdUsed => false;

        public bool IsConditionSelectionUsed => true;

        public long UniqueSelectionId => Config.GridHeatEventUniqueId;

        public MyStringId EventDisplayName => MyStringId.GetOrCompute("Grid Max Temperature Changed");

        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; }
        }

        public string YesNoToolbarYesDescription => "Temperature threshold reached";

        public string YesNoToolbarNoDescription => "Tempreature threshold not reached";

        public override string ComponentTypeDebugString => nameof(GridMaxTemperatureChanged);

        private int _lastTriggeredAction = 0;

        private IMyEventControllerBlock EventController => Entity as IMyEventControllerBlock;

        public bool IsBlocksListUsed => false;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            HeatSession.Api.Registry.RegisterEventControllerEvent(this);
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
            HeatSession.Api.Registry.RemoveEventControllerEvent(this);
        }

        public override MyObjectBuilder_ComponentBase Serialize(bool copy = false)
        {
            var builder = new MyObjectBuilder_ModCustomComponent
            {
                ComponentType = nameof(GridMaxTemperatureChanged),
                CustomModData = _temperatureThreshold.ToString(),
                RemoveExistingComponentOnNewInsert = true,
                SubtypeName = nameof(GridMaxTemperatureChanged)
            };
            
            return builder;
        }

        public override void Deserialize(MyObjectBuilder_ComponentBase builder)
        {
            base.Deserialize(builder);
            var customBuilder = (MyObjectBuilder_ModCustomComponent)builder;

            _temperatureThreshold = float.Parse(customBuilder.CustomModData);
        }
        
        public override bool IsSerialized()
        {
            return true;
        }

        public void AddBlocks(List<IMyTerminalBlock> blocks)
        {
        }

        public void CreateTerminalInterfaceControls<T>() where T : IMyTerminalBlock
        {
            var sliderBox =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, T>("HeatThresholdReachedEvent.TemperatureThreshold");
            sliderBox.Visible = b => b.Components.Get<GridMaxTemperatureChanged>().IsSelected;
            sliderBox.SetLimits(-1000, 1000);
            sliderBox.Getter = b => b.Components.Get<GridMaxTemperatureChanged>()._temperatureThreshold;
            sliderBox.Setter = (b, value) =>
            {
                b.Components.Get<GridMaxTemperatureChanged>()._temperatureThreshold = value;
                b.Components.Get<GridMaxTemperatureChanged>().NotifyValuesChanged();
                NotifyServer(b.EntityId, value);
            };
            sliderBox.Title = MyStringId.GetOrCompute("Threshold");
            sliderBox.Tooltip = MyStringId.GetOrCompute("Set the temperature threshold for triggering the event.");
            sliderBox.Writer = (b, sb) =>
                {
                    sb.Append(b.Components.Get<GridMaxTemperatureChanged>()._temperatureThreshold.ToString("F1"));
                    sb.Append(" °C");
                };
            
            MyAPIGateway.TerminalControls.AddControl<T>(sliderBox);
        }

        public bool IsBlockValidForList(IMyTerminalBlock block)
        {
            return false; // This event does not use a block list
        }

        public void NotifyValuesChanged()
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            if (EventController == null)
                return;

            if (!IsSelected)
                return;

            float threshold = _temperatureThreshold;
            bool isBelow = EventController.IsLowerOrEqualCondition;
            IMyCubeGrid grid = EventController.CubeGrid;
            GridHeatManager gridHeatManager;
            HeatSession.GetGridHeatManager(grid, out gridHeatManager);

            bool result;
            float currentTemperature = gridHeatManager.GetMaxTemperature();
            if (isBelow)
            {
                result = currentTemperature <= threshold;
            }
            else
            {
                result = currentTemperature >= threshold;
            }
            EventController.TriggerAction(result ? 0 : 1);
            _lastTriggeredAction = result ? 0 : 1;
            UpdateDetailedInfo(EventController.EntityId);
        }

        public void RemoveBlocks(IEnumerable<IMyTerminalBlock> blocks)
        {
        }

        public void UpdateDetailedInfo(long entityId)
        {
            if (!IsSelected)
                return;

            var info = EventController.GetDetailedInfo();
            info.Clear();
            if (entityId != EventController.EntityId)
                return;

            info.AppendFormat(EventController.IsLowerOrEqualCondition ? MyTexts.GetString(MySpaceTexts.EventBellowEqualInfo) : MyTexts.GetString(MySpaceTexts.EventAboveEqualInfo));
            info.AppendLine();
            var treshholdValue = _temperatureThreshold.ToString("F1");
            info.AppendFormat(MyTexts.GetString(MySpaceTexts.EventThresholdInfo), treshholdValue, "°C");
            info.AppendLine();
            IMyCubeGrid grid = EventController.CubeGrid;
            GridHeatManager gridHeatManager;
            HeatSession.GetGridHeatManager(grid, out gridHeatManager);
            var num = gridHeatManager.GetMaxTemperature();
            var blockInput = num.ToString("F1");
            info.AppendFormat(MyTexts.GetString(MySpaceTexts.EventBlockInputInfo), grid.CustomName, blockInput, "°C");
            info.AppendLine();
            info.AppendFormat(MyTexts.GetString(MySpaceTexts.EventOutputInfo), _lastTriggeredAction + 1);
            EventController.SetDetailedInfoDirty();
        }

        public void UpdateSettings(long entityId, float treshholdValue)
        {
            if (entityId != EventController.EntityId)
                return;

            _temperatureThreshold = treshholdValue;
            NotifyValuesChanged();
        }

        private void NotifyServer(long entityId, float threshold)
        {
            // Sync should be going from CLIENT to SERVER!
            if (MyAPIGateway.Multiplayer.IsServer)
                return;

            var message = new HeatEventSettingsSync
            {
                EntityId = entityId,
                Threshold = threshold
            };

            HeatSession.networking?.SendToServer(message);
        }
    }
}
