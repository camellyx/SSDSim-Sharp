using System;
using Smulator.SSD.Components;

namespace Smulator.Disk.SSD.NetworkGenerator
{
    public class ControllerGenerator
    {
		public ControllerGenerator()
		{
		}

        public Controller BuildControllerBus(
			string id,
			ControllerParameterSet ctrlParams,
            FlashChipParameterSet flashChipParams,
            InterconnectParameterSet netParams,
            MultistageSchedulingParameters copParameters,
            FlashChip[][] flashChips,
            string RTLogFilePath,//path of response time log file: Used to record each response time of each request
            int seed
			)
		{
            return BuildControllerBus(
                id,
                netParams.BusChannelCount,
                netParams.ChipCountPerChannel,
                NotListedNetworkParameters.channelWidth,
                netParams.readTransferCycleTime,
                netParams.writeTransferCycleTime,
                NotListedNetworkParameters.readCommandAddressCycleCount,
                NotListedNetworkParameters.writeCommandAddressCycleCount,
                NotListedNetworkParameters.eraseCommandAddressCycleCount,
                NotListedNetworkParameters.dummyBusyTime,
                NotListedNetworkParameters.readDataOutputReadyTime,
                NotListedNetworkParameters.ALEtoDataStartTransitionTime,
                NotListedNetworkParameters.WEtoRBTransitionTime,
                NotListedFlashChipParameters.SuspendWriteSetup,
                NotListedFlashChipParameters.SuspendEraseSetup,
                flashChipParams.pageReadLatency,/*page read delay in nano-seconds*/
                flashChipParams.pageWriteLatency,/*page write delay in nano-seconds*/
                flashChipParams.blockEraseLatency,/*block erase delay in nano-seconds*/
                flashChipParams.dieNoPerChip,
                flashChipParams.planeNoPerDie,
                flashChipParams.blockNoPerPlane,
                flashChipParams.pageNoPerBlock,
                flashChipParams.pageCapacity,
                NotListedFlashChipParameters.blockEraseLimit,
                NotListedControllerParameterSet.HostBufferSize,
                NotListedControllerParameterSet.GCProperties.GCPolicy,
                NotListedControllerParameterSet.GCProperties.RGAConstant,
                NotListedControllerParameterSet.WGreedyWindowSize,
                NotListedControllerParameterSet.DynamicWearLevelingEnabled,
                ctrlParams.HostInterfaceType,
                ctrlParams.InitialStaus,
                ctrlParams.PercentageOfValidPages,
                ctrlParams.PercentageOfValidPagesStdDev,
                ctrlParams.OverprovisionRatio,
                ctrlParams.DataCachingEnabled,
                ctrlParams.DataCacheCapacity,
                ctrlParams.DFTLEnabled,
                ctrlParams.DFTLCapacity,
                ctrlParams.SchedulingPolicy,
                ctrlParams.SchedulingWindowSize,
                ctrlParams.PlaneAllocationScheme,
                ctrlParams.BlockAllocationScheme,
                NotListedControllerParameterSet.TwainBlockManagementEnabled,
                NotListedControllerParameterSet.CopyBackEnabled,
                NotListedControllerParameterSet.CopybackRequiresOddEvenConstraint,
                NotListedControllerParameterSet.MultiplaneCMDEnabled,
                NotListedControllerParameterSet.BAConstraintForMultiplane,
                NotListedControllerParameterSet.InterleavedCMDEnabled,
                ctrlParams.ReadToWritePrioritizationFactor,
                ctrlParams.WriteToErasePrioritizationFactor,
                ctrlParams.Workloads,
                ctrlParams.LoggingEnabled,
                copParameters,
                RTLogFilePath,//path of response time log file: Used to record each response time of each request
                flashChips,
                seed);
		}

