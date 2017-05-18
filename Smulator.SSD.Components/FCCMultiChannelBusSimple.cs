using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public class FCCMultiChannelBusSimple : FCCBase
    {
        public FTL FTL = null;
        public FlashChip[][] FlashChips = null;
        public BusChannelSprinkler[] channelInfos = null;
        ulong readCommandAddressCycleCount = 7, readCommandAddressCycleTime = 0;
        ulong eraseCommandAddressCycleCount = 5, eraseCommandAddressCycleTime = 0;
        ulong writeCommandAddressCycleCount = 7, writeCommandAddressCycleTime = 0;
        uint channelWidthInByte = 0;
        enum EventType
        {
            ReadDataTransferred, ReadCommandAndAddressTransferred, ReadCommandAndAddressTransferred_TwoPlane, ReadCommandAndAddressTransferred_Interleave,
            WriteDataTransferred, CopybackWriteDataTransferred, WriteDataTransferred_TwoPlane, WriteDataTransferred_Interleave, WriteDataTransferred_InterleaveTwoPlane,
            EraseSetupCompleted, EraseSetupCompleted_TwoPlane, EraseSetupCompleted_Interleave, EraseSetupCompleted_InterleaveTwoPlane
        };
        ulong readLatency = 0, writeLatency = 0, eraseLatency = 0;
        ulong readDataOutputReadyTime = 20, WEtoRBTransitionTime = 100, ALEtoDataStartTransitionTime = 70, dummyBusyTime = 500;

        /*The following list of variables are used for simulation performance reasons. This way, we escape from repeated calculation of transfer times.*/
        ulong normalReadSetup = 0, twoPlaneReadSetup = 0;
        ulong normalWriteSetup = 0, twoPlaneWriteSetup = 0, copybackWriteSetup = 0;
        ulong normalEraseSetup = 0, twoPlaneEraseSetup = 0;
        public readonly ulong suspendWriteSetup = 5000, suspendEraseSetup = 20000;

        #region SetupFunctions
        public FCCMultiChannelBusSimple(string id, ulong readTransferCycleTime, ulong writeTransferCycleTime,
            ulong readCommandAddressCycleCount, ulong writeCommandAddressCycleCount, ulong eraseCommandAddressCycleCount,
            ulong dummyBusyTime, ulong readDataOutputReadyTime, ulong ALEtoDataStartTransitionTime, ulong WEtoRBTransitionTime,
            ulong readLatency, ulong writeLatency, ulong eraseLatency,
            uint pageCapacityInByte,
            uint channelWidthInByte, FTL ftl, FlashChip[][] flashChips, BusChannelSprinkler[] channelInfo, HostInterface hostInterface)
            : base(id)
        {
            this.readLatency = readLatency;
            this.writeLatency = writeLatency;
            this.eraseLatency = eraseLatency;
            this.ReadTransferCycleTime = readTransferCycleTime;
            this.WriteTransferCycleTime = writeTransferCycleTime;
            this.readCommandAddressCycleCount = readCommandAddressCycleCount;
            this.readCommandAddressCycleTime = readCommandAddressCycleCount * writeTransferCycleTime;
            this.writeCommandAddressCycleCount = writeCommandAddressCycleCount;
            this.writeCommandAddressCycleTime = writeCommandAddressCycleCount * writeTransferCycleTime;
            this.eraseCommandAddressCycleCount = eraseCommandAddressCycleCount;
            this.eraseCommandAddressCycleTime = eraseCommandAddressCycleCount * writeTransferCycleTime;
            this.dummyBusyTime = dummyBusyTime;
            this.readDataOutputReadyTime = readDataOutputReadyTime;
            this.ALEtoDataStartTransitionTime = ALEtoDataStartTransitionTime;
            this.WEtoRBTransitionTime = WEtoRBTransitionTime;
            this.channelWidthInByte = channelWidthInByte;
            this.FTL = ftl;
            this.FlashChips = flashChips;
            this.channelInfos = channelInfo;
            this.HostInterface = hostInterface;

            this.normalReadSetup = readCommandAddressCycleTime + WEtoRBTransitionTime;
            this.twoPlaneReadSetup = readCommandAddressCycleTime + WEtoRBTransitionTime + dummyBusyTime + readCommandAddressCycleTime + WEtoRBTransitionTime;

            this.normalWriteSetup = writeCommandAddressCycleTime + ALEtoDataStartTransitionTime + WEtoRBTransitionTime;
            this.twoPlaneWriteSetup = writeCommandAddressCycleTime + ALEtoDataStartTransitionTime + WEtoRBTransitionTime + dummyBusyTime;
            this.copybackWriteSetup = writeCommandAddressCycleTime + WEtoRBTransitionTime;

            this.normalEraseSetup = eraseCommandAddressCycleTime + WEtoRBTransitionTime;
            this.twoPlaneEraseSetup = eraseCommandAddressCycleTime + WEtoRBTransitionTime + dummyBusyTime;

            this.InterleaveProgramSetup = normalWriteSetup;
            this.MultiPlaneProgramSetup = twoPlaneWriteSetup;
            this.InterleaveReadSetup = normalReadSetup;
            this.MultiplaneReadSetup = twoPlaneReadSetup;

        }

        public override void SetupDelegates(bool propagateToChilds)
        {
            base.SetupDelegates(propagateToChilds);
            for (int i = 0; i < FlashChips.Length; i++)
                for (int j = 0; j < FlashChips[i].Length; j++)
                    FlashChips[i][j].onInternalRequestServiced += new FlashChip.InternalRequestServicedHandler(OnFlashChipOperationCompleted);
        }

        public override void ResetDelegates(bool propagateToChilds)
        {
            for (int i = 0; i < FlashChips.Length; i++)
                for (int j = 0; j < FlashChips[i].Length; j++)
                    FlashChips[i][j].onInternalRequestServiced -= new FlashChip.InternalRequestServicedHandler(OnFlashChipOperationCompleted);


            base.ResetDelegates(propagateToChilds);
        }
        #endregion

        #region NetworkFunctions
        public override void SendSimpleCommandToChip(InternalRequest internalReq)
        {
            IntegerPageAddress targetAddress = internalReq.TargetPageAddress;
            FlashChip targetChip = this.FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID];
            ulong suspendOverhead = 0;
            if (targetChip.Status != FlashChipStatus.Idle)

                switch (internalReq.Type)
                {
                    case InternalRequestType.Read:
                        if (targetChip.Status == FlashChipStatus.Writing)
                        {
                            targetChip.Suspend();
                            suspendOverhead = suspendWriteSetup;
                        }
                        else if (targetChip.Status == FlashChipStatus.Erasing)
                        {
                            targetChip.Suspend();
                            suspendOverhead = suspendEraseSetup;
                        }
                        else throw new GeneralException("Requesting communication to a busy flash chip!");
                        break;
                    case InternalRequestType.Write:
                        if (targetChip.Status == FlashChipStatus.Erasing)
                        {
                            targetChip.Suspend();
                            suspendOverhead = suspendEraseSetup;
                        }
                        else throw new GeneralException("Requesting communication to a busy flash chip!");
                        break;
                    case InternalRequestType.Clean:
                        throw new GeneralException("Requesting communication to a busy flash chip!");
                }

            if (this.channelInfos[targetAddress.ChannelID].Status == BusChannelStatus.Busy)
                throw new GeneralException("Requesting communication on a busy bus!");

            BusChannelSprinkler targetChannel = this.channelInfos[targetAddress.ChannelID];
            targetChannel.Status = BusChannelStatus.Busy;
            targetChannel.ActiveTransfersCount++;
            targetChip.LastTransferStart = XEngineFactory.XEngine.Time;
            switch (internalReq.Type)
            {
                case InternalRequestType.Read:
                    FTL.IssuedReadCMD++;
                    /* tWB + tR + tRR
                     * According to ONFI standard, after command and data transfer, we have to wait for different signals to be driven*/
                    targetChip.Status = FlashChipStatus.TransferringReadCommandAddress;
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time + normalReadSetup + suspendOverhead,
                        this, internalReq, (int)EventType.ReadCommandAndAddressTransferred));
                    internalReq.TransferTime += normalReadSetup;
                    break;
                case InternalRequestType.Write:
                    FTL.IssuedProgramCMD++;
                    if ((internalReq as InternalWriteRequest).UpdateRead == null)
                    {
                        targetChip.Status = FlashChipStatus.TransferringWriteCommandAndData;
                        /*Command + 5*Address + tADL + Din + Command + tWB*/
                        XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                            + normalWriteSetup + suspendOverhead
                            + (internalReq.BodyTransferCycles * WriteTransferCycleTime), this, internalReq, (int)EventType.WriteDataTransferred));
                        internalReq.TransferTime += normalWriteSetup + (internalReq.BodyTransferCycles * WriteTransferCycleTime);
                    }
                    else
                    {
                        targetChip.Status = FlashChipStatus.TransferringReadCommandAddress;
                        //We assume that copyback command is just used for gc. Therefore, we do not consider internalReq.TransferTime
                        if (internalReq.ExecutionType == InternalRequestExecutionType.Copyback)
                        {
                            FTL.IssuedReadCMD++;
                            FTL.IssuedCopyBackProgramCMD++;
                            XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time + normalReadSetup, this,
                                (internalReq as InternalWriteRequest).UpdateRead, (int)EventType.ReadCommandAndAddressTransferred));
                        }
                        else
                            throw new Exception("Unexpected situation occured! A write request with unhandled update read.");
                    }
                    break;
                case InternalRequestType.Clean:
                    FTL.IssuedEraseCMD++;
                    targetChip.Status = FlashChipStatus.EraseSetup;
                    /*Command + 3*Address + Command + tWB*/
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time + normalEraseSetup,
                        this, internalReq, (int)EventType.EraseSetupCompleted));
                    break;
                default:
                    throw new GeneralException("Unhandled event specified!");
            }
        }
        public override void SendAdvCommandToChipRD(InternalReadRequest firstInternalReq, InternalReadRequest secondInternalReq)
        {
            IntegerPageAddress targetAddress = firstInternalReq.TargetPageAddress;
            //performance
            ulong suspendOverhead = 0;
            if (FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Status != FlashChipStatus.Idle)
            {
                if (FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Status == FlashChipStatus.Erasing
                    || FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Status == FlashChipStatus.Writing)
                {
                    FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Suspend();
                    suspendOverhead = suspendWriteSetup;
                    if (FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Status == FlashChipStatus.Erasing)
                        suspendOverhead = suspendEraseSetup;
                }
                else throw new GeneralException("Requesting communication to a busy flash chip!");
            }
            if ((firstInternalReq.ExecutionType == InternalRequestExecutionType.InterleavedMultiplane) || (firstInternalReq.Type != InternalRequestType.Read))
                throw new GeneralException("This function does not support this type of execution");

            BusChannelSprinkler targetChannel = this.channelInfos[targetAddress.ChannelID];
            targetChannel.Status = BusChannelStatus.Busy;

            targetChannel.FlashChips[targetAddress.LocalFlashChipID].Status = FlashChipStatus.TransferringReadCommandAddress;
            targetChannel.FlashChips[targetAddress.LocalFlashChipID].LastTransferStart = XEngineFactory.XEngine.Time;
            FTL.IssuedReadCMD += 2;

            switch (firstInternalReq.ExecutionType)
            {
                case InternalRequestExecutionType.Interleaved:
                    if (!firstInternalReq.TargetPageAddress.EqualsForInterleaved(secondInternalReq.TargetPageAddress))
                        throw new GeneralException("Violation of addressing condition for interleaved read command execution.");
                    FTL.IssuedInterleaveReadCMD += 2;
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                        + normalReadSetup + suspendOverhead, this, firstInternalReq, (int)EventType.ReadCommandAndAddressTransferred_Interleave));
                    targetChannel.ActiveTransfersCount++;
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                        + 2 * normalReadSetup + suspendOverhead, this, secondInternalReq, (int)EventType.ReadCommandAndAddressTransferred_Interleave));
                    targetChannel.ActiveTransfersCount++;
                    firstInternalReq.TransferTime += normalReadSetup + suspendOverhead;
                    secondInternalReq.TransferTime += normalReadSetup + suspendOverhead;
                    break;
                case InternalRequestExecutionType.Multiplane:
                    if (!firstInternalReq.TargetPageAddress.EqualsForMultiplane(secondInternalReq.TargetPageAddress, FTL.BAConstraintForMultiPlane))
                        throw new GeneralException("Violation of addressing condition for multiplane read command execution.");
                    FTL.IssuedMultiplaneReadCMD += 2;
                    InternalReadRequest[] reqs = new InternalReadRequest[2];
                    reqs[0] = firstInternalReq;
                    reqs[1] = secondInternalReq;
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                        + twoPlaneReadSetup, this, reqs, (int)EventType.ReadCommandAndAddressTransferred_TwoPlane));
                    targetChannel.ActiveTransfersCount++;
                    firstInternalReq.TransferTime += twoPlaneReadSetup + suspendOverhead;
                    secondInternalReq.TransferTime += twoPlaneReadSetup + suspendOverhead;
                    break;
                default:
                    throw new GeneralException("This function does not support this type of execution");
            }
        }
        public override void SendAdvCommandToChipWR(InternalWriteRequestLinkedList internalReqList)
        {
            IntegerPageAddress targetAddress = internalReqList.First.Value.TargetPageAddress;
            ulong suspendOverhead = 0;
            if (FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Status != FlashChipStatus.Idle)
            {
                if (FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Status == FlashChipStatus.Erasing)
                {
                    FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Suspend();
                    suspendOverhead = suspendEraseSetup;
                }
                else throw new GeneralException("Requesting communication to a busy flash chip!");
            }
            if (channelInfos[targetAddress.ChannelID].Status == BusChannelStatus.Busy)
                throw new GeneralException("Requesting communication on a busy bus!");

            BusChannelSprinkler targetChannel = this.channelInfos[targetAddress.ChannelID];
            targetChannel.Status = BusChannelStatus.Busy;
            targetChannel.FlashChips[targetAddress.LocalFlashChipID].LastTransferStart = XEngineFactory.XEngine.Time;

            ulong lastTime = XEngineFactory.XEngine.Time;
            ulong setupTime = suspendOverhead;
            IntegerPageAddress referenceAddressDie = null, referenceAddressPlane = null;
            switch (internalReqList.First.Value.ExecutionType)
            {
                case InternalRequestExecutionType.InterleavedMultiplane:
                    uint prevDieID = internalReqList.First.Value.TargetPageAddress.DieID;
                    if (internalReqList.Count < 3)
                        throw new Exception("InterleaveTwoPlane command execution at least requires 3 requests.");

                    this.FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Status = FlashChipStatus.TransferringWriteCommandAndData;
                    InternalWriteRequestLinkedList tempList = new InternalWriteRequestLinkedList();
                    referenceAddressDie = internalReqList.First.Value.TargetPageAddress;
                    referenceAddressPlane = internalReqList.First.Value.TargetPageAddress;
                    for (var req = internalReqList.First; req != null; req = req.Next)
                    {
                        FTL.IssuedProgramCMD++;
                        FTL.IssuedInterleaveMultiplaneProgramCMD++;
                        FTL.IssuedInterleaveProgramCMD++;

                        if (prevDieID == req.Value.TargetPageAddress.DieID)
                        {
                            if (req != internalReqList.First)
                                if (!req.Value.TargetPageAddress.EqualsForMultiplane(referenceAddressPlane, FTL.BAConstraintForMultiPlane))
                                    throw new GeneralException("Violation of addressing condition for multiplane program command execution.");
                            setupTime += twoPlaneWriteSetup + (req.Value.BodyTransferCycles * WriteTransferCycleTime);
                            tempList.AddLast(req.Value);
                            req.Value.TransferTime += twoPlaneWriteSetup + (req.Value.BodyTransferCycles * WriteTransferCycleTime);
                        }
                        else
                        {
                            if (!req.Value.TargetPageAddress.EqualsForInterleaved(referenceAddressDie))
                                throw new GeneralException("Violation of addressing condition for interleaved program command execution.");
                            referenceAddressPlane = req.Value.TargetPageAddress;
                            if (tempList.Count > 1)
                                FTL.IssuedMultiplaneProgramCMD += (ulong)tempList.Count;
                            setupTime -= dummyBusyTime;//the last request does not require TDBSY
                            lastTime += setupTime;
                            tempList.Last.Value.TransferTime -= dummyBusyTime;
                            XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(lastTime, this, tempList, (int)EventType.WriteDataTransferred_InterleaveTwoPlane));
                            targetChannel.ActiveTransfersCount++;

                            tempList = new InternalWriteRequestLinkedList();
                            tempList.AddLast(req.Value);
                            prevDieID = req.Value.TargetPageAddress.DieID;
                            setupTime = twoPlaneWriteSetup + (req.Value.BodyTransferCycles * WriteTransferCycleTime);
                            req.Value.TransferTime += twoPlaneWriteSetup + (req.Value.BodyTransferCycles * WriteTransferCycleTime);
                        }
                    }
                    //The last group of requests handled here
                    setupTime -= dummyBusyTime;
                    lastTime += setupTime;
                    tempList.Last.Value.TransferTime -= dummyBusyTime;
                    if (tempList.Count > 1)
                        FTL.IssuedMultiplaneProgramCMD += (ulong) tempList.Count;
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(lastTime, this, tempList, (int)EventType.WriteDataTransferred_InterleaveTwoPlane));
                    targetChannel.ActiveTransfersCount++;
                    break;
                case InternalRequestExecutionType.Interleaved:
                    targetChannel.FlashChips[targetAddress.LocalFlashChipID].Status = FlashChipStatus.TransferringWriteCommandAndData;
                    referenceAddressDie = internalReqList.First.Value.TargetPageAddress;
                    for (var req = internalReqList.First; req != null; req = req.Next)
                    {
                        if (req != internalReqList.First)
                            if (!req.Value.TargetPageAddress.EqualsForInterleaved(referenceAddressDie))
                                throw new GeneralException("Violation of addressing condition for interleaved program command execution.");
                        FTL.IssuedProgramCMD++;
                        FTL.IssuedInterleaveProgramCMD++;
                        
                        lastTime += normalWriteSetup + (req.Value.BodyTransferCycles * WriteTransferCycleTime);
                        req.Value.TransferTime += normalWriteSetup + (req.Value.BodyTransferCycles * WriteTransferCycleTime);
                        XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(lastTime, this, req.Value, (int)EventType.WriteDataTransferred_Interleave));
                        targetChannel.ActiveTransfersCount++;
                    }
                    break;
                case InternalRequestExecutionType.Multiplane:
                {
                    targetChannel.FlashChips[targetAddress.LocalFlashChipID].Status = FlashChipStatus.TransferringWriteCommandAndData;
                    referenceAddressPlane = internalReqList.First.Value.TargetPageAddress;
                    for (var req = internalReqList.First; req != null; req = req.Next)
                    {
                        if (req != internalReqList.First)
                            if (!req.Value.TargetPageAddress.EqualsForMultiplane(referenceAddressPlane, FTL.BAConstraintForMultiPlane))
                                throw new GeneralException("Violation of addressing condition for multiplane program command execution.");
                        FTL.IssuedProgramCMD++;
                        FTL.IssuedMultiplaneProgramCMD++;

                        setupTime += twoPlaneWriteSetup + (req.Value.BodyTransferCycles * WriteTransferCycleTime);
                        req.Value.TransferTime += twoPlaneWriteSetup + (req.Value.BodyTransferCycles * WriteTransferCycleTime);
                        if (req.Next == null)
                            req.Value.TransferTime -= dummyBusyTime;//the last request does not require TDBSY
                    }

                    setupTime -= dummyBusyTime;//the last request does not require TDBSY

                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time + setupTime, this, internalReqList, (int)EventType.WriteDataTransferred_TwoPlane));
                    targetChannel.ActiveTransfersCount++;
                    break;
                }
                case InternalRequestExecutionType.CopybackInterleaved:
                    break;
                case InternalRequestExecutionType.CopybackTwoPlane:
                    break;
                case InternalRequestExecutionType.CopybackInterleavedTwoPlane:
                    break;
            }
        }
        public override void SendAdvCommandToChipER(InternalCleanRequestLinkedList internalReqList)
        {
            IntegerPageAddress targetAddress = internalReqList.First.Value.TargetPageAddress;
            if (this.channelInfos[targetAddress.ChannelID].Status == BusChannelStatus.Busy)
                throw new GeneralException("Requesting communication on a busy bus!");

            BusChannelSprinkler targetChannel = this.channelInfos[targetAddress.ChannelID];
            targetChannel.Status = BusChannelStatus.Busy;
            targetChannel.FlashChips[targetAddress.LocalFlashChipID].LastTransferStart = XEngineFactory.XEngine.Time;

            ulong lastTime = XEngineFactory.XEngine.Time;
            ulong setupTime = 0;
            IntegerPageAddress referenceAddressDie = null, referenceAddressPlane = null;
            switch (internalReqList.First.Value.ExecutionType)
            {
                case InternalRequestExecutionType.InterleavedMultiplane:
                    uint prevDieID = internalReqList.First.Value.TargetPageAddress.DieID;
                    if (internalReqList.Count < 3)
                        throw new Exception("InterleaveTwoPlane command execution at least requires 3 requests.");

                    this.FlashChips[targetAddress.ChannelID][targetAddress.LocalFlashChipID].Status = FlashChipStatus.TransferringEraseCommandAddress;
                    InternalCleanRequestLinkedList tempList = new InternalCleanRequestLinkedList();
                    referenceAddressDie = internalReqList.First.Value.TargetPageAddress;
                    referenceAddressPlane = internalReqList.First.Value.TargetPageAddress;
                    for (var req = internalReqList.First; req != null; req = req.Next)
                    {
                        FTL.IssuedEraseCMD++;
                        FTL.IssuedInterleaveMultiplaneEraseCMD++;
                        FTL.IssuedInterleaveEraseCMD++;

                        if (prevDieID == req.Value.TargetPageAddress.DieID)
                        {
                            if (req != internalReqList.First)
                                if (!req.Value.TargetPageAddress.EqualsForMultiplaneErase(referenceAddressPlane, FTL.BAConstraintForMultiPlane))
                                    throw new GeneralException("Violation of addressing condition for multiplane erase command execution.");
                            setupTime += twoPlaneEraseSetup;
                            tempList.AddLast(req.Value);
                        }
                        else
                        {
                            if (!req.Value.TargetPageAddress.EqualsForInterleaved(referenceAddressDie))
                                throw new GeneralException("Violation of addressing condition for interleaved erase command execution.");
                            referenceAddressPlane = req.Value.TargetPageAddress;
                            if (tempList.Count > 1)
                                FTL.IssuedMultiplaneEraseCMD += (ulong)tempList.Count;
                            setupTime -= dummyBusyTime;//the last request does not require TDBSY
                            lastTime += setupTime;
                            XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(lastTime, this, tempList, (int)EventType.EraseSetupCompleted_InterleaveTwoPlane));
                            targetChannel.ActiveTransfersCount++;

                            tempList = new InternalCleanRequestLinkedList();
                            tempList.AddLast(req.Value);
                            prevDieID = req.Value.TargetPageAddress.DieID;
                            setupTime = twoPlaneEraseSetup;
                        }
                    }
                    //The last group of requests handled here
                    setupTime -= dummyBusyTime;
                    lastTime += setupTime;
                    if (tempList.Count > 1)
                        FTL.IssuedMultiplaneEraseCMD += (ulong) tempList.Count;
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(lastTime, this, tempList, (int)EventType.EraseSetupCompleted_InterleaveTwoPlane));
                    targetChannel.ActiveTransfersCount++;
                    break;
                case InternalRequestExecutionType.Interleaved:
                    targetChannel.FlashChips[targetAddress.LocalFlashChipID].Status = FlashChipStatus.EraseSetup;
                    referenceAddressDie = internalReqList.First.Value.TargetPageAddress;
                    for (var req = internalReqList.First; req != null; req = req.Next)
                    {
                        if (req != internalReqList.First)
                            if (!req.Value.TargetPageAddress.EqualsForInterleaved(referenceAddressDie))
                                throw new GeneralException("Violation of addressing condition for interleaved erase command execution.");
                        FTL.IssuedEraseCMD++;
                        FTL.IssuedInterleaveEraseCMD++;
                        lastTime += normalEraseSetup;
                        XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(lastTime, this, req.Value, (int)EventType.EraseSetupCompleted_Interleave));
                        targetChannel.ActiveTransfersCount++;
                    }
                    break;
                case InternalRequestExecutionType.Multiplane:
                    referenceAddressPlane = internalReqList.First.Value.TargetPageAddress;
                    var comparedReq = internalReqList.First.Next;
                    while (comparedReq != null)
                    {
                        if (!comparedReq.Value.TargetPageAddress.EqualsForMultiplaneErase(referenceAddressPlane, FTL.BAConstraintForMultiPlane))
                            throw new GeneralException("Violation of addressing condition for multiplane erase command execution.");
                        comparedReq = comparedReq.Next;
                    }
                    FTL.IssuedEraseCMD += (ulong) internalReqList.Count;
                    FTL.IssuedMultiplaneEraseCMD += (ulong) internalReqList.Count;
                    targetChannel.FlashChips[targetAddress.LocalFlashChipID].Status = FlashChipStatus.EraseSetup;
                    setupTime = ((ulong)internalReqList.Count) * twoPlaneEraseSetup - dummyBusyTime;
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time + setupTime, this, internalReqList, (int)EventType.EraseSetupCompleted_TwoPlane));
                    targetChannel.ActiveTransfersCount++;
                    break;
            }
        }
        #endregion

        #region EventHandlers
        public override void ProcessXEvent(XEvent e)
        {
            InternalReadRequest targetReadRequest = null;
            InternalRequest targetRequest = null;
            FlashChip targetChip = null;
            BusChannelSprinkler targetChannel = null;
            switch ((EventType)e.Type)
            {
                case EventType.ReadDataTransferred:
                    targetReadRequest = e.Parameters as InternalReadRequest;
                    targetChannel = this.channelInfos[targetReadRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetReadRequest.TargetPageAddress.LocalFlashChipID];

                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetReadRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    if (targetReadRequest.ExecutionType == InternalRequestExecutionType.Interleaved
                        || targetReadRequest.ExecutionType == InternalRequestExecutionType.InterleavedMultiplane)
                        if (targetChip.ThisRoundExecutionFinish != ulong.MaxValue)
                        {
                            if (targetChip.ThisRoundExecutionFinish > targetChip.LastTransferStart)
                                targetChip.TotalTransferPeriodOverlapped += (targetChip.ThisRoundExecutionFinish - targetChip.LastTransferStart);
                        }
                        else
                            targetChip.TotalTransferPeriodOverlapped += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);

                    targetChip.CurrentWaitingTransfers--;

                    if (targetReadRequest.IsUpdate)
                        targetReadRequest.RelatedWrite.UpdateRead = null;
                    else
                        HostInterface.SendResponseToHost(targetReadRequest);

                    /* This part handles read data transfers related to two-plane or interleaved command execution.
                     * According to the SSDSim source code, the data transfer of a group of requests, beloging to 
                     * the mentioned command types, is handled together.*/
                    if ((targetChip.CurrentWaitingTransfers > 0) &&
                        (targetChannel.WaitingCopybackRequests.Count == 0) &&
                        (targetChannel.WaitingFlashTransfersForEmergencyGC.Count == 0))
                    {
                        for (var waitingReq = targetChannel.WaitingFlashTransfers.First; waitingReq != null; waitingReq = waitingReq.Next)
                            if (waitingReq.Value.TargetPageAddress.LocalFlashChipID == targetChip.LocalChipID)
                            {
                                handleWaitingRead(targetChannel, targetChip, waitingReq.Value);
                                targetChannel.WaitingFlashTransfers.Remove(waitingReq);
                                return;
                            }
                    }

                    if (targetChip.CurrentExecutingOperationCount == 0 && targetChip.CurrentWaitingTransfers == 0)
                    {
                        targetChip.Status = FlashChipStatus.Idle;
                        if (targetChip.Suspended)
                            targetChip.Resume();
                    }
                    break;
                case EventType.ReadCommandAndAddressTransferred:
                    targetReadRequest = e.Parameters as InternalReadRequest;
                    targetChannel = this.channelInfos[targetReadRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetReadRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetReadRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.PerformOperation(targetReadRequest);
                    break;
                case EventType.ReadCommandAndAddressTransferred_TwoPlane:
                    targetReadRequest = (e.Parameters as InternalReadRequest[])[0];
                    targetChannel = this.channelInfos[targetReadRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetReadRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetReadRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.PerformOperation(targetReadRequest);
                    targetChip.PerformOperation((e.Parameters as InternalRequest[])[1]);
                    break;
                case EventType.ReadCommandAndAddressTransferred_Interleave:
                    targetReadRequest = e.Parameters as InternalReadRequest;
                    targetChannel = this.channelInfos[targetReadRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetReadRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetReadRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    if (targetChip.CurrentExecutingOperationCount > 0)
                        targetChip.TotalTransferPeriodOverlapped += XEngineFactory.XEngine.Time - targetChip.LastTransferStart;
                    targetChip.LastTransferStart = XEngineFactory.XEngine.Time;//Start of command transfer for second command
                    targetChip.PerformOperation(targetReadRequest);
                    break;
                case EventType.WriteDataTransferred:
                case EventType.EraseSetupCompleted:
                    targetRequest = e.Parameters as InternalRequest;
                    targetChannel = this.channelInfos[targetRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.PerformOperation(targetRequest);
                    break;
                case EventType.CopybackWriteDataTransferred:
                    targetRequest = e.Parameters as InternalRequest;
                    targetChannel = this.channelInfos[targetRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.PerformOperation(targetRequest);
                    break;
                case EventType.WriteDataTransferred_TwoPlane:
                {
                    InternalWriteRequestLinkedList reqList = e.Parameters as InternalWriteRequestLinkedList;
                    targetRequest = reqList.First.Value;
                    targetChannel = this.channelInfos[targetRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    for (var writeReq = reqList.First; writeReq != null; writeReq = writeReq.Next)
                        targetChip.PerformOperation(writeReq.Value);
                    break;
                }
                case EventType.EraseSetupCompleted_TwoPlane:
                {
                    InternalCleanRequestLinkedList reqList = e.Parameters as InternalCleanRequestLinkedList;
                    targetRequest = reqList.First.Value;
                    targetChannel = this.channelInfos[targetRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    for (var writeReq = reqList.First; writeReq != null; writeReq = writeReq.Next)
                        targetChip.PerformOperation(writeReq.Value);
                    break;
                }
                case EventType.WriteDataTransferred_Interleave:
                case EventType.EraseSetupCompleted_Interleave:
                    targetRequest = e.Parameters as InternalRequest;
                    targetChannel = this.channelInfos[targetRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    if (targetChip.CurrentExecutingOperationCount > 0)
                        targetChip.TotalTransferPeriodOverlapped += XEngineFactory.XEngine.Time - targetChip.LastTransferStart;
                    targetChip.LastTransferStart = XEngineFactory.XEngine.Time;//Start of data transfer for next command
                    targetChip.PerformOperation(targetRequest);
                    break;
                case EventType.WriteDataTransferred_InterleaveTwoPlane:
                {
                    InternalWriteRequestLinkedList reqList = e.Parameters as InternalWriteRequestLinkedList;
                    targetRequest = reqList.First.Value;
                    targetChannel = this.channelInfos[targetRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    if (targetChip.CurrentExecutingOperationCount > 0)
                        targetChip.TotalTransferPeriodOverlapped += XEngineFactory.XEngine.Time - targetChip.LastTransferStart;
                    targetChip.LastTransferStart = XEngineFactory.XEngine.Time;//Start of data transfer for next command
                    for (var writeReq = reqList.First; writeReq != null; writeReq = writeReq.Next)
                        targetChip.PerformOperation(writeReq.Value);
                    break;
                }
                case EventType.EraseSetupCompleted_InterleaveTwoPlane:
                {
                    InternalCleanRequestLinkedList reqList = e.Parameters as InternalCleanRequestLinkedList;
                    targetRequest = reqList.First.Value;
                    targetChannel = this.channelInfos[targetRequest.TargetPageAddress.ChannelID];
                    targetChannel.ActiveTransfersCount--;
                    targetChip = targetChannel.FlashChips[targetRequest.TargetPageAddress.LocalFlashChipID];
                    targetChip.TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    targetChip.Dies[targetRequest.TargetPageAddress.DieID].TotalTransferPeriod += (XEngineFactory.XEngine.Time - targetChip.LastTransferStart);
                    if (targetChip.CurrentExecutingOperationCount > 0)
                        targetChip.TotalTransferPeriodOverlapped += XEngineFactory.XEngine.Time - targetChip.LastTransferStart;
                    targetChip.LastTransferStart = XEngineFactory.XEngine.Time;//Start of data transfer for next command
                    for (var writeReq = reqList.First; writeReq != null; writeReq = writeReq.Next)
                        targetChip.PerformOperation(writeReq.Value);
                    break;
                }
                default:
                    throw new GeneralException("Unhandled event specified!");
            }

            if (targetChannel.ActiveTransfersCount > 0)
                return;

            /* Copyback requests are prioritized over other type of requests since they need very short transfer time.
               In addition, they are just used for GC purpose. */
            if (targetChannel.WaitingCopybackRequests.Count > 0)
            {
                InternalWriteRequest waitingReq = targetChannel.WaitingCopybackRequests.First.Value;
                targetChannel.FlashChips[waitingReq.TargetPageAddress.LocalFlashChipID].Status = FlashChipStatus.TransferringCopybackWriteCommand;
                targetChannel.FlashChips[waitingReq.TargetPageAddress.LocalFlashChipID].LastTransferStart = XEngineFactory.XEngine.Time;
                targetChannel.FlashChips[waitingReq.TargetPageAddress.LocalFlashChipID].CurrentWaitingTransfers--;//The related read request was waiting for the start of copyback write
                targetChannel.Status = BusChannelStatus.Busy;
                /*Command + 5*Address + Command + tWB*/
                XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                    + copybackWriteSetup, this, waitingReq, (int)EventType.CopybackWriteDataTransferred));
                targetChannel.ActiveTransfersCount++;
                targetChannel.WaitingCopybackRequests.Remove(waitingReq);
                return;
            }
            if (targetChannel.WaitingFlashTransfersForEmergencyGC.Count > 0)
            {
                InternalReadRequest waitingReq = targetChannel.WaitingFlashTransfersForEmergencyGC.First.Value;
                targetChannel.FlashChips[waitingReq.TargetPageAddress.LocalFlashChipID].Status = FlashChipStatus.TransferringReadData;
                targetChannel.FlashChips[waitingReq.TargetPageAddress.LocalFlashChipID].LastTransferStart = XEngineFactory.XEngine.Time;
                targetChannel.Status = BusChannelStatus.Busy;
                XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                    + (waitingReq.BodyTransferCycles * ReadTransferCycleTime), this, waitingReq, (int)EventType.ReadDataTransferred));
                targetChannel.ActiveTransfersCount++;
                targetChannel.WaitingFlashTransfersForEmergencyGC.Remove(waitingReq);
                return;
            }
            else if (targetChannel.WaitingFlashTransfers.Count > 0)
            {
                InternalReadRequest waitingReq = targetChannel.WaitingFlashTransfers.First.Value;
                targetChannel.FlashChips[waitingReq.TargetPageAddress.LocalFlashChipID].Status = FlashChipStatus.TransferringReadData;
                targetChannel.FlashChips[waitingReq.TargetPageAddress.LocalFlashChipID].LastTransferStart = XEngineFactory.XEngine.Time;
                targetChannel.Status = BusChannelStatus.Busy;
                XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                    + (waitingReq.BodyTransferCycles * ReadTransferCycleTime), this, waitingReq, (int)EventType.ReadDataTransferred));
                targetChannel.ActiveTransfersCount++;
                waitingReq.TransferTime += (waitingReq.BodyTransferCycles * ReadTransferCycleTime);
                targetChannel.WaitingFlashTransfers.Remove(waitingReq);
                return;
            }
            /*else if (targetChannel.WaitingFlashTransfersForBackgroundGC.Count > 0)
            {
                InternalReadRequest waitingReq = targetChannel.WaitingFlashTransfersForBackgroundGC.First.Value;
                targetChannel.FlashChips[waitingReq.TargetPageAddress.LocalFlashChipID].Status = FlashChipStatus.TransferringReadData;
                targetChannel.FlashChips[waitingReq.TargetPageAddress.LocalFlashChipID].LastTransferStart = XEngineFactory.XEngine.Time;
                targetChannel.Status = BusChannelStatus.Busy;
                XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                    + (waitingReq.BodyTransferCycles * ReadTransferCycleTime), this, waitingReq, (int)EventType.ReadDataTransferred));
                targetChannel.ActiveTransfersCount++;
                targetChannel.WaitingFlashTransfersForBackgroundGC.Remove(waitingReq);
                return;
            }*/
         
            targetChannel.Status = BusChannelStatus.Idle;
            FTL.IOScheduler.OnBusChannelIdle(targetChannel);
        }
        public void OnFlashChipOperationCompleted(InternalRequest targetRequest, uint rowID, FlashChip targetFlashChip) 
        {
            switch (targetRequest.Type)
            {
                case InternalRequestType.Read:
                    if (this.channelInfos[rowID].Status == BusChannelStatus.Idle)
                    {
                        handleWaitingRead(this.channelInfos[rowID], targetFlashChip, targetRequest as InternalReadRequest);
                        return;
                    }

                    if (targetRequest.ExecutionType == InternalRequestExecutionType.Copyback)
                        this.channelInfos[rowID].WaitingCopybackRequests.AddLast((targetRequest as InternalReadRequest).RelatedWrite);
                    else if (targetRequest.IsForGC)
                    {
                        if (targetRequest.RelatedCMReq.IsEmergency)
                            this.channelInfos[rowID].WaitingFlashTransfersForEmergencyGC.AddLast(targetRequest as InternalReadRequest);
                    }
                    else this.channelInfos[rowID].WaitingFlashTransfers.AddLast(targetRequest as InternalReadRequest);
                    return;
                case InternalRequestType.Write:
                    if (targetRequest.IsForGC)
                        targetRequest.RelatedCMReq.InternalWriteRequestList.Remove(targetRequest as InternalWriteRequest);
                    else
                        HostInterface.SendResponseToHost(targetRequest);
                    break;
                default:
                    break;
            }

            if (targetFlashChip.Status == FlashChipStatus.Idle)
            {
                if (targetFlashChip.Suspended)
                    targetFlashChip.Resume();
                else FTL.IOScheduler.OnFlashchipIdle(targetFlashChip);
            }
        }
        private void handleWaitingRead(BusChannelSprinkler targetChannel, FlashChip targetFlashChip, InternalReadRequest targetRequest)
        {
            if (targetRequest.ExecutionType == InternalRequestExecutionType.Copyback)
            {
                targetFlashChip.Status = FlashChipStatus.TransferringCopybackWriteCommand;
                targetFlashChip.CurrentWaitingTransfers--;//The related read request was waiting for the start of copyback write
                /*Command + 5 * Address + Command + tWB*/
                XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                    + copybackWriteSetup, this, targetRequest.RelatedWrite, (int)EventType.CopybackWriteDataTransferred));
            }
            else
            {
                if (targetFlashChip.CurrentExecutingOperationCount == 0)
                    targetFlashChip.Status = FlashChipStatus.TransferringReadData;
                XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time
                    + (targetRequest.BodyTransferCycles * ReadTransferCycleTime), this, targetRequest, (int)EventType.ReadDataTransferred));
                targetRequest.TransferTime += (targetRequest.BodyTransferCycles * ReadTransferCycleTime);
            }
            targetChannel.ActiveTransfersCount++;
            targetChannel.Status = BusChannelStatus.Busy;
            targetFlashChip.LastTransferStart = XEngineFactory.XEngine.Time;
        }
        #endregion
    }
}
