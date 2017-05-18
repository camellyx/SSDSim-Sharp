using System;
using System.Collections.Generic;
using Smulator.BaseComponents;
using System.IO;
using Smulator.Util;

namespace Smulator.SSD.Components
{
    /// <summary>
    /// <title>HostInterface</title>
    /// <description> This class provides trace-based simulation for multiple IO streams.
    /// </description>
    /// <copyright>Copyright(c)2010</copyright>
    /// <company></company>
    /// <author>Arash Tavakko ( www.arasht.ir )</author>
    /// <version>Version 1.0</version>
    /// <date>24/11/2016</date>
    /// </summary>



    public class HostInterfaceNVMe : HostInterface
    {

        #region RequestGenerationParameters
        InputStreamBase[] _inputStreams = null;
        int numberOfStreams = 0;
        bool overflowOccured = false;
        new ulong[] loggingCntr = { 0 };
        #endregion

        double announcementStep = 0.05;
        double nextAnnouncementMilestone = 0.05;
        ulong SimulationStopTime = 0;
        public bool Finished = false;
        ulong totalRequestsToGenerate = 0;

        #region SetupFunctions
        public HostInterfaceNVMe(
            string id,
            FTL ftl,
            Controller controller,
            int numberOfTraces,
            InputStreamBase[] inputStreams,
            bool RTLoggingEnabled,
            string RTLogFilePath)
            : base(id)
        {
            FTL = ftl;
            Controller = controller;
            this.RTLoggingEnabled = RTLoggingEnabled;

            numberOfStreams = numberOfTraces;
            _inputStreams = inputStreams;
            loggingCntr = new ulong[numberOfTraces];
            for (int i = 0; i < numberOfTraces; i++)
                loggingCntr[i] = 0;

            if (RTLoggingEnabled)
            {
                this.RTLogFilePath = RTLogFilePath;
                this.RTLogFile = new StreamWriter(RTLogFilePath);
                this.RTLogFile.WriteLine("Time(us)\tAverageResponseTime(us)\tThisRoundAverageResponseTime(us)\tMultiplaneReadCommandRatio\tInterleavedReadCommandRatio\tMultiplaneWriteCommandRatio\tInterleavedWriteCommandRatio\tAverageRatePerMillisecond(gc/s)\tAverageRatePerMillisecondThisRound(gc/s)\tAverageReadCMDWaitingTime(us)\tAverageProgramCMDWaitingTime(us)");
                this.RTLogFileR = new StreamWriter(RTLogFilePath.Remove(RTLogFilePath.LastIndexOf(".log")) + "-R.log");
                this.RTLogFileR.WriteLine("Time(us)\tAverageResponseTime(us)\tThisRoundAverageResponseTime(us)");
                this.RTLogFileW = new StreamWriter(RTLogFilePath.Remove(RTLogFilePath.LastIndexOf(".log")) + "-W.log");
                this.RTLogFileW.WriteLine("Time(us)\tAverageResponseTime(us)\tThisRoundAverageResponseTime(us)");
            }
        }

        public override void Validate()
        {
            base.Validate();
            if (this.Controller == null)
                throw new ValidationException("HostInterface has no controller");
            if (FTL == null)
                throw new ValidationException(string.Format("HostInterface has no FTL", ID));
        }
        public override void SetupDelegates(bool propagateToChilds)
        {
            base.SetupDelegates(propagateToChilds);
            if (propagateToChilds)
            {
            }
        }
        public override void ResetDelegates(bool propagateToChilds)
        {
            if (propagateToChilds)
            {
            }
            base.ResetDelegates(propagateToChilds);
        }
        public override void Start()
        {
        }
        #endregion

        #region PreprocessTrace
        public override void Preprocess()
        {
            Console.WriteLine("\n");
            Console.WriteLine(".................................................");
            Console.WriteLine("Preprocess of input streams started\n");

            foreach (InputStreamBase stream in _inputStreams)
                if (SimulationStopTime < stream.SimulationStopTime)
                    SimulationStopTime = stream.SimulationStopTime;

            foreach (InputStreamBase stream in _inputStreams)
            {
                stream.Preprocess(this, SimulationStopTime, foldAddress, ignoreUnallocatedReads);
                totalRequestsToGenerate += stream.NumberOfRequestsToGenerate;
            }

            Console.WriteLine("Preprocess finished!");
            Console.WriteLine(".................................................\n");
        }
        #endregion

