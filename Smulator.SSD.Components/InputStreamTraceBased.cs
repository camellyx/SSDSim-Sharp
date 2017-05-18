using System;
using System.Collections.Generic;
using System.IO;
using Smulator.BaseComponents;
using Smulator.Util;

namespace Smulator.SSD.Components
{
    public class InputStreamTraceBased : InputStreamBase
    {
        public uint percentageToBeSimulated;
        public string _traceFilePath;
        public uint TotalReplayCount = 0, CurrentReplayRound = 1;
        public ulong[] ReplayAddressOffset;
        public RandomGenerator randomAddressOffsetGenerator;
        //public ulong LoggingStep = 0, LoggingCntr = 0,
        //public double NextAnnouncementMilestone = 0.05;
        private ulong thisRoundHandledRequestsCount;//Used for handling replay, in each round of execution HostInterface should generate a total number of: reqNoUnit 
        private StreamReader _inputFile;
        private uint totalRequestsInFile = 0;
        private string[] NextInputLine;
        private ulong lastRequestArrivalTime = 0;


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
        public InputStreamTraceBased(string flowName, string traceFilePath, StreamPriorityClass priorityClass, uint percentageToBeSimulated, uint replayCount,
            uint totalLinesInLogfile, AddressMappingDomain addressMappingDomain, int seed) : base(flowName, priorityClass, addressMappingDomain)
        {
            _traceFilePath = traceFilePath;
            _inputFile = new StreamReader(_traceFilePath);
            TotalReplayCount = replayCount;
            randomAddressOffsetGenerator = new RandomGenerator(seed);

            this.percentageToBeSimulated = percentageToBeSimulated;
            if (this.percentageToBeSimulated > 100)
            {
                this.percentageToBeSimulated = 100;
                Console.WriteLine(@"Bad value for percentage of simulation! It is automatically set to 100%");
            }

            #region CalculateReqNo
            //calculate how many requets should be handled during simulation
            StreamReader traceFile2 = new StreamReader(_traceFilePath);
            string[] currentInputLine;
            while (traceFile2.Peek() >= 0)
            {
                currentInputLine = traceFile2.ReadLine().Split(HostInterface.Separator);
                if (currentInputLine.Length < 5)
                    break;
                totalRequestsInFile++;
                lastRequestArrivalTime = ulong.Parse(currentInputLine[HostInterface.ASCIITraceTimeColumn]);
            }
            SimulationStopTime = lastRequestArrivalTime * replayCount;
            traceFile2.Close();
            #endregion

            NextInputLine = null;
            if (_inputFile.Peek() >= 0)
                NextInputLine = _inputFile.ReadLine().Split(HostInterface.Separator);
        }
        public override void Preprocess(HostInterface hostInterface, ulong simulationStopTime, bool foldAddress, bool ignoreUnallocatedReads)
        {
            if (SimulationStopTime == simulationStopTime)
            {
                TotalReplayCount = 1;
                ReplayAddressOffset = new ulong[1];
                ReplayAddressOffset[0] = 0;
                AddressOffset = 0;
            }
            else
            {
                SimulationStopTime = simulationStopTime;
                TotalReplayCount = Convert.ToUInt32(Math.Ceiling((double)SimulationStopTime / lastRequestArrivalTime));
                ReplayAddressOffset = new ulong[TotalReplayCount];
                ReplayAddressOffset[0] = 0;
                for (int i = 1; i < TotalReplayCount; i++)
                    ReplayAddressOffset[i] = randomAddressOffsetGenerator.UniformULong(0, hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].LargestLSN);
                AddressOffset = 0;
            }


