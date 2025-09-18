using System.Diagnostics.Contracts;
using ProtoBuf;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;

namespace TSUT.HeatManagement
{
    // tag numbers in ProtoInclude collide with numbers from ProtoMember in the same class, therefore they must be unique.
    [ProtoInclude(1000, typeof(HeatSyncMessage))]
    [ProtoInclude(1001, typeof(HeatEventSync))]
    [ProtoInclude(1002, typeof(HeatEventSettingsSync))]
    [ProtoInclude(1003, typeof(RequestHeatConfig))]
    [ProtoInclude(1004, typeof(HeatConfigResponse))]
    [ProtoInclude(1005, typeof(HeatNetworkSyncMessage))]

    [ProtoContract]
    public abstract class PacketBase
    {
        // this field's value will be sent if it's not the default value.
        // to define a default value you must use the [DefaultValue(...)] attribute.
        [ProtoMember(1)]
        public readonly ulong SenderId;

        public PacketBase()
        {
            SenderId = MyAPIGateway.Multiplayer.MyId;
        }

        /// <summary>
        /// Called when this packet is received on this machine.
        /// </summary>
        /// <returns>Return true if you want the packet to be sent to other clients (only works server side)</returns>
        public abstract bool Received();
    }

    [ProtoContract]
    public class HeatSyncMessage : PacketBase
    {
        public HeatSyncMessage() { } // Empty constructor required for deserialization

        [ProtoMember(1)]
        public long EntityId;

        [ProtoMember(2)]
        public float Heat;

        public override bool Received()
        {
            HeatSession.UpdateUI(EntityId, Heat);

            return false;
        }
    }

    [ProtoContract]
    public class HeatValuePair
    {
        [ProtoMember(1)] public long BlockId;
        [ProtoMember(2)] public float Heat;
    }

    [ProtoContract]
    public class HeatNetworkSyncMessage : PacketBase
    {
        public HeatNetworkSyncMessage() { } // Empty constructor required for deserialization

        [ProtoMember(1)]
        public long GridId;

        [ProtoMember(2)]
        public List<HeatValuePair> Heats;

        public override bool Received()
        {
            HeatSession.UpdateNetowkrsUI(GridId, Heats);

            return false;
        }
    }

    [ProtoContract]
    public class HeatEventSync : PacketBase
    {
        public HeatEventSync() { } // Empty constructor required for deserialization

        [ProtoMember(1)]
        public long EntityId;


        public override bool Received()
        {
            HeatSession.UpdateEventControllers(EntityId);

            return false;
        }
    }

    [ProtoContract]
    public class HeatEventSettingsSync : PacketBase
    {
        public HeatEventSettingsSync() { } // Empty constructor required for deserialization

        [ProtoMember(1)]
        public long EntityId;

        [ProtoMember(2)]
        public float Threshold;

        public override bool Received()
        {
            HeatSession.UpdateEventControllerSettings(EntityId, Threshold);
            return false;
        }
    }

    [ProtoContract]
    public class RequestHeatConfig : PacketBase
    {
        public RequestHeatConfig() { } // Empty constructor required for deserialization


        public override bool Received()
        {
            HeatSession.OnHeatConfigRequested(this);
            return false;
        }
    }

    [ProtoContract]
    public class HeatConfigResponse : PacketBase
    {
        public HeatConfigResponse() { } // Empty constructor required for deserialization

        [ProtoMember(1)]
        public float HEAT_COOLDOWN_COEFF;

        [ProtoMember(2)]
        public float HEAT_RADIATION_COEFF;

        [ProtoMember(3)]
        public float DISCHARGE_HEAT_FRACTION;

        [ProtoMember(4)]
        public float THERMAL_CONDUCTIVITY;

        [ProtoMember(5)]
        public float VENT_COOLING_RATE;

        [ProtoMember(6)]
        public float THRUSTER_COOLING_RATE;

        [ProtoMember(7)]
        public float CRITICAL_TEMP;

        [ProtoMember(8)]
        public float WIND_COOLING_MULT;

        [ProtoMember(9)]
        public bool LIMIT_TO_PLAYER_GRIDS;

        [ProtoMember(10)]
        public float HEATPIPE_CONDUCTIVITY;

        [ProtoMember(11)]
        public float EXHAUST_HEAT_REJECTION_RATE;

        [ProtoMember(12)]
        public bool HEAT_GLOW_INDICATION;
        [ProtoMember(13)]
        public string HEAT_SYSTEM_VERSION;
        [ProtoMember(14)]
        public bool HEAT_SYSTEM_AUTO_UPDATE;

        public override bool Received()
        {
            HeatSession.UpdateHeatConfig(this);
            return false;
        }
    }
}