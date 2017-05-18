using System;
using System.Collections.Generic;
using Smulator.BaseComponents;
using System.IO;

namespace Smulator.SSD.Components
{
    /// <change>
    /// <author>Arash Tavakkol</author>
    /// <description> New parameters and functions added to support synthetic request generation.</description>
    /// <date>2013/08/12</date>
    /// </change>
    /// <change>
    /// <author>Arash Tavakkol</author>
    /// <description> Initial version.</description>
    /// <date>2011/19/12</date>
    /// </change>
    /// <change>
    /// <author>Arash Tavakkol</author>
    /// <description>New functionality added for request folding and ignoring unallocated reads.</description>
    /// <date>2014/03/11</date>
    /// </change>

    /// <summary>
    /// <title>HostInterface</title>
    /// <description> 
    /// </description>
    /// <copyright>Copyright(c)2010</copyright>
    /// <company></company>
    /// <author>Arash Tavakkol ( www.arasht.ir )</author>
    /// <version>Version 2.0</version>
    /// <date>2013/08/12</date>
    /// </summary>

    public class HostInterface : XObject
    {
        public FTL FTL;
        public Controller Controller;
        public enum HostInterfaceType { SATATraceBased, SATASynthetic, NVMe};
        public enum HostInterfaceEventType { GenerateNextIORequest, RequestCompletedByDRAM};

        public enum ReqeustGenerationMode { Normal, Saturated };

        #region RequestGenerationParameters
        public uint NCQSize = 20;
        public LinkedList<IORequest> NCQ = new LinkedList<IORequest>();//Native Command Queue of SATA standard
        public LinkedList<IORequest> HostQueue = new LinkedList<IORequest>();//This queue is simulates OS queue and used to store requests when NCQ is full

        protected ulong ReceivedRequestCount = 0, ReceivedReadRequestCount = 0, ReceivedWriteRequestCount = 0;        
        protected ReqeustGenerationMode Mode = ReqeustGenerationMode.Normal;
        protected bool reqExists = true;        
        protected bool RTLoggingEnabled = false;
        protected string RTLogFilePath;
        protected StreamWriter RTLogFile, RTLogFileR, RTLogFileW;
        protected ulong totalLinesInLogfile = 100000, loggingStep = 10, loggingCntr = 0;
        protected bool gcStarted = false;
        private bool _inSetupPhase = true;
        protected ulong requestProcessingTime = 1000;//(in nanoseconds) a very low processing overhead for each request

        bool replay = true;
        uint targetReplayCount, currentReplayRound = 1;
        ulong replayOffset = 0;
        bool overflowOccured = false;
        #endregion

        #region TraceBasedGenerationParameters
        public static char[] Separator = { ' ' };

        protected ulong numberOfRequestsToBeSeen = 0;//The target line in the trace file, that the simulation process must continue to reach it. Its value is calculated based on percentageToBeSimulated
        protected ulong reqNoUnit = 0, thisRoundHandledRequestsCount = 0, thisRoundIgnoredRequestsCount = 0;//Used for handling replay, in each round of execution HostInterface should generate a total number of: reqNoUnit 
        protected XEvent myNextArrivalEvent = null;
        protected bool foldAddress = true; //If there is an access to addresses larger than disk logical storage space, then fold it (i.e. LBA = LBA % GreatestLBA)
        protected bool ignoreUnallocatedReads = false;//If there is a read to an unwritten address, then just ignore it

        private uint percentageToBeSimulated = 100;
        private string traceFilePath;
        private StreamReader traceFile;
        private string[] currentInputLine = null;
        #endregion

        #region StatisticsParameters
        protected ulong ignoredRequestsCount = 0;
        protected ulong handledRequestsCount = 0, handledRequestsCount_BGC = 0, handledRequestsCount_AGC = 0;
        protected ulong handledReadRequestsCount = 0, handledReadRequestsCount_BGC = 0, handledReadRequestsCount_AGC = 0;
        protected ulong handledWriteRequestsCount = 0, handledWriteRequestsCount_BGC = 0, handledWriteRequestsCount_AGC = 0;
        protected ulong avgResponseTime_BGC, avgResponseTimeR_BGC, avgResponseTimeW_BGC = 0; //To prevent from overflow, we assume the unit is nanoseconds
        protected ulong sumResponseTime =  0, sumResponseTimeR = 0, sumResponseTimeW = 0;//To prevent from overflow, we assume the unit is microseconds
        protected ulong sumResponseTime_AGC = 0, sumResponseTimeR_AGC = 0, sumResponseTimeW_AGC = 0;//To prevent from overflow, we assume the unit is microseconds
        protected ulong minResponseTime_BGC, minResponseTimeR_BGC, minResponseTimeW_BGC, maxResponseTime_BGC, maxResponseTimeR_BGC, maxResponseTimeW_BGC;
        protected ulong minResponseTime = ulong.MaxValue, minResponseTimeR = ulong.MaxValue, minResponseTimeW = ulong.MaxValue;
        protected ulong maxResponseTime = 0, maxResponseTimeR = 0, maxResponseTimeW = 0;
        protected ulong minResponseTime_AGC = ulong.MaxValue, minResponseTimeR_AGC = ulong.MaxValue, minResponseTimeW_AGC = ulong.MaxValue;
        protected ulong maxResponseTime_AGC = 0, maxResponseTimeR_AGC = 0, maxResponseTimeW_AGC = 0;
        protected ulong transferredBytesCount_BGC = 0, transferredBytesCount = 0, transferredBytesCount_AGC = 0;
        protected ulong transferredBytesCountR_BGC = 0, transferredBytesCountR = 0, transferredBytesCountR_AGC = 0;
        protected ulong transferredBytesCountW_BGC = 0, transferredBytesCountW = 0, transferredBytesCountW_AGC = 0;

