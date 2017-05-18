using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Smulator.BaseComponents;
using Smulator.SSD.Components;
using Smulator.Util;
using System.Xml.Serialization;

namespace Smulator.Disk.SSD.NetworkGenerator
{
    public class NotListedMessageGenerationParameterSet
    {
        public static HostInterface.ReqeustGenerationMode Mode = HostInterface.ReqeustGenerationMode.Normal;
        public static bool FoldAddress = true; //If there is an access to addresses larger than maximum logical storage address, then fold it (i.e. LBA = LBA % LargestLSN)
        public static bool IgnoreUnallocatedReads = false;//If there is a read to an unwritten address, then just ignore it
    }
    [XmlInclude(typeof(TraceBasedParameterSet))]
    [XmlInclude(typeof(SyntheticParameterSet))]
    public class WorkloadParameterSet : BaseParameterSet
    {
        public StreamPriorityClass PriorityClass = StreamPriorityClass.High;
        public string ChannelIDs = "0,1,2,3,4,5,6,7";
        public string ChipIDs = "0,1,2,3,4,5,6,7";
        public string DieIDs = "0,1";
        public string PlaneIDs = "0,1";
    }
    public class TraceBasedParameterSet : WorkloadParameterSet
    {
        public string FilePath = @"E:\Simulation\synthetic.trace";
        public uint PercentageToBeSimulated = 100;//percentage of trace file that must be used in the simulation process. 100 means all, 0 means nothing.
        public uint ReplayCount = 1;//replay the workload from first request
    }
    public class SyntheticParameterSet : WorkloadParameterSet
    {
        public uint AverageRequestInterArrivalTime = 1000000; //nanoseconds
        public double ReadRatio = 0.5;//0-100
        public uint TotalNumberOfRequests = 1000000;
        public InputStreamSynthetic.DistributionType AddressDistType = InputStreamSynthetic.DistributionType.Uniform;
        /* Following two paramters are used for request distribution setup.
         * Uniform Distribution:
         *      reqSizeDistParam1 : minimum request address as a ratio of the whole address space (sector) - if 0 is used, automatically calculated
         *      reqSizeDistParam2 : maximum request address as a ratio of the whole address space (sector) - if 0 is used, automatically calculated
         * Normal Distribution:
         *      reqSizeDistParam1 : address value mean as a ratio of the whole address space (sector)
         *      reqSizeDistParam2 : address value variance as a ratio of the whole address space (sector)
         * HotCold:
         *      reqSizeDistParam1 : ratio of hot area
         *      reqSizeDistParam2 : ratio of traffic directed to the hot area
        */
        public double AddressDistParam1 = 0.1;//a ratio of the whole address space
        public double AddressDistParam2 = 0.2;//a ratio of the whole address space
        public InputStreamSynthetic.DistributionType reqSizeDistType = InputStreamSynthetic.DistributionType.Normal;
        /* Following two paramters are used for request distribution setup.
         * Uniform Distribution:
         *      reqSizeDistParam1 : minimum request size in KB
         *      reqSizeDistParam2 : maximum request size in KB
         * Normal Distribution:
         *      reqSizeDistParam1 : request size mean in KB
         *      reqSizeDistParam2 : request size variance in KB
         * Fixed Distribution:
         *      reqSizeDistParam1 : request size in KB
         *      reqSizeDistParam2 : don't care
        */
        public uint reqSizeDistParam1 = 16;//in Sector
        public uint reqSizeDistParam2 = 8;//in Sector
    }
    public class MultistageSchedulingParameters : BaseParameterSet
    {
        public ulong HistoryUpdateInterval = 200000000;//every 100ms
        public uint BatchSize = 32;
        public bool RateControllerEnabled = true;
        public bool PriorityControllerEnabled = true;
        public bool ReadWriteBalancerEnabled = true;
    }
    public class GCParameterSet : BaseParameterSet
    {
        public GarbageCollector.GCPolicyType GCPolicy = GarbageCollector.GCPolicyType.RGA;
        public uint RGAConstant = 10;  //Random set size for RGA (d-choices) garbage collection introduced by Li-SIGMETRICS13 (VanHoudt-SIGMETRICS13)
    }

    /// <summary>
    /// If you want any parameter to be read from input, then you should move it to the ControllerParameterSet class
    /// </summary>
    public class NotListedControllerParameterSet
    {
        public static GCParameterSet GCProperties = new GCParameterSet();
        public static bool DynamicWearLevelingEnabled = true;
        public static uint HostBufferSize = 100;
        public static bool CopybackRequiresOddEvenConstraint = false; // many NAND products impose odd/even condition on the source and destination addresses of copyback
        public static uint WGreedyWindowSize; //Window size for WindowedGreedy garbage collection introduced by HU-SYSTOR09
        public static bool TwainBlockManagementEnabled = false;
        public static bool CopyBackEnabled = false; //copy back execution is enabled or not
        public static bool MultiplaneCMDEnabled = true; //Multiplane command execution is enabled or not
        public static bool InterleavedCMDEnabled = true; //Interleaved command excution is enabled or not 
        public static bool BAConstraintForMultiplane = true; //Block address should be the same for multiplane command execution
    }
    public class ControllerParameterSet : BaseParameterSet
    {
        public bool LoggingEnabled = false;
        public WorkloadParameterSet[] Workloads = { new SyntheticParameterSet() };
        public HostInterface.HostInterfaceType HostInterfaceType = HostInterface.HostInterfaceType.NVMe;
        public IOSchedulingPolicy SchedulingPolicy = IOSchedulingPolicy.Sprinkler;
        public PlaneAllocationSchemeType PlaneAllocationScheme = PlaneAllocationSchemeType.CDWP;
        public BlockAllocationSchemeType BlockAllocationScheme = BlockAllocationSchemeType.FirstFit;
        public bool DataCachingEnabled = false;
        public uint DataCacheCapacity = 131074;//in bytes
        public bool DFTLEnabled = false;
        public uint DFTLCapacity = 1024;//in bytes
        public double OverprovisionRatio = 0.1;
        public InitialStatus InitialStaus = InitialStatus.SteadyState;
        public uint PercentageOfValidPages = 30;
        public uint PercentageOfValidPagesStdDev = 20;
        public uint SchedulingWindowSize = 32;
        public uint ReadToWritePrioritizationFactor = 20;
        public uint WriteToErasePrioritizationFactor = 3;
        public MultistageSchedulingParameters CopParameters = new MultistageSchedulingParameters();
    }
}
