using System;
using System.Collections.Generic;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public enum FlashChipStatus { Idle,
        TransferringReadCommandAddress, Reading, TransferringReadData,
        Waiting,
        TransferringWriteCommandAndData, TransferringCopybackWriteCommand, Writing,
        EraseSetup, TransferringEraseCommandAddress, Erasing
    };
    public class FlashChip : MetaComponent
    {
        ///<change>
        ///<date>29/02/2017</date>
        ///<description>Support for write/erase suspension added.</description>
        ///</change>
        /// <summary>
        /// <title>FlashChip</title>
        /// <description> 
        /// </description>
        /// <copyright>Copyright(c)2011</copyright>
        /// <company></company>
        /// <author>Arash Tavakkol ( www.arasht.com )</author>
        /// <version>Version 1.0</version>
        /// <date>2011/12/18</date>
        /// </summary>
        #region StructuralParameters
        public BusChannelBase ConnectedChannel = null;
        public uint ChannelID = uint.MaxValue;
        public uint LocalChipID = uint.MaxValue;         //Flashchip position in its related channel
        public uint OverallChipID = uint.MaxValue;  //Flashchip ID in the entire list of flash chips
        public FlashChipDie[] Dies;
        public uint PlaneNoPerDie;                  //indicate how many planes in a die
        public uint BlockNoPerPlane;                //indicate how many blocks in a plane
        public uint PageNoPerBlock;                 //indicate how many pages in a block
        public uint BlockEraseLimit;                //The maximum number of erase operation for a flash chip
        public uint CurrentActiveDieID;               //channel has serveral states, including idle, command/address transfer,data transfer,unknown
     
        private FlashChipStatus status;
        public uint CurrentExecutingOperationCount = 0;//Used to determine if chips Idle status
        public uint CurrentWaitingTransfers = 0;
        protected ulong readLatency = 0, programLatency = 0, eraseLatency = 0;
        ulong readDataOutputReadyTime = 20;

        public ulong ExpectedFinishTime = 0;
        public ulong IssueTimeOfExecutingRequest = 0;
        public bool Suspended = false;
        public uint TotalSuspensionCount = 0, TotalResumeCount = 0;
        #endregion
        #region StatisticParameters
        public ulong ReadCount;                     //how many read count in the process of workload
        public ulong ProgamCount;
        public ulong EraseCount;
        public ulong totalCommandExecutionPeriod = 0;
        public ulong ThisRoundExecutionStart = ulong.MaxValue, ThisRoundExecutionFinish = ulong.MaxValue;
        public ulong LastTransferStart = ulong.MaxValue, TotalTransferPeriod = 0, TotalTransferPeriodOverlapped = 0;
        #endregion


        #region SetupFunctions
        public FlashChip(
			string id,
            uint channelID,
            uint localChipID,
            uint overallChipID,
            uint dieNo,
            uint PlaneNoPerDie,
            uint BlockNoPerPlane,
            uint PageNoPerBlock,
            uint BlockEraseLimit,
            ulong readLatency,
            ulong programLatency,
            ulong eraseLatency,
            ulong readDataOutputReadyTime
			):base(id)
		{
            this.ChannelID = channelID;
            this.LocalChipID = localChipID;
            this.OverallChipID = overallChipID;
            this.CurrentActiveDieID = 0;
            this.PlaneNoPerDie = PlaneNoPerDie;
            this.BlockNoPerPlane = BlockNoPerPlane;
            this.PageNoPerBlock = PageNoPerBlock;
            this.BlockEraseLimit = BlockEraseLimit;
            this.Dies = new FlashChipDie[dieNo];
            for (uint dieID = 0; dieID < dieNo; dieID++)
                Dies[dieID] = new FlashChipDie(channelID, overallChipID, localChipID, dieID, PlaneNoPerDie, BlockNoPerPlane, PageNoPerBlock);
            this.ReadCount = 0;
            this.ProgamCount = 0;
            this.EraseCount = 0;

            this.readLatency = readLatency;
            this.programLatency = programLatency;
            this.eraseLatency = eraseLatency;
            this.readDataOutputReadyTime = readDataOutputReadyTime;
            this.Status = FlashChipStatus.Idle;

        }
        public override void Validate()
        {
            base.Validate();
            if (this.ConnectedChannel == null)
                throw new ValidationException(string.Format("FlashChip ({0}) has no channel", ID));
            if (this.Dies == null || this.Dies.Length < 1)
                throw new ValidationException(string.Format("FlashChip ({0}) has no Die", ID));
            for (int i = 0; i < this.Dies.Length; i++)
            {
                if (this.Dies[i].Planes == null)
                    throw new ValidationException(string.Format("Die ({0}) has no Planes", ID));
                if (this.Dies[i].Planes.Length < 1)
                    throw new ValidationException(string.Format("Die ({0}) has no Planes", ID));
            }
        }
        #endregion
        public virtual void PerformOperation(InternalRequest internalReq)
        {
            FlashChipDie targetDie = this.Dies[internalReq.TargetPageAddress.DieID];
            if (targetDie.Status != DieStatus.Idle
                && (internalReq.ExecutionType != InternalRequestExecutionType.Multiplane)
                && (internalReq.ExecutionType != InternalRequestExecutionType.InterleavedMultiplane))
                throw new Exception("Executing operation on a busy die");
            if(targetDie.Planes[internalReq.TargetPageAddress.PlaneID].Status == PlaneStatus.Busy)
                throw new Exception("Executing operation on a busy plane");

            targetDie.CurrentExecutingOperationCount++;
            targetDie.Status = DieStatus.Busy;
            targetDie.Planes[internalReq.TargetPageAddress.PlaneID].Status = PlaneStatus.Busy;
            targetDie.Planes[internalReq.TargetPageAddress.PlaneID].CurrentExecutingRequest = internalReq;
            ulong executionTime = 0;
            switch (internalReq.Type)
            {
                case InternalRequestType.Read:
                    this.Status = FlashChipStatus.Reading;
                    /* tR + tRR
                     * According to micron's manual, after read operation accomplishment, we have to wait for ready signals to be driven*/
                    executionTime = readLatency + readDataOutputReadyTime;
                    break;
                case InternalRequestType.Write:
                    this.Status = FlashChipStatus.Writing;
                    executionTime = programLatency;
                    break;
                case InternalRequestType.Clean:
                    this.Status = FlashChipStatus.Erasing;
                    executionTime = eraseLatency;//This is a normal erase requeset
                    break;
                default:
                    throw new Exception("Unhandled operation type");

            }
            internalReq.ExpectedFinishTime = XEngineFactory.XEngine.Time + executionTime;
            internalReq.FlashChipExecutionEvent = new XEvent(internalReq.ExpectedFinishTime, this, internalReq, 0);
            XEngineFactory.XEngine.EventList.InsertXEvent(internalReq.FlashChipExecutionEvent);
            CurrentExecutingOperationCount++;
            if (CurrentExecutingOperationCount == 1)
            {
                ThisRoundExecutionStart = XEngineFactory.XEngine.Time;
                ThisRoundExecutionFinish = ulong.MaxValue;
                ExpectedFinishTime = internalReq.ExpectedFinishTime;
                IssueTimeOfExecutingRequest = internalReq.IssueTime;
            }
        }
        public void Suspend()
        {
            if (Suspended)
                throw new Exception("Suspending a previously suspended chip!");
            Suspended = true;
            TotalSuspensionCount++;

            foreach (FlashChipDie die in Dies)
                foreach (FlashChipPlane plane in die.Planes)
                    if (plane.CurrentExecutingRequest != null)
                    {
                        InternalRequest targetRequest = plane.CurrentExecutingRequest;
                        if (targetRequest.Type == InternalRequestType.Read)
                            throw new Exception("Suspend is not supported for read operations!");

                        targetRequest.RemainingExecutionTime = targetRequest.ExpectedFinishTime - XEngineFactory.XEngine.Time;
                        if (targetRequest.RemainingExecutionTime < 0)
                            throw new Exception("Strange command suspension time occured!");
                        targetRequest.FlashChipExecutionEvent.Removed = true;
                        targetRequest.FlashChipExecutionEvent = null;

                        plane.CurrentExecutingRequest = null;
                        plane.SuspendedExecutingRequest = targetRequest;

                        die.CurrentExecutingOperationCount--;
                        if (die.CurrentExecutingOperationCount == 0)
                            die.Status = DieStatus.Idle;
                        die.Planes[targetRequest.TargetPageAddress.PlaneID].Status = PlaneStatus.Idle;

                        CurrentExecutingOperationCount--;
                    }
        }
        public void Resume()
        {
            if (!Suspended)
                throw new Exception("Resume requested but there is no suspended Flash transaction!");
            Suspended = false;
            TotalResumeCount++;

            foreach (FlashChipDie die in Dies)
                foreach (FlashChipPlane plane in die.Planes)
                    if (plane.SuspendedExecutingRequest != null)
                    {
                        InternalRequest targetRequest = plane.SuspendedExecutingRequest;
                        if (die.Status != DieStatus.Idle
                            && (targetRequest.ExecutionType != InternalRequestExecutionType.Multiplane)
                            && (targetRequest.ExecutionType != InternalRequestExecutionType.InterleavedMultiplane))
                            throw new Exception("Executing operation on a busy die");
                        if (plane.Status == PlaneStatus.Busy)
                            throw new Exception("Executing operation on a busy plane");

                        die.CurrentExecutingOperationCount++;
                        die.Status = DieStatus.Busy;
                        plane.Status = PlaneStatus.Busy;
                        plane.CurrentExecutingRequest = targetRequest;
                        plane.SuspendedExecutingRequest = null;

                        CurrentExecutingOperationCount++;
                        switch (targetRequest.Type)
                        {
                            case InternalRequestType.Read:
                                this.Status = FlashChipStatus.Reading;
                                break;
                            case InternalRequestType.Write:
                                this.Status = FlashChipStatus.Writing;
                                break;
                            case InternalRequestType.Clean:
                                this.Status = FlashChipStatus.Erasing;
                                break;
                            default:
                                throw new Exception("Unhandled operation type");

                        }
                        targetRequest.ExpectedFinishTime = XEngineFactory.XEngine.Time + targetRequest.RemainingExecutionTime;
                        targetRequest.FlashChipExecutionEvent = new XEvent(targetRequest.ExpectedFinishTime, this, targetRequest, 0);
                        XEngineFactory.XEngine.EventList.InsertXEvent(targetRequest.FlashChipExecutionEvent);
                    }

        }

        #region Properties
        public void sameBlockStatistics(string id, System.Xml.XmlTextWriter writer)
        {
            writer.WriteStartElement(id);
            writer.WriteAttributeString("ID", ID.ToString());
            for (int blockNo = 0; blockNo < BlockNoPerPlane; blockNo++)
            {
                writer.WriteStartElement(id + "_blockforchip");
                writer.WriteAttributeString("block_ID", blockNo.ToString());
                writer.WriteAttributeString("validVariance", calculateVariaceValidForFlashChip(blockNo).ToString());
                writer.WriteAttributeString("invalidVariance", calculateVariaceinvalidForFlashChip(blockNo).ToString());
                writer.WriteAttributeString("freeVariance", calculateVariaceFreeForFlashChip(blockNo).ToString());
                writer.WriteAttributeString("validAvg", calculateAverageValidForFlashChip(blockNo).ToString());
                writer.WriteAttributeString("invalidAvg", calculateAverageInvalidForFlashChip(blockNo).ToString());
                writer.WriteAttributeString("freeAvg", calculateAverageFreeForFlashChip(blockNo).ToString());
                writer.WriteEndElement();
            }
            for (int dieCntr = 0; dieCntr < Dies.Length; dieCntr++)
            {
                writer.WriteStartElement(id + "_die");
                writer.WriteAttributeString("ID", ID.ToString() + ".die." + dieCntr);
                for (int blockNo = 0; blockNo < BlockNoPerPlane; blockNo++)
                {
                    writer.WriteStartElement(id + "_blockfordie");
                    writer.WriteAttributeString("block_ID", blockNo.ToString());
                    writer.WriteAttributeString("validVariance", calculateVariaceValidForDie(blockNo, dieCntr).ToString());
                    writer.WriteAttributeString("invalidVariance", calculateVariaceinValidForDie(blockNo, dieCntr).ToString());
                    writer.WriteAttributeString("freeVariance", calculateVariaceFreeForDie(blockNo, dieCntr).ToString());
                    writer.WriteAttributeString("validAvg", calculateAvgValidForDie(blockNo, dieCntr).ToString());
                    writer.WriteAttributeString("invalidAvg", calculateAvgInvalidForDie(blockNo, dieCntr).ToString());
                    writer.WriteAttributeString("freeAvg", calculateAvgFreeForDie(blockNo, dieCntr).ToString());
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        public double calculateVariaceValidForDie(int blockNo, int dieCntr)
        {
            double average = calculateAvgValidForDie(blockNo, dieCntr);
            ulong sumOfSquare = 0;
            for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
            {
                FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                ulong validCount = PageNoPerBlock - targetBlock.InvalidPageNo - targetBlock.FreePageNo;
                sumOfSquare += validCount * validCount;
            }
            return (double)sumOfSquare / ((double)(PlaneNoPerDie)) - average * average;

        }
        public double calculateVariaceinValidForDie(int blockNo, int dieCntr)
        {
            double average = calculateAvgInvalidForDie(blockNo, dieCntr);
            ulong sumOfSquare = 0;

            for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
            {
                FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                sumOfSquare += targetBlock.InvalidPageNo * targetBlock.InvalidPageNo;
            }
            return (double)sumOfSquare / ((double)(PlaneNoPerDie)) - average * average;
        }
        public double calculateVariaceFreeForDie(int blockNo, int dieCntr)
        {
            double average = calculateAvgFreeForDie(blockNo, dieCntr);
            ulong sumOfSquare = 0;

            for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
            {
                FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                sumOfSquare += targetBlock.FreePageNo * targetBlock.FreePageNo;
            }

            return (double)sumOfSquare / ((double)(PlaneNoPerDie)) - average * average;
        }
        public double calculateAvgValidForDie(int blockNo, int dieCntr)
        {
            ulong sumOfValid = 0;
            for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
            {
                FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                sumOfValid += PageNoPerBlock - targetBlock.InvalidPageNo - targetBlock.FreePageNo;
            }
            return ((double)sumOfValid) / ((double)(PlaneNoPerDie));
        }
        public double calculateAvgInvalidForDie(int blockNo, int dieCntr)
        {
            ulong sumOfInvalid = 0;
            for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
            {
                FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                sumOfInvalid += targetBlock.InvalidPageNo;
            }
            return ((double)sumOfInvalid) / ((double)(PlaneNoPerDie));
        }
        public double calculateAvgFreeForDie(int blockNo, int dieCntr)
        {
            ulong sumOfFree = 0;
            for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
            {
                FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                sumOfFree += targetBlock.FreePageNo;
            }
            return ((double)sumOfFree) / ((double)(PlaneNoPerDie));
        }
        public double calculateVariaceValidForFlashChip(int blockNo)
        {
            double average = calculateAverageValidForFlashChip(blockNo);
            ulong sumOfSquare = 0;
            for (int dieCntr = 0; dieCntr < Dies.Length; dieCntr++)
            {
                for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
                {
                    FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                    ulong validCount = PageNoPerBlock - targetBlock.InvalidPageNo - targetBlock.FreePageNo;
                    sumOfSquare += validCount * validCount;
                }
            }
            return (double)sumOfSquare / ((double)(Dies.Length * PlaneNoPerDie)) - average * average;
        }
        public double calculateVariaceinvalidForFlashChip(int blockNo)
        {
            double average = calculateAverageInvalidForFlashChip(blockNo);
            ulong sumOfSquare = 0;
            for (int dieCntr = 0; dieCntr < Dies.Length; dieCntr++)
            {
                for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
                {
                    FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                    sumOfSquare += targetBlock.InvalidPageNo * targetBlock.InvalidPageNo;
                }
            }
            return (double)sumOfSquare / ((double)(Dies.Length * PlaneNoPerDie)) - average * average;
        }
        public double calculateVariaceFreeForFlashChip(int blockNo)
        {
            double average = calculateAverageFreeForFlashChip(blockNo);
            ulong sumOfSquare = 0;
            for (int dieCntr = 0; dieCntr < Dies.Length; dieCntr++)
            {
                for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
                {
                    FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                    sumOfSquare += targetBlock.FreePageNo * targetBlock.FreePageNo;
                }
            }
            return (double)sumOfSquare / ((double)(Dies.Length * PlaneNoPerDie)) - average * average;
        }
        public double calculateAverageValidForFlashChip(int blockNo)
        {
            ulong sumOfValid = 0;
            for (int dieCntr = 0; dieCntr < Dies.Length; dieCntr++)
            {
                for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
                {
                    FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                    sumOfValid += PageNoPerBlock - targetBlock.InvalidPageNo - targetBlock.FreePageNo;
                }
            }
            return ((double)sumOfValid) / ((double)(Dies.Length * PlaneNoPerDie));
        }
        public double calculateAverageInvalidForFlashChip(int blockNo)
        {
            ulong sumOfInvalid = 0;
            for (int dieCntr = 0; dieCntr < Dies.Length; dieCntr++)
            {
                for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
                {
                    FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                    sumOfInvalid += targetBlock.InvalidPageNo;
                }
            }
            return ((double)sumOfInvalid) / ((double)(Dies.Length * PlaneNoPerDie));
        }
        public double calculateAverageFreeForFlashChip(int blockNo)
        {
            ulong sumOfFree = 0;
            for (int dieCntr = 0; dieCntr < Dies.Length; dieCntr++)
            {
                for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
                {
                    FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockNo];
                    sumOfFree += targetBlock.FreePageNo;
                }
            }
            return ((double)sumOfFree) / ((double)(Dies.Length * PlaneNoPerDie));
        }

        public override void Snapshot(string id, System.Xml.XmlTextWriter writer)
        {
            writer.WriteStartElement(id);
            writer.WriteAttributeString("ID", ID.ToString());
            writer.WriteAttributeString("ReadCount", ReadCount.ToString());
            writer.WriteAttributeString("ProgamCount", ProgamCount.ToString());
            writer.WriteAttributeString("EraseCount", EraseCount.ToString());
            writer.WriteAttributeString("TotalExecutionPeriodRatio", ((double)totalCommandExecutionPeriod / (double)XEngineFactory.XEngine.Time).ToString());
            writer.WriteAttributeString("TotalTransferPeriodRatio", ((double)TotalTransferPeriod / (double)XEngineFactory.XEngine.Time).ToString());
            writer.WriteAttributeString("TotalTransferPeriodOverlapped", ((double)TotalTransferPeriodOverlapped / (double)XEngineFactory.XEngine.Time).ToString());
            for (int dieCntr = 0; dieCntr < Dies.Length; dieCntr++)
            {
                writer.WriteStartElement(id + "_Die");
                writer.WriteAttributeString("ID", ID.ToString() + ".die." + dieCntr );
                writer.WriteAttributeString("TotalReadExecutionPeriodRatio", ((double)Dies[dieCntr].TotalReadExecutionPeriod / (double)XEngineFactory.XEngine.Time).ToString());
                writer.WriteAttributeString("TotalProgramExecutionPeriodRatio", ((double)Dies[dieCntr].TotalProgramExecutionPeriod / (double)XEngineFactory.XEngine.Time).ToString());
                writer.WriteAttributeString("TotalEraseExecutionPeriodRatio", ((double)Dies[dieCntr].TotalEraseExecutionPeriod / (double)XEngineFactory.XEngine.Time).ToString());
                writer.WriteAttributeString("TotalTransferPeriodRatio", ((double)Dies[dieCntr].TotalTransferPeriod / (double)XEngineFactory.XEngine.Time).ToString());
                for (int planeCntr = 0; planeCntr < PlaneNoPerDie; planeCntr++)
                {
                    writer.WriteStartElement(id + "_Plane");
                    writer.WriteAttributeString("ID", ID.ToString() + ".plane." + dieCntr + "." + planeCntr);

                    uint freePagesNo = 0, totalPagesNo = 0;

                    uint totalBlockEraseCount = 0, maxBlockErasureCount = 0, minBlockErasureCount = uint.MaxValue;
                    uint totalValidPagesCount = 0, maxBlockValidPagesCount = 0, minBlockValidPagesCount = uint.MaxValue;
                    uint totalInvalidPagesCount = 0, maxBlockInvalidPagesCount = 0, minBlockInvalidPagesCount = uint.MaxValue;

                    double averageBlockEraseCount = 0, blockEraseStdDev = 0;
                    double averageBlockValidPagesCount, blockValidPagesCountStdDev = 0, averageBlockInvalidPagesCount, blockInvalidPagesCountStdDev = 0;

                    for (int blockCntr = 0; blockCntr < BlockNoPerPlane; blockCntr++)
                    {
                        FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockCntr];

                        totalBlockEraseCount += targetBlock.EraseCount;
                        if (targetBlock.EraseCount > maxBlockErasureCount)
                            maxBlockErasureCount = targetBlock.EraseCount;
                        if (targetBlock.EraseCount < minBlockErasureCount)
                            minBlockErasureCount = targetBlock.EraseCount;

                        uint validpagecount = PageNoPerBlock - targetBlock.InvalidPageNo - targetBlock.FreePageNo;
                        totalValidPagesCount += validpagecount;
                        if (validpagecount > maxBlockValidPagesCount)
                            maxBlockValidPagesCount = validpagecount;
                        if (validpagecount < minBlockValidPagesCount)
                            minBlockValidPagesCount = validpagecount;

                        totalPagesNo += (uint)Dies[dieCntr].Planes[planeCntr].Blocks[blockCntr].Pages.Length;
                        totalInvalidPagesCount += targetBlock.InvalidPageNo;
                        if (targetBlock.InvalidPageNo > maxBlockInvalidPagesCount)
                            maxBlockInvalidPagesCount = targetBlock.InvalidPageNo;
                        if (targetBlock.InvalidPageNo < minBlockInvalidPagesCount)
                            minBlockInvalidPagesCount = targetBlock.InvalidPageNo;

                        freePagesNo += targetBlock.FreePageNo;
                    }

                    averageBlockEraseCount = (double)totalBlockEraseCount / (double)(BlockNoPerPlane);
                    averageBlockValidPagesCount = (double)totalValidPagesCount / (double)(BlockNoPerPlane);
                    averageBlockInvalidPagesCount = (double)totalInvalidPagesCount / (double)(BlockNoPerPlane);

                    for (uint blockCntr = 0; blockCntr < BlockNoPerPlane; blockCntr++)
                    {
                        FlashChipBlock targetBlock = Dies[dieCntr].Planes[planeCntr].Blocks[blockCntr];
                        uint validPagesCount = PageNoPerBlock - targetBlock.InvalidPageNo - targetBlock.FreePageNo;
                        blockEraseStdDev += Math.Pow((averageBlockEraseCount - targetBlock.EraseCount), 2);
                        blockValidPagesCountStdDev += Math.Pow((averageBlockValidPagesCount - validPagesCount), 2);
                        blockInvalidPagesCountStdDev += Math.Pow((averageBlockInvalidPagesCount - targetBlock.InvalidPageNo), 2);
                    }
                    blockEraseStdDev = Math.Sqrt((double)blockEraseStdDev / (double)BlockNoPerPlane);
                    blockValidPagesCountStdDev = Math.Sqrt((double)blockValidPagesCountStdDev / (double)BlockNoPerPlane);
                    blockInvalidPagesCountStdDev = Math.Sqrt((double)blockInvalidPagesCountStdDev / (double)BlockNoPerPlane);

                    writer.WriteAttributeString("ReadCount", Dies[dieCntr].Planes[planeCntr].ReadCount.ToString());
                    writer.WriteAttributeString("ProgramCount", Dies[dieCntr].Planes[planeCntr].ProgamCount.ToString());
                    writer.WriteAttributeString("EraseCount", Dies[dieCntr].Planes[planeCntr].EraseCount.ToString());
                    writer.WriteAttributeString("TotalPagesNo", totalPagesNo.ToString());
                    writer.WriteAttributeString("ValidPagesNo", totalValidPagesCount.ToString());
                    writer.WriteAttributeString("FreePagesNo", freePagesNo.ToString());
                    writer.WriteAttributeString("InvalidPagesNo", totalInvalidPagesCount.ToString());
                    writer.WriteAttributeString("BlockEraseCountAverage", averageBlockEraseCount.ToString());
                    writer.WriteAttributeString("BlockEraseCountStdDev", blockEraseStdDev.ToString());
                    writer.WriteAttributeString("BlockEraseCountMin", minBlockErasureCount.ToString());
                    writer.WriteAttributeString("BlockEraseCountMax", maxBlockErasureCount.ToString());
                    writer.WriteAttributeString("BlockValidPagesCountAverage", averageBlockValidPagesCount.ToString());
                    writer.WriteAttributeString("BlockValidPagesCountStdDev", blockValidPagesCountStdDev.ToString());
                    writer.WriteAttributeString("BlockValidPagesCountMin", minBlockValidPagesCount.ToString());
                    writer.WriteAttributeString("BlockValidPagesCountMax", maxBlockValidPagesCount.ToString());
                    writer.WriteAttributeString("BlockInvalidPagesCountAverage", averageBlockInvalidPagesCount.ToString());
                    writer.WriteAttributeString("BlockInvalidPagesCountStdDev", blockInvalidPagesCountStdDev.ToString());
                    writer.WriteAttributeString("BlockInvalidPagesCountMin", minBlockInvalidPagesCount.ToString());
                    writer.WriteAttributeString("BlockInvalidPagesCountMax", maxBlockInvalidPagesCount.ToString());
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();//Die
            }
            writer.WriteEndElement();//Flashchip
        }

        public FlashChipStatus Status
        {
            get { return this.status; }
            set
            {
                if (value == FlashChipStatus.Idle && this.status != FlashChipStatus.Idle)
                    this.ConnectedChannel.BusyChipCount--;
                else if (value != FlashChipStatus.Idle && this.status == FlashChipStatus.Idle)
                    this.ConnectedChannel.BusyChipCount++;
                this.status = value;
            }
        }
        #endregion

        #region Event Handlers
        public override void ProcessXEvent(XEvent e)
        {
            if (e.Removed)
                return;
            //Just only one xevent: OperationFinished
            InternalRequest targetRequest = e.Parameters as InternalRequest;
            FlashChipDie targetDie = this.Dies[targetRequest.TargetPageAddress.DieID];

            targetDie.CurrentExecutingOperationCount--;
            if (targetDie.CurrentExecutingOperationCount == 0)
                targetDie.Status = DieStatus.Idle;
            targetDie.Planes[targetRequest.TargetPageAddress.PlaneID].Status = PlaneStatus.Idle;
            targetDie.Planes[targetRequest.TargetPageAddress.PlaneID].CurrentExecutingRequest = null;

            CurrentExecutingOperationCount--;
            if (CurrentExecutingOperationCount == 0)
            {
                Status = FlashChipStatus.Idle;
                totalCommandExecutionPeriod += XEngineFactory.XEngine.Time - ThisRoundExecutionStart;
                ThisRoundExecutionFinish = XEngineFactory.XEngine.Time;
            }

            switch (targetRequest.Type)
            {
                case InternalRequestType.Read:
                    this.ReadCount++;
                    targetDie.Planes[targetRequest.TargetPageAddress.PlaneID].ReadCount++;
                    targetRequest.ExecutionTime += this.readLatency;
                    this.Status = FlashChipStatus.Waiting;

                    //Either a normal read (waiting for read data transfer) or a copyback read (waiting for write execution)
                    this.CurrentWaitingTransfers++;

                    if (targetDie.Status == DieStatus.Idle)
                        targetDie.TotalReadExecutionPeriod += readLatency;
                    break;
                case InternalRequestType.Write:
                    this.ProgamCount++;
                    targetDie.Planes[targetRequest.TargetPageAddress.PlaneID].ProgamCount++;
                    targetRequest.ExecutionTime += this.programLatency;

                    if (targetDie.Status == DieStatus.Idle)
                        targetDie.TotalProgramExecutionPeriod += programLatency;
                    break;
                case InternalRequestType.Clean:
                    this.EraseCount++;
                    targetDie.Planes[targetRequest.TargetPageAddress.PlaneID].EraseCount++;
                    targetRequest.ExecutionTime += this.eraseLatency;
                    FlashChipBlock targetBlock = this.Dies[targetRequest.TargetPageAddress.DieID].Planes[targetRequest.TargetPageAddress.PlaneID].Blocks[targetRequest.TargetPageAddress.BlockID];
                    targetDie.Planes[targetRequest.TargetPageAddress.PlaneID].FreePagesNo += (PageNoPerBlock - targetBlock.FreePageNo);
                    targetBlock.FreePageNo = PageNoPerBlock;
                    targetBlock.InvalidPageNo = 0;
                    targetBlock.LastWrittenPageNo = -1;
                    targetBlock.EraseCount++;
                    for (int i = 0; i < PageNoPerBlock; i++)
                    {
                        targetBlock.Pages[i].StreamID = FlashChipPage.PG_NOSTREAM;
                        targetBlock.Pages[i].ValidStatus = FlashChipPage.PG_FREE;
                        targetBlock.Pages[i].LPN = ulong.MaxValue;
                    }

                    #region UpdateFastGCData
                    targetDie.BlockInfoAbstract[targetBlock.BlockID].FreePageNo += PageNoPerBlock;
                    targetDie.BlockInfoAbstract[targetBlock.BlockID].InvalidPageNo = 0;
                    targetDie.BlockInfoAbstract[targetBlock.BlockID].EraseCount++;
                    #endregion

                    if (targetDie.Status == DieStatus.Idle)
                        targetDie.TotalEraseExecutionPeriod += eraseLatency;
                    break;
                default:
                    break;
            }
            OnInternalRequestServiced(targetRequest);
        }
        #endregion
    
        #region Delegates
        public delegate void InternalRequestServicedHandler(InternalRequest targetRequest, uint rowID, FlashChip targetFlashChip);
        public event InternalRequestServicedHandler onInternalRequestServiced;
        protected virtual void OnInternalRequestServiced(InternalRequest targetRequest)
        {
            if (onInternalRequestServiced != null)
                onInternalRequestServiced(targetRequest, ChannelID, this);
        }
        #endregion

    }
}
