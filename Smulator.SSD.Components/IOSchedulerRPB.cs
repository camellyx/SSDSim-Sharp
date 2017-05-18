using System;
using System.Collections.Generic;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public class IOSchedulerRPB : IOSchedulerBase
    {
        BusChannelRPB[] channels;
        LinkedList<FlowInformation>[] flowsList;
        NVMeIODispatcherRPB NVMeIODispatcher;
        uint batchSize = 32;
        bool _rateControllerEnabled = true;
        bool _priorityControllerEnabled = true;
        bool _readWriteBalancerEnabled = true;
        ulong _readToWritePrioritizationFactor = 1;
        ulong _writeToErasePrioritizationFactor = 1;
        ulong eraseReasonableSuspensionTimeForRead = 0, eraseReasonableSuspensionTimeForWrite = 0;//the time period 
        ulong writeReasonableSuspensionTimeForRead = 0;
        readonly uint[] GCPriorityClassThresholds;
        bool dynamicBatching = false;
        bool simulationStart = true;//used to detect the simulation start phase, so it can process GC requests generated during trace file preprocessing
        private enum PriorityControlSchedulingModel { AllFair, SoftQoS, MultiplePriorities, TurnedOff};
        private PriorityControlSchedulingModel schedulingModel = PriorityControlSchedulingModel.AllFair;

        public IOSchedulerRPB(string id, FTL ftl, IOSchedulingPolicy policy, NVMeIODispatcherRPB nvmeIODispatcher, uint batchSize, int flowCount,
            ulong readToWritePrioritizationFactor, ulong writeToErasePrioritizationFactor,
            bool rateControllerEnabled, bool priorityControllerEnabled, bool readWriteBalancerEnabled,
            ulong suspendWriteSetup, ulong suspendEraseSetup, ulong ReadTransferCycleTime, ulong WriteTransferCycleTime) : base(id, ftl)
        {
            channels = ftl.ChannelInfos as BusChannelRPB[];
            flowsList = new LinkedList<FlowInformation>[InputStreamBase.PriorityClassCount];
            for (int i = 0; i < InputStreamBase.PriorityClassCount; i++)
                flowsList[i] = new LinkedList<FlowInformation>();
            NVMeIODispatcher = nvmeIODispatcher;
            foreach (FlowInformation flow in nvmeIODispatcher.Flows)
                flowsList[(int)flow.PriorityClass].AddLast(flow);

            this.batchSize = batchSize;
            _readToWritePrioritizationFactor = readToWritePrioritizationFactor;
            writeReasonableSuspensionTimeForRead = (suspendWriteSetup + 10000 + (_FTL.PageCapacity * ReadTransferCycleTime));
            eraseReasonableSuspensionTimeForRead = (suspendEraseSetup + 10000 + (_FTL.PageCapacity * WriteTransferCycleTime));
            eraseReasonableSuspensionTimeForWrite = (suspendEraseSetup + 10000 + (_FTL.PageCapacity * WriteTransferCycleTime));
            _writeToErasePrioritizationFactor = writeToErasePrioritizationFactor;
            _rateControllerEnabled = rateControllerEnabled;
            _priorityControllerEnabled = priorityControllerEnabled;
            _readWriteBalancerEnabled = readWriteBalancerEnabled;
            GCPriorityClassThresholds = new uint[InputStreamBase.PriorityClassCount];
            for (int i = 0; i < InputStreamBase.PriorityClassCount; i++)
                GCPriorityClassThresholds[i] = Convert.ToUInt32((_FTL.PagesNoPerBlock * (InputStreamBase.PriorityClassCount - i - 1)) / InputStreamBase.PriorityClassCount);

            if (priorityControllerEnabled)
                switch (policy)
                {
                    case IOSchedulingPolicy.MultiStageAllFair:
                        schedulingModel = PriorityControlSchedulingModel.AllFair;
                        createExecutionBatch += new ExecutionBatchCreator(CreateExecutionBatchAllFair);
                        break;
                    case IOSchedulingPolicy.MultiStageSoftQoS:
                        schedulingModel = PriorityControlSchedulingModel.SoftQoS;
                        createExecutionBatch += new ExecutionBatchCreator(CreateExecutionBatchSoftQoS);
                        break;
                    case IOSchedulingPolicy.MultiStageMultiplePriorities:
                        schedulingModel = PriorityControlSchedulingModel.MultiplePriorities;
                        createExecutionBatch += new ExecutionBatchCreator(CreateExecutionBatchMultiplePriorities);
                        break;
                    default:
                        throw new Exception("Unexpected scheduling policy type!");
                }
            else
            {
                schedulingModel = PriorityControlSchedulingModel.TurnedOff;
                createExecutionBatch += new ExecutionBatchCreator(CreateExecutionBatchDefault);
            }
        }

        //This is the first stage of our scheduler i.e. RateController
        public override void Schedule(uint priorityClass, uint streamID)
        {
            if (_rateControllerEnabled)
            {
                #region RateControlledEnqueue
                //select the least recently used flow
                int[] checkList = new int[flowsList[priorityClass].Count];
                int maxValue = 0;

                while (NVMeIODispatcher.Flows[streamID].ReadQueue.Count > 0)
                {
                    var waitingReadOp = NVMeIODispatcher.Flows[streamID].ReadQueue.First;
                    for (int i = 0; i < checkList.Length; i++)
                        checkList[i] = 0;
                    maxValue = 0;
                    InternalReadRequestLinkedList targetQueue = channels[waitingReadOp.Value.TargetPageAddress.ChannelID].RateControlledQueues.ReadQueues[priorityClass];
                    if (targetQueue.Count == 0)
                        targetQueue.AddLast(waitingReadOp.Value);
                    else
                    {
                        LinkedListNode<InternalReadRequest> insertPosition = targetQueue.First;
                        while (insertPosition != null)
                        {
                            checkList[insertPosition.Value.RelatedIORequest.StreamID]++;
                            if (checkList[insertPosition.Value.RelatedIORequest.StreamID] > maxValue)
                                maxValue = checkList[insertPosition.Value.RelatedIORequest.StreamID];
                            if (maxValue - checkList[waitingReadOp.Value.RelatedIORequest.StreamID] > 1)
                                break;
                            insertPosition = insertPosition.Next;
                        }

                        if (insertPosition == null)
                            targetQueue.AddLast(waitingReadOp.Value);
                        else
                            targetQueue.AddBefore(insertPosition, waitingReadOp.Value);
                    }
                    NVMeIODispatcher.Flows[streamID].ReadQueue.RemoveFirst();
                }


                while (NVMeIODispatcher.Flows[streamID].WriteQueue.Count > 0)
                {
                    var waitingWriteOp = NVMeIODispatcher.Flows[streamID].WriteQueue.First;
                    for (int i = 0; i < checkList.Length; i++)
                        checkList[i] = 0;
                    maxValue = 0;
                    InternalWriteRequestLinkedList targetQueue = channels[waitingWriteOp.Value.TargetPageAddress.ChannelID].RateControlledQueues.WriteQueues[priorityClass];
                    if (targetQueue.Count == 0)
                        targetQueue.AddLast(waitingWriteOp.Value);
                    else
                    {
                        LinkedListNode<InternalWriteRequest> insertPosition = targetQueue.First;
                        while (insertPosition != null)
                        {
                            checkList[insertPosition.Value.RelatedIORequest.StreamID]++;
                            if (checkList[insertPosition.Value.RelatedIORequest.StreamID] > maxValue)
                                maxValue = checkList[insertPosition.Value.RelatedIORequest.StreamID];
                            if (maxValue - checkList[waitingWriteOp.Value.RelatedIORequest.StreamID] > 1)
                                break;
                            insertPosition = insertPosition.Next;
                        }

                        if (insertPosition == null)
                            targetQueue.AddLast(waitingWriteOp.Value);
                        else
                            targetQueue.AddBefore(insertPosition, waitingWriteOp.Value);
                    }
                    NVMeIODispatcher.Flows[streamID].WriteQueue.RemoveFirst();
                }

                #endregion
            }
            else
            {
                #region Round-RobinEnqueue
                bool allQueuesEmpty = false;
                LinkedListNode<FlowInformation> currentFlowQueue = null;
                //round-robin schedule of reads
                while (!allQueuesEmpty)
                {
                    allQueuesEmpty = true;
                    for (currentFlowQueue = flowsList[priorityClass].First; currentFlowQueue != null; currentFlowQueue = currentFlowQueue.Next)
                        if (currentFlowQueue.Value.ReadQueue.First != null)
                        {
                            allQueuesEmpty = false;
                            channels[currentFlowQueue.Value.ReadQueue.First.Value.TargetPageAddress.ChannelID].RateControlledQueues.ReadQueues[priorityClass].AddLast(currentFlowQueue.Value.ReadQueue.First.Value);
                            currentFlowQueue.Value.ReadQueue.RemoveFirst();
                        }
                }
                //round-robin schedule of writes
                allQueuesEmpty = false;
                while (!allQueuesEmpty)
                {
                    allQueuesEmpty = true;
                    for (currentFlowQueue = flowsList[priorityClass].First; currentFlowQueue != null; currentFlowQueue = currentFlowQueue.Next)
                        if (currentFlowQueue.Value.WriteQueue.First != null)
                        {
                            allQueuesEmpty = false;
                            channels[currentFlowQueue.Value.WriteQueue.First.Value.TargetPageAddress.ChannelID].RateControlledQueues.WriteQueues[priorityClass].AddLast(currentFlowQueue.Value.WriteQueue.First.Value);
                            currentFlowQueue.Value.WriteQueue.RemoveFirst();
                        }
                }
                #endregion
            }
            PriorityAndGCSchedule();
        }
        /// <summary>
        /// Invoked after new request arrival or finishing a flash operation
        /// </summary>
        /// <param name="channelID">Target channel ID</param>
        /// <param name="localChipID">Target flash chip ID in the channel</param>

        #region PriorityScheduler
        public void UpdateGCQueuePriority(IntegerPageAddress targetPlaneAddress)
        {
            if (channels[targetPlaneAddress.ChannelID].RateControlledQueues.GCEraseQueueForPlanes[targetPlaneAddress.LocalFlashChipID][targetPlaneAddress.DieID][targetPlaneAddress.PlaneID].Count == 0)
                return;
            uint decisionFactor = 0;

            FlashChipPlane targetPlane = channels[targetPlaneAddress.ChannelID].FlashChips[targetPlaneAddress.LocalFlashChipID].Dies[targetPlaneAddress.DieID].Planes[targetPlaneAddress.PlaneID];
            uint writeQueueLength = _FTL.ResourceAccessTable.PlaneEntries[targetPlaneAddress.ChannelID][targetPlaneAddress.LocalFlashChipID][targetPlaneAddress.DieID][targetPlaneAddress.PlaneID].WaitingWriteCount;
            uint freepagesCount = targetPlane.FreePagesNo;

            if (freepagesCount <= writeQueueLength)
                decisionFactor = 0;
            else
                decisionFactor = freepagesCount - writeQueueLength;
            for (int priorityCntr = 0; priorityCntr < InputStreamBase.PriorityClassCount; priorityCntr++)
                if (decisionFactor >= GCPriorityClassThresholds[priorityCntr])
                {
                    int priorityClass = InputStreamBase.PriorityClassCount - priorityCntr - 1;
                    if (priorityClass != targetPlane.GCPriorityClass)
                    {
                        if (targetPlane.RelatedNodeInGCLinkedList != null)
                            channels[targetPlaneAddress.ChannelID].RateControlledQueues.GCPriorityClasses[targetPlane.GCPriorityClass].Remove(targetPlane.RelatedNodeInGCLinkedList);
                        targetPlane.GCPriorityClass = priorityClass;
                        targetPlane.RelatedNodeInGCLinkedList = channels[targetPlaneAddress.ChannelID].RateControlledQueues.GCPriorityClasses[priorityClass].AddLast(targetPlane.PlaneGlobalAddress);
                    }
                    break;
                }
        }
        private void PriorityAndGCSchedule()
        {
            if (!PrioritySchedulerLocked)
            {
                PrioritySchedulerLocked = true;
                for (uint channelCntr = 0; channelCntr < _FTL.ChannelCount; channelCntr++)
                {
                    if (simulationStart)
                        _FTL.GarbageCollector.ChannelInvokeGCRPB(channelCntr);
                    if (dynamicBatching || channels[channelCntr].ExecutionBachtes.ReadBatch.Count == 0
                        || channels[channelCntr].ExecutionBachtes.WriteBatch.Count == 0
                        || channels[channelCntr].ExecutionBachtes.EraseBatch.Count == 0)
                        createExecutionBatch(channelCntr);
                    if (channels[channelCntr].Status == BusChannelStatus.Idle)
                    {
                        TransactionExecuterReturnStatus readExecStatus = GenerateReadTransaction(channelCntr, false);
                        if (readExecStatus != TransactionExecuterReturnStatus.SuccessfullyExecuted)
                        {
                            TransactionExecuterReturnStatus writeExecStatus = GenerateWriteTransaction(channelCntr, false);
                            if (writeExecStatus != TransactionExecuterReturnStatus.SuccessfullyExecuted)
                                if (GenerateEraseTransaction(channelCntr) != TransactionExecuterReturnStatus.SuccessfullyExecuted)
                                    if (readExecStatus == TransactionExecuterReturnStatus.ExittedForOtherQueues)
                                        if (GenerateReadTransaction(channelCntr, true) != TransactionExecuterReturnStatus.SuccessfullyExecuted
                                            && writeExecStatus == TransactionExecuterReturnStatus.ExittedForOtherQueues)
                                            GenerateWriteTransaction(channelCntr, true);
                        }
                    }
                }
                simulationStart = false;
                PrioritySchedulerLocked = false;
            }
        }
        public void CreateExecutionBatchMultiplePriorities(uint channelID)
        {
            InternalReadRequestLinkedList readBatch = channels[channelID].ExecutionBachtes.ReadBatch;
            InternalWriteRequestLinkedList writeBatch = channels[channelID].ExecutionBachtes.WriteBatch;
            InternalCleanRequestLinkedList eraseBatch = channels[channelID].ExecutionBachtes.EraseBatch;
            RateControlOutputQueue targetRateControlledQueue = channels[channelID].RateControlledQueues;

            #region CreateReadBatch
            if (dynamicBatching || readBatch.Count == 0)
            {
                for (int priorityClass = 0; priorityClass < InputStreamBase.PriorityClassCount; priorityClass++)
                    if (targetRateControlledQueue.ReadQueues[priorityClass].Count > 0 || targetRateControlledQueue.GCPriorityClasses[priorityClass].Count > 0)
                    {
                        var readReq = targetRateControlledQueue.ReadQueues[priorityClass].First;
                        LinkedListNode<InternalReadRequest> removedReq = null;
                        while (readBatch.Count != batchSize && readReq != null)
                        {
                            if (_FTL.ResourceAccessTable.PlaneEntries[channelID][readReq.Value.TargetPageAddress.LocalFlashChipID][readReq.Value.TargetPageAddress.DieID][readReq.Value.TargetPageAddress.PlaneID].OnTheFlyRead == null)
                            {
                                _FTL.ResourceAccessTable.PlaneEntries[channelID][readReq.Value.TargetPageAddress.LocalFlashChipID][readReq.Value.TargetPageAddress.DieID][readReq.Value.TargetPageAddress.PlaneID].OnTheFlyRead = readReq.Value;
                                readReq.Value.RelatedNodeInList = readBatch.AddLast(readReq.Value);
                                removedReq = readReq;
                                readReq = readReq.Next;
                                targetRateControlledQueue.ReadQueues[priorityClass].Remove(removedReq);
                            }
                            else readReq = readReq.Next;
                        }

                        var planeAddress = targetRateControlledQueue.GCPriorityClasses[priorityClass].First;
                        while (readBatch.Count != batchSize && planeAddress != null)
                        {
                            if (_FTL.ResourceAccessTable.PlaneEntries[planeAddress.Value.ChannelID][planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].OnTheFlyRead == null
                                && targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].Count > 0)
                            {
                                _FTL.ResourceAccessTable.PlaneEntries[planeAddress.Value.ChannelID][planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].OnTheFlyRead =
                                    targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value;
                                targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value.RelatedNodeInList 
                                    = readBatch.AddLast(targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value);
                                targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].RemoveFirst();
                            }
                            planeAddress = planeAddress.Next;
                        }
                    }
            }
            #endregion
            #region CreateWriteBatch
            if (dynamicBatching || writeBatch.Count == 0)
            {
                for (int priorityClass = 0; priorityClass < InputStreamBase.PriorityClassCount; priorityClass++)
                    if (targetRateControlledQueue.WriteQueues[priorityClass].Count > 0 || targetRateControlledQueue.GCPriorityClasses[priorityClass].Count > 0)
                    {
                        var planeAddress = targetRateControlledQueue.GCPriorityClasses[priorityClass].First;
                        while (writeBatch.Count != batchSize && planeAddress != null)
                        {
                            if (_FTL.ResourceAccessTable.PlaneEntries[planeAddress.Value.ChannelID][planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].OnTheFlyWrite == null
                                && targetRateControlledQueue.GCWriteQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].Count > 0)
                                if (targetRateControlledQueue.GCWriteQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value.UpdateRead == null)
                                {
                                    _FTL.ResourceAccessTable.PlaneEntries[planeAddress.Value.ChannelID][planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].OnTheFlyWrite =
                                        targetRateControlledQueue.GCWriteQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value;
                                    targetRateControlledQueue.GCWriteQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value.RelatedNodeInList
                                        = writeBatch.AddLast(targetRateControlledQueue.GCWriteQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value);
                                    targetRateControlledQueue.GCWriteQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].RemoveFirst();
                                }
                            planeAddress = planeAddress.Next;
                        }

                        var writeReq = targetRateControlledQueue.WriteQueues[priorityClass].First;
                        LinkedListNode<InternalWriteRequest> removedReq = null;
                        while (writeBatch.Count != batchSize && writeReq != null)
                        {
                            if (_FTL.ResourceAccessTable.PlaneEntries[channelID][writeReq.Value.TargetPageAddress.LocalFlashChipID][writeReq.Value.TargetPageAddress.DieID][writeReq.Value.TargetPageAddress.PlaneID].OnTheFlyWrite == null
                                && writeReq.Value.UpdateRead == null)
                            {
                                _FTL.ResourceAccessTable.PlaneEntries[channelID][writeReq.Value.TargetPageAddress.LocalFlashChipID][writeReq.Value.TargetPageAddress.DieID][writeReq.Value.TargetPageAddress.PlaneID].OnTheFlyWrite = writeReq.Value;
                                writeReq.Value.RelatedNodeInList = writeBatch.AddLast(writeReq.Value);
                                removedReq = writeReq;
                                writeReq = writeReq.Next;
                                targetRateControlledQueue.WriteQueues[priorityClass].Remove(removedReq);
                            }
                            else writeReq = writeReq.Next;
                        }
                    }
            }
            #endregion
            #region CreateEraseBatch
            if (_FTL.ResourceAccessTable.ChannelEntries[channelID].WaitingGCReqsCount > 0)
            {
                for (int priorityClass = 0; priorityClass < InputStreamBase.PriorityClassCount; priorityClass++)
                    if (targetRateControlledQueue.GCPriorityClasses[priorityClass].Count > 0)
                    {
                        for (var planeAddress = targetRateControlledQueue.GCPriorityClasses[priorityClass].First; planeAddress != null; planeAddress = planeAddress.Next)
                        {
                            var eraseReq = targetRateControlledQueue.GCEraseQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First;
                            while(eraseReq != null)
                                if (_FTL.ResourceAccessTable.PlaneEntries[planeAddress.Value.ChannelID][planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].OnTheFlyErase == null
                                    && eraseReq.Value.InternalWriteRequestList.Count == 0)
                                {
                                    _FTL.ResourceAccessTable.PlaneEntries[planeAddress.Value.ChannelID][planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].OnTheFlyErase = eraseReq.Value;
                                    eraseReq.Value.RelatedNodeInList = eraseBatch.AddLast(eraseReq.Value);
                                    targetRateControlledQueue.GCEraseQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].Remove(eraseReq);
                                    break;
                                }
                                else eraseReq = eraseReq.Next;
                        }
                    }
            }
            #endregion
            //reorder requests in batch
        }
        public void CreateExecutionBatchAllFair(uint channelID)
        { }
        public void CreateExecutionBatchSoftQoS(uint channelID)
        { }
        public void CreateExecutionBatchDefault(uint channelID)
        {
            RateControlOutputQueue targetRateControlledQueue = channels[channelID].RateControlledQueues;

            #region NoPriority
           /* bool allQueuesEmpty = false;
            while (!allQueuesEmpty)
            {
                allQueuesEmpty = true;
                for (int priorityLevel = 0; priorityLevel < InputStreamBase.PriorityClassCount; priorityLevel++)
                {
                    var planeAddress = targetRateControlledQueue.GCPriorityClasses[priorityClass].First;
                    while (planeAddress != null)
                    {
                        if (channels[channelID].RateControlledQueues.GCReadQueueForChips[chipNo].Count > 0)
                        {
                            allQueuesEmpty = false;
                            channels[channelID].ExecutionBachtes.ReadBatch.AddLast(channels[channelID].RateControlledQueues.GCReadQueueForChips[chipNo].First);
                            channels[channelID].RateControlledQueues.GCReadQueueForChips[chipNo].RemoveFirst();
                        }
                        if (_FTL.ResourceAccessTable.PlaneEntries[planeAddress.Value.ChannelID][planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].OnTheFlyRead == null
                            && targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].Count > 0)
                        {
                            _FTL.ResourceAccessTable.PlaneEntries[planeAddress.Value.ChannelID][planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].OnTheFlyRead =
                                targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value;
                            targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value.RelatedNodeInList
                                = readBatch.AddLast(targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].First.Value);
                            targetRateControlledQueue.GCReadQueueForPlanes[planeAddress.Value.LocalFlashChipID][planeAddress.Value.DieID][planeAddress.Value.PlaneID].RemoveFirst();
                        }
                        planeAddress = planeAddress.Next;
                    }
                    if (channels[channelID].RateControlledQueues.ReadQueues[priorityLevel].Count > 0)
                    {
                        allQueuesEmpty = false;
                        channels[channelID].ExecutionBachtes.ReadBatch.AddLast(channels[channelID].RateControlledQueues.ReadQueues[priorityLevel].First);
                        channels[channelID].RateControlledQueues.ReadQueues[priorityLevel].RemoveFirst();
                    }
                }
            }
            allQueuesEmpty = false;
            while (!allQueuesEmpty)
            {
                allQueuesEmpty = true;
                for (int priorityLevel = 0; priorityLevel < InputStreamBase.PriorityClassCount; priorityLevel++)
                {
                    if (channels[channelID].RateControlledQueues.WriteQueues[priorityLevel].Count > 0)
                    {
                        allQueuesEmpty = false;
                        channels[channelID].ExecutionBachtes.WriteBatch.AddLast(channels[channelID].RateControlledQueues.WriteQueues[priorityLevel].First);
                        channels[channelID].RateControlledQueues.WriteQueues[priorityLevel].RemoveFirst();
                    }
                }
                for (int chipNo = 0; chipNo < InputStreamBase.PriorityClassCount; chipNo++)
                {
                    if (channels[channelID].RateControlledQueues.GCWriteQueueForChips[chipNo].Count > 0)
                    {
                        allQueuesEmpty = false;
                        channels[channelID].ExecutionBachtes.WriteBatch.AddLast(channels[channelID].RateControlledQueues.GCWriteQueueForChips[chipNo].First);
                        channels[channelID].RateControlledQueues.GCWriteQueueForChips[chipNo].RemoveFirst();
                    }
                }
            }*/
            #endregion
        }

        private delegate void ExecutionBatchCreator(uint channelID);

        private event ExecutionBatchCreator createExecutionBatch;
        #endregion

        #region ReadWriteBalancer
        enum TransactionExecuterReturnStatus { NoOperationFound, EmptyBatch, ExittedForOtherQueues, SuccessfullyExecuted }
        TransactionExecuterReturnStatus GenerateReadTransaction(uint channelID, bool reExecute)
        {
            BusChannelRPB targetChannel = channels[channelID];
            InternalReadRequestLinkedList sourceReadReqList = targetChannel.ExecutionBachtes.ReadBatch;
            //Check is there any request to be serviced?
            if (sourceReadReqList.Count == 0)
                return TransactionExecuterReturnStatus.EmptyBatch;

            if (!reExecute)
            {
                //To preserve timing priority between reads and writes, we first check for the existence of write requests issued sooner than reads
                if (targetChannel.ExecutionBachtes.WriteBatch.Count > 0)
                {
                    if (_readWriteBalancerEnabled)
                    {
                        ulong writeWaitTime = XEngineFactory.XEngine.Time - targetChannel.ExecutionBachtes.WriteBatch.First.Value.IssueTime;
                        ulong readWaitTime = XEngineFactory.XEngine.Time - sourceReadReqList.First.Value.IssueTime;
                        if (writeWaitTime > readWaitTime * _readToWritePrioritizationFactor
                            && targetChannel.ExecutionBachtes.WriteBatch.First.Value.UpdateRead == null)
                            //give another chance to reads
                            if (targetChannel.ReadHistory > _readToWritePrioritizationFactor * targetChannel.WriteHistory)
                                return TransactionExecuterReturnStatus.ExittedForOtherQueues;
                    }
                    else
                    {
                        if (targetChannel.ExecutionBachtes.WriteBatch.First.Value.IssueTime < sourceReadReqList.First.Value.IssueTime
                            && targetChannel.ExecutionBachtes.WriteBatch.First.Value.UpdateRead == null)
                            return TransactionExecuterReturnStatus.ExittedForOtherQueues;
                    }
                }
                if (targetChannel.ExecutionBachtes.EraseBatch.Count > 0)
                {
                    if (_readWriteBalancerEnabled)
                    {
                        ulong eraseWaitTime = XEngineFactory.XEngine.Time - targetChannel.ExecutionBachtes.EraseBatch.First.Value.IssueTime;
                        ulong readWaitTime = XEngineFactory.XEngine.Time - sourceReadReqList.First.Value.IssueTime;
                        if (eraseWaitTime > readWaitTime * _readToWritePrioritizationFactor
                            && targetChannel.ExecutionBachtes.EraseBatch.First.Value.InternalWriteRequestList.Count == 0)
                            //give another chance to reads
                            if (targetChannel.ReadHistory > _readToWritePrioritizationFactor * targetChannel.WriteHistory)
                                return TransactionExecuterReturnStatus.ExittedForOtherQueues;
                    }
                    else
                    {
                        if (targetChannel.ExecutionBachtes.EraseBatch.First.Value.IssueTime < sourceReadReqList.First.Value.IssueTime
                            && targetChannel.ExecutionBachtes.EraseBatch.First.Value.InternalWriteRequestList.Count == 0)
                            return TransactionExecuterReturnStatus.ExittedForOtherQueues;
                    }
                }
            }

            for (var firstReq = sourceReadReqList.First; firstReq != null; firstReq = firstReq.Next)
            {
                bool OKforExecution = false;

                if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                    OKforExecution = true;
                else if (_readWriteBalancerEnabled)
                {
                    if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Writing && !firstReq.Value.TargetFlashChip.Suspended)
                    {
                        if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > writeReasonableSuspensionTimeForRead)
                        {
                            ulong writeWaitTime = firstReq.Value.TargetFlashChip.ExpectedFinishTime - firstReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest - _FTL.pageProgramLatency;
                            ulong readWaitTime = XEngineFactory.XEngine.Time - firstReq.Value.IssueTime + 1;
                            if (writeWaitTime < readWaitTime * _readToWritePrioritizationFactor)
                                OKforExecution = true;
                        }
                    }
                    else if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Erasing && !firstReq.Value.TargetFlashChip.Suspended)
                    {
                        if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > eraseReasonableSuspensionTimeForRead)
                        {
                            //ulong eraseWaitTime = XEngineFactory.XEngine.Time - firstReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest;
                            ulong eraseWaitTime = firstReq.Value.TargetFlashChip.ExpectedFinishTime - firstReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest - _FTL.eraseLatency;
                            ulong readWaitTime = XEngineFactory.XEngine.Time - firstReq.Value.IssueTime + 1;
                            if (eraseWaitTime < readWaitTime * _readToWritePrioritizationFactor)
                                OKforExecution = true;
                        }
                    }
                }


                //To prevent starvation we should check if the oldest pending InternalRequest could be serviced or not?
                if ((firstReq != sourceReadReqList.First) && (sourceReadReqList.First.Value.TargetFlashChip.Status == FlashChipStatus.Idle))
                    break;
                if (OKforExecution)
                {
                    #region SearchForMultiDieCommand
                    for (var secondReq = firstReq.Next; secondReq != null; secondReq = secondReq.Next)
                        if ((firstReq.Value.TargetPageAddress.OverallFlashChipID == secondReq.Value.TargetPageAddress.OverallFlashChipID)
                            &&
                            (firstReq.Value.TargetPageAddress.DieID != secondReq.Value.TargetPageAddress.DieID))
                        {
                            firstReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                            secondReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                            sourceReadReqList.Remove(firstReq);
                            sourceReadReqList.Remove(secondReq);
                            targetChannel.ExecutionBachtes.ReadActivityHistoryRecent += 2;
                            _FTL.FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                            return TransactionExecuterReturnStatus.SuccessfullyExecuted;
                        }
                    #endregion
                    #region SearchForTwoPlaneCommand
                    for (var secondReq = firstReq.Next; secondReq != null; secondReq = secondReq.Next)
                    {
                        if ((firstReq.Value.TargetPageAddress.OverallFlashChipID == secondReq.Value.TargetPageAddress.OverallFlashChipID)
                            && (firstReq.Value.TargetPageAddress.DieID == secondReq.Value.TargetPageAddress.DieID)
                            && (firstReq.Value.TargetPageAddress.PlaneID != secondReq.Value.TargetPageAddress.PlaneID)
                            && ((firstReq.Value.TargetPageAddress.BlockID == secondReq.Value.TargetPageAddress.BlockID) || !_FTL.BAConstraintForMultiPlane)
                            && (firstReq.Value.TargetPageAddress.PageID == secondReq.Value.TargetPageAddress.PageID))
                        {
                            firstReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                            secondReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                            sourceReadReqList.Remove(firstReq);
                            sourceReadReqList.Remove(secondReq);
                            targetChannel.ExecutionBachtes.ReadActivityHistoryRecent += 2;
                            _FTL.FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                            return TransactionExecuterReturnStatus.SuccessfullyExecuted;
                        }
                    }
                    #endregion
                    #region SingleOperation
                    sourceReadReqList.Remove(firstReq);
                    targetChannel.ExecutionBachtes.ReadActivityHistoryRecent++;
                    _FTL.FCC.SendSimpleCommandToChip(firstReq.Value);
                    return TransactionExecuterReturnStatus.SuccessfullyExecuted;
                    #endregion
                }
            }

            return TransactionExecuterReturnStatus.NoOperationFound;
        }
        TransactionExecuterReturnStatus GenerateWriteTransaction(uint channelID, bool reExecute)
        {
            BusChannelRPB targetChannel = channels[channelID];
            InternalWriteRequestLinkedList sourceWriteReqList = targetChannel.ExecutionBachtes.WriteBatch;
            //Check is there any request to be serviced?
            if (sourceWriteReqList.Count == 0)
                return TransactionExecuterReturnStatus.EmptyBatch;

            if (targetChannel.ExecutionBachtes.EraseBatch.Count > 0)
            {
                if (_readWriteBalancerEnabled)
                {
                    ulong eraseWaitTime = XEngineFactory.XEngine.Time - targetChannel.ExecutionBachtes.EraseBatch.First.Value.IssueTime;
                    ulong writeWaitTime = XEngineFactory.XEngine.Time - sourceWriteReqList.First.Value.IssueTime;
                    if (eraseWaitTime > writeWaitTime * _writeToErasePrioritizationFactor
                        && targetChannel.ExecutionBachtes.EraseBatch.First.Value.InternalWriteRequestList.Count == 0)
                        return TransactionExecuterReturnStatus.ExittedForOtherQueues;
                }
                else
                {
                    if (targetChannel.ExecutionBachtes.EraseBatch.First.Value.IssueTime < sourceWriteReqList.First.Value.IssueTime
                        && targetChannel.ExecutionBachtes.EraseBatch.First.Value.InternalWriteRequestList.Count == 0)
                        return TransactionExecuterReturnStatus.ExittedForOtherQueues;
                }
            }

            for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
            {
                bool OKforExecution = false;
                if (writeReq.Value.UpdateRead == null)
                {
                    if (writeReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                        OKforExecution = true;
                    /* For now, erase suspension for writes is disabled so that we just suspend operations in favor if reads
                     * else
                    {
                        if (_readWriteBalancerEnabled && !writeReq.Value.TargetFlashChip.Suspended)
                        {
                            if (writeReq.Value.TargetFlashChip.Status == FlashChipStatus.Erasing)
                                if (writeReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > eraseReasonableSuspensionTimeForWrite)
                                    OKforExecution = true;
                        }
                    }*/
                }

                if (OKforExecution)
                {
                    FlashChip targetChip = writeReq.Value.TargetFlashChip;
                    InternalWriteRequest[,] candidateWriteReqs = new InternalWriteRequest[_FTL.DieNoPerChip, _FTL.PlaneNoPerDie];
                    uint[] countOfWriteReqForDie = new uint[_FTL.DieNoPerChip];
                    InternalWriteRequest firstReq = writeReq.Value;
                    uint dieID, planeID;
                    uint firstDieID = writeReq.Value.TargetPageAddress.DieID, firstPlaneID = writeReq.Value.TargetPageAddress.PlaneID;

                    for (int i = 0; i < _FTL.DieNoPerChip; i++)
                    {
                        countOfWriteReqForDie[i] = 0;
                        for (int j = 0; j < _FTL.PlaneNoPerDie; j++)
                            candidateWriteReqs[i, j] = null;
                    }
                    countOfWriteReqForDie[firstDieID]++;
                    candidateWriteReqs[firstDieID, firstPlaneID] = firstReq;

                    int reqCount = 1;
                    InternalWriteRequestLinkedList executionList = new InternalWriteRequestLinkedList();
                    ulong transferTime = 0;

                    for (var nextWriteReq = writeReq.Next; nextWriteReq != null && reqCount < _FTL.FlashChipExecutionCapacity; nextWriteReq = nextWriteReq.Next)
                        if (nextWriteReq.Value.TargetPageAddress.LocalFlashChipID == targetChip.LocalChipID
                            && nextWriteReq.Value.UpdateRead == null)
                        {
                            if (candidateWriteReqs[nextWriteReq.Value.TargetPageAddress.DieID, nextWriteReq.Value.TargetPageAddress.PlaneID] == null)
                            {
                                reqCount++;
                                countOfWriteReqForDie[nextWriteReq.Value.TargetPageAddress.DieID]++;
                                candidateWriteReqs[nextWriteReq.Value.TargetPageAddress.DieID, nextWriteReq.Value.TargetPageAddress.PlaneID] = nextWriteReq.Value;
                            }
                        }

                    if (reqCount == 1)
                    {
                        if (firstReq.IsForGC)
                        {
                            firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                            targetChannel.ExecutionBachtes.WriteActivityHistoryRecent++;
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                            _FTL.FCC.SendSimpleCommandToChip(firstReq);
                        }
                        else
                        {
                            targetChannel.ExecutionBachtes.WriteActivityHistoryRecent++;
                            _FTL.AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                        }
                        return TransactionExecuterReturnStatus.SuccessfullyExecuted;
                    }

                    bool multiPlaneFlag = false, multiDieFlag = false;
                    uint lastDieID = uint.MaxValue;
                    for (uint dieCntr = 0; dieCntr < _FTL.DieNoPerChip; dieCntr++)
                    {
                        dieID = (firstDieID + dieCntr) % _FTL.DieNoPerChip;
                        if (countOfWriteReqForDie[dieID] > 0)
                        {
                            InternalWriteRequest firstInternalReqOfDie = null;
                            for (uint planeCntr = 0; planeCntr < _FTL.PlaneNoPerDie; planeCntr++)
                            {
                                planeID = (firstPlaneID + planeCntr) % _FTL.PlaneNoPerDie;
                                InternalWriteRequest currentReq = candidateWriteReqs[dieID, planeID];
                                if (currentReq != null)
                                {
                                    if (firstInternalReqOfDie == null)
                                    {
                                        if (lastDieID != uint.MaxValue)
                                        {
                                            transferTime += _FTL.FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            if (transferTime >= _FTL.pageProgramLatency)
                                            {
                                                transferTime -= _FTL.FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                                continue;
                                            }
                                            multiDieFlag = true;
                                        }
                                        lastDieID = dieID;
                                        firstInternalReqOfDie = currentReq;
                                        if (!currentReq.IsForGC)
                                            _FTL.AllocatePPNInPlane(currentReq);
                                        executionList.AddLast(currentReq);
                                    }
                                    else
                                    {
                                        transferTime += _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        if (transferTime >= _FTL.pageProgramLatency)
                                        {
                                            transferTime -= _FTL.FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            continue;
                                        }
                                        if (currentReq.IsForGC)
                                        {
                                            if ((currentReq.TargetPageAddress.BlockID == firstInternalReqOfDie.TargetPageAddress.BlockID) || !_FTL.BAConstraintForMultiPlane)
                                                if (currentReq.TargetPageAddress.PlaneID == firstInternalReqOfDie.TargetPageAddress.PlaneID)
                                                {
                                                    multiPlaneFlag = true;
                                                    executionList.AddLast(currentReq);
                                                    continue;
                                                }
                                            transferTime -= _FTL.FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        }
                                        else
                                        {
                                            if (_FTL.FindLevelPageStrict(targetChip, firstInternalReqOfDie, currentReq))
                                            {
                                                multiPlaneFlag = true;
                                                executionList.AddLast(currentReq);
                                            }
                                            else
                                                transferTime -= _FTL.FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (executionList.Count > 1)
                    {
                        if (multiPlaneFlag && multiDieFlag)
                        {
                            for (var wr = executionList.First; wr != null; wr = wr.Next)
                            {
                                sourceWriteReqList.Remove(wr.Value.RelatedNodeInList);
                                wr.Value.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                            }
                        }
                        else if (multiDieFlag)
                        {
                            for (var wr = executionList.First; wr != null; wr = wr.Next)
                            {
                                sourceWriteReqList.Remove(wr.Value.RelatedNodeInList);
                                wr.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                            }
                        }
                        else
                        {
                            for (var wr = executionList.First; wr != null; wr = wr.Next)
                            {
                                sourceWriteReqList.Remove(wr.Value.RelatedNodeInList);
                                wr.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                            }
                        }
                        targetChannel.ExecutionBachtes.WriteActivityHistoryRecent += (ulong)executionList.Count;
                        _FTL.FCC.SendAdvCommandToChipWR(executionList);
                    }
                    else
                    {
                        firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                        targetChannel.ExecutionBachtes.WriteActivityHistoryRecent++;
                        sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                        _FTL.FCC.SendSimpleCommandToChip(firstReq);
                    }
                    return TransactionExecuterReturnStatus.SuccessfullyExecuted;
                }
            }
            return TransactionExecuterReturnStatus.NoOperationFound;
        }
        TransactionExecuterReturnStatus GenerateEraseTransaction(uint channelID)
        {
            BusChannelRPB targetChannel = channels[channelID];
            InternalCleanRequestLinkedList sourceEraseReqList = targetChannel.ExecutionBachtes.EraseBatch;
            //Check is there any request to be serviced?
            if (sourceEraseReqList.Count == 0)
                return TransactionExecuterReturnStatus.EmptyBatch;

            for (var eraseReq = sourceEraseReqList.First; eraseReq != null; eraseReq = eraseReq.Next)
            {
                if (eraseReq.Value.InternalWriteRequestList.Count > 0)
                    continue;
                else if (eraseReq.Value.TargetFlashChip.Status != FlashChipStatus.Idle)
                    continue;

                FlashChip targetChip = eraseReq.Value.TargetFlashChip;
                InternalCleanRequest[,] candidateEraseReqs = new InternalCleanRequest[_FTL.DieNoPerChip, _FTL.PlaneNoPerDie];
                uint[] countOfEraseReqForDie = new uint[_FTL.DieNoPerChip];
                InternalCleanRequest firstReq = eraseReq.Value;
                uint dieID, planeID;
                uint firstDieID = eraseReq.Value.TargetPageAddress.DieID, firstPlaneID = eraseReq.Value.TargetPageAddress.PlaneID;

                for (int i = 0; i < _FTL.DieNoPerChip; i++)
                {
                    countOfEraseReqForDie[i] = 0;
                    for (int j = 0; j < _FTL.PlaneNoPerDie; j++)
                        candidateEraseReqs[i, j] = null;
                }
                countOfEraseReqForDie[firstDieID]++;
                candidateEraseReqs[firstDieID, firstPlaneID] = firstReq;

                int reqCount = 1;
                for (var nextEraseReq = eraseReq.Next; nextEraseReq != null && reqCount < _FTL.FlashChipExecutionCapacity; nextEraseReq = nextEraseReq.Next)
                    if (nextEraseReq.Value.TargetPageAddress.LocalFlashChipID == eraseReq.Value.TargetPageAddress.LocalFlashChipID &&
                        nextEraseReq.Value.InternalWriteRequestList.Count == 0)
                    {
                        if (candidateEraseReqs[nextEraseReq.Value.TargetPageAddress.DieID, nextEraseReq.Value.TargetPageAddress.PlaneID] == null)
                        {
                            reqCount++;
                            countOfEraseReqForDie[nextEraseReq.Value.TargetPageAddress.DieID]++;
                            candidateEraseReqs[nextEraseReq.Value.TargetPageAddress.DieID, nextEraseReq.Value.TargetPageAddress.PlaneID] = nextEraseReq.Value;
                        }
                    }

                if (reqCount == 1)
                {
                    sourceEraseReqList.Remove(firstReq.RelatedNodeInList);
                    _FTL.FCC.SendSimpleCommandToChip(firstReq);
                    return TransactionExecuterReturnStatus.SuccessfullyExecuted;
                }

                InternalCleanRequestLinkedList executionList = new InternalCleanRequestLinkedList();
                bool multiPlaneFlag = false, multiDieFlag = false;
                uint lastDieID = uint.MaxValue;
                for (uint dieCntr = 0; dieCntr < _FTL.DieNoPerChip; dieCntr++)
                {
                    dieID = (firstDieID + dieCntr) % _FTL.DieNoPerChip;
                    if (countOfEraseReqForDie[dieID] > 0)
                    {
                        InternalCleanRequest firstInternalReqOfDie = null;
                        for (uint planeCntr = 0; planeCntr < _FTL.PlaneNoPerDie; planeCntr++)
                        {
                            planeID = (firstPlaneID + planeCntr) % _FTL.PlaneNoPerDie;
                            InternalCleanRequest currentReq = candidateEraseReqs[dieID, planeID];
                            if (currentReq != null)
                            {
                                if (firstInternalReqOfDie == null)
                                {
                                    if (lastDieID != uint.MaxValue)
                                        multiDieFlag = true;
                                    lastDieID = dieID;
                                    firstInternalReqOfDie = currentReq;
                                    executionList.AddLast(currentReq);
                                }
                                else
                                {
                                    if ((currentReq.TargetPageAddress.BlockID == firstInternalReqOfDie.TargetPageAddress.BlockID) || !_FTL.BAConstraintForMultiPlane)
                                    {
                                        multiPlaneFlag = true;
                                        executionList.AddLast(currentReq);
                                    }
                                }
                            }
                        }
                    }
                }

                if (executionList.Count > 1)
                {
                    if (multiPlaneFlag && multiDieFlag)
                    {
                        for (var er = executionList.First; er != null; er = er.Next)
                        {
                            sourceEraseReqList.Remove(er.Value.RelatedNodeInList);
                            er.Value.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                        }
                    }
                    else if (multiDieFlag)
                    {
                        for (var er = executionList.First; er != null; er = er.Next)
                        {
                            sourceEraseReqList.Remove(er.Value.RelatedNodeInList);
                            er.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                        }
                    }
                    else
                    {
                        for (var er = executionList.First; er != null; er = er.Next)
                        {
                            sourceEraseReqList.Remove(er.Value.RelatedNodeInList);
                            er.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                        }
                    }
                    _FTL.FCC.SendAdvCommandToChipER(executionList);
                }
                else
                {
                    firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                    sourceEraseReqList.Remove(firstReq.RelatedNodeInList);
                    _FTL.FCC.SendSimpleCommandToChip(firstReq);
                }
                return TransactionExecuterReturnStatus.SuccessfullyExecuted;
            }
            return TransactionExecuterReturnStatus.NoOperationFound;
        }

        #region Event Handlers for Last Stage
        bool PrioritySchedulerLocked = false;
        public override void OnBusChannelIdle(BusChannelBase channel)
        {
            BusChannelRPB targetChannel = channel as BusChannelRPB;
            if (targetChannel.ActiveTransfersCount > 0)
                throw new Exception("Invalid BusChannelIdle invokation!");

            if (dynamicBatching || channels[targetChannel.ChannelID].ExecutionBachtes.ReadBatch.Count == 0
                || channels[targetChannel.ChannelID].ExecutionBachtes.WriteBatch.Count == 0
                || channels[targetChannel.ChannelID].ExecutionBachtes.EraseBatch.Count == 0)
                createExecutionBatch(targetChannel.ChannelID);

            //early escape of function execution to decrease overall execution time.
            if (targetChannel.BusyChipCount == _FTL.ChipNoPerChannel)
                return;

            TransactionExecuterReturnStatus readExecStatus = GenerateReadTransaction(targetChannel.ChannelID, false);
            if (readExecStatus != TransactionExecuterReturnStatus.SuccessfullyExecuted)
            {
                TransactionExecuterReturnStatus writeExecStatus = GenerateWriteTransaction(targetChannel.ChannelID, false);
                if (writeExecStatus != TransactionExecuterReturnStatus.SuccessfullyExecuted)
                    if (GenerateEraseTransaction(targetChannel.ChannelID) != TransactionExecuterReturnStatus.SuccessfullyExecuted)
                        if (readExecStatus == TransactionExecuterReturnStatus.ExittedForOtherQueues)
                            if (GenerateReadTransaction(targetChannel.ChannelID, true) != TransactionExecuterReturnStatus.SuccessfullyExecuted
                                && writeExecStatus == TransactionExecuterReturnStatus.ExittedForOtherQueues)
                                GenerateWriteTransaction(targetChannel.ChannelID, true);
            }
        }
        public override void OnFlashchipIdle(FlashChip targetFlashchip)
        {
            uint channelID = targetFlashchip.ChannelID;
            if (dynamicBatching || channels[channelID].ExecutionBachtes.ReadBatch.Count == 0
                || channels[channelID].ExecutionBachtes.WriteBatch.Count == 0
                || channels[channelID].ExecutionBachtes.EraseBatch.Count == 0)
                createExecutionBatch(channelID);
            if (channels[channelID].Status == BusChannelStatus.Idle)
            {
                TransactionExecuterReturnStatus readExecStatus = GenerateReadTransaction(channelID, false);
                if (readExecStatus != TransactionExecuterReturnStatus.SuccessfullyExecuted)
                {
                    TransactionExecuterReturnStatus writeExecStatus = GenerateWriteTransaction(channelID, false);
                    if (writeExecStatus != TransactionExecuterReturnStatus.SuccessfullyExecuted)
                        if (GenerateEraseTransaction(channelID) != TransactionExecuterReturnStatus.SuccessfullyExecuted)
                            if (readExecStatus == TransactionExecuterReturnStatus.ExittedForOtherQueues)
                                if (GenerateReadTransaction(channelID, true) != TransactionExecuterReturnStatus.SuccessfullyExecuted
                                    && writeExecStatus == TransactionExecuterReturnStatus.ExittedForOtherQueues)
                                    GenerateWriteTransaction(channelID, true);
                }
            }
        }
        #endregion
        #endregion
    }
}
