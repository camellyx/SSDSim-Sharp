using System;
using System.Collections.Generic;
using System.Text;
using Smulator.Util;
using Smulator.BaseComponents;
using System.IO;

namespace Smulator.SSD.Components
{
    #region Change info
    /// <change>
    /// <author>Arash Tavakkol</author>
    /// <description>Supoort for hot/cold model on Rosenblum [Reosenblum-92], added</description>
    /// <date>2014/04/10</date>
    /// </change>
    #endregion 
    /// <summary>
    /// <title>HostInterfaceSynthetic</title>
    /// <description> 
    /// Simple synthetic IO request generator.
    /// </description>
    /// <copyright>Copyright(c)2013</copyright>
    /// <company></company>
    /// <author>Arash Tavakkol ( www.arasht.ir )</author>
    /// <version>Version 1.0</version>
    /// <date>2013/08/18</date>
    /// </summary>
    
        public class HostInterfaceSynthetic : HostInterface
    {
        ulong averageRequestInterArrivalTime = 1000000;//nano-seconds
        double readRatio = 0.50000;

        RandomGenerator randomRequestTypeGenerator;
        int requestTypeGenerationSeed = 0;

        RandomGenerator randomTimeIntervalGenerator;
        int timeIntervalGenerationSeed = 0;

        RandomGenerator randomAddressGenerator1;
        InputStreamSynthetic.DistributionType addressDistributionType = InputStreamSynthetic.DistributionType.Uniform;
        int addressGenerationSeed1 = 0, addressGenerationSeed2 = 0, addressGenerationSeed3 = 0;
        ulong addressDistributionParam1 = 0, addressDistributionParam2 = 0;
        double hotSpaceRatio = 0.2000;//used in hot/cold generation model, the ratio of hot area
        double hotTrafficRate = 0.8000;//used in hot/cold generation model, the ratio of traffic that goes to hot area
        ulong hotAddressRange = 0;
        RandomGenerator randomHotColdRatioGenerator, randomHotAddressGenerator;

        RandomGenerator randomRequestSizeGenerator;
        InputStreamSynthetic.DistributionType requestSizeDistributionType = InputStreamSynthetic.DistributionType.Fixed;
        int requestSizeGenerationSeed = 0;
        uint requestSizeDistributionParam1B = 0, requestSizeDistributionParam2B = 0;//SP stands for Bytes

        ulong nextRequetArrivalTime = 0;
        InputStreamSynthetic inputStream = null;


        #region SetupFunctions
        public HostInterfaceSynthetic(
            string id,
            uint NCQSize,
            FTL ftl,
            Controller controller,
            AddressMappingDomain mappingDomain,
            HostInterface.ReqeustGenerationMode mode,
            ulong numberOfRequestsToBeGenerated,
            ulong averageRequestInterArrivalTime,
            uint readPercentage,
            InputStreamSynthetic.DistributionType addressDistributionType,
            double addressDistributionParam1,
            double addressDistributionParam2,
            InputStreamSynthetic.DistributionType requestSizeDistributionType,
            uint requestSizeDistributionParam1B,
            uint requestSizeDistributionParam2B,
            int seed,
            bool RTLoggingEnabled,
            string RTLogFilePath)
            : base(id)
        {
            this.NCQSize = NCQSize;
            this.FTL = ftl;
            this.Controller = controller;
            this.Mode = mode;

            this.numberOfRequestsToBeSeen = numberOfRequestsToBeGenerated;
            this.reqNoUnit = numberOfRequestsToBeGenerated;
            this.averageRequestInterArrivalTime = averageRequestInterArrivalTime;
            this.readRatio = (double)readPercentage / (double)100;

            inputStream = new InputStreamSynthetic("SynthFlow0", StreamPriorityClass.High, mappingDomain, numberOfRequestsToBeGenerated,
                averageRequestInterArrivalTime, readPercentage, addressDistributionType, addressDistributionParam1, addressDistributionParam2,
                requestSizeDistributionType, requestSizeDistributionParam1B, requestSizeDistributionParam2B, seed++);

            this.RTLoggingEnabled = RTLoggingEnabled;
            if (RTLoggingEnabled)
            {
                this.RTLogFilePath = RTLogFilePath;
                this.RTLogFile = new StreamWriter(RTLogFilePath);
                //this.RTLogFile.WriteLine("Time(us)\tAverageResponseTime(us)\tThisRoundAverageResponseTime(us)\tAverageRatePerMillisecond(gc/s)\tAverageRatePerMillisecondThisRound(gc/s)");
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
                    nextRequetArrivalTime = XEngineFactory.XEngine.Time + (ulong)(randomTimeIntervalGenerator.Exponential(averageRequestInterArrivalTime));
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent((ulong)(nextRequetArrivalTime), this, null, 0));
                    break;
                case ReqeustGenerationMode.Saturated:
                    while (NCQ.Count < NCQSize)
                        SegmentIORequestNoCache_Sprinkler(SaturatedMode_GetNextRequest());
                    break;
                default:
                    throw new Exception("Unhandled message generation mode!");
            }
        }
        #endregion

