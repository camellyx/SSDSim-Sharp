using System;
using System.Collections.Generic;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public enum InternalRequestType
    {
         Read, Write, Clean, Undefined
    }

    public enum InternalRequestExecutionType
    {
        Simple, Copyback, CopybackTwoPlane, CopybackInterleaved, CopybackInterleavedTwoPlane, Multiplane, Interleaved, InterleavedMultiplane
    }

    public abstract class InternalRequest
    {
        public ulong LPN;
        public ulong PPN;
        public uint SizeInSubpages;
        public uint SizeInByte; //number of bytes contained in the request: bytes in the real page + bytes of metadata
        public uint BodyTransferCycles;//= SizeInByte ÷ ChannelWidth
        public InternalRequestType Type = InternalRequestType.Undefined;
        public IntegerPageAddress TargetPageAddress = new IntegerPageAddress(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue);
        public FlashChip TargetFlashChip = null;
        public IORequest RelatedIORequest = null;
        public uint State;
        public ulong IssueTime = XEngineFactory.XEngine.Time;

        public XEvent FlashChipExecutionEvent = null;
        public ulong ExpectedFinishTime = 0;
        public ulong RemainingExecutionTime = 0;

        /* Used to calculate service time and transfer time for a normal read/program operation used to respond to the host IORequests.
           In other words, these variables are not important if InternalRequest is used for garbage collection.*/
        public ulong ExecutionTime = 0;
        public ulong TransferTime = 0;

        public bool IsForGC = false; //Is this internal request is related to GC?

        public InternalCleanRequest RelatedCMReq = null;//If a write request is related to a clean request (gc operation), this field is used to point to the mentioned request
        public InternalRequestExecutionType ExecutionType = InternalRequestExecutionType.Simple;
        //public LinkedListNode<InternalRequest> RelatedNodeInList = null;//Just used for high performance linkedlist insertion/deletion

        #region SetupFunctions
        public InternalRequest()
        {
        }
        public InternalRequest(InternalRequestType type, uint sizeInSubpages, uint sizeInByte, uint bodyTransferCycles,
            uint ChannelID, uint localFlashChipID, uint dieID, uint planeID, uint blockID, uint pageID, uint overallFlashChipID)
        {
            this.Type = type;
            this.SizeInSubpages = sizeInSubpages;
            this.SizeInByte = sizeInByte;
            this.BodyTransferCycles = bodyTransferCycles;
            TargetPageAddress.ChannelID = ChannelID;
            TargetPageAddress.LocalFlashChipID = localFlashChipID;
            TargetPageAddress.DieID = dieID;
            TargetPageAddress.PlaneID = planeID;
            TargetPageAddress.BlockID = blockID;
            TargetPageAddress.PageID = pageID;
            TargetPageAddress.OverallFlashChipID = overallFlashChipID;
        }
        public InternalRequest(InternalRequestType type, uint sizeInSubpages, uint sizeInByte, uint bodyTransferCycles, IntegerPageAddress targetPageAddress)
        {
            this.Type = type;
            this.SizeInSubpages = sizeInSubpages;
            this.SizeInByte = sizeInByte;
            this.BodyTransferCycles = bodyTransferCycles;
            TargetPageAddress.ChannelID = targetPageAddress.ChannelID;
            TargetPageAddress.LocalFlashChipID = targetPageAddress.LocalFlashChipID;
            TargetPageAddress.DieID = targetPageAddress.DieID;
            TargetPageAddress.PlaneID = targetPageAddress.PlaneID;
            TargetPageAddress.BlockID = targetPageAddress.BlockID;
            TargetPageAddress.PageID = targetPageAddress.PageID;
            TargetPageAddress.OverallFlashChipID = targetPageAddress.OverallFlashChipID;
        }
        #endregion
    }

    public class InternalRequestLinkedList : LinkedList<InternalRequest>
    {
        public InternalRequestLinkedList()
            : base()
        {
        }
    }

    public class LNPLinkedList : LinkedList<ulong>
    {
        public LNPLinkedList()
            : base()
        {
        }
    }
}