            Console.WriteLine("Stream from trace " + FlowName + "\n");
            uint[] State = new uint[hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].TotalPagesNo];
            for (uint i = 0; i < hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].TotalPagesNo; i++)
                State[i] = 0;
            ulong timeSentinel = 0, offest = 0;
            for (int replayCntr = 0; replayCntr < TotalReplayCount; replayCntr++)
            {
                string[] currentInputLine;
                ulong time = 0;
                uint device, reqSize = 0, sub_size, ope, add_size;
                ulong lsn;

                StreamReader traceFile2 = new StreamReader(_traceFilePath);

                while (traceFile2.Peek() >= 0 && timeSentinel < SimulationStopTime)
                {
                    currentInputLine = traceFile2.ReadLine().Split(HostInterface.Separator);
                    time = ulong.Parse(currentInputLine[HostInterface.ASCIITraceTimeColumn]);
                    device = uint.Parse(currentInputLine[HostInterface.ASCIITraceDeviceColumn]);
                    lsn = ReplayAddressOffset[replayCntr] + uint.Parse(currentInputLine[HostInterface.ASCIITraceAddressColumn]);
                    reqSize = uint.Parse(currentInputLine[HostInterface.ASCIITraceSizeColumn]);
                    ope = uint.Parse(currentInputLine[HostInterface.ASCIITraceTypeColumn]);
                    hostInterface.TraceAssert(time, device, lsn, reqSize, ope);
                    timeSentinel = offest + time;
                    NumberOfRequestsToGenerate++;
                    add_size = 0;
                    //If address folding is disabled and lsn is greater than largetLSN, then ignore this request
                    if (!foldAddress)
                        if (lsn + reqSize > hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].LargestLSN)
                            continue;

                    if (ope == HostInterface.ASCIITraceReadCodeInteger)//read request
                    {
                        while (add_size < reqSize)
                        {
                            lsn = lsn % hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].LargestLSN;
                            sub_size = hostInterface.FTL.SubpageNoPerPage - (uint)(lsn % hostInterface.FTL.SubpageNoPerPage);
                            if (add_size + sub_size >= reqSize)
                            {
                                sub_size = reqSize - add_size;
                                add_size += sub_size;
                            }

                            if ((sub_size > hostInterface.FTL.SubpageNoPerPage) || (add_size > reqSize))
                            {
                                Console.WriteLine("preprocess sub_size:{0}\n", sub_size);
                            }
                            ulong lpn = lsn / hostInterface.FTL.SubpageNoPerPage;

                            if (!ignoreUnallocatedReads)
                            {
                                if (State[lpn] == 0)
                                {
                                    State[lpn] = hostInterface.FTL.SetEntryState(lsn, sub_size);
                                    hostInterface.FTL.HandleMissingReadAccessTarget(_id, lsn, State[lpn]);
                                }
                                else //if (State[lpn] > 0)
                                {
                                    uint map_entry_new = hostInterface.FTL.SetEntryState(lsn, sub_size);
                                    uint map_entry_old = State[lpn];
                                    uint modify_temp = map_entry_new | map_entry_old;
                                    State[lpn] = modify_temp;

                                    if (((map_entry_new ^ map_entry_old) & map_entry_new) != 0)
                                    {
                                        uint map_entry_old_real_map = hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].MappingTable.State[lpn];
                                        uint modify_real_map = ((map_entry_new ^ map_entry_old) & map_entry_new) | map_entry_old_real_map;
                                        if (hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].MappingTable.State[lpn] == 0)
                                            hostInterface.FTL.HandleMissingReadAccessTarget(_id, lsn, modify_real_map);
                                        else
                                            hostInterface.FTL.ModifyPageTableStateforMissingRead(_id, lpn, modify_real_map);
                                    }
                                }//else if(Controller.DRAM.Map.Entry[lpn].state>0)
                            }
                            lsn = lsn + sub_size;
                            add_size += sub_size;
                        }//while(add_size<size)
                    }
                    else
                    {
                        while (add_size < reqSize)
                        {
                            lsn = lsn % hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].LargestLSN;
                            sub_size = hostInterface.FTL.SubpageNoPerPage - (uint)(lsn % hostInterface.FTL.SubpageNoPerPage);
                            if (add_size + sub_size >= reqSize)
                            {
                                sub_size = reqSize - add_size;
                                add_size += sub_size;
                            }

                            if ((sub_size > hostInterface.FTL.SubpageNoPerPage) || (add_size > reqSize))
                            {
                                Console.WriteLine("preprocess sub_size:{0}\n", sub_size);
                            }

                            ulong lpn = lsn / hostInterface.FTL.SubpageNoPerPage;
                            if (State[lpn] == 0)
                                State[lpn] = hostInterface.FTL.SetEntryState(lsn, sub_size);   //0001
                            else
                            {
                                uint map_entry_new = hostInterface.FTL.SetEntryState(lsn, sub_size);
                                uint map_entry_old = hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].MappingTable.State[lpn];
                                uint modify = map_entry_new | map_entry_old;
                                State[lpn] = modify;
                            }
                            lsn = lsn + sub_size;
                            add_size += sub_size;
                        }
                    }
                }
                offest = timeSentinel;
                traceFile2.Close();
            }

            ulong eventTime = TimeOffset + ulong.Parse(NextInputLine[HostInterface.ASCIITraceTimeColumn]);
            XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(eventTime, hostInterface, _id, 0));
        }
        public override IORequest GetNextIORequest(HostInterface hostInterface, bool foldAddress, bool ignoreUnallocatedReads)
        {
            IORequest request = null;
            if (NextInputLine != null)
            {
                #region GenerateRequest
                ulong lsn = AddressOffset + ulong.Parse(NextInputLine[HostInterface.ASCIITraceAddressColumn]);
                uint size = uint.Parse(NextInputLine[HostInterface.ASCIITraceSizeColumn]);
                if (hostInterface.FTL.AddressMapper.CheckRequestAddress(_id, lsn, size, foldAddress))
                {
                    if (foldAddress)
                        lsn = lsn % hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].LargestLSN;
                    if (NextInputLine[HostInterface.ASCIITraceTypeColumn] == HostInterface.ASCIITraceWriteCode)//write request in ascii traces
                    {
                        request = new IORequest(_id, TimeOffset + ulong.Parse(NextInputLine[HostInterface.ASCIITraceTimeColumn]), lsn, size, IORequestType.Write);
                        ReceivedWriteRequestCount++;
                        ReceivedRequestCount++;
                        request.RelatedNodeInList = SubmissionQueue.AddLast(request);
                        if (HeadRequest == null)
                            HeadRequest = request.RelatedNodeInList;
                    }
                    else//read request in ascii traces
                    {
                        bool goodRead = true;
                        if (ignoreUnallocatedReads)
                            if (!hostInterface.FTL.AddressMapper.CheckReadRequest(_id, lsn, size))
                            {
                                goodRead = false;
                                IgnoredRequestsCount++;
                            }

                        request = new IORequest(_id, TimeOffset + ulong.Parse(NextInputLine[HostInterface.ASCIITraceTimeColumn]), lsn, size, IORequestType.Read);
                        ReceivedReadRequestCount++;
                        ReceivedRequestCount++;
                        if (goodRead)
                        {
                            request.RelatedNodeInList = SubmissionQueue.AddLast(request);
                            if (HeadRequest == null)
                                HeadRequest = request.RelatedNodeInList;
                        }
                        else request.ToBeIgnored = true;
                    }
                }
                #endregion

                #region PrepareNextRequest
                if (_inputFile.Peek() >= 0)
                {
                    if (ReceivedRequestCount < NumberOfRequestsToGenerate)
                    {
                        NextInputLine = _inputFile.ReadLine().Split(HostInterface.Separator);
                        ulong eventTime = TimeOffset + ulong.Parse(NextInputLine[HostInterface.ASCIITraceTimeColumn]);
                        XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(eventTime, hostInterface, _id, 0));
                    }
                }
                else
                {
                    NextInputLine = null;
                    if (XEngineFactory.XEngine.Time < SimulationStopTime)
                    {
                        _inputFile.Close();
                        _inputFile = new StreamReader(_traceFilePath);
                        if (_inputFile.Peek() >= 0)
                        {
                            NextInputLine = _inputFile.ReadLine().Split(HostInterface.Separator);
                            thisRoundHandledRequestsCount = 0; thisRoundSumResponseTime = 0;
                            thisRoundHandledReadRequestsCount = 0; thisRoundSumResponseTimeR = 0;
                            thisRoundHandledWriteRequestsCount = 0; thisRoundSumResponseTimeW = 0;
                        }
                        CurrentReplayRound++;
                        TimeOffset = XEngineFactory.XEngine.Time;
                        ulong eventTime = TimeOffset + ulong.Parse(NextInputLine[HostInterface.ASCIITraceTimeColumn]);
                        XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(eventTime, hostInterface, _id, 0));
                        AddressOffset = ReplayAddressOffset[CurrentReplayRound - 1];
                        Console.WriteLine("\n\n******************************************");
                        Console.WriteLine("* Round {0} of {1} execution started  *", CurrentReplayRound, FlowName);
                        Console.WriteLine("******************************************\n");
                    }
                }
                #endregion
            }
            return request;
        }
        public override void Close()
        {
            _inputFile.Close();
        }
        protected override bool updateStatistics(IORequest targetIORequest)
        {
            thisRoundHandledRequestsCount++;//used in replay
            return base.updateStatistics(targetIORequest);
        }

        #region Properties
        public string TracePath
        {
            get
            {
                return
                  this._traceFilePath;
            }
        }
        #endregion
    }

}
