using System;

using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    /// <summary>
    /// Internal Request for page movement and cleaning: This type of request contains a list of move sources and destinations 
    /// </summary>
    public class InternalCleanRequest : InternalRequest
    {
        public uint NormalPageMovementCount = 0; //Use in garbage collection operation
        public uint CopyBackPageMovementCount = 0;
        public bool IsEmergency = false;
        public bool IsFast = false;
        public InternalWriteRequestLinkedList InternalWriteRequestList;
        public LinkedListNode<InternalCleanRequest> RelatedNodeInList = null;//Just used for high performance linkedlist insertion/deletion

        public InternalCleanRequest()
            : base()
        {
        }
        public InternalCleanRequest(InternalRequestType type, IntegerPageAddress targetPageAddress, bool isEmergency, bool isFast)
            : base(type, 0, 0, 0, targetPageAddress)
        {
            InternalWriteRequestList = new InternalWriteRequestLinkedList();
            this.IsEmergency = isEmergency;
            this.IsFast = isFast;
        }

        public InternalCleanRequest(InternalRequestType type, uint rowID, uint flashChipID, uint dieID, uint planeID, uint blockID, uint pageID, uint overallFlashChipID,
            bool isEmergency, bool isFast)
            : base(type, 0, 0, 0, rowID, flashChipID, dieID, planeID, blockID, pageID, overallFlashChipID)
        {
            this.IsEmergency = isEmergency;
            this.IsFast = isFast;
        }
        public InternalCleanRequest(InternalRequestType type, IntegerPageAddress targetPageAddress, bool isEmergency, bool isFast, InternalRequestExecutionType execType)
            : this(type, targetPageAddress, isEmergency, isFast)
        {
            this.ExecutionType = execType;
        }
    }

    public class InternalCleanRequestLinkedList : LinkedList<InternalCleanRequest>
    {
        public InternalCleanRequestLinkedList()
            : base()
        {
        }
    }
}