        public static char[] Separator = { ' ', ',', '-', '\t', '\n' };
        public Controller BuildControllerBus(
            string id,
            uint rowCount,
            uint chipNoPerRow,
            uint channelWidth,
            ulong readTransferCycleTime,
            ulong writeTransferCycleTime,
            ulong readCommandAddressCycleCount,
            ulong writeCommandAddressCycleCount,
            ulong eraseCommandAddressCycleCount,
            ulong dummyBusyTime,
            ulong readDataOutputReadyTime,
            ulong ALEtoDataStartTransitionTime,
            ulong WEtoRBTransitionTime,
            ulong suspendProgramTime,
            ulong suspendEraseTime,
            ulong readLatency,/*page read delay in nano-seconds*/
            ulong writeLatency,/*page write delay in nano-seconds*/
            ulong eraseLatency,/*block erase delay in nano-seconds*/
            uint dieNoPerChip,
            uint planeNoPerDie,
            uint blockNoPerPlane,
            uint pageNoPerBlock,
            uint pageCapacity,
            uint blockEraseLimit,
            uint hostBufferSize,
            GarbageCollector.GCPolicyType GCType,
            uint RGAConstant,
            uint WGreedyWindowSize,
            bool dynamicWearLevelingEnabled,
            HostInterface.HostInterfaceType hostInterfaceType,
            InitialStatus initialStatus,
            uint percentageOfValidPages,
            uint validPagesStdDev,
            double overprovisionRatio,
            bool dataCachingEnabled,
            uint dataCacheCapacity,
            bool dftlEnabled,
            uint dftlCapacity,
            IOSchedulingPolicy ioSchedulingPolicy,
            uint windowSize,
            PlaneAllocationSchemeType planeAllocationScheme,
            BlockAllocationSchemeType blockAllocationScheme,
            bool twainBlockManagementEnabled,
            bool copyBackEnabled,
            bool copyBackOddEvenPageConstraint,
            bool multiplaneCMDEnabled,
            bool iBAConstraintForMultiplane,
            bool interleavedCMDEnabled,
            uint readToWritePrioritizationFactor,
            uint writeToErasePrioritizationFactor,
            WorkloadParameterSet[] Workloads,
            bool LoggingEnabled,
            MultistageSchedulingParameters copParameters,
            string RTLogFilePath,//path of response time log file: Used to record each response time of each request
            FlashChip[][] flashChips,
            int seed
            )
        {
            Controller ctrl = new Controller(id, initialStatus,percentageOfValidPages, validPagesStdDev, null, ++seed);

            GarbageCollector gc = new GarbageCollector(id + ".GC", GCType, RGAConstant, WGreedyWindowSize, overprovisionRatio,
                                                        rowCount * chipNoPerRow, pageNoPerBlock * blockNoPerPlane, blockNoPerPlane, pageNoPerBlock,
                                                        dynamicWearLevelingEnabled, copyBackEnabled, copyBackOddEvenPageConstraint, multiplaneCMDEnabled,
                                                        interleavedCMDEnabled, LoggingEnabled,
                                                        twainBlockManagementEnabled, RTLogFilePath, ++seed);
            int numberOfInputStreams = Workloads.Length;
            AddressMappingDomain[] addressMappingDomains = new AddressMappingDomain[numberOfInputStreams];
            for (int i = 0; i < Workloads.Length; i++)
            {
                string[] channelIDs = Workloads[i].ChannelIDs.Split(Separator);
                uint[] channels = new uint[channelIDs.Length];
                for (int j = 0; j < channels.Length; j++)
                    channels[j] = uint.Parse(channelIDs[j]);
                Array.Sort(channels);
                if (channels[channels.Length - 1] >= rowCount)
                    throw new Exception("Bad channel number specified for workload No " + i + "!");

                string[] chiplIDs = Workloads[i].ChipIDs.Split(Separator);
                uint[] chips = new uint[chiplIDs.Length];
                for (int j = 0; j < chips.Length; j++)
                    chips[j] = uint.Parse(chiplIDs[j]);
                Array.Sort(chips);
                if (chips[chips.Length - 1] >= chipNoPerRow)
                    throw new Exception("Bad chip number specified for workload No  " + i + "!");

                string[] dieIDs = Workloads[i].DieIDs.Split(Separator);
                uint[] dies = new uint[dieIDs.Length];
                for (int j = 0; j < dies.Length; j++)
                    dies[j] = uint.Parse(dieIDs[j]);
                Array.Sort(dies);
                if (dies[dies.Length - 1] >= dieNoPerChip)
                    throw new Exception("Bad die number specified for workload No " + i + "!");

                string[] planesIDs = Workloads[i].PlaneIDs.Split(Separator);
                uint[] planes = new uint[planesIDs.Length];
                for (int j = 0; j < planes.Length; j++)
                    planes[j] = uint.Parse(planesIDs[j]);
                Array.Sort(planes);
                if (planes[planes.Length - 1] >= planeNoPerDie)
                    throw new Exception("Bad die number specified for stream No " + i + "!");

                addressMappingDomains[i] = new AddressMappingDomain(planeAllocationScheme, blockAllocationScheme, channels, chips, dies, planes, 
                    blockNoPerPlane, pageNoPerBlock, pageCapacity / FTL.SubPageCapacity, overprovisionRatio, dftlEnabled, dftlCapacity);
            }
            
            FTL ftl = new FTL(id + ".FTL",
                planeAllocationScheme, blockAllocationScheme, ioSchedulingPolicy, false, multiplaneCMDEnabled,
                iBAConstraintForMultiplane, interleavedCMDEnabled, null, GCType,
                gc, addressMappingDomains, flashChips, rowCount,
                chipNoPerRow, dieNoPerChip, planeNoPerDie, blockNoPerPlane, pageNoPerBlock,
                pageCapacity, blockEraseLimit, overprovisionRatio,
                readLatency, writeLatency, eraseLatency, seed++);
            ctrl.AddXObject(ftl);
            ftl.Controller = ctrl;
            gc.FTL = ftl;
            ctrl.FTL = ftl;

            DRAMDataCache dram = new DRAMDataCache(ftl, dataCacheCapacity);
            ftl.DRAM = dram;
            ctrl.DRAM = dram;

            HostInterface HI = null;
            NVMeIODispatcherBase nvmeIOHandler = null;
            IOSchedulerBase ioScheduler = null;
            switch (hostInterfaceType)
            {
                case HostInterface.HostInterfaceType.SATATraceBased:
                    HI = new HostInterface(id + ".HostInterface", hostBufferSize, ftl, ctrl,
                        NotListedMessageGenerationParameterSet.Mode, (Workloads[0] as TraceBasedParameterSet).FilePath, (Workloads[0] as TraceBasedParameterSet).PercentageToBeSimulated,
                        LoggingEnabled, RTLogFilePath,
                        (Workloads[0] as TraceBasedParameterSet).ReplayCount, NotListedMessageGenerationParameterSet.FoldAddress, NotListedMessageGenerationParameterSet.IgnoreUnallocatedReads);

                    ioScheduler = new IOSchedulerSprinkler(id + ".IOScheduler", ftl, readToWritePrioritizationFactor, suspendProgramTime, suspendEraseTime, readTransferCycleTime, writeTransferCycleTime);
                    break;
                case HostInterface.HostInterfaceType.NVMe:
                    {
                        InputStreamBase[] inputStreams = new InputStreamBase[numberOfInputStreams];
                        for (int i = 0; i < numberOfInputStreams; i++)
                        {
                            WorkloadParameterSet streamParameter = Workloads[i];
                            if (streamParameter is TraceBasedParameterSet)
                            {
                                int fileNameStartIndex = 0;
                                if ((streamParameter as TraceBasedParameterSet).FilePath.IndexOf("/") >= 0)
                                    fileNameStartIndex = (streamParameter as TraceBasedParameterSet).FilePath.LastIndexOf("/");
                                else
                                if ((streamParameter as TraceBasedParameterSet).FilePath.IndexOf("\\") >= 0)
                                    fileNameStartIndex = (streamParameter as TraceBasedParameterSet).FilePath.LastIndexOf("\\");
                                inputStreams[i] = new InputStreamTraceBased((streamParameter as TraceBasedParameterSet).FilePath.Remove(0, fileNameStartIndex),
                                    (streamParameter as TraceBasedParameterSet).FilePath, (streamParameter as TraceBasedParameterSet).PriorityClass,
                                    (streamParameter as TraceBasedParameterSet).PercentageToBeSimulated,
                                    (streamParameter as TraceBasedParameterSet).ReplayCount, 10000, addressMappingDomains[i], ++seed);
                            }
                            else
                            {
                                inputStreams[i] = new InputStreamSynthetic("SynthFlow" + i,
                                    (streamParameter as SyntheticParameterSet).PriorityClass,
                                    addressMappingDomains[i],
                                    (streamParameter as SyntheticParameterSet).TotalNumberOfRequests,
                                    (streamParameter as SyntheticParameterSet).AverageRequestInterArrivalTime,
                                    (streamParameter as SyntheticParameterSet).ReadRatio,
                                    (streamParameter as SyntheticParameterSet).AddressDistType,
                                    (streamParameter as SyntheticParameterSet).AddressDistParam1,
                                    (streamParameter as SyntheticParameterSet).AddressDistParam2,
                                    (streamParameter as SyntheticParameterSet).reqSizeDistType,
                                    (streamParameter as SyntheticParameterSet).reqSizeDistParam1,
                                    (streamParameter as SyntheticParameterSet).reqSizeDistParam2, ++seed);
                            }

                        }
                        
                        HI = new HostInterfaceNVMe(id + ".HostInterface", ftl, ctrl, numberOfInputStreams, inputStreams, LoggingEnabled, RTLogFilePath);

                        switch (ioSchedulingPolicy)
                        {
                            case IOSchedulingPolicy.Sprinkler:
                                nvmeIOHandler = new NVMeIODispatcherSimple(id + ".HILogic", ftl, HI, (uint)numberOfInputStreams, windowSize);
                                ioScheduler = new IOSchedulerSprinkler(id + ".IOScheduler", ftl, readToWritePrioritizationFactor, suspendProgramTime, suspendEraseTime, readTransferCycleTime, writeTransferCycleTime);
                                break;
                            case IOSchedulingPolicy.MultiStageAllFair:
                            case IOSchedulingPolicy.MultiStageSoftQoS:
                            case IOSchedulingPolicy.MultiStageMultiplePriorities:
                                nvmeIOHandler = new NVMeIODispatcherRPB(id + ".HILogic", ftl, HI, 1024, inputStreams, rowCount, copParameters.HistoryUpdateInterval);
                                ioScheduler = new IOSchedulerRPB(id + ".IOScheduler", ftl, ioSchedulingPolicy, nvmeIOHandler as NVMeIODispatcherRPB, copParameters.BatchSize, numberOfInputStreams,
                                    readToWritePrioritizationFactor, writeToErasePrioritizationFactor, copParameters.RateControllerEnabled, copParameters.PriorityControllerEnabled, copParameters.ReadWriteBalancerEnabled,
                                    suspendProgramTime, suspendEraseTime, readTransferCycleTime, writeTransferCycleTime);
                                break;
                        }
                    }
                    break;
                case HostInterface.HostInterfaceType.SATASynthetic:
                    /*HI = new HostInterfaceSynthetic(id + ".HostInterface", hostBufferSize, ftl, ctrl, NotListedMessageGenerationParameterSet.Mode,
                        workloadProperties.SyntheticGenerationProperties[0].TotalNumberOfRequests, workloadProperties.SyntheticGenerationProperties[0].AverageRequestInterArrivalTime,
                        workloadProperties.SyntheticGenerationProperties[0].ReadPercentage,
                        workloadProperties.SyntheticGenerationProperties[0].AddressDistType,
                        workloadProperties.SyntheticGenerationProperties[0].AddressDistParam1, workloadProperties.SyntheticGenerationProperties[0].AddressDistParam2,
                        workloadProperties.SyntheticGenerationProperties[0].reqSizeDistType,
                        workloadProperties.SyntheticGenerationProperties[0].reqSizeDistParam1, workloadProperties.SyntheticGenerationProperties[0].reqsizeDistParam2,
                        ++seed, workloadProperties.LoggingEnabled, RTLogFilePath);
                    break;*/
                default:
                    throw new Exception("Unknown HostInterface type!");
            }
            ftl.HostInterface = HI;
            ftl.IOScheduler = ioScheduler;
            ctrl.HostInterface = HI;
            ctrl.AddXObject(HI);
            ctrl.AddXObject(nvmeIOHandler);
            //readDataOutputReadyTime += readTransferCycleTime;
            FCCBase fcc = null;
            switch (ioSchedulingPolicy)
            {
                case IOSchedulingPolicy.Sprinkler:
                    fcc = new FCCMultiChannelBusSimple(id + ".FCC", readTransferCycleTime, writeTransferCycleTime, readCommandAddressCycleCount,
                            writeCommandAddressCycleCount, eraseCommandAddressCycleCount, dummyBusyTime, readDataOutputReadyTime, ALEtoDataStartTransitionTime,
                            WEtoRBTransitionTime, readLatency, writeLatency, eraseLatency, pageCapacity,
                            channelWidth / FTL.ByteSize, ftl, flashChips, ftl.ChannelInfos as BusChannelSprinkler[], HI);
                    break;
                case IOSchedulingPolicy.MultiStageAllFair:
                case IOSchedulingPolicy.MultiStageSoftQoS:
                case IOSchedulingPolicy.MultiStageMultiplePriorities:
                    fcc = new FCCMultiChannelBusRPB(id + ".FCC", readTransferCycleTime, writeTransferCycleTime, readCommandAddressCycleCount,
                            writeCommandAddressCycleCount, eraseCommandAddressCycleCount, dummyBusyTime, readDataOutputReadyTime, ALEtoDataStartTransitionTime,
                            WEtoRBTransitionTime, readLatency, writeLatency, eraseLatency, suspendProgramTime, suspendEraseTime, copParameters.HistoryUpdateInterval, pageCapacity,
                            channelWidth / FTL.ByteSize, ftl, flashChips, ftl.ChannelInfos as BusChannelRPB[], HI);
                    break;
                default:
                    throw new Exception("Unhandled scheduling type!");
            }
            ftl.FCC = fcc;
            ctrl.FCC = fcc;

            return ctrl;
        }
    }
}
