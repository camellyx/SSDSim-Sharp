using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    public enum IORequestType
    {
        Read, Write
    }
    public class IORequest
    {
        public string RequestID = "" + lastId++;

        public ulong LSN;

        public ulong InitiationTime;
        public ulong ResponseTime;
        public ulong EstimatedFinishTime;//used in QoS scheduling

        public uint SizeInByte;
        public uint SizeInSubpages;
        public InternalRequestLinkedList InternalRequestList;
        public IORequestType Type;
	    public uint CompleteLSNCount = 0;   //record the count of lsn served by buffer
        public uint[] NeedDistrFlag = null;
        public uint DistributionFlag;

        public static uint lastId;
        public LinkedListNode<IORequest> RelatedNodeInList = null;
        public uint StreamID;
        public bool ToBeIgnored = false;

        public IORequest(uint streamID, ulong initiationTime, ulong LSN, uint requestDataSizeInSubpages, IORequestType type)
        {
            this.StreamID = streamID;
            this.InitiationTime = initiationTime;
            this.LSN = LSN;
            this.SizeInByte = requestDataSizeInSubpages * FTL.SubPageCapacity;
            this.SizeInSubpages = requestDataSizeInSubpages;
            this.Type = type;
            this.InternalRequestList = new InternalRequestLinkedList();
        }
        public IORequest(ulong initiationTime, ulong LSN, uint requestDataSizeInSubpages, IORequestType type) : this(AddressMappingModule.DefaultStreamID, initiationTime, LSN, requestDataSizeInSubpages, type)
        { }
    }
}
