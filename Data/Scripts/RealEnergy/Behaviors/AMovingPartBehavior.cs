using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.HeatManagement
{
    public abstract class AMovingPartHeatManager<T> : AHeatBehavior, IDirectHeatAcceptor where T : IMyCubeBlock
    {
        protected T _part;
        private float _lastHeatChange = 0f;

        public AMovingPartHeatManager(T part, bool SkipCustomInfoRegistration = false)
        {
            _part = part;
            if (_part is IMyTerminalBlock && !SkipCustomInfoRegistration)
            {
                (_part as IMyTerminalBlock).AppendingCustomInfo += GetCustomInfo;
            }
        }

        private void GetCustomInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            builder.AppendLine($"--- Heat Management ---");
            float heat = HeatSession.Api.Utils.GetHeat(_part);
            builder.AppendLine($"Temperature: {heat:F1} °C");
        }

        public override void Cleanup()
        {
            if (_part is IMyTerminalBlock)
                (_part as IMyTerminalBlock).AppendingCustomInfo -= GetCustomInfo;
            _part = default(T);
        }

        public override float GetHeatChange(float deltaTime) {
            if (_lastHeatChange != 0f)
            {
                var temp = _lastHeatChange;
                _lastHeatChange = 0f;
                return temp;
            }
            var counterpartyBehavior = GetCounterpartyHeatManager();
            if (counterpartyBehavior != null)
            {
                var exchangeResult = HeatSession.Api.Utils.GetExchangeWithNeighbor(_part, counterpartyBehavior.Block, deltaTime);
                if (counterpartyBehavior is IDirectHeatAcceptor)
                {
                    (counterpartyBehavior as IDirectHeatAcceptor).ApplyHeatChange(-exchangeResult);
                }
                return exchangeResult;
            }
            return 0f;
        }

        public override void ReactOnNewHeat(float heat)
        {
            HeatSession.Api.Effects.UpdateBlockHeatLight(_part, HeatSession.Api.Utils.GetHeat(_part));
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(_part, deltaTime);
        }

        public void ApplyHeatChange(float heatChange)
        {
            _lastHeatChange += heatChange;
        }

        abstract protected IHeatBehavior GetCounterpartyHeatManager();

        abstract protected float HeatConductivity { get; }
    }
}