        ulong averageOperationLifeTime_BGC = 0, averageOperationExecutionTime_BGC, averageOperationTransferTime_BGC = 0, averageOperationWaitingTime_BGC = 0;
        protected ulong sumOfInternalRequestExecutionTime = 0, sumOfInternalRequestLifeTime = 0, sumOfInternalRequestTransferTime = 0, sumOfInternalRequestWaitingTime = 0;
        protected ulong sumOfInternalRequestExecutionTime_AGC = 0, sumOfInternalRequestLifeTime_AGC = 0, sumOfInternalRequestTransferTime_AGC = 0, sumOfInternalRequestWaitingTime_AGC = 0;
        ulong averageReadOperationLifeTime_BGC = 0, averageReadOperationExecutionTime_BGC, averageReadOperationTransferTime_BGC = 0, averageReadOperationWaitingTime_BGC = 0;
        protected ulong sumOfReadRequestExecutionTime = 0, sumOfReadRequestLifeTime = 0, sumOfReadRequestTransferTime = 0, sumOfReadRequestWaitingTime = 0;
        protected ulong sumOfReadRequestExecutionTime_AGC = 0, sumOfReadRequestLifeTime_AGC = 0, sumOfReadRequestTransferTime_AGC = 0, sumOfReadRequestWaitingTime_AGC = 0;
        ulong averageProgramOperationLifeTime_BGC = 0, averageProgramOperationExecutionTime_BGC, averageProgramOperationTransferTime_BGC = 0, averageProgramOperationWaitingTime_BGC = 0;
        protected ulong sumOfProgramRequestExecutionTime = 0, sumOfProgramRequestLifeTime = 0, sumOfProgramRequestTransferTime = 0, sumOfProgramRequestWaitingTime = 0;
        protected ulong sumOfProgramRequestExecutionTime_AGC = 0, sumOfProgramRequestLifeTime_AGC = 0, sumOfProgramRequestTransferTime_AGC = 0, sumOfProgramRequestWaitingTime_AGC = 0;
        protected ulong totalFlashOperations = 0, totalReadOperations = 0, totalProgramOperations = 0;
        protected ulong totalFlashOperations_AGC = 0, totalReadOperations_AGC = 0, totalProgramOperations_AGC = 0;

        //Logging variables
        protected ulong thisRoundSumResponseTime = 0, thisRoundSumResponseTimeR = 0, thisRoundSumResponseTimeW = 0;
        protected ulong thisRoundHandledReadRequestsCount = 0, thisRoundHandledWriteRequestsCount = 0;

        ulong changeTime = 0;
        //bool warmedUp = false;
        public static ulong NanoSecondCoeff = 1000000000;//the coefficient to convert nanoseconds to second
        public const int ASCIITraceTimeColumn = 0;
        public const int ASCIITraceDeviceColumn = 1;
        public const int ASCIITraceAddressColumn = 2;
        public const int ASCIITraceSizeColumn = 3;
        public const int ASCIITraceTypeColumn = 4;
        public const string ASCIITraceWriteCode = "0";
        public const string ASCIITraceReadCode = "1";
        public const int ASCIITraceWriteCodeInteger = 0;
        public const int ASCIITraceReadCodeInteger = 1;
        double nextAnnouncementMilestone = 0.05;
        double announcementStep = 0.05;
        #endregion

        #region SetupFunctions
        public HostInterface(
            string id,
            uint NCQSize,
            FTL ftl,
            Controller controller,
            ReqeustGenerationMode mode,
            string traceFilePath,
            uint percentageToBeSimulated,
            bool RTLoggingEnabled,
            string RTLogFilePath,
            uint targetReplayCount,
            bool foldAddress,
            bool ignoreUnallocatedReads)
            : base(id)
        {
            this._inSetupPhase = true;
            this.NCQSize = NCQSize;
            this.FTL = ftl;
            this.Controller = controller;
            this.Mode = mode;
            this.RTLoggingEnabled = RTLoggingEnabled;
            reqExists = true;
            this.targetReplayCount = targetReplayCount;
            if (targetReplayCount > 1)
                ContinueReplay();
            else StopReplay();

            this.percentageToBeSimulated = percentageToBeSimulated;
            if (this.percentageToBeSimulated > 100)
            {
                this.percentageToBeSimulated = 100;
                Console.WriteLine(@"Bad value for percentage of simulation! It is automatically set to 100%");
            }
            this.traceFile = new StreamReader(traceFilePath);
            this.traceFilePath = traceFilePath;
            this.ignoreUnallocatedReads = ignoreUnallocatedReads;
            this.foldAddress = foldAddress;
            Prepare();

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

        public HostInterface(string id)
            : base(id)
        { }

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
            switch (Mode)
            {
                case ReqeustGenerationMode.Normal:
                    if (currentInputLine != null)
                        XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(ulong.Parse(currentInputLine[0]), this, null, 0));
                    break;
                case ReqeustGenerationMode.Saturated:
                    while (NCQ.Count < NCQSize)
                        SegmentIORequestNoCache_Sprinkler(SaturatedMode_GetNextRequest());
                    break;
                default:
                    throw new Exception("Unhandled message generation mode!");
            }
        }

        private void Prepare()
        {
            currentInputLine = null;
            if (traceFile.Peek() >= 0)
                currentInputLine = traceFile.ReadLine().Split(Separator);
        }

        public void SetupPhaseFinished()
        {
            this._inSetupPhase = false;
        }
        #endregion

        #region PreprocessTrace

        public void TraceAssert(ulong time_t, uint device, ulong lsn, uint size, uint ope)
        {/*
            if (time_t < 0 || device < 0 || lsn < 0 || size < 0 || ope < 0)
            {
                Console.WriteLine("trace error:{0} {1} {2} {3} {4}", time_t, device, lsn, size, ope);
                Console.ReadLine();
                Environment.Exit(1);
            }*/
            if (time_t == 0 && device == 0 && lsn == 0 && size == 0 && ope == 0)
            {
                Console.WriteLine("probable read a blank line\n");
                Console.ReadLine();
            }

        }

