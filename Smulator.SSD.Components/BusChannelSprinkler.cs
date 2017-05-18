using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    /// <summary>
    /// BusChannel is used to store current status and control information of a bus channel
    /// </summary>
    /// 
    public class BusChannelSprinkler : BusChannelBase
    {

        public InternalWriteRequestLinkedList WaitingInternalWriteReqs;
        public InternalReadRequestLinkedList WaitingInternalReadReqs;
        public InternalWriteRequestLinkedList WaitingCopybackRequests = new InternalWriteRequestLinkedList();//The copyback write transfers that their corresponding read is finished and are waiting for command transfer
        public InternalReadRequestLinkedList WaitingFlashTransfers = new InternalReadRequestLinkedList();
        public InternalReadRequestLinkedList WaitingFlashTransfersForEmergencyGC = new InternalReadRequestLinkedList();//The read request related to emergency GC are scheduled in this list


        /// <summary>
        /// Stores channel management information.
        /// </summary>
        /// <param name="flashChips"></param>
        /// <param name="waitingInternalWriteReqs">If allocation scheme is F, then this variable is
        /// shared among all channels, otherwise, each channel has a private version of this variable.</param>
        /// <param name="rowID"></param>
        public BusChannelSprinkler(uint channelID, FlashChip[] flashChips, InternalReadRequestLinkedList waitingInternalReadReqs,
            InternalWriteRequestLinkedList waitingInternalWriteReqs, InternalWriteRequestLinkedList waitingCopybackRequests) : base(channelID, flashChips)
        {
            WaitingInternalReadReqs = waitingInternalReadReqs;
            WaitingInternalWriteReqs = waitingInternalWriteReqs;
            WaitingCopybackRequests = waitingCopybackRequests;
        }
    }
}
