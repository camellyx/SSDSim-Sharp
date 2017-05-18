using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Smulator.Util;
using Smulator.BaseComponents;
using Smulator.SSD.Components.Cache;

namespace Smulator.SSD.Components
{
    #region Change info
    /// <change>
    /// <author>Arash Tavakkol</author>
    /// <description>All mapping-related functions are moved to AddressMappingModule.</description>
    /// <date>25/11/2016</date>
    /// </change>
    /// 
    /// <change>
    /// <author>Arash Tavakkol</author>
    /// <description>Support for steady-state simulation added.</description>
    /// <date>2014/11/07</date>
    /// </change>
    #endregion
    /// <summary>
    /// <title>Flash Translation Layer</title>
    /// <description> 
    /// </description>
    /// <copyright>Copyright(c)2011</copyright>
    /// <company></company>
    /// <author>Arash Tavakkol ( www.arasht.com )</author>
    /// <version>Version 1.0</version>
    /// <date>2011/12/18</date>
    /// </summary>	
    public enum AdvancedCommandType { TwoPlaneWrite, TwoPlaneRead, Interleave, CopyBack, InterLeaveTwoPlane, None };
    public enum FTLEventType { RequestCompletedByDRAM, StartHandlingRequestsInSaturateMode };
    public enum FlashSchedulingPolicyType { Sprinkler, QoSAware};

    public class FTL : XObject
    {
        public DRAM DRAM;
        public HostInterface HostInterface;
        public Controller Controller;
        public GarbageCollector GarbageCollector;
        public FCCBase FCC;
        public AddressMappingModule AddressMapper;
        public BlockAllocationSchemeType BlockAllocationScheme = BlockAllocationSchemeType.FirstFit;
        public FlashSchedulingPolicyType SchedulingPolicy = FlashSchedulingPolicyType.Sprinkler;
        public bool MultiplaneCMDEnabled = false; //Multiplane Write is enabled or not
        public bool BAConstraintForMultiPlane = true;
        public bool InterleavedCMDEnabled = false; //Interleave command excution is enabled or not
        public AdvancedCommandType currentReadAdvancedCommandType, currentWriteAdvancedCommandType;
        //private uint realTimeIReq = 0;
        public BusChannelBase[] ChannelInfos;
        public FlashChip[] FlashChips;
        public uint CurrentActiveChannel;   //used in Bus
        private RandomGenerator randomChannelGenerator, randomChipGenerator;
        private bool sharedAllocationPoolBus = false;//If the allocation scheme is Dynamic or similar, then the allocation pool should be shared
        public bool dynamicChannelAssignment = false, dynamicWayAssignment = false, dynamicDieAssignment = false, dynamicPlaneAssignment = false;
        public bool planePrioritizedOverDie = false;
        public bool isStaticScheme = false;
        private bool keepBlockUsageHistory = false;
        public bool TB_Enabled = false;//Is Twain Block mangement enabled?

        //Used in initial prepration of the storage space
        private Queue<string>[] InitialValidPagesList = null;
        RandomGenerator randomStreamGenerator, randomAddressGenerator, randomBlockGenerator;

        //This is used to manage race conditions:
        //sometimes a read is issued just before a write on a LPN.
        //sometimes multiple writes are issued on a LPN
        public LNPLinkedList[] currentActiveWriteLPNs = null;

        //MultiChannelBus: In case of fully dynamic allocation, internal requests are inserted into this queue
        public InternalWriteRequestLinkedList WaitingInternalWriteReqs = new InternalWriteRequestLinkedList();

        #region CopParameters
        public RPBResourceAccessTable ResourceAccessTable;
        public IOSchedulerBase IOScheduler;
        #endregion

        #region Paramteres
        #region StatisticalParameters
        public ulong IssuedReadCMD = 0, IssuedInterleaveReadCMD = 0, IssuedMultiplaneReadCMD = 0;
        public ulong IssuedProgramCMD = 0, IssuedInterleaveProgramCMD = 0, IssuedMultiplaneProgramCMD = 0, IssuedInterleaveMultiplaneProgramCMD = 0, IssuedCopyBackProgramCMD = 0;
        public ulong IssuedEraseCMD = 0, IssuedInterleaveEraseCMD = 0, IssuedMultiplaneEraseCMD = 0, IssuedInterleaveMultiplaneEraseCMD = 0;

        //ExternalPageProgramCount is used to record the number of page programs to respond to (external) IO requests
        //GCPageProgramCount is used to record the number of page programs due to data transfer during garbage collection
        public ulong TotalPageProgams = 0, DummyPageProgramsForPreprocess = 0, PageProgramsForWorkload = 0, PageProgramForGC = 0;
        public ulong TotalPageReads = 0, PageReadsForUpdate = 0, PageReadsForWorkload = 0, PageReadsForGC = 0;
        public ulong WastePageCount = 0;
        public ulong TotalBlockErases = 0;


        bool FTLStatisticsUpdated = false;
        double averageFlashChipCMDExecutionPeriodNet = 0, averageFlashChipCMDExecutionPeriodOverlapped = 0, averageFlashChipTransferPeriodNet = 0;
        double averageDieReadExecutionPeriod = 0, averageDieWriteExecutionPeriod = 0, averageDieEraseExecutionPeriod = 0,
            averageDieTransferPeriod = 0;
        double averagePageReadsPerPlane = 0, averagePageProgramsPerPlane = 0, averageBlockErasesPerPlane = 0;
        double averageNumberOfFreePagesPerPlane = 0, averageNumberOfValidPagesPerPlane = 0, averageNumberOfInvalidPagesPerPlane = 0;
        double planePageReadsStdDev = 0, planePageProgramsStdDev = 0, planeBlockErasesStdDev = 0, planeFreePagesStdDev = 0, planeValidPagesStdDev = 0, planeInvalidPagesStdDev = 0;

        ulong totalPlaneReadCount = 0, minPlaneReadCount = ulong.MaxValue, maxPlaneReadCount = 0,
                totalPlaneProgramCount = 0, minPlaneProgramCount = ulong.MaxValue, maxPlaneProgramCount = 0,
                totalPlaneEraseCount = 0, minPlaneEraseCount = ulong.MaxValue, maxPlaneEraseCount = 0,
                totalPlaneValidPagesCount = 0, maxPlaneValidPagesCount = 0, minPlaneValidPagesCount = ulong.MaxValue,
                totalPlaneFreePagesCount = 0, maxPlaneFreePagesCount = 0, minPlaneFreePagesCount = ulong.MaxValue,
                totalPlaneInvalidPagesCount = 0, maxPlaneInvalidPagesCount = 0, minPlaneInvalidPagesCount = ulong.MaxValue;
        #endregion

        #region StructuralParameters
        //Never should be changed
        public static uint SubPageCapacity = 512;
        public static uint ByteSize = 8;
        public static uint ChannelWidthInByte;
        public static uint ChannelWidthInBit;

        public uint TotalChipNo;
        public uint ChannelCount;           //Number of channels
        public uint ChipNoPerChannel;
        public uint DieNoPerChip;           //Number of dies per flash chip
        public uint PlaneNoPerDie;          //indicate how many planes in a die
        public uint BlockNoPerPlane;        //indicate how many blocks in a plane
        public uint PageCapacity = 0;
        public uint SubpageNoPerPage;       //indicate how many subpage in a page
        public uint BlockEraseLimit;        //The maximum number of erase operation for a flash chip
        public double OverprovisionRatio;

        public uint PagesNoPerBlock = 0;
        public uint FlashChipExecutionCapacity;

        public ulong pageReadLatency = 0, pageProgramLatency, eraseLatency = 0;
        #endregion
        #endregion