        #region RequestGenerationFunctions
        public override void ProcessXEvent(XEvent e)
        {
            switch ((HostInterfaceEventType)e.Type)
            {
                case HostInterfaceEventType.GenerateNextIORequest:
                    if (XEngineFactory.XEngine.Time >= SimulationStopTime)
                        Finished = true;
                    uint streamID = (uint)e.Parameters;
                    IORequest request = _inputStreams[streamID].GetNextIORequest(this, foldAddress, ignoreUnallocatedReads);
                    ReceivedRequestCount++;
                    if (request.Type == IORequestType.Write)//write request in ascii traces
                        ReceivedWriteRequestCount++;
                    else//read request in ascii traces
                        ReceivedReadRequestCount++;
                    if (request.ToBeIgnored)
                        ignoredRequestsCount++;
                    else
                        OnIORequestArrived(streamID);
                    break;
                case HostInterfaceEventType.RequestCompletedByDRAM:
                    IORequest targetRequest = e.Parameters as IORequest;
                    SendEarlyResponseToHost(targetRequest);
                    break;
                default:
                    throw new Exception("Unhandled XEvent type");
            }
        }
        #endregion

        #region StatisticsFunctions
        /* When an IO request is handled without creating any InternalRequest
         * this function is invoked (i.e., IO request is handled by cache or by requests currently being serviced.
         * For example if we have a read request for an lpn which is simultaneously being written with another
         * request, therefore we can simply return the value of write without performing any flash read operation.)*/
        public override void SendEarlyResponseToHost(IORequest targetIORequest)
        {
            _inputStreams[targetIORequest.StreamID].WriteToCompletionQueue(targetIORequest, requestProcessingTime);
            checked
            {
                try
                {
                    sumResponseTime += (targetIORequest.ResponseTime / 1000);
                }
                catch (OverflowException)
                {
                    Console.WriteLine("Overflow exception occured while calculating statistics in HostInterface.");
                    if (overflowOccured)
                        throw new Exception("I can just handle one overflow event, but I received the second one!");
                    overflowOccured = true;
                    XEngineFactory.XEngine.StopSimulation();
                    return;
                }
            }
            thisRoundSumResponseTime += targetIORequest.ResponseTime / 1000;
            transferredBytesCount += targetIORequest.SizeInByte;
            handledRequestsCount++;//used for general statistics
            thisRoundHandledRequestsCount++;//used in replay
            if (gcStarted)
            {
                sumResponseTime_AGC += (targetIORequest.ResponseTime / 1000);
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
                if (gcStarted)
                {
                    sumResponseTimeW_AGC += (targetIORequest.ResponseTime / 1000);
                    transferredBytesCountW_AGC += targetIORequest.SizeInByte;
                    handledWriteRequestsCount_AGC++;
                }
            }
            else
            {
                sumResponseTimeR += (targetIORequest.ResponseTime / 1000);
                thisRoundSumResponseTimeR += (targetIORequest.ResponseTime / 1000);
                transferredBytesCountR += targetIORequest.SizeInByte;
                handledReadRequestsCount++;
                thisRoundHandledReadRequestsCount++;
                if (gcStarted)
                {
                    sumResponseTimeR_AGC += (targetIORequest.ResponseTime / 1000);
                    transferredBytesCountR_AGC += targetIORequest.SizeInByte;
                    handledReadRequestsCount_AGC++;
                }
            }

            if ((handledRequestsCount / (double) totalRequestsToGenerate) > nextAnnouncementMilestone)
            {
                nextAnnouncementMilestone += announcementStep;
                OnStatReady();
            }

            OnIORequestCompleted(targetIORequest.StreamID);
        }
        public override void SendResponseToHost(InternalRequest internalReq)
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
                FTL.OnLPNServiced(internalReq.RelatedIORequest.StreamID, internalReq.LPN);
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
            if (_inputStreams[targetIORequest.StreamID].WriteToCompletionQueue(internalReq, requestProcessingTime))
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
                        if (overflowOccured)
                            throw new Exception("I can just handle one overflow event, but I received the second one!");
                        overflowOccured = true;
                        XEngineFactory.XEngine.StopSimulation();
                        return;
                    }
                }
                thisRoundSumResponseTime += targetIORequest.ResponseTime / 1000;
                if (minResponseTime > targetIORequest.ResponseTime)
                    minResponseTime = targetIORequest.ResponseTime;
                else if (maxResponseTime < targetIORequest.ResponseTime)
                    maxResponseTime = targetIORequest.ResponseTime;
                transferredBytesCount += targetIORequest.SizeInByte;
                handledRequestsCount++;//used for general statistics
                thisRoundHandledRequestsCount++;//used in replay
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

                if ((handledRequestsCount / (double)totalRequestsToGenerate) > nextAnnouncementMilestone)
                {
                    nextAnnouncementMilestone += announcementStep;
                    OnStatReady();
                }

                OnIORequestCompleted(targetIORequest.StreamID);
            }//_inputStreams[targetIORequest.StreamID].WriteToCompletionQueue...
        }
        public override void FirstGCEvent()
        {
            gcStarted = true;
            foreach (InputStreamBase IS in _inputStreams)
                IS.FirstGCEvent();
        }
        public override void Snapshot(string id, System.Xml.XmlTextWriter writer)
        {
            writer.WriteStartElement(id + "_Statistics");
            writer.WriteAttributeString("ID", ID.ToString());
            writer.WriteAttributeString("ReceivedRequestCount", ReceivedRequestCount.ToString());
            writer.WriteAttributeString("ReceivedReadRequestCount", ReceivedReadRequestCount.ToString());
            writer.WriteAttributeString("ReceivedWriteRequestCount", ReceivedWriteRequestCount.ToString());
            writer.WriteAttributeString("HandledRequestsCount", HandledRequestsCount.ToString());
            writer.WriteAttributeString("HandledReadRequestsCount", HandledReadRequestsCount.ToString());
            writer.WriteAttributeString("HandledWriteRequestsCount", HandledWriteRequestsCount.ToString());
            writer.WriteAttributeString("IgnoredRequestsRatio", RatioOfIgnoredRequests.ToString());

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


            writer.WriteStartElement(id + "_Statistics_AfterGCStart");
            writer.WriteAttributeString("ID", ID.ToString());
            writer.WriteAttributeString("HandledRequestsCount", HandledRequestsCount_AGC.ToString());
            writer.WriteAttributeString("HandledReadRequestsCount", HandledReadRequestsCount_AGC.ToString());
            writer.WriteAttributeString("HandledWriteRequestsCount", HandledWriteRequestsCount_AGC.ToString());

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
            writer.WriteEndElement();

            for (int i = 0; i < numberOfStreams; i++)
            {
                writer.WriteStartElement("Stream_Statistics");
                _inputStreams[i].Snapshot(writer);
                _inputStreams[i].Close();
                writer.WriteEndElement();
            }

        }
        #endregion
        public InputStreamBase[] InputStreams
        {
            get { return _inputStreams; }
        }
        public double SimulationProgress
        {
            get { return (double) handledRequestsCount / totalRequestsToGenerate; }
        }

        #region delegates
        public delegate void RequestCompletedHandler(uint streamID);
        public event RequestCompletedHandler onIORequestCompleted;
        protected void OnIORequestCompleted(uint streamID)
        {
            if (onIORequestCompleted != null)
                onIORequestCompleted(streamID);
        }

        public delegate void IORequestArrivedHandler(uint streamID);
        public event IORequestArrivedHandler onIORequestArrived;
        protected void OnIORequestArrived(uint streamID)
        {
            if (onIORequestArrived != null)
                onIORequestArrived(streamID);
        }
        #endregion
    }
}
