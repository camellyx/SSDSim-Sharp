using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    class InternalCleanRequestFast : InternalCleanRequest
    {
        public uint[] BlockIDs;
        public uint NumberOfErases;
        public InternalCleanRequestLinkedList ParallelEraseRequests;

        public InternalCleanRequestFast(InternalRequestType type, IntegerPageAddress targetPageAddress, bool isEmergency, bool isFast)
            : base(type, targetPageAddress, isEmergency, isFast)
        {
            InternalWriteRequestList = new InternalWriteRequestLinkedList();
            this.IsEmergency = isEmergency;
            this.IsFast = isFast;
        }
    }
}
