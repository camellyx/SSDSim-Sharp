using System;
using Smulator.BaseComponents;
using Smulator.Util;

namespace Smulator.SSD.Components
{
    public class InputStreamSynthetic : InputStreamBase
    {
        public enum DistributionType { Uniform, Fixed, Normal, HotCold };
        ulong averageRequestInterArrivalTime = 1000000;//nano-seconds
        double readRatio = 0.50000;

        RandomGenerator randomRequestTypeGenerator;
        int requestTypeGenerationSeed = 0;

        RandomGenerator randomTimeIntervalGenerator;
        int timeIntervalGenerationSeed = 0;

        RandomGenerator randomAddressGenerator1;
        DistributionType addressDistributionType = DistributionType.Uniform;
        int addressGenerationSeed1 = 0, addressGenerationSeed2 = 0, addressGenerationSeed3 = 0;
        ulong _addressDistributionParam1 = 0, _addressDistributionParam2 = 0;
        double hotSpaceRatio = 0.2000;//used in hot/cold generation model, the ratio of hot area
        double hotTrafficRate = 0.8000;//used in hot/cold generation model, the ratio of traffic that goes to hot area
        ulong hotAddressRange = 0;
        RandomGenerator randomHotColdRatioGenerator, randomHotAddressGenerator;

        RandomGenerator randomRequestSizeGenerator;
        DistributionType requestSizeDistributionType = DistributionType.Fixed;
        int requestSizeGenerationSeed = 0;
        uint _requestSizeDistributionParam1Sector = 0, _requestSizeDistributionParam2Sector = 0;

        ulong nextRequetArrivalTime = 0;

        public InputStreamSynthetic(string flowName, StreamPriorityClass priorityClass, AddressMappingDomain addressMappingDomain,
            ulong numberOfRequestsToBeGenerated,
            ulong averageRequestInterArrivalTime,
            double readRatio,
            DistributionType addressDistributionType,
            double addressDistributionParam1,
            double addressDistributionParam2,
            DistributionType requestSizeDistributionType,
            uint requestSizeDistributionParam1B,
            uint requestSizeDistributionParam2B,
            int seed)
            : base(flowName, priorityClass, addressMappingDomain)
        {
            NumberOfRequestsToGenerate = numberOfRequestsToBeGenerated;
            this.averageRequestInterArrivalTime = averageRequestInterArrivalTime;
            this.readRatio = readRatio;
            SimulationStopTime = averageRequestInterArrivalTime * numberOfRequestsToBeGenerated;

            this.addressDistributionType = addressDistributionType;
            switch (addressDistributionType)
            {
                case DistributionType.Uniform:
                    if (addressDistributionParam1 > addressDistributionParam2)
                    {
                        Console.WriteLine("Bad parameter specified for address distribution");
                        double temp = addressDistributionParam1;
                        addressDistributionParam1 = addressDistributionParam2;
                        addressDistributionParam2 = temp;
                    }

                    if (addressDistributionParam1 != 0 && addressDistributionParam1 <= 1)
                        this._addressDistributionParam1 = (ulong)(addressDistributionParam1 * addressMappingDomain.LargestLSN);
                    else if (addressDistributionParam1 != 0)
                    {
                        addressDistributionParam1 = 0;
                        Console.WriteLine("Bad parameter specified for address distribution");
                    }
                    if (addressDistributionParam2 != 0 && addressDistributionParam2 <= 1 && addressDistributionParam1 < addressDistributionParam2)
                        this._addressDistributionParam2 = (ulong)(addressDistributionParam2 * addressMappingDomain.LargestLSN);
                    else
                    {
                        if (addressDistributionParam2 > 1)
                            Console.WriteLine("Bad parameter specified for address distribution");
                        this._addressDistributionParam2 = addressMappingDomain.LargestLSN;
                    }
                    break;
                case DistributionType.Normal:
                    if (addressDistributionParam1 > 0 && addressDistributionParam1 < addressMappingDomain.LargestLSN)
                        this._addressDistributionParam1 = ((ulong)addressDistributionParam1 * addressMappingDomain.LargestLSN);
                    else
                    {
                        this._addressDistributionParam1 = addressMappingDomain.LargestLSN / 2;
                        Console.WriteLine("Bad parameter specified for address distribution");
                    }

                    this._addressDistributionParam2 = ((ulong)addressDistributionParam2 * addressMappingDomain.LargestLSN);
                    break;
                case DistributionType.HotCold:
                    if (!(addressDistributionParam1 <= 1.0))
                    {
                        addressDistributionParam1 = 0;
                        Console.WriteLine("Bad value for f in Hot/Cold address distribution!\nI set it to zero.");
                    }
                    if (!(addressDistributionParam2 <= 0.1))
                    {
                        addressDistributionParam2 = 0;
                        Console.WriteLine("Bad value for r in Hot/Cold address distribution!\nI set it to zero.");
                    }
                    hotSpaceRatio = addressDistributionParam1;
                    hotTrafficRate = addressDistributionParam2;
                    hotAddressRange = (ulong)(hotSpaceRatio * addressMappingDomain.LargestLSN);
                    RandomGenerator tempRand = new RandomGenerator(++seed);//Math.Abs(DateTime.Now.Ticks.GetHashCode()));
                    this._addressDistributionParam1 = tempRand.UniformULong(0, addressMappingDomain.LargestLSN - (ulong)hotAddressRange);//Used to find the start address of the hot area

                    addressGenerationSeed2 = ++seed;
                    randomHotColdRatioGenerator = new RandomGenerator(addressGenerationSeed2);
                    addressGenerationSeed3 = ++seed;
                    randomHotAddressGenerator = new RandomGenerator(addressGenerationSeed3);
                    break;
                case DistributionType.Fixed:
                default:
                    throw new Exception("Unhandled address distribution type!");
            }

            this.requestSizeDistributionType = requestSizeDistributionType;
            _requestSizeDistributionParam1Sector = requestSizeDistributionParam1B;
            _requestSizeDistributionParam2Sector = requestSizeDistributionParam2B;
            if (this.requestSizeDistributionType == DistributionType.Uniform)
            {
                if (requestSizeDistributionParam1B > requestSizeDistributionParam2B)
                {
                    Console.WriteLine("Bad parameter sepcified for request size distribution");
                    uint temp = _requestSizeDistributionParam1Sector;
                    _requestSizeDistributionParam1Sector = _requestSizeDistributionParam2Sector;
                    _requestSizeDistributionParam2Sector = temp;
                }
            }

            requestSizeGenerationSeed = ++seed;
            randomRequestSizeGenerator = new RandomGenerator(requestSizeGenerationSeed);
            requestTypeGenerationSeed = ++seed;
            randomRequestTypeGenerator = new RandomGenerator(++requestTypeGenerationSeed);
            timeIntervalGenerationSeed = ++seed;
            randomTimeIntervalGenerator = new RandomGenerator(timeIntervalGenerationSeed);
            addressGenerationSeed1 = ++seed;
            randomAddressGenerator1 = new RandomGenerator(addressGenerationSeed1);
        }
        public override void Preprocess(HostInterface hostInterface, ulong simulationStopTime, bool foldAddress, bool ignoreUnallocatedReads)
        {
            uint[] State = new uint[hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].TotalPagesNo];
            for (uint i = 0; i < hostInterface.FTL.AddressMapper.AddressMappingDomains[_id].TotalPagesNo; i++)
                State[i] = 0;