        public virtual void Preprocess()
        {
            uint[] State = new uint[FTL.AddressMapper.AddressMappingDomains[AddressMappingModule.DefaultStreamID].TotalPagesNo];
            for (uint i = 0; i < FTL.AddressMapper.AddressMappingDomains[AddressMappingModule.DefaultStreamID].TotalPagesNo; i++)
                State[i] = 0;

            Console.WriteLine("\n");
            Console.WriteLine(".................................................\n");
            Console.WriteLine("Trace file preprocess started\n");

            string[] currentInputLine;
            char[] separators = { '\t', ' ', ',', '.' };
            ulong time = 0;
            uint device, reqSize = 0, sub_size, ope, add_size;
            ulong lsn;
            StreamReader traceFile2 = new StreamReader(traceFilePath);

            #region CalculateReqNo
            //calculate how many requets should be handled during simulation
            ulong totalReqsInFile = 0;
            while (traceFile2.Peek() >= 0)
            {
                currentInputLine = traceFile2.ReadLine().Split(separators);
                if (currentInputLine.Length < 5)
                    break;
                totalReqsInFile++;
            }
            if (percentageToBeSimulated == 100)
                numberOfRequestsToBeSeen = totalReqsInFile;
            else
                numberOfRequestsToBeSeen = Convert.ToUInt64(((double)percentageToBeSimulated / (double)100) * (double)totalReqsInFile);
            loggingStep = (numberOfRequestsToBeSeen * targetReplayCount) / totalLinesInLogfile;
            if (loggingStep == 0)
                loggingStep = 1;
            reqNoUnit = numberOfRequestsToBeSeen;
            traceFile2.Close();
            #endregion
            traceFile2 = new StreamReader(traceFilePath);
            ulong reqCntr = 0;
            while (traceFile2.Peek() >= 0 && reqCntr < numberOfRequestsToBeSeen)
            {
                currentInputLine = traceFile2.ReadLine().Split(separators);
                time = ulong.Parse(currentInputLine[0]);
                device = uint.Parse(currentInputLine[1]);
                lsn = uint.Parse(currentInputLine[2]);
                reqSize = uint.Parse(currentInputLine[3]);
                ope = uint.Parse(currentInputLine[4]);
                TraceAssert(time, device, lsn, reqSize, ope);
                reqCntr++;
                add_size = 0;
                //If address folding is disabled and lsn is greater than largetLSN, then ignore this request
                if (!foldAddress)
                    if ((lsn + reqSize * FTL.SubPageCapacity) > FTL.AddressMapper.LargestLSN)
                        continue;

                if (ope == 1)//read request
                {
                    while (add_size < reqSize)
                    {
                        lsn = lsn % FTL.AddressMapper.LargestLSN;
                        sub_size = FTL.SubpageNoPerPage - (uint)(lsn % FTL.SubpageNoPerPage);
                        if (add_size + sub_size >= reqSize)
                        {
                            sub_size = reqSize - add_size;
                            add_size += sub_size;
                        }

                        if ((sub_size > FTL.SubpageNoPerPage) || (add_size > reqSize))
                        {
                            Console.WriteLine("preprocess sub_size:{0}\n", sub_size);
                        }
                        ulong lpn = lsn / FTL.SubpageNoPerPage;

                        if (!this.ignoreUnallocatedReads)
                        {
                            if (State[lpn] == 0)
                            {
                                State[lpn] = FTL.SetEntryState(lsn, sub_size);
                                FTL.HandleMissingReadAccessTarget(lsn, State[lpn]);
                            }
                            else //if (State[lpn] > 0)
                            {
                                uint map_entry_new = FTL.SetEntryState(lsn, sub_size);
                                uint map_entry_old = State[lpn];
                                uint modify_temp = map_entry_new | map_entry_old;
                                State[lpn] = modify_temp;

                                if (((map_entry_new ^ map_entry_old) & map_entry_new) != 0)
                                {
                                    uint map_entry_old_real_map = FTL.AddressMapper.AddressMappingDomains[AddressMappingModule.DefaultStreamID].MappingTable.State[lpn];
                                    uint modify_real_map = ((map_entry_new ^ map_entry_old) & map_entry_new) | map_entry_old_real_map;
                                    if (FTL.AddressMapper.AddressMappingDomains[AddressMappingModule.DefaultStreamID].MappingTable.State[lpn] == 0)
                                        FTL.HandleMissingReadAccessTarget(lsn, modify_real_map);
                                    else
                                        FTL.ModifyPageTableStateforMissingRead(lpn, modify_real_map);
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
                        lsn = lsn % FTL.AddressMapper.LargestLSN;
                        sub_size = FTL.SubpageNoPerPage - (uint)(lsn % FTL.SubpageNoPerPage);
                        if (add_size + sub_size >= reqSize)
                        {
                            sub_size = reqSize - add_size;
                            add_size += sub_size;
                        }

                        if ((sub_size > FTL.SubpageNoPerPage) || (add_size > reqSize))
                        {
                            Console.WriteLine("preprocess sub_size:{0}\n", sub_size);
                        }

                        ulong lpn = lsn / FTL.SubpageNoPerPage;
                        if (State[lpn] == 0)
                            State[lpn] = FTL.SetEntryState(lsn, sub_size);   //0001
                        else
                        {
                            uint map_entry_new = FTL.SetEntryState(lsn, sub_size);
                            uint map_entry_old = FTL.AddressMapper.AddressMappingDomains[AddressMappingModule.DefaultStreamID].MappingTable.State[lpn];
                            uint modify = map_entry_new | map_entry_old;
                            State[lpn] = modify;
                        }
                        lsn = lsn + sub_size;
                        add_size += sub_size;
                    }
                }
            }
            Console.WriteLine("Trace file preprocess complete!\n");
            Console.WriteLine(".................................................\n");
            traceFile2.Close();
        }
        #endregion

        #region RequestGenerationFunctions
        private void createFlashTransaction(ulong lpn, uint sizeInSubpages, uint state, IORequest currentIORequest)
        {

            if (currentIORequest.Type == IORequestType.Read)
            {
                InternalReadRequest subRequest = new InternalReadRequest();
                FTL.TotalPageReads++;
                FTL.PageReadsForWorkload++;
                if (FTL.AddressMapper.AddressMappingDomains[currentIORequest.StreamID].MappingTable.State[lpn] == 0)
                {
                    for (var LPN = FTL.currentActiveWriteLPNs[currentIORequest.StreamID].First; LPN != null; LPN = LPN.Next)
                        if (LPN.Value == lpn)
                            return;
                    throw new Exception("Accessing an unwritten logical address for read!");
                }

                FTL.AddressMapper.ConvertPPNToPageAddress(FTL.AddressMapper.AddressMappingDomains[currentIORequest.StreamID].MappingTable.PPN[lpn], subRequest.TargetPageAddress);
                subRequest.TargetFlashChip = FTL.FlashChips[subRequest.TargetPageAddress.OverallFlashChipID];
                subRequest.LPN = lpn;
                subRequest.PPN = FTL.AddressMapper.AddressMappingDomains[currentIORequest.StreamID].MappingTable.PPN[lpn];
                subRequest.SizeInSubpages = sizeInSubpages;
                subRequest.SizeInByte = sizeInSubpages * FTL.SubPageCapacity;
                subRequest.BodyTransferCycles = subRequest.SizeInByte / FTL.ChannelWidthInByte;

                subRequest.State = (FTL.AddressMapper.AddressMappingDomains[currentIORequest.StreamID].MappingTable.State[lpn] & 0x7fffffff);
                subRequest.Type = InternalRequestType.Read;
                subRequest.RelatedIORequest = currentIORequest;

                //Arash: I have omitted a section of original code to ignore simultaneous read of same memory location

                subRequest.RelatedNodeInList = (FTL.ChannelInfos[subRequest.TargetPageAddress.ChannelID] as BusChannelSprinkler).WaitingInternalReadReqs.AddLast(subRequest);
                currentIORequest.InternalRequestList.AddLast(subRequest);
            }
            else //currentRequest.Type == IORequestType.Write
            {
                InternalWriteRequest subRequest = new InternalWriteRequest();
                FTL.currentActiveWriteLPNs[currentIORequest.StreamID].AddLast(lpn);
                FTL.TotalPageProgams++;
                FTL.PageProgramsForWorkload++;
                subRequest.LPN = lpn;
                subRequest.PPN = 0;
                subRequest.SizeInSubpages = sizeInSubpages;
                subRequest.SizeInByte = sizeInSubpages * FTL.SubPageCapacity;
                subRequest.BodyTransferCycles = subRequest.SizeInByte / FTL.ChannelWidthInByte;
                subRequest.State = state;
                subRequest.Type = InternalRequestType.Write;
                subRequest.RelatedIORequest = currentIORequest;
                //The above line should be positioned before AllocateLocation.
                FTL.AllocatePlaneToWriteTransaction_Sprinkler(subRequest);
                currentIORequest.InternalRequestList.AddLast(subRequest);
            }
        }
        /// <summary>
        /// Breaks current IORequest (recently read from trace file) into InternalRequest.
        /// If IORequest includes 7 pages, then 7 InternalRequests will be generated.
        /// </summary>
        /// <param name="currentRequest">The most recently arrived IORequest</param>
        /// 
        public virtual void SegmentIORequestNoCache_Sprinkler(IORequest currentRequest)
        {
            ulong lsn = currentRequest.LSN;
            uint reqSize = currentRequest.SizeInSubpages;

            uint subState = 0;
            uint handledSize = 0, subSize = 0;

            while (handledSize < reqSize)
            {
                lsn = lsn % FTL.AddressMapper.LargestLSN;
                subSize = FTL.SubpageNoPerPage - (uint)(lsn % FTL.SubpageNoPerPage);
                if (handledSize + subSize >= reqSize)
                {
                    subSize = reqSize - handledSize;
                    handledSize += subSize;
                }
                ulong lpn = lsn / FTL.SubpageNoPerPage;
                subState = FTL.SetEntryState(lsn, subSize);
                createFlashTransaction(lpn, subSize, subState, currentRequest);
                lsn = lsn + subSize;
                handledSize += subSize;
            }

            if (currentRequest.InternalRequestList.Count == 0)
                SendEarlyResponseToHost(currentRequest);
        }
        private uint transferSize(int need_distribute, ulong lpn, IORequest currentRequest)
        {
            ulong first_lpn, last_lpn;
            uint state, trans_size;
            uint mask = 0, offset1 = 0, offset2 = 0;

            first_lpn = (currentRequest.LSN) / (FTL.SubpageNoPerPage);
            last_lpn = (currentRequest.LSN + currentRequest.SizeInSubpages - 1) / (FTL.SubpageNoPerPage);

            mask = ~(0xffffffff << (int)(FTL.SubpageNoPerPage));
            state = mask;
            if (lpn == first_lpn)
            {
                offset1 = (uint)(FTL.SubpageNoPerPage - ((lpn + 1) * FTL.SubpageNoPerPage - currentRequest.LSN));
                state = state & (0xffffffff << (int)offset1);
            }
            if (lpn == last_lpn)
            {
                offset2 = (uint)(FTL.SubpageNoPerPage - ((lpn + 1) * FTL.SubpageNoPerPage - (currentRequest.LSN + currentRequest.SizeInSubpages)));
                state = state & (~(0xffffffff << (int)offset2));
            }

            trans_size = FTL.SizeInSubpages((uint)(state & need_distribute));//doroste?

            return trans_size;
        }
        public void SegmentIORequestWithCache_Sprinkler(IORequest currentRequest)
        {
            ulong start, end, first_lsn, last_lsn, lpn;
            uint j, k, sub_size;
            int i = 0;
            uint[] complt;

            uint full_page = ~(0xffffffff << (int)FTL.SubpageNoPerPage);


            if (currentRequest.DistributionFlag == 0)
            {
                if (currentRequest.CompleteLSNCount != currentRequest.SizeInSubpages)
                {
                    first_lsn = currentRequest.LSN;
                    last_lsn = first_lsn + currentRequest.SizeInSubpages;
                    complt = currentRequest.NeedDistrFlag;
                    start = first_lsn - (first_lsn % FTL.SubpageNoPerPage);
                    end = ((last_lsn / FTL.SubpageNoPerPage) + 1) * FTL.SubpageNoPerPage;
                    i = (int)(end - start) / 32;

                    while (i >= 0)
                    {
                        for (j = 0; j < (32 / FTL.SubpageNoPerPage); j++)
                        {
                            k = (complt[((int)(end - start) / 32 - i)] >> (int)(FTL.SubpageNoPerPage * j)) & full_page;
                            if (k != 0)
                            {
                                lpn = (ulong)(start / (FTL.SubpageNoPerPage) + ((end - start) / (ulong)(32 - i)) * 32 / FTL.SubpageNoPerPage + j);
                                sub_size = transferSize((int)k, lpn, currentRequest);//true?
                                if (sub_size == 0)
                                {
                                    continue;
                                }
                                else
                                {
                                    createFlashTransaction(lpn, sub_size, 0, currentRequest);
                                }
                            }
                        }
                        i = i - 1;
                    }

                }

            }

        }
        public override void ProcessXEvent(XEvent e)
        {
            switch ((HostInterfaceEventType)e.Type)
            {
                case HostInterfaceEventType.GenerateNextIORequest:
                    this.SetupPhaseFinished();
                    IORequest request = null;

                    ulong lsn = ulong.Parse(currentInputLine[2]);
                    uint size = uint.Parse(currentInputLine[3]);
                    if (FTL.AddressMapper.CheckRequestAddress(lsn, size, foldAddress))
                    {
                        lsn = lsn % FTL.AddressMapper.LargestLSN;//Actually, we should use this translation if folding is enabled.
                        if (currentInputLine[4] == "0")//write request in ascii traces
                        {
                            request = new IORequest(replayOffset + ulong.Parse(currentInputLine[0]), lsn, size, IORequestType.Write);
                            ReceivedWriteRequestCount++;
                            ReceivedRequestCount++;
                            if (NCQ.Count < NCQSize)
                            {
                                request.RelatedNodeInList = NCQ.AddLast(request);
                                SegmentIORequestNoCache_Sprinkler(request);
                                FTL.IOScheduler.Schedule((uint)StreamPriorityClass.Urgent, AddressMappingModule.DefaultStreamID);
                                //FTL.ServiceIORequest();
                            }
                            else
                                HostQueue.AddLast(request);
                        }
                        else//read request in ascii traces
                        {
                            bool goodRead = true;
                            if (ignoreUnallocatedReads)
                                if (!FTL.AddressMapper.CheckReadRequest(lsn, size))
                                {
                                    goodRead = false;
                                    ignoredRequestsCount++;
                                    thisRoundIgnoredRequestsCount++;
                                }

                            if (goodRead)
                            {
                                request = new IORequest(replayOffset + ulong.Parse(currentInputLine[0]), lsn, size, IORequestType.Read);
                                ReceivedReadRequestCount++;
                                ReceivedRequestCount++;
                                if (NCQ.Count < NCQSize)
                                {
                                    request.RelatedNodeInList = NCQ.AddLast(request);
                                    SegmentIORequestNoCache_Sprinkler(request);
                                    FTL.IOScheduler.Schedule((uint)StreamPriorityClass.Urgent, AddressMappingModule.DefaultStreamID);
                                    //FTL.ServiceIORequest();
                                }
                                else
                                    HostQueue.AddLast(request);
                            }
                        }
                    }
                    else
                    {
                        ignoredRequestsCount++;
                        thisRoundIgnoredRequestsCount++;
                    }
                    if (traceFile.Peek() >= 0)
                        currentInputLine = traceFile.ReadLine().Split(Separator);
                    else
                    {
                        reqExists = false;
                        currentInputLine = null;
                        if (replay)
                        {
                            traceFile.Close();
                            traceFile = new StreamReader(traceFilePath);
                            if (traceFile.Peek() >= 0)
                            {
                                currentInputLine = traceFile.ReadLine().Split(Separator);
                                numberOfRequestsToBeSeen += reqNoUnit;
                                replayOffset = XEngineFactory.XEngine.Time;
                                reqExists = true;
                                nextAnnouncementMilestone = 0.05;
                                thisRoundHandledRequestsCount = 0; thisRoundSumResponseTime = 0; thisRoundIgnoredRequestsCount = 0;
                                thisRoundHandledReadRequestsCount = 0; thisRoundSumResponseTimeR = 0;
                                thisRoundHandledWriteRequestsCount = 0; thisRoundSumResponseTimeW = 0;
                                this.FTL.GarbageCollector.ResetTemporaryLoggingVariables();
                                currentReplayRound++;
                                Console.WriteLine("\n\n******************************************");
                                Console.WriteLine("* Round {0} of workload execution started  *", currentReplayRound);
                                Console.WriteLine("******************************************\n");
                                if (currentReplayRound == targetReplayCount)
                                    StopReplay();
                            }
                        }

                    }

                    if ((ReceivedRequestCount + IgnoredRequestsCount) == numberOfRequestsToBeSeen)
                    {
                        reqExists = false;
                        currentInputLine = null;
                        if (replay)
                        {
                            traceFile.Close();
                            traceFile = new StreamReader(traceFilePath);
                            if (traceFile.Peek() >= 0)
                            {
                                currentInputLine = traceFile.ReadLine().Split(Separator);
                                numberOfRequestsToBeSeen += reqNoUnit;
                                replayOffset = XEngineFactory.XEngine.Time;
                                reqExists = true;
                                nextAnnouncementMilestone = 0.05;
                                thisRoundHandledRequestsCount = 0; thisRoundSumResponseTime = 0; thisRoundIgnoredRequestsCount = 0;
                                thisRoundHandledReadRequestsCount = 0; thisRoundSumResponseTimeR = 0;
                                thisRoundHandledWriteRequestsCount = 0; thisRoundSumResponseTimeW = 0;
                                this.FTL.GarbageCollector.ResetTemporaryLoggingVariables();
                                currentReplayRound++;
                                Console.WriteLine("\n\n******************************************");
                                Console.WriteLine("* Round {0} of workload execution started  *", currentReplayRound);
                                Console.WriteLine("******************************************\n");
                                if (currentReplayRound == targetReplayCount)
                                    StopReplay();
                            }
                        }
                    }

                    if (currentInputLine != null)
                        XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(replayOffset + ulong.Parse(currentInputLine[0]), this, null, 0));
                    break;
                case HostInterfaceEventType.RequestCompletedByDRAM:
                    IORequest targetRequest = e.Parameters as IORequest;
                    SendEarlyResponseToHost(targetRequest);
                    break;
                default:
                    throw new Exception("Unhandled XEvent type");
            }
        }

        /*
         * This function is used in passive operation of HostInterface
         * In this mode, FTL asks HostInterface for next IO request.
         */
        public virtual IORequest SaturatedMode_GetNextRequest()
        {
            IORequest request = null;

            if (!reqExists)
                return null;

            ulong lsn = ulong.Parse(currentInputLine[2]);
            uint size = uint.Parse(currentInputLine[3]);
            
            if (FTL.AddressMapper.CheckRequestAddress(lsn, size, foldAddress))
            {
                lsn = lsn % FTL.AddressMapper.LargestLSN;//Actually, we should use this translation if folding is enabled.
                if (currentInputLine[4] == "0")//write request in ascii traces
                {
                    request = new IORequest(XEngineFactory.XEngine.Time, lsn, size, IORequestType.Write);
                    ReceivedWriteRequestCount++;
                    ReceivedRequestCount++;
                    request.RelatedNodeInList = NCQ.AddLast(request);
                }
                else//read request in ascii traces
                {
                    bool goodRead = true;
                    if (ignoreUnallocatedReads)
                        if (!FTL.AddressMapper.CheckReadRequest(lsn, size))
                        {
                            goodRead = false;
                            ignoredRequestsCount++;
                            thisRoundIgnoredRequestsCount++;
                        }

                    if (goodRead)
                    {
                        request = new IORequest(XEngineFactory.XEngine.Time, lsn, size, IORequestType.Read);
                        ReceivedReadRequestCount++;
                        ReceivedRequestCount++;
                        request.RelatedNodeInList = NCQ.AddLast(request);
                    }
                }
            }
            else
            {
                ignoredRequestsCount++;
                thisRoundIgnoredRequestsCount++;
            }
            if (traceFile.Peek() >= 0)
                currentInputLine = traceFile.ReadLine().Split(Separator);
            else
            {
                reqExists = false;
                currentInputLine = null;
                if (replay)
                {
                    traceFile.Close();
                    traceFile = new StreamReader(traceFilePath);
                    if (traceFile.Peek() >= 0)
                    {
                        currentInputLine = traceFile.ReadLine().Split(Separator);
                        numberOfRequestsToBeSeen += reqNoUnit;
                        replayOffset = XEngineFactory.XEngine.Time;
                        reqExists = true;
                        nextAnnouncementMilestone = 0.05;
                        thisRoundHandledRequestsCount = 0; thisRoundSumResponseTime = 0; thisRoundIgnoredRequestsCount = 0;
                        thisRoundHandledReadRequestsCount = 0; thisRoundSumResponseTimeR = 0;
                        thisRoundHandledWriteRequestsCount = 0; thisRoundSumResponseTimeW = 0;
                        this.FTL.GarbageCollector.ResetTemporaryLoggingVariables();
                        currentReplayRound++;
                        Console.WriteLine("\n\n******************************************");
                        Console.WriteLine("* Round {0} of workload execution started  *", currentReplayRound);
                        Console.WriteLine("******************************************\n");
                        if (currentReplayRound == targetReplayCount)
                            StopReplay();
                    }
                }
            }

            if ((ReceivedRequestCount + IgnoredRequestsCount) == numberOfRequestsToBeSeen)
            {
                reqExists = false;
                currentInputLine = null;
                if (replay)
                {
                    traceFile.Close();
                    traceFile = new StreamReader(traceFilePath);
                    if (traceFile.Peek() >= 0)
                    {
                        currentInputLine = traceFile.ReadLine().Split(Separator);
                        numberOfRequestsToBeSeen += reqNoUnit;
                        replayOffset = XEngineFactory.XEngine.Time;
                        reqExists = true;
                        nextAnnouncementMilestone = 0.05;
                        thisRoundHandledRequestsCount = 0; thisRoundSumResponseTime = 0; thisRoundIgnoredRequestsCount = 0;
                        thisRoundHandledReadRequestsCount = 0; thisRoundSumResponseTimeR = 0;
                        thisRoundHandledWriteRequestsCount = 0; thisRoundSumResponseTimeW = 0;
                        this.FTL.GarbageCollector.ResetTemporaryLoggingVariables();
                        currentReplayRound++;
                        Console.WriteLine("\n\n******************************************");
                        Console.WriteLine("* Round {0} of workload execution started  *", currentReplayRound);
                        Console.WriteLine("******************************************\n");
                        if (currentReplayRound == targetReplayCount)
                            StopReplay();
                    }
                }
            }

            if (request == null)
                return SaturatedMode_GetNextRequest();
            
            return request;
        }
        #endregion

        #region StatisticsFunctions
        /* When an IO request is handled without creating any InternalRequest
         * this function is invoked (i.e., IO request is handled by cache or by requests currently being serviced.
         * For example if we have a read request for an lpn which is simultaneously being written with another
         * request, therefore we can simply return the value of write without performing any flash read operation.)*/
        public virtual void SendEarlyResponseToHost(IORequest targetIORequest)
        {
            NCQ.Remove(targetIORequest.RelatedNodeInList);
            targetIORequest.ResponseTime = XEngineFactory.XEngine.Time - targetIORequest.InitiationTime + requestProcessingTime;
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

            /* 
             * If we are in setup phase (in fact in setup phase of saturate mode where requests
             * are inserted into channel queues before simulation start point), then Host Interface
             * should not take any action.
             */
            if (!this._inSetupPhase)
            {
                if (RTLoggingEnabled)
                {
                    loggingCntr++;
                    if (loggingCntr % loggingStep == 0)
                    {
                        RTLogFile.WriteLine(XEngineFactory.XEngine.Time / 1000
                            + "\t" + AvgResponseTime + "\t" + ThisRoundAvgResponseTime
                            + "\t" + ((double)(this.FTL.IssuedMultiplaneReadCMD) / (double)this.FTL.IssuedReadCMD)
                            + "\t" + ((double)(this.FTL.IssuedInterleaveReadCMD) / (double)this.FTL.IssuedReadCMD)
                            + "\t" + ((double)(this.FTL.IssuedMultiplaneProgramCMD) / (double)this.FTL.IssuedProgramCMD)
                            + "\t" + ((double)(this.FTL.IssuedInterleaveProgramCMD) / (double)this.FTL.IssuedProgramCMD)
                            + "\t" + this.FTL.GarbageCollector.AverageEmergencyGCRate + "\t" + this.FTL.GarbageCollector.ThisRoundAverageEmergencyGCRate
                            + "\t" + this.AverageReadCMDWaitingTime + "\t" + this.AverageProgramCMDWaitingTime);
                        
                        if (targetIORequest.Type == IORequestType.Read)
                            RTLogFileR.WriteLine(XEngineFactory.XEngine.Time / 1000 + "\t" + AvgResponseTimeR + "\t" + ThisRoundAvgResponseTimeR);
                        else
                            RTLogFileW.WriteLine(XEngineFactory.XEngine.Time / 1000 + "\t" + +AvgResponseTimeW + "\t" + ThisRoundAvgResponseTimeW);
                    }
                }

                if (ThisRoundAllSeenRequestsPercent > nextAnnouncementMilestone)
                {
                    /*if (!warmedUp)
                        if (HandledRequestsPercent >= 0.1)
                            warmupReached();*/
                    nextAnnouncementMilestone += announcementStep;
                    OnStatReady();
                }

                if (Mode == ReqeustGenerationMode.Saturated)
                {
                    if (reqExists)
                    {
                        SegmentIORequestNoCache_Sprinkler(SaturatedMode_GetNextRequest());
                        FTL.IOScheduler.Schedule((uint)StreamPriorityClass.Urgent, AddressMappingModule.DefaultStreamID);
                        //FTL.ServiceIORequest();
                    }
                }
                else if (HostQueue.Count > 0)
                {
                    IORequest oldestRequest = HostQueue.First.Value;
                    HostQueue.RemoveFirst();
                    oldestRequest.RelatedNodeInList = NCQ.AddLast(oldestRequest);
                    SegmentIORequestNoCache_Sprinkler(oldestRequest);
                    FTL.IOScheduler.Schedule((uint) StreamPriorityClass.Urgent, AddressMappingModule.DefaultStreamID);
                    //FTL.ServiceIORequest();
                }
            }
        }
     
        public virtual void SendResponseToHost(InternalRequest internalReq)
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
                FTL.OnLPNServiced(AddressMappingModule.DefaultStreamID, internalReq.LPN);
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
                NCQ.Remove(targetIORequest.RelatedNodeInList);
                targetIORequest.ResponseTime = XEngineFactory.XEngine.Time - targetIORequest.InitiationTime + requestProcessingTime;
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

                if (RTLoggingEnabled)
                {
                    loggingCntr++;
                    if (loggingCntr % loggingStep == 0)
                    {
                        RTLogFile.WriteLine(XEngineFactory.XEngine.Time / 1000
                            + "\t" + AvgResponseTime + "\t" + ThisRoundAvgResponseTime
                            + "\t" + ((double)(this.FTL.IssuedMultiplaneReadCMD) / (double)this.FTL.IssuedReadCMD)
                            + "\t" + ((double)(this.FTL.IssuedInterleaveReadCMD) / (double)this.FTL.IssuedReadCMD)
                            + "\t" + ((double)(this.FTL.IssuedMultiplaneProgramCMD) / (double)this.FTL.IssuedProgramCMD)
                            + "\t" + ((double)(this.FTL.IssuedInterleaveProgramCMD) / (double)this.FTL.IssuedProgramCMD)
                            + "\t" + this.FTL.GarbageCollector.AverageEmergencyGCRate + "\t" + this.FTL.GarbageCollector.ThisRoundAverageEmergencyGCRate
                            + "\t" + this.AverageReadCMDWaitingTime + "\t" + this.AverageProgramCMDWaitingTime);
                        if (targetIORequest.Type == IORequestType.Read)
                            RTLogFileR.WriteLine(XEngineFactory.XEngine.Time / 1000 + "\t" + AvgResponseTimeR + "\t" + ThisRoundAvgResponseTimeR);
                        else
                            RTLogFileW.WriteLine(XEngineFactory.XEngine.Time / 1000 + "\t" + +AvgResponseTimeW + "\t" + ThisRoundAvgResponseTimeW);
                    }
                }
                
                if (ThisRoundAllSeenRequestsPercent > nextAnnouncementMilestone)
                {
                    /*if (!warmedUp)
                        if (HandledRequestsPercent >= 0.1)
                            warmupReached();*/
                    nextAnnouncementMilestone += announcementStep;
                    OnStatReady();
                }

                if (Mode == ReqeustGenerationMode.Saturated){
                    if (reqExists)
                    {
                        IORequest inputReq = this.SaturatedMode_GetNextRequest();
                        if (inputReq != null)//We added the ability to ignore existing host requests (out of range host LSN, ....), therefore we may receive null valued inputReq
                        {
                            SegmentIORequestNoCache_Sprinkler(inputReq);
                            FTL.IOScheduler.Schedule((uint)StreamPriorityClass.Urgent, AddressMappingModule.DefaultStreamID);
                            //FTL.ServiceIORequest();
                        }
                    }
                }
                else if (HostQueue.Count > 0)
                {
                    IORequest oldestRequest = HostQueue.First.Value;
                    HostQueue.RemoveFirst();
                    oldestRequest.RelatedNodeInList = NCQ.AddLast(oldestRequest);
                    SegmentIORequestNoCache_Sprinkler(oldestRequest);
                    FTL.IOScheduler.Schedule((uint) StreamPriorityClass.Urgent, AddressMappingModule.DefaultStreamID);
                    //FTL.ServiceIORequest();
                }
            }//if (targetIORequest.InternalRequestList.Count == 0)
        }

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
            //warmedUp = true;
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
            writer.WriteEndElement();