        #region SetupFunctions
        public FTL(string id,
            PlaneAllocationSchemeType planeAllocationScheme, BlockAllocationSchemeType blockAllocationScheme,
            FlashSchedulingPolicyType schedulingPolicy,
            bool channelLevelAllocationIsPreferred,
            bool multiplaneCMDEnabled, bool iBAConstraintForMultiplane, bool interleavedCMDEnabled, FCCBase FCC,
            GarbageCollector.GCPolicyType gcPolicyType, GarbageCollector garbageCollector,
            AddressMappingDomain[] addressMappingDomains,
            FlashChip[][] flashChips, uint channelCount, uint chipNoPerChannel, uint dieNoPerChip, uint planeNoPerDie, uint blockNoPerPlane,
            uint pageNoPerBlock, uint pageCapacity, uint blockEraseLimit, double overprovisionRatio,
            ulong pageReadLatency, ulong pageProgramLatency, ulong eraseLatency, int seed)
            : base(id)
        {
            MultiplaneCMDEnabled = multiplaneCMDEnabled;
            BAConstraintForMultiPlane = iBAConstraintForMultiplane;
            InterleavedCMDEnabled = interleavedCMDEnabled;
            this.FCC = FCC;
            GarbageCollector = garbageCollector;
            SchedulingPolicy = schedulingPolicy;


            this.currentReadAdvancedCommandType = AdvancedCommandType.None;
            if (MultiplaneCMDEnabled && InterleavedCMDEnabled)
                this.currentReadAdvancedCommandType = AdvancedCommandType.InterLeaveTwoPlane;
            else if (MultiplaneCMDEnabled)
                this.currentReadAdvancedCommandType = AdvancedCommandType.TwoPlaneRead;
            else if (InterleavedCMDEnabled)
                this.currentReadAdvancedCommandType = AdvancedCommandType.Interleave;

            this.currentWriteAdvancedCommandType = AdvancedCommandType.None;
            if (MultiplaneCMDEnabled && InterleavedCMDEnabled)
                this.currentWriteAdvancedCommandType = AdvancedCommandType.InterLeaveTwoPlane;
            else if (MultiplaneCMDEnabled)
                this.currentWriteAdvancedCommandType = AdvancedCommandType.TwoPlaneWrite;
            else if (InterleavedCMDEnabled)
                this.currentWriteAdvancedCommandType = AdvancedCommandType.Interleave;

            #region allocationSchemeParameterSetup
            if (planeAllocationScheme == PlaneAllocationSchemeType.F
                || planeAllocationScheme == PlaneAllocationSchemeType.W
                || planeAllocationScheme == PlaneAllocationSchemeType.WD || planeAllocationScheme == PlaneAllocationSchemeType.WDP
                || planeAllocationScheme == PlaneAllocationSchemeType.WP || planeAllocationScheme == PlaneAllocationSchemeType.WPD
                || planeAllocationScheme == PlaneAllocationSchemeType.D
                || planeAllocationScheme == PlaneAllocationSchemeType.DW || planeAllocationScheme == PlaneAllocationSchemeType.DWP
                || planeAllocationScheme == PlaneAllocationSchemeType.DP || planeAllocationScheme == PlaneAllocationSchemeType.DPW
                || planeAllocationScheme == PlaneAllocationSchemeType.P
                || planeAllocationScheme == PlaneAllocationSchemeType.PW || planeAllocationScheme == PlaneAllocationSchemeType.PWD
                || planeAllocationScheme == PlaneAllocationSchemeType.PD || planeAllocationScheme == PlaneAllocationSchemeType.PDW)
            {
                sharedAllocationPoolBus = true;
                dynamicChannelAssignment = true;
            }
            else
            {
                sharedAllocationPoolBus = false;
                dynamicChannelAssignment = false;
            }

            if (planeAllocationScheme == PlaneAllocationSchemeType.F
                || planeAllocationScheme == PlaneAllocationSchemeType.C
                || planeAllocationScheme == PlaneAllocationSchemeType.CD || planeAllocationScheme == PlaneAllocationSchemeType.CDP
                || planeAllocationScheme == PlaneAllocationSchemeType.CP || planeAllocationScheme == PlaneAllocationSchemeType.CPD
                || planeAllocationScheme == PlaneAllocationSchemeType.D
                || planeAllocationScheme == PlaneAllocationSchemeType.DC || planeAllocationScheme == PlaneAllocationSchemeType.DCP
                || planeAllocationScheme == PlaneAllocationSchemeType.DP || planeAllocationScheme == PlaneAllocationSchemeType.DPC
                || planeAllocationScheme == PlaneAllocationSchemeType.P
                || planeAllocationScheme == PlaneAllocationSchemeType.PC || planeAllocationScheme == PlaneAllocationSchemeType.PCD
                || planeAllocationScheme == PlaneAllocationSchemeType.PD || planeAllocationScheme == PlaneAllocationSchemeType.PDC)
                dynamicWayAssignment = true;
            else dynamicWayAssignment = false;

            if (planeAllocationScheme == PlaneAllocationSchemeType.F
                || planeAllocationScheme == PlaneAllocationSchemeType.C
                || planeAllocationScheme == PlaneAllocationSchemeType.CW || planeAllocationScheme == PlaneAllocationSchemeType.CWP
                || planeAllocationScheme == PlaneAllocationSchemeType.CP || planeAllocationScheme == PlaneAllocationSchemeType.CPW
                || planeAllocationScheme == PlaneAllocationSchemeType.W
                || planeAllocationScheme == PlaneAllocationSchemeType.WC || planeAllocationScheme == PlaneAllocationSchemeType.WCP
                || planeAllocationScheme == PlaneAllocationSchemeType.WP || planeAllocationScheme == PlaneAllocationSchemeType.WPC
                || planeAllocationScheme == PlaneAllocationSchemeType.P
                || planeAllocationScheme == PlaneAllocationSchemeType.PC || planeAllocationScheme == PlaneAllocationSchemeType.PCW
                || planeAllocationScheme == PlaneAllocationSchemeType.PW || planeAllocationScheme == PlaneAllocationSchemeType.PWC)
                dynamicDieAssignment = true;
            else dynamicDieAssignment = false;

            if (planeAllocationScheme == PlaneAllocationSchemeType.F
                || planeAllocationScheme == PlaneAllocationSchemeType.C
                || planeAllocationScheme == PlaneAllocationSchemeType.CW || planeAllocationScheme == PlaneAllocationSchemeType.CWD
                || planeAllocationScheme == PlaneAllocationSchemeType.CD || planeAllocationScheme == PlaneAllocationSchemeType.CDW
                || planeAllocationScheme == PlaneAllocationSchemeType.W
                || planeAllocationScheme == PlaneAllocationSchemeType.WC || planeAllocationScheme == PlaneAllocationSchemeType.WCD
                || planeAllocationScheme == PlaneAllocationSchemeType.WD || planeAllocationScheme == PlaneAllocationSchemeType.WDC
                || planeAllocationScheme == PlaneAllocationSchemeType.D
                || planeAllocationScheme == PlaneAllocationSchemeType.DC || planeAllocationScheme == PlaneAllocationSchemeType.DCW
                || planeAllocationScheme == PlaneAllocationSchemeType.DW || planeAllocationScheme == PlaneAllocationSchemeType.DWC)
                dynamicPlaneAssignment = true;
            else dynamicPlaneAssignment = false;

            isStaticScheme = planeAllocationScheme == PlaneAllocationSchemeType.CWDP || planeAllocationScheme == PlaneAllocationSchemeType.CWPD || planeAllocationScheme == PlaneAllocationSchemeType.CDWP
                            || planeAllocationScheme == PlaneAllocationSchemeType.CDPW || planeAllocationScheme == PlaneAllocationSchemeType.CPWD || planeAllocationScheme == PlaneAllocationSchemeType.CPDW
                            || planeAllocationScheme == PlaneAllocationSchemeType.WCDP || planeAllocationScheme == PlaneAllocationSchemeType.WCPD || planeAllocationScheme == PlaneAllocationSchemeType.WDCP
                            || planeAllocationScheme == PlaneAllocationSchemeType.WDPC || planeAllocationScheme == PlaneAllocationSchemeType.WPCD || planeAllocationScheme == PlaneAllocationSchemeType.WPDC
                            || planeAllocationScheme == PlaneAllocationSchemeType.DCWP || planeAllocationScheme == PlaneAllocationSchemeType.DCPW || planeAllocationScheme == PlaneAllocationSchemeType.DWCP
                            || planeAllocationScheme == PlaneAllocationSchemeType.DWPC || planeAllocationScheme == PlaneAllocationSchemeType.DPCW || planeAllocationScheme == PlaneAllocationSchemeType.DPWC
                            || planeAllocationScheme == PlaneAllocationSchemeType.PCWD || planeAllocationScheme == PlaneAllocationSchemeType.PCDW || planeAllocationScheme == PlaneAllocationSchemeType.PWCD
                            || planeAllocationScheme == PlaneAllocationSchemeType.PWDC || planeAllocationScheme == PlaneAllocationSchemeType.PDCW || planeAllocationScheme == PlaneAllocationSchemeType.PDWC;

            if (planeAllocationScheme == PlaneAllocationSchemeType.CWPD || planeAllocationScheme == PlaneAllocationSchemeType.CPWD || planeAllocationScheme == PlaneAllocationSchemeType.CPDW
                || planeAllocationScheme == PlaneAllocationSchemeType.WCPD || planeAllocationScheme == PlaneAllocationSchemeType.WPCD || planeAllocationScheme == PlaneAllocationSchemeType.WPDC
                || planeAllocationScheme == PlaneAllocationSchemeType.PCWD || planeAllocationScheme == PlaneAllocationSchemeType.PCDW || planeAllocationScheme == PlaneAllocationSchemeType.PWCD
                || planeAllocationScheme == PlaneAllocationSchemeType.PWDC || planeAllocationScheme == PlaneAllocationSchemeType.PDCW || planeAllocationScheme == PlaneAllocationSchemeType.PDWC
                || planeAllocationScheme == PlaneAllocationSchemeType.CP || planeAllocationScheme == PlaneAllocationSchemeType.CPW || planeAllocationScheme == PlaneAllocationSchemeType.CPW
                || planeAllocationScheme == PlaneAllocationSchemeType.WP || planeAllocationScheme == PlaneAllocationSchemeType.WPC || planeAllocationScheme == PlaneAllocationSchemeType.WPD
                || planeAllocationScheme == PlaneAllocationSchemeType.P
                || planeAllocationScheme == PlaneAllocationSchemeType.PC || planeAllocationScheme == PlaneAllocationSchemeType.PCW || planeAllocationScheme == PlaneAllocationSchemeType.PCD
                || planeAllocationScheme == PlaneAllocationSchemeType.PW || planeAllocationScheme == PlaneAllocationSchemeType.PWC || planeAllocationScheme == PlaneAllocationSchemeType.PWD
                || planeAllocationScheme == PlaneAllocationSchemeType.PD || planeAllocationScheme == PlaneAllocationSchemeType.PDC || planeAllocationScheme == PlaneAllocationSchemeType.PDW)
                planePrioritizedOverDie = true;
            #endregion

            DieNoPerChip = dieNoPerChip;
            PlaneNoPerDie = planeNoPerDie;
            BlockNoPerPlane = blockNoPerPlane;
            PagesNoPerBlock = pageNoPerBlock;
            PageCapacity = pageCapacity;
            SubpageNoPerPage = pageCapacity / SubPageCapacity;
            BlockEraseLimit = blockEraseLimit;
            OverprovisionRatio = overprovisionRatio;
            ChannelCount = channelCount;
            ChipNoPerChannel = chipNoPerChannel;
            TotalChipNo = channelCount * chipNoPerChannel;
            InitialValidPagesList = new Queue<string>[TotalChipNo * DieNoPerChip * PlaneNoPerDie];
            for (int i = 0; i < InitialValidPagesList.Length; i++)
                InitialValidPagesList[i] = new Queue<string>();
            randomStreamGenerator = new RandomGenerator(++seed);
            randomAddressGenerator = new RandomGenerator(++seed);
            randomBlockGenerator = new RandomGenerator(++seed);


            this.FlashChipExecutionCapacity = 1;
            if (MultiplaneCMDEnabled && InterleavedCMDEnabled)
                this.FlashChipExecutionCapacity = this.DieNoPerChip * this.PlaneNoPerDie;
            else if (MultiplaneCMDEnabled)
                this.FlashChipExecutionCapacity = this.PlaneNoPerDie;
            else if (InterleavedCMDEnabled)
                this.FlashChipExecutionCapacity = this.DieNoPerChip;
            this.pageReadLatency = pageReadLatency;
            this.pageProgramLatency = pageProgramLatency;
            this.eraseLatency = eraseLatency;

            if (gcPolicyType == Components.GarbageCollector.GCPolicyType.FIFO
                ||
                gcPolicyType == Components.GarbageCollector.GCPolicyType.WindowedGreedy)
                keepBlockUsageHistory = true;
            else
                keepBlockUsageHistory = false;

            FlashChips = new FlashChip[channelCount * chipNoPerChannel];
            switch (schedulingPolicy)
            {
                case FlashSchedulingPolicyType.Sprinkler:
                    ChannelInfos = new BusChannelSprinkler[channelCount];
                    for (uint i = 0; i < channelCount; i++)
                    {
                        if (sharedAllocationPoolBus)
                            ChannelInfos[i] = new BusChannelSprinkler(i, flashChips[i], new InternalReadRequestLinkedList(), this.WaitingInternalWriteReqs, new InternalWriteRequestLinkedList());
                        else
                            ChannelInfos[i] = new BusChannelSprinkler(i, flashChips[i], new InternalReadRequestLinkedList(), new InternalWriteRequestLinkedList(), new InternalWriteRequestLinkedList());
                    }
                    break;
                case FlashSchedulingPolicyType.QoSAware:
                    ChannelInfos = new RPBBusChannel[channelCount];
                    for (uint i = 0; i < channelCount; i++)
                    {
                        ChannelInfos[i] = new RPBBusChannel(i, flashChips[i]);
                    }
                    break;
                default:
                    throw new Exception("Unhandled scheduling policy type!");
            }
            for (uint i = 0; i < channelCount; i++)
                for (uint j = 0; j < chipNoPerChannel; j++)
                {
                    this.FlashChips[flashChips[i][j].OverallChipID] = flashChips[i][j];
                }

            this.AddressMapper = new AddressMappingModule(this, addressMappingDomains,
                flashChips, channelCount, chipNoPerChannel, dieNoPerChip, planeNoPerDie, blockNoPerPlane, pageNoPerBlock, overprovisionRatio);

            currentActiveWriteLPNs = new LNPLinkedList[addressMappingDomains.Length];
            for (int i = 0; i < addressMappingDomains.Length; i++)
                currentActiveWriteLPNs[i] = new LNPLinkedList();



            this.randomChannelGenerator = new RandomGenerator(++seed);
            this.randomChipGenerator = new RandomGenerator(++seed);
        }

        public override void Validate()
        {
            base.Validate();
            if (FCC == null)
                throw new ValidationException(string.Format("FTL ({0}) has no Flash Chip Controller", ID));
            if (GarbageCollector == null)
                throw new ValidationException(string.Format("FTL ({0}) has no Garbage Collector", ID));
            if (Controller == null)
                throw new ValidationException(string.Format("FTL ({0}) has no controller", ID));
            if (SubpageNoPerPage > 31 || SubpageNoPerPage < 1)
                throw new ValidationException(string.Format("Subpage No per page must be chosen between 1 and 31!\n This value is automatically calculated based on page capacity.", ID));
            if (PageCapacity < 1)
                throw new ValidationException("Page size or subpage size is not set for Controller");
            if (SubPageCapacity != 512)
                throw new ValidationException("Implementation assumption violation!\n The subpage capacity (sector size) must be 512 bytes.");
            if (PageCapacity % SubPageCapacity != 0)
                throw new ValidationException("Page capacity must be a multiple of subpage size: " + SubPageCapacity);
        }

