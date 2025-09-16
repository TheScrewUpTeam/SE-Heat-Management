using Sandbox.Game.EntityComponents; // Example base namespace
using VRage.Game.Components;
using Sandbox.ModAPI;
using VRage.Utils;
using System.Collections.Generic;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.Game.Localization;
using VRage;
using System.Linq;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using System;

namespace TSUT.HeatManagement
{
    [MyComponentBuilder(typeof(ObjectBuilderHeat))]
    [MyComponentType(typeof(BlockTemperatureChanged))]
    [MyEntityDependencyType(typeof(IMyEventControllerBlock))]
    public class BlockTemperatureChanged : MyEventProxyEntityComponent, IMyEventComponentWithGui, IEventControllerEvent
    {
        private HashSet<IMyTerminalBlock> _blocks = new HashSet<IMyTerminalBlock>();

        private float _temperatureThreshold = 600f; // Default threshold value

        private bool _isSelected;

        public bool IsThresholdUsed => false;

        public bool IsConditionSelectionUsed => true;

        public bool IsBlocksListUsed => true;

        public long UniqueSelectionId => Config.BlockHeatEventUniqueId;

        public MyStringId EventDisplayName => MyStringId.GetOrCompute("Block Temperature Changed");

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                _isSelected = value;
                EventController.SetDetailedInfoDirty();
            }
        }

        public string YesNoToolbarYesDescription => "Temperature threshold reached";

        public string YesNoToolbarNoDescription => "Tempreature threshold not reached";

        public override string ComponentTypeDebugString => nameof(BlockTemperatureChanged);

        private int _lastTriggeredAction = 0;

        private IMyEventControllerBlock EventController => Entity as IMyEventControllerBlock;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            HeatSession.Api.Registry.RegisterEventControllerEvent(this);
            EventController.CubeGrid.OnBlockRemoved += OnBlockRemoved;
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
            EventController.CubeGrid.OnBlockRemoved -= OnBlockRemoved;
            HeatSession.Api.Registry.RemoveEventControllerEvent(this);
        }

        public override MyObjectBuilder_ComponentBase Serialize(bool copy = false)
        {
            var builder = new MyObjectBuilder_ModCustomComponent
            {
                ComponentType = nameof(BlockTemperatureChanged),
                CustomModData = _temperatureThreshold.ToString(),
                RemoveExistingComponentOnNewInsert = true,
                SubtypeName = nameof(BlockTemperatureChanged)
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

        private void OnBlockRemoved(IMySlimBlock block)
        {
            if (block == null || block.FatBlock == null)
                return;

            if (_blocks.Contains(block.FatBlock as IMyTerminalBlock))
            {
                RemoveBlocks(new List<IMyTerminalBlock> { block.FatBlock as IMyTerminalBlock });
            }
        }

        public void AddBlocks(List<IMyTerminalBlock> blocks)
        {
            _blocks.UnionWith(blocks);
            UpdateDetailedInfo(EventController.EntityId);
        }

        public void CreateTerminalInterfaceControls<T>() where T : IMyTerminalBlock
        {
            var sliderBox =
                MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, T>("HeatThresholdReachedEvent.TemperatureThreshold");
            sliderBox.Visible = b => b.Components.Get<BlockTemperatureChanged>().IsSelected;
            sliderBox.SetLimits(-1000, 1000);
            sliderBox.Getter = b => b.Components.Get<BlockTemperatureChanged>()._temperatureThreshold;
            sliderBox.Setter = (b, value) =>
            {
                b.Components.Get<BlockTemperatureChanged>()._temperatureThreshold = value;
                b.Components.Get<BlockTemperatureChanged>().NotifyValuesChanged();
                MyLog.Default.WriteLineAndConsole($"[HeatManagement] BlockTemperatureChanged: Threshold set to {value} °C");
                NotifyServer(b.EntityId, value);
            };
            sliderBox.Title = MyStringId.GetOrCompute("Threshold");
            sliderBox.Tooltip = MyStringId.GetOrCompute("Set the temperature threshold for triggering the event.");
            sliderBox.Writer = (b, sb) =>
                {
                    sb.Append(b.Components.Get<BlockTemperatureChanged>()._temperatureThreshold.ToString("F1"));
                    sb.Append(" °C");
                };
            
            MyAPIGateway.TerminalControls.AddControl<T>(sliderBox);
        }

        public bool IsBlockValidForList(IMyTerminalBlock block)
        {
            return HeatSession.GetBehaviorForBlock(block) is IHeatBehavior;
        }

        public void NotifyValuesChanged()
        {
            if (EventController == null)
                return;

            if (!IsSelected)
                return;

            UpdateDetailedInfo(EventController.EntityId);

            float threshold = _temperatureThreshold;
            bool isBelow = EventController.IsLowerOrEqualCondition;
            bool isAndGate = EventController.IsAndModeEnabled;

            bool result = isAndGate;
            foreach (var block in _blocks)
            {
                bool conditionMet = false;
                float currentTemperature = HeatSession.Api.Utils.GetHeat(block);
                if (isBelow)
                {
                    conditionMet = currentTemperature <= threshold;
                }
                else
                {
                    conditionMet = currentTemperature >= threshold;
                }
                if (isAndGate)
                {
                    result = result && conditionMet;
                }
                else
                {
                    result = result || conditionMet;
                }
            }
            if (_lastTriggeredAction != (result ? 0 : 1)) {
                EventController.TriggerAction(result ? 0 : 1);
                _lastTriggeredAction = result ? 0 : 1;
            }
        }

        public void RemoveBlocks(IEnumerable<IMyTerminalBlock> blocks)
        {
            foreach (var block in blocks)
            {
                _blocks.Remove(block);
            }
            UpdateDetailedInfo(EventController.EntityId);
        }

        public void UpdateDetailedInfo(long entityId)
        {
            if (!IsSelected)
                return;

            var info = EventController.GetDetailedInfo();
            info.Clear();
            if (entityId != EventController.EntityId || _blocks.Count == 0)
                return;

            info.AppendFormat(EventController.IsLowerOrEqualCondition ? MyTexts.GetString(MySpaceTexts.EventBellowEqualInfo) : MyTexts.GetString(MySpaceTexts.EventAboveEqualInfo));
            info.AppendLine();
            var treshholdValue = _temperatureThreshold.ToString("F1");
            info.AppendFormat(MyTexts.GetString(MySpaceTexts.EventThresholdInfo), treshholdValue, "°C");
            info.AppendLine();
            foreach (var terminalBlock in _blocks)
            {
                var num = HeatSession.Api.Utils.GetHeat(terminalBlock);
                var blockInput = num.ToString("F1");
                info.AppendFormat(MyTexts.GetString(MySpaceTexts.EventBlockInputInfo), terminalBlock.CustomName, blockInput, "°C");
                info.AppendLine();
            }
            info.AppendFormat(MyTexts.GetString(MySpaceTexts.EventOutputInfo), _lastTriggeredAction + 1);
            EventController.SetDetailedInfoDirty();

            NotifyClients(entityId);
        }

        private void NotifyClients(long entityId)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            var message = new HeatEventSync
            {
                EntityId = entityId
            };

            HeatSession.networking?.RelayToClients(message);
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

        public void UpdateSettings(long entityId, float treshholdValue)
        {
            if (entityId != EventController.EntityId)
                return;

            _temperatureThreshold = treshholdValue;
            NotifyValuesChanged();
        }
    }
}