            Console.WriteLine("Stream " + FlowName + "\n");

            ulong reqCntr = 0;
            ulong lsn = 0;
            uint reqSize = 0, sub_size, add_size;
            ulong currentTime = (ulong) randomTimeIntervalGenerator.Exponential(averageRequestInterArrivalTime);
            SimulationStopTime = simulationStopTime;
            while (reqCntr < NumberOfRequestsToGenerate || currentTime <= simulationStopTime)
            {
                #region RequestGeneration
                reqCntr++;
                switch (addressDistributionType)
                {
                    case DistributionType.Uniform:
                        lsn = randomAddressGenerator1.UniformULong(((ulong)_addressDistributionParam1 * AddressMappingDomain.LargestLSN),
                            ((ulong)_addressDistributionParam2 * AddressMappingDomain.LargestLSN));
                        break;
                    case DistributionType.Normal:
                        double templsn = randomAddressGenerator1.Normal(_addressDistributionParam1, _addressDistributionParam2);
                        lsn = (ulong)templsn;
                        if (templsn < 0)
                            lsn = 0;
                        else if (templsn > hostInterface.FTL.AddressMapper.LargestLSN)
                            lsn = hostInterface.FTL.AddressMapper.LargestLSN;
                        break;
                    case DistributionType.HotCold:
                        if (randomHotColdRatioGenerator.Uniform(0, 1) < hotTrafficRate)
                        {
                            lsn = _addressDistributionParam1 + randomHotAddressGenerator.UniformULong(0, hotAddressRange);
                        }
                        else
                        {
                            lsn = randomAddressGenerator1.UniformULong(0, hostInterface.FTL.AddressMapper.LargestLSN - hotAddressRange);
                            if (lsn > _addressDistributionParam1)
                                lsn += hotAddressRange;
                        }
                        break;
                    case DistributionType.Fixed:
                    default:
                        throw new Exception("Unknown distribution type for address.");
                }

                switch (requestSizeDistributionType)
                {
                    case DistributionType.Uniform:
                        double tempReqSize = randomRequestSizeGenerator.Uniform(_requestSizeDistributionParam1Sector, _requestSizeDistributionParam2Sector);
                        reqSize = (uint)(Math.Ceiling(tempReqSize));
                        if (reqSize == 0)
                            reqSize = 1;
                        break;
                    case DistributionType.Normal:
                        tempReqSize = randomRequestSizeGenerator.Normal(_requestSizeDistributionParam1Sector, _requestSizeDistributionParam2Sector);
                        reqSize = (uint)(Math.Ceiling(tempReqSize));
                        if (tempReqSize < 0)
                            reqSize = 1;
                        break;
                    case DistributionType.Fixed:
                        reqSize = _requestSizeDistributionParam1Sector;
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
                        lsn = lsn % hostInterface.FTL.AddressMapper.LargestLSN;
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
                        {
                            State[lpn] = hostInterface.FTL.SetEntryState(lsn, sub_size);
                            hostInterface.FTL.HandleMissingReadAccessTarget(lsn, State[lpn]);
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
                                    hostInterface.FTL.HandleMissingReadAccessTarget(lsn, modify_real_map);
                                else
                                    hostInterface.FTL.ModifyPageTableStateforMissingRead(lpn, modify_real_map);
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
                        lsn = lsn % hostInterface.FTL.AddressMapper.LargestLSN;
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
                currentTime += (ulong)randomTimeIntervalGenerator.Exponential(averageRequestInterArrivalTime);
            }
            NumberOfRequestsToGenerate = reqCntr;
            randomRequestSizeGenerator = new RandomGenerator(requestSizeGenerationSeed);
            randomRequestTypeGenerator = new RandomGenerator(requestTypeGenerationSeed);
            randomTimeIntervalGenerator = new RandomGenerator(timeIntervalGenerationSeed);
            randomAddressGenerator1 = new RandomGenerator(addressGenerationSeed1);
            randomHotColdRatioGenerator = new RandomGenerator(addressGenerationSeed2);
            randomHotAddressGenerator = new RandomGenerator(addressGenerationSeed3);

            nextRequetArrivalTime = XEngineFactory.XEngine.Time + (ulong)(randomTimeIntervalGenerator.Exponential(averageRequestInterArrivalTime));
            XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent((ulong)(nextRequetArrivalTime), hostInterface, _id, 0));
        }
        public override IORequest GetNextIORequest(HostInterface hostInterface, bool foldAddress, bool ignoreUnallocatedReads)
        {
            ReceivedRequestCount++;
            IORequestType reqType = IORequestType.Write;
            if (randomRequestTypeGenerator.Uniform(0, 1) < readRatio)
            {
                reqType = IORequestType.Read;
                ReceivedReadRequestCount++;
            }
            else ReceivedWriteRequestCount++;

            ulong lsn = 0;
            switch (addressDistributionType)
            {
                case DistributionType.Uniform:
                    lsn = randomAddressGenerator1.UniformULong((ulong)(_addressDistributionParam1 * AddressMappingDomain.LargestLSN)
                        , ((ulong)_addressDistributionParam2 * AddressMappingDomain.LargestLSN));
                    break;
                case DistributionType.Normal:
                    double templsn = randomAddressGenerator1.Normal(_addressDistributionParam1, _addressDistributionParam2);
                    lsn = (uint)templsn;
                    if (templsn < 0)
                        lsn = 0;
                    else if (templsn > AddressMappingDomain.LargestLSN)
                        lsn = AddressMappingDomain.LargestLSN;
                    break;
                case DistributionType.HotCold:
                    if (randomHotColdRatioGenerator.Uniform(0, 1) < hotTrafficRate)
                    {
                        lsn = _addressDistributionParam1 + randomHotAddressGenerator.UniformULong(0, hotAddressRange);
                    }
                    else
                    {
                        lsn = randomAddressGenerator1.UniformULong(0, AddressMappingDomain.LargestLSN - hotAddressRange);
                        if (lsn > _addressDistributionParam1)
                            lsn += hotAddressRange;
                    }
                    break;
                case DistributionType.Fixed:
                default:
                    throw new Exception("Unknown distribution type for address.");
            }

            uint reqSize = 0;
            switch (requestSizeDistributionType)
            {
                case DistributionType.Uniform:
                    double tempReqSize = randomRequestSizeGenerator.Uniform(_requestSizeDistributionParam1Sector, _requestSizeDistributionParam2Sector);
                    reqSize = (uint)(Math.Ceiling(tempReqSize));
                    if (reqSize == 0)
                        reqSize = 1;
                    break;
                case DistributionType.Normal:
                    tempReqSize = randomRequestSizeGenerator.Normal(_requestSizeDistributionParam1Sector, _requestSizeDistributionParam2Sector);
                    reqSize = (uint)(Math.Ceiling(tempReqSize));
                    if (tempReqSize < 0)
                        reqSize = 1;
                    break;
                case DistributionType.Fixed:
                    reqSize = _requestSizeDistributionParam1Sector;
                    break;
                default:
                    throw new Exception("Uknown distribution type for requset size.");
            }

            IORequest request = new IORequest(XEngineFactory.XEngine.Time, lsn, reqSize, reqType);
            request.RelatedNodeInList = SubmissionQueue.AddLast(request);
            if (HeadRequest == null)
                HeadRequest = request.RelatedNodeInList;
            if (ReceivedRequestCount < NumberOfRequestsToGenerate)
            {
                nextRequetArrivalTime = XEngineFactory.XEngine.Time + (ulong)(randomTimeIntervalGenerator.Exponential(averageRequestInterArrivalTime));
                if (nextRequetArrivalTime <= SimulationStopTime)
                    XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(nextRequetArrivalTime, hostInterface, _id, 0));
            }
            return request;
        }
        public override void Close()
        {
        }
    }
}