        public override void SetupDelegates(bool propagateToChilds)
        {
            base.SetupDelegates(propagateToChilds);
            if (propagateToChilds)
            {
                if (FCC != null)
                    FCC.SetupDelegates(true);
                if (HostInterface != null)
                    HostInterface.SetupDelegates(true);
            }
        }

        public override void ResetDelegates(bool propagateToChilds)
        {
            if (propagateToChilds)
            {
                if (HostInterface != null)
                    HostInterface.ResetDelegates(true);
                if (FCC != null)
                    FCC.ResetDelegates(true);
            }
            base.ResetDelegates(propagateToChilds);
        }

        public override void Start()
        {
            base.Start();
            if (HostInterface.IsSaturatedMode)
            {
                XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(0, this, null, (int)FTLEventType.StartHandlingRequestsInSaturateMode));
            }
        }
        #endregion

        #region SimulationPreprationFunctions
        public uint SetEntryState(ulong lsn, uint size)
        {
            uint temp, state;
            int move;

            temp = ~(0xffffffff << (int)size);
            move = (int)(lsn % this.SubpageNoPerPage);
            state = temp << (int)move;

            return state;
        }
        public void HandleMissingReadAccessTarget(uint streamID, ulong lsn, uint state)
        {
            IntegerPageAddress pageAddress = AddressMapper.CreateMappingEntryForMissingRead(lsn, streamID);
            switch (Controller.InitialSSDStatus)
            {
                case InitialStatus.Empty:
                    AllocateBlock(pageAddress);
                    FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].Blocks[pageAddress.BlockID].LastWrittenPageNo++;
                    uint last_write_page = (uint)FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].Blocks[pageAddress.BlockID].LastWrittenPageNo;

