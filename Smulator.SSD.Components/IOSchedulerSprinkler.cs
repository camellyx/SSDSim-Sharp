using System;
using System.Collections.Generic;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public class IOSchedulerSprinkler : IOSchedulerBase
    {
        bool programEraseSuspensionEnabled = true;
        ulong _readToWritePrioritizationFactor = 1;
        ulong _writeReasonableSuspensionTimeForRead = 0;
        ulong _eraseReasonableSuspensionTimeForRead = 0;//the time period 

        public IOSchedulerSprinkler(string id, FTL ftl,
            ulong readToWritePrioritizationFactor,
            ulong suspendWriteSetup, ulong suspendEraseSetup, ulong ReadTransferCycleTime, ulong WriteTransferCycleTime) : base(id, ftl)
        {
            _readToWritePrioritizationFactor = readToWritePrioritizationFactor;

            _writeReasonableSuspensionTimeForRead = (suspendWriteSetup + 10000 + (_FTL.PageCapacity * ReadTransferCycleTime));
            _eraseReasonableSuspensionTimeForRead = (suspendEraseSetup + 10000 + (_FTL.PageCapacity * WriteTransferCycleTime));
        }
        public override void Start()
        {
            base.Start();
            if (_FTL.HostInterface.IsSaturatedMode)
            {
                XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(0, this, null, 0));
            }
        }

        /// <summary>
        /// This function is invoked to check for possiblity of handling new IORequest.
        /// Note: This function is just invoked in normal request generation mode, but not saturated one.
        /// </summary>
        public override void Schedule(uint priorityClass, uint streamID)
        {
            for (uint channelID = 0; channelID < _FTL.ChannelCount; channelID++)
            {
                if (_FTL.ChannelInfos[channelID].Status == BusChannelStatus.Idle)
                {
                    if (_FTL.GarbageCollector.ChannelInvokeGCBase(channelID))
                        continue;
                    if (!ServiceReadCommands(channelID))
                        ServiceWriteCommands(channelID);
                }
            }
        }
        bool PerformAdvancedReadCommand(InternalReadRequestLinkedList sourceReadReqList)
        {
            /*If allocation scheme determines plane address before die address, then in the advanced command execution
            process, we should priotize multiplane command allocation over multidie allocation*/
            if (_FTL.planePrioritizedOverDie)
            {
                #region SearchForTwoPlaneCommand
                if (_FTL.currentReadAdvancedCommandType == AdvancedCommandType.TwoPlaneRead
                    || _FTL.currentReadAdvancedCommandType == AdvancedCommandType.InterLeaveTwoPlane)
                {
                    for (var firstReq = sourceReadReqList.First; firstReq != null; firstReq = firstReq.Next)
                    {
                        bool OKforExecution = false;

                        if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                            OKforExecution = true;
                        else if (programEraseSuspensionEnabled)
                        {
                            if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Writing && !firstReq.Value.TargetFlashChip.Suspended)
                            {
                                if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _writeReasonableSuspensionTimeForRead)
                                {
                                    ulong writeWaitTime = firstReq.Value.TargetFlashChip.ExpectedFinishTime - firstReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest - _FTL.pageProgramLatency;
                                    ulong readWaitTime = XEngineFactory.XEngine.Time - firstReq.Value.IssueTime + 1;
                                    if (writeWaitTime < readWaitTime * _readToWritePrioritizationFactor)
                                        OKforExecution = true;
                                }
                            }
                            else if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Erasing && !firstReq.Value.TargetFlashChip.Suspended)
                            {
                                if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _eraseReasonableSuspensionTimeForRead)
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
                                    _FTL.FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                                    return true;
                                }
                            }
                        }
                    }
                }
                #endregion
                #region SearchForMultiDieCommand
                if (_FTL.currentReadAdvancedCommandType == AdvancedCommandType.Interleave
                    || _FTL.currentReadAdvancedCommandType == AdvancedCommandType.InterLeaveTwoPlane)
                {
                    for (var firstReq = sourceReadReqList.First; firstReq != null; firstReq = firstReq.Next)
                    {
                        bool OKforExecution = false;

                        if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                            OKforExecution = true;
                        else if (programEraseSuspensionEnabled)
                        {
                            if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Writing && !firstReq.Value.TargetFlashChip.Suspended)
                            {
                                if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _writeReasonableSuspensionTimeForRead)
                                {
                                    ulong writeWaitTime = firstReq.Value.TargetFlashChip.ExpectedFinishTime - firstReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest - _FTL.pageProgramLatency;
                                    ulong readWaitTime = XEngineFactory.XEngine.Time - firstReq.Value.IssueTime + 1;
                                    if (writeWaitTime < readWaitTime * _readToWritePrioritizationFactor)
                                        OKforExecution = true;
                                }
                            }
                            else if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Erasing && !firstReq.Value.TargetFlashChip.Suspended)
                            {
                                if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _eraseReasonableSuspensionTimeForRead)
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
                            for (var secondReq = firstReq.Next; secondReq != null; secondReq = secondReq.Next)
                                if ((firstReq.Value.TargetPageAddress.OverallFlashChipID == secondReq.Value.TargetPageAddress.OverallFlashChipID)
                                    &&
                                    (firstReq.Value.TargetPageAddress.DieID != secondReq.Value.TargetPageAddress.DieID))
                                {
                                    firstReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                    secondReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                    sourceReadReqList.Remove(firstReq);
                                    sourceReadReqList.Remove(secondReq);
                                    _FTL.FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                                    return true;
                                }
                        }
                    }
                }
                #endregion
            }
            else
            {
                #region SearchForMultiDieCommand
                if (_FTL.currentReadAdvancedCommandType == AdvancedCommandType.Interleave
                    || _FTL.currentReadAdvancedCommandType == AdvancedCommandType.InterLeaveTwoPlane)
                {
                    for (var firstReq = sourceReadReqList.First; firstReq != null; firstReq = firstReq.Next)
                    {

                        bool OKforExecution = false;

                        if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                            OKforExecution = true;
                        else if (programEraseSuspensionEnabled)
                        {
                            if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Writing && !firstReq.Value.TargetFlashChip.Suspended)
                            {
                                if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _writeReasonableSuspensionTimeForRead)
                                {
                                    ulong writeWaitTime = firstReq.Value.TargetFlashChip.ExpectedFinishTime - firstReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest - _FTL.pageProgramLatency;
                                    ulong readWaitTime = XEngineFactory.XEngine.Time - firstReq.Value.IssueTime + 1;
                                    if (writeWaitTime < readWaitTime * _readToWritePrioritizationFactor)
                                        OKforExecution = true;
                                }
                            }
                            else if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Erasing && !firstReq.Value.TargetFlashChip.Suspended)
                            {
                                if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _eraseReasonableSuspensionTimeForRead)
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
                            for (var secondReq = firstReq.Next; secondReq != null; secondReq = secondReq.Next)
                                if ((firstReq.Value.TargetPageAddress.OverallFlashChipID == secondReq.Value.TargetPageAddress.OverallFlashChipID)
                                    &&
                                    (firstReq.Value.TargetPageAddress.DieID != secondReq.Value.TargetPageAddress.DieID))
                                {
                                    firstReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                    secondReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                    sourceReadReqList.Remove(firstReq);
                                    sourceReadReqList.Remove(secondReq);
                                    _FTL.FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                                    return true;
                                }
                        }
                    }
                }
                #endregion
                #region SearchForTwoPlaneCommand
                if (_FTL.currentReadAdvancedCommandType == AdvancedCommandType.TwoPlaneRead
                    || _FTL.currentReadAdvancedCommandType == AdvancedCommandType.InterLeaveTwoPlane)
                {
                    for (var firstReq = sourceReadReqList.First; firstReq != null; firstReq = firstReq.Next)
                    {

                        bool OKforExecution = false;

                        if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                            OKforExecution = true;
                        else if (programEraseSuspensionEnabled)
                        {
                            if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Writing && !firstReq.Value.TargetFlashChip.Suspended)
                            {
                                if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _writeReasonableSuspensionTimeForRead)
                                {
                                    ulong writeWaitTime = firstReq.Value.TargetFlashChip.ExpectedFinishTime - firstReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest - _FTL.pageProgramLatency;
                                    ulong readWaitTime = XEngineFactory.XEngine.Time - firstReq.Value.IssueTime + 1;
                                    if (writeWaitTime < readWaitTime * _readToWritePrioritizationFactor)
                                        OKforExecution = true;
                                }
                            }
                            else if (firstReq.Value.TargetFlashChip.Status == FlashChipStatus.Erasing && !firstReq.Value.TargetFlashChip.Suspended)
                            {
                                if (firstReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _eraseReasonableSuspensionTimeForRead)
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
                                    _FTL.FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                                    return true;
                                }
                            }
                        }
                    }
                }
                #endregion
            }

            return false;
        }
        void PerformAdvancedProgramCMDForFlashChip(FlashChip targetChip, InternalWriteRequestLinkedList sourceWriteReqList)
        {
            InternalWriteRequestLinkedList executionList = new InternalWriteRequestLinkedList();
            //We should care about data transfer time for scheduling scheme. Otherwise, the advanced command execution will not
            //be efficient.
            ulong transferTime = 0;

            #region StaticDieStaticPlaneAllocation
            /*PlaneAllocationSchemeType.CWDP || PlaneAllocationSchemeType.CWPD || PlaneAllocationSchemeType.CDWP
             * || PlaneAllocationSchemeType.CDPW || PlaneAllocationSchemeType.CPWD || PlaneAllocationSchemeType.CPDW
             * || PlaneAllocationSchemeType.WCDP || PlaneAllocationSchemeType.WCPD || PlaneAllocationSchemeType.WDCP
             * || PlaneAllocationSchemeType.WDPC || PlaneAllocationSchemeType.WPCD || PlaneAllocationSchemeType.WPDC
             * || PlaneAllocationSchemeType.DCWP || PlaneAllocationSchemeType.DCPW || PlaneAllocationSchemeType.DWCP
             * || PlaneAllocationSchemeType.DWPC || PlaneAllocationSchemeType.DPCW || PlaneAllocationSchemeType.DPWC
             * || PlaneAllocationSchemeType.PCWD || PlaneAllocationSchemeType.PCDW || PlaneAllocationSchemeType.PWCD
             * || PlaneAllocationSchemeType.PWDC || PlaneAllocationSchemeType.PDCW || PlaneAllocationSchemeType.PDWC
             */
            /* PlaneAllocationSchemeType.DP || PlaneAllocationSchemeType.PD
                || PlaneAllocationSchemeType.CDP || PlaneAllocationSchemeType.CPD
                || PlaneAllocationSchemeType.WDP || PlaneAllocationSchemeType.WPD
                || PlaneAllocationSchemeType.DCP || PlaneAllocationSchemeType.DWP
                || PlaneAllocationSchemeType.DPC || PlaneAllocationSchemeType.DPW
                || PlaneAllocationSchemeType.PCD || PlaneAllocationSchemeType.PWD
                || PlaneAllocationSchemeType.PDC || PlaneAllocationSchemeType.PDW*/
            if (_FTL.isStaticScheme || (!_FTL.dynamicPlaneAssignment && !_FTL.dynamicDieAssignment))
            {
                InternalWriteRequest[,] candidateWriteReqs = new InternalWriteRequest[_FTL.DieNoPerChip, _FTL.PlaneNoPerDie];
                uint[] countOfWriteReqForDie = new uint[_FTL.DieNoPerChip];
                InternalWriteRequest firstReq = null;
                uint dieID, planeID;
                uint firstDieID = uint.MaxValue, firstPlaneID = uint.MaxValue;

                for (int i = 0; i < _FTL.DieNoPerChip; i++)
                {
                    countOfWriteReqForDie[i] = 0;
                    for (int j = 0; j < _FTL.PlaneNoPerDie; j++)
                        candidateWriteReqs[i, j] = null;
                }

                int reqCount = 0;
                for (var writeReq = sourceWriteReqList.First; writeReq != null && reqCount < _FTL.FlashChipExecutionCapacity; writeReq = writeReq.Next)
                    if (writeReq.Value.UpdateRead == null)
                    {
                        IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                        if (_FTL.dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                        {
                            if (candidateWriteReqs[targetAddress.DieID, targetAddress.PlaneID] == null)
                            {
                                targetAddress.ChannelID = targetChip.ChannelID;
                                targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                targetAddress.OverallFlashChipID = targetChip.OverallChipID;

                                reqCount++;
                                countOfWriteReqForDie[targetAddress.DieID]++;
                                candidateWriteReqs[targetAddress.DieID, targetAddress.PlaneID] = writeReq.Value;
                                if (firstReq == null)
                                {
                                    firstReq = writeReq.Value;
                                    firstDieID = targetAddress.DieID;
                                    firstPlaneID = targetAddress.PlaneID;
                                }
                            }
                        }
                    }

                if (reqCount == 0)
                    return;

                if (reqCount == 1)
                {
                    _FTL.AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                    return;
                }

                switch (_FTL.currentWriteAdvancedCommandType)
                {
                    #region InterleaveTwoPlane
                    case AdvancedCommandType.InterLeaveTwoPlane:
                        {
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

                            if (executionList.Count > 1)
                            {
                                if (multiPlaneFlag && multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                    }
                                }
                                else if (multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                    }
                                }
                                else
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                    }
                                }
                                _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                                _FTL.FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region Interleave
                    case AdvancedCommandType.Interleave:
                        {
                            executionList.Clear();
                            _FTL.AllocatePPNInPlane(firstReq);
                            executionList.AddLast(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);

                            for (uint dieCntr = 1; dieCntr < _FTL.DieNoPerChip; dieCntr++)
                            {
                                dieID = (firstDieID + dieCntr) % _FTL.DieNoPerChip;
                                if (countOfWriteReqForDie[dieID] > 0)
                                {
                                    InternalWriteRequest firstInternalReqOfDie;
                                    for (uint planeCntr = 0; planeCntr < _FTL.PlaneNoPerDie; planeCntr++)
                                    {
                                        firstInternalReqOfDie = candidateWriteReqs[dieID, planeCntr];
                                        if (firstInternalReqOfDie != null)
                                        {
                                            transferTime += _FTL.FCC.InterleaveProgramSetup + (firstInternalReqOfDie.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            if (transferTime >= _FTL.pageProgramLatency)
                                            {
                                                transferTime -= _FTL.FCC.InterleaveProgramSetup + (firstInternalReqOfDie.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                                continue;
                                            }

                                            _FTL.AllocatePPNInPlane(firstInternalReqOfDie);
                                            firstInternalReqOfDie.ExecutionType = InternalRequestExecutionType.Interleaved;
                                            executionList.AddLast(firstInternalReqOfDie);
                                            sourceWriteReqList.Remove(firstInternalReqOfDie.RelatedNodeInList);
                                            targetChip.Dies[dieID].CurrentActivePlaneID = (targetChip.Dies[dieID].CurrentActivePlaneID + 1) % _FTL.PlaneNoPerDie;
                                            break;
                                        }
                                    }
                                }
                            }//for (int dieCntr = 0; dieCntr < this.DieNoPerChip; dieCntr++)
                            if (executionList.Count > 1)
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Interleaved;
                                _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                _FTL.FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region TwoPlane
                    case AdvancedCommandType.TwoPlaneWrite:
                        {
                            executionList.Clear();
                            _FTL.AllocatePPNInPlane(firstReq);
                            executionList.AddLast(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);

                            if (countOfWriteReqForDie[firstDieID] > 1)
                            {
                                for (uint planeCntr = 1; planeCntr < _FTL.PlaneNoPerDie; planeCntr++)
                                {
                                    planeID = (firstPlaneID + planeCntr) % _FTL.PlaneNoPerDie;
                                    InternalWriteRequest currentReq = candidateWriteReqs[firstDieID, planeID];
                                    if (currentReq != null)
                                    {
                                        transferTime += _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        if (transferTime >= _FTL.pageProgramLatency)
                                        {
                                            transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            continue;
                                        }

                                        if (executionList.Count == 1)
                                        {
                                            if (_FTL.FindLevelPage(executionList.First.Value, currentReq))
                                            {
                                                currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                                executionList.AddLast(currentReq);
                                                sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                            }
                                            else
                                                transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        }
                                        else
                                        {
                                            if (_FTL.FindLevelPageStrict(targetChip, executionList.First.Value, currentReq))
                                            {
                                                currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                                executionList.AddLast(currentReq);
                                                sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                            }
                                            else
                                                transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        }
                                    }
                                }//for (int planeCntr = 0; planeCntr < this.PlaneNoPerDie; planeCntr++)
                                if (executionList.Count > 1)
                                {
                                    firstReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                    _FTL.FCC.SendAdvCommandToChipWR(executionList);
                                    return;
                                }
                            }

                            firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                            _FTL.FCC.SendSimpleCommandToChip(firstReq);
                            return;
                        }
                    #endregion
                    default:
                        throw new Exception("Unexpected invokation for advanced command execution.");
                }

            }// if (deterministicFlashChipAssignment)
            #endregion
            #region StaticDieDynamicPlaneAllocation
            /*     planeAllocationScheme == PlaneAllocationSchemeType.CWD
                || planeAllocationScheme == PlaneAllocationSchemeType.CD || planeAllocationScheme == PlaneAllocationSchemeType.CDW
                || planeAllocationScheme == PlaneAllocationSchemeType.WCD
                || planeAllocationScheme == PlaneAllocationSchemeType.WD || planeAllocationScheme == PlaneAllocationSchemeType.WDC
                || planeAllocationScheme == PlaneAllocationSchemeType.D
                || planeAllocationScheme == PlaneAllocationSchemeType.DC || planeAllocationScheme == PlaneAllocationSchemeType.DCW
                || planeAllocationScheme == PlaneAllocationSchemeType.DW || planeAllocationScheme == PlaneAllocationSchemeType.DWC*/
            else if (!_FTL.dynamicDieAssignment && _FTL.dynamicPlaneAssignment)
            {
                InternalWriteRequest[,] candidateWriteReqs = new InternalWriteRequest[_FTL.DieNoPerChip, _FTL.PlaneNoPerDie];
                uint[] countOfWriteReqForDie = new uint[_FTL.DieNoPerChip];
                InternalWriteRequest firstReq = null;
                uint dieID, firstDieID = uint.MaxValue;
                for (int i = 0; i < _FTL.DieNoPerChip; i++)
                {
                    countOfWriteReqForDie[i] = 0;
                    for (int j = 0; j < _FTL.PlaneNoPerDie; j++)
                    {
                        targetChip.Dies[i].Planes[j].CommandAssigned = false;
                        candidateWriteReqs[i, j] = null;
                    }
                }

                int reqCount = 0;
                for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                    if (writeReq.Value.UpdateRead == null)
                    {
                        IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                        if (countOfWriteReqForDie[targetAddress.DieID] < _FTL.PlaneNoPerDie)
                        {
                            if (_FTL.dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                            {
                                writeReq.Value.TargetFlashChip = targetChip;
                                targetAddress.ChannelID = targetChip.ChannelID;
                                targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                targetAddress.OverallFlashChipID = targetChip.OverallChipID;

                                reqCount++;
                                candidateWriteReqs[targetAddress.DieID, countOfWriteReqForDie[targetAddress.DieID]] = writeReq.Value;
                                countOfWriteReqForDie[targetAddress.DieID]++;
                                if (firstReq == null)
                                {
                                    firstReq = writeReq.Value;
                                    firstDieID = targetAddress.DieID;
                                }
                            }
                        }
                    }


                if (reqCount == 0)
                    return;

                if (reqCount == 1)
                {
                    _FTL.AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                    return;
                }

                switch (_FTL.currentWriteAdvancedCommandType)
                {
                    #region InterleaveTwoPlane
                    case AdvancedCommandType.InterLeaveTwoPlane:
                        {
                            bool multiPlaneFlag = false, multiDieFlag = false;
                            uint lastDieID = uint.MaxValue;
                            for (uint dieCntr = 0; dieCntr < _FTL.DieNoPerChip; dieCntr++)
                            {
                                dieID = (firstDieID + dieCntr) % _FTL.DieNoPerChip;
                                if (countOfWriteReqForDie[dieID] > 0)
                                {
                                    InternalWriteRequest firstInternalReqOfDie = null;
                                    for (int planeCntr = 0; planeCntr < countOfWriteReqForDie[dieID]; planeCntr++)
                                    {
                                        InternalWriteRequest currentReq = candidateWriteReqs[dieID, planeCntr];
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
                                            currentReq.TargetPageAddress.PlaneID = targetChip.Dies[dieID].CurrentActivePlaneID;
                                            targetChip.Dies[dieID].Planes[currentReq.TargetPageAddress.PlaneID].CommandAssigned = true;
                                            targetChip.Dies[dieID].CurrentActivePlaneID = (currentReq.TargetPageAddress.PlaneID + 1) % _FTL.PlaneNoPerDie;
                                            firstInternalReqOfDie = currentReq;
                                            _FTL.AllocatePPNInPlane(currentReq);
                                            executionList.AddLast(currentReq);
                                        }
                                        else
                                        {
                                            transferTime += _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            if (transferTime >= _FTL.pageProgramLatency)
                                            {
                                                transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                                continue;
                                            }
                                            if (_FTL.FindLevelPageStrict(targetChip, firstInternalReqOfDie, currentReq))
                                            {
                                                multiPlaneFlag = true;
                                                executionList.AddLast(currentReq);
                                            }
                                            else
                                                transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        }
                                    }
                                }
                            }

                            if (executionList.Count > 1)
                            {
                                if (multiPlaneFlag && multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    }
                                }
                                else if (multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    }
                                }
                                else
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    }
                                }

                                _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                                _FTL.FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region Interleave
                    case AdvancedCommandType.Interleave:
                        {
                            executionList.Clear();
                            firstReq.TargetPageAddress.PlaneID = targetChip.Dies[firstReq.TargetPageAddress.DieID].CurrentActivePlaneID;
                            targetChip.Dies[firstReq.TargetPageAddress.DieID].CurrentActivePlaneID = (firstReq.TargetPageAddress.PlaneID + 1) % _FTL.PlaneNoPerDie;
                            _FTL.AllocatePPNInPlane(firstReq);
                            executionList.AddLast(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                            for (uint dieCntr = 1; dieCntr < _FTL.DieNoPerChip; dieCntr++)
                            {
                                dieID = (firstDieID + dieCntr) % _FTL.DieNoPerChip;
                                if (countOfWriteReqForDie[dieID] > 0)
                                {
                                    for (uint planeCntr = 0; planeCntr < countOfWriteReqForDie[dieID]; planeCntr++)
                                    {
                                        InternalWriteRequest firstInternalReqOfDie = candidateWriteReqs[dieID, planeCntr];
                                        transferTime += _FTL.FCC.InterleaveProgramSetup + (firstInternalReqOfDie.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        if (transferTime >= _FTL.pageProgramLatency)
                                        {
                                            transferTime -= _FTL.FCC.InterleaveProgramSetup + (firstInternalReqOfDie.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            continue;
                                        }

                                        firstInternalReqOfDie.TargetPageAddress.PlaneID = targetChip.Dies[dieID].CurrentActivePlaneID;
                                        targetChip.Dies[dieID].CurrentActivePlaneID = (firstInternalReqOfDie.TargetPageAddress.PlaneID + 1) % _FTL.PlaneNoPerDie;
                                        _FTL.AllocatePPNInPlane(firstInternalReqOfDie);
                                        firstInternalReqOfDie.ExecutionType = InternalRequestExecutionType.Interleaved;
                                        executionList.AddLast(firstInternalReqOfDie);
                                        sourceWriteReqList.Remove(firstInternalReqOfDie.RelatedNodeInList);
                                        break;
                                    }
                                }
                            }//for (int dieCntr = 0; dieCntr < this.DieNoPerChip; dieCntr++)
                            if (executionList.Count > 1)
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Interleaved;
                                _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                _FTL.FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region TwoPlane
                    case AdvancedCommandType.TwoPlaneWrite:
                        {
                            if (countOfWriteReqForDie[firstDieID] > 1)
                            {
                                executionList.Clear();
                                firstReq.TargetPageAddress.PlaneID = targetChip.Dies[firstReq.TargetPageAddress.DieID].CurrentActivePlaneID;
                                targetChip.Dies[firstReq.TargetPageAddress.DieID].CurrentActivePlaneID = (firstReq.TargetPageAddress.PlaneID + 1) % _FTL.PlaneNoPerDie;
                                _FTL.AllocatePPNInPlane(firstReq);
                                executionList.AddLast(firstReq);
                                sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                                for (int planeCntr = 1; planeCntr < countOfWriteReqForDie[firstDieID]; planeCntr++)
                                {
                                    InternalWriteRequest currentReq = candidateWriteReqs[firstDieID, planeCntr];

                                    transferTime += _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                    if (transferTime >= _FTL.pageProgramLatency)
                                    {
                                        transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        continue;
                                    }
                                    if (executionList.Count == 1)
                                    {
                                        if (_FTL.FindLevelPage(executionList.First.Value, currentReq))
                                        {
                                            currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                            executionList.AddLast(currentReq);
                                            sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                        }
                                        else
                                            transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                    }
                                    else if (_FTL.FindLevelPageStrict(targetChip, executionList.First.Value, currentReq))
                                    {
                                        currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                        executionList.AddLast(currentReq);
                                        sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                    }
                                    else
                                        transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                }//for (int planeCntr = 0; planeCntr < this.PlaneNoPerDie; planeCntr++)
                                if (executionList.Count > 1)
                                {
                                    firstReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                    _FTL.FCC.SendAdvCommandToChipWR(executionList);
                                    return;
                                }
                                else
                                {
                                    firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                    _FTL.FCC.SendSimpleCommandToChip(firstReq);
                                    return;
                                }
                            }

                            _FTL.AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                            return;
                        }
                    #endregion
                    default:
                        throw new Exception("Unexpected invokation for advanced command execution.");
                }
            }
            #endregion
            #region StaticPlaneDynamicDieAllocation
            /*     planeAllocationScheme == PlaneAllocationSchemeType.CWP
                || planeAllocationScheme == PlaneAllocationSchemeType.CP || planeAllocationScheme == PlaneAllocationSchemeType.CPW
                || planeAllocationScheme == PlaneAllocationSchemeType.WCP
                || planeAllocationScheme == PlaneAllocationSchemeType.WP || planeAllocationScheme == PlaneAllocationSchemeType.WPC
                || planeAllocationScheme == PlaneAllocationSchemeType.P
                || planeAllocationScheme == PlaneAllocationSchemeType.PC || planeAllocationScheme == PlaneAllocationSchemeType.PCW
                || planeAllocationScheme == PlaneAllocationSchemeType.PW || planeAllocationScheme == PlaneAllocationSchemeType.PWC*/
            else if (!_FTL.dynamicPlaneAssignment && _FTL.dynamicDieAssignment)
            {

                InternalWriteRequest firstReq = null;

                switch (_FTL.currentWriteAdvancedCommandType)
                {
                    #region InterleaveTwoPlane
                    case AdvancedCommandType.InterLeaveTwoPlane:
                        {
                            bool multiPlaneFlag = false, multiDieFlag = false;
                            uint lastDieID = uint.MaxValue;
                            uint firstPlaneID = 0;
                            //InternalRequest[,] candidateWriteReqs = new InternalRequest[this.PlaneNoPerDie, this.DieNoPerChip];
                            List<InternalWriteRequest>[] candidateWriteReqs = new List<InternalWriteRequest>[_FTL.PlaneNoPerDie];
                            for (int i = 0; i < _FTL.PlaneNoPerDie; i++)
                                candidateWriteReqs[i] = new List<InternalWriteRequest>((int)_FTL.DieNoPerChip);

                            int reqCount = 0;
                            for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                                if (writeReq.Value.UpdateRead == null)
                                {
                                    IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                                    if (_FTL.dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                                    {
                                        targetAddress.ChannelID = targetChip.ChannelID;
                                        targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                        targetAddress.OverallFlashChipID = targetChip.OverallChipID;

                                        reqCount++;
                                        candidateWriteReqs[targetAddress.PlaneID].Add(writeReq.Value);
                                        if (firstReq == null)
                                        {
                                            firstReq = writeReq.Value;
                                            firstPlaneID = firstReq.TargetPageAddress.PlaneID;
                                        }
                                    }
                                }


                            if (reqCount == 0)
                                return;

                            if (reqCount == 1)
                            {
                                _FTL.AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                                return;
                            }
                            executionList.Clear();

                            for (uint dieCntr = 0; dieCntr < _FTL.DieNoPerChip; dieCntr++)
                            {
                                InternalWriteRequest firstInternalReqOfDie = null;
                                for (uint planeCntr = 0; planeCntr < _FTL.PlaneNoPerDie && reqCount > 0; planeCntr++)
                                {
                                    uint planeID = (firstPlaneID + planeCntr) % _FTL.PlaneNoPerDie;
                                    if (candidateWriteReqs[planeID].Count > 0)
                                    {
                                        InternalWriteRequest currentReq = candidateWriteReqs[planeID][0];
                                        if (firstInternalReqOfDie == null)
                                        {
                                            if (lastDieID != uint.MaxValue)
                                            {
                                                transferTime += _FTL.FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                                if (transferTime >= _FTL.pageProgramLatency)
                                                {
                                                    transferTime -= _FTL.FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                                    currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                                    continue;
                                                }
                                                multiDieFlag = true;
                                            }
                                            currentReq.TargetPageAddress.DieID = targetChip.CurrentActiveDieID;
                                            lastDieID = targetChip.CurrentActiveDieID;
                                            firstInternalReqOfDie = currentReq;
                                            _FTL.AllocatePPNInPlane(currentReq);
                                            executionList.AddLast(currentReq);
                                            candidateWriteReqs[planeID].RemoveAt(0);
                                            reqCount--;
                                        }
                                        else
                                        {
                                            transferTime += _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            if (transferTime >= _FTL.pageProgramLatency)
                                            {
                                                transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                                currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                                continue;
                                            }
                                            if (_FTL.FindLevelPageStrict(targetChip, firstInternalReqOfDie, currentReq))
                                            {
                                                multiPlaneFlag = true;
                                                executionList.AddLast(currentReq);
                                                candidateWriteReqs[planeID].RemoveAt(0);
                                                reqCount--;
                                            }
                                            else
                                            {
                                                transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                                currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                            }
                                        }
                                    }
                                }
                                targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % _FTL.DieNoPerChip;
                                if (reqCount == 0)
                                    break;
                            }

                            if (executionList.Count > 1)
                            {
                                if (multiPlaneFlag && multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                    }
                                }
                                else if (multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                    }
                                }
                                else
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                    }
                                }

                                _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                _FTL.FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region Interleave
                    case AdvancedCommandType.Interleave:
                        {
                            bool firstdie = true;
                            for (var writeReq = sourceWriteReqList.First; writeReq != null && executionList.Count < _FTL.DieNoPerChip; writeReq = writeReq.Next)
                                if (writeReq.Value.UpdateRead == null)
                                {
                                    IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                                    if (_FTL.dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                                    {
                                        if (firstdie)
                                            firstdie = false;
                                        else
                                        {
                                            transferTime += _FTL.FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            if (transferTime >= _FTL.pageProgramLatency)
                                            {
                                                transferTime -= _FTL.FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                                continue;
                                            }
                                        }

                                        targetAddress.ChannelID = targetChip.ChannelID;
                                        targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                        targetAddress.OverallFlashChipID = targetChip.OverallChipID;
                                        targetAddress.DieID = targetChip.CurrentActiveDieID;
                                        targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % _FTL.DieNoPerChip;
                                        _FTL.AllocatePPNInPlane(writeReq.Value);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                        executionList.AddLast(writeReq.Value);
                                    }
                                }

                            if (executionList.Count == 0)
                                return;
                            if (executionList.Count == 1)
                            {
                                executionList.First.Value.ExecutionType = InternalRequestExecutionType.Simple;
                                sourceWriteReqList.Remove(executionList.First.Value.RelatedNodeInList);
                                _FTL.FCC.SendSimpleCommandToChip(executionList.First.Value);
                                return;
                            }
                            for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                            _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            return;
                        }
                    #endregion
                    #region TwoPlane
                    case AdvancedCommandType.TwoPlaneWrite:
                        {
                            InternalWriteRequestLinkedList[] candidateWriteReqs = new InternalWriteRequestLinkedList[_FTL.PlaneNoPerDie];
                            for (int i = 0; i < _FTL.PlaneNoPerDie; i++)
                                candidateWriteReqs[i] = new InternalWriteRequestLinkedList();

                            int reqCount = 0;
                            for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                                if (writeReq.Value.UpdateRead == null)
                                {
                                    IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                                    if (_FTL.dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                                    {
                                        targetAddress.ChannelID = targetChip.ChannelID;
                                        targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                        targetAddress.OverallFlashChipID = targetChip.OverallChipID;

                                        reqCount++;
                                        candidateWriteReqs[targetAddress.PlaneID].AddLast(writeReq.Value);
                                        if (firstReq == null)
                                            firstReq = writeReq.Value;
                                    }
                                }


                            if (reqCount == 0)
                                return;

                            if (reqCount == 1)
                            {
                                _FTL.AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                                return;
                            }
                            executionList.Clear();
                            firstReq.TargetPageAddress.DieID = targetChip.CurrentActiveDieID;
                            targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % _FTL.DieNoPerChip;
                            _FTL.AllocatePPNInPlane(firstReq);
                            executionList.AddLast(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);

                            for (uint planeCntr = 1; planeCntr < _FTL.PlaneNoPerDie; planeCntr++)
                            {
                                uint planeID = (firstReq.TargetPageAddress.PlaneID + planeCntr) % _FTL.PlaneNoPerDie;
                                for (var writeReqItr = candidateWriteReqs[planeID].First; writeReqItr != null; writeReqItr = writeReqItr.Next)
                                {
                                    InternalWriteRequest currentReq = writeReqItr.Value;

                                    if (executionList.Count == 1)
                                    {
                                        transferTime += _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        if (transferTime >= _FTL.pageProgramLatency)
                                        {
                                            transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            continue;
                                        }
                                        currentReq.TargetPageAddress.DieID = firstReq.TargetPageAddress.DieID;
                                        if (_FTL.FindLevelPage(firstReq, currentReq))
                                        {
                                            currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                            executionList.AddLast(currentReq);
                                            sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                            break;
                                        }
                                        else
                                        {
                                            transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                        }
                                    }
                                    else
                                    {
                                        transferTime += _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        if (transferTime >= _FTL.pageProgramLatency)
                                        {
                                            transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            continue;
                                        }
                                        currentReq.TargetPageAddress.DieID = firstReq.TargetPageAddress.DieID;
                                        if (_FTL.FindLevelPageStrict(targetChip, firstReq, currentReq))
                                        {
                                            currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                            executionList.AddLast(currentReq);
                                            sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                            break;
                                        }
                                        else
                                        {
                                            transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                        }
                                    }
                                }
                            }
                            if (executionList.Count > 1)
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                _FTL.FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    default:
                        throw new Exception("Unexpected invokation for advanced command execution.");
                }
            }
            #endregion
            #region JustStaticCWAllocation
            /*PlaneAllocationSchemeType.F
            || PlaneAllocationSchemeType.C  || PlaneAllocationSchemeType.W
            || PlaneAllocationSchemeType.CW || PlaneAllocationSchemeType.WC*/
            else if (_FTL.dynamicDieAssignment && _FTL.dynamicPlaneAssignment)
            {
                InternalWriteRequest firstReq = null;
                for (var writeReq = sourceWriteReqList.First; (writeReq != null) && (executionList.Count < _FTL.FlashChipExecutionCapacity); writeReq = writeReq.Next)
                    if (writeReq.Value.UpdateRead == null)
                        if (_FTL.dynamicWayAssignment || (writeReq.Value.TargetPageAddress.LocalFlashChipID == targetChip.LocalChipID))
                        {
                            writeReq.Value.TargetPageAddress.ChannelID = targetChip.ChannelID;
                            writeReq.Value.TargetPageAddress.LocalFlashChipID = targetChip.LocalChipID;
                            writeReq.Value.TargetPageAddress.OverallFlashChipID = targetChip.OverallChipID;
                            executionList.AddLast(writeReq.Value);
                            if (firstReq == null)
                                firstReq = writeReq.Value;
                        }

                if (executionList.Count == 0)
                    return;

                if (executionList.Count == 1)
                {
                    _FTL.AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, executionList.First.Value); //there was only one request
                    return;
                }

                IntegerPageAddress targetAddress = null;
                switch (_FTL.currentWriteAdvancedCommandType)
                {
                    #region InterleaveTwoPlane
                    case AdvancedCommandType.InterLeaveTwoPlane:
                        {
                            bool multiPlaneFlag = false, multiDieFlag = false;
                            int currentReqCntr = 0;
                            InternalWriteRequest[] candidateWriteReqsAsList = new InternalWriteRequest[_FTL.FlashChipExecutionCapacity];

                            if (_FTL.GarbageCollector.TB_Enabled)
                            {
                                uint startDieID = targetChip.CurrentActiveDieID;
                                uint startPlaneID = targetChip.Dies[targetChip.CurrentActiveDieID].CurrentActivePlaneID;
                                uint assignedDieCount = 0;
                                for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next, currentReqCntr++)
                                {
                                    if (startDieID != targetChip.CurrentActiveDieID)
                                        multiDieFlag = true;
                                    if (startPlaneID != targetChip.Dies[targetChip.CurrentActiveDieID].CurrentActivePlaneID)
                                        multiPlaneFlag = true;
                                    targetAddress = writeReq.Value.TargetPageAddress;
                                    targetAddress.DieID = targetChip.CurrentActiveDieID;
                                    targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                    _FTL.AllocatePPNInPlane(writeReq.Value);
                                    targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % _FTL.PlaneNoPerDie;
                                    if (targetAddress.PlaneID == (_FTL.PlaneNoPerDie - 1))
                                    {
                                        targetChip.CurrentActiveDieID = (targetAddress.DieID + 1) % _FTL.DieNoPerChip;
                                        startPlaneID = targetChip.Dies[targetChip.CurrentActiveDieID].CurrentActivePlaneID;
                                        assignedDieCount++;
                                    }

                                    candidateWriteReqsAsList[targetAddress.DieID * _FTL.PlaneNoPerDie + targetAddress.PlaneID] = writeReq.Value;
                                    sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    if (assignedDieCount == _FTL.DieNoPerChip)
                                        break;
                                }
                            }
                            else
                                for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next, currentReqCntr++)
                                {
                                    candidateWriteReqsAsList[currentReqCntr] = writeReq.Value;
                                    targetAddress = writeReq.Value.TargetPageAddress;

                                    if (currentReqCntr < _FTL.DieNoPerChip)
                                    {
                                        if (currentReqCntr > 0)
                                        {
                                            transferTime += _FTL.FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            if (transferTime >= _FTL.pageProgramLatency)
                                            {
                                                transferTime -= _FTL.FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                                candidateWriteReqsAsList[currentReqCntr] = null;
                                                currentReqCntr--;
                                                continue;
                                            }
                                            multiDieFlag = true;
                                        }
                                        targetAddress.DieID = targetChip.CurrentActiveDieID;
                                        targetChip.CurrentActiveDieID = (targetAddress.DieID + 1) % _FTL.DieNoPerChip;
                                        targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                        targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % _FTL.PlaneNoPerDie;
                                        targetChip.Dies[targetAddress.DieID].Planes[targetAddress.PlaneID].CommandAssigned = true;
                                        _FTL.AllocatePPNInPlane(writeReq.Value);
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    }
                                    else
                                    {
                                        transferTime += _FTL.FCC.MultiPlaneProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                        if (transferTime >= _FTL.pageProgramLatency)
                                        {
                                            transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            candidateWriteReqsAsList[currentReqCntr] = null;
                                            continue;
                                        }

                                        if (_FTL.FindLevelPageStrict(targetChip, candidateWriteReqsAsList[currentReqCntr % (int)_FTL.DieNoPerChip], candidateWriteReqsAsList[currentReqCntr]))
                                        {
                                            multiPlaneFlag = true;
                                            sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        }
                                        else
                                        {
                                            transferTime -= _FTL.FCC.MultiPlaneProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                            candidateWriteReqsAsList[currentReqCntr] = null;
                                        }
                                    }
                                }
                            #region PlanePrioritized
                            /* Plane-level allocation is prioritized over die level allocation
                             * {
                                uint assignedDieCount = 0;
                                InternalRequest myfirstReq = null;
                                for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next, currentReqCntr++)
                                {
                                    targetAddress = writeReq.Value.TargetPageAddress;
                                    targetAddress.DieID = targetChip.CurrentActiveDieID;
                                    if (myfirstReq == null)
                                    {
                                        targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                        AllocatePPNInPlane(writeReq.Value);
                                        targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % this.PlaneNoPerDie;
                                        myfirstReq = writeReq.Value;
                                    }
                                    else
                                    {
                                        if (FindLevelPageStrict(targetChip, myfirstReq, writeReq.Value))
                                            multiPlaneFlag = true;
                                        else {
                                            writeReq.Value.TargetPageAddress.DieID = uint.MaxValue;
                                            writeReq.Value.TargetPageAddress.PlaneID = uint.MaxValue;
                                            assignedDieCount++;
                                            targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % this.DieNoPerChip;
                                            if (assignedDieCount == DieNoPerChip)
                                                break;
                                            multiDieFlag = true;
                                            targetAddress.DieID = targetChip.CurrentActiveDieID;
                                            targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                            AllocatePPNInPlane(writeReq.Value);
                                            targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % this.PlaneNoPerDie;
                                            myfirstReq = writeReq.Value;
                                        }
                                    }

                                    candidateWriteReqsAsList[writeReq.Value.TargetPageAddress.DieID * this.PlaneNoPerDie + writeReq.Value.TargetPageAddress.PlaneID] = writeReq.Value;
                                    sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                }
                            }*/
                            #endregion

                            executionList.Clear();
                            if (multiDieFlag && multiPlaneFlag)
                            {
                                if (_FTL.GarbageCollector.TB_Enabled)
                                {
                                    for (uint dieCntr = 0; dieCntr < _FTL.DieNoPerChip; dieCntr++)
                                        for (uint planeCntr = 0; planeCntr < _FTL.PlaneNoPerDie; planeCntr++)
                                            if (candidateWriteReqsAsList[dieCntr * _FTL.PlaneNoPerDie + planeCntr] != null)
                                            {
                                                candidateWriteReqsAsList[dieCntr * _FTL.PlaneNoPerDie + planeCntr].ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                                executionList.AddLast(candidateWriteReqsAsList[dieCntr * _FTL.PlaneNoPerDie + planeCntr]);
                                            }
                                }
                                else
                                {
                                    //We start from last die since it probably has lower number of requests.
                                    //Hence the impact of data transmission on the overall response time is declined.
                                    for (uint dieCntr = 0; dieCntr < _FTL.DieNoPerChip; dieCntr++)
                                    {
                                        candidateWriteReqsAsList[dieCntr].ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                        executionList.AddLast(candidateWriteReqsAsList[dieCntr]);
                                        for (uint reqCntr = _FTL.DieNoPerChip; reqCntr < candidateWriteReqsAsList.Length; reqCntr++)
                                            if (candidateWriteReqsAsList[reqCntr] != null)
                                                if (candidateWriteReqsAsList[reqCntr].TargetPageAddress.DieID == candidateWriteReqsAsList[dieCntr].TargetPageAddress.DieID)
                                                {
                                                    candidateWriteReqsAsList[reqCntr].ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                                    executionList.AddLast(candidateWriteReqsAsList[reqCntr]);
                                                }
                                    }
                                }
                                _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else if (multiPlaneFlag)
                            {
                                for (int reqCntr = 0; reqCntr < candidateWriteReqsAsList.Length; reqCntr++)
                                {
                                    if (candidateWriteReqsAsList[reqCntr] != null)
                                    {
                                        candidateWriteReqsAsList[reqCntr].ExecutionType = InternalRequestExecutionType.Multiplane;
                                        executionList.AddLast(candidateWriteReqsAsList[reqCntr]);
                                    }
                                }
                                _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else if (multiDieFlag)
                            {
                                for (int reqCntr = 0; reqCntr < candidateWriteReqsAsList.Length; reqCntr++)
                                {
                                    if (candidateWriteReqsAsList[reqCntr] != null)
                                    {
                                        candidateWriteReqsAsList[reqCntr].ExecutionType = InternalRequestExecutionType.Interleaved;
                                        executionList.AddLast(candidateWriteReqsAsList[reqCntr]);
                                    }
                                }
                                _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                for (int i = 0; i < candidateWriteReqsAsList.Length; i++)
                                    if (candidateWriteReqsAsList[i] != null)
                                    {
                                        candidateWriteReqsAsList[i].ExecutionType = InternalRequestExecutionType.Simple;
                                        _FTL.FCC.SendSimpleCommandToChip(candidateWriteReqsAsList[i]);
                                    }
                            }

                            return;
                        }
                    #endregion
                    #region Interleave
                    case AdvancedCommandType.Interleave:
                        {
                            InternalWriteRequestLinkedList newExecutionList = new InternalWriteRequestLinkedList();
                            targetAddress = firstReq.TargetPageAddress;
                            targetAddress.DieID = targetChip.CurrentActiveDieID;
                            targetChip.CurrentActiveDieID = (targetAddress.DieID + 1) % _FTL.DieNoPerChip;
                            targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                            targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % _FTL.PlaneNoPerDie;
                            _FTL.AllocatePPNInPlane(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                            newExecutionList.AddLast(firstReq);
                            for (var writeReq = executionList.First.Next; writeReq != null; writeReq = writeReq.Next)
                            {
                                transferTime += _FTL.FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                if (transferTime >= _FTL.pageProgramLatency)
                                {
                                    transferTime -= _FTL.FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                    continue;
                                }
                                writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                targetAddress = writeReq.Value.TargetPageAddress;
                                targetAddress.DieID = targetChip.CurrentActiveDieID;
                                targetChip.CurrentActiveDieID = (targetAddress.DieID + 1) % _FTL.DieNoPerChip;
                                targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % _FTL.PlaneNoPerDie;
                                _FTL.AllocatePPNInPlane(writeReq.Value);
                                sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                newExecutionList.AddLast(writeReq.Value);
                            }
                            if (newExecutionList.Count > 1)
                            {
                                newExecutionList.First.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                _FTL.FCC.SendAdvCommandToChipWR(newExecutionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                _FTL.FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region TwoPlane
                    case AdvancedCommandType.TwoPlaneWrite:
                        {
                            for (var writeReq = executionList.First.Next; writeReq != null; writeReq = writeReq.Next)
                            {
                                transferTime += _FTL.FCC.MultiPlaneProgramSetup + (writeReq.Value.BodyTransferCycles * _FTL.FCC.WriteTransferCycleTime);
                                if (writeReq == executionList.First.Next)
                                {
                                    if (transferTime >= _FTL.pageProgramLatency)
                                    {
                                        _FTL.AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                                        return;
                                    }
                                    firstReq.TargetPageAddress.DieID = targetChip.CurrentActiveDieID;
                                    writeReq.Value.TargetPageAddress.DieID = targetChip.CurrentActiveDieID;
                                    if (!_FTL.FindLevelPage(firstReq, writeReq.Value))
                                    {
                                        firstReq.TargetPageAddress.DieID = uint.MaxValue;
                                        writeReq.Value.TargetPageAddress.DieID = uint.MaxValue;
                                        _FTL.AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                                        return;
                                    }
                                }
                                else
                                {
                                    if (transferTime >= _FTL.pageProgramLatency)
                                    {
                                        while (executionList.Last != writeReq)
                                            executionList.RemoveLast();
                                        executionList.RemoveLast();
                                        break;
                                    }
                                    if (!_FTL.FindLevelPageStrict(targetChip, firstReq, writeReq.Value))
                                    {
                                        while (executionList.Last != writeReq)
                                            executionList.RemoveLast();
                                        executionList.RemoveLast();
                                        break;
                                    }
                                }
                                writeReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                            }//for (var writeReq = executionList.First.Next; writeReq != null; writeReq = writeReq.Next)
                            targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % _FTL.DieNoPerChip;

                            executionList.First.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                            _FTL.FCC.SendAdvCommandToChipWR(executionList);
                            return;
                        }
                    #endregion
                    default:
                        throw new Exception("Unexpected invokation for advanced command execution.");
                }

            }//else (Dynamic Allocation)
            #endregion

        }
        bool ServiceReadCommands(uint channelID)
        {
            BusChannelSprinkler targetChannel = _FTL.ChannelInfos[channelID] as BusChannelSprinkler;
            InternalReadRequestLinkedList sourceReadReqs = targetChannel.WaitingInternalReadReqs;
            //Check is there any request to be serviced?
            if (sourceReadReqs.Count == 0)
                return false;

            //To preserve timing priority between reads and writes, we first check for the existence of write requests issued sooner than reads
            if (targetChannel.WaitingInternalWriteReqs.Count > 0)
            {
                if (programEraseSuspensionEnabled)
                {
                    ulong writeWaitTime = XEngineFactory.XEngine.Time - targetChannel.WaitingInternalWriteReqs.First.Value.IssueTime;
                    ulong readWaitTime = XEngineFactory.XEngine.Time - sourceReadReqs.First.Value.IssueTime;
                    if (writeWaitTime > readWaitTime * _readToWritePrioritizationFactor
                        && targetChannel.WaitingInternalWriteReqs.First.Value.UpdateRead == null)
                        return false;
                }
                else
                {
                    if (targetChannel.WaitingInternalWriteReqs.First.Value.IssueTime < sourceReadReqs.First.Value.IssueTime
                        && targetChannel.WaitingInternalWriteReqs.First.Value.UpdateRead == null)
                        return false;
                }
            }

            if (sourceReadReqs.Count > 1)
                if (PerformAdvancedReadCommand(sourceReadReqs))
                    return true;

            /*Normal command execution (i.e. no advanced command supported)*/
            for (var readReq = sourceReadReqs.First; readReq != null; readReq = readReq.Next)
            {
                bool OKforExecution = false;

                if (readReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                    OKforExecution = true;
                else if (programEraseSuspensionEnabled)
                {
                    if (readReq.Value.TargetFlashChip.Status == FlashChipStatus.Writing && !readReq.Value.TargetFlashChip.Suspended)
                    {
                        if (readReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _writeReasonableSuspensionTimeForRead)
                        {
                            ulong writeWaitTime = readReq.Value.TargetFlashChip.ExpectedFinishTime - readReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest - _FTL.pageProgramLatency;
                            ulong readWaitTime = XEngineFactory.XEngine.Time - readReq.Value.IssueTime + 1;
                            if (writeWaitTime < readWaitTime * _readToWritePrioritizationFactor)
                                OKforExecution = true;
                        }
                    }
                    else if (readReq.Value.TargetFlashChip.Status == FlashChipStatus.Erasing && !readReq.Value.TargetFlashChip.Suspended)
                    {
                        if (readReq.Value.TargetFlashChip.ExpectedFinishTime - XEngineFactory.XEngine.Time > _eraseReasonableSuspensionTimeForRead)
                        {
                            //ulong eraseWaitTime = XEngineFactory.XEngine.Time - firstReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest;
                            ulong eraseWaitTime = readReq.Value.TargetFlashChip.ExpectedFinishTime - readReq.Value.TargetFlashChip.IssueTimeOfExecutingRequest - _FTL.eraseLatency;
                            ulong readWaitTime = XEngineFactory.XEngine.Time - readReq.Value.IssueTime + 1;
                            if (eraseWaitTime < readWaitTime * _readToWritePrioritizationFactor)
                                OKforExecution = true;
                        }
                    }
                }
                if (OKforExecution)
                {
                    sourceReadReqs.Remove(readReq);
                    _FTL.FCC.SendSimpleCommandToChip(readReq.Value);
                    return true;
                }
            }
            return false;
        }
        void ServiceWriteCommands(uint channelID)
        {
            BusChannelSprinkler targetChannel = (_FTL.ChannelInfos[channelID] as BusChannelSprinkler);
            InternalWriteRequestLinkedList sourceWriteReqList = targetChannel.WaitingInternalWriteReqs;
            //Check is there any request to be serviced?
            if (sourceWriteReqList.Count == 0)
                return;

            IntegerPageAddress targetAddress = new IntegerPageAddress(channelID, 0, 0, 0, 0, 0, 0);
            FlashChip targetChip = null;

            #region StaticAllocationScheme
            if (_FTL.isStaticScheme || (!_FTL.dynamicWayAssignment && !_FTL.dynamicChannelAssignment))
            {
                if (_FTL.InterleavedCMDEnabled || _FTL.MultiplaneCMDEnabled)
                {
                    //Start from the latest unresponsed request
                    for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                        if ((writeReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle) && (writeReq.Value.UpdateRead == null))
                        {
                            PerformAdvancedProgramCMDForFlashChip(writeReq.Value.TargetFlashChip, sourceWriteReqList);
                            if (targetChannel.Status == BusChannelStatus.Idle)
                                throw new Exception("Unexpected situation!");
                        }
                }//if (InterleaveCommandEnabled || MPWCommandEnabled)
                else
                {
                    for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                        if (writeReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                        {
                            if (writeReq.Value.UpdateRead == null)
                            {
                                if (_FTL.dynamicDieAssignment)
                                {
                                    writeReq.Value.TargetPageAddress.DieID = writeReq.Value.TargetFlashChip.CurrentActiveDieID;
                                    writeReq.Value.TargetFlashChip.CurrentActiveDieID = (writeReq.Value.TargetFlashChip.CurrentActiveDieID + 1) % _FTL.DieNoPerChip;
                                }
                                if (_FTL.dynamicPlaneAssignment)
                                {
                                    writeReq.Value.TargetPageAddress.PlaneID = writeReq.Value.TargetFlashChip.Dies[writeReq.Value.TargetPageAddress.DieID].CurrentActivePlaneID;
                                    writeReq.Value.TargetFlashChip.Dies[writeReq.Value.TargetPageAddress.DieID].CurrentActivePlaneID = (writeReq.Value.TargetFlashChip.Dies[writeReq.Value.TargetPageAddress.DieID].CurrentActivePlaneID + 1) % _FTL.PlaneNoPerDie;
                                }
                                _FTL.AllocatePPNInPlane(writeReq.Value);
                                sourceWriteReqList.Remove(writeReq);
                                _FTL.FCC.SendSimpleCommandToChip(writeReq.Value);
                                return;
                            }
                        }
                }
            }//if (isStaticScheme || !dynamicWayAssignment)
            #endregion
            #region DynamicChannelAssignement
            else if (!_FTL.dynamicWayAssignment && _FTL.dynamicChannelAssignment)//just dynamic channel allocation
            {
                if (_FTL.InterleavedCMDEnabled || _FTL.MultiplaneCMDEnabled)
                {
                    //Start from the latest unresponsed request
                    for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                        if ((targetChannel.FlashChips[writeReq.Value.TargetPageAddress.LocalFlashChipID].Status == FlashChipStatus.Idle) && (writeReq.Value.UpdateRead == null))
                        {
                            PerformAdvancedProgramCMDForFlashChip(targetChannel.FlashChips[writeReq.Value.TargetPageAddress.LocalFlashChipID], sourceWriteReqList);
                            if (targetChannel.Status == BusChannelStatus.Idle)
                                throw new Exception("Unexpected situation!");
                        }
                }//if (InterleaveCommandEnabled || MPWCommandEnabled)
                else
                {
                    for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                    {
                        targetChip = targetChannel.FlashChips[writeReq.Value.TargetPageAddress.LocalFlashChipID];
                        if (targetChip.Status == FlashChipStatus.Idle)
                        {
                            targetAddress = writeReq.Value.TargetPageAddress;
                            targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                            targetAddress.OverallFlashChipID = targetChip.OverallChipID;
                            if (writeReq.Value.UpdateRead == null)
                            {
                                if (_FTL.dynamicDieAssignment)
                                {
                                    targetAddress.DieID = targetChip.CurrentActiveDieID;
                                    targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % _FTL.DieNoPerChip;
                                }
                                if (_FTL.dynamicPlaneAssignment)
                                {
                                    targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                    targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID + 1) % _FTL.PlaneNoPerDie;
                                }
                                _FTL.AllocatePPNInPlane(writeReq.Value);
                                sourceWriteReqList.Remove(writeReq);
                                _FTL.FCC.SendSimpleCommandToChip(writeReq.Value);
                                return;
                            }
                        }
                    }
                }
            }
            #endregion
            #region DynamicWayAssignement
            else//dynamic way allocation
            {
                if (_FTL.InterleavedCMDEnabled || _FTL.MultiplaneCMDEnabled)
                {
                    for (int chipCntr = 0; chipCntr < _FTL.ChipNoPerChannel; chipCntr++)
                    {
                        targetChip = targetChannel.FlashChips[targetChannel.CurrentActiveChip];
                        if (targetChip.Status == FlashChipStatus.Idle)
                        {
                            PerformAdvancedProgramCMDForFlashChip(targetChip, sourceWriteReqList);
                            targetChannel.CurrentActiveChip = (targetChannel.CurrentActiveChip + 1) % _FTL.ChipNoPerChannel;
                            if (targetChannel.Status == BusChannelStatus.Busy)
                                return;
                        }
                        targetChannel.CurrentActiveChip = (targetChannel.CurrentActiveChip + 1) % _FTL.ChipNoPerChannel;
                    }
                }//if (InterleaveCommandEnabled || MPWCommandEnabled)
                else
                {
                    for (int chipCntr = 0; chipCntr < _FTL.ChipNoPerChannel; chipCntr++)
                    {
                        targetChip = targetChannel.FlashChips[targetChannel.CurrentActiveChip];
                        if (targetChip.Status == FlashChipStatus.Idle)
                            for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                                if (writeReq.Value.UpdateRead == null)
                                {
                                    sourceWriteReqList.Remove(writeReq);

                                    targetAddress = writeReq.Value.TargetPageAddress;
                                    targetAddress.ChannelID = targetChip.ChannelID;
                                    targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                    targetAddress.OverallFlashChipID = targetChip.OverallChipID;
                                    if (_FTL.dynamicDieAssignment)
                                    {
                                        targetAddress.DieID = targetChip.CurrentActiveDieID;
                                        targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % _FTL.DieNoPerChip;
                                    }
                                    if (_FTL.dynamicPlaneAssignment)
                                    {
                                        targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                        targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID =
                                            (targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID + 1) % _FTL.PlaneNoPerDie;
                                    }

                                    _FTL.AllocatePPNInPlane(writeReq.Value);
                                    _FTL.FCC.SendSimpleCommandToChip(writeReq.Value);
                                    targetChannel.CurrentActiveChip = (targetChannel.CurrentActiveChip + 1) % _FTL.ChipNoPerChannel;
                                    return;
                                }
                        targetChannel.CurrentActiveChip = (targetChannel.CurrentActiveChip + 1) % _FTL.ChipNoPerChannel;
                    }
                }
            }
            #endregion
        }
        public override void OnBusChannelIdle(BusChannelBase channel)
        {
            BusChannelSprinkler targetChannel = channel as BusChannelSprinkler;
            if (targetChannel.ActiveTransfersCount > 0)
                throw new Exception("Invalid BusChannelIdle invokation!");

            //early escape of function execution to decrease overall execution time.
            if (targetChannel.BusyChipCount == _FTL.ChipNoPerChannel)
                return;

            if (_FTL.GarbageCollector.ChannelInvokeGCBase(targetChannel.ChannelID))
                return;

            if (!ServiceReadCommands(targetChannel.ChannelID))
                ServiceWriteCommands(targetChannel.ChannelID);
        }
        public override void OnFlashchipIdle(FlashChip targetFlashchip)
        {
            uint channelID = targetFlashchip.ChannelID;
            if (_FTL.ChannelInfos[channelID].Status == BusChannelStatus.Idle)
            {
                if (_FTL.GarbageCollector.ChannelInvokeGCBase(channelID))
                    return;

                if (!ServiceReadCommands(channelID))
                    ServiceWriteCommands(channelID);
            }
        }
        public override void ProcessXEvent(XEvent e)
        {
            _FTL.HostInterface.SetupPhaseFinished();
            for (uint channelID = 0; channelID < _FTL.ChannelCount; channelID++)
            {
                if (!ServiceReadCommands(channelID))
                    ServiceWriteCommands(channelID);
            }
        }

        /*
        bool PerformAdvancedReadCommand(InternalReadRequestLinkedList sourceReadReqList)
        {
            //If allocation scheme determines plane address before die address, then in the advanced command execution
            //process, we should priotize multiplane command allocation over multidie allocation
            if (planePrioritizedOverDie)
            {
                #region SearchForTwoPlaneCommand
                if (this.currentReadAdvancedCommandType == AdvancedCommandType.TwoPlaneRead
                    || this.currentReadAdvancedCommandType == AdvancedCommandType.InterLeaveTwoPlane)
                {
                    for (var firstReq = sourceReadReqList.First; firstReq != null; firstReq = firstReq.Next)
                    {
                        if (firstReq.Value.TargetFlashChip.Status != FlashChipStatus.Idle)
                            continue;

                        //To prevent starvation we should check if the oldest pending InternalRequest could be serviced or not?
                        if ((firstReq != sourceReadReqList.First) && (sourceReadReqList.First.Value.TargetFlashChip.Status == FlashChipStatus.Idle))
                            break;

                        for (var secondReq = firstReq.Next; secondReq != null; secondReq = secondReq.Next)
                        {
                            if ((firstReq.Value.TargetPageAddress.OverallFlashChipID == secondReq.Value.TargetPageAddress.OverallFlashChipID)
                                && (firstReq.Value.TargetPageAddress.DieID == secondReq.Value.TargetPageAddress.DieID)
                                && (firstReq.Value.TargetPageAddress.PlaneID != secondReq.Value.TargetPageAddress.PlaneID)
                                && ((firstReq.Value.TargetPageAddress.BlockID == secondReq.Value.TargetPageAddress.BlockID) || !BAConstraintForMultiPlane)
                                && (firstReq.Value.TargetPageAddress.PageID == secondReq.Value.TargetPageAddress.PageID))
                            {
                                firstReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                secondReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                sourceReadReqList.Remove(firstReq);
                                sourceReadReqList.Remove(secondReq);
                                FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                                return true;
                            }
                        }
                    }
                }
                #endregion
                #region SearchForMultiDieCommand
                if (this.currentReadAdvancedCommandType == AdvancedCommandType.Interleave
                    || this.currentReadAdvancedCommandType == AdvancedCommandType.InterLeaveTwoPlane)
                {

                    for (var firstReq = sourceReadReqList.First; firstReq != null; firstReq = firstReq.Next)
                    {
                        if (firstReq.Value.TargetFlashChip.Status != FlashChipStatus.Idle)
                            continue;

                        //To prevent starvation we should check if the oldest pending InternalRequest could be serviced or not?
                        if ((firstReq != sourceReadReqList.First) && (sourceReadReqList.First.Value.TargetFlashChip.Status == FlashChipStatus.Idle))
                            break;

                        for (var secondReq = firstReq.Next; secondReq != null; secondReq = secondReq.Next)
                            if ((firstReq.Value.TargetPageAddress.OverallFlashChipID == secondReq.Value.TargetPageAddress.OverallFlashChipID)
                                &&
                                (firstReq.Value.TargetPageAddress.DieID != secondReq.Value.TargetPageAddress.DieID))
                            {
                                firstReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                secondReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                sourceReadReqList.Remove(firstReq);
                                sourceReadReqList.Remove(secondReq);
                                FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                                return true;
                            }
                    }
                }
                #endregion
            }
            else
            {
                #region SearchForMultiDieCommand
                if (this.currentReadAdvancedCommandType == AdvancedCommandType.Interleave
                    || this.currentReadAdvancedCommandType == AdvancedCommandType.InterLeaveTwoPlane)
                {
                    for (var firstReq = sourceReadReqList.First; firstReq != null; firstReq = firstReq.Next)
                    {
                        if (firstReq.Value.TargetFlashChip.Status != FlashChipStatus.Idle)
                            continue;

                        //To prevent starvation we should check if the oldest pending InternalRequest could be serviced or not?
                        if ((firstReq != sourceReadReqList.First) && (sourceReadReqList.First.Value.TargetFlashChip.Status == FlashChipStatus.Idle))
                            break;

                        for (var secondReq = firstReq.Next; secondReq != null; secondReq = secondReq.Next)
                            if ((firstReq.Value.TargetPageAddress.OverallFlashChipID == secondReq.Value.TargetPageAddress.OverallFlashChipID)
                                &&
                                (firstReq.Value.TargetPageAddress.DieID != secondReq.Value.TargetPageAddress.DieID))
                            {
                                firstReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                secondReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                sourceReadReqList.Remove(firstReq);
                                sourceReadReqList.Remove(secondReq);
                                FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                                return true;
                            }
                    }
                }
                #endregion
                #region SearchForTwoPlaneCommand
                if (this.currentReadAdvancedCommandType == AdvancedCommandType.TwoPlaneRead
                    || this.currentReadAdvancedCommandType == AdvancedCommandType.InterLeaveTwoPlane)
                {
                    for (var firstReq = sourceReadReqList.First; firstReq != null; firstReq = firstReq.Next)
                    {
                        if (firstReq.Value.TargetFlashChip.Status != FlashChipStatus.Idle)
                            continue;

                        //To prevent starvation we should check if the oldest pending InternalRequest could be serviced or not?
                        if ((firstReq != sourceReadReqList.First) && (sourceReadReqList.First.Value.TargetFlashChip.Status == FlashChipStatus.Idle))
                            break;

                        for (var secondReq = firstReq.Next; secondReq != null; secondReq = secondReq.Next)
                        {
                            if ((firstReq.Value.TargetPageAddress.OverallFlashChipID == secondReq.Value.TargetPageAddress.OverallFlashChipID)
                                && (firstReq.Value.TargetPageAddress.DieID == secondReq.Value.TargetPageAddress.DieID)
                                && (firstReq.Value.TargetPageAddress.PlaneID != secondReq.Value.TargetPageAddress.PlaneID)
                                && ((firstReq.Value.TargetPageAddress.BlockID == secondReq.Value.TargetPageAddress.BlockID) || !BAConstraintForMultiPlane)
                                && (firstReq.Value.TargetPageAddress.PageID == secondReq.Value.TargetPageAddress.PageID))
                            {
                                firstReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                secondReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                sourceReadReqList.Remove(firstReq);
                                sourceReadReqList.Remove(secondReq);
                                FCC.SendAdvCommandToChipRD(firstReq.Value, secondReq.Value);
                                return true;
                            }
                        }
                    }
                }
                #endregion
            }


            return false;
        }
        void PerformAdvancedProgramCMDForFlashChip(FlashChip targetChip, InternalWriteRequestLinkedList sourceWriteReqList)
        {
            InternalWriteRequestLinkedList executionList = new InternalWriteRequestLinkedList();
            //We should care about data transfer time for scheduling scheme. Otherwise, the advanced command execution will not
            //be efficient.
            ulong transferTime = 0;

            #region StaticDieStaticPlaneAllocation
            //PlaneAllocationSchemeType.CWDP || PlaneAllocationSchemeType.CWPD || PlaneAllocationSchemeType.CDWP
            //  || PlaneAllocationSchemeType.CDPW || PlaneAllocationSchemeType.CPWD || PlaneAllocationSchemeType.CPDW
            //  || PlaneAllocationSchemeType.WCDP || PlaneAllocationSchemeType.WCPD || PlaneAllocationSchemeType.WDCP
            //  || PlaneAllocationSchemeType.WDPC || PlaneAllocationSchemeType.WPCD || PlaneAllocationSchemeType.WPDC
            //  || PlaneAllocationSchemeType.DCWP || PlaneAllocationSchemeType.DCPW || PlaneAllocationSchemeType.DWCP
            //  || PlaneAllocationSchemeType.DWPC || PlaneAllocationSchemeType.DPCW || PlaneAllocationSchemeType.DPWC
            //  || PlaneAllocationSchemeType.PCWD || PlaneAllocationSchemeType.PCDW || PlaneAllocationSchemeType.PWCD
            //  || PlaneAllocationSchemeType.PWDC || PlaneAllocationSchemeType.PDCW || PlaneAllocationSchemeType.PDWC
             
             //PlaneAllocationSchemeType.DP || PlaneAllocationSchemeType.PD
             //   || PlaneAllocationSchemeType.CDP || PlaneAllocationSchemeType.CPD
             //   || PlaneAllocationSchemeType.WDP || PlaneAllocationSchemeType.WPD
             //   || PlaneAllocationSchemeType.DCP || PlaneAllocationSchemeType.DWP
             //   || PlaneAllocationSchemeType.DPC || PlaneAllocationSchemeType.DPW
             //   || PlaneAllocationSchemeType.PCD || PlaneAllocationSchemeType.PWD
             //   || PlaneAllocationSchemeType.PDC || PlaneAllocationSchemeType.PDW
            if (isStaticScheme || (!dynamicPlaneAssignment && !dynamicDieAssignment))
            {
                InternalWriteRequest[,] candidateWriteReqs = new InternalWriteRequest[this.DieNoPerChip, this.PlaneNoPerDie];
                uint[] countOfWriteReqForDie = new uint[this.DieNoPerChip];
                InternalWriteRequest firstReq = null;
                uint dieID, planeID;
                uint firstDieID = uint.MaxValue, firstPlaneID = uint.MaxValue;

                for (int i = 0; i < this.DieNoPerChip; i++)
                {
                    countOfWriteReqForDie[i] = 0;
                    for (int j = 0; j < this.PlaneNoPerDie; j++)
                        candidateWriteReqs[i, j] = null;
                }

                int reqCount = 0;
                for (var writeReq = sourceWriteReqList.First; writeReq != null && reqCount < this.FlashChipExecutionCapacity; writeReq = writeReq.Next)
                    if (writeReq.Value.UpdateRead == null)
                    {
                        IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                        if (dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                        {
                            if (candidateWriteReqs[targetAddress.DieID, targetAddress.PlaneID] == null)
                            {
                                targetAddress.ChannelID = targetChip.ChannelID;
                                targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                targetAddress.OverallFlashChipID = targetChip.OverallChipID;

                                reqCount++;
                                countOfWriteReqForDie[targetAddress.DieID]++;
                                candidateWriteReqs[targetAddress.DieID, targetAddress.PlaneID] = writeReq.Value;
                                if (firstReq == null)
                                {
                                    firstReq = writeReq.Value;
                                    firstDieID = targetAddress.DieID;
                                    firstPlaneID = targetAddress.PlaneID;
                                }
                            }
                        }
                    }

                if (reqCount == 0)
                    return;

                if (reqCount == 1)
                {
                    AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                    return;
                }

                switch (currentWriteAdvancedCommandType)
                {
                    #region InterleaveTwoPlane
                    case AdvancedCommandType.InterLeaveTwoPlane:
                        {
                            bool multiPlaneFlag = false, multiDieFlag = false;
                            uint lastDieID = uint.MaxValue;
                            for (uint dieCntr = 0; dieCntr < DieNoPerChip; dieCntr++)
                            {
                                dieID = (firstDieID + dieCntr) % this.DieNoPerChip;
                                if (countOfWriteReqForDie[dieID] > 0)
                                {
                                    InternalWriteRequest firstInternalReqOfDie = null;
                                    for (uint planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
                                    {
                                        planeID = (firstPlaneID + planeCntr) % this.PlaneNoPerDie;
                                        InternalWriteRequest currentReq = candidateWriteReqs[dieID, planeID];
                                        if (currentReq != null)
                                        {
                                            if (firstInternalReqOfDie == null)
                                            {
                                                if (lastDieID != uint.MaxValue)
                                                {
                                                    transferTime += FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                    if (transferTime >= this.pageProgramLatency)
                                                    {
                                                        transferTime -= FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                        continue;
                                                    }
                                                    multiDieFlag = true;
                                                }
                                                lastDieID = dieID;
                                                firstInternalReqOfDie = currentReq;
                                                AllocatePPNInPlane(currentReq);
                                                executionList.AddLast(currentReq);
                                            }
                                            else
                                            {
                                                transferTime += FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                if (transferTime >= this.pageProgramLatency)
                                                {
                                                    transferTime -= FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                    continue;
                                                }
                                                if (FindLevelPageStrict(targetChip, firstInternalReqOfDie, currentReq))
                                                {
                                                    multiPlaneFlag = true;
                                                    executionList.AddLast(currentReq);
                                                }
                                                else
                                                    transferTime -= FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            }
                                        }
                                    }
                                }
                            }

                            if (executionList.Count > 1)
                            {
                                if (multiPlaneFlag && multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                    }
                                }
                                else if (multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                    }
                                }
                                else
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                    }
                                }
                                FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                                FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region Interleave
                    case AdvancedCommandType.Interleave:
                        {
                            executionList.Clear();
                            AllocatePPNInPlane(firstReq);
                            executionList.AddLast(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);

                            for (uint dieCntr = 1; dieCntr < this.DieNoPerChip; dieCntr++)
                            {
                                dieID = (firstDieID + dieCntr) % this.DieNoPerChip;
                                if (countOfWriteReqForDie[dieID] > 0)
                                {
                                    InternalWriteRequest firstInternalReqOfDie;
                                    for (uint planeCntr = 0; planeCntr < this.PlaneNoPerDie; planeCntr++)
                                    {
                                        firstInternalReqOfDie = candidateWriteReqs[dieID, planeCntr];
                                        if (firstInternalReqOfDie != null)
                                        {
                                            transferTime += FCC.InterleaveProgramSetup + (firstInternalReqOfDie.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            if (transferTime >= this.pageProgramLatency)
                                            {
                                                transferTime -= FCC.InterleaveProgramSetup + (firstInternalReqOfDie.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                continue;
                                            }

                                            AllocatePPNInPlane(firstInternalReqOfDie);
                                            firstInternalReqOfDie.ExecutionType = InternalRequestExecutionType.Interleaved;
                                            executionList.AddLast(firstInternalReqOfDie);
                                            sourceWriteReqList.Remove(firstInternalReqOfDie.RelatedNodeInList);
                                            targetChip.Dies[dieID].CurrentActivePlaneID = (targetChip.Dies[dieID].CurrentActivePlaneID + 1) % this.PlaneNoPerDie;
                                            break;
                                        }
                                    }
                                }
                            }//for (int dieCntr = 0; dieCntr < this.DieNoPerChip; dieCntr++)
                            if (executionList.Count > 1)
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Interleaved;
                                FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region TwoPlane
                    case AdvancedCommandType.TwoPlaneWrite:
                        {
                            executionList.Clear();
                            AllocatePPNInPlane(firstReq);
                            executionList.AddLast(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);

                            if (countOfWriteReqForDie[firstDieID] > 1)
                            {
                                for (uint planeCntr = 1; planeCntr < this.PlaneNoPerDie; planeCntr++)
                                {
                                    planeID = (firstPlaneID + planeCntr) % this.PlaneNoPerDie;
                                    InternalWriteRequest currentReq = candidateWriteReqs[firstDieID, planeID];
                                    if (currentReq != null)
                                    {
                                        transferTime += FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                        if (transferTime >= this.pageProgramLatency)
                                        {
                                            transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            continue;
                                        }

                                        if (executionList.Count == 1)
                                        {
                                            if (FindLevelPage(executionList.First.Value, currentReq))
                                            {
                                                currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                                executionList.AddLast(currentReq);
                                                sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                            }
                                            else
                                                transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                        }
                                        else
                                        {
                                            if (FindLevelPageStrict(targetChip, executionList.First.Value, currentReq))
                                            {
                                                currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                                executionList.AddLast(currentReq);
                                                sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                            }
                                            else
                                                transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                        }
                                    }
                                }//for (int planeCntr = 0; planeCntr < this.PlaneNoPerDie; planeCntr++)
                                if (executionList.Count > 1)
                                {
                                    firstReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                    FCC.SendAdvCommandToChipWR(executionList);
                                    return;
                                }
                            }

                            firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                            FCC.SendSimpleCommandToChip(firstReq);
                            return;
                        }
                    #endregion
                    default:
                        throw new Exception("Unexpected invokation for advanced command execution.");
                }

            }// if (deterministicFlashChipAssignment)
            #endregion
            #region StaticDieDynamicPlaneAllocation
                // planeAllocationScheme == PlaneAllocationSchemeType.CWD
                //|| planeAllocationScheme == PlaneAllocationSchemeType.CD || planeAllocationScheme == PlaneAllocationSchemeType.CDW
                //|| planeAllocationScheme == PlaneAllocationSchemeType.WCD
                //|| planeAllocationScheme == PlaneAllocationSchemeType.WD || planeAllocationScheme == PlaneAllocationSchemeType.WDC
                //|| planeAllocationScheme == PlaneAllocationSchemeType.D
                //|| planeAllocationScheme == PlaneAllocationSchemeType.DC || planeAllocationScheme == PlaneAllocationSchemeType.DCW
                //|| planeAllocationScheme == PlaneAllocationSchemeType.DW || planeAllocationScheme == PlaneAllocationSchemeType.DWC
            else if (!dynamicDieAssignment && dynamicPlaneAssignment)
            {
                InternalWriteRequest[,] candidateWriteReqs = new InternalWriteRequest[this.DieNoPerChip, this.PlaneNoPerDie];
                uint[] countOfWriteReqForDie = new uint[this.DieNoPerChip];
                InternalWriteRequest firstReq = null;
                uint dieID, firstDieID = uint.MaxValue;
                for (int i = 0; i < this.DieNoPerChip; i++)
                {
                    countOfWriteReqForDie[i] = 0;
                    for (int j = 0; j < this.PlaneNoPerDie; j++)
                    {
                        targetChip.Dies[i].Planes[j].CommandAssigned = false;
                        candidateWriteReqs[i, j] = null;
                    }
                }

                int reqCount = 0;
                for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                    if (writeReq.Value.UpdateRead == null)
                    {
                        IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                        if (countOfWriteReqForDie[targetAddress.DieID] < this.PlaneNoPerDie)
                        {
                            if (dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                            {
                                writeReq.Value.TargetFlashChip = targetChip;
                                targetAddress.ChannelID = targetChip.ChannelID;
                                targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                targetAddress.OverallFlashChipID = targetChip.OverallChipID;

                                reqCount++;
                                candidateWriteReqs[targetAddress.DieID, countOfWriteReqForDie[targetAddress.DieID]] = writeReq.Value;
                                countOfWriteReqForDie[targetAddress.DieID]++;
                                if (firstReq == null)
                                {
                                    firstReq = writeReq.Value;
                                    firstDieID = targetAddress.DieID;
                                }
                            }
                        }
                    }


                if (reqCount == 0)
                    return;

                if (reqCount == 1)
                {
                    AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                    return;
                }

                switch (currentWriteAdvancedCommandType)
                {
                    #region InterleaveTwoPlane
                    case AdvancedCommandType.InterLeaveTwoPlane:
                        {
                            bool multiPlaneFlag = false, multiDieFlag = false;
                            uint lastDieID = uint.MaxValue;
                            for (uint dieCntr = 0; dieCntr < DieNoPerChip; dieCntr++)
                            {
                                dieID = (firstDieID + dieCntr) % this.DieNoPerChip;
                                if (countOfWriteReqForDie[dieID] > 0)
                                {
                                    InternalWriteRequest firstInternalReqOfDie = null;
                                    for (int planeCntr = 0; planeCntr < countOfWriteReqForDie[dieID]; planeCntr++)
                                    {
                                        InternalWriteRequest currentReq = candidateWriteReqs[dieID, planeCntr];
                                        if (firstInternalReqOfDie == null)
                                        {
                                            if (lastDieID != uint.MaxValue)
                                            {
                                                transferTime += FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                if (transferTime >= this.pageProgramLatency)
                                                {
                                                    transferTime -= FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                    continue;
                                                }
                                                multiDieFlag = true;
                                            }

                                            lastDieID = dieID;
                                            currentReq.TargetPageAddress.PlaneID = targetChip.Dies[dieID].CurrentActivePlaneID;
                                            targetChip.Dies[dieID].Planes[currentReq.TargetPageAddress.PlaneID].CommandAssigned = true;
                                            targetChip.Dies[dieID].CurrentActivePlaneID = (currentReq.TargetPageAddress.PlaneID + 1) % this.PlaneNoPerDie;
                                            firstInternalReqOfDie = currentReq;
                                            AllocatePPNInPlane(currentReq);
                                            executionList.AddLast(currentReq);
                                        }
                                        else
                                        {
                                            transferTime += FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            if (transferTime >= this.pageProgramLatency)
                                            {
                                                transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                continue;
                                            }
                                            if (FindLevelPageStrict(targetChip, firstInternalReqOfDie, currentReq))
                                            {
                                                multiPlaneFlag = true;
                                                executionList.AddLast(currentReq);
                                            }
                                            else
                                                transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                        }
                                    }
                                }
                            }

                            if (executionList.Count > 1)
                            {
                                if (multiPlaneFlag && multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    }
                                }
                                else if (multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    }
                                }
                                else
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    }
                                }

                                FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                                FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region Interleave
                    case AdvancedCommandType.Interleave:
                        {
                            executionList.Clear();
                            firstReq.TargetPageAddress.PlaneID = targetChip.Dies[firstReq.TargetPageAddress.DieID].CurrentActivePlaneID;
                            targetChip.Dies[firstReq.TargetPageAddress.DieID].CurrentActivePlaneID = (firstReq.TargetPageAddress.PlaneID + 1) % this.PlaneNoPerDie;
                            AllocatePPNInPlane(firstReq);
                            executionList.AddLast(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                            for (uint dieCntr = 1; dieCntr < this.DieNoPerChip; dieCntr++)
                            {
                                dieID = (firstDieID + dieCntr) % this.DieNoPerChip;
                                if (countOfWriteReqForDie[dieID] > 0)
                                {
                                    for (uint planeCntr = 0; planeCntr < countOfWriteReqForDie[dieID]; planeCntr++)
                                    {
                                        InternalWriteRequest firstInternalReqOfDie = candidateWriteReqs[dieID, planeCntr];
                                        transferTime += FCC.InterleaveProgramSetup + (firstInternalReqOfDie.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                        if (transferTime >= this.pageProgramLatency)
                                        {
                                            transferTime -= FCC.InterleaveProgramSetup + (firstInternalReqOfDie.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            continue;
                                        }

                                        firstInternalReqOfDie.TargetPageAddress.PlaneID = targetChip.Dies[dieID].CurrentActivePlaneID;
                                        targetChip.Dies[dieID].CurrentActivePlaneID = (firstInternalReqOfDie.TargetPageAddress.PlaneID + 1) % this.PlaneNoPerDie;
                                        AllocatePPNInPlane(firstInternalReqOfDie);
                                        firstInternalReqOfDie.ExecutionType = InternalRequestExecutionType.Interleaved;
                                        executionList.AddLast(firstInternalReqOfDie);
                                        sourceWriteReqList.Remove(firstInternalReqOfDie.RelatedNodeInList);
                                        break;
                                    }
                                }
                            }//for (int dieCntr = 0; dieCntr < this.DieNoPerChip; dieCntr++)
                            if (executionList.Count > 1)
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Interleaved;
                                FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region TwoPlane
                    case AdvancedCommandType.TwoPlaneWrite:
                        {
                            if (countOfWriteReqForDie[firstDieID] > 1)
                            {
                                executionList.Clear();
                                firstReq.TargetPageAddress.PlaneID = targetChip.Dies[firstReq.TargetPageAddress.DieID].CurrentActivePlaneID;
                                targetChip.Dies[firstReq.TargetPageAddress.DieID].CurrentActivePlaneID = (firstReq.TargetPageAddress.PlaneID + 1) % this.PlaneNoPerDie;
                                AllocatePPNInPlane(firstReq);
                                executionList.AddLast(firstReq);
                                sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                                for (int planeCntr = 1; planeCntr < countOfWriteReqForDie[firstDieID]; planeCntr++)
                                {
                                    InternalWriteRequest currentReq = candidateWriteReqs[firstDieID, planeCntr];

                                    transferTime += FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                    if (transferTime >= this.pageProgramLatency)
                                    {
                                        transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                        continue;
                                    }
                                    if (executionList.Count == 1)
                                    {
                                        if (FindLevelPage(executionList.First.Value, currentReq))
                                        {
                                            currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                            executionList.AddLast(currentReq);
                                            sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                        }
                                        else
                                            transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                    }
                                    else if (FindLevelPageStrict(targetChip, executionList.First.Value, currentReq))
                                    {
                                        currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                        executionList.AddLast(currentReq);
                                        sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                    }
                                    else
                                        transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                }//for (int planeCntr = 0; planeCntr < this.PlaneNoPerDie; planeCntr++)
                                if (executionList.Count > 1)
                                {
                                    firstReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                    FCC.SendAdvCommandToChipWR(executionList);
                                    return;
                                }
                                else
                                {
                                    firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                    FCC.SendSimpleCommandToChip(firstReq);
                                    return;
                                }
                            }

                            AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                            return;
                        }
                    #endregion
                    default:
                        throw new Exception("Unexpected invokation for advanced command execution.");
                }
            }
            #endregion
            #region StaticPlaneDynamicDieAllocation
                // planeAllocationScheme == PlaneAllocationSchemeType.CWP
                //|| planeAllocationScheme == PlaneAllocationSchemeType.CP || planeAllocationScheme == PlaneAllocationSchemeType.CPW
                //|| planeAllocationScheme == PlaneAllocationSchemeType.WCP
                //|| planeAllocationScheme == PlaneAllocationSchemeType.WP || planeAllocationScheme == PlaneAllocationSchemeType.WPC
                //|| planeAllocationScheme == PlaneAllocationSchemeType.P
                //|| planeAllocationScheme == PlaneAllocationSchemeType.PC || planeAllocationScheme == PlaneAllocationSchemeType.PCW
                //|| planeAllocationScheme == PlaneAllocationSchemeType.PW || planeAllocationScheme == PlaneAllocationSchemeType.PWC
            else if (!dynamicPlaneAssignment && dynamicDieAssignment)
            {

                InternalWriteRequest firstReq = null;

                switch (currentWriteAdvancedCommandType)
                {
                    #region InterleaveTwoPlane
                    case AdvancedCommandType.InterLeaveTwoPlane:
                        {
                            bool multiPlaneFlag = false, multiDieFlag = false;
                            uint lastDieID = uint.MaxValue;
                            uint firstPlaneID = 0;
                            //InternalRequest[,] candidateWriteReqs = new InternalRequest[this.PlaneNoPerDie, this.DieNoPerChip];
                            List<InternalWriteRequest>[] candidateWriteReqs = new List<InternalWriteRequest>[this.PlaneNoPerDie];
                            for (int i = 0; i < this.PlaneNoPerDie; i++)
                                candidateWriteReqs[i] = new List<InternalWriteRequest>((int)this.DieNoPerChip);

                            int reqCount = 0;
                            for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                                if (writeReq.Value.UpdateRead == null)
                                {
                                    IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                                    if (dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                                    {
                                        targetAddress.ChannelID = targetChip.ChannelID;
                                        targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                        targetAddress.OverallFlashChipID = targetChip.OverallChipID;

                                        reqCount++;
                                        candidateWriteReqs[targetAddress.PlaneID].Add(writeReq.Value);
                                        if (firstReq == null)
                                        {
                                            firstReq = writeReq.Value;
                                            firstPlaneID = firstReq.TargetPageAddress.PlaneID;
                                        }
                                    }
                                }


                            if (reqCount == 0)
                                return;

                            if (reqCount == 1)
                            {
                                AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                                return;
                            }
                            executionList.Clear();

                            for (uint dieCntr = 0; dieCntr < this.DieNoPerChip; dieCntr++)
                            {
                                InternalWriteRequest firstInternalReqOfDie = null;
                                for (uint planeCntr = 0; planeCntr < this.PlaneNoPerDie && reqCount > 0; planeCntr++)
                                {
                                    uint planeID = (firstPlaneID + planeCntr) % this.PlaneNoPerDie;
                                    if (candidateWriteReqs[planeID].Count > 0)
                                    {
                                        InternalWriteRequest currentReq = candidateWriteReqs[planeID][0];
                                        if (firstInternalReqOfDie == null)
                                        {
                                            if (lastDieID != uint.MaxValue)
                                            {
                                                transferTime += FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                if (transferTime >= this.pageProgramLatency)
                                                {
                                                    transferTime -= FCC.InterleaveProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                    currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                                    continue;
                                                }
                                                multiDieFlag = true;
                                            }
                                            currentReq.TargetPageAddress.DieID = targetChip.CurrentActiveDieID;
                                            lastDieID = targetChip.CurrentActiveDieID;
                                            firstInternalReqOfDie = currentReq;
                                            AllocatePPNInPlane(currentReq);
                                            executionList.AddLast(currentReq);
                                            candidateWriteReqs[planeID].RemoveAt(0);
                                            reqCount--;
                                        }
                                        else
                                        {
                                            transferTime += FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            if (transferTime >= this.pageProgramLatency)
                                            {
                                                transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                                continue;
                                            }
                                            if (FindLevelPageStrict(targetChip, firstInternalReqOfDie, currentReq))
                                            {
                                                multiPlaneFlag = true;
                                                executionList.AddLast(currentReq);
                                                candidateWriteReqs[planeID].RemoveAt(0);
                                                reqCount--;
                                            }
                                            else
                                            {
                                                transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                            }
                                        }
                                    }
                                }
                                targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % this.DieNoPerChip;
                                if (reqCount == 0)
                                    break;
                            }

                            if (executionList.Count > 1)
                            {
                                if (multiPlaneFlag && multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                    }
                                }
                                else if (multiDieFlag)
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                    }
                                }
                                else
                                {
                                    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                    {
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                    }
                                }

                                FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region Interleave
                    case AdvancedCommandType.Interleave:
                        {
                            bool firstdie = true;
                            for (var writeReq = sourceWriteReqList.First; writeReq != null && executionList.Count < this.DieNoPerChip; writeReq = writeReq.Next)
                                if (writeReq.Value.UpdateRead == null)
                                {
                                    IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                                    if (dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                                    {
                                        if (firstdie)
                                            firstdie = false;
                                        else
                                        {
                                            transferTime += FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            if (transferTime >= this.pageProgramLatency)
                                            {
                                                transferTime -= FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                continue;
                                            }
                                        }

                                        targetAddress.ChannelID = targetChip.ChannelID;
                                        targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                        targetAddress.OverallFlashChipID = targetChip.OverallChipID;
                                        targetAddress.DieID = targetChip.CurrentActiveDieID;
                                        targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % this.DieNoPerChip;
                                        AllocatePPNInPlane(writeReq.Value);
                                        writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                        executionList.AddLast(writeReq.Value);
                                    }
                                }

                            if (executionList.Count == 0)
                                return;
                            if (executionList.Count == 1)
                            {
                                executionList.First.Value.ExecutionType = InternalRequestExecutionType.Simple;
                                sourceWriteReqList.Remove(executionList.First.Value.RelatedNodeInList);
                                FCC.SendSimpleCommandToChip(executionList.First.Value);
                                return;
                            }
                            for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next)
                                sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                            FCC.SendAdvCommandToChipWR(executionList);
                            return;
                        }
                    #endregion
                    #region TwoPlane
                    case AdvancedCommandType.TwoPlaneWrite:
                        {
                            InternalWriteRequestLinkedList[] candidateWriteReqs = new InternalWriteRequestLinkedList[this.PlaneNoPerDie];
                            for (int i = 0; i < this.PlaneNoPerDie; i++)
                                candidateWriteReqs[i] = new InternalWriteRequestLinkedList();

                            int reqCount = 0;
                            for (var writeReq = sourceWriteReqList.First; writeReq != null; writeReq = writeReq.Next)
                                if (writeReq.Value.UpdateRead == null)
                                {
                                    IntegerPageAddress targetAddress = writeReq.Value.TargetPageAddress;
                                    if (dynamicWayAssignment || targetAddress.LocalFlashChipID == targetChip.LocalChipID)
                                    {
                                        targetAddress.ChannelID = targetChip.ChannelID;
                                        targetAddress.LocalFlashChipID = targetChip.LocalChipID;
                                        targetAddress.OverallFlashChipID = targetChip.OverallChipID;

                                        reqCount++;
                                        candidateWriteReqs[targetAddress.PlaneID].AddLast(writeReq.Value);
                                        if (firstReq == null)
                                            firstReq = writeReq.Value;
                                    }
                                }


                            if (reqCount == 0)
                                return;

                            if (reqCount == 1)
                            {
                                AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                                return;
                            }
                            executionList.Clear();
                            firstReq.TargetPageAddress.DieID = targetChip.CurrentActiveDieID;
                            targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % this.DieNoPerChip;
                            AllocatePPNInPlane(firstReq);
                            executionList.AddLast(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);

                            for (uint planeCntr = 1; planeCntr < this.PlaneNoPerDie; planeCntr++)
                            {
                                uint planeID = (firstReq.TargetPageAddress.PlaneID + planeCntr) % this.PlaneNoPerDie;
                                for (var writeReqItr = candidateWriteReqs[planeID].First; writeReqItr != null; writeReqItr = writeReqItr.Next)
                                {
                                    InternalWriteRequest currentReq = writeReqItr.Value;

                                    if (executionList.Count == 1)
                                    {
                                        transferTime += FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                        if (transferTime >= this.pageProgramLatency)
                                        {
                                            transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            continue;
                                        }
                                        currentReq.TargetPageAddress.DieID = firstReq.TargetPageAddress.DieID;
                                        if (FindLevelPage(firstReq, currentReq))
                                        {
                                            currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                            executionList.AddLast(currentReq);
                                            sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                            break;
                                        }
                                        else
                                        {
                                            transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                        }
                                    }
                                    else
                                    {
                                        transferTime += FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                        if (transferTime >= this.pageProgramLatency)
                                        {
                                            transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            continue;
                                        }
                                        currentReq.TargetPageAddress.DieID = firstReq.TargetPageAddress.DieID;
                                        if (FindLevelPageStrict(targetChip, firstReq, currentReq))
                                        {
                                            currentReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                            executionList.AddLast(currentReq);
                                            sourceWriteReqList.Remove(currentReq.RelatedNodeInList);
                                            break;
                                        }
                                        else
                                        {
                                            transferTime -= FCC.MultiPlaneProgramSetup + (currentReq.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            currentReq.TargetPageAddress.DieID = uint.MaxValue;
                                        }
                                    }
                                }
                            }
                            if (executionList.Count > 1)
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                                FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    default:
                        throw new Exception("Unexpected invokation for advanced command execution.");
                }
            }
            #endregion
            #region JustStaticCWAllocation
            //PlaneAllocationSchemeType.F
            //|| PlaneAllocationSchemeType.C  || PlaneAllocationSchemeType.W
            //|| PlaneAllocationSchemeType.CW || PlaneAllocationSchemeType.WC
            else if (dynamicDieAssignment && dynamicPlaneAssignment)
            {
                InternalWriteRequest firstReq = null;
                for (var writeReq = sourceWriteReqList.First; (writeReq != null) && (executionList.Count < this.FlashChipExecutionCapacity); writeReq = writeReq.Next)
                    if (writeReq.Value.UpdateRead == null)
                        if (dynamicWayAssignment || (writeReq.Value.TargetPageAddress.LocalFlashChipID == targetChip.LocalChipID))
                        {
                            writeReq.Value.TargetPageAddress.ChannelID = targetChip.ChannelID;
                            writeReq.Value.TargetPageAddress.LocalFlashChipID = targetChip.LocalChipID;
                            writeReq.Value.TargetPageAddress.OverallFlashChipID = targetChip.OverallChipID;
                            executionList.AddLast(writeReq.Value);
                            if (firstReq == null)
                                firstReq = writeReq.Value;
                        }

                if (executionList.Count == 0)
                    return;

                if (executionList.Count == 1)
                {
                    AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, executionList.First.Value); //there was only one request
                    return;
                }

                IntegerPageAddress targetAddress = null;
                switch (currentWriteAdvancedCommandType)
                {
                    #region InterleaveTwoPlane
                    case AdvancedCommandType.InterLeaveTwoPlane:
                        {
                            bool multiPlaneFlag = false, multiDieFlag = false;
                            int currentReqCntr = 0;
                            InternalWriteRequest[] candidateWriteReqsAsList = new InternalWriteRequest[FlashChipExecutionCapacity];

                            if (GarbageCollector.TB_Enabled)
                            {
                                uint startDieID = targetChip.CurrentActiveDieID;
                                uint startPlaneID = targetChip.Dies[targetChip.CurrentActiveDieID].CurrentActivePlaneID;
                                uint assignedDieCount = 0;
                                for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next, currentReqCntr++)
                                {
                                    if (startDieID != targetChip.CurrentActiveDieID)
                                        multiDieFlag = true;
                                    if (startPlaneID != targetChip.Dies[targetChip.CurrentActiveDieID].CurrentActivePlaneID)
                                        multiPlaneFlag = true;
                                    targetAddress = writeReq.Value.TargetPageAddress;
                                    targetAddress.DieID = targetChip.CurrentActiveDieID;
                                    targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                    AllocatePPNInPlane(writeReq.Value);
                                    targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % this.PlaneNoPerDie;
                                    if (targetAddress.PlaneID == (PlaneNoPerDie - 1))
                                    {
                                        targetChip.CurrentActiveDieID = (targetAddress.DieID + 1) % this.DieNoPerChip;
                                        startPlaneID = targetChip.Dies[targetChip.CurrentActiveDieID].CurrentActivePlaneID;
                                        assignedDieCount++;
                                    }

                                    candidateWriteReqsAsList[targetAddress.DieID * this.PlaneNoPerDie + targetAddress.PlaneID] = writeReq.Value;
                                    sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    if (assignedDieCount == DieNoPerChip)
                                        break;
                                }
                            }
                            else
                                for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next, currentReqCntr++)
                                {
                                    candidateWriteReqsAsList[currentReqCntr] = writeReq.Value;
                                    targetAddress = writeReq.Value.TargetPageAddress;

                                    if (currentReqCntr < this.DieNoPerChip)
                                    {
                                        if (currentReqCntr > 0)
                                        {
                                            transferTime += FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            if (transferTime >= this.pageProgramLatency)
                                            {
                                                transferTime -= FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                                candidateWriteReqsAsList[currentReqCntr] = null;
                                                currentReqCntr--;
                                                continue;
                                            }
                                            multiDieFlag = true;
                                        }
                                        targetAddress.DieID = targetChip.CurrentActiveDieID;
                                        targetChip.CurrentActiveDieID = (targetAddress.DieID + 1) % this.DieNoPerChip;
                                        targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                        targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % this.PlaneNoPerDie;
                                        targetChip.Dies[targetAddress.DieID].Planes[targetAddress.PlaneID].CommandAssigned = true;
                                        AllocatePPNInPlane(writeReq.Value);
                                        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                    }
                                    else
                                    {
                                        transferTime += FCC.MultiPlaneProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                        if (transferTime >= this.pageProgramLatency)
                                        {
                                            transferTime -= FCC.MultiPlaneProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            candidateWriteReqsAsList[currentReqCntr] = null;
                                            continue;
                                        }

                                        if (FindLevelPageStrict(targetChip, candidateWriteReqsAsList[currentReqCntr % (int)this.DieNoPerChip], candidateWriteReqsAsList[currentReqCntr]))
                                        {
                                            multiPlaneFlag = true;
                                            sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                        }
                                        else
                                        {
                                            transferTime -= FCC.MultiPlaneProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                            candidateWriteReqsAsList[currentReqCntr] = null;
                                        }
                                    }
                                }
                            #region PlanePrioritized
                            // Plane-level allocation is prioritized over die level allocation
                            // {
                            //    uint assignedDieCount = 0;
                            //    InternalRequest myfirstReq = null;
                            //    for (var writeReq = executionList.First; writeReq != null; writeReq = writeReq.Next, currentReqCntr++)
                            //    {
                            //        targetAddress = writeReq.Value.TargetPageAddress;
                            //        targetAddress.DieID = targetChip.CurrentActiveDieID;
                            //        if (myfirstReq == null)
                            //        {
                            //            targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                            //            AllocatePPNInPlane(writeReq.Value);
                            //            targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % this.PlaneNoPerDie;
                            //            myfirstReq = writeReq.Value;
                            //        }
                            //        else
                            //        {
                            //            if (FindLevelPageStrict(targetChip, myfirstReq, writeReq.Value))
                            //                multiPlaneFlag = true;
                            //            else {
                            //                writeReq.Value.TargetPageAddress.DieID = uint.MaxValue;
                            //                writeReq.Value.TargetPageAddress.PlaneID = uint.MaxValue;
                            //                assignedDieCount++;
                            //                targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % this.DieNoPerChip;
                            //                if (assignedDieCount == DieNoPerChip)
                            //                    break;
                            //                multiDieFlag = true;
                            //                targetAddress.DieID = targetChip.CurrentActiveDieID;
                            //                targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                            //                AllocatePPNInPlane(writeReq.Value);
                            //                targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % this.PlaneNoPerDie;
                            //                myfirstReq = writeReq.Value;
                            //            }
                            //        }

                            //        candidateWriteReqsAsList[writeReq.Value.TargetPageAddress.DieID * this.PlaneNoPerDie + writeReq.Value.TargetPageAddress.PlaneID] = writeReq.Value;
                            //        sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                            //    }
                            //}
                            #endregion

                            executionList.Clear();
                            if (multiDieFlag && multiPlaneFlag)
                            {
                                if (GarbageCollector.TB_Enabled)
                                {
                                    for (uint dieCntr = 0; dieCntr < this.DieNoPerChip; dieCntr++)
                                        for (uint planeCntr = 0; planeCntr < this.PlaneNoPerDie; planeCntr++)
                                            if (candidateWriteReqsAsList[dieCntr * PlaneNoPerDie + planeCntr] != null)
                                            {
                                                candidateWriteReqsAsList[dieCntr * PlaneNoPerDie + planeCntr].ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                                executionList.AddLast(candidateWriteReqsAsList[dieCntr * PlaneNoPerDie + planeCntr]);
                                            }
                                }
                                else
                                {
                                    //We start from last die since it probably has lower number of requests.
                                    //Hence the impact of data transmission on the overall response time is declined.
                                    for (uint dieCntr = 0; dieCntr < this.DieNoPerChip; dieCntr++)
                                    {
                                        candidateWriteReqsAsList[dieCntr].ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                        executionList.AddLast(candidateWriteReqsAsList[dieCntr]);
                                        for (uint reqCntr = this.DieNoPerChip; reqCntr < candidateWriteReqsAsList.Length; reqCntr++)
                                            if (candidateWriteReqsAsList[reqCntr] != null)
                                                if (candidateWriteReqsAsList[reqCntr].TargetPageAddress.DieID == candidateWriteReqsAsList[dieCntr].TargetPageAddress.DieID)
                                                {
                                                    candidateWriteReqsAsList[reqCntr].ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                                    executionList.AddLast(candidateWriteReqsAsList[reqCntr]);
                                                }
                                    }
                                }
                                FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else if (multiPlaneFlag)
                            {
                                for (int reqCntr = 0; reqCntr < candidateWriteReqsAsList.Length; reqCntr++)
                                {
                                    if (candidateWriteReqsAsList[reqCntr] != null)
                                    {
                                        candidateWriteReqsAsList[reqCntr].ExecutionType = InternalRequestExecutionType.Multiplane;
                                        executionList.AddLast(candidateWriteReqsAsList[reqCntr]);
                                    }
                                }
                                FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else if (multiDieFlag)
                            {
                                for (int reqCntr = 0; reqCntr < candidateWriteReqsAsList.Length; reqCntr++)
                                {
                                    if (candidateWriteReqsAsList[reqCntr] != null)
                                    {
                                        candidateWriteReqsAsList[reqCntr].ExecutionType = InternalRequestExecutionType.Interleaved;
                                        executionList.AddLast(candidateWriteReqsAsList[reqCntr]);
                                    }
                                }
                                FCC.SendAdvCommandToChipWR(executionList);
                            }
                            else
                            {
                                for (int i = 0; i < candidateWriteReqsAsList.Length; i++)
                                    if (candidateWriteReqsAsList[i] != null)
                                    {
                                        candidateWriteReqsAsList[i].ExecutionType = InternalRequestExecutionType.Simple;
                                        FCC.SendSimpleCommandToChip(candidateWriteReqsAsList[i]);
                                    }
                            }

                            return;
                        }
                    #endregion
                    #region Interleave
                    case AdvancedCommandType.Interleave:
                        {
                            InternalWriteRequestLinkedList newExecutionList = new InternalWriteRequestLinkedList();
                            targetAddress = firstReq.TargetPageAddress;
                            targetAddress.DieID = targetChip.CurrentActiveDieID;
                            targetChip.CurrentActiveDieID = (targetAddress.DieID + 1) % this.DieNoPerChip;
                            targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                            targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % this.PlaneNoPerDie;
                            AllocatePPNInPlane(firstReq);
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                            newExecutionList.AddLast(firstReq);
                            for (var writeReq = executionList.First.Next; writeReq != null; writeReq = writeReq.Next)
                            {
                                transferTime += FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                if (transferTime >= this.pageProgramLatency)
                                {
                                    transferTime -= FCC.InterleaveProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                    continue;
                                }
                                writeReq.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                targetAddress = writeReq.Value.TargetPageAddress;
                                targetAddress.DieID = targetChip.CurrentActiveDieID;
                                targetChip.CurrentActiveDieID = (targetAddress.DieID + 1) % this.DieNoPerChip;
                                targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % this.PlaneNoPerDie;
                                AllocatePPNInPlane(writeReq.Value);
                                sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                                newExecutionList.AddLast(writeReq.Value);
                            }
                            if (newExecutionList.Count > 1)
                            {
                                newExecutionList.First.Value.ExecutionType = InternalRequestExecutionType.Interleaved;
                                FCC.SendAdvCommandToChipWR(newExecutionList);
                            }
                            else
                            {
                                firstReq.ExecutionType = InternalRequestExecutionType.Simple;
                                FCC.SendSimpleCommandToChip(firstReq);
                            }
                            return;
                        }
                    #endregion
                    #region TwoPlane
                    case AdvancedCommandType.TwoPlaneWrite:
                        {
                            for (var writeReq = executionList.First.Next; writeReq != null; writeReq = writeReq.Next)
                            {
                                transferTime += FCC.MultiPlaneProgramSetup + (writeReq.Value.BodyTransferCycles * FCC.WriteTransferCycleTime);
                                if (writeReq == executionList.First.Next)
                                {
                                    if (transferTime >= this.pageProgramLatency)
                                    {
                                        AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                                        return;
                                    }
                                    firstReq.TargetPageAddress.DieID = targetChip.CurrentActiveDieID;
                                    writeReq.Value.TargetPageAddress.DieID = targetChip.CurrentActiveDieID;
                                    if (!FindLevelPage(firstReq, writeReq.Value))
                                    {
                                        firstReq.TargetPageAddress.DieID = uint.MaxValue;
                                        writeReq.Value.TargetPageAddress.DieID = uint.MaxValue;
                                        AllocatePPNandExecuteSimpleWrite(sourceWriteReqList, firstReq);
                                        return;
                                    }
                                }
                                else
                                {
                                    if (transferTime >= this.pageProgramLatency)
                                    {
                                        while (executionList.Last != writeReq)
                                            executionList.RemoveLast();
                                        executionList.RemoveLast();
                                        break;
                                    }
                                    if (!FindLevelPageStrict(targetChip, firstReq, writeReq.Value))
                                    {
                                        while (executionList.Last != writeReq)
                                            executionList.RemoveLast();
                                        executionList.RemoveLast();
                                        break;
                                    }
                                }
                                writeReq.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                                sourceWriteReqList.Remove(writeReq.Value.RelatedNodeInList);
                            }//for (var writeReq = executionList.First.Next; writeReq != null; writeReq = writeReq.Next)
                            targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % this.DieNoPerChip;

                            executionList.First.Value.ExecutionType = InternalRequestExecutionType.Multiplane;
                            sourceWriteReqList.Remove(firstReq.RelatedNodeInList);
                            FCC.SendAdvCommandToChipWR(executionList);
                            return;
                        }
                    #endregion
                    default:
                        throw new Exception("Unexpected invokation for advanced command execution.");
                }

            }//else (Dynamic Allocation)
            #endregion

        }
         */
    }
}