            writer.WriteStartElement(id + "_Statistics_AfterGCStart");
            writer.WriteAttributeString("ID", ID.ToString());
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
            writer.WriteEndElement();

            writer.WriteStartElement(id + "_Statistics" + "_BeforeGCStart");
            writer.WriteAttributeString("ID", ID.ToString());
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
            writer.WriteEndElement();

            traceFile.Close();
            if (RTLoggingEnabled)
            {
                RTLogFile.Close();
                RTLogFileR.Close();
                RTLogFileW.Close();
            }
        }
        #endregion

        #region Properties
        private void StopReplay()
        {
            this.replay = false;
        }
        private void ContinueReplay()
        {
            this.replay = true;
        }
        public bool IsSaturatedMode
        {
            get { return this.Mode == ReqeustGenerationMode.Saturated; }
        }
        public uint CurrentReplayRound
        {
            get { return this.currentReplayRound; }
        }

        #region CMDLifeTimeParameters
        public ulong AverageCMDLifeTime_BGC//The unit is microseconds
        {
            get { return this.averageOperationLifeTime_BGC; }
        }
        public ulong AverageCMDLifeTime//The unit is microseconds
        {
            get { return this.sumOfInternalRequestLifeTime / this.totalFlashOperations; }
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
            get { return this.sumOfInternalRequestExecutionTime / this.totalFlashOperations; }
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
            get { return this.sumOfInternalRequestTransferTime / this.totalFlashOperations; }
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
            get { return this.sumOfInternalRequestWaitingTime / this.totalFlashOperations; }
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
            get {
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

        #region ResponstTimeParameters
        public ulong AvgResponseTime//The unit is microseconds
        {
            get { return this.sumResponseTime / this.HandledRequestsCount; }
        }
        public ulong ThisRoundAvgResponseTime //Used for the purpose of logging
        {
            get { return this.thisRoundSumResponseTime / this.thisRoundHandledRequestsCount; }
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
            get { return (double)IgnoredRequestsCount / (double)numberOfRequestsToBeSeen; }
        }
        public double ThisRoundAllSeenRequestsPercent//Since we have some cases where we ignore host requests (no folding, or ignoring unwritten reads), we use this metric
        {
            get { return ((double)(this.thisRoundHandledRequestsCount + this.thisRoundIgnoredRequestsCount) / (double)this.reqNoUnit); }
        }
        public ulong HandledRequestsCount_BGC
        {
            get { return this.handledRequestsCount_BGC; }
        }
        public ulong HandledRequestsCount
        {
            get { return this.handledRequestsCount; }
        }
        public ulong IgnoredRequestsCount
        {
            get { return this.ignoredRequestsCount; }
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
                else return (((double)this.handledRequestsCount_BGC / (double)changeTime) * (double)NanoSecondCoeff);
            }
        }
        public double IOPS
        {
            get { return (((double)this.handledRequestsCount / (double)XEngineFactory.XEngine.Time) * (double)NanoSecondCoeff); }
        }
        public double IOPS_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.handledRequestsCount_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)NanoSecondCoeff);
            }
        }
        public double IOPSReads_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return (((double)this.handledReadRequestsCount_BGC / (double)changeTime) * (double)NanoSecondCoeff);
            }
        }
        public double IOPSReads
        {
            get { return (((double)this.handledReadRequestsCount / (double)XEngineFactory.XEngine.Time) * (double)NanoSecondCoeff); }
        }
        public double IOPSReads_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.handledReadRequestsCount_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)NanoSecondCoeff);
            }
        }
        public double IOPSWrites_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return (((double)this.handledWriteRequestsCount_BGC / (double)changeTime) * (double)NanoSecondCoeff);
            }
        }
        public double IOPSWrites
        {
            get { return (((double)this.handledWriteRequestsCount / (double)XEngineFactory.XEngine.Time) * (double)NanoSecondCoeff); }
        }
        public double IOPSWrites_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.handledWriteRequestsCount_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)NanoSecondCoeff);
            }
        }

        public double AggregateBandWidth_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return ((((double)this.transferredBytesCount_BGC / (double)changeTime) * (double)NanoSecondCoeff) / 1000000);
            }
        }
        public double AggregateBandWidth
        {
            get { return ((((double)this.transferredBytesCount / (double)XEngineFactory.XEngine.Time) * (double)NanoSecondCoeff) / 1000000); }
        }
        public double AggregateBandWidth_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return ((((double)this.transferredBytesCount_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)NanoSecondCoeff) / 1000000);
            }
        }
        public double AggregateBandWidthReads_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return ((((double)this.transferredBytesCountR_BGC / (double)changeTime) * (double)NanoSecondCoeff) / 1000000);
            }
        }
        public double AggregateBandWidthReads
        {
            get { return ((((double)this.transferredBytesCountR / (double)XEngineFactory.XEngine.Time) * (double)NanoSecondCoeff) / 1000000); }
        }
        public double AggregateBandWidthReads_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.transferredBytesCountR_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)NanoSecondCoeff);
            }
        }
        public double AggregateBandWidthWrites_BGC
        {
            get
            {
                if (changeTime == 0) return 0;
                else return (((double)this.transferredBytesCountW_BGC / (double)changeTime) * (double)NanoSecondCoeff);
            }
        }
        public double AggregateBandWidthWrites
        {
            get { return (((double)this.transferredBytesCountW / (double)XEngineFactory.XEngine.Time) * (double)NanoSecondCoeff); }
        }
        public double AggregateBandWidthWrites_AGC
        {
            get
            {
                if (!gcStarted) return 0;
                return (((double)this.transferredBytesCountW_AGC / (double)(XEngineFactory.XEngine.Time - changeTime)) * (double)NanoSecondCoeff);
            }
        }
        #endregion
        #endregion

        #region delegates
        public delegate void StatReadyHandler(object sender);
        public event StatReadyHandler onStatReady;
        protected void OnStatReady()
        {
            if (onStatReady != null)
                onStatReady(this);
        }

       #endregion
    }
}
