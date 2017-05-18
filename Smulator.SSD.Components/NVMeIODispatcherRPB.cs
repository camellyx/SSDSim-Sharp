using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public class FlowInformation
    {
        public StreamPriorityClass PriorityClass = StreamPriorityClass.Low;
        public LinkedList<IORequest> DeviceInternalQueue = new LinkedList<IORequest>();
        public InternalReadRequestLinkedList ReadQueue = new InternalReadRequestLinkedList();
        public InternalWriteRequestLinkedList WriteQueue = new InternalWriteRequestLinkedList();
        public readonly uint MaxCapacity;
        public uint FetchedRequests = 0;
        public FlowInformation(uint maxCapacity, StreamPriorityClass priorityClass)
        {
            MaxCapacity = maxCapacity;
            PriorityClass = priorityClass;
        }
    }
    class IOFetchEngine
    {
        HostInterfaceNVMe myHI = null;
        NVMeIODispatcherRPB myHandler = null;

        public IOFetchEngine(HostInterfaceNVMe HI, NVMeIODispatcherRPB handler)
        {
            myHI = HI;
            myHandler = handler;
        }
        public bool Fetch(uint streamID)
        {
            bool fetched = false;
            while (myHI.InputStreams[streamID].HeadRequest != null
                && myHandler.Flows[streamID].FetchedRequests < myHandler.Flows[streamID].MaxCapacity)
            {
                myHandler.Flows[streamID].DeviceInternalQueue.AddLast(myHI.InputStreams[streamID].HeadRequest.Value);
                myHI.InputStreams[streamID].HeadRequest = myHI.InputStreams[streamID].HeadRequest.Next;
                myHandler.Flows[streamID].FetchedRequests++;
                fetched = true;
            }
            return fetched;
        }
    }
    class RequestSegmentation
    {
        public bool Locked = false;
        HostInterfaceNVMe myHI = null;
        NVMeIODispatcherRPB myHandler = null;
        FTL myFTL = null;

        public RequestSegmentation(HostInterfaceNVMe HI, NVMeIODispatcherRPB handler, FTL ftl)
        {
            myHI = HI;
            myHandler = handler;
            myFTL = ftl;
            Locked = false;
        }
        public void AllocatePlaneToWriteTransaction(InternalWriteRequest internalReq)
        {
            if (myFTL.AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.State[internalReq.LPN] != 0)
            {
                if ((internalReq.State & myFTL.AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.State[internalReq.LPN])
                    != myFTL.AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.State[internalReq.LPN])
                {
                    myFTL.TotalPageReads++;
                    myFTL.PageReadsForWorkload++;
                    myFTL.PageReadsForUpdate++;
                    InternalReadRequest update = new InternalReadRequest();
                    myFTL.AddressMapper.ConvertPPNToPageAddress(myFTL.AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.PPN[internalReq.LPN], update.TargetPageAddress);
                    update.TargetFlashChip = myFTL.FlashChips[update.TargetPageAddress.OverallFlashChipID];
                    update.LPN = internalReq.LPN;
                    update.PPN = myFTL.AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.PPN[internalReq.LPN];
                    update.State = ((myFTL.AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.State[internalReq.LPN] ^ internalReq.State) & 0x7fffffff);
                    update.SizeInSubpages = FTL.SizeInSubpages(update.State);
                    update.SizeInByte = update.SizeInSubpages * FTL.SubPageCapacity;
                    update.BodyTransferCycles = update.SizeInByte / FTL.ChannelWidthInByte;
                    update.Type = InternalRequestType.Read;
                    update.IsUpdate = true;
                    update.RelatedWrite = internalReq;
                    update.RelatedIORequest = internalReq.RelatedIORequest;
                    internalReq.UpdateRead = update;
                    internalReq.State = (internalReq.State | update.State);
                    internalReq.SizeInSubpages = FTL.SizeInSubpages(internalReq.State);
                }
            }
            myFTL.AddressMapper.AllocatePlaneForWrite(internalReq.RelatedIORequest.StreamID, internalReq);
        }
        private void createFlashOperationRequest(ulong lpn, uint sizeInSubpages, uint state, IORequest currentIORequest)
        {
            if (currentIORequest.Type == IORequestType.Read)
            {
                InternalReadRequest subRequest = new InternalReadRequest();
                myFTL.TotalPageReads++;
                myFTL.PageReadsForWorkload++;
                if (myFTL.AddressMapper.AddressMappingDomains[currentIORequest.StreamID].MappingTable.State[lpn] == 0)
                {
                    for (var LPN = myFTL.currentActiveWriteLPNs[currentIORequest.StreamID].First; LPN != null; LPN = LPN.Next)
                        if (LPN.Value == lpn)
                            return;
                    throw new Exception("Accessing an unwritten logical address for read!");
                }

                myFTL.AddressMapper.ConvertPPNToPageAddress(myFTL.AddressMapper.AddressMappingDomains[currentIORequest.StreamID].MappingTable.PPN[lpn], subRequest.TargetPageAddress);
                subRequest.TargetFlashChip = myFTL.FlashChips[subRequest.TargetPageAddress.OverallFlashChipID];
                subRequest.LPN = lpn;
                subRequest.PPN = myFTL.AddressMapper.AddressMappingDomains[currentIORequest.StreamID].MappingTable.PPN[lpn];
                subRequest.SizeInSubpages = sizeInSubpages;
                subRequest.SizeInByte = sizeInSubpages * FTL.SubPageCapacity;
                subRequest.BodyTransferCycles = subRequest.SizeInByte / FTL.ChannelWidthInByte;

                subRequest.State = (myFTL.AddressMapper.AddressMappingDomains[currentIORequest.StreamID].MappingTable.State[lpn] & 0x7fffffff);
                subRequest.Type = InternalRequestType.Read;
                subRequest.RelatedIORequest = currentIORequest;

                //Arash: I have omitted a section of original code to ignore simultaneous read of same memory location

                currentIORequest.InternalRequestList.AddLast(subRequest);
            }
            else //currentRequest.Type == IORequestType.Write
            {
                InternalWriteRequest subRequest = new InternalWriteRequest();
                myFTL.currentActiveWriteLPNs[currentIORequest.StreamID].AddLast(lpn);
                myFTL.TotalPageProgams++;
                myFTL.PageProgramsForWorkload++;
                subRequest.LPN = lpn;
                subRequest.PPN = 0;
                subRequest.SizeInSubpages = sizeInSubpages;
                subRequest.SizeInByte = sizeInSubpages * FTL.SubPageCapacity;
                subRequest.BodyTransferCycles = subRequest.SizeInByte / FTL.ChannelWidthInByte;
                subRequest.State = state;
                subRequest.Type = InternalRequestType.Write;
                subRequest.RelatedIORequest = currentIORequest;
                //The above line should be positioned before AllocateLocation.
                AllocatePlaneToWriteTransaction(subRequest);
                currentIORequest.InternalRequestList.AddLast(subRequest);
            }
        }
        private void SegmentIORequestNoCache(IORequest currentRequest)
        {
            ulong lsn = currentRequest.LSN;
            uint reqSize = currentRequest.SizeInSubpages;

            uint subState = 0;
            uint handledSize = 0, subSize = 0;

            while (handledSize < reqSize)
            {
                lsn = lsn % myFTL.AddressMapper.LargestLSN;
                subSize = myFTL.SubpageNoPerPage - (uint)(lsn % myFTL.SubpageNoPerPage);
                if (handledSize + subSize >= reqSize)
                {
                    subSize = reqSize - handledSize;
                    handledSize += subSize;
                }
                ulong lpn = lsn / myFTL.SubpageNoPerPage;
                subState = myFTL.SetEntryState(lsn, subSize);
                createFlashOperationRequest(lpn, subSize, subState, currentRequest);
                lsn = lsn + subSize;
                handledSize += subSize;
            }

            if (currentRequest.InternalRequestList.Count == 0)
                myHI.SendEarlyResponseToHost(currentRequest);

            /*try to estimate the execution time of each flash transaction separately*/
            foreach (InternalRequest ir in currentRequest.InternalRequestList)
            {
                uint queueID = ir.TargetPageAddress.ChannelID;
                if (queueID == uint.MaxValue)
                    throw new Exception("Current implementation does not support dynamic channel allocation!");

            }
        }
        private bool channelLevelQueuesFull()
        {
            return false;
            /*int minQueueLength = int.MaxValue, maxQueueLength = 0;
            bool allFull = false;

            for (int i = 0; i < myFTL.ChannelInfos.Length; i++)
                for (int p = 0; p < InputStream.PriorityClassCount; p++)
                {
                    if ((myFTL.ChannelInfos[i] as BusChannelQoSAware).FirstStageChannelQueues.ReadQueues[p].Count < firstStageMaxQueueCapacity)
                        allFull = false;
                    if ((myFTL.ChannelInfos[i] as BusChannelQoSAware).FirstStageChannelQueues.WriteQueues[p].Count < firstStageMaxQueueCapacity)
                        allFull = false;
                    if ((myFTL.ChannelInfos[i] as BusChannelQoSAware).FirstStageChannelQueues.ReadQueues[p].Count < minQueueLength)
                        minQueueLength = (myFTL.ChannelInfos[i] as BusChannelQoSAware).FirstStageChannelQueues.ReadQueues[p].Count;
                    if ((myFTL.ChannelInfos[i] as BusChannelQoSAware).FirstStageChannelQueues.ReadQueues[p].Count > maxQueueLength)
                        maxQueueLength = (myFTL.ChannelInfos[i] as BusChannelQoSAware).FirstStageChannelQueues.ReadQueues[p].Count;
                    if ((myFTL.ChannelInfos[i] as BusChannelQoSAware).FirstStageChannelQueues.WriteQueues[p].Count > maxQueueLength)
                        maxQueueLength = (myFTL.ChannelInfos[i] as BusChannelQoSAware).FirstStageChannelQueues.WriteQueues[p].Count;

                    if (maxQueueLength - minQueueLength > firstStageDiffQueueCapacity)
                        return true;
                }

            return allFull;*/
        }
        public void Segment(uint streamID)
        {
            if (Locked)
                return;

            Locked = true;
            while (myHandler.Flows[streamID].DeviceInternalQueue.Count > 0)
            {
                IORequest currentRequest = myHandler.Flows[streamID].DeviceInternalQueue.First.Value;
                myHandler.Flows[streamID].DeviceInternalQueue.RemoveFirst();
                SegmentIORequestNoCache(currentRequest);
                IntegerPageAddress addr = null;
                foreach (InternalRequest ir in currentRequest.InternalRequestList)
                {
                    addr = ir.TargetPageAddress;
                    if (ir.Type == InternalRequestType.Read)
                    {
                        if (!myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadRequests.ContainsKey(currentRequest))
                            myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadRequests.Add(currentRequest, 1);
                        else
                            myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadRequests[currentRequest] = ((int)myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadRequests[currentRequest]) + 1;
                        if (!myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadRequests.ContainsKey(currentRequest))
                            myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadRequests.Add(currentRequest, 1);
                        else
                            myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadRequests[currentRequest]
                                = ((int)myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadRequests[currentRequest]) + 1;
                        if (!myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadRequests.ContainsKey(currentRequest))
                            myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadRequests.Add(currentRequest, 1);
                        else
                            myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadRequests[currentRequest]
                                = ((int)myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadRequests[currentRequest]) + 1;

                        myHandler.Flows[streamID].ReadQueue.AddLast(ir as InternalReadRequest);
                        myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadCount++;
                        myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadCount++;
                        myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadCount++;
                    }
                    else
                    {
                        if (!myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingWriteRequests.ContainsKey(currentRequest))
                            myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingWriteRequests.Add(currentRequest, 1);
                        else
                            myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingWriteRequests[currentRequest] = ((int)myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingWriteRequests[currentRequest]) + 1;
                        if (!myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingWriteRequests.ContainsKey(currentRequest))
                            myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingWriteRequests.Add(currentRequest, 1);
                        else
                            myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingWriteRequests[currentRequest]
                                = ((int)myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingWriteRequests[currentRequest]) + 1;
                        if (!myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingWriteRequests.ContainsKey(currentRequest))
                            myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingWriteRequests.Add(currentRequest, 1);
                        else
                            myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingWriteRequests[currentRequest]
                                = ((int)myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingWriteRequests[currentRequest]) + 1;

                        myHandler.Flows[streamID].WriteQueue.AddLast(ir as InternalWriteRequest);
                        myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingWriteCount++;
                        myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingWriteCount++;
                        myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingWriteCount++;
                        if ((ir as InternalWriteRequest).UpdateRead != null)
                        {
                            addr = (ir as InternalWriteRequest).UpdateRead.TargetPageAddress;
                            if (!myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadRequests.ContainsKey(currentRequest))
                                myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadRequests.Add(currentRequest, 1);
                            else
                                myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadRequests[currentRequest] = ((int)myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadRequests[currentRequest]) + 1;
                            if (!myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadRequests.ContainsKey(currentRequest))
                                myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadRequests.Add(currentRequest, 1);
                            else
                                myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadRequests[currentRequest]
                                    = ((int)myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadRequests[currentRequest]) + 1;
                            if (!myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadRequests.ContainsKey(currentRequest))
                                myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadRequests.Add(currentRequest, 1);
                            else
                                myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadRequests[currentRequest]
                                    = ((int)myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadRequests[currentRequest]) + 1;

                            myHandler.Flows[streamID].ReadQueue.AddLast((ir as InternalWriteRequest).UpdateRead);
                            myFTL.ResourceAccessTable.ChannelEntries[addr.ChannelID].WaitingReadCount++;
                            myFTL.ResourceAccessTable.DieEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID].WaitingReadCount++;
                            myFTL.ResourceAccessTable.PlaneEntries[addr.ChannelID][addr.LocalFlashChipID][addr.DieID][addr.PlaneID].WaitingReadCount++;
                        }
                    }
                    (myFTL.IOScheduler as IOSchedulerRPB).UpdateGCQueuePriority(ir.TargetPageAddress);
                }
            }
            Locked = false;

            myFTL.IOScheduler.Schedule((uint)myHandler.Flows[streamID].PriorityClass, streamID);
        }
    }
    public class NVMeIODispatcherRPB : NVMeIODispatcherBase
    {
        private IOFetchEngine IOFetchEngine = null;
        private RequestSegmentation RequestSegmenter = null;
        public FlowInformation[] Flows;
        private ulong historyUpdateInterval = 0;

        #region SetupFunctions
        public NVMeIODispatcherRPB(string id, FTL ftl, HostInterface HI, uint maxInternalQueueCapacityPerFlow, 
            InputStreamBase[] inputStreams, uint channelCount, ulong historyUpdateInterval) : base(id, ftl, HI as HostInterfaceNVMe)
        {
            Flows = new FlowInformation[inputStreams.Length];
            for (int i = 0; i < inputStreams.Length; i++)
                Flows[i] = new FlowInformation(maxInternalQueueCapacityPerFlow, inputStreams[i].PriorityClass);
            IOFetchEngine = new IOFetchEngine(HI as HostInterfaceNVMe, this);
            RequestSegmenter = new RequestSegmentation(HI as HostInterfaceNVMe, this, ftl);
            this.historyUpdateInterval = historyUpdateInterval;
            ftl.ResourceAccessTable = new RPBResourceAccessTable();
            ftl.ResourceAccessTable.ChannelEntries = new ResourceAccessTableEntry[ftl.ChannelCount];
            ftl.ResourceAccessTable.DieEntries = new ResourceAccessTableEntry[ftl.ChannelCount][][];
            ftl.ResourceAccessTable.PlaneEntries = new ResourceAccessTableEntry[ftl.ChannelCount][][][];
            for (int i = 0; i < ftl.ChannelCount; i++)
            {
                ftl.ResourceAccessTable.ChannelEntries[i] = new ResourceAccessTableEntry();
                ftl.ResourceAccessTable.DieEntries[i] = new ResourceAccessTableEntry[ftl.ChipNoPerChannel][];
                ftl.ResourceAccessTable.PlaneEntries[i] = new ResourceAccessTableEntry[ftl.ChipNoPerChannel][][];
                for (int chipCntr = 0; chipCntr < ftl.ChipNoPerChannel; chipCntr++)
                {
                    ftl.ResourceAccessTable.DieEntries[i][chipCntr] = new ResourceAccessTableEntry[ftl.DieNoPerChip];
                    ftl.ResourceAccessTable.PlaneEntries[i][chipCntr] = new ResourceAccessTableEntry[ftl.DieNoPerChip][];
                    for (int dieCntr = 0; dieCntr < ftl.DieNoPerChip; dieCntr++)
                    {
                        ftl.ResourceAccessTable.DieEntries[i][chipCntr][dieCntr] = new ResourceAccessTableEntry();
                        ftl.ResourceAccessTable.PlaneEntries[i][chipCntr][dieCntr] = new ResourceAccessTableEntry[ftl.PlaneNoPerDie];
                        for (int planeCntr = 0; planeCntr < ftl.PlaneNoPerDie; planeCntr++)
                            ftl.ResourceAccessTable.PlaneEntries[i][chipCntr][dieCntr][planeCntr] = new ResourceAccessTableEntry();
                    }
                }
            }
        }
        public override void Start()
        {
            XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(historyUpdateInterval, this, null, 0));
        }
        public override void SetupDelegates(bool propagateToChilds)
        {
            base.SetupDelegates(propagateToChilds);

            HostInterface.onIORequestArrived += new HostInterfaceNVMe.IORequestArrivedHandler(IORequestArrivedHandler);
            HostInterface.onIORequestCompleted += new HostInterfaceNVMe.RequestCompletedHandler(IORequestCompletedHandler);
        }
        public override void ResetDelegates(bool propagateToChilds)
        {
            HostInterface.onIORequestArrived -= new HostInterfaceNVMe.IORequestArrivedHandler(IORequestArrivedHandler);
            HostInterface.onIORequestCompleted -= new HostInterfaceNVMe.RequestCompletedHandler(IORequestCompletedHandler);

            base.ResetDelegates(propagateToChilds);
        }
        #endregion
        protected override void IORequestCompletedHandler(uint streamID)
        {
            Flows[streamID].FetchedRequests--;
            if (IOFetchEngine.Fetch(streamID))
                if (!RequestSegmenter.Locked)
                    RequestSegmenter.Segment(streamID);
        }
        protected override void IORequestArrivedHandler(uint streamID)
        {
            if (IOFetchEngine.Fetch(streamID))
                if (!RequestSegmenter.Locked)
                    RequestSegmenter.Segment(streamID);
        }
    }
}
