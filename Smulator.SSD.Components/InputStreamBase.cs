using System;
using System.Collections.Generic;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public enum StreamPriorityClass { Urgent = 0, High = 1, Medium = 2, Low = 3};
    public abstract class InputStreamBase
    {
        string _flowName;
        public StreamPriorityClass PriorityClass = StreamPriorityClass.Low;
        public static readonly int PriorityClassCount = 4;//The value assigned to this variable must always reflect the number of 
        public LinkedList<IORequest> SubmissionQueue = new LinkedList<IORequest>();//This is the NVMe submission queue dedicated to this input stream
        public LinkedListNode<IORequest> HeadRequest = null;
        public AddressMappingDomain AddressMappingDomain = null;
        public ulong SimulationStopTime = 0;
        public ulong ReceivedRequestCount = 0, ReceivedReadRequestCount = 0, ReceivedWriteRequestCount = 0;
        public ulong TimeOffset = 0;
        public ulong AddressOffset;

        protected uint _id = 0;
        private static uint _lastID = 0;
        private bool gcStarted = false;
        private ulong changeTime = 0;
        private bool mathOverflowOccuredInStatistics = false;
        public bool HasRequest = false;
        public ulong NumberOfRequestsToGenerate = 0;

        #region StatisticsParameters
        ulong ignoredRequestsCount = 0;
        ulong handledRequestsCount = 0, handledRequestsCount_BGC = 0, handledRequestsCount_AGC = 0;
        ulong handledReadRequestsCount = 0, handledReadRequestsCount_BGC = 0, handledReadRequestsCount_AGC = 0;
        ulong handledWriteRequestsCount = 0, handledWriteRequestsCount_BGC = 0, handledWriteRequestsCount_AGC = 0;
        ulong avgResponseTime_BGC, avgResponseTimeR_BGC, avgResponseTimeW_BGC = 0; //To prevent from overflow, we assume the unit is nanoseconds
        ulong sumResponseTime = 0, sumResponseTimeR = 0, sumResponseTimeW = 0;//To prevent from overflow, we assume the unit is microseconds
        ulong sumResponseTime_AGC = 0, sumResponseTimeR_AGC = 0, sumResponseTimeW_AGC = 0;//To prevent from overflow, we assume the unit is microseconds
        ulong minResponseTime_BGC, minResponseTimeR_BGC, minResponseTimeW_BGC, maxResponseTime_BGC, maxResponseTimeR_BGC, maxResponseTimeW_BGC;
        ulong minResponseTime = ulong.MaxValue, minResponseTimeR = ulong.MaxValue, minResponseTimeW = ulong.MaxValue;
        ulong maxResponseTime = 0, maxResponseTimeR = 0, maxResponseTimeW = 0;
        ulong minResponseTime_AGC = ulong.MaxValue, minResponseTimeR_AGC = ulong.MaxValue, minResponseTimeW_AGC = ulong.MaxValue;
        ulong maxResponseTime_AGC = 0, maxResponseTimeR_AGC = 0, maxResponseTimeW_AGC = 0;
        ulong transferredBytesCount_BGC = 0, transferredBytesCount = 0, transferredBytesCount_AGC = 0;
        ulong transferredBytesCountR_BGC = 0, transferredBytesCountR = 0, transferredBytesCountR_AGC = 0;
        ulong transferredBytesCountW_BGC = 0, transferredBytesCountW = 0, transferredBytesCountW_AGC = 0;

        ulong averageOperationLifeTime_BGC = 0, averageOperationExecutionTime_BGC, averageOperationTransferTime_BGC = 0, averageOperationWaitingTime_BGC = 0;
        ulong sumOfInternalRequestExecutionTime = 0, sumOfInternalRequestLifeTime = 0, sumOfInternalRequestTransferTime = 0, sumOfInternalRequestWaitingTime = 0;
        ulong sumOfInternalRequestExecutionTime_AGC = 0, sumOfInternalRequestLifeTime_AGC = 0, sumOfInternalRequestTransferTime_AGC = 0, sumOfInternalRequestWaitingTime_AGC = 0;
        ulong averageReadOperationLifeTime_BGC = 0, averageReadOperationExecutionTime_BGC, averageReadOperationTransferTime_BGC = 0, averageReadOperationWaitingTime_BGC = 0;
        ulong sumOfReadRequestExecutionTime = 0, sumOfReadRequestLifeTime = 0, sumOfReadRequestTransferTime = 0, sumOfReadRequestWaitingTime = 0;
        ulong sumOfReadRequestExecutionTime_AGC = 0, sumOfReadRequestLifeTime_AGC = 0, sumOfReadRequestTransferTime_AGC = 0, sumOfReadRequestWaitingTime_AGC = 0;
        ulong averageProgramOperationLifeTime_BGC = 0, averageProgramOperationExecutionTime_BGC, averageProgramOperationTransferTime_BGC = 0, averageProgramOperationWaitingTime_BGC = 0;
        ulong sumOfProgramRequestExecutionTime = 0, sumOfProgramRequestLifeTime = 0, sumOfProgramRequestTransferTime = 0, sumOfProgramRequestWaitingTime = 0;
        ulong sumOfProgramRequestExecutionTime_AGC = 0, sumOfProgramRequestLifeTime_AGC = 0, sumOfProgramRequestTransferTime_AGC = 0, sumOfProgramRequestWaitingTime_AGC = 0;
        ulong totalFlashOperations = 0, totalReadOperations = 0, totalProgramOperations = 0;
        ulong totalFlashOperations_AGC = 0, totalReadOperations_AGC = 0, totalProgramOperations_AGC = 0;

        //Logging variables
        ulong thisRoundSumResponseTime = 0, thisRoundSumResponseTimeR = 0, thisRoundSumResponseTimeW = 0;
        ulong thisRoundHandledReadRequestsCount = 0, thisRoundHandledWriteRequestsCount = 0;
        #endregion

        public InputStreamBase(string flowName, StreamPriorityClass priorityClass, AddressMappingDomain addressMappingDomain)
        {
            _id = _lastID++;
            _flowName = flowName;
            PriorityClass = priorityClass;

            AddressMappingDomain = addressMappingDomain;
        }
        public bool WriteToCompletionQueue(IORequest IOReq, ulong requestProcessingTime)
        {
            SubmissionQueue.Remove(IOReq.RelatedNodeInList);
            IOReq.ResponseTime = XEngineFactory.XEngine.Time - IOReq.InitiationTime + requestProcessingTime;
            updateStatistics(IOReq);
            return true;
        }
        public bool WriteToCompletionQueue(InternalRequest internalReq, ulong requestProcessingTime)
        {
            sumOfInternalRequestLifeTime += (XEngineFactory.XEngine.Time - internalReq.IssueTime) / 1000;//Nanoseconds is converted to microseconds
            sumOfInternalRequestExecutionTime += internalReq.ExecutionTime / 1000;//Nanoseconds is converted to microseconds
            sumOfInternalRequestTransferTime += internalReq.TransferTime / 1000;//Nanoseconds is converted to microseconds
            sumOfInternalRequestWaitingTime += (XEngineFactory.XEngine.Time - (internalReq.IssueTime + internalReq.ExecutionTime + internalReq.TransferTime)) / 1000;//Nanoseconds is converted to microseconds
            totalFlashOperations++;

            if (gcStarted)
            {
                sumOfInternalRequestLifeTime_AGC += (XEngineFactory.XEngine.Time - internalReq.IssueTime) / 1000;
                sumOfInternalRequestExecutionTime_AGC += internalReq.ExecutionTime / 1000;
                sumOfInternalRequestTransferTime_AGC += internalReq.TransferTime / 1000;
                sumOfInternalRequestWaitingTime_AGC += (XEngineFactory.XEngine.Time - (internalReq.IssueTime + internalReq.ExecutionTime + internalReq.TransferTime)) / 1000;
                totalFlashOperations_AGC++;
            }

            if (internalReq.Type == InternalRequestType.Read)
            {
                sumOfReadRequestLifeTime += (XEngineFactory.XEngine.Time - internalReq.IssueTime) / 1000;
                sumOfReadRequestExecutionTime += internalReq.ExecutionTime / 1000;
                sumOfReadRequestTransferTime += internalReq.TransferTime / 1000;
                sumOfReadRequestWaitingTime += (XEngineFactory.XEngine.Time - (internalReq.IssueTime + internalReq.ExecutionTime + internalReq.TransferTime)) / 1000;
                totalReadOperations++;
                if (gcStarted)
                {
                    sumOfReadRequestLifeTime_AGC += (XEngineFactory.XEngine.Time - internalReq.IssueTime) / 1000;
                    sumOfReadRequestExecutionTime_AGC += internalReq.ExecutionTime / 1000;
                    sumOfReadRequestTransferTime_AGC += internalReq.TransferTime / 1000;
                    sumOfReadRequestWaitingTime_AGC += (XEngineFactory.XEngine.Time - (internalReq.IssueTime + internalReq.ExecutionTime + internalReq.TransferTime)) / 1000;
                    totalReadOperations_AGC++;
                }
            }
            else
            {
                sumOfProgramRequestLifeTime += (XEngineFactory.XEngine.Time - internalReq.IssueTime) / 1000;
                sumOfProgramRequestExecutionTime += internalReq.ExecutionTime / 1000;
                sumOfProgramRequestTransferTime += internalReq.TransferTime / 1000;
                sumOfProgramRequestWaitingTime += (XEngineFactory.XEngine.Time - (internalReq.IssueTime + internalReq.ExecutionTime + internalReq.TransferTime)) / 1000;
                totalProgramOperations++;
                if (gcStarted)
                {
                    sumOfProgramRequestLifeTime_AGC += (XEngineFactory.XEngine.Time - internalReq.IssueTime) / 1000;
                    sumOfProgramRequestExecutionTime_AGC += internalReq.ExecutionTime / 1000;
                    sumOfProgramRequestTransferTime_AGC += internalReq.TransferTime / 1000;
                    sumOfProgramRequestWaitingTime_AGC += (XEngineFactory.XEngine.Time - (internalReq.IssueTime + internalReq.ExecutionTime + internalReq.TransferTime)) / 1000;
                    totalProgramOperations_AGC++;
                }
            }
            IORequest targetIORequest = internalReq.RelatedIORequest;
            internalReq.RelatedIORequest = null;
            targetIORequest.InternalRequestList.Remove(internalReq);
            if (targetIORequest.InternalRequestList.Count == 0)
            {
                SubmissionQueue.Remove(targetIORequest.RelatedNodeInList);
                targetIORequest.ResponseTime = XEngineFactory.XEngine.Time - targetIORequest.InitiationTime + requestProcessingTime;
                return updateStatistics(targetIORequest);
            }//if (targetIORequest.InternalRequestList.Count == 0)

            return false;
        }
        public abstract IORequest GetNextIORequest(HostInterface hostInterface, bool foldAddress, bool ignoreUnallocatedReads);
        public abstract void Close();
        public abstract void Preprocess(HostInterface hostInterface, ulong simulationStopTime, bool foldAddress, bool ignoreUnallocatedReads);
        public virtual void FirstGCEvent()
        {
            gcStarted = true;

            handledRequestsCount_BGC = handledRequestsCount;
            handledReadRequestsCount_BGC = handledReadRequestsCount;
            handledWriteRequestsCount_BGC = handledWriteRequestsCount;
            avgResponseTime_BGC = AvgResponseTime;
            minResponseTime_BGC = minResponseTime;
            maxResponseTime_BGC = maxResponseTime;
            avgResponseTimeR_BGC = AvgResponseTimeR;
            minResponseTimeR_BGC = minResponseTimeR;
            maxResponseTimeR_BGC = maxResponseTimeR;
            avgResponseTimeW_BGC = AvgResponseTimeW;
            minResponseTimeW_BGC = minResponseTimeW;
            maxResponseTimeW_BGC = maxResponseTimeW;

            transferredBytesCount_BGC = transferredBytesCount;
            transferredBytesCountR_BGC = transferredBytesCountR;
            transferredBytesCountW_BGC = transferredBytesCountW;

            averageOperationLifeTime_BGC = AverageCMDLifeTime;
            averageOperationExecutionTime_BGC = AverageCMDExecutionTime;
            averageOperationTransferTime_BGC = AverageCMDTransferTime;
            averageOperationWaitingTime_BGC = AverageCMDWaitingTime;


            averageReadOperationLifeTime_BGC = AverageReadCMDLifeTime;
            averageReadOperationExecutionTime_BGC = AverageReadCMDExecutionTime;
            averageReadOperationTransferTime_BGC = AverageReadCMDTransferTime;
            averageReadOperationWaitingTime_BGC = AverageReadCMDWaitingTime;


            averageProgramOperationLifeTime_BGC = AverageProgramCMDLifeTime;
            averageProgramOperationExecutionTime_BGC = AverageProgramCMDExecutionTime;
            averageProgramOperationTransferTime_BGC = AverageProgramCMDTransferTime;
            averageProgramOperationWaitingTime_BGC = AverageProgramCMDWaitingTime;

            changeTime = XEngineFactory.XEngine.Time;
        }
        protected virtual bool updateStatistics(IORequest targetIORequest)
        {
            checked
            {
                try
                {
                    sumResponseTime += (targetIORequest.ResponseTime / 1000);
                }
                catch (OverflowException ex)
                {
                    Console.WriteLine("Overflow exception occured while calculating statistics in HostInterface.");
                    if (mathOverflowOccuredInStatistics)
                        throw new Exception("I can just handle one overflow event, but I received the second one!");
                    mathOverflowOccuredInStatistics = true;
                    XEngineFactory.XEngine.StopSimulation();
                    return false;
                }
            }
            thisRoundSumResponseTime += targetIORequest.ResponseTime / 1000;
            if (minResponseTime > targetIORequest.ResponseTime)
                minResponseTime = targetIORequest.ResponseTime;
            else if (maxResponseTime < targetIORequest.ResponseTime)
                maxResponseTime = targetIORequest.ResponseTime;
            transferredBytesCount += targetIORequest.SizeInByte;
            handledRequestsCount++;//used for general statistics
            if (gcStarted)
            {
                sumResponseTime_AGC += (targetIORequest.ResponseTime / 1000);
                if (minResponseTime_AGC > targetIORequest.ResponseTime)
                    minResponseTime_AGC = targetIORequest.ResponseTime;
                else if (maxResponseTime_AGC < targetIORequest.ResponseTime)
                    maxResponseTime_AGC = targetIORequest.ResponseTime;
                transferredBytesCount_AGC += targetIORequest.SizeInByte;
                handledRequestsCount_AGC++;//used for general statistics
            }
            if (targetIORequest.Type == IORequestType.Write)
            {
                sumResponseTimeW += (targetIORequest.ResponseTime / 1000);
                thisRoundSumResponseTimeW += (targetIORequest.ResponseTime / 1000);
                transferredBytesCountW += targetIORequest.SizeInByte;
                handledWriteRequestsCount++;
                thisRoundHandledWriteRequestsCount++;
                if (minResponseTimeW > targetIORequest.ResponseTime)
                    minResponseTimeW = targetIORequest.ResponseTime;
                else if (maxResponseTimeW < targetIORequest.ResponseTime)
                    maxResponseTimeW = targetIORequest.ResponseTime;
                if (gcStarted)
                {
                    sumResponseTimeW_AGC += (targetIORequest.ResponseTime / 1000);
                    transferredBytesCountW_AGC += targetIORequest.SizeInByte;
                    handledWriteRequestsCount_AGC++;
                    if (minResponseTimeW_AGC > targetIORequest.ResponseTime)
                        minResponseTimeW_AGC = targetIORequest.ResponseTime;
                    else if (maxResponseTimeW_AGC < targetIORequest.ResponseTime)
                        maxResponseTimeW_AGC = targetIORequest.ResponseTime;
                }
            }
            else
            {
                sumResponseTimeR += (targetIORequest.ResponseTime / 1000);
                thisRoundSumResponseTimeR += (targetIORequest.ResponseTime / 1000);
                transferredBytesCountR += targetIORequest.SizeInByte;
                handledReadRequestsCount++;
                thisRoundHandledReadRequestsCount++;
                if (minResponseTimeR > targetIORequest.ResponseTime)
                    minResponseTimeR = targetIORequest.ResponseTime;
                else if (maxResponseTimeR < targetIORequest.ResponseTime)
                    maxResponseTimeR = targetIORequest.ResponseTime;
                if (gcStarted)
                {
                    sumResponseTimeR_AGC += (targetIORequest.ResponseTime / 1000);
                    transferredBytesCountR_AGC += targetIORequest.SizeInByte;
                    handledReadRequestsCount_AGC++;
                    if (minResponseTimeR_AGC > targetIORequest.ResponseTime)
                        minResponseTimeR_AGC = targetIORequest.ResponseTime;
                    else if (maxResponseTimeR_AGC < targetIORequest.ResponseTime)
                        maxResponseTimeR_AGC = targetIORequest.ResponseTime;
                }
            }
            return true;
        }
        public static void ResetGlobalID()
        {
            _lastID = 0;
        }
        public void Snapshot(System.Xml.XmlTextWriter writer)
        {
            writer.WriteStartElement("Total");
            writer.WriteAttributeString("FlowName", FlowName);
            writer.WriteAttributeString("ReceivedRequestCount", ReceivedRequestCount.ToString());
            writer.WriteAttributeString("ReceivedReadRequestCount", ReceivedReadRequestCount.ToString());
            writer.WriteAttributeString("ReceivedWriteRequestCount", ReceivedWriteRequestCount.ToString());
            writer.WriteAttributeString("HandledRequestsCount", HandledRequestsCount.ToString());
            writer.WriteAttributeString("HandledReadRequestsCount", HandledReadRequestsCount.ToString());
            writer.WriteAttributeString("HandledWriteRequestsCount", HandledWriteRequestsCount.ToString());
            writer.WriteAttributeString("IgnoredRequestsRatio", RatioOfIgnoredRequests.ToString());
            writer.WriteAttributeString("AvgResponseTime_us", AvgResponseTime.ToString());
            writer.WriteAttributeString("MinResponseTime_us", MinResponseTime.ToString());
            writer.WriteAttributeString("MaxResponseTime_us", MaxResponseTime.ToString());
            writer.WriteAttributeString("AvgResponseTimeRead_us", AvgResponseTimeR.ToString());
            writer.WriteAttributeString("MinResponseTimeRead_us", MinResponseTimeR.ToString());
            writer.WriteAttributeString("MaxResponseTimeRead_us", MaxResponseTimeR.ToString());
            writer.WriteAttributeString("AvgResponseTimeWrite_us", AvgResponseTimeW.ToString());
            writer.WriteAttributeString("MinResponseTimeWrite_us", MinResponseTimeW.ToString());
            writer.WriteAttributeString("MaxResponseTimeWrite_us", MaxResponseTimeW.ToString());
            writer.WriteAttributeString("AverageCMDLifeTime_us", AverageCMDLifeTime.ToString());
            writer.WriteAttributeString("AverageCMDExecutionTime_us", AverageCMDExecutionTime.ToString());
            writer.WriteAttributeString("AverageCMDTransferTime_us", AverageCMDTransferTime.ToString());
            writer.WriteAttributeString("AverageCMDWaitingTime_us", AverageCMDWaitingTime.ToString());
            writer.WriteAttributeString("AverageReadCMDLifeTime_us", AverageReadCMDLifeTime.ToString());
            writer.WriteAttributeString("AverageReadCMDExecutionTime_us", AverageReadCMDExecutionTime.ToString());
            writer.WriteAttributeString("AverageReadCMDTransferTime_us", AverageReadCMDTransferTime.ToString());
            writer.WriteAttributeString("AverageReadCMDWaitingTime_us", AverageReadCMDWaitingTime.ToString());
            writer.WriteAttributeString("AverageProgramCMDLifeTime_us", AverageProgramCMDLifeTime.ToString());
            writer.WriteAttributeString("AverageProgramCMDExecutionTime_us", AverageProgramCMDExecutionTime.ToString());
            writer.WriteAttributeString("AverageProgramCMDTransferTime_us", AverageProgramCMDTransferTime.ToString());
            writer.WriteAttributeString("AverageProgramCMDWaitingTime_us", AverageProgramCMDWaitingTime.ToString());
            writer.WriteAttributeString("IOPS", IOPS.ToString());
            writer.WriteAttributeString("IOPSRead", IOPSReads.ToString());
            writer.WriteAttributeString("IOPSWrite", IOPSWrites.ToString());
            writer.WriteAttributeString("AggregateBandWidth_MB", AggregateBandWidth.ToString());
            writer.WriteAttributeString("AggregateBandWidthRead_MB", AggregateBandWidthReads.ToString());
            writer.WriteAttributeString("AggregateBandWidthWrites_MB", AggregateBandWidthWrites.ToString());
            writer.WriteEndElement();//Total


            writer.WriteStartElement("AfterGCStart");
            writer.WriteAttributeString("FlowName", FlowName);
            writer.WriteAttributeString("HandledRequestsCount", HandledRequestsCount_AGC.ToString());
            writer.WriteAttributeString("HandledReadRequestsCount", HandledReadRequestsCount_AGC.ToString());
            writer.WriteAttributeString("HandledWriteRequestsCount", HandledWriteRequestsCount_AGC.ToString());
            writer.WriteAttributeString("AvgResponseTime_us", AvgResponseTime_AGC.ToString());
            writer.WriteAttributeString("MinResponseTime_us", MinResponseTime_AGC.ToString());
            writer.WriteAttributeString("MaxResponseTime_us", MaxResponseTime_AGC.ToString());
            writer.WriteAttributeString("AvgResponseTimeRead_us", AvgResponseTimeR_AGC.ToString());
            writer.WriteAttributeString("MinResponseTimeRead_us", MinResponseTimeR_AGC.ToString());
            writer.WriteAttributeString("MaxResponseTimeRead_us", MaxResponseTimeR_AGC.ToString());
            writer.WriteAttributeString("AvgResponseTimeWrite_us", AvgResponseTimeW_AGC.ToString());
            writer.WriteAttributeString("MinResponseTimeWrite_us", MinResponseTimeW_AGC.ToString());
            writer.WriteAttributeString("MaxResponseTimeWrite_us", MaxResponseTimeW_AGC.ToString());
            writer.WriteAttributeString("AverageCMDLifeTime_us", AverageCMDLifeTime_AGC.ToString());
            writer.WriteAttributeString("AverageCMDExecutionTime_us", AverageCMDExecutionTime_AGC.ToString());
            writer.WriteAttributeString("AverageCMDTransferTime_us", AverageCMDTransferTime_AGC.ToString());
            writer.WriteAttributeString("AverageCMDWaitingTime_us", AverageCMDWaitingTime_AGC.ToString());
            writer.WriteAttributeString("AverageReadCMDLifeTime_us", AverageReadCMDLifeTime_AGC.ToString());
            writer.WriteAttributeString("AverageReadCMDExecutionTime_us", AverageReadCMDExecutionTime_AGC.ToString());
            writer.WriteAttributeString("AverageReadCMDTransferTime_us", AverageReadCMDTransferTime_AGC.ToString());
            writer.WriteAttributeString("AverageReadCMDWaitingTime_us", AverageReadCMDWaitingTime_AGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDLifeTime_us", AverageProgramCMDLifeTime_AGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDExecutionTime_us", AverageProgramCMDExecutionTime_AGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDTransferTime_us", AverageProgramCMDTransferTime_AGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDWaitingTime_us", AverageProgramCMDWaitingTime_AGC.ToString());
            writer.WriteAttributeString("IOPS", IOPS_AGC.ToString());
            writer.WriteAttributeString("IOPSRead", IOPSReads_AGC.ToString());
            writer.WriteAttributeString("IOPSWrite", IOPSWrites_AGC.ToString());
            writer.WriteAttributeString("AggregateBandWidth_MB", AggregateBandWidth_AGC.ToString());
            writer.WriteAttributeString("AggregateBandWidthRead_MB", AggregateBandWidthReads_AGC.ToString());
            writer.WriteAttributeString("AggregateBandWidthWrites_MB", AggregateBandWidthWrites_AGC.ToString());
            writer.WriteEndElement();//AfterGCStart

            writer.WriteStartElement("BeforeGCStart");
            writer.WriteAttributeString("FlowName", FlowName);
            writer.WriteAttributeString("HandledRequestsCount", HandledRequestsCount_BGC.ToString());
            writer.WriteAttributeString("HandledReadRequestsCount", HandledReadRequestsCount_BGC.ToString());
            writer.WriteAttributeString("HandledWriteRequestsCount", HandledWriteRequestsCount_BGC.ToString());
            writer.WriteAttributeString("AvgResponseTime_us", AvgResponseTime_BGC.ToString());
            writer.WriteAttributeString("MinResponseTime_us", MinResponseTime_BGC.ToString());
            writer.WriteAttributeString("MaxResponseTime_us", MaxResponseTime_BGC.ToString());
            writer.WriteAttributeString("AvgResponseTimeRead_us", AvgResponseTimeR_BGC.ToString());
            writer.WriteAttributeString("MinResponseTimeRead_us", MinResponseTimeR_BGC.ToString());
            writer.WriteAttributeString("MaxResponseTimeRead_us", MaxResponseTimeR_BGC.ToString());
            writer.WriteAttributeString("AvgResponseTimeWrite_us", AvgResponseTimeW_BGC.ToString());
            writer.WriteAttributeString("MinResponseTimeWrite_us", MinResponseTimeW_BGC.ToString());
            writer.WriteAttributeString("MaxResponseTimeWrite_us", MaxResponseTimeW_BGC.ToString());
            writer.WriteAttributeString("AverageCMDLifeTime_us", AverageCMDLifeTime_BGC.ToString());
            writer.WriteAttributeString("AverageCMDExecutionTime_us", AverageCMDExecutionTime_BGC.ToString());
            writer.WriteAttributeString("AverageCMDTransferTime_us", AverageCMDTransferTime_BGC.ToString());
            writer.WriteAttributeString("AverageCMDWaitingTime_us", AverageCMDWaitingTime_BGC.ToString());
            writer.WriteAttributeString("AverageReadCMDLifeTime_us", AverageReadCMDLifeTime_BGC.ToString());
            writer.WriteAttributeString("AverageReadCMDExecutionTime_us", AverageReadCMDExecutionTime_BGC.ToString());
            writer.WriteAttributeString("AverageReadCMDTransferTime_us", AverageReadCMDTransferTime_BGC.ToString());
            writer.WriteAttributeString("AverageReadCMDWaitingTime_us", AverageReadCMDWaitingTime_BGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDLifeTime_us", AverageProgramCMDLifeTime_BGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDExecutionTime_us", AverageProgramCMDExecutionTime_BGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDTransferTime_us", AverageProgramCMDTransferTime_BGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDWaitingTime_us", AverageProgramCMDWaitingTime_BGC.ToString());
            writer.WriteAttributeString("IOPS", IOPS_BGC.ToString());
            writer.WriteAttributeString("IOPSRead", IOPSReads_BGC.ToString());
            writer.WriteAttributeString("IOPSWrite", IOPSWrites_BGC.ToString());
            writer.WriteAttributeString("AggregateBandWidth_MB", AggregateBandWidth_BGC.ToString());
            writer.WriteAttributeString("AggregateBandWidthRead_MB", AggregateBandWidthReads_BGC.ToString());
            writer.WriteAttributeString("AggregateBandWidthWrites_MB", AggregateBandWidthWrites_BGC.ToString());
            writer.WriteEndElement();//BeforeGCStart
        }

        #region Properties
        public uint ID
        {
            get { return this._id; }
        }
        public string FlowName
        {
            get
            {
                return
                  this._flowName;
            }
        }
        #endregion

        #region CMDLifeTimeParameters
        public ulong AverageCMDLifeTime_BGC//The unit is microseconds
        {
            get { return this.averageOperationLifeTime_BGC; }
        }
        public ulong AverageCMDLifeTime//The unit is microseconds
        {
            get
            {
                if (this.totalFlashOperations > 0) return this.sumOfInternalRequestLifeTime / this.totalFlashOperations;
                else return 0;
            }
        }
        public ulong AverageCMDLifeTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalFlashOperations_AGC > 0) return this.sumOfInternalRequestLifeTime_AGC / this.totalFlashOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageCMDExecutionTime_BGC//The unit is microseconds
        {
            get { return this.averageOperationExecutionTime_BGC; }
        }
        public ulong AverageCMDExecutionTime//The unit is microseconds
        {
            get
            {
                if (this.totalFlashOperations > 0) return this.sumOfInternalRequestExecutionTime / this.totalFlashOperations;
                else return 0;
            }
        }
        public ulong AverageCMDExecutionTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalFlashOperations_AGC > 0) return this.sumOfInternalRequestExecutionTime_AGC / this.totalFlashOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageCMDTransferTime_BGC//The unit is microseconds
        {
            get { return this.averageOperationTransferTime_BGC; }
        }
        public ulong AverageCMDTransferTime//The unit is microseconds
        {
            get
            {
                if (this.totalFlashOperations > 0) return this.sumOfInternalRequestTransferTime / this.totalFlashOperations;
                else return 0;
            }
        }
        public ulong AverageCMDTransferTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalFlashOperations_AGC > 0) return this.sumOfInternalRequestTransferTime_AGC / this.totalFlashOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageCMDWaitingTime_BGC//The unit is microseconds
        {
            get { return this.averageOperationWaitingTime_BGC; }
        }
        public ulong AverageCMDWaitingTime//The unit is microseconds
        {
            get
            {
                if (totalFlashOperations > 0) return this.sumOfInternalRequestWaitingTime / this.totalFlashOperations;
                else return 0;
            }
        }
        public ulong AverageCMDWaitingTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalFlashOperations_AGC > 0) return this.sumOfInternalRequestWaitingTime_AGC / this.totalFlashOperations_AGC;
                else return 0;
            }
        }

        public ulong AverageReadCMDLifeTime_BGC//The unit is microseconds
        {
            get { return this.averageReadOperationLifeTime_BGC; }
        }
        public ulong AverageReadCMDLifeTime//The unit is microseconds
        {
            get
            {
                if (this.totalReadOperations > 0) return this.sumOfReadRequestLifeTime / this.totalReadOperations;
                else return 0;
            }
        }
        public ulong AverageReadCMDLifeTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalReadOperations_AGC > 0) return this.sumOfReadRequestLifeTime_AGC / this.totalReadOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageReadCMDExecutionTime_BGC//The unit is microseconds
        {
            get { return this.averageReadOperationExecutionTime_BGC; }
        }
        public ulong AverageReadCMDExecutionTime//The unit is microseconds
        {
            get
            {
                if (this.totalReadOperations > 0) return this.sumOfReadRequestExecutionTime / this.totalReadOperations;
                else return 0;
            }
        }
        public ulong AverageReadCMDExecutionTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalReadOperations_AGC > 0) return this.sumOfReadRequestExecutionTime_AGC / this.totalReadOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageReadCMDTransferTime_BGC//The unit is microseconds
        {
            get { return this.averageReadOperationTransferTime_BGC; }
        }
        public ulong AverageReadCMDTransferTime//The unit is microseconds
        {
            get
            {
                if (this.totalReadOperations > 0) return this.sumOfReadRequestTransferTime / this.totalReadOperations;
                else return 0;
            }
        }
        public ulong AverageReadCMDTransferTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalReadOperations_AGC > 0) return this.sumOfReadRequestTransferTime_AGC / this.totalReadOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageReadCMDWaitingTime_BGC//The unit is microseconds
        {
            get { return this.averageReadOperationWaitingTime_BGC; }
        }
        public ulong AverageReadCMDWaitingTime//The unit is microseconds
        {
            get
            {
                if (this.totalReadOperations > 0) return this.sumOfReadRequestWaitingTime / this.totalReadOperations;
                else return 0;
            }
        }

        public ulong AverageReadCMDWaitingTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalReadOperations_AGC > 0) return this.sumOfReadRequestWaitingTime_AGC / this.totalReadOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageProgramCMDLifeTime_BGC//The unit is microseconds
        {
            get { return this.averageProgramOperationLifeTime_BGC; }
        }
        public ulong AverageProgramCMDLifeTime//The unit is microseconds
        {
            get
            {
                if (this.totalProgramOperations > 0) return this.sumOfProgramRequestLifeTime / this.totalProgramOperations;
                else return 0;
            }
        }
        public ulong AverageProgramCMDLifeTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalProgramOperations_AGC > 0) return this.sumOfProgramRequestLifeTime_AGC / this.totalProgramOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageProgramCMDExecutionTime_BGC//The unit is microseconds
        {
            get { return this.averageProgramOperationExecutionTime_BGC; }
        }
        public ulong AverageProgramCMDExecutionTime//The unit is microseconds
        {
            get
            {
                if (this.totalProgramOperations > 0) return this.sumOfProgramRequestExecutionTime / this.totalProgramOperations;
                else return 0;
            }
        }
        public ulong AverageProgramCMDExecutionTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalProgramOperations_AGC > 0) return this.sumOfProgramRequestExecutionTime_AGC / this.totalProgramOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageProgramCMDTransferTime_BGC//The unit is microseconds
        {
            get { return this.averageProgramOperationTransferTime_BGC; }
        }
        public ulong AverageProgramCMDTransferTime//The unit is microseconds
        {
            get
            {
                if (this.totalProgramOperations > 0) return this.sumOfProgramRequestTransferTime / this.totalProgramOperations;
                else return 0;
            }
        }
        public ulong AverageProgramCMDTransferTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalProgramOperations_AGC > 0) return this.sumOfProgramRequestTransferTime_AGC / this.totalProgramOperations_AGC;
                else return 0;
            }
        }
        public ulong AverageProgramCMDWaitingTime_BGC//The unit is microseconds
        {
            get { return this.averageProgramOperationWaitingTime_BGC; }
        }
        public ulong AverageProgramCMDWaitingTime//The unit is microseconds
        {
            get
            {
                if (this.totalProgramOperations > 0) return this.sumOfProgramRequestWaitingTime / this.totalProgramOperations;
                else return 0;
            }
        }
        public ulong AverageProgramCMDWaitingTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.totalProgramOperations_AGC > 0) return this.sumOfProgramRequestWaitingTime_AGC / this.totalProgramOperations_AGC;
                else return 0;
            }
        }
        #endregion

        #region ResponseTimeParameters
        public ulong AvgResponseTime//The unit is microseconds
        {
            get
            {
                if (HandledRequestsCount > 0) return this.sumResponseTime / this.HandledRequestsCount;
                else return 0;
            }
        }
        public ulong MinResponseTime//The unit is microseconds
        {
            get { return this.minResponseTime / 1000; }
        }
        public ulong MaxResponseTime//The unit is microseconds
        {
            get { return this.maxResponseTime / 1000; }
        }
        public ulong AvgResponseTime_BGC//The unit is microseconds
        {
            get { return this.avgResponseTime_BGC; }
        }
        public ulong MinResponseTime_BGC//The unit is microseconds
        {
            get { return this.minResponseTime_BGC / 1000; }
        }
        public ulong MaxResponseTime_BGC//The unit is microseconds
        {
            get { return this.maxResponseTime_BGC / 1000; }
        }
        public ulong AvgResponseTime_AGC//The unit is microseconds
        {
            get
            {
                if (this.handledRequestsCount_AGC > 0) return this.sumResponseTime_AGC / this.handledRequestsCount_AGC;
                else return 0;
            }
        }
        public ulong MinResponseTime_AGC//The unit is microseconds
        {
            get { return this.minResponseTime_AGC / 1000; }//nanoseconds to microseconds
        }
        public ulong MaxResponseTime_AGC//The unit is microseconds
        {
            get { return this.maxResponseTime_AGC / 1000; }
        }

        public ulong AvgResponseTimeR//The unit is microseconds
        {
            get
            {
                if (handledReadRequestsCount > 0) return this.sumResponseTimeR / this.handledReadRequestsCount;
                else return 0;
            }
        }
        public ulong ThisRoundAvgResponseTimeR //Used for the purpose of logging
        {
            get
            {
                if (thisRoundHandledReadRequestsCount > 0) return this.thisRoundSumResponseTimeR / this.thisRoundHandledReadRequestsCount;
                else return 0;
            }
        }
        public ulong MinResponseTimeR//The unit is microseconds
        {
            get
            {
                if (handledReadRequestsCount > 0) return this.minResponseTimeR / 1000;
                else return 0;
            }
        }
        public ulong MaxResponseTimeR//The unit is microseconds
        {
            get
            {
                if (handledReadRequestsCount > 0) return this.maxResponseTimeR / 1000;
                else return 0;
            }
        }
        public ulong AvgResponseTimeR_BGC//The unit is microseconds
        {
            get
            {
                if (handledReadRequestsCount_BGC > 0) return this.avgResponseTimeR_BGC;
                else return 0;
            }
        }
        public ulong MinResponseTimeR_BGC//The unit is microseconds
        {
            get
            {
                if (handledReadRequestsCount_BGC > 0) return this.minResponseTimeR_BGC / 1000;
                else return 0;
            }
        }
        public ulong MaxResponseTimeR_BGC//The unit is microseconds
        {
            get
            {
                if (handledReadRequestsCount_BGC > 0) return this.maxResponseTimeR_BGC / 1000;
                else return 0;
            }
        }
        public ulong AvgResponseTimeR_AGC//The unit is microseconds
        {
            get
            {
                if (handledReadRequestsCount_AGC > 0) return this.sumResponseTimeR_AGC / this.handledReadRequestsCount_AGC;
                else return 0;
            }
        }
        public ulong MinResponseTimeR_AGC//The unit is microseconds
        {
            get
            {
                if (handledReadRequestsCount_AGC > 0) return this.minResponseTimeR_AGC / 1000;
                else return 0;
            }
        }
        public ulong MaxResponseTimeR_AGC//The unit is microseconds
        {
            get
            {
                if (handledReadRequestsCount_AGC > 0) return this.maxResponseTimeR_AGC / 1000;
                else return 0;
            }
        }

        public ulong AvgResponseTimeW//The unit is microseconds
        {
            get
            {
                if (handledWriteRequestsCount > 0) return this.sumResponseTimeW / this.handledWriteRequestsCount;
                else return 0;
            }
        }
        public ulong ThisRoundAvgResponseTimeW //Used for the purpose of logging
        {
            get
            {
                if (thisRoundHandledWriteRequestsCount > 0) return this.thisRoundSumResponseTimeW / this.thisRoundHandledWriteRequestsCount;
                else return 0;
            }
        }
        public ulong MinResponseTimeW//The unit is microseconds
        {
            get
            {
                if (handledWriteRequestsCount > 0) return this.minResponseTimeW / 1000;
                else return 0;
            }
        }
        public ulong MaxResponseTimeW//The unit is microseconds
        {
            get
            {
                if (handledWriteRequestsCount > 0) return this.maxResponseTimeW / 1000;
                else return 0;
            }
        }
        public ulong AvgResponseTimeW_BGC//The unit is microseconds
        {
            get
            {
                if (handledWriteRequestsCount_BGC > 0) return this.avgResponseTimeW_BGC;
                else return 0;
            }
        }
        public ulong MinResponseTimeW_BGC//The unit is microseconds
        {
            get
            {
                if (handledWriteRequestsCount_BGC > 0) return this.minResponseTimeW_BGC / 1000;
                else return 0;
            }
        }
        public ulong MaxResponseTimeW_BGC//The unit is microseconds
        {
            get
            {
                if (handledWriteRequestsCount_BGC > 0) return this.maxResponseTimeW_BGC / 1000;
                else return 0;
            }
        }
        public ulong AvgResponseTimeW_AGC//The unit is microseconds
        {
            get
            {
                if (handledWriteRequestsCount_AGC > 0) return this.sumResponseTimeW_AGC / this.handledWriteRequestsCount_AGC;
                else return 0;
            }
        }
        public ulong MinResponseTimeW_AGC//The unit is microseconds
        {
            get
            {
                if (handledWriteRequestsCount_AGC > 0) return this.minResponseTimeW_AGC / 1000;
                else return 0;
            }
        }
        public ulong MaxResponseTimeW_AGC//The unit is microseconds
        {
            get
            {
                if (handledWriteRequestsCount_AGC > 0) return this.maxResponseTimeW_AGC / 1000;
                else return 0;
            }
        }
        #endregion

        #region RequestCountingParameters
        public double RatioOfIgnoredRequests
        {
            get { return (double)ignoredRequestsCount / ReceivedRequestCount; }
        }
        public ulong IgnoredRequestsCount
        {
            get { return this.ignoredRequestsCount; }
            set { this.ignoredRequestsCount = value; }
        }
        public ulong HandledRequestsCount_BGC
        {
            get { return this.handledRequestsCount_BGC; }
        }
        public ulong HandledRequestsCount
        {
            get { return this.handledRequestsCount; }
        }
        public ulong HandledRequestsCount_AGC
        {
            get { return this.handledRequestsCount_AGC; }
        }

        public ulong HandledReadRequestsCount_BGC
        {
            get { return this.handledReadRequestsCount_BGC; }
        }
        public ulong HandledReadRequestsCount
        {
            get { return this.handledReadRequestsCount; }
        }
        public ulong HandledReadRequestsCount_AGC
        {
            get { return this.handledReadRequestsCount_AGC; }
        }

        public ulong HandledWriteRequestsCount_BGC
        {
            get { return this.handledWriteRequestsCount_BGC; }
        }
        public ulong HandledWriteRequestsCount
        {
            get { return this.handledWriteRequestsCount; }
        }
        public ulong HandledWriteRequestsCount_AGC
        {
            get { return this.handledWriteRequestsCount_AGC; }
        }
        #endregion

        #region ThroughputParameters
        public double IOPS_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return (((double)this.handledRequestsCount_BGC / (double)changeTime) * (double)HostInterface.NanoSecondCoeff);
            }
        }
        public double IOPS
        {
            get { return (((double)this.handledRequestsCount / (double)XEngineFactory.XEngine.Time) * (double)HostInterface.NanoSecondCoeff); }
        }
        public double IOPS_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.handledRequestsCount_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)HostInterface.NanoSecondCoeff);
            }
        }
        public double IOPSReads_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return (((double)this.handledReadRequestsCount_BGC / (double)changeTime) * (double)HostInterface.NanoSecondCoeff);
            }
        }
        public double IOPSReads
        {
            get { return (((double)this.handledReadRequestsCount / (double)XEngineFactory.XEngine.Time) * (double)HostInterface.NanoSecondCoeff); }
        }
        public double IOPSReads_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.handledReadRequestsCount_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)HostInterface.NanoSecondCoeff);
            }
        }
        public double IOPSWrites_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return (((double)this.handledWriteRequestsCount_BGC / (double)changeTime) * (double)HostInterface.NanoSecondCoeff);
            }
        }
        public double IOPSWrites
        {
            get { return (((double)this.handledWriteRequestsCount / (double)XEngineFactory.XEngine.Time) * (double)HostInterface.NanoSecondCoeff); }
        }
        public double IOPSWrites_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.handledWriteRequestsCount_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)HostInterface.NanoSecondCoeff);
            }
        }

        public double AggregateBandWidth_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return ((((double)this.transferredBytesCount_BGC / (double)changeTime) * (double)HostInterface.NanoSecondCoeff) / 1000000);
            }
        }
        public double AggregateBandWidth
        {
            get { return ((((double)this.transferredBytesCount / (double)XEngineFactory.XEngine.Time) * (double)HostInterface.NanoSecondCoeff) / 1000000); }
        }
        public double AggregateBandWidth_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return ((((double)this.transferredBytesCount_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)HostInterface.NanoSecondCoeff) / 1000000);
            }
        }
        public double AggregateBandWidthReads_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return ((((double)this.transferredBytesCountR_BGC / (double)changeTime) * (double)HostInterface.NanoSecondCoeff) / 1000000);
            }
        }
        public double AggregateBandWidthReads
        {
            get { return ((((double)this.transferredBytesCountR / (double)XEngineFactory.XEngine.Time) * (double)HostInterface.NanoSecondCoeff) / 1000000); }
        }
        public double AggregateBandWidthReads_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.transferredBytesCountR_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)HostInterface.NanoSecondCoeff);
            }
        }
        public double AggregateBandWidthWrites_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return (((double)this.transferredBytesCountW_BGC / (double)changeTime) * (double)HostInterface.NanoSecondCoeff);
            }
        }
        public double AggregateBandWidthWrites
        {
            get { return (((double)this.transferredBytesCountW / (double)XEngineFactory.XEngine.Time) * (double)HostInterface.NanoSecondCoeff); }
        }
        public double AggregateBandWidthWrites_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.transferredBytesCountW_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)HostInterface.NanoSecondCoeff);
            }
        }
        #endregion

    }

}