                    //performance
                    if (last_write_page >= (int)(PagesNoPerBlock))
                    {
                        FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].Blocks[pageAddress.BlockID].LastWrittenPageNo = 0;
                        throw new Exception("error! the last written page ID was larger than block size!!");
                    }
                    pageAddress.PageID = last_write_page;
                    FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].Blocks[pageAddress.BlockID].FreePageNo--;
                    FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].BlockInfoAbstract[pageAddress.BlockID].FreePageNo--;
                    FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].FreePagesNo--;
                    break;
                case InitialStatus.SteadyState:
                    Queue<string> validPagesList = InitialValidPagesList[pageAddress.OverallFlashChipID * (DieNoPerChip * PlaneNoPerDie)
                        + pageAddress.DieID * PlaneNoPerDie + pageAddress.PlaneID];
                    if (validPagesList.Count > 0)//we have dummy  valid pages available for the read request
                    {
                        string[] address = validPagesList.Dequeue().Split(HostInterface.Separator);
                        uint blockID = uint.Parse(address[0]);
                        uint pageID = uint.Parse(address[1]);
                        pageAddress.BlockID = blockID;
                        pageAddress.PageID = pageID;
                    }
                    else //There is no dummy valid pages available, so we have to create one
                    {
                        int cntr = 0;
                        uint pageID = 0, blockID = 0;
                        FlashChipPlane targetPlane = FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID];
                        while (cntr < BlockNoPerPlane)
                        {
                            blockID = randomBlockGenerator.UniformUInt(0, BlockNoPerPlane - 1);
                            if (targetPlane.Blocks[blockID].InvalidPageNo > 0)
                                break;
                            cntr++;
                        }
                        if (cntr == BlockNoPerPlane)
                            throw new Exception("It seems that this workload is large for this SSD!");
                        pageID = PagesNoPerBlock - targetPlane.Blocks[blockID].InvalidPageNo;
                        if (targetPlane.Blocks[blockID].Pages[pageID].ValidStatus != FlashChipPage.PG_INVALID)
                            throw new Exception("Incorrect assumption about preprocessing!");

                        pageAddress.BlockID = blockID;
                        pageAddress.PageID = pageID;

                        targetPlane.Blocks[pageAddress.BlockID].InvalidPageNo--;
                        FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].BlockInfoAbstract[pageAddress.BlockID].InvalidPageNo--;
                    }
                    break;
                default:
                    throw new Exception("Unhandled initial status type!");
            }

            FlashChips[pageAddress.OverallFlashChipID].ProgamCount++;
            FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].ProgamCount++;

            DummyPageProgramsForPreprocess++;
            TotalPageProgams++;

            ulong ppn = AddressMapper.ConvertPageAddressToPPN(pageAddress);
            ulong lpn = lsn / SubpageNoPerPage;

            AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn] = ppn;
            AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] = state;
            FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].Blocks[pageAddress.BlockID].Pages[pageAddress.PageID].LPN = lpn;
            FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].Blocks[pageAddress.BlockID].Pages[pageAddress.PageID].ValidStatus = AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn];
            FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].Blocks[pageAddress.BlockID].Pages[pageAddress.PageID].StreamID = (ushort)streamID;
        }
        public void HandleMissingReadAccessTarget(ulong lsn, uint state)
        {
            HandleMissingReadAccessTarget(AddressMappingModule.DefaultStreamID, lsn, state);
        }
        public void ModifyPageTableStateforMissingRead(uint streamID, ulong lpn, uint state)
        {
            AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] = state;
            ulong ppn = AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn];
            IntegerPageAddress targetAddress = AddressMapper.ConvertPPNToPageAddress(ppn);
            FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[targetAddress.PlaneID].Blocks[targetAddress.BlockID].Pages[targetAddress.PageID].ValidStatus 
                = AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn];
        }
        public void ModifyPageTableStateforMissingRead(ulong lpn, uint state)
        {
            ModifyPageTableStateforMissingRead(AddressMappingModule.DefaultStreamID, lpn, state);
        }
        public void MakePageInvalid(uint channelID, uint localChipID, uint dieID, uint planeID, uint blockID, uint pageID)
        {
            uint overallChipID = AddressMapper.GetOveralFlashchipID(channelID, localChipID);
            FlashChipPlane targetPlane = FlashChips[overallChipID].Dies[dieID].Planes[planeID];

            targetPlane.Blocks[blockID].Pages[pageID].StreamID = FlashChipPage.PG_NOSTREAM;
            targetPlane.Blocks[blockID].Pages[pageID].ValidStatus = FlashChipPage.PG_INVALID;
            targetPlane.Blocks[blockID].Pages[pageID].LPN = 0;

            targetPlane.FreePagesNo--;
            targetPlane.Blocks[blockID].FreePageNo--;
            targetPlane.Blocks[blockID].InvalidPageNo++;
            targetPlane.Blocks[blockID].LastWrittenPageNo++;
            FlashChips[overallChipID].Dies[dieID].BlockInfoAbstract[blockID].FreePageNo--;
            FlashChips[overallChipID].Dies[dieID].BlockInfoAbstract[blockID].InvalidPageNo++;

            if (targetPlane.FreePagesNo < GarbageCollector.EmergencyThreshold_PlaneFreePages && !targetPlane.HasGCRequest)
            {
                ChannelInfos[channelID].EmergencyGCRequests.AddLast(new GCJob(channelID, localChipID, dieID, planeID, 0xffffffff,  overallChipID, GCJobType.Emergency));
                GarbageCollector.EmergencyGCRequests++;
                targetPlane.HasGCRequest = true;
                if (SchedulingPolicy == FlashSchedulingPolicyType.QoSAware)
                {
                    ResourceAccessTable.ChannelEntries[channelID].WaitingGCs++;
                    ResourceAccessTable.DieEntries[channelID][localChipID][dieID].WaitingGCs++;
                }
            }
        }
        public void MakePageValid(uint channelID, uint localChipID, uint dieID, uint planeID, uint blockID, uint pageID)
        {
            uint overallChipID = AddressMapper.GetOveralFlashchipID(channelID, localChipID);
            FlashChipPlane targetPlane = FlashChips[overallChipID].Dies[dieID].Planes[planeID];

            InitialValidPagesList[overallChipID * (DieNoPerChip * PlaneNoPerDie) + dieID * PlaneNoPerDie + planeID].Enqueue(blockID + " " + pageID);
            targetPlane.FreePagesNo--;
            targetPlane.Blocks[blockID].FreePageNo--;
            targetPlane.Blocks[blockID].LastWrittenPageNo++;
            FlashChips[localChipID].Dies[dieID].BlockInfoAbstract[blockID].FreePageNo--;

            if (targetPlane.FreePagesNo < GarbageCollector.EmergencyThreshold_PlaneFreePages && !targetPlane.HasGCRequest)
            {
                ChannelInfos[channelID].EmergencyGCRequests.AddLast(new GCJob(channelID, localChipID, dieID, planeID, 0xffffffff,  overallChipID, GCJobType.Emergency));
                GarbageCollector.EmergencyGCRequests++;
                targetPlane.HasGCRequest = true;
                if (SchedulingPolicy == FlashSchedulingPolicyType.QoSAware)
                {
                    ResourceAccessTable.ChannelEntries[channelID].WaitingGCs++;
                    ResourceAccessTable.DieEntries[channelID][localChipID][dieID].WaitingGCs++;
                }
            }
        }
        public void ManageUnallocatedValidPages()
        {
            AddressMapper.PopulateLPNTableForSteadyStateSimulation();
            for (uint channelID = 0; channelID < ChannelCount; channelID++)
                for (uint chipID = 0; chipID < ChipNoPerChannel; chipID++)
                    for (uint dieID = 0; dieID < DieNoPerChip; dieID++)
                        for (uint planeID = 0; planeID < PlaneNoPerDie; planeID++)
                        {
                            Queue<string> validPagesList = InitialValidPagesList[((channelID * ChipNoPerChannel) + chipID) * (DieNoPerChip * PlaneNoPerDie) + dieID * PlaneNoPerDie + planeID];
                            while (validPagesList.Count > 0)
                            {
                                string[] address = validPagesList.Dequeue().Split(HostInterface.Separator);
                                uint blockID = uint.Parse(address[0]);
                                uint pageID = uint.Parse(address[1]);
                                IntegerPageAddress pageAddress = new IntegerPageAddress(channelID, chipID, dieID, planeID, blockID, pageID, AddressMapper.GetOveralFlashchipID(channelID, chipID));
                                ulong ppn = AddressMapper.ConvertPageAddressToPPN(pageAddress);

                                uint streamID = AddressMappingModule.DefaultStreamID;
                                if (HostInterface is HostInterfaceNVMe)
                                    streamID = randomStreamGenerator.UniformUInt(0, FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].AllocatedStreams[
                                        (uint)FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].AllocatedStreams.Length - 1]);
                                ulong lpn = AddressMapper.GetValidLPNForAddress(randomAddressGenerator, streamID, channelID, chipID, dieID, planeID, blockID, pageID);
                                AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn] = ppn;
                                AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] = SetEntryState(lpn * SubpageNoPerPage, SubpageNoPerPage);   //0001


                                FlashChips[pageAddress.OverallFlashChipID].ProgamCount++;
                                FlashChips[pageAddress.OverallFlashChipID].Dies[pageAddress.DieID].Planes[pageAddress.PlaneID].ProgamCount++;
                                DummyPageProgramsForPreprocess++;
                                TotalPageProgams++;

                                FlashChips[pageAddress.OverallFlashChipID].Dies[dieID].Planes[planeID].Blocks[blockID].Pages[pageID].LPN = lpn;
                                FlashChips[pageAddress.OverallFlashChipID].Dies[dieID].Planes[planeID].Blocks[blockID].Pages[pageID].ValidStatus = AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn];
                                FlashChips[pageAddress.OverallFlashChipID].Dies[dieID].Planes[planeID].Blocks[blockID].Pages[pageID].StreamID = (ushort)streamID;
                            }
                        }
            AddressMapper.FreeLPNTables();
        }
        #endregion

        public static uint SizeInSubpages(uint stored)
        {
            uint total = 0, mask = 0x80000000;

            for (uint i = 1; i <= 32; i++)
            {
                if ((stored & mask) != 0)
                    total++;
                stored <<= 1;
            }
            return total;
        }
        /// <summary>
        /// This function handles a new IOReqeust that is generated by HostInterface.
        /// Note: This function is just invoked in normal request generation mode, but not saturated one.
        /// </summary>
        /// <param name="request"></param>
        public void ServiceIORequest()
        {
            switch (SchedulingPolicy)
            {
                case FlashSchedulingPolicyType.Sprinkler:
                    /*Arash: Currently I have commented this part, since we did not checked cache execution correctness 
                     * and also it may increase overall execution time.
                     * if (this.DRAM.CachingEnabled)
                    {
                        this.DRAM.CheckCache(request);
                        DistributeRequestWithCache(request);
                    }
                    else*/
                    BusProcessInternalRequests_SprinklerScheduling();
                    break;
                default:
                    throw new Exception("FTL! Unhandled scheduling policy type!");
            }
        }
        public void OnLPNServiced(uint streamID, ulong lpn)
        {
            currentActiveWriteLPNs[streamID].Remove(lpn);
        }

        #region HelperFunctionsProgramCommand
        public void AllocateBlock(IntegerPageAddress address)
        {
            FlashChipPlane targetPlane = this.FlashChips[address.OverallFlashChipID].Dies[address.DieID].Planes[address.PlaneID];
            if (targetPlane.CurrentActiveBlock.FreePageNo > 0)
            {
                address.BlockID = targetPlane.CurrentActiveBlockID;
                return;
            }

            uint count = 0;

            switch (BlockAllocationScheme)
            {
                case BlockAllocationSchemeType.FirstFit:
                {
                    #region TB
                    if (this.GarbageCollector.TB_Enabled)
                    {
                        bool allok = true;
                        FlashChipDie targetDie = this.FlashChips[address.OverallFlashChipID].Dies[address.DieID];
                        while (count < this.BlockNoPerPlane)
                        {
                            allok = true;
                            for (int planeID = 0; planeID < this.PlaneNoPerDie; planeID++)
                                if (targetDie.Planes[planeID].Blocks[targetDie.CurrentActiveBlockID].FreePageNo == 0)
                                    allok = false;
                            if (allok)
                                break;
                            targetDie.CurrentActiveBlockID = (targetDie.CurrentActiveBlockID + 1) % this.BlockNoPerPlane;
                            count++;
                        }
                        address.BlockID = targetDie.CurrentActiveBlockID;
                        for (int planeID = 0; planeID < this.PlaneNoPerDie; planeID++)
                        {
                            targetPlane = targetDie.Planes[planeID];
                            targetPlane.CurrentActiveBlockID = targetDie.CurrentActiveBlockID;
                            targetPlane.CurrentActiveBlock = targetPlane.Blocks[targetDie.CurrentActiveBlockID];
                            if (keepBlockUsageHistory)
                                if (targetPlane.BlockUsageListHead == null)
                                {
                                    targetPlane.BlockUsageListHead = targetPlane.CurrentActiveBlock;
                                    targetPlane.BlockUsageListTail = targetPlane.BlockUsageListHead;
                                }
                                else
                                {
                                    targetPlane.BlockUsageListTail.Next = targetPlane.CurrentActiveBlock;
                                    targetPlane.BlockUsageListTail = targetPlane.CurrentActiveBlock;
                                }
                        }
                    }
                    #endregion
                    #region ConventionalGC
                    else
                    {
                        while ((targetPlane.Blocks[targetPlane.CurrentActiveBlockID].FreePageNo == 0) && (count < this.BlockNoPerPlane))
                        {
                            targetPlane.CurrentActiveBlockID = (targetPlane.CurrentActiveBlockID + 1) % this.BlockNoPerPlane;
                            count++;
                        }

                        address.BlockID = targetPlane.CurrentActiveBlockID;
                        targetPlane.CurrentActiveBlock = targetPlane.Blocks[targetPlane.CurrentActiveBlockID];
                        if (keepBlockUsageHistory)
                            if (targetPlane.BlockUsageListHead == null)
                            {
                                targetPlane.BlockUsageListHead = targetPlane.CurrentActiveBlock;
                                targetPlane.BlockUsageListTail = targetPlane.BlockUsageListHead;
                            }
                            else
                            {
                                targetPlane.BlockUsageListTail.Next = targetPlane.CurrentActiveBlock;
                                targetPlane.BlockUsageListTail = targetPlane.CurrentActiveBlock;
                            }
                    }
                    #endregion
                    break;
                }
                default:
                    throw new Exception("The" + BlockAllocationScheme.ToString() + " block allocation strategy is not implemented yet!");
            }

            if (count == this.BlockNoPerPlane)
                throw new GeneralException("Error in FindActiveBlock! No active block for address: FlashChip " + address.OverallFlashChipID + ", Die " + address.DieID + ", Plane " + address.PlaneID);
        }
        public ulong AllocatePPNInPlaneForGC(FlashChipPage srcPage, IntegerPageAddress srcAddress, IntegerPageAddress destAddress)
        {
            AllocateBlock(destAddress);

            FlashChipPlane targetPlane = this.FlashChips[destAddress.OverallFlashChipID].Dies[destAddress.DieID].Planes[destAddress.PlaneID];
            targetPlane.FreePagesNo--;

            targetPlane.CurrentActiveBlock.LastWrittenPageNo++;
            targetPlane.CurrentActiveBlock.FreePageNo--;
            FlashChips[destAddress.OverallFlashChipID].Dies[destAddress.DieID].BlockInfoAbstract[targetPlane.CurrentActiveBlock.BlockID].FreePageNo--;
            destAddress.PageID = (uint)targetPlane.CurrentActiveBlock.LastWrittenPageNo;

            targetPlane.Blocks[srcAddress.BlockID].InvalidPageNo++;
            FlashChips[srcAddress.OverallFlashChipID].Dies[srcAddress.DieID].BlockInfoAbstract[srcAddress.BlockID].InvalidPageNo++;

            if (AddressMapper.AddressMappingDomains[srcPage.StreamID].MappingTable.State[srcPage.LPN] != srcPage.ValidStatus)
                throw new Exception("Inconsistent mapping table status!");
            AddressMapper.AddressMappingDomains[srcPage.StreamID].MappingTable.PPN[srcPage.LPN] = AddressMapper.ConvertPageAddressToPPN(destAddress);

            return AddressMapper.AddressMappingDomains[srcPage.StreamID].MappingTable.PPN[srcPage.LPN];
        }
        public void AllocatePPNInPlane(InternalRequest internalReq)
        {
            ulong lpn = internalReq.LPN;
            uint streamID = internalReq.RelatedIORequest.StreamID;
            IntegerPageAddress targetAddress = internalReq.TargetPageAddress;

            AllocateBlock(targetAddress);

            FlashChipPlane targetPlane = this.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[targetAddress.PlaneID];
            targetPlane.FreePagesNo--;
            targetPlane.CurrentActiveBlock.LastWrittenPageNo++;
            targetPlane.CurrentActiveBlock.FreePageNo--;
            FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].BlockInfoAbstract[targetPlane.CurrentActiveBlock.BlockID].FreePageNo--;
            targetAddress.PageID = (uint)targetPlane.CurrentActiveBlock.LastWrittenPageNo;

            FlashChipPage targetPage = targetPlane.CurrentActiveBlock.Pages[targetAddress.PageID];

            if (this.AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] == 0)
            {
                //performance
                if (AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn] != 0)
                {
                    Console.WriteLine("Inconsistency situation in FTL.AllocatePPNInPlane()!!");
                    Console.ReadLine();
                }
                AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn] = AddressMapper.ConvertPageAddressToPPN(targetAddress);
                AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] = internalReq.State;
            }
            else
            {
                IntegerPageAddress location = AddressMapper.ConvertPPNToPageAddress(AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn]);
                FlashChipPage prevPage = this.FlashChips[location.OverallFlashChipID].Dies[location.DieID].Planes[location.PlaneID].Blocks[location.BlockID].Pages[location.PageID];

                //performance
                if (prevPage.LPN != lpn)
                {
                    Console.WriteLine("Inconsistency situation in FTL.AllocatePPNInPlane()!!");
                    Console.ReadLine();
                }

                prevPage.StreamID = FlashChipPage.PG_NOSTREAM;
                prevPage.ValidStatus = FlashChipPage.PG_INVALID;
                prevPage.LPN = 0;
                FlashChips[location.OverallFlashChipID].Dies[location.DieID].Planes[location.PlaneID].Blocks[location.BlockID].InvalidPageNo++;
                FlashChips[location.OverallFlashChipID].Dies[location.DieID].BlockInfoAbstract[location.BlockID].InvalidPageNo++;
                if (FlashChips[location.OverallFlashChipID].Dies[location.DieID].Planes[location.PlaneID].Blocks[location.BlockID].InvalidPageNo == this.PagesNoPerBlock)
                    if ((GarbageCollector.Type == Components.GarbageCollector.GCPolicyType.Greedy) && this.GarbageCollector.UseDirectErase)
                        FlashChips[location.OverallFlashChipID].Dies[location.DieID].Planes[location.PlaneID].DirectEraseNodes.AddLast(location.BlockID);


                AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn] = AddressMapper.ConvertPageAddressToPPN(targetAddress);
                AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] = AddressMapper.AddressMappingDomains[streamID].MappingTable.State[internalReq.LPN] | internalReq.State;
            }

            internalReq.PPN = AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn];

            targetPage.LPN = internalReq.LPN;
            targetPage.ValidStatus = AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn];
            targetPage.StreamID = (ushort)internalReq.RelatedIORequest.StreamID;

            if (targetPlane.FreePagesNo < GarbageCollector.EmergencyThreshold_PlaneFreePages && !targetPlane.HasGCRequest)
            {
                ChannelInfos[targetAddress.ChannelID].EmergencyGCRequests.AddLast(new GCJob(targetAddress.ChannelID, targetAddress.LocalFlashChipID, targetAddress.DieID, targetAddress.PlaneID, 0xffffffff, targetAddress.OverallFlashChipID, GCJobType.Emergency));
                GarbageCollector.EmergencyGCRequests++;
                targetPlane.HasGCRequest = true;
                if (SchedulingPolicy == FlashSchedulingPolicyType.QoSAware)
                {
                    GarbageCollector.ChannelInvokeGCCop(targetAddress.ChannelID);
                    ResourceAccessTable.ChannelEntries[targetAddress.ChannelID].WaitingGCs++;
                    ResourceAccessTable.DieEntries[targetAddress.ChannelID][targetAddress.LocalFlashChipID][targetAddress.DieID].WaitingGCs++;
                }
            }
        }
        public void AllocatePPNandExecuteSimpleWrite(InternalWriteRequestLinkedList sourceReqList, InternalWriteRequest internalReq)
        {
            IntegerPageAddress targetAddress = internalReq.TargetPageAddress;

            if (dynamicDieAssignment)
            {
                if (targetAddress.DieID < uint.MaxValue)
                    throw new Exception("Unsupported condition!");
                targetAddress.DieID = this.FlashChips[targetAddress.OverallFlashChipID].CurrentActiveDieID;
                this.FlashChips[targetAddress.OverallFlashChipID].CurrentActiveDieID = (targetAddress.DieID + 1) % this.DieNoPerChip;
            }

            if (dynamicPlaneAssignment)
            {
                if (targetAddress.PlaneID < uint.MaxValue)
                    throw new Exception("Unsupported condition!");
                targetAddress.PlaneID = this.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].CurrentActivePlaneID;
                this.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].CurrentActivePlaneID = (targetAddress.PlaneID + 1) % this.PlaneNoPerDie;
            }
   
            AllocatePPNInPlane(internalReq);

            internalReq.ExecutionType = InternalRequestExecutionType.Simple;
            sourceReqList.Remove(internalReq.RelatedNodeInList);
            FCC.SendSimpleCommandToChip(internalReq);
        }
        #region HelperFunctionsMultiplaneProgramCMD
        public bool FindLevelPage(InternalRequest subA, InternalRequest subB)
        {
            IntegerPageAddress adA = new IntegerPageAddress(subA.TargetPageAddress);
            IntegerPageAddress adB = new IntegerPageAddress(subB.TargetPageAddress);

            FlashChip targetChip = this.FlashChips[adA.OverallFlashChipID];
            uint oldActivePlane = targetChip.Dies[adA.DieID].CurrentActivePlaneID;


            if (dynamicPlaneAssignment)
            {
                adA.PlaneID = targetChip.Dies[adA.DieID].CurrentActivePlaneID;
                if (adA.PlaneID % 2 == 0)
                {
                    adB.PlaneID = adA.PlaneID + 1;
                    targetChip.Dies[adA.DieID].CurrentActivePlaneID = (targetChip.Dies[adA.DieID].CurrentActivePlaneID + 2) % this.PlaneNoPerDie;
                }
                else
                {
                    adA.PlaneID = (adA.PlaneID + 1) % this.PlaneNoPerDie;
                    adB.PlaneID = adA.PlaneID + 1;
                    targetChip.Dies[adA.DieID].CurrentActivePlaneID = (targetChip.Dies[adA.DieID].CurrentActivePlaneID + 3) % this.PlaneNoPerDie;
                }
            }

            AllocateBlock(adA);
            AllocateBlock(adB);

            #region adA.BlockID=adB.BlockID
            if ((adA.BlockID == adB.BlockID) || !BAConstraintForMultiPlane)
            {
                adA.PageID = (uint)targetChip.Dies[adA.DieID].Planes[adA.PlaneID].Blocks[adA.BlockID].LastWrittenPageNo + 1;
                adB.PageID = (uint)targetChip.Dies[adB.DieID].Planes[adB.PlaneID].Blocks[adB.BlockID].LastWrittenPageNo + 1;
                if (adA.PageID == adB.PageID)
                {
                    subA.TargetPageAddress = adA;
                    subB.TargetPageAddress = adB;
                    ModifyPageState(subA);
                    ModifyPageState(subB);
                    return true;
                }
                else
                {
                    targetChip.Dies[adA.DieID].CurrentActivePlaneID = oldActivePlane;
                    return false;
                }
            }//if (adA.BlockID == adB.BlockID)
            #endregion
            else
            {
                adA.PageID = (uint)targetChip.Dies[adA.DieID].Planes[adA.PlaneID].Blocks[adA.BlockID].LastWrittenPageNo + 1;
                adB.PageID = (uint)targetChip.Dies[adB.DieID].Planes[adB.PlaneID].Blocks[adB.BlockID].LastWrittenPageNo + 1;

                #region adA.PageID<adB.PageID 
                if (adA.PageID < adB.PageID)
                {
                    targetChip.Dies[adA.DieID].CurrentActivePlaneID = oldActivePlane;
                    return false;
                }//if (adA.PageID < adB.PageID)
                #endregion
                #region adA.PageID>=adB.PageID
                else
                {
                    if ((adA.PageID == adB.PageID) && (adA.PageID == 0))
                    {
                        if ((targetChip.Dies[adA.DieID].Planes[adA.PlaneID].Blocks[adA.BlockID].Pages[adA.PageID].ValidStatus == FlashChipPage.PG_FREE)
                            && (targetChip.Dies[adA.DieID].Planes[adB.PlaneID].Blocks[adA.BlockID].Pages[adA.PageID].ValidStatus == FlashChipPage.PG_FREE))
                        {
                            adB.BlockID = adA.BlockID;
                            adB.PageID = adA.PageID;
                            subA.TargetPageAddress = adA;
                            subB.TargetPageAddress = adB;
                            ModifyPageState(subA);
                            ModifyPageState(subB);
                            return true;
                        }
                        else if ((targetChip.Dies[adA.DieID].Planes[adA.PlaneID].Blocks[adB.BlockID].Pages[adA.PageID].ValidStatus == FlashChipPage.PG_FREE)
                            && (targetChip.Dies[adA.DieID].Planes[adB.PlaneID].Blocks[adB.BlockID].Pages[adA.PageID].ValidStatus == FlashChipPage.PG_FREE))
                        {
                            adA.BlockID = adB.BlockID;
                            adB.PageID = adA.PageID;
                            subA.TargetPageAddress = adA;
                            subB.TargetPageAddress = adB;
                            ModifyPageState(subA);
                            ModifyPageState(subB);
                            return true;
                        }
                        else
                        {
                            targetChip.Dies[adA.DieID].CurrentActivePlaneID = oldActivePlane;
                            return false;
                        }
                    }
                    else
                    {
                        targetChip.Dies[adA.DieID].CurrentActivePlaneID = oldActivePlane;
                        return false;
                    }
                }
                #endregion
            }//if (adA.BlockID == adB.BlockID) else

        }
        public bool FindLevelPageStrict(FlashChip targetChip, InternalRequest sub0, InternalRequest sub1)
        {
            IntegerPageAddress ad0 = sub0.TargetPageAddress;
            IntegerPageAddress ad1 = new IntegerPageAddress(ad0);
            uint oldActivePlane = targetChip.Dies[ad1.DieID].CurrentActivePlaneID;

            #region DynamicPlaneAllocation
            if (dynamicPlaneAssignment)
            {
                for (int planeCntr = 0; planeCntr < this.PlaneNoPerDie; planeCntr++)
                {
                    ad1.PlaneID = targetChip.Dies[ad1.DieID].CurrentActivePlaneID;
                    if (targetChip.Dies[ad1.DieID].Planes[ad1.PlaneID].CommandAssigned)
                    {
                        this.FlashChips[ad1.OverallFlashChipID].Dies[ad1.DieID].CurrentActivePlaneID = (ad1.PlaneID + 1) % this.PlaneNoPerDie;
                        continue;
                    }

                    AllocateBlock(ad1);

                    if ((ad1.BlockID == ad0.BlockID) || !BAConstraintForMultiPlane)
                    {
                        ad1.PageID = (uint)targetChip.Dies[ad1.DieID].Planes[ad1.PlaneID].Blocks[ad1.BlockID].LastWrittenPageNo + 1;
                        if (ad1.PageID == ad0.PageID)
                        {
                            sub1.TargetPageAddress = ad1;
                            ModifyPageState(sub1);
                            targetChip.Dies[ad1.DieID].CurrentActivePlaneID = (ad1.PlaneID + 1) % this.PlaneNoPerDie;
                            return true;
                        }
                        else if (ad1.PageID < ad0.PageID)
                        {
                            ad1.PageID = ad0.PageID;
                        }
                    }//if (ad1.BlockID == ad0.BlockID)
                    this.FlashChips[ad1.OverallFlashChipID].Dies[ad1.DieID].CurrentActivePlaneID = (ad1.PlaneID + 1) % this.PlaneNoPerDie;
                }

                this.FlashChips[ad1.OverallFlashChipID].Dies[ad1.DieID].CurrentActivePlaneID = oldActivePlane;
                return false;
            }
            #endregion
            #region StaticPlaneAllocation
            else
            {
                ad1.PlaneID = sub1.TargetPageAddress.PlaneID;
                AllocateBlock(ad1);

                if ((ad1.BlockID == ad0.BlockID) || !BAConstraintForMultiPlane)
                {
                    ad1.PageID = (uint)targetChip.Dies[ad1.DieID].Planes[ad1.PlaneID].Blocks[ad1.BlockID].LastWrittenPageNo + 1;

                    if (ad1.PageID > ad0.PageID)
                        return false;
                    else if (ad1.PageID < ad0.PageID)
                    {
                        return false;
                    }
                    else
                    {
                        sub1.TargetPageAddress = ad1;
                        ModifyPageState(sub1);
                        return true;
                    }
                }
                else
                    return false;
            }
            #endregion
        }
        public void ModifyPageState(InternalRequest internalReq)
        {
            uint full_page = ~(0xffffffff << (int)this.SubpageNoPerPage);
            ulong lpn = internalReq.LPN;
            IntegerPageAddress targetAddress = internalReq.TargetPageAddress;
            uint streamID = internalReq.RelatedIORequest.StreamID;

            FlashChipBlock targetBlock = this.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[targetAddress.PlaneID].Blocks[targetAddress.BlockID];
            targetBlock.LastWrittenPageNo = (int)targetAddress.PageID;
            targetBlock.FreePageNo--;

            FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].BlockInfoAbstract[targetBlock.BlockID].FreePageNo--;
            FlashChipPage targetPage = targetBlock.Pages[targetAddress.PageID];

            if (AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] == 0)  /*this is the first logical page*/
            {
                AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn] = AddressMapper.ConvertPageAddressToPPN(targetAddress);
                AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] = internalReq.State;
            }
            else
            {
                IntegerPageAddress location = AddressMapper.ConvertPPNToPageAddress(AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn]);
                FlashChipPage prevPage = this.FlashChips[location.OverallFlashChipID].Dies[location.DieID].Planes[location.PlaneID].Blocks[location.BlockID].Pages[location.PageID];

                prevPage.StreamID = FlashChipPage.PG_NOSTREAM;
                prevPage.ValidStatus = FlashChipPage.PG_INVALID;
                prevPage.LPN = 0;
                this.FlashChips[location.OverallFlashChipID].Dies[location.DieID].Planes[location.PlaneID].Blocks[location.BlockID].InvalidPageNo++;
                FlashChips[location.OverallFlashChipID].Dies[location.DieID].BlockInfoAbstract[location.BlockID].InvalidPageNo++;

                if (this.FlashChips[location.OverallFlashChipID].Dies[location.DieID].Planes[location.PlaneID].Blocks[location.BlockID].InvalidPageNo == this.PagesNoPerBlock)
                    if ((GarbageCollector.Type == Components.GarbageCollector.GCPolicyType.Greedy) && this.GarbageCollector.UseDirectErase)
                        this.FlashChips[location.OverallFlashChipID].Dies[location.DieID].Planes[location.PlaneID].DirectEraseNodes.AddLast(location.BlockID);


                AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn] = AddressMapper.ConvertPageAddressToPPN(targetAddress);
                AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] = AddressMapper.AddressMappingDomains[streamID].MappingTable.State[lpn] | internalReq.State;
            }

            internalReq.PPN = AddressMapper.AddressMappingDomains[streamID].MappingTable.PPN[lpn];

            this.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[targetAddress.PlaneID].FreePagesNo--;
            this.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[targetAddress.PlaneID].CommandAssigned = true;
            targetPage.LPN = lpn;
            targetPage.ValidStatus = internalReq.State;
            targetPage.StreamID = (ushort)internalReq.RelatedIORequest.StreamID;

            if (FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[targetAddress.PlaneID].FreePagesNo < GarbageCollector.EmergencyThreshold_PlaneFreePages
                && !FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[targetAddress.PlaneID].HasGCRequest)
            {
                ChannelInfos[targetAddress.ChannelID].EmergencyGCRequests.AddLast(new GCJob(targetAddress.ChannelID, targetAddress.LocalFlashChipID, targetAddress.DieID, targetAddress.PlaneID, 0xffffffff, targetAddress.OverallFlashChipID, GCJobType.Emergency));
                GarbageCollector.EmergencyGCRequests++;
                FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[targetAddress.PlaneID].HasGCRequest = true;
                if (SchedulingPolicy == FlashSchedulingPolicyType.QoSAware)
                {
                    GarbageCollector.ChannelInvokeGCCop(targetAddress.ChannelID);
                    ResourceAccessTable.ChannelEntries[targetAddress.ChannelID].WaitingGCs++;
                    ResourceAccessTable.DieEntries[targetAddress.ChannelID][targetAddress.LocalFlashChipID][targetAddress.DieID].WaitingGCs++;
                }
            }
        }
        #endregion
        #endregion

        #region Sprinkler_Scheduling
        /// <summary>
        /// Finds possible two-plane internal requests, when underlying communication between flash chips
        /// and controller is Multi-Channel Bus
        /// </summary>
        /// <param name="sourceReadReqList">The source list of read requests, to search for candidates of advanced command execution</param>
        /// <param name="advCommand">Advanced command that should be performed</param>
        bool PerformAdvancedReadCommand(InternalReadRequestLinkedList sourceReadReqList)
        {
            /*If allocation scheme determines plane address before die address, then in the advanced command execution
            process, we should priotize multiplane command allocation over multidie allocation*/
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
        bool ServiceReadCommands_SprinklerScheduling(uint channelID)
        {
            BusChannelSprinkler targetChannel = ChannelInfos[channelID] as BusChannelSprinkler;
            InternalReadRequestLinkedList sourceReadReqs = targetChannel.WaitingInternalReadReqs;
            //Check is there any request to be serviced?
            if (sourceReadReqs.Count == 0)
                return false;

            //To preserve timing priority between reads and writes, we first check for the existence of write requests issued sooner than reads
            if (targetChannel.WaitingInternalWriteReqs.Count > 0)
                if (targetChannel.WaitingInternalWriteReqs.First.Value.IssueTime < sourceReadReqs.First.Value.IssueTime
                    && targetChannel.WaitingInternalWriteReqs.First.Value.UpdateRead == null)
                    return false;
            

            if (this.currentReadAdvancedCommandType != AdvancedCommandType.None && sourceReadReqs.Count > 1)
                if (PerformAdvancedReadCommand(sourceReadReqs))
                    return true;
            
            /*Normal command execution (i.e. no advanced command supported)*/
            for (var readReq = sourceReadReqs.First; readReq != null; readReq = readReq.Next)
                if (readReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                {
                    sourceReadReqs.Remove(readReq);
                    FCC.SendSimpleCommandToChip(readReq.Value);
                    return true;
                }
            return false;
        }
        /// <summary>
        /// Allocates location to a write internal requests according to the current allocation scheme.
        /// It also adds the request to the corresponding service queue
        /// </summary>
        /// <param name="internalReq">Target internal request which needs allocation</param>
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
            /*     planeAllocationScheme == PlaneAllocationSchemeType.CWD
                || planeAllocationScheme == PlaneAllocationSchemeType.CD || planeAllocationScheme == PlaneAllocationSchemeType.CDW
                || planeAllocationScheme == PlaneAllocationSchemeType.WCD
                || planeAllocationScheme == PlaneAllocationSchemeType.WD || planeAllocationScheme == PlaneAllocationSchemeType.WDC
                || planeAllocationScheme == PlaneAllocationSchemeType.D
                || planeAllocationScheme == PlaneAllocationSchemeType.DC || planeAllocationScheme == PlaneAllocationSchemeType.DCW
                || planeAllocationScheme == PlaneAllocationSchemeType.DW || planeAllocationScheme == PlaneAllocationSchemeType.DWC*/
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
            /*     planeAllocationScheme == PlaneAllocationSchemeType.CWP
                || planeAllocationScheme == PlaneAllocationSchemeType.CP || planeAllocationScheme == PlaneAllocationSchemeType.CPW
                || planeAllocationScheme == PlaneAllocationSchemeType.WCP
                || planeAllocationScheme == PlaneAllocationSchemeType.WP || planeAllocationScheme == PlaneAllocationSchemeType.WPC
                || planeAllocationScheme == PlaneAllocationSchemeType.P
                || planeAllocationScheme == PlaneAllocationSchemeType.PC || planeAllocationScheme == PlaneAllocationSchemeType.PCW
                || planeAllocationScheme == PlaneAllocationSchemeType.PW || planeAllocationScheme == PlaneAllocationSchemeType.PWC*/
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
            /*PlaneAllocationSchemeType.F
            || PlaneAllocationSchemeType.C  || PlaneAllocationSchemeType.W
            || PlaneAllocationSchemeType.CW || PlaneAllocationSchemeType.WC*/
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
        public void AllocatePlaneToWriteTransaction_Sprinkler(InternalWriteRequest internalReq)
        {
            if (AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.State[internalReq.LPN] != 0)
            {
                if ((internalReq.State & AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.State[internalReq.LPN])
                    != AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.State[internalReq.LPN])
                {
                    TotalPageReads++;
                    PageReadsForWorkload++;
                    PageReadsForUpdate++;
                    InternalReadRequest update = new InternalReadRequest();
                    AddressMapper.ConvertPPNToPageAddress(AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.PPN[internalReq.LPN], update.TargetPageAddress);
                    update.TargetFlashChip = FlashChips[update.TargetPageAddress.OverallFlashChipID];
                    update.LPN = internalReq.LPN;
                    update.PPN = AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.PPN[internalReq.LPN];
                    update.State = ((AddressMapper.AddressMappingDomains[internalReq.RelatedIORequest.StreamID].MappingTable.State[internalReq.LPN] ^ internalReq.State) & 0x7fffffff);
                    update.SizeInSubpages = SizeInSubpages(update.State);
                    update.SizeInByte = update.SizeInSubpages * FTL.SubPageCapacity;
                    update.BodyTransferCycles = update.SizeInByte / ChannelWidthInByte;
                    update.Type = InternalRequestType.Read;
                    update.IsUpdate = true;
                    update.RelatedWrite = internalReq;
                    update.RelatedIORequest = internalReq.RelatedIORequest;
                    internalReq.UpdateRead = update;
                    internalReq.State = (internalReq.State | update.State);
                    internalReq.SizeInSubpages = SizeInSubpages(internalReq.State);
                }
            }

            AddressMapper.AllocatePlaneForWrite(internalReq.RelatedIORequest.StreamID, internalReq);

            uint queueID = internalReq.TargetPageAddress.ChannelID;
            if (sharedAllocationPoolBus)
                queueID = 0;
            internalReq.RelatedNodeInList = (ChannelInfos[queueID] as BusChannelSprinkler).WaitingInternalWriteReqs.AddLast(internalReq);
            if (internalReq.UpdateRead != null)// && !(CopyBackCommandEnabled && isStaticScheme))
                                               /*From original code I figured out that, if advanced commands are enabled, we handle
                                                * update read and its related write request, separately.
                                                * However, in case of normal command execution, the original code fails to operate correctly.
                                                * My decision is this: update reads are always handled separately.*/
                internalReq.UpdateRead.RelatedNodeInList = (ChannelInfos[internalReq.UpdateRead.TargetPageAddress.ChannelID] as BusChannelSprinkler).WaitingInternalReadReqs.AddLast(internalReq.UpdateRead);
        }
        void ServiceWriteCommands_SprinklerScheduling(uint channelID)
        {
            BusChannelSprinkler targetChannel = (ChannelInfos[channelID] as BusChannelSprinkler);
            InternalWriteRequestLinkedList sourceWriteReqList = targetChannel.WaitingInternalWriteReqs;
            //Check is there any request to be serviced?
            if (sourceWriteReqList.Count == 0)
                return;

            IntegerPageAddress targetAddress = new IntegerPageAddress(channelID, 0, 0, 0, 0, 0, 0);
            FlashChip targetChip = null;

            #region StaticAllocationScheme
            if (isStaticScheme || (!dynamicWayAssignment && !dynamicChannelAssignment))
            {
                if (InterleavedCMDEnabled || MultiplaneCMDEnabled)
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
                                if (dynamicDieAssignment)
                                {
                                    writeReq.Value.TargetPageAddress.DieID = writeReq.Value.TargetFlashChip.CurrentActiveDieID;
                                    writeReq.Value.TargetFlashChip.CurrentActiveDieID = (writeReq.Value.TargetFlashChip.CurrentActiveDieID + 1) % this.DieNoPerChip;
                                }
                                if (dynamicPlaneAssignment)
                                {
                                    writeReq.Value.TargetPageAddress.PlaneID = writeReq.Value.TargetFlashChip.Dies[writeReq.Value.TargetPageAddress.DieID].CurrentActivePlaneID;
                                    writeReq.Value.TargetFlashChip.Dies[writeReq.Value.TargetPageAddress.DieID].CurrentActivePlaneID = (writeReq.Value.TargetFlashChip.Dies[writeReq.Value.TargetPageAddress.DieID].CurrentActivePlaneID + 1) % this.PlaneNoPerDie;
                                }
                                AllocatePPNInPlane(writeReq.Value);
                                sourceWriteReqList.Remove(writeReq);
                                FCC.SendSimpleCommandToChip(writeReq.Value);
                                return;
                            }
                            /*else if (CopyBackCommandEnabled)
                            {
                                if (dynamicDieAssignment)
                                {
                                    writeReq.Value.TargetPageAddress.DieID = writeReq.Value.TargetFlashChip.CurrentActiveDie;
                                    writeReq.Value.TargetFlashChip.CurrentActiveDie = (writeReq.Value.TargetFlashChip.CurrentActiveDie + 1) % this.DieNoPerChip;
                                }
                                if (dynamicPlaneAssignment)
                                {
                                    writeReq.Value.TargetPageAddress.PlaneID = writeReq.Value.TargetFlashChip.Dies[writeReq.Value.TargetPageAddress.DieID].CurrentActivePlane;
                                    writeReq.Value.TargetFlashChip.Dies[writeReq.Value.TargetPageAddress.DieID].CurrentActivePlane = (writeReq.Value.TargetFlashChip.Dies[writeReq.Value.TargetPageAddress.DieID].CurrentActivePlane + 1) % this.PlaneNoPerDie;
                                }
                                AllocatePPNInPlane(writeReq.Value);
                                sourceWriteReqList.Remove(writeReq);
                                PerformSimpleCopyBackWrite(writeReq.Value);
                                return;
                            }*/
                        }
                }
            }//if (isStaticScheme || !dynamicWayAssignment)
            #endregion
            #region DynamicChannelAssignement
            else if (!dynamicWayAssignment && dynamicChannelAssignment)//just dynamic channel allocation
            {
                if (InterleavedCMDEnabled || MultiplaneCMDEnabled)
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
                                if (dynamicDieAssignment)
                                {
                                    targetAddress.DieID = targetChip.CurrentActiveDieID;
                                    targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % this.DieNoPerChip;
                                }
                                if (dynamicPlaneAssignment)
                                {
                                    targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                    targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID = (targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID + 1) % this.PlaneNoPerDie;
                                }
                                AllocatePPNInPlane(writeReq.Value);
                                sourceWriteReqList.Remove(writeReq);
                                FCC.SendSimpleCommandToChip(writeReq.Value);
                                return;
                            }
                            /*else if (CopyBackCommandEnabled)
                            {
                                if (dynamicDieAssignment)
                                {
                                    targetAddress.DieID = targetChip.CurrentActiveDie;
                                    targetChip.CurrentActiveDie = (targetChip.CurrentActiveDie + 1) % this.DieNoPerChip;
                                }
                                if (dynamicPlaneAssignment)
                                {
                                    targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlane;
                                    targetChip.Dies[targetAddress.DieID].CurrentActivePlane = (targetChip.Dies[targetAddress.DieID].CurrentActivePlane + 1) % this.PlaneNoPerDie;
                                }
                                AllocatePPNInPlane(writeReq.Value);
                                sourceWriteReqList.Remove(writeReq);
                                PerformSimpleCopyBackWrite(writeReq.Value);
                                return;
                            }*/
                        }
                    }
                }
            }
            #endregion
            #region DynamicWayAssignement
            else//dynamic way allocation
            {
                if (InterleavedCMDEnabled || MultiplaneCMDEnabled)
                {
                    for (int chipCntr = 0; chipCntr < this.ChipNoPerChannel; chipCntr++)
                    {
                        targetChip = targetChannel.FlashChips[targetChannel.CurrentActiveChip];
                        if (targetChip.Status == FlashChipStatus.Idle)
                        {
                            PerformAdvancedProgramCMDForFlashChip(targetChip, sourceWriteReqList);
                            targetChannel.CurrentActiveChip = (targetChannel.CurrentActiveChip + 1) % this.ChipNoPerChannel;
                            if (targetChannel.Status == BusChannelStatus.Busy)
                                return;
                        }
                        targetChannel.CurrentActiveChip = (targetChannel.CurrentActiveChip + 1) % this.ChipNoPerChannel;
                    }
                }//if (InterleaveCommandEnabled || MPWCommandEnabled)
                else
                {
                    for (int chipCntr = 0; chipCntr < this.ChipNoPerChannel; chipCntr++)
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
                                    if (dynamicDieAssignment)
                                    {
                                        targetAddress.DieID = targetChip.CurrentActiveDieID;
                                        targetChip.CurrentActiveDieID = (targetChip.CurrentActiveDieID + 1) % this.DieNoPerChip;
                                    }
                                    if (dynamicPlaneAssignment)
                                    {
                                        targetAddress.PlaneID = targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID;
                                        targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID =
                                            (targetChip.Dies[targetAddress.DieID].CurrentActivePlaneID + 1) % this.PlaneNoPerDie;
                                    }

                                    AllocatePPNInPlane(writeReq.Value);
                                    FCC.SendSimpleCommandToChip(writeReq.Value);
                                    targetChannel.CurrentActiveChip = (targetChannel.CurrentActiveChip + 1) % this.ChipNoPerChannel;
                                    return;
                                }
                        targetChannel.CurrentActiveChip = (targetChannel.CurrentActiveChip + 1) % this.ChipNoPerChannel;
                    }
                }
            }
            #endregion
        }
        #region Event Handlers Sprinkler Queueing
        public override void ProcessXEvent(XEvent e)
        {
            switch ((FTLEventType)e.Type)
            {
                case FTLEventType.StartHandlingRequestsInSaturateMode:
                    switch (SchedulingPolicy)
                    {
                        case FlashSchedulingPolicyType.Sprinkler:
                            this.HostInterface.SetupPhaseFinished();
                            uint randomRow = randomChannelGenerator.GetInteger(this.ChannelCount - 1);
                            for (uint channelCntr = 0; channelCntr < this.ChannelCount; channelCntr++)
                            {
                                uint channelID = (randomRow + channelCntr) % this.ChannelCount;

                                if (!ServiceReadCommands_SprinklerScheduling(channelID))
                                    ServiceWriteCommands_SprinklerScheduling(channelID);
                            }
                            break;
                        case FlashSchedulingPolicyType.QoSAware:
                            throw new Exception("Un-handled scheduling policy!");
                    }
                    break;
                case FTLEventType.RequestCompletedByDRAM:
                default:
                    throw new Exception("Unhandled XEvent type");
            }
        }
        /// <summary>
        /// This function is invoked by ServiceIORequest method to check for possiblity of handling new IORequest.
        /// Note: This function is just invoked in normal request generation mode, but not saturated one.
        /// </summary>
        /// <param name="request"></param>
        private void BusProcessInternalRequests_SprinklerScheduling()
        {
            uint randomRow = randomChannelGenerator.GetInteger(this.ChannelCount - 1);
            for (uint channelCntr = 0; channelCntr < this.ChannelCount; channelCntr++)
            {
                uint channelID = (randomRow + channelCntr) % this.ChannelCount;
                if (this.ChannelInfos[channelID].Status == BusChannelStatus.Idle)
                {
                    if (GarbageCollector.ChannelInvokeGCBase(channelID))
                        continue;
                    if (!ServiceReadCommands_SprinklerScheduling(channelID))
                        ServiceWriteCommands_SprinklerScheduling(channelID);
                }
            }
        }
        public void BusChannelIdle_SprinklerScheduling(BusChannelSprinkler targetChannel)
        {
            if (targetChannel.ActiveTransfersCount > 0)
                throw new Exception("Invalid BusChannelIdle invokation!");

            //early escape of function execution to decrease overall execution time.
            if (targetChannel.BusyChipCount == this.ChipNoPerChannel)
                return;

            if (GarbageCollector.ChannelInvokeGCBase(targetChannel.ChannelID))
                return;

            if (!ServiceReadCommands_SprinklerScheduling(targetChannel.ChannelID))
                ServiceWriteCommands_SprinklerScheduling(targetChannel.ChannelID);
        }
        public void BusFlashchipIdle_SprinklerScheduling(FlashChip targetFlashchip)
        {
            uint channelID = targetFlashchip.ChannelID;
            if (this.ChannelInfos[channelID].Status == BusChannelStatus.Idle)
            {
                if (GarbageCollector.ChannelInvokeGCBase(channelID))
                    return;

                if (!ServiceReadCommands_SprinklerScheduling(channelID))
                    ServiceWriteCommands_SprinklerScheduling(channelID);
            }
        }
        #endregion
        #endregion

        #region Properties
        public override void Snapshot(string id, System.Xml.XmlTextWriter writer)
        {
            writer.WriteStartElement(id + "_Statistics");
            writer.WriteAttributeString("ID", ID.ToString());
            writer.WriteAttributeString("TotalIssuedProgramCMD", IssuedProgramCMD.ToString());
            writer.WriteAttributeString("IssuedProgramCMDInterleave", IssuedInterleaveProgramCMD.ToString());
            writer.WriteAttributeString("IssuedProgramCMDMultiplane", IssuedMultiplaneProgramCMD.ToString());
            writer.WriteAttributeString("IssuedProgramCMDInterleaveMultiplane", IssuedInterleaveMultiplaneProgramCMD.ToString());
            writer.WriteAttributeString("IssuedProgramCMDCopyBack", IssuedCopyBackProgramCMD.ToString());
            writer.WriteAttributeString("TotalIssuedReadCMD", IssuedReadCMD.ToString());
            writer.WriteAttributeString("IssuedReadCMDInterleave", IssuedInterleaveReadCMD.ToString());
            writer.WriteAttributeString("IssuedReadCMDMultiplane", IssuedMultiplaneReadCMD.ToString());
            writer.WriteAttributeString("TotalIssuedEraseCMD", IssuedEraseCMD.ToString());
            writer.WriteAttributeString("IssuedEraseCMDInterleave", IssuedInterleaveEraseCMD.ToString());
            writer.WriteAttributeString("IssuedEraseCMDMultiplane", IssuedMultiplaneEraseCMD.ToString());
            writer.WriteAttributeString("IssuedEraseCMDInterleaveMultiplane", IssuedInterleaveMultiplaneEraseCMD.ToString());
            writer.WriteAttributeString("TotalPageProgams", TotalPageProgams.ToString());
            writer.WriteAttributeString("PageProgramsForWorkload", PageProgramsForWorkload.ToString());
            writer.WriteAttributeString("PageProgramForGC", PageProgramForGC.ToString());
            writer.WriteAttributeString("DummyPageProgramsForPreprocess", DummyPageProgramsForPreprocess.ToString());
            writer.WriteAttributeString("TotalPageReadCount", TotalPageReads.ToString());
            writer.WriteAttributeString("PageReadsForWorkload", PageReadsForWorkload.ToString());
            writer.WriteAttributeString("PageReadsForGC", PageReadsForGC.ToString());
            writer.WriteAttributeString("PageReadsForUpdate", PageReadsForUpdate.ToString());
            writer.WriteAttributeString("TotalBlockEraseCount", TotalBlockErases.ToString());
            writer.WriteAttributeString("WastePageCount", WastePageCount.ToString());
            writer.WriteEndElement();
        }
        void UpdateStatistics()
        {
            if (FTLStatisticsUpdated)
                return;

            ulong totalExecutionPeriodNet = 0, totalExecutionOverlapped = 0, totalTransferPeriodNet = 0;
            ulong totalDieReadExecutionPeriod = 0, totalDieWriteExecutionPeriod = 0, totalDieEraseExecutionPeriod = 0, totalDieTransferPeriod = 0;

            for (int channelCntr = 0; channelCntr < ChannelCount; channelCntr++)
                for (int chipCntr = 0; chipCntr < ChipNoPerChannel; chipCntr++)
                {
                    FlashChip targetChip = ChannelInfos[channelCntr].FlashChips[chipCntr];
                    totalExecutionPeriodNet += targetChip.totalCommandExecutionPeriod - targetChip.TotalTransferPeriodOverlapped;
                    totalExecutionOverlapped += targetChip.TotalTransferPeriodOverlapped;
                    totalTransferPeriodNet += targetChip.TotalTransferPeriod - targetChip.TotalTransferPeriodOverlapped;
                    for (int tempdieCntr = 0; tempdieCntr < DieNoPerChip; tempdieCntr++)
                    {
                        totalDieReadExecutionPeriod += targetChip.Dies[tempdieCntr].TotalReadExecutionPeriod;
                        totalDieWriteExecutionPeriod += targetChip.Dies[tempdieCntr].TotalProgramExecutionPeriod;
                        totalDieEraseExecutionPeriod += targetChip.Dies[tempdieCntr].TotalEraseExecutionPeriod;
                        totalDieTransferPeriod += targetChip.Dies[tempdieCntr].TotalTransferPeriod;
                        for (uint tempPlaneCntr = 0; tempPlaneCntr < PlaneNoPerDie; tempPlaneCntr++)
                        {
                            ulong planeReadCount = targetChip.Dies[tempdieCntr].Planes[tempPlaneCntr].ReadCount;
                            totalPlaneReadCount += planeReadCount;
                            if (planeReadCount > maxPlaneReadCount)
                                maxPlaneReadCount = planeReadCount;
                            if (planeReadCount < minPlaneReadCount)
                                minPlaneReadCount = planeReadCount;
                            ulong planeProgramCount = targetChip.Dies[tempdieCntr].Planes[tempPlaneCntr].ProgamCount;
                            totalPlaneProgramCount += planeProgramCount;
                            if (planeProgramCount > maxPlaneProgramCount)
                                maxPlaneProgramCount = planeProgramCount;
                            if (planeProgramCount < minPlaneProgramCount)
                                minPlaneProgramCount = planeProgramCount;
                            ulong planeEraseCount = targetChip.Dies[tempdieCntr].Planes[tempPlaneCntr].EraseCount;
                            totalPlaneEraseCount += planeEraseCount;
                            if (planeEraseCount > maxPlaneEraseCount)
                                maxPlaneEraseCount = planeEraseCount;
                            if (planeEraseCount < minPlaneEraseCount)
                                minPlaneEraseCount = planeEraseCount;

                            ulong planeFreePagesCount = targetChip.Dies[tempdieCntr].Planes[tempPlaneCntr].FreePagesNo;
                            totalPlaneFreePagesCount += planeFreePagesCount;
                            if (planeFreePagesCount > maxPlaneFreePagesCount)
                                maxPlaneFreePagesCount = planeFreePagesCount;
                            if (planeFreePagesCount < minPlaneFreePagesCount)
                                minPlaneFreePagesCount = planeFreePagesCount;

                            ulong planeInvalidPagesCount = 0;
                            for (uint tempBlockCntr = 0; tempBlockCntr < BlockNoPerPlane; tempBlockCntr++)
                            {
                                planeInvalidPagesCount += targetChip.Dies[tempdieCntr].Planes[tempPlaneCntr].Blocks[tempBlockCntr].InvalidPageNo;
                            }
                            totalPlaneInvalidPagesCount += planeInvalidPagesCount;
                            if (planeInvalidPagesCount > maxPlaneInvalidPagesCount)
                                maxPlaneInvalidPagesCount = planeInvalidPagesCount;
                            if (planeInvalidPagesCount < minPlaneInvalidPagesCount)
                                minPlaneInvalidPagesCount = planeInvalidPagesCount;

                            ulong planeValidPagesCount = AddressMapper.PagesNoPerPlane - (planeInvalidPagesCount + planeFreePagesCount);
                            totalPlaneValidPagesCount += planeValidPagesCount;
                            if (planeValidPagesCount > maxPlaneValidPagesCount)
                                maxPlaneValidPagesCount = planeValidPagesCount;
                            if (planeValidPagesCount < minPlaneValidPagesCount)
                                minPlaneValidPagesCount = planeValidPagesCount;
                        }
                    }
                }


            uint totalDies = TotalChipNo * DieNoPerChip;
            double totalPlanes = TotalChipNo * DieNoPerChip * PlaneNoPerDie;
            averageFlashChipCMDExecutionPeriodNet = (double)totalExecutionPeriodNet / ((double)XEngineFactory.XEngine.Time * (double)TotalChipNo);
            averageFlashChipCMDExecutionPeriodOverlapped = (double)totalExecutionOverlapped / ((double)XEngineFactory.XEngine.Time * (double)TotalChipNo);
            averageFlashChipTransferPeriodNet = (double)totalTransferPeriodNet / ((double)XEngineFactory.XEngine.Time * (double)TotalChipNo);
            averageDieReadExecutionPeriod = (double)totalDieReadExecutionPeriod / ((double)XEngineFactory.XEngine.Time * (double)totalDies);
            averageDieWriteExecutionPeriod = (double)totalDieWriteExecutionPeriod / ((double)XEngineFactory.XEngine.Time * (double)totalDies);
            averageDieEraseExecutionPeriod = (double)totalDieEraseExecutionPeriod / ((double)XEngineFactory.XEngine.Time * (double)totalDies);
            averageDieTransferPeriod = (double)totalDieTransferPeriod / ((double)XEngineFactory.XEngine.Time * (double)totalDies);
            averagePageReadsPerPlane = (double)totalPlaneReadCount / (double)(TotalChipNo * DieNoPerChip * PlaneNoPerDie);
            averagePageProgramsPerPlane = (double)totalPlaneProgramCount / (double)(TotalChipNo * DieNoPerChip * PlaneNoPerDie);
            averageBlockErasesPerPlane = (double)totalPlaneEraseCount / (double)(TotalChipNo * DieNoPerChip * PlaneNoPerDie);
            averageNumberOfFreePagesPerPlane = (double)totalPlaneFreePagesCount / (double)(TotalChipNo * DieNoPerChip * PlaneNoPerDie);
            averageNumberOfValidPagesPerPlane = (double)totalPlaneValidPagesCount / (double)(TotalChipNo * DieNoPerChip * PlaneNoPerDie);
            averageNumberOfInvalidPagesPerPlane = (double)totalPlaneInvalidPagesCount / (double)(TotalChipNo * DieNoPerChip * PlaneNoPerDie);
            for (int channelCntr = 0; channelCntr < ChannelCount; channelCntr++)
                for (int chipCntr = 0; chipCntr < ChipNoPerChannel; chipCntr++)
                {
                    for (int tempdieCntr = 0; tempdieCntr < DieNoPerChip; tempdieCntr++)
                        for (uint tempPlaneCntr = 0; tempPlaneCntr < PlaneNoPerDie; tempPlaneCntr++)
                        {
                            planePageReadsStdDev += Math.Pow((averagePageReadsPerPlane - ChannelInfos[channelCntr].FlashChips[chipCntr].Dies[tempdieCntr].Planes[tempPlaneCntr].ReadCount), 2);
                            planePageProgramsStdDev += Math.Pow((averagePageProgramsPerPlane - ChannelInfos[channelCntr].FlashChips[chipCntr].Dies[tempdieCntr].Planes[tempPlaneCntr].ProgamCount), 2);
                            planeBlockErasesStdDev += Math.Pow((averageBlockErasesPerPlane - ChannelInfos[channelCntr].FlashChips[chipCntr].Dies[tempdieCntr].Planes[tempPlaneCntr].EraseCount), 2);
                            planeFreePagesStdDev += Math.Pow((averageNumberOfFreePagesPerPlane - ChannelInfos[channelCntr].FlashChips[chipCntr].Dies[tempdieCntr].Planes[tempPlaneCntr].FreePagesNo), 2);
                            ulong planeInvalidPagesNo = 0;
                            for (uint tempBlockCntr = 0; tempBlockCntr < BlockNoPerPlane; tempBlockCntr++)
                            {
                                planeInvalidPagesNo += ChannelInfos[channelCntr].FlashChips[chipCntr].Dies[tempdieCntr].Planes[tempPlaneCntr].Blocks[tempBlockCntr].InvalidPageNo;
                            }
                            planeInvalidPagesStdDev += Math.Pow((averageNumberOfInvalidPagesPerPlane - planeInvalidPagesNo), 2);
                            planeValidPagesStdDev += Math.Pow((averageNumberOfValidPagesPerPlane - (AddressMapper.PagesNoPerPlane - ChannelInfos[channelCntr].FlashChips[chipCntr].Dies[tempdieCntr].Planes[tempPlaneCntr].FreePagesNo - planeInvalidPagesNo)), 2);
                        }
                    ChannelInfos[channelCntr].FlashChips[chipCntr] = null;
                }
            planePageReadsStdDev = Math.Sqrt(planePageReadsStdDev / totalPlanes);
            planePageProgramsStdDev = Math.Sqrt(planePageProgramsStdDev / totalPlanes);
            planeBlockErasesStdDev = Math.Sqrt(planeBlockErasesStdDev / totalPlanes);
            planeFreePagesStdDev = Math.Sqrt(planeFreePagesStdDev / totalPlanes);
            planeInvalidPagesStdDev = Math.Sqrt(planeInvalidPagesStdDev / totalPlanes);
            planeValidPagesStdDev = Math.Sqrt(planeValidPagesStdDev / totalPlanes);
            FTLStatisticsUpdated = true;
        }
        public double AverageFlashChipCMDExecutionPeriodNet
        {
            get
            {
                UpdateStatistics();
                return this.averageFlashChipCMDExecutionPeriodNet;
            }
        }
        public double AverageFlashChipCMDExecutionPeriodOverlapped
        {
            get
            {
                UpdateStatistics();
                return averageFlashChipCMDExecutionPeriodOverlapped;
            }
        }
        public double AverageFlashChipTransferPeriodNet
        {
            get
            {
                UpdateStatistics();
                return averageFlashChipTransferPeriodNet;
            }
        }
        public double AverageFlashChipeIdePeriod
        {
            get
            {
                UpdateStatistics();
                return ((double)1 - averageFlashChipCMDExecutionPeriodNet - averageFlashChipCMDExecutionPeriodOverlapped - averageFlashChipTransferPeriodNet);
            }
        }
        public double AverageDieReadExecutionPeriod
        {
            get
            {
                UpdateStatistics();
                return averageDieReadExecutionPeriod;
            }
        }
        public double AverageDieWriteExecutionPeriod
        {
            get
            {
                UpdateStatistics();
                return averageDieWriteExecutionPeriod;
            }
        }
        public double AverageDieEraseExecutionPeriod
        {
            get
            {
                UpdateStatistics();
                return averageDieEraseExecutionPeriod;
            }
        }
        public double AverageDieTransferPeriod
        {
            get
            {
                UpdateStatistics();
                return averageDieTransferPeriod;
            }
        }
        public double AverageDieIdlePeriod
        {
            get
            {
                UpdateStatistics();
                return ((double)1 - averageDieReadExecutionPeriod - averageDieWriteExecutionPeriod
                                            - averageDieEraseExecutionPeriod - averageDieTransferPeriod);
            }
        }
        public double AveragePageReadsPerPlane
        {
            get
            {
                UpdateStatistics();
                return averagePageReadsPerPlane;
            }
        }
        public double AveragePageProgramsPerPlane
        {
            get
            {
                UpdateStatistics();
                return averagePageProgramsPerPlane;
            }
        }
        public double AverageBlockErasesPerPlane
        {
            get
            {
                UpdateStatistics();
                return averageBlockErasesPerPlane;
            }
        }
        public double AverageNumberOfFreePagesPerPlane
        {
            get
            {
                UpdateStatistics();
                return averageNumberOfFreePagesPerPlane;
            }
        }
        public double AverageNumberOfValidPagesPerPlane
        {
            get
            {
                UpdateStatistics();
                return averageNumberOfValidPagesPerPlane;
            }
        }
        public double AverageNumberOfInvalidPagesPerPlane
        {
            get
            {
                UpdateStatistics();
                return averageNumberOfInvalidPagesPerPlane;
            }
        }
        public double PlanePageReadsStdDev
        {
            get
            {
                UpdateStatistics();
                return planePageReadsStdDev;
            }
        }
        public double PlanePageProgramsStdDev
        {
            get
            {
                UpdateStatistics();
                return planePageProgramsStdDev;
            }
        }
        public double PlaneBlockErasesStdDev
        {
            get
            {
                UpdateStatistics();
                return planeBlockErasesStdDev;
            }
        }
        public double PlaneFreePagesStdDev
        {
            get
            {
                UpdateStatistics();
                return planeFreePagesStdDev;
            }
        }
        public double PlaneValidPagesStdDev
        {
            get
            {
                UpdateStatistics();
                return planeValidPagesStdDev;
            }
        }
        public double PlaneInvalidPagesStdDev
        {
            get
            {
                UpdateStatistics();
                return planeInvalidPagesStdDev;
            }
        }
        public ulong TotalPlaneReadCount
        {
            get
            {
                UpdateStatistics();
                return totalPlaneReadCount;
            }
        }
        public ulong MinPlaneReadCount
        {
            get
            {
                UpdateStatistics();
                return minPlaneReadCount;
            }
        }
        public ulong MaxPlaneReadCount
        {
            get
            {
                UpdateStatistics();
                return maxPlaneReadCount;
            }
        }
        public ulong TotalPlaneProgramCount
        {
            get
            {
                UpdateStatistics();
                return totalPlaneProgramCount;
            }
        }
        public ulong MinPlaneProgramCount
        {
            get
            {
                UpdateStatistics();
                return minPlaneProgramCount;
            }
        }
        public ulong MaxPlaneProgramCount
        {
            get
            {
                UpdateStatistics();
                return maxPlaneProgramCount;
            }
        }
        public ulong TotalPlaneEraseCount
        {
            get
            {
                UpdateStatistics();
                return totalPlaneEraseCount;
            }
        }
        public ulong MinPlaneEraseCount
        {
            get
            {
                UpdateStatistics();
                return minPlaneEraseCount;
            }
        }
        public ulong MaxPlaneEraseCount
        {
            get
            {
                UpdateStatistics();
                return maxPlaneEraseCount;
            }
        }
        public ulong TotalPlaneValidPagesCount
        {
            get
            {
                UpdateStatistics();
                return totalPlaneValidPagesCount;
            }
        }
        public ulong MaxPlaneValidPagesCount
        {
            get
            {
                UpdateStatistics();
                return maxPlaneValidPagesCount;
            }
        }
        public ulong MinPlaneValidPagesCount
        {
            get
            {
                UpdateStatistics();
                return minPlaneValidPagesCount;
            }
        }
        public ulong TotalPlaneFreePagesCount
        {
            get
            {
                UpdateStatistics();
                return totalPlaneFreePagesCount;
            }
        }
        public ulong MaxPlaneFreePagesCount
        {
            get
            {
                UpdateStatistics();
                return maxPlaneFreePagesCount;
            }
        }
        public ulong MinPlaneFreePagesCount
        {
            get
            {
                UpdateStatistics();
                return minPlaneFreePagesCount;
            }
        }
        public ulong TotalPlaneInvalidPagesCount
        {
            get
            {
                UpdateStatistics();
                return totalPlaneInvalidPagesCount;
            }
        }
        public ulong MaxPlaneInvalidPagesCount
        {
            get
            {
                UpdateStatistics();
                return maxPlaneInvalidPagesCount;
            }
        }
        public ulong MinPlaneInvalidPagesCount
        {
            get
            {
                UpdateStatistics();
                return minPlaneInvalidPagesCount;
            }
        }
        #endregion
    }
}
