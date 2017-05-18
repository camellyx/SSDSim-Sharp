using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    public class RateControlOutputQueue
    {
        public InternalReadRequestLinkedList[] ReadQueues;//Different read queues for different priority classes
        public InternalWriteRequestLinkedList[] WriteQueues;//Different write queues for different priority classes
        public InternalReadRequestLinkedList[][][] GCReadQueueForPlanes;
        public InternalWriteRequestLinkedList[][][] GCWriteQueueForPlanes;
        public InternalCleanRequestLinkedList[][][] GCEraseQueueForPlanes;
        public LinkedList<IntegerPageAddress>[] GCPriorityClasses;
        public RateControlOutputQueue(int priorityClassCount, uint chipCount, uint dieNoPerChip, uint planeNoPerDie)
        {
            ReadQueues = new InternalReadRequestLinkedList[priorityClassCount];
            WriteQueues = new InternalWriteRequestLinkedList[priorityClassCount];
            for (int i = 0; i < priorityClassCount; i++)
            {
                ReadQueues[i] = new InternalReadRequestLinkedList();
                WriteQueues[i] = new InternalWriteRequestLinkedList();
            }
            GCReadQueueForPlanes = new InternalReadRequestLinkedList[chipCount][][];
            GCWriteQueueForPlanes = new InternalWriteRequestLinkedList[chipCount][][];
            GCEraseQueueForPlanes = new InternalCleanRequestLinkedList[chipCount][][];
            for (int chipCntr = 0; chipCntr < chipCount; chipCntr++)
            {
                GCReadQueueForPlanes[chipCntr] = new InternalReadRequestLinkedList[dieNoPerChip][];
                GCWriteQueueForPlanes[chipCntr] = new InternalWriteRequestLinkedList[dieNoPerChip][];
                GCEraseQueueForPlanes[chipCntr] = new InternalCleanRequestLinkedList[dieNoPerChip][];
                for (int dieCntr = 0; dieCntr < dieNoPerChip; dieCntr++)
                {
                    GCReadQueueForPlanes[chipCntr][dieCntr] = new InternalReadRequestLinkedList[planeNoPerDie];
                    GCWriteQueueForPlanes[chipCntr][dieCntr] = new InternalWriteRequestLinkedList[planeNoPerDie];
                    GCEraseQueueForPlanes[chipCntr][dieCntr] = new InternalCleanRequestLinkedList[planeNoPerDie];
                    for (int planeCntr = 0; planeCntr < planeNoPerDie; planeCntr++)
                    {
                        GCReadQueueForPlanes[chipCntr][dieCntr][planeCntr] = new InternalReadRequestLinkedList();
                        GCWriteQueueForPlanes[chipCntr][dieCntr][planeCntr] = new InternalWriteRequestLinkedList();
                        GCEraseQueueForPlanes[chipCntr][dieCntr][planeCntr] = new InternalCleanRequestLinkedList();
                    }
                }
            }
            GCPriorityClasses = new LinkedList<IntegerPageAddress>[InputStreamBase.PriorityClassCount];
            for (int i = 0; i < InputStreamBase.PriorityClassCount; i++)
                GCPriorityClasses[i] = new LinkedList<IntegerPageAddress>();
        }
    }
    public class ExecutionBatch
    {
        public InternalReadRequestLinkedList ReadBatch;
        public InternalWriteRequestLinkedList WriteBatch;
        public InternalCleanRequestLinkedList EraseBatch;
        public ulong ReadActivityHistoryOld = 0, ReadActivityHistoryRecent = 0;
        public ulong WriteActivityHistoryOld = 0, WriteActivityHistoryRecent = 0;
        public ExecutionBatch()
        {
            ReadBatch = new InternalReadRequestLinkedList();
            WriteBatch = new InternalWriteRequestLinkedList();
            EraseBatch = new InternalCleanRequestLinkedList();
        }
    }
    public class BusChannelRPB : BusChannelBase
    {
        public RateControlOutputQueue RateControlledQueues;
        public ExecutionBatch ExecutionBachtes;
        public InternalReadRequestLinkedList WaitingFlashTransfers = new InternalReadRequestLinkedList();
        public InternalReadRequestLinkedList WaitingFlashTransfersForGC = new InternalReadRequestLinkedList();//The read request related to GC are scheduled in this list
        public InternalWriteRequestLinkedList WaitingCopybackRequests = new InternalWriteRequestLinkedList();//The copyback write transfers that their corresponding read is finished and are waiting for command transfer

        /// <summary>
        /// Stores channel management information.
        /// </summary>
        /// <param name="flashChips"></param>
        /// <param name="waitingInternalWriteReqs">If allocation scheme is F, then this variable is
        /// shared among all channels, otherwise, each channel has a private version of this variable.</param>
        /// <param name="rowID"></param>
        public BusChannelRPB(uint channelID, FlashChip[] flashChips, uint chipNoPerChannel, uint dieNoPerChip, uint planeNoPerDie) : base(channelID, flashChips)
        {
            RateControlledQueues = new RateControlOutputQueue(InputStreamBase.PriorityClassCount, chipNoPerChannel, dieNoPerChip, planeNoPerDie);
            ExecutionBachtes = new ExecutionBatch();
        }
        public void ResetHistory()
        {
            ExecutionBachtes.ReadActivityHistoryOld = ExecutionBachtes.ReadActivityHistoryRecent;
            ExecutionBachtes.ReadActivityHistoryRecent = 0;
            ExecutionBachtes.WriteActivityHistoryOld = ExecutionBachtes.WriteActivityHistoryRecent;
            ExecutionBachtes.WriteActivityHistoryRecent = 0;
        }
        public ulong WriteHistory
        {
            get { return ExecutionBachtes.WriteActivityHistoryRecent * 2 + ExecutionBachtes.WriteActivityHistoryOld; }
        }
        public ulong ReadHistory
        {
            get { return ExecutionBachtes.ReadActivityHistoryRecent * 2 + ExecutionBachtes.ReadActivityHistoryOld; }
        }
    }

}
