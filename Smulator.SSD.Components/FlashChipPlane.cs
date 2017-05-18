using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;

namespace Smulator.SSD.Components
{
    public class DirectEraseList : LinkedList<uint>
    {
    }

    public enum PlaneStatus { Busy, Idle };
    public class FlashChipPlane
    {
        public FlashChipBlock[] Blocks;
        public uint BlockNo = 0;
        public uint FreePagesNo;            //The number of free pages of the plane
        public uint CurrentActiveBlockID = 0;            //Current active block of the plane
        public FlashChipBlock CurrentActiveBlock = null;
        public PlaneStatus Status = PlaneStatus.Idle;
        public DirectEraseList DirectEraseNodes = new DirectEraseList();//list of blockIDS that their InvalidPageNo == PagesNoPerBlock
        public bool CommandAssigned = false;
        public ulong ReadCount;                     //how many read count in the process of workload
        public ulong ProgamCount;
        public ulong EraseCount;
        public FlashChipBlock  BlockUsageListHead = null, BlockUsageListTail = null;//This linked list is used in FIFO and WindowedGreedy GCs.
        public bool HasGCRequest = false;//just used in preprocessing phase
        public uint[] AllocatedStreams = null;
        public ArrayList AllocatedStreamsTemp = new ArrayList();
        public InternalRequest CurrentExecutingRequest = null;
        public InternalRequest SuspendedExecutingRequest = null;
        public LinkedListNode<IntegerPageAddress> RelatedNodeInGCLinkedList = null;
        public int GCPriorityClass;
        public IntegerPageAddress PlaneGlobalAddress = null;

        public FlashChipPlane(uint channelID, uint overallChipID, uint localChipID, uint dieID, uint planeID,
            uint BlocksNoPerPlane, uint PagesNoPerBlock)
		{
            this.PlaneGlobalAddress = new IntegerPageAddress(channelID, localChipID, dieID, planeID, 0, 0, overallChipID);
            this.BlockNo = BlocksNoPerPlane;
            this.FreePagesNo = BlocksNoPerPlane * PagesNoPerBlock;
            this.Blocks = new FlashChipBlock[BlocksNoPerPlane];
            for (uint i = 0; i < BlocksNoPerPlane; i++)
                this.Blocks[i] = new FlashChipBlock(PagesNoPerBlock, i);
            this.ReadCount = 0;
            this.ProgamCount = 0;
            this.EraseCount = 0;
            this.CurrentActiveBlockID = 0;
            this.CurrentActiveBlock = this.Blocks[0];
		}
    }
}
