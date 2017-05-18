using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    public enum BusChannelStatus { Busy, Idle };
    public abstract class BusChannelBase
    {
        public GCJobList EmergencyGCRequests;

        public InternalCleanRequestLinkedList CurrentEmergencyCMRequests = new InternalCleanRequestLinkedList();

        public BusChannelStatus Status = BusChannelStatus.Idle;
        public uint ChannelID = uint.MaxValue;
        public FlashChip[] FlashChips;
        public uint CurrentActiveChip;
        public uint BusyChipCount = 0;
        public uint ActiveTransfersCount = 0;//Used for multi-die commands, where FCC has no solution to find channel 

        public BusChannelBase(uint channelID, FlashChip[] flashChips)
        {
            EmergencyGCRequests = new GCJobList();

            Status = BusChannelStatus.Idle;
            ChannelID = channelID;
            CurrentActiveChip = 0;
            FlashChips = flashChips;
            for (int i = 0; i < flashChips.Length; i++)
                flashChips[i].ConnectedChannel = this;
        }
    }
}