        #region PreprocessTrace
        public override void Preprocess()
        {
            loggingStep = numberOfRequestsToBeSeen / totalLinesInLogfile;
            if (loggingStep == 0)
                loggingStep = 1;

            uint[] State = new uint[FTL.AddressMapper.AddressMappingDomains[AddressMappingModule.DefaultStreamID].TotalPagesNo];
            for (uint i = 0; i < FTL.AddressMapper.AddressMappingDomains[AddressMappingModule.DefaultStreamID].TotalPagesNo; i++)
                State[i] = 0;

            Console.WriteLine("\n");
            Console.WriteLine(".................................................\n");
            Console.WriteLine("Preprocess started\n");

            ulong reqCntr = 0;
            ulong lsn = 0;
            uint reqSize = 0, sub_size, add_size;
            uint streamID = 0;
            while (reqCntr < numberOfRequestsToBeSeen)
            {
                #region RequestGeneration
                reqCntr++;
                switch (addressDistributionType)
                {
                    case InputStreamSynthetic.DistributionType.Uniform:
                        lsn = randomAddressGenerator1.UniformULong(addressDistributionParam1, addressDistributionParam2);
                        break;
                    case InputStreamSynthetic.DistributionType.Normal:
                        double templsn = randomAddressGenerator1.Normal(addressDistributionParam1, addressDistributionParam2);
                        lsn = (ulong)templsn;
                        if (templsn < 0)
                            lsn = 0;
                        else if (templsn > this.FTL.AddressMapper.LargestLSN)
                            lsn = this.FTL.AddressMapper.LargestLSN;
                        break;
                    case InputStreamSynthetic.DistributionType.Fixed:
                        lsn = addressDistributionParam1;
                        break;
                    case InputStreamSynthetic.DistributionType.HotCold:
                        if (randomHotColdRatioGenerator.Uniform(0, 1) < hotTrafficRate)
                        {
                            lsn = addressDistributionParam1 + randomHotAddressGenerator.UniformULong(0, hotAddressRange);
                        }
                        else
                        {
                            lsn = randomAddressGenerator1.UniformULong(0, this.FTL.AddressMapper.LargestLSN - hotAddressRange);
                            if (lsn > addressDistributionParam1)
                                lsn += hotAddressRange;
                        }
                        break;
                    default:
                        throw new Exception("Unknown distribution type for address.");
                }

                switch (requestSizeDistributionType)
                {
                    case InputStreamSynthetic.DistributionType.Uniform:
                        double tempReqSize = randomRequestSizeGenerator.Uniform(requestSizeDistributionParam1B, requestSizeDistributionParam2B);
                        reqSize = (uint)(Math.Ceiling(tempReqSize / (double)FTL.SubPageCapacity));
                        if (reqSize == 0)
                            reqSize = 1;
                        break;
                    case InputStreamSynthetic.DistributionType.Normal:
                        tempReqSize = randomRequestSizeGenerator.Normal(requestSizeDistributionParam1B, requestSizeDistributionParam2B);
                        reqSize = (uint)(Math.Ceiling(tempReqSize / (double)FTL.SubPageCapacity));
                        if (tempReqSize < 0)
                            reqSize = 1;
                        break;
                    case InputStreamSynthetic.DistributionType.Fixed:
                        reqSize = (uint)(Math.Ceiling(requestSizeDistributionParam1B / (double)FTL.SubPageCapacity));
                        break;
                    default:
                        throw new Exception("Uknown distribution type for requset size.");
                }
                #endregion

                add_size = 0;
                if (randomRequestTypeGenerator.Uniform(0, 1) < readRatio)//read request
                {
                    while (add_size < reqSize)
                    {
                        lsn = lsn % this.FTL.AddressMapper.LargestLSN;
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

                        lsn = lsn + sub_size;
                        add_size += sub_size;
                    }//while(add_size<size)
                }
                else
                {
                    while (add_size < reqSize)
                    {
                        lsn = lsn % this.FTL.AddressMapper.LargestLSN;
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
                            uint map_entry_old = FTL.AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn];
                            uint modify = map_entry_new | map_entry_old;
                            State[lpn] = modify;
                        }
                        lsn = lsn + sub_size;
                        add_size += sub_size;
                    }
                }
            }
            randomRequestSizeGenerator = new RandomGenerator(requestSizeGenerationSeed);
            randomRequestTypeGenerator = new RandomGenerator(requestTypeGenerationSeed);
            randomAddressGenerator1 = new RandomGenerator(addressGenerationSeed1);
            randomHotColdRatioGenerator = new RandomGenerator(addressGenerationSeed2);
            randomHotAddressGenerator = new RandomGenerator(addressGenerationSeed3);
            Console.WriteLine("Preprocess complete!\n");
            Console.WriteLine(".................................................\n");
        }
        #endregion
        #region RequestGenerationFunctions
        public override void ProcessXEvent(XEvent e)
        {
            switch ((HostInterfaceEventType)e.Type)
            {
                case HostInterfaceEventType.GenerateNextIORequest:
                    IORequest request = null;

                    IORequestType reqType = IORequestType.Write;
                    if (randomRequestTypeGenerator.Uniform(0, 1) < readRatio)
                    {
                        reqType = IORequestType.Read;
                        ReceivedReadRequestCount++;
                    }
                    else
                        ReceivedWriteRequestCount++;


                    ulong lsn = 0;
                    switch (addressDistributionType)
                    {
                        case InputStreamSynthetic.DistributionType.Uniform:
                            lsn = randomAddressGenerator1.UniformULong(addressDistributionParam1, addressDistributionParam2);
                            break;
                        case InputStreamSynthetic.DistributionType.Normal:
                            double templsn = randomAddressGenerator1.Normal(addressDistributionParam1, addressDistributionParam2);
                            lsn = (uint)templsn;
                            if (templsn < 0)
                                lsn = 0;
                            else if (templsn > this.FTL.AddressMapper.LargestLSN)
                                lsn = this.FTL.AddressMapper.LargestLSN;
                            break;
                        case InputStreamSynthetic.DistributionType.Fixed:
                            lsn = addressDistributionParam1;
                            break;
                        case InputStreamSynthetic.DistributionType.HotCold:
                            if (randomHotColdRatioGenerator.Uniform(0, 1) < hotTrafficRate)
                            {
                                lsn = addressDistributionParam1 + randomHotAddressGenerator.UniformULong(0, hotAddressRange);
                            }
                            else
                            {
                                lsn = randomAddressGenerator1.UniformULong(0, this.FTL.AddressMapper.LargestLSN - hotAddressRange);
                                if (lsn > addressDistributionParam1)
                                    lsn += hotAddressRange;
                            }
                            break;
                        default:
                            throw new Exception("Unknown distribution type for address.");
                    }

                    uint reqSize = 0;
                    switch (requestSizeDistributionType)
                    {
                        case InputStreamSynthetic.DistributionType.Uniform:
                            double tempReqSize = randomRequestSizeGenerator.Uniform(requestSizeDistributionParam1B, requestSizeDistributionParam2B);
                            reqSize = (uint)(Math.Ceiling(tempReqSize / (double)FTL.SubPageCapacity));
                            if (reqSize == 0)
                                reqSize = 1;
                            break;
                        case InputStreamSynthetic.DistributionType.Normal:
                            tempReqSize = randomRequestSizeGenerator.Normal(requestSizeDistributionParam1B, requestSizeDistributionParam2B);
                            reqSize = (uint)(Math.Ceiling(tempReqSize / (double)FTL.SubPageCapacity));
                            if (tempReqSize < 0)
                                reqSize = 1;
                            break;
                        case InputStreamSynthetic.DistributionType.Fixed:
                            reqSize = (uint)(Math.Ceiling(requestSizeDistributionParam1B / (double)FTL.SubPageCapacity));
                            break;
                        default:
                            throw new Exception("Uknown distribution type for requset size.");
                    }

                    request = new IORequest(XEngineFactory.XEngine.Time, lsn, reqSize, reqType);

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

                    if (ReceivedRequestCount < numberOfRequestsToBeSeen)
                    {
                        nextRequetArrivalTime = XEngineFactory.XEngine.Time + (ulong)(randomTimeIntervalGenerator.Exponential(averageRequestInterArrivalTime));
                        XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(nextRequetArrivalTime, this, null, 0));
                    }
                    break;
                case HostInterfaceEventType.RequestCompletedByDRAM:
                    IORequest targetRequest = e.Parameters as IORequest;
                    SendEarlyResponseToHost(targetRequest);
                    break;
                default:
                    throw new Exception("Unhandled XEvent type");
            }
        }

        public override IORequest SaturatedMode_GetNextRequest()
        {
            if (ReceivedRequestCount == numberOfRequestsToBeSeen)
                return null;

            ulong lsn = 0;
            switch (addressDistributionType)
            {
                case InputStreamSynthetic.DistributionType.Uniform:
                    lsn = randomAddressGenerator1.UniformULong(addressDistributionParam1, addressDistributionParam2);
                    break;
                case InputStreamSynthetic.DistributionType.Normal:
                    double templsn = randomAddressGenerator1.Normal(addressDistributionParam1, addressDistributionParam2);
                    lsn = (uint)templsn;
                    if (templsn < 0)
                        lsn = 0;
                    else if (templsn > this.FTL.AddressMapper.LargestLSN)
                        lsn = this.FTL.AddressMapper.LargestLSN;
                    break;
                case InputStreamSynthetic.DistributionType.Fixed:
                    lsn = addressDistributionParam1;
                    break;
                case InputStreamSynthetic.DistributionType.HotCold:
                    if (randomHotColdRatioGenerator.Uniform(0, 1) < hotTrafficRate)
                    {
                        lsn = addressDistributionParam1 + randomHotAddressGenerator.UniformULong(0, hotAddressRange);
                    }
                    else
                    {
                        lsn = randomAddressGenerator1.UniformULong(0, this.FTL.AddressMapper.LargestLSN - hotAddressRange);
                        if (lsn > addressDistributionParam1)
                            lsn += hotAddressRange;
                    }
                    break;
                default:
                    throw new Exception("Unknown distribution type for address.");
            }

            uint reqSize = 0;
            switch (requestSizeDistributionType)
            {
                case InputStreamSynthetic.DistributionType.Uniform:
                    double tempReqSize = randomRequestSizeGenerator.Uniform(requestSizeDistributionParam1B, requestSizeDistributionParam2B);
                    reqSize = (uint)(Math.Ceiling(tempReqSize / (double)FTL.SubPageCapacity));
                    if (reqSize == 0)
                        reqSize = 1;
                    break;
                case InputStreamSynthetic.DistributionType.Normal:
                    tempReqSize = randomRequestSizeGenerator.Normal(requestSizeDistributionParam1B, requestSizeDistributionParam2B);
                    reqSize = (uint)(Math.Ceiling(tempReqSize / (double)FTL.SubPageCapacity));
                    if (tempReqSize < 0)
                        reqSize = 1;
                    break;
                case InputStreamSynthetic.DistributionType.Fixed:
                    reqSize = (uint)(Math.Ceiling(requestSizeDistributionParam1B / (double)FTL.SubPageCapacity));
                    break;
                default:
                    throw new Exception("Uknown distribution type for requset size.");
            }
            IORequest request = null;
            if (randomRequestTypeGenerator.Uniform(0, 1) < readRatio)
            {
                request = new IORequest(XEngineFactory.XEngine.Time, lsn, reqSize, IORequestType.Read);
                ReceivedReadRequestCount++;
            }
            else
            {
                request = new IORequest(XEngineFactory.XEngine.Time, lsn, reqSize, IORequestType.Write);
                ReceivedWriteRequestCount++;
            }

            ReceivedRequestCount++;
            if (ReceivedRequestCount == numberOfRequestsToBeSeen)
                reqExists = false;

            request.RelatedNodeInList = NCQ.AddLast(request);

            return request;
        }
        #endregion

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
            writer.WriteAttributeString("AvgResponseTime_us", AvgResponseTime.ToString());
            writer.WriteAttributeString("MinResponseTime_us", MinResponseTime.ToString());
            writer.WriteAttributeString("MaxResponseTime_us", MaxResponseTime.ToString());
            writer.WriteAttributeString("AvgResponseTimeRead_us", AvgResponseTimeR.ToString());
            writer.WriteAttributeString("MinResponseTimeRead_us", MinResponseTimeR.ToString());
            writer.WriteAttributeString("MaxResponseTimeRead_us", MaxResponseTimeR.ToString());
            writer.WriteAttributeString("AvgResponseTimeWrite_us", AvgResponseTimeW.ToString());
            writer.WriteAttributeString("MinResponseTimeWrite_us", MinResponseTimeW.ToString());
            writer.WriteAttributeString("MaxResponseTimeWrite_us", MaxResponseTimeW.ToString());

            writer.WriteAttributeString("AverageCMDLifeTime_ms", AverageCMDLifeTime.ToString());
            writer.WriteAttributeString("AverageCMDExecutionTime_ms", AverageCMDExecutionTime.ToString());
            writer.WriteAttributeString("AverageCMDTransferTime_ms", AverageCMDTransferTime.ToString());
            writer.WriteAttributeString("AverageCMDWaitingTime_ms", AverageCMDWaitingTime.ToString());

            writer.WriteAttributeString("AverageReadCMDLifeTime_ms", AverageReadCMDLifeTime.ToString());
            writer.WriteAttributeString("AverageReadCMDExecutionTime_ms", AverageReadCMDExecutionTime.ToString());
            writer.WriteAttributeString("AverageReadCMDTransferTime_ms", AverageReadCMDTransferTime.ToString());
            writer.WriteAttributeString("AverageReadCMDWaitingTime_ms", AverageReadCMDWaitingTime.ToString());

            writer.WriteAttributeString("AverageProgramCMDLifeTime_ms", AverageProgramCMDLifeTime.ToString());
            writer.WriteAttributeString("AverageProgramCMDExecutionTime_ms", AverageProgramCMDExecutionTime.ToString());
            writer.WriteAttributeString("AverageProgramCMDTransferTime_ms", AverageProgramCMDTransferTime.ToString());
            writer.WriteAttributeString("AverageProgramCMDWaitingTime_ms", AverageProgramCMDWaitingTime.ToString());

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

            writer.WriteAttributeString("AverageCMDLifeTime_ms", AverageCMDLifeTime_AGC.ToString());
            writer.WriteAttributeString("AverageCMDExecutionTime_ms", AverageCMDExecutionTime_AGC.ToString());
            writer.WriteAttributeString("AverageCMDTransferTime_ms", AverageCMDTransferTime_AGC.ToString());
            writer.WriteAttributeString("AverageCMDWaitingTime_ms", AverageCMDWaitingTime_AGC.ToString());

            writer.WriteAttributeString("AverageReadCMDLifeTime_ms", AverageReadCMDLifeTime_AGC.ToString());
            writer.WriteAttributeString("AverageReadCMDExecutionTime_ms", AverageReadCMDExecutionTime_AGC.ToString());
            writer.WriteAttributeString("AverageReadCMDTransferTime_ms", AverageReadCMDTransferTime_AGC.ToString());
            writer.WriteAttributeString("AverageReadCMDWaitingTime_ms", AverageReadCMDWaitingTime_AGC.ToString());

            writer.WriteAttributeString("AverageProgramCMDLifeTime_ms", AverageProgramCMDLifeTime_AGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDExecutionTime_ms", AverageProgramCMDExecutionTime_AGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDTransferTime_ms", AverageProgramCMDTransferTime_AGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDWaitingTime_ms", AverageProgramCMDWaitingTime_AGC.ToString());

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

            writer.WriteAttributeString("AverageCMDLifeTime_ms", AverageCMDLifeTime_BGC.ToString());
            writer.WriteAttributeString("AverageCMDExecutionTime_ms", AverageCMDExecutionTime_BGC.ToString());
            writer.WriteAttributeString("AverageCMDTransferTime_ms", AverageCMDTransferTime_BGC.ToString());
            writer.WriteAttributeString("AverageCMDWaitingTime_ms", AverageCMDWaitingTime_BGC.ToString());
            writer.WriteAttributeString("AverageReadCMDLifeTime_ms", AverageReadCMDLifeTime_BGC.ToString());
            writer.WriteAttributeString("AverageReadCMDExecutionTime_ms", AverageReadCMDExecutionTime_BGC.ToString());
            writer.WriteAttributeString("AverageReadCMDTransferTime_ms", AverageReadCMDTransferTime_BGC.ToString());
            writer.WriteAttributeString("AverageReadCMDWaitingTime_ms", AverageReadCMDWaitingTime_BGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDLifeTime_ms", AverageProgramCMDLifeTime_BGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDExecutionTime_ms", AverageProgramCMDExecutionTime_BGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDTransferTime_ms", AverageProgramCMDTransferTime_BGC.ToString());
            writer.WriteAttributeString("AverageProgramCMDWaitingTime_ms", AverageProgramCMDWaitingTime_BGC.ToString());

            writer.WriteAttributeString("IOPS", IOPS_BGC.ToString());
            writer.WriteAttributeString("IOPSRead", IOPSReads_BGC.ToString());
            writer.WriteAttributeString("IOPSWrite", IOPSWrites_BGC.ToString());
            writer.WriteAttributeString("AggregateBandWidth_MB", AggregateBandWidth_BGC.ToString());
            writer.WriteAttributeString("AggregateBandWidthRead_MB", AggregateBandWidthReads_BGC.ToString());
            writer.WriteAttributeString("AggregateBandWidthWrites_MB", AggregateBandWidthWrites_BGC.ToString());
            writer.WriteEndElement();


            if (RTLoggingEnabled)
            {
                RTLogFile.Close();
                RTLogFileR.Close();
                RTLogFileW.Close();
            }
        }

    }
}
