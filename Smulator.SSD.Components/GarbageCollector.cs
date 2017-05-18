using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Smulator.BaseComponents;
using Smulator.Util;

namespace Smulator.SSD.Components
{
    public enum GCJobType { Emergency };
    public class GCJob
    {
        public IntegerPageAddress TargetAddress;
        public GCJobType Type;

        public GCJob(uint RowID, uint FlashChipID, uint DieID, uint PlaneID, uint BlockID, uint OverallFlashChipID, GCJobType type)
        {
            this.TargetAddress = new IntegerPageAddress(RowID, FlashChipID, DieID, PlaneID, BlockID, 0, OverallFlashChipID);
            this.Type = type;
        }

        public GCJob(IntegerPageAddress targetAddress, GCJobType type)
        {
            this.TargetAddress = new IntegerPageAddress(targetAddress);
            this.Type = type;
        }
    }
    public class GCJobList : LinkedList<GCJob>
    {
    }
    #region Change info
    /// <change>
    /// <author>Arash Tavakkol</author>
    /// <description>A greate change: Emergeny grabage collection threshold is removed from input file. This 
    /// garbage collection is triggered when there are very few pages available inside plane.</description>
    /// <date>2014/11/07</date>
    /// </change>
    #endregion 
    /// <summary>
    /// Emergency garbage collection process:
    /// 1) Request added as a GCJob to the ChannelInformation.CurrentEmergencyCMRequests list.
    /// 2) BusInvokeGarbageCollection is executed:
    ///     2-1) If there is a direct erase opportunity(i.e. a block without any valid pages), just remove job from ChannelInformation.CurrentEmergencyCMRequests and performa erase
    ///     2-2) If there is no direct erase then:
    ///         2-2-1) Find a block based on specified GCPolicyType 
    ///         2-2-2) Generate an InternalRequestCM to perform page movement
    ///         2-2-3) Add request to ChannelInformation.CurrentEmergencyCMRequests
    ///         2-2-4) Remove GCJob from ChannelInformation.CurrentEmergencyCMRequests 
    ///         2-2-5) Perfrom read/write requests of the InternalRequestCM.InternalWriteRequestList, one-by-one
    ///         2-2-6) When all requests are finished, remove InternalRequestCM from ChannelInformation.CurrentEmergencyCMRequests and perform a simple erase operation.
    /// </summary>    
    public class GarbageCollector : XObject
    {
        /*Following set is defined to reflect GC methods introduced or referenced in this paper:
         * "A Mean Field Model for a Class of Garbage Collection Algorithms in Flash-based Solid State Drives", SIGMETRICS 2013.
         * and "Stochastic Modeling of Large-Scale Solid-State Storage Systems: Analysis, Design Tradeoffs and Optimization", SIGMETRICS 2013.*/
        public enum GCPolicyType
        {
            Greedy, GreedyUnfair, WindowedGreedy, Random, RandomPlus, RandomPlusPlus, RGA, FIFO, MS,
            TB_Greedy_FreeAware,
            TB_Greedy_Blind,
            Proposed1,  //alternativelly selects min(validPagesCount) and min(EraseCount)
            Proposed2,  //selects min(validPagesCount) if no block found with [AverageEraseCount - EraseCount] > a * AverageEraseCount
            Proposed3,  //selects min(cost), cost = validPagesCount + (AverageEraseCount - EraseCount)
            Proposed4,  //selects min(cost), cost = validPagesCount^2 + (AverageEraseCount - EraseCount)
            Proposed5   //selects min(cost), cost = a*validPagesCount^p1 + b*(AverageEraseCount - EraseCount)^p2
        };
 
        public GCPolicyType Type = GCPolicyType.Greedy;
        public bool copybackOddEvenConstraint = false; //According to ONFI 3.2 standard, copyback command have odd/even page constraint
        public bool dynamicWearLevelingEnabled = true;
        public uint EmergencyThreshold_PlaneFreePages = 0; //minimum number of free pages for a plane that triggers garbage collection
        public FTL FTL;
        public ulong EmergencyGCRequests = 0;
        public ulong TotalGCExecutionCount = 0, EmergencyGCExecutionCount = 0, SkippedEmergencyGCRequests = 0;
        protected uint LastGreedyStartPosition = 0;
        RandomGenerator randomBlockGenerator = null;
        uint randomPlusPlusThreshold = 0, RGAConstant = 10, WGreedyWindowSize = 500, blockNoPerPlane = 0, pageNoPerBlock = 0;
        ulong firstEmergencyGCStartTime = ulong.MaxValue;
        ulong lastEmergencyGCStartTime = 0, sumOfEmergencyGCArrivalTime = 0;

        ulong DirectEraseExecution = 0, totalPageMovements = 0, totalPageMovementsForEmergencyGC = 0;//page movement count

        ulong totalNumberOfComparisonsToFindCandidateBlock = 0, totalNumberOfRandomNumberGeneration = 0, 
            totalNumberOfComparisonToCreateRandomSet = 0;

        public bool UseDirectErase = false;//Direct erase was implemented in original SSDSim code.
        private bool LoggingEnabled = false;
        private bool copyBackCommandEnabled = false; //copy back execution is enabled or not
        private bool multiplaneEraseEnabled = false; //multiplane execution is enabled or not
        private bool interleavedEraseEnabled = false; //interleaved execution is enabled or not
        protected string GCLogFilePath;
        protected StreamWriter GCLogFile;
        protected StreamWriter[] GCLogSpecialFiles;
        protected ulong loggingStep = 5, loggingCntr = 0;
        protected ulong thisRoundSumOfEmergencyGCArrivalTime = 0;
        protected ulong thisRoundTotalPageMovementsForEmergencyGC = 0;
        public ulong ThisRoundEmergencyGCExecutionCount = 0;
        bool erasureStatisticsUpdated = true;
        ulong blockEraseCountMax = 0, blockEraseCountMin = ulong.MaxValue;
        double averageBlockEraseCount = 0, blockEraseStdDev = 0;
        double[] validPageDistributionOnBlocksPDF, invalidPageDistributionOnBlocksPDF, selectionFunctionPDF;
        ulong[] selectionFrequency;//This array stores the frequency of selecting the blocks with different number of valid pages
        double wearLevelingFairness;
        double averageBlockValidPagesCount, blockValidPagesCountStdDev, averageBlockInvalidPagesCount, blockInvalidPagesCountStdDev;
        uint maxBlockValidPagesCount = 0, minBlockValidPagesCount = uint.MaxValue, maxBlockInvalidPagesCount = 0, minBlockInvalidPagesCount = uint.MaxValue;
        
        uint setSizeForProposedAlgorithm = 0;
        bool turn = true;
        double Proposed2Parameter = 0.9;
        double Proposed5a = 2, Proposed5P1 = 2, Proposed5b = 2, Proposed5P2 = 1;

        public bool TB_Enabled = false;//Is Twain Block mangement enabled?

        #region SetupFunctions
        public GarbageCollector(string id, GCPolicyType Type,
            uint RGAConstant, uint WGreedyWindowSize, double overprovisioningRatio,
            uint totalChipNo, uint pageNoPerPlane, uint blockNoPerPlane, uint pageNoPerBlock,
            bool dynamicWearLevelingEnabled, bool copyBackEnabled, bool copybackOddEvenConstraint,
            bool multiplaneEnabled, bool interleavedEnabled, bool GCLoggingEnabled,
            bool TB_Enabled,
            string GCLogFilePath, int seed) :
            base(id)
        {
            this.Type = Type;
            this.RGAConstant = RGAConstant;
            this.WGreedyWindowSize = WGreedyWindowSize;
            this.EmergencyThreshold_PlaneFreePages = pageNoPerBlock * 10;
            this.randomBlockGenerator = new RandomGenerator(seed);
            this.blockNoPerPlane = blockNoPerPlane;
            this.pageNoPerBlock = pageNoPerBlock;
            this.randomPlusPlusThreshold = (uint)((double)pageNoPerBlock * overprovisioningRatio);
            this.selectionFrequency = new ulong[pageNoPerBlock + 1];
            for (int i = 0; i < pageNoPerBlock + 1; i++)
                this.selectionFrequency[i] = 0;

            this.dynamicWearLevelingEnabled = dynamicWearLevelingEnabled;
            this.copyBackCommandEnabled = copyBackEnabled;
            this.copybackOddEvenConstraint = copybackOddEvenConstraint;
            this.multiplaneEraseEnabled = multiplaneEnabled;
            this.interleavedEraseEnabled = interleavedEnabled;

                
            this.TB_Enabled = TB_Enabled;

            this.LoggingEnabled = GCLoggingEnabled;
            if (GCLoggingEnabled)
            {
                this.GCLogFilePath = GCLogFilePath;
                this.GCLogFile = new StreamWriter(GCLogFilePath.Remove(GCLogFilePath.LastIndexOf(".log")) + "-GC.log");
                this.GCLogFile.WriteLine("TriggerTime(us)\tFlashChipID\tDieID\tPlaneID\tAverageRatePerSecond(gc/ms)\tAverageRatePerSecondThisRound(gc/s)\tAverageGCCost(pagemoves/gc)\tAverageGCCostThisRound(pagemoves/gc)");
                /*this.GCLogSpecialFiles = new StreamWriter[totalChipNo];
                for (int i = 0; i < totalChipNo; i++)
                {
                    this.GCLogSpecialFiles[i] = new StreamWriter(GCLogFilePath.Remove(GCLogFilePath.LastIndexOf(".log")) + "Chip-" + i + "-GC.log");
                    this.GCLogSpecialFiles[i].WriteLine("TriggerTime(us)\tDieID\tPlaneID\tAverageRatePerSecond(gc/ms)\tAverageRatePerSecondThisRound(gc/s)\tAverageGCCost(pagemoves/gc)\tAverageGCCostThisRound(pagemoves/gc)");
                }*/
            }
            validPageDistributionOnBlocksPDF = new double[pageNoPerBlock + 1];
            invalidPageDistributionOnBlocksPDF = new double[pageNoPerBlock + 1];
            selectionFunctionPDF = new double[pageNoPerBlock + 1];

            setSizeForProposedAlgorithm = (uint) Math.Log(blockNoPerPlane, 2);
        }
        public override void Start()
        {
        }
        public override void Validate()
        {
            base.Validate();
            if (FTL == null)
                throw new ValidationException(string.Format("GC ({0}) has no FTL", ID));
        }
        #endregion

        #region GarbageCollectionForBusChannel
        /// <summary>
        /// Checks GC requests and triggers GC process.
        /// It assumes that target channel is in idle state.
        /// </summary>
        /// <param name="channelID">The ID of target channel.</param>
        /// <returns>Returns true if GC is invoked.</returns>
        private bool BusStartEmergencyGC(IntegerPageAddress GCTargetAddress, BusChannelBase targetChannel, LinkedListNode<GCJob> gcJob)
        {
            FlashChipPlane targetPlane = FTL.FlashChips[GCTargetAddress.OverallFlashChipID].Dies[GCTargetAddress.DieID].Planes[GCTargetAddress.PlaneID];
            FlashChipBlock selectedMaxBlock = null;
            GCTargetAddress.BlockID = uint.MaxValue;
            IntegerPageAddress tempAddress = new IntegerPageAddress(GCTargetAddress);


            if (Type == GCPolicyType.Greedy && UseDirectErase)
                if (FTL.FlashChips[GCTargetAddress.OverallFlashChipID].Dies[GCTargetAddress.DieID].Planes[GCTargetAddress.PlaneID].DirectEraseNodes.Count > 0)
                {
                    this.EmergencyGCExecutionCount++; this.ThisRoundEmergencyGCExecutionCount++;
                    this.TotalGCExecutionCount++;
                    erasureStatisticsUpdated = true;
                    DirectErase(GCTargetAddress);
                    targetChannel.EmergencyGCRequests.Remove(gcJob);
                    targetPlane.HasGCRequest = false;
                    return true;
                }


            SelectCandidateBlock(GCTargetAddress, targetPlane);
            //There were no block with invalid page!
            if (GCTargetAddress.BlockID == uint.MaxValue)
            {
                this.SkippedEmergencyGCRequests++;
                return false;
            }
            else if (GCTargetAddress.BlockID == targetPlane.CurrentActiveBlockID)
                throw new Exception("A forbidden condition occured. The candidate GC block is the same as current active block in FlashChip:"
                    + GCTargetAddress.OverallFlashChipID
                    + ",Die:" + GCTargetAddress.DieID
                    + ",Plane:" + GCTargetAddress.PlaneID + ")");

            erasureStatisticsUpdated = true;
            this.EmergencyGCExecutionCount++; this.ThisRoundEmergencyGCExecutionCount++;
            this.TotalGCExecutionCount++;

            selectedMaxBlock = targetPlane.Blocks[GCTargetAddress.BlockID];
            selectionFrequency[pageNoPerBlock - selectedMaxBlock.InvalidPageNo - selectedMaxBlock.FreePageNo]++;
            InternalCleanRequest cmReq = new InternalCleanRequest(InternalRequestType.Clean, GCTargetAddress, true, false);
            cmReq.TargetFlashChip = FTL.FlashChips[GCTargetAddress.OverallFlashChipID];

            IntegerPageAddress destAddress = new IntegerPageAddress(GCTargetAddress);
            for (uint pageID = 0; pageID < pageNoPerBlock; pageID++)
            {
                if (selectedMaxBlock.Pages[pageID].ValidStatus != FlashChipPage.PG_FREE && selectedMaxBlock.Pages[pageID].ValidStatus != FlashChipPage.PG_INVALID)
                {
                    GCTargetAddress.PageID = pageID;
                    setupPageMovement(GCTargetAddress, destAddress, cmReq);
                    this.totalPageMovements++;
                    this.totalPageMovementsForEmergencyGC++;
                    this.thisRoundTotalPageMovementsForEmergencyGC++;
                    FTL.PageReadsForGC++;
                    FTL.PageProgramForGC++;
                    FTL.TotalPageProgams++;
                    FTL.TotalPageReads++;
                }
            }
            targetChannel.EmergencyGCRequests.Remove(gcJob);
            targetPlane.HasGCRequest = false;
            if (LoggingEnabled)
            {
                this.GCLogFile.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}", (XEngineFactory.XEngine.Time / 1000), tempAddress.OverallFlashChipID, tempAddress.DieID, tempAddress.PlaneID,
                    this.AverageEmergencyGCRate, ThisRoundAverageEmergencyGCRate, AverageEmergencyGCCost, ThisRoundAverageEmergencyGCCost);
                //GCLogSpecialFiles[tempAddress.OverallFlashChipID].WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", (XEngineFactory.XEngine.Time / 1000), tempAddress.DieID, tempAddress.PlaneID,
                //this.AverageEmergencyGCRate, ThisRoundAverageEmergencyGCRate, AverageEmergencyGCCost, ThisRoundAverageEmergencyGCCost);
            }
            FTL.TotalBlockErases++;
            if (cmReq.InternalWriteRequestList.Count > 0)
            {
                targetChannel.CurrentEmergencyCMRequests.AddLast(cmReq);
                movePage(cmReq, targetChannel);
            }
            else FTL.FCC.SendSimpleCommandToChip(cmReq); //No page movement is required
            return true;
        }
        private bool BusStartEmergencyGCTB(IntegerPageAddress GCTargetAddress, BusChannelBase targetChannel, LinkedListNode<GCJob> gcJob)
        {
            FlashChipPlane targetPlane = FTL.FlashChips[GCTargetAddress.OverallFlashChipID].Dies[GCTargetAddress.DieID].Planes[GCTargetAddress.PlaneID];
            FlashChipBlock selectedMaxBlock = null;
            GCTargetAddress.BlockID = uint.MaxValue;
            IntegerPageAddress tempAddress = new IntegerPageAddress(GCTargetAddress);

            this.EmergencyGCExecutionCount++; this.ThisRoundEmergencyGCExecutionCount++;
            this.TotalGCExecutionCount++;
            erasureStatisticsUpdated = true;

            uint[] blockIDPerDie = new uint[FTL.DieNoPerChip];
            for (uint i = 0; i < FTL.DieNoPerChip; i++)
                blockIDPerDie[i] = uint.MaxValue;
            SelectCandidateBlockGCTB(GCTargetAddress, blockIDPerDie);

            //selectionFrequency[pageNoPerBlock - selectedMaxBlockUnit.InvalidPageNo - selectedMaxBlockUnit.FreePageNo]++;
            InternalCleanRequestFast cmReq = new InternalCleanRequestFast(InternalRequestType.Clean, GCTargetAddress, true, true);
            cmReq.BlockIDs = blockIDPerDie;
            cmReq.NumberOfErases = FTL.DieNoPerChip * FTL.PlaneNoPerDie;
            cmReq.TargetFlashChip = FTL.FlashChips[GCTargetAddress.OverallFlashChipID];
            cmReq.ParallelEraseRequests = new InternalCleanRequestLinkedList();
            
            for (uint dieID = 0; dieID < FTL.DieNoPerChip; dieID++)
            {
                FlashChipDie targetDie = FTL.FlashChips[GCTargetAddress.OverallFlashChipID].Dies[dieID];
                GCTargetAddress.DieID = dieID;
                GCTargetAddress.BlockID = blockIDPerDie[dieID];

                uint totalWrites = (pageNoPerBlock * FTL.PlaneNoPerDie) - targetDie.BlockInfoAbstract[GCTargetAddress.BlockID].InvalidPageNo - targetDie.BlockInfoAbstract[GCTargetAddress.BlockID].FreePageNo;

                if (totalWrites != 0)
                {
                    totalWrites += targetDie.CurrentActivePlaneID;//This is required for equaivalent distribution of writes
                    ArrayList[] movementDestination = new ArrayList[FTL.PlaneNoPerDie];
                    ArrayList[] movablePageIDs = new ArrayList[FTL.PlaneNoPerDie];
                    uint[] neededMovementsCountForPlanes = new uint[FTL.PlaneNoPerDie];
                    uint maxWrites = 0;
                    
                    for (int planeID = 0; planeID < FTL.PlaneNoPerDie; planeID++)
                    {
                        movementDestination[planeID] = new ArrayList();
                        movablePageIDs[planeID] = new ArrayList();

                        GCTargetAddress.PlaneID = (uint)planeID;
                        cmReq.ParallelEraseRequests.AddLast(new InternalCleanRequest(InternalRequestType.Clean, GCTargetAddress, true, true, InternalRequestExecutionType.InterleavedMultiplane));
                        FTL.TotalBlockErases++;

                        selectedMaxBlock = FTL.FlashChips[GCTargetAddress.OverallFlashChipID].Dies[GCTargetAddress.DieID].Planes[GCTargetAddress.PlaneID].Blocks[GCTargetAddress.BlockID];

                        neededMovementsCountForPlanes[planeID] = pageNoPerBlock - targetDie.Planes[GCTargetAddress.PlaneID].Blocks[GCTargetAddress.BlockID].InvalidPageNo - targetDie.Planes[GCTargetAddress.PlaneID].Blocks[GCTargetAddress.BlockID].FreePageNo;
                        if (planeID < targetDie.CurrentActivePlaneID)
                            neededMovementsCountForPlanes[planeID]++;
                        if (neededMovementsCountForPlanes[planeID] > maxWrites)
                            maxWrites = neededMovementsCountForPlanes[planeID];
                        for (uint pageID = 0; pageID < pageNoPerBlock; pageID++)
                            if (selectedMaxBlock.Pages[pageID].ValidStatus != FlashChipPage.PG_FREE && selectedMaxBlock.Pages[pageID].ValidStatus != FlashChipPage.PG_INVALID)
                                movablePageIDs[planeID].Add(pageID);
                    }

                    #region EvenWriteDistribution
                    for (int i = 0; i < maxWrites; i++)
                    {
                        for (uint planeCntr = 0; planeCntr < FTL.PlaneNoPerDie; planeCntr++)
                        {
                            if (neededMovementsCountForPlanes[planeCntr] > 0)
                            {
                                movementDestination[planeCntr].Add(planeCntr);
                                neededMovementsCountForPlanes[planeCntr]--;
                            }
                            else
                            {
                                uint localMaxID = 0;
                                for (uint j = 0; j < FTL.PlaneNoPerDie; j++)
                                    if (neededMovementsCountForPlanes[j] >= neededMovementsCountForPlanes[localMaxID])
                                        localMaxID = j;

                                movementDestination[localMaxID].Add(planeCntr);
                                neededMovementsCountForPlanes[localMaxID]--;
                            }
                            totalWrites--;
                            if (totalWrites == 0)
                                break;
                        }
                        if (totalWrites == 0)
                            break;
                    }
                    for (int i = 0; i < targetDie.CurrentActivePlaneID; i++)
                        movementDestination[i].RemoveAt(0);
                    targetDie.CurrentActivePlaneID = (targetDie.CurrentActivePlaneID + ((pageNoPerBlock * FTL.PlaneNoPerDie) - targetDie.BlockInfoAbstract[GCTargetAddress.BlockID].InvalidPageNo - targetDie.BlockInfoAbstract[GCTargetAddress.BlockID].FreePageNo)) % FTL.PlaneNoPerDie;
                    #endregion

                    for (int pageCntr = 0; pageCntr < maxWrites; pageCntr++)
                    {
                        for (int planeID = (int)FTL.PlaneNoPerDie - 1; planeID >= 0 ; planeID--)
                        {
                            IntegerPageAddress destAddress = new IntegerPageAddress(GCTargetAddress);
                            if (movablePageIDs[planeID].Count > pageCntr)
                            {
                                GCTargetAddress.PlaneID = (uint)planeID;
                                GCTargetAddress.PageID = (uint)movablePageIDs[planeID][pageCntr];
                                destAddress.PlaneID = (uint)movementDestination[planeID][pageCntr];
                                setupPageMovement(GCTargetAddress, destAddress, cmReq);
                                this.totalPageMovements++;
                                this.totalPageMovementsForEmergencyGC++;
                                this.thisRoundTotalPageMovementsForEmergencyGC++;
                                FTL.PageReadsForGC++;
                                FTL.PageProgramForGC++;
                                FTL.TotalPageProgams++;
                                FTL.TotalPageReads++;
                            }
                        }
                    }
                }
                else
                    for (uint planeID = 0; planeID < FTL.PlaneNoPerDie; planeID++)
                    {
                        GCTargetAddress.PlaneID = planeID;
                        cmReq.ParallelEraseRequests.AddLast(new InternalCleanRequest(InternalRequestType.Clean, GCTargetAddress, true, true, InternalRequestExecutionType.InterleavedMultiplane));
                        FTL.TotalBlockErases++;
                    }
                FTL.WastePageCount += targetDie.BlockInfoAbstract[GCTargetAddress.BlockID].FreePageNo;
            }
            targetChannel.EmergencyGCRequests.Remove(gcJob);
            targetPlane.HasGCRequest = false;

            GCJobList mustRemoveJobList = new GCJobList();
            for (var tempGCJob = targetChannel.EmergencyGCRequests.First; tempGCJob != null; tempGCJob = tempGCJob.Next)
                if (tempGCJob.Value.TargetAddress.OverallFlashChipID == GCTargetAddress.OverallFlashChipID)
                    mustRemoveJobList.AddLast(tempGCJob.Value);
            for (var tempGCJob = mustRemoveJobList.First; tempGCJob != null; tempGCJob = tempGCJob.Next)
            {
                targetChannel.EmergencyGCRequests.Remove(tempGCJob.Value);
                targetChannel.FlashChips[tempGCJob.Value.TargetAddress.LocalFlashChipID].Dies[tempGCJob.Value.TargetAddress.DieID].Planes[tempGCJob.Value.TargetAddress.PlaneID].HasGCRequest = false;
            }

            if (LoggingEnabled)
            {
                this.GCLogFile.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}", (XEngineFactory.XEngine.Time / 1000), tempAddress.OverallFlashChipID, tempAddress.DieID, tempAddress.PlaneID,
                    this.AverageEmergencyGCRate, ThisRoundAverageEmergencyGCRate, AverageEmergencyGCCost, ThisRoundAverageEmergencyGCCost);
                /*GCLogSpecialFiles[tempAddress.OverallFlashChipID].WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", (XEngineFactory.XEngine.Time / 1000), tempAddress.DieID, tempAddress.PlaneID,
                this.AverageEmergencyGCRate, ThisRoundAverageEmergencyGCRate, AverageEmergencyGCCost, ThisRoundAverageEmergencyGCCost);*/
            }
            if (cmReq.InternalWriteRequestList.Count > 0)
            {
                targetChannel.CurrentEmergencyCMRequests.AddLast(cmReq);
                movePage(cmReq, targetChannel);
            }
            else FTL.FCC.SendAdvCommandToChipER(cmReq.ParallelEraseRequests);
            return true;
        }
        public bool ChannelInvokeGCBase(uint channelID)
        {
            BusChannelBase targetChannel = FTL.ChannelInfos[channelID];
            if (targetChannel.Status != BusChannelStatus.Idle)
                throw new Exception("Invoking garbage collection on a busy channel (ID = @1)!");

            #region ContinueTriggeredGCs
            //It is preferred to continue previously triggered GCs before responding new GC requests. 
            if (targetChannel.CurrentEmergencyCMRequests.Count > 0)
            {
                for (var cmReq = targetChannel.CurrentEmergencyCMRequests.First; cmReq != null; cmReq = cmReq.Next)
                    if (cmReq.Value.TargetFlashChip.Status == FlashChipStatus.Idle)
                    {
                        InternalCleanRequest targetReq = cmReq.Value as InternalCleanRequest;
                        if (targetReq.InternalWriteRequestList.Count > 0)
                        {
                            movePage(targetReq, targetChannel);
                            return true;
                        }

                        //All required page movements are accomplished and erase operation could be executed.
                        targetChannel.CurrentEmergencyCMRequests.Remove(targetReq);

                        if (targetReq.IsFast)
                            FTL.FCC.SendAdvCommandToChipER((targetReq as InternalCleanRequestFast).ParallelEraseRequests);
                        else
                            FTL.FCC.SendSimpleCommandToChip(targetReq);

                        return true;
                    }
            }
            #endregion

            #region RespondToNewEGCRequests
            if (targetChannel.EmergencyGCRequests.Count > 0)
            {
                for (var gcJob = targetChannel.EmergencyGCRequests.First; gcJob != null; gcJob = gcJob.Next)
                {
                    IntegerPageAddress targetAddress = gcJob.Value.TargetAddress;
                    if (targetChannel.FlashChips[targetAddress.LocalFlashChipID].Status == FlashChipStatus.Idle)
                    {
                        //Calculate GC execution statistics
                        ulong distanceToPrevExecution = 0;
                        if (lastEmergencyGCStartTime > 0)
                            distanceToPrevExecution = XEngineFactory.XEngine.Time - lastEmergencyGCStartTime;
                        if (this.firstEmergencyGCStartTime == ulong.MaxValue)
                        {
                            firstEmergencyGCStartTime = XEngineFactory.XEngine.Time;
                            FTL.HostInterface.FirstGCEvent();
                        }
                        else
                        {
                            sumOfEmergencyGCArrivalTime += distanceToPrevExecution;
                            thisRoundSumOfEmergencyGCArrivalTime += distanceToPrevExecution;
                            lastEmergencyGCStartTime = XEngineFactory.XEngine.Time;
                        }

                        if (TB_Enabled)
                            return BusStartEmergencyGCTB(targetAddress, targetChannel, gcJob);
                        else
                            return BusStartEmergencyGC(targetAddress, targetChannel, gcJob);
                    }
                }
            }
            #endregion

            return false;
        }
        public void ChannelInvokeGCRPB(uint channelID)
        {
            BusChannelRPB targetChannel = (FTL.ChannelInfos[channelID] as BusChannelRPB);

            if (targetChannel.EmergencyGCRequests.Count > 0)
            {
                for (var gcJob = targetChannel.EmergencyGCRequests.First; gcJob != null; gcJob = gcJob.Next)
                {
                    IntegerPageAddress GCTargetAddress = gcJob.Value.TargetAddress;

                    #region Statistics
                    //Calculate GC execution statistics
                    ulong distanceToPrevExecution = 0;
                    if (lastEmergencyGCStartTime > 0)
                        distanceToPrevExecution = XEngineFactory.XEngine.Time - lastEmergencyGCStartTime;
                    if (firstEmergencyGCStartTime == ulong.MaxValue)
                    {
                        firstEmergencyGCStartTime = XEngineFactory.XEngine.Time;
                        FTL.HostInterface.FirstGCEvent();
                    }
                    else
                    {
                        sumOfEmergencyGCArrivalTime += distanceToPrevExecution;
                        thisRoundSumOfEmergencyGCArrivalTime += distanceToPrevExecution;
                        lastEmergencyGCStartTime = XEngineFactory.XEngine.Time;
                    }
                    if (LoggingEnabled)
                    {
                        GCLogFile.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}", (XEngineFactory.XEngine.Time / 1000), GCTargetAddress.OverallFlashChipID, GCTargetAddress.DieID, GCTargetAddress.PlaneID,
                            AverageEmergencyGCRate, ThisRoundAverageEmergencyGCRate, AverageEmergencyGCCost, ThisRoundAverageEmergencyGCCost);
                        //GCLogSpecialFiles[tempAddress.OverallFlashChipID].WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", (XEngineFactory.XEngine.Time / 1000), GCTargetAddress.DieID, GCTargetAddress.PlaneID,
                        //this.AverageEmergencyGCRate, ThisRoundAverageEmergencyGCRate, AverageEmergencyGCCost, ThisRoundAverageEmergencyGCCost);
                    }

                    erasureStatisticsUpdated = true;
                    EmergencyGCExecutionCount++;
                    ThisRoundEmergencyGCExecutionCount++;
                    TotalGCExecutionCount++;
                    FTL.TotalBlockErases++;
                    #endregion

                    FlashChipPlane targetPlane = FTL.FlashChips[GCTargetAddress.OverallFlashChipID].Dies[GCTargetAddress.DieID].Planes[GCTargetAddress.PlaneID];
                    FlashChipBlock selectedMaxBlock = null;
                    GCTargetAddress.BlockID = uint.MaxValue;
                    SelectCandidateBlock(GCTargetAddress, targetPlane);
                    //There were no block with invalid page!
                    if (GCTargetAddress.BlockID == uint.MaxValue)
                        SkippedEmergencyGCRequests++;
                    else if (GCTargetAddress.BlockID == targetPlane.CurrentActiveBlockID)
                        throw new Exception("A forbidden condition occured. The candidate GC block is the same as current active block in FlashChip:"
                            + GCTargetAddress.OverallFlashChipID
                            + ",Die:" + GCTargetAddress.DieID
                            + ",Plane:" + GCTargetAddress.PlaneID + ")");
                    selectedMaxBlock = targetPlane.Blocks[GCTargetAddress.BlockID];
                    selectionFrequency[pageNoPerBlock - selectedMaxBlock.InvalidPageNo - selectedMaxBlock.FreePageNo]++;

                    InternalCleanRequest cmReq = new InternalCleanRequest(InternalRequestType.Clean, GCTargetAddress, true, false);
                    cmReq.TargetFlashChip = FTL.FlashChips[GCTargetAddress.OverallFlashChipID];
                    IntegerPageAddress destAddress = new IntegerPageAddress(GCTargetAddress);
                    for (uint pageID = 0; pageID < pageNoPerBlock; pageID++)
                    {
                        if (selectedMaxBlock.Pages[pageID].ValidStatus != FlashChipPage.PG_FREE && selectedMaxBlock.Pages[pageID].ValidStatus != FlashChipPage.PG_INVALID)
                        {
                            GCTargetAddress.PageID = pageID;
                            setupPageMovement(GCTargetAddress, destAddress, cmReq);
                            totalPageMovements++;
                            totalPageMovementsForEmergencyGC++;
                            thisRoundTotalPageMovementsForEmergencyGC++;
                            FTL.PageReadsForGC++;
                            FTL.PageProgramForGC++;
                            FTL.TotalPageProgams++;
                            FTL.TotalPageReads++;
                        }
                    }

                    targetChannel.EmergencyGCRequests.Remove(gcJob);
                    foreach (InternalWriteRequest wr in cmReq.InternalWriteRequestList)
                    {
                        targetChannel.RateControlledQueues.GCReadQueueForPlanes[wr.TargetPageAddress.LocalFlashChipID][wr.TargetPageAddress.DieID][wr.TargetPageAddress.PlaneID].AddLast(wr.UpdateRead);
                        targetChannel.RateControlledQueues.GCWriteQueueForPlanes[wr.TargetPageAddress.LocalFlashChipID][wr.TargetPageAddress.DieID][wr.TargetPageAddress.PlaneID].AddLast(wr);
                        FTL.ResourceAccessTable.ChannelEntries[wr.TargetPageAddress.ChannelID].WaitingGCWriteCount++;
                        FTL.ResourceAccessTable.ChannelEntries[wr.UpdateRead.TargetPageAddress.ChannelID].WaitingGCReadCount++;
                        FTL.ResourceAccessTable.DieEntries[wr.TargetPageAddress.ChannelID][wr.TargetPageAddress.LocalFlashChipID][wr.TargetPageAddress.DieID].WaitingGCWriteCount++;
                        FTL.ResourceAccessTable.DieEntries[wr.UpdateRead.TargetPageAddress.ChannelID][wr.UpdateRead.TargetPageAddress.LocalFlashChipID][wr.UpdateRead.TargetPageAddress.DieID].WaitingGCReadCount++;
                        FTL.ResourceAccessTable.PlaneEntries[wr.TargetPageAddress.ChannelID][wr.TargetPageAddress.LocalFlashChipID][wr.TargetPageAddress.DieID][wr.TargetPageAddress.PlaneID].WaitingGCWriteCount++;
                        FTL.ResourceAccessTable.PlaneEntries[wr.UpdateRead.TargetPageAddress.ChannelID][wr.UpdateRead.TargetPageAddress.LocalFlashChipID][wr.UpdateRead.TargetPageAddress.DieID][wr.TargetPageAddress.PlaneID].WaitingGCReadCount++;
                    }
                    targetChannel.RateControlledQueues.GCEraseQueueForPlanes[cmReq.TargetPageAddress.LocalFlashChipID][cmReq.TargetPageAddress.DieID][cmReq.TargetPageAddress.PlaneID].AddLast(cmReq);
                    (FTL.IOScheduler as IOSchedulerRPB).UpdateGCQueuePriority(GCTargetAddress);
                }
            }
        }
        public void RPBGCFinished(IntegerPageAddress targetPlaneAddress)
        {
            FTL.FlashChips[targetPlaneAddress.OverallFlashChipID].Dies[targetPlaneAddress.DieID].Planes[targetPlaneAddress.PlaneID].HasGCRequest = false;
            FTL.ResourceAccessTable.ChannelEntries[targetPlaneAddress.ChannelID].WaitingGCReqsCount--;
            FTL.ResourceAccessTable.DieEntries[targetPlaneAddress.ChannelID][targetPlaneAddress.LocalFlashChipID][targetPlaneAddress.DieID].WaitingGCReqsCount--;
            FTL.ResourceAccessTable.PlaneEntries[targetPlaneAddress.ChannelID][targetPlaneAddress.LocalFlashChipID][targetPlaneAddress.DieID][targetPlaneAddress.PlaneID].WaitingGCReqsCount--;
            FTL.ResourceAccessTable.PlaneEntries[targetPlaneAddress.ChannelID][targetPlaneAddress.LocalFlashChipID][targetPlaneAddress.DieID][targetPlaneAddress.PlaneID].OnTheFlyErase = null;
            if (FTL.ResourceAccessTable.PlaneEntries[targetPlaneAddress.ChannelID][targetPlaneAddress.LocalFlashChipID][targetPlaneAddress.DieID][targetPlaneAddress.PlaneID].WaitingGCReqsCount == 0)
            {
                (FTL.ChannelInfos[targetPlaneAddress.ChannelID] as BusChannelRPB).RateControlledQueues.GCPriorityClasses[FTL.FlashChips[targetPlaneAddress.OverallFlashChipID].Dies[targetPlaneAddress.DieID].Planes[targetPlaneAddress.PlaneID].GCPriorityClass]
                    .Remove(FTL.FlashChips[targetPlaneAddress.OverallFlashChipID].Dies[targetPlaneAddress.DieID].Planes[targetPlaneAddress.PlaneID].RelatedNodeInGCLinkedList);
                FTL.FlashChips[targetPlaneAddress.OverallFlashChipID].Dies[targetPlaneAddress.DieID].Planes[targetPlaneAddress.PlaneID].RelatedNodeInGCLinkedList = null;
            }
        }
        #endregion

        #region HelperFunctions
        private void SelectCandidateBlockGCTB(IntegerPageAddress targetAddress, uint[] blockIDs)
        {
            FlashChip targetChip = this.FTL.FlashChips[targetAddress.OverallFlashChipID];
            uint startPosition = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);

            switch (Type)
            {
                case GCPolicyType.Greedy:
                {
                    for (uint dieID = 0; dieID < FTL.DieNoPerChip; dieID++)
                    {
                        FlashChipDie targetDie = targetChip.Dies[dieID];
                        uint minEraseCount = uint.MaxValue;
                        uint currentBlockID = 0;
                        uint maxInvalidPageCount = 0;
                        for (uint i = 0; i < FTL.BlockNoPerPlane; i++)
                        {
                            currentBlockID = (startPosition + i) % FTL.BlockNoPerPlane;
                            if (targetDie.CurrentActiveBlockID == currentBlockID)
                                continue;

                            if (targetDie.BlockInfoAbstract[currentBlockID].FreePageNo == 0)
                                if (targetDie.BlockInfoAbstract[currentBlockID].InvalidPageNo > maxInvalidPageCount)
                                {
                                    maxInvalidPageCount = targetDie.BlockInfoAbstract[currentBlockID].InvalidPageNo;
                                    minEraseCount = targetDie.BlockInfoAbstract[currentBlockID].EraseCount;
                                    blockIDs[dieID] = currentBlockID;
                                }
                                else if ((targetDie.BlockInfoAbstract[currentBlockID].InvalidPageNo == maxInvalidPageCount)
                                    && (targetDie.BlockInfoAbstract[currentBlockID].EraseCount < minEraseCount))
                                {
                                    minEraseCount = targetDie.BlockInfoAbstract[currentBlockID].EraseCount;
                                    blockIDs[dieID] = currentBlockID;
                                }
                        }

                        //There were no block group with FreePageNo = 0
                        if (maxInvalidPageCount == 0)
                        {
                            for (uint i = 0; i < FTL.BlockNoPerPlane; i++)
                            {
                                currentBlockID = (startPosition + i) % FTL.BlockNoPerPlane;
                                if (targetDie.CurrentActiveBlockID == currentBlockID)
                                    continue;

                                if (targetDie.BlockInfoAbstract[currentBlockID].InvalidPageNo > maxInvalidPageCount)
                                {
                                    maxInvalidPageCount = targetDie.BlockInfoAbstract[currentBlockID].InvalidPageNo;
                                    minEraseCount = targetDie.BlockInfoAbstract[currentBlockID].EraseCount;
                                    blockIDs[dieID] = currentBlockID;
                                }
                                else if ((targetDie.BlockInfoAbstract[currentBlockID].InvalidPageNo == maxInvalidPageCount)
                                    && (targetDie.BlockInfoAbstract[currentBlockID].EraseCount < minEraseCount))
                                {
                                    minEraseCount = targetDie.BlockInfoAbstract[currentBlockID].EraseCount;
                                    blockIDs[dieID] = currentBlockID;
                                }
                            }
                        }
                    }
                    totalNumberOfComparisonsToFindCandidateBlock += blockNoPerPlane;
                    break;
                    }
                case GCPolicyType.RGA:
                {
                    for (uint dieID = 0; dieID < FTL.DieNoPerChip; dieID++)
                    {
                        uint selectedBlockID = 0;
                        uint[] randomSet = new uint[RGAConstant];
                        uint maxInvalidPageCount = 0;
                        uint minErase = uint.MaxValue;
                        FlashChipDie targetDie = targetChip.Dies[dieID];
                        #region selectAListOfCandidateBlocks
                        for (uint i = 0; i < RGAConstant; i++)
                        {
                            bool loop;
                            do
                            {
                                loop = false;
                                selectedBlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                                totalNumberOfRandomNumberGeneration++;

                                totalNumberOfComparisonToCreateRandomSet++;
                                if ((targetDie.BlockInfoAbstract[selectedBlockID].FreePageNo > 0) || (selectedBlockID == targetDie.CurrentActiveBlockID))
                                    loop = true;
                                else
                                    for (uint j = 0; j < i; j++)
                                    {
                                        totalNumberOfComparisonToCreateRandomSet++;
                                        if (randomSet[j] == selectedBlockID)
                                        {
                                            loop = true;
                                            break;
                                        }
                                    }
                            }
                            while (loop);
                            randomSet[i] = selectedBlockID;
                        }
                        #endregion

                        for (uint i = 0; i < RGAConstant; i++)
                            if (targetDie.BlockInfoAbstract[randomSet[i]].InvalidPageNo > maxInvalidPageCount)
                            {
                                maxInvalidPageCount = targetDie.BlockInfoAbstract[randomSet[i]].InvalidPageNo;
                                minErase = targetDie.BlockInfoAbstract[randomSet[i]].EraseCount;
                                blockIDs[dieID] = randomSet[i];
                            }
                            else if (targetDie.BlockInfoAbstract[randomSet[i]].InvalidPageNo == maxInvalidPageCount)
                            {
                                if (dynamicWearLevelingEnabled)
                                    if (targetDie.BlockInfoAbstract[randomSet[i]].EraseCount < minErase)
                                    {
                                        maxInvalidPageCount = targetDie.BlockInfoAbstract[randomSet[i]].InvalidPageNo;
                                        minErase = targetDie.BlockInfoAbstract[randomSet[i]].EraseCount;
                                        blockIDs[dieID] = randomSet[i];
                                    }
                            }
                        totalNumberOfComparisonsToFindCandidateBlock += RGAConstant;
                    }
                    break;
                }
                default:
                throw new NotImplementedException("This type of GC is not implemented for Twain Block scheme");
            }
        }
        private void SelectCandidateBlock(IntegerPageAddress targetAddress, FlashChipPlane targetPlane)
        {
            int cntr = 0;
            uint maxInvalidPageCount = 0;
            uint minErase = uint.MaxValue;
            uint[] randomSet;//used for randomized selection algorithms
            uint selectedBlockID = 0;
            double avgBlockErase = 0;

            switch (Type)
            {
                #region ProposedGCPolicies
                case GCPolicyType.Proposed1:
                {
                    #region SelectAListOfCandidateBlocks
                    randomSet = new uint[setSizeForProposedAlgorithm];
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        bool loop;
                        do
                        {
                            loop = false;
                            selectedBlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                            totalNumberOfRandomNumberGeneration++;

                            totalNumberOfComparisonToCreateRandomSet++;
                            if ((targetPlane.Blocks[selectedBlockID].FreePageNo > 0) || (selectedBlockID == targetPlane.CurrentActiveBlockID))
                                loop = true;
                            else
                                for (uint j = 0; j < i; j++)
                                {
                                    totalNumberOfComparisonToCreateRandomSet++;
                                    if (randomSet[j] == selectedBlockID)
                                    {
                                        loop = true;
                                        break;
                                    }
                                }
                        }
                        while (loop);
                        randomSet[i] = selectedBlockID;
                    }
                    #endregion
                    #region SearchForCandidate
                    uint maxID = randomSet[0], minID = randomSet[0];
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        if (targetPlane.Blocks[randomSet[i]].InvalidPageNo > targetPlane.Blocks[maxID].InvalidPageNo)
                            maxID = randomSet[i];
                        if (targetPlane.Blocks[randomSet[i]].EraseCount < targetPlane.Blocks[minID].EraseCount)
                            minID = randomSet[i];
                    }
                    if (turn)
                    {
                        if (targetPlane.Blocks[maxID].InvalidPageNo == targetPlane.Blocks[minID].InvalidPageNo)
                            targetAddress.BlockID = minID;
                        else
                            targetAddress.BlockID = maxID;
                    }
                    else
                    {
                        if (targetPlane.Blocks[maxID].EraseCount == targetPlane.Blocks[minID].EraseCount)
                            targetAddress.BlockID = maxID;
                        else
                            targetAddress.BlockID = minID;
                    }
                    totalNumberOfComparisonsToFindCandidateBlock += setSizeForProposedAlgorithm;
                    turn = !turn;
                    #endregion
                    break;
                }
                case GCPolicyType.Proposed2:
                {
                    #region SelectAListOfCandidateBlocks
                    randomSet = new uint[setSizeForProposedAlgorithm];
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        bool loop;
                        do
                        {
                            loop = false;
                            selectedBlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                            totalNumberOfRandomNumberGeneration++;

                            totalNumberOfComparisonToCreateRandomSet++;
                            if ((targetPlane.Blocks[selectedBlockID].FreePageNo > 0) || (selectedBlockID == targetPlane.CurrentActiveBlockID))
                                loop = true;
                            else
                                for (uint j = 0; j < i; j++)
                                {
                                    totalNumberOfComparisonToCreateRandomSet++;
                                    if (randomSet[j] == selectedBlockID)
                                    {
                                        loop = true;
                                        break;
                                    }
                                }
                        }
                        while (loop);
                        randomSet[i] = selectedBlockID;
                        avgBlockErase += targetPlane.Blocks[selectedBlockID].EraseCount;
                    }
                    #endregion
                    #region CalculateStatistics
                    avgBlockErase /= setSizeForProposedAlgorithm;
                    double blockEraseStdDev = 0;
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        totalNumberOfComparisonsToFindCandidateBlock++;
                        blockEraseStdDev += Math.Pow((avgBlockErase - targetPlane.Blocks[randomSet[i]].EraseCount), 2);

                    }
                    blockEraseStdDev = Math.Sqrt(blockEraseStdDev / ((double)setSizeForProposedAlgorithm));
                    #endregion
                    #region SearchForCandidate
                    targetAddress.BlockID = randomSet[0];
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        totalNumberOfComparisonsToFindCandidateBlock++;
                        if (targetPlane.Blocks[randomSet[i]].EraseCount < avgBlockErase)
                            if ((avgBlockErase - targetPlane.Blocks[randomSet[i]].EraseCount) > Math.Pow(Proposed2Parameter, avgBlockErase) * avgBlockErase)
                            {
                                targetAddress.BlockID = randomSet[i];
                                break;
                            }
                        if (targetPlane.Blocks[randomSet[i]].InvalidPageNo > targetPlane.Blocks[targetAddress.BlockID].InvalidPageNo)
                            targetAddress.BlockID = randomSet[i];
                    }
                    #endregion
                    break;
                }
                case GCPolicyType.Proposed3:
                {
                    #region SelectAListOfCandidateBlocks
                    randomSet = new uint[setSizeForProposedAlgorithm];
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        bool loop;
                        do
                        {
                            loop = false;
                            selectedBlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                            totalNumberOfRandomNumberGeneration++;

                            totalNumberOfComparisonToCreateRandomSet++;
                            if ((targetPlane.Blocks[selectedBlockID].FreePageNo > 0) || (selectedBlockID == targetPlane.CurrentActiveBlockID))
                                loop = true;
                            else
                                for (uint j = 0; j < i; j++)
                                {
                                    totalNumberOfComparisonToCreateRandomSet++;
                                    if (randomSet[j] == selectedBlockID)
                                    {
                                        loop = true;
                                        break;
                                    }
                                }
                        }
                        while (loop);
                        randomSet[i] = selectedBlockID;
                        avgBlockErase += targetPlane.Blocks[selectedBlockID].EraseCount;
                    }
                    #endregion
                    avgBlockErase /= setSizeForProposedAlgorithm;
                    #region SearchForCandidate
                    double minCost = double.MaxValue;
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        double cost = (pageNoPerBlock - targetPlane.Blocks[randomSet[i]].FreePageNo - targetPlane.Blocks[randomSet[i]].InvalidPageNo)
                            + (avgBlockErase - targetPlane.Blocks[randomSet[i]].EraseCount);
                        if (cost < minCost)
                        {
                            minCost = cost;
                            targetAddress.BlockID = randomSet[i];
                        }
                        
                    }
                    totalNumberOfComparisonsToFindCandidateBlock += setSizeForProposedAlgorithm;
                    #endregion
                    break;
                }
                case GCPolicyType.Proposed4:
                {
                    #region selectAListOfCandidateBlocks
                    randomSet = new uint[setSizeForProposedAlgorithm];
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        bool loop;
                        do
                        {
                            loop = false;
                            selectedBlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                            totalNumberOfRandomNumberGeneration++;

                            totalNumberOfComparisonToCreateRandomSet++;
                            if ((targetPlane.Blocks[selectedBlockID].FreePageNo > 0) || (selectedBlockID == targetPlane.CurrentActiveBlockID))
                                loop = true;
                            else
                                for (uint j = 0; j < i; j++)
                                {
                                    totalNumberOfComparisonToCreateRandomSet++;
                                    if (randomSet[j] == selectedBlockID)
                                    {
                                        loop = true;
                                        break;
                                    }
                                }
                        }
                        while (loop);
                        randomSet[i] = selectedBlockID;
                        avgBlockErase += targetPlane.Blocks[selectedBlockID].EraseCount;
                    }
                    #endregion
                    avgBlockErase /= setSizeForProposedAlgorithm;
                    double minCost = double.MaxValue;
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        double cost = Math.Pow((pageNoPerBlock - targetPlane.Blocks[randomSet[i]].FreePageNo - targetPlane.Blocks[randomSet[i]].InvalidPageNo), 2)
                            + (avgBlockErase - targetPlane.Blocks[randomSet[i]].EraseCount);
                        if (cost < minCost)
                        {
                            minCost = cost;
                            targetAddress.BlockID = randomSet[i];
                        }

                    }
                    totalNumberOfComparisonsToFindCandidateBlock += setSizeForProposedAlgorithm;
                    break;
                }
                case GCPolicyType.Proposed5:
                {
                    #region selectAListOfCandidateBlocks
                    randomSet = new uint[setSizeForProposedAlgorithm];
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        bool loop;
                        do
                        {
                            loop = false;
                            selectedBlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                            totalNumberOfRandomNumberGeneration++;

                            totalNumberOfComparisonToCreateRandomSet++;
                            if ((targetPlane.Blocks[selectedBlockID].FreePageNo > 0) || (selectedBlockID == targetPlane.CurrentActiveBlockID))
                                loop = true;
                            else
                                for (uint j = 0; j < i; j++)
                                {
                                    totalNumberOfComparisonToCreateRandomSet++;
                                    if (randomSet[j] == selectedBlockID)
                                    {
                                        loop = true;
                                        break;
                                    }
                                }
                        }
                        while (loop);
                        randomSet[i] = selectedBlockID;
                        avgBlockErase += targetPlane.Blocks[selectedBlockID].EraseCount;
                    }
                    #endregion
                    avgBlockErase /= setSizeForProposedAlgorithm;
                    double minCost = double.MaxValue;
                    for (uint i = 0; i < setSizeForProposedAlgorithm; i++)
                    {
                        double cost = 
                            Proposed5a * Math.Pow((pageNoPerBlock - targetPlane.Blocks[randomSet[i]].FreePageNo - targetPlane.Blocks[randomSet[i]].InvalidPageNo), Proposed5P1)
                            + Math.Sign(Proposed5b  * (avgBlockErase - targetPlane.Blocks[randomSet[i]].EraseCount))
                            * Math.Abs(Proposed5b * Math.Pow(avgBlockErase - targetPlane.Blocks[randomSet[i]].EraseCount, Proposed5P2));
                        if (cost < minCost)
                        {
                            minCost = cost;
                            targetAddress.BlockID = randomSet[i];
                        }

                    }
                    totalNumberOfComparisonsToFindCandidateBlock += setSizeForProposedAlgorithm;
                    break;
                }
                #endregion
                case GCPolicyType.Greedy:
                {
                    uint startPosition = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1); ;
                    uint currentBlockID = 0;
                    for (uint i = 0; i < FTL.BlockNoPerPlane; i++)
                    {
                        currentBlockID = (startPosition + i) % FTL.BlockNoPerPlane;
                        if (currentBlockID == targetPlane.CurrentActiveBlockID)
                            continue;
                        if (targetPlane.Blocks[currentBlockID].InvalidPageNo > maxInvalidPageCount)
                        {
                            maxInvalidPageCount = targetPlane.Blocks[currentBlockID].InvalidPageNo;
                            minErase = targetPlane.Blocks[currentBlockID].EraseCount;
                            targetAddress.BlockID = currentBlockID;
                        }
                        else if (targetPlane.Blocks[currentBlockID].InvalidPageNo == maxInvalidPageCount)
                        {
                            if (dynamicWearLevelingEnabled)
                                if (targetPlane.Blocks[currentBlockID].EraseCount < minErase)
                                {
                                    maxInvalidPageCount = targetPlane.Blocks[currentBlockID].InvalidPageNo;
                                    minErase = targetPlane.Blocks[currentBlockID].EraseCount;
                                    targetAddress.BlockID = currentBlockID;
                                }
                        }
                    }
                    totalNumberOfComparisonsToFindCandidateBlock += blockNoPerPlane;
                    break;
                }
                case GCPolicyType.GreedyUnfair:
                {
                    uint currentblock = LastGreedyStartPosition;
                    for (uint i = 0; i < blockNoPerPlane; i++)
                    {
                        currentblock = (currentblock + i) % blockNoPerPlane;
                        if (currentblock == targetPlane.CurrentActiveBlockID)
                            continue;
                        if (targetPlane.Blocks[currentblock].InvalidPageNo > maxInvalidPageCount)
                        {
                            maxInvalidPageCount = targetPlane.Blocks[currentblock].InvalidPageNo;
                            minErase = targetPlane.Blocks[currentblock].EraseCount;
                            targetAddress.BlockID = currentblock;
                        }
                        else if (targetPlane.Blocks[currentblock].InvalidPageNo == maxInvalidPageCount)
                        {
                            if (dynamicWearLevelingEnabled)
                                if (targetPlane.Blocks[currentblock].EraseCount < minErase)
                                {
                                    maxInvalidPageCount = targetPlane.Blocks[currentblock].InvalidPageNo;
                                    minErase = targetPlane.Blocks[currentblock].EraseCount;
                                    targetAddress.BlockID = currentblock;
                                }
                        }
                    }
                    totalNumberOfComparisonsToFindCandidateBlock += blockNoPerPlane;
                    LastGreedyStartPosition = (LastGreedyStartPosition + 1) % blockNoPerPlane;
                    break;
                }
                case GCPolicyType.RGA:
                    #region selectAListOfCandidateBlocks
                    randomSet = new uint[RGAConstant];
                    for (uint i = 0; i < RGAConstant; i++)
                    {
                        bool loop;
                        do
                        {
                            loop = false;
                            selectedBlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                            totalNumberOfRandomNumberGeneration++;

                            totalNumberOfComparisonToCreateRandomSet++;
                            if ((targetPlane.Blocks[selectedBlockID].FreePageNo > 0) || (selectedBlockID == targetPlane.CurrentActiveBlockID))
                                loop = true;
                            else
                                for (uint j = 0; j < i; j++)
                                {
                                    totalNumberOfComparisonToCreateRandomSet++;
                                    if (randomSet[j] == selectedBlockID)
                                    {
                                        loop = true;
                                        break;
                                    }
                                }
                        }
                        while (loop);
                        randomSet[i] = selectedBlockID;
                    }
                    #endregion
                    for (uint i = 0; i < RGAConstant; i++)
                        if (targetPlane.Blocks[randomSet[i]].InvalidPageNo > maxInvalidPageCount)
                        {
                            maxInvalidPageCount = targetPlane.Blocks[randomSet[i]].InvalidPageNo;
                            minErase = targetPlane.Blocks[randomSet[i]].EraseCount;
                            targetAddress.BlockID = randomSet[i];
                        }
                        else if (targetPlane.Blocks[randomSet[i]].InvalidPageNo == maxInvalidPageCount)
                        {
                            if (dynamicWearLevelingEnabled)
                                if (targetPlane.Blocks[randomSet[i]].EraseCount < minErase)
                                {
                                    maxInvalidPageCount = targetPlane.Blocks[randomSet[i]].InvalidPageNo;
                                    minErase = targetPlane.Blocks[randomSet[i]].EraseCount;
                                    targetAddress.BlockID = randomSet[i];
                                }
                        }
                    totalNumberOfComparisonsToFindCandidateBlock += RGAConstant;
                    break;
                case GCPolicyType.Random:
                    do
                    {
                        targetAddress.BlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                        totalNumberOfRandomNumberGeneration++;
                        totalNumberOfComparisonToCreateRandomSet++;
                        cntr++;
                    } while ((targetPlane.Blocks[targetAddress.BlockID].FreePageNo > 0 || targetAddress.BlockID == targetPlane.CurrentActiveBlockID) && cntr < FTL.BlockNoPerPlane);
                    break;
                case GCPolicyType.RandomPlus:
                    do
                    {
                        targetAddress.BlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                        totalNumberOfRandomNumberGeneration++;
                        totalNumberOfComparisonToCreateRandomSet++;
                        cntr++;
                    } while ((targetPlane.Blocks[targetAddress.BlockID].FreePageNo > 0 || targetPlane.Blocks[targetAddress.BlockID].InvalidPageNo == 0 || targetAddress.BlockID == targetPlane.CurrentActiveBlockID) && cntr < FTL.BlockNoPerPlane);
                    break;
                case GCPolicyType.RandomPlusPlus:
                    do
                    {
                        targetAddress.BlockID = randomBlockGenerator.UniformUInt(0, blockNoPerPlane - 1);
                        totalNumberOfRandomNumberGeneration++;
                        totalNumberOfComparisonToCreateRandomSet++;
                        cntr++;
                    } while ((targetPlane.Blocks[targetAddress.BlockID].InvalidPageNo < randomPlusPlusThreshold || targetAddress.BlockID == targetPlane.CurrentActiveBlockID) && cntr < FTL.BlockNoPerPlane);
                    break;
                case GCPolicyType.WindowedGreedy:
                    if (targetPlane.BlockUsageListHead == null)
                        break;
                    FlashChipBlock blockWithMaximumInvalids = targetPlane.BlockUsageListHead, itsPrevious = null;
                    FlashChipBlock iterator = targetPlane.BlockUsageListHead, tempPrevious = null;
                    uint windowCntr = 1;
                        
                    tempPrevious = iterator; iterator = iterator.Next;
                    while (iterator != null && windowCntr < WGreedyWindowSize)
                    {
                        if (iterator.InvalidPageNo > blockWithMaximumInvalids.InvalidPageNo)
                        {
                            blockWithMaximumInvalids = iterator;
                            itsPrevious = tempPrevious;
                        }
                        tempPrevious = iterator;
                        iterator = iterator.Next;
                        windowCntr++;
                    }
                    if (blockWithMaximumInvalids != null)
                    {
                        targetAddress.BlockID = blockWithMaximumInvalids.BlockID;
                        if (blockWithMaximumInvalids == targetPlane.BlockUsageListHead)
                            targetPlane.BlockUsageListHead = blockWithMaximumInvalids.Next;
                        else
                        {
                            if (blockWithMaximumInvalids == targetPlane.BlockUsageListTail)
                            {
                                targetPlane.BlockUsageListTail = itsPrevious;
                                itsPrevious.Next = null;
                            }
                            else itsPrevious.Next = blockWithMaximumInvalids.Next;
                        }
                        blockWithMaximumInvalids.Next = null;
                    }
                    totalNumberOfComparisonsToFindCandidateBlock += WGreedyWindowSize;
                    break;
                case GCPolicyType.FIFO:
                    FlashChipBlock currentHead = targetPlane.BlockUsageListHead;
                    if (currentHead == null)
                        break;
                    targetAddress.BlockID = targetPlane.BlockUsageListHead.BlockID;
                    targetPlane.BlockUsageListHead = targetPlane.BlockUsageListHead.Next;
                    if (targetPlane.BlockUsageListHead == null)
                        targetPlane.BlockUsageListTail = null;
                    currentHead.Next = null;
                    break;
                case GCPolicyType.MS:
                default:
                    throw new Exception("Unhandled GCPolicyType!");
            }
        }
        /// <summary>
        /// This functions is invoked by FCC to perform page movement when command arrives at target flash chip
        /// </summary>
        /// <param name="sourceAddress"></param>
        /// <param name="cmReq"></param>
        private void setupPageMovement(IntegerPageAddress sourceAddress, IntegerPageAddress destAddress, InternalCleanRequest cmReq)
        {
            FlashChipPlane targetPlane = FTL.FlashChips[sourceAddress.OverallFlashChipID].Dies[sourceAddress.DieID].Planes[sourceAddress.PlaneID];

            ulong oldPPN = FTL.AddressMapper.ConvertPageAddressToPPN(sourceAddress);
            FlashChipPage srcPage = targetPlane.Blocks[sourceAddress.BlockID].Pages[sourceAddress.PageID];

            ulong newPPN = FTL.AllocatePPNInPlaneForGC(srcPage, sourceAddress, destAddress);
            FlashChipPage destPage = targetPlane.Blocks[destAddress.BlockID].Pages[destAddress.PageID];
            bool isCopyBack = false;

            if (copyBackCommandEnabled)
            {
                if (copybackOddEvenConstraint)
                {
                    if ((oldPPN % 2) != (newPPN % 2))
                        cmReq.NormalPageMovementCount++;
                    else
                    {
                        cmReq.CopyBackPageMovementCount++;
                        isCopyBack = true;
                    }
                }
                else
                {
                    cmReq.CopyBackPageMovementCount++;
                    isCopyBack = true;
                }
            }//if (FTL.CopyBackCommandEnabled)
            else
                cmReq.NormalPageMovementCount++;

            destPage.LPN = srcPage.LPN;
            destPage.ValidStatus = srcPage.ValidStatus;
            destPage.StreamID = srcPage.StreamID;

            srcPage.StreamID = FlashChipPage.PG_NOSTREAM;
            srcPage.ValidStatus = FlashChipPage.PG_INVALID;
            srcPage.LPN = 0;

            //performance
            /* if (oldPPN != FTL.AddressMapper.AddressMappingDomains[FTL.AddressMapper.GetStreamID(destAddress)].MappingTable.PPN[destPage.LPN])
             {
                 Console.WriteLine("Error in garbage collection function!");
                 Console.ReadLine();
             }*/

            //FTL.AddressMapper.AddressMappingDomains[FTL.AddressMapper.GetStreamID(destAddress)].MappingTable.PPN[destPage.LPN] = newPPN;

            InternalWriteRequest writeReq = new InternalWriteRequest();
            InternalReadRequest update = new InternalReadRequest();
            update.TargetPageAddress = new IntegerPageAddress(sourceAddress);
            update.TargetFlashChip = FTL.FlashChips[destAddress.OverallFlashChipID];
            update.LPN = destPage.LPN;
            update.PPN = oldPPN;
            update.State = destPage.ValidStatus;
            update.SizeInSubpages = FTL.SizeInSubpages(update.State);
            update.SizeInByte = update.SizeInSubpages * FTL.SubPageCapacity;
            update.BodyTransferCycles = update.SizeInByte / FTL.ChannelWidthInByte;
            update.Type = InternalRequestType.Read;
            update.RelatedWrite = writeReq;
            update.RelatedIORequest = null;
            update.RelatedCMReq = cmReq;
            update.IsUpdate = true;
            update.IsForGC = true;

            writeReq.TargetPageAddress = new IntegerPageAddress(destAddress);
            writeReq.TargetFlashChip = FTL.FlashChips[destAddress.OverallFlashChipID];
            writeReq.UpdateRead = update;
            writeReq.LPN = update.LPN;
            writeReq.PPN = newPPN;
            writeReq.State = update.State;
            writeReq.SizeInSubpages = update.SizeInSubpages;
            writeReq.SizeInByte = update.SizeInByte;
            writeReq.BodyTransferCycles = update.BodyTransferCycles;
            writeReq.Type = InternalRequestType.Write;
            writeReq.RelatedIORequest = null;
            writeReq.RelatedCMReq = cmReq;
            writeReq.IsForGC = true;
            if (isCopyBack)
            {
                update.ExecutionType = InternalRequestExecutionType.Copyback;
                writeReq.ExecutionType = InternalRequestExecutionType.Copyback;
            }
            cmReq.InternalWriteRequestList.AddLast(writeReq);
        }
        /// <summary>
        /// Performs GC page movement between.
        /// </summary>
        /// <param name="cmReq"></param>
        /// <param name="targetChannel"></param>
        private void movePage(InternalCleanRequest cmReq, BusChannelBase targetChannel)
        {
            if (cmReq.InternalWriteRequestList.First.Value.UpdateRead != null)
            {
                if (cmReq.InternalWriteRequestList.First.Value.ExecutionType == InternalRequestExecutionType.Copyback)
                    FTL.FCC.SendSimpleCommandToChip(cmReq.InternalWriteRequestList.First.Value);//Handle read/write pair using copyback command
                else
                    FTL.FCC.SendSimpleCommandToChip(cmReq.InternalWriteRequestList.First.Value.UpdateRead);//Handle related read command
            }
            else
                FTL.FCC.SendSimpleCommandToChip(cmReq.InternalWriteRequestList.First.Value);
        }
        /// <summary>
        /// This function was originally provided in SSDSim. Direct erase is a special implementation of greedy GC in which 
        /// a candidate block is selected from the queue of blocks that just include invalid pages. The selection policy is FIFO.
        /// </summary>
        /// <param name="targetAddress">The address of the plane which has a GC request.</param>
        private void DirectErase(IntegerPageAddress targetAddress)
        {
            bool interleaverFlag = false, multiplaneFlag = false;
            FlashChipDie targetDie = FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID];
            FlashChipPlane targetPlane = targetDie.Planes[targetAddress.PlaneID];
            uint targetBlockID = targetPlane.DirectEraseNodes.First.Value;
            InternalCleanRequestLinkedList executionReqList = new InternalCleanRequestLinkedList();


            if (FTL.MultiplaneCMDEnabled)
            {
                for (int planeCntr = 0; planeCntr < FTL.PlaneNoPerDie; planeCntr++)
                    if (planeCntr == targetAddress.PlaneID)
                        continue;
                    else if ((targetDie.Planes[planeCntr].DirectEraseNodes.Count > 0) && (targetDie.Planes[planeCntr].DirectEraseNodes.First.Value == targetBlockID))
                    {
                        multiplaneFlag = true;
                        break;
                    }
            }

            if (FTL.InterleavedCMDEnabled)
            {
                for (int dieCntr = 0; dieCntr < FTL.DieNoPerChip; dieCntr++)
                {
                    if (dieCntr != targetAddress.DieID)
                        for (int planeCntr = 0; planeCntr < FTL.PlaneNoPerDie; planeCntr++)
                            if (FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[dieCntr].Planes[planeCntr].DirectEraseNodes.Count > 0)
                            {
                                interleaverFlag = true;
                                break;
                            }
                    if (interleaverFlag)
                        break;
                }
            }


            if (multiplaneFlag && interleaverFlag)
            {
                bool tempFlag = false;
                for (uint dieCntr = 0; dieCntr < FTL.DieNoPerChip; dieCntr++)
                {
                    uint firstBlock = uint.MaxValue;
                    targetDie = FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[dieCntr];
                    targetAddress.DieID = dieCntr;
                    for (uint planeCntr = 0; planeCntr < FTL.PlaneNoPerDie; planeCntr++)
                    {
                        targetAddress.PlaneID = planeCntr;
                        if (FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[dieCntr].Planes[planeCntr].DirectEraseNodes.Count > 0)
                        {
                            if (firstBlock == uint.MaxValue)
                            {
                                FTL.TotalBlockErases++;
                                targetAddress.BlockID = targetDie.Planes[planeCntr].DirectEraseNodes.First.Value;
                                InternalCleanRequest iReq = new InternalCleanRequest(InternalRequestType.Clean, targetAddress, false, false);
                                iReq.ExecutionType = InternalRequestExecutionType.Interleaved;
                                executionReqList.AddLast(iReq);
                                targetDie.Planes[planeCntr].DirectEraseNodes.RemoveFirst();
                                firstBlock = targetAddress.BlockID;
                            }
                            else if (targetDie.Planes[planeCntr].DirectEraseNodes.First.Value == firstBlock)
                            {
                                FTL.TotalBlockErases++;
                                tempFlag = true;
                                InternalCleanRequest iReq = new InternalCleanRequest(InternalRequestType.Clean, targetAddress, false, false);
                                iReq.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                executionReqList.AddLast(iReq);
                                targetDie.Planes[planeCntr].DirectEraseNodes.RemoveFirst();
                            }
                        }
                    }
                }
                if (tempFlag)
                {
                    InternalCleanRequestLinkedList executionList2 = new InternalCleanRequestLinkedList();
                    for (int dieCntr = 0; dieCntr < FTL.DieNoPerChip; dieCntr++)
                        for (var cleanReq = executionReqList.First; cleanReq != null; cleanReq = cleanReq.Next)
                        {
                            if (cleanReq.Value.TargetPageAddress.DieID == dieCntr)
                            {
                                cleanReq.Value.ExecutionType = InternalRequestExecutionType.InterleavedMultiplane;
                                executionList2.AddLast(cleanReq.Value);
                            }
                        }
                    executionReqList = executionList2;
                }
                DirectEraseExecution += (ulong)executionReqList.Count;
                FTL.FCC.SendAdvCommandToChipER(executionReqList);
            }
            else if (interleaverFlag)
            {
                for (uint dieCntr = 0; dieCntr < FTL.DieNoPerChip; dieCntr++)
                    for (uint planeCntr = 0; planeCntr < FTL.PlaneNoPerDie; planeCntr++)
                    {
                        if (FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[dieCntr].Planes[planeCntr].DirectEraseNodes.Count > 0)
                        {
                            FTL.TotalBlockErases++;
                            targetAddress.DieID = dieCntr;
                            targetAddress.PlaneID = planeCntr;
                            targetAddress.BlockID = FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[dieCntr].Planes[planeCntr].DirectEraseNodes.First.Value;
                            InternalCleanRequest iReq = new InternalCleanRequest(InternalRequestType.Clean, targetAddress, false, false);
                            iReq.ExecutionType = InternalRequestExecutionType.Interleaved;
                            executionReqList.AddLast(iReq);
                            FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[dieCntr].Planes[planeCntr].DirectEraseNodes.RemoveFirst();
                            break;
                        }
                    }
                DirectEraseExecution += (ulong)executionReqList.Count;
                FTL.FCC.SendAdvCommandToChipER(executionReqList);
            }
            else if (multiplaneFlag)
            {
                for (uint planeCntr = 0; planeCntr < FTL.PlaneNoPerDie; planeCntr++)
                    if (FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[planeCntr].DirectEraseNodes.Count > 0)
                    {
                        if (FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[planeCntr].DirectEraseNodes.First.Value == targetBlockID)//check same block address in neighbor planes
                        {
                            FTL.TotalBlockErases++;
                            InternalCleanRequest iReq = new InternalCleanRequest(InternalRequestType.Clean, targetAddress.ChannelID,
                                targetAddress.LocalFlashChipID, targetAddress.DieID, planeCntr, targetBlockID, 0,
                                targetAddress.OverallFlashChipID, false, false);
                            iReq.ExecutionType = InternalRequestExecutionType.Multiplane;
                            executionReqList.AddLast(iReq);
                            FTL.FlashChips[targetAddress.OverallFlashChipID].Dies[targetAddress.DieID].Planes[planeCntr].DirectEraseNodes.RemoveFirst();
                        }
                    }
                DirectEraseExecution += (ulong)executionReqList.Count;
                FTL.FCC.SendAdvCommandToChipER(executionReqList);
            }
            else
            {
                FTL.TotalBlockErases++;
                targetAddress.BlockID = targetPlane.DirectEraseNodes.First.Value;
                InternalCleanRequest iReq = new InternalCleanRequest(InternalRequestType.Clean, targetAddress, false, false);
                targetPlane.DirectEraseNodes.RemoveFirst();
                DirectEraseExecution++;
                FTL.FCC.SendSimpleCommandToChip(iReq);
            }
        }
        #endregion

        #region Properties
        protected void UpdateStatistics()
        {
            if (!erasureStatisticsUpdated)
                return;
            ulong totalBlockEraseCount = 0, totalValidPagesCount = 0, totalInvalidPagesCount = 0;
            ulong totalBlockNo = FTL.TotalChipNo * FTL.DieNoPerChip * FTL.PlaneNoPerDie * FTL.BlockNoPerPlane;

            ulong[] frequencyCountForValidPages = new ulong[pageNoPerBlock + 1];
            ulong[] frequencyCountForInvalidPages = new ulong[pageNoPerBlock + 1];

            for (int i = 0; i < pageNoPerBlock + 1; i++)
            {
                frequencyCountForValidPages[i] = 0;
                frequencyCountForInvalidPages[i] = 0;
            }

            for (int rowCntr = 0; rowCntr < FTL.ChannelCount; rowCntr++)
                for (int chipCntr = 0; chipCntr < FTL.ChipNoPerChannel; chipCntr++)
                    for (int dieCntr = 0; dieCntr < FTL.DieNoPerChip; dieCntr++)
                    {
                        for (uint planeCntr = 0; planeCntr < FTL.PlaneNoPerDie; planeCntr++)
                        {
                            for (uint blockCntr = 0; blockCntr < FTL.BlockNoPerPlane; blockCntr++)
                            {
                                FlashChipBlock targetBlock = FTL.ChannelInfos[rowCntr].FlashChips[chipCntr].Dies[dieCntr].Planes[planeCntr].Blocks[blockCntr];
                                totalBlockEraseCount += targetBlock.EraseCount;
                                if (targetBlock.EraseCount > blockEraseCountMax)
                                    blockEraseCountMax = targetBlock.EraseCount;
                                if (targetBlock.EraseCount < blockEraseCountMin)
                                    blockEraseCountMin = targetBlock.EraseCount;

                                uint validpagecount = pageNoPerBlock - targetBlock.InvalidPageNo - targetBlock.FreePageNo;
                                totalValidPagesCount += validpagecount;
                                if (validpagecount > maxBlockValidPagesCount)
                                    maxBlockValidPagesCount = validpagecount;
                                if (validpagecount < minBlockValidPagesCount)
                                    minBlockValidPagesCount = validpagecount;
                                frequencyCountForValidPages[validpagecount]++;

                                totalInvalidPagesCount += targetBlock.InvalidPageNo;
                                if (targetBlock.InvalidPageNo > maxBlockInvalidPagesCount)
                                    maxBlockInvalidPagesCount = targetBlock.InvalidPageNo;
                                if (targetBlock.InvalidPageNo < minBlockInvalidPagesCount)
                                    minBlockInvalidPagesCount = targetBlock.InvalidPageNo;
                                frequencyCountForInvalidPages[targetBlock.InvalidPageNo]++;
                            }
                        }
                    }
            averageBlockEraseCount = (double)totalBlockEraseCount / (double)(totalBlockNo);
            averageBlockValidPagesCount = (double)totalValidPagesCount / (double)(totalBlockNo);
            averageBlockInvalidPagesCount = (double)totalInvalidPagesCount / (double)(totalBlockNo);
            
            for (int rowCntr = 0; rowCntr < FTL.ChannelCount; rowCntr++)
                for (int chipCntr = 0; chipCntr < FTL.ChipNoPerChannel; chipCntr++)
                    for (int dieCntr = 0; dieCntr < FTL.DieNoPerChip; dieCntr++)
                    {
                        for (uint planeCntr = 0; planeCntr < FTL.PlaneNoPerDie; planeCntr++)
                        {
                            for (uint blockCntr = 0; blockCntr < FTL.BlockNoPerPlane; blockCntr++)
                            {
                                FlashChipBlock currentBlock = FTL.ChannelInfos[rowCntr].FlashChips[chipCntr].Dies[dieCntr].Planes[planeCntr].Blocks[blockCntr];
                                uint validPagesCount = pageNoPerBlock - currentBlock.FreePageNo - currentBlock.InvalidPageNo;
                                blockEraseStdDev += Math.Pow((averageBlockEraseCount - currentBlock.EraseCount), 2);
                                blockValidPagesCountStdDev += Math.Pow((averageBlockValidPagesCount - validPagesCount), 2);
                                blockInvalidPagesCountStdDev += Math.Pow((averageBlockInvalidPagesCount - currentBlock.InvalidPageNo), 2);
                            }
                        }
                    }
            blockEraseStdDev = Math.Sqrt(blockEraseStdDev / ((double)totalBlockNo));
            blockValidPagesCountStdDev = Math.Sqrt(blockValidPagesCountStdDev / ((double)totalBlockNo));
            blockInvalidPagesCountStdDev = Math.Sqrt(blockInvalidPagesCountStdDev / ((double)totalBlockNo));

            for (int i = 0; i < pageNoPerBlock + 1; i++)
            {
                validPageDistributionOnBlocksPDF[i] = (double)frequencyCountForValidPages[i] / (double)totalBlockNo;
                invalidPageDistributionOnBlocksPDF[i] = (double)frequencyCountForInvalidPages[i] / (double)totalBlockNo;
                selectionFunctionPDF[i] = (TotalGCExecutionCount == 0 ? 0 : (double)selectionFrequency[i] / (double)TotalGCExecutionCount);
            }
            wearLevelingFairness = 0;
            for (int i = 0; i < pageNoPerBlock + 1; i++)
            {
                if (validPageDistributionOnBlocksPDF[i] != 0)
                    wearLevelingFairness += (selectionFunctionPDF[i] * selectionFunctionPDF[i]
                    / validPageDistributionOnBlocksPDF[i]);
            }
            wearLevelingFairness = 1 / wearLevelingFairness;
            erasureStatisticsUpdated = false;
        }
        public override void Snapshot(string id, System.Xml.XmlTextWriter writer)
        {
            writer.WriteStartElement(id + "_Statistics");
            writer.WriteAttributeString("ID", ID.ToString());
            writer.WriteAttributeString("DirectEraseExecution", DirectEraseExecution.ToString());//Directly erased blocks, without any page movement. This may occur in either EGC or BGC processes.
            writer.WriteAttributeString("TotalPageMovements", this.totalPageMovements.ToString());
            writer.WriteAttributeString("WriteAmplification", this.WriteAmplification.ToString());
            writer.WriteAttributeString("WearLevelingFairness", this.WearLevelingFairness.ToString());
            writer.WriteAttributeString("AverageCost", this.AverageGCCost.ToString());
            writer.WriteAttributeString("BlockEraseCountAverage", this.BlockEraseCountAverage.ToString());
            writer.WriteAttributeString("BlockEraseCountStdDev", this.BlockEraseCountStdDev.ToString());
            writer.WriteAttributeString("BlockEraseCountMin", this.BlockEraseCountMin.ToString());
            writer.WriteAttributeString("BlockEraseCountMax", this.BlockEraseCountMax.ToString());
            writer.WriteAttributeString("BlockValidPagesCountAverage", this.BlockValidPagesCountAverage.ToString());
            writer.WriteAttributeString("BlockValidPagesCountStdDev", this.BlockValidPagesCountStdDev.ToString());
            writer.WriteAttributeString("BlockValidPagesCountMin", this.BlockValidPagesCountMin.ToString());
            writer.WriteAttributeString("BlockValidPagesCountMax", this.BlockValidPagesCountMax.ToString());
            writer.WriteAttributeString("BlockInvalidPagesCountAverage", this.BlockInvalidPagesCountAverage.ToString());
            writer.WriteAttributeString("BlockInvalidPagesCountStdDev", this.BlockInvalidPagesCountStdDev.ToString());
            writer.WriteAttributeString("BlockInvalidPagesCountMin", this.BlockInvalidPagesCountMin.ToString());
            writer.WriteAttributeString("BlockInvalidPagesCountMax", this.BlockInvalidPagesCountMax.ToString());
            writer.WriteAttributeString("EmergencyGCRequestsCount", this.EmergencyGCRequests.ToString());
            writer.WriteAttributeString("EmergencyGCExecutionCount", this.EmergencyGCExecutionCount.ToString());
            writer.WriteAttributeString("EmergencyGCAvgCost", this.AverageEmergencyGCCost.ToString());
            writer.WriteAttributeString("EmergencyGCAvgTriggerInterval", this.AverageEmergencyGCTriggerInterval.ToString());
            //writer.WriteAttributeString("AverageNoOfComparisonsToFindCandidateBlock", this.AverageNoOfComparisonsToFindCandidateBlock.ToString());
            //writer.WriteAttributeString("AverageNoOfComparisonsToCreateRandomSet", this.AverageNoOfComparisonsToCreateRandomSet.ToString());
            //writer.WriteAttributeString("AverageNoOfRandomNumberGenerationToCreateRandomSet", this.AverageNoOfRandomNumberGenerationToCreateRandomSet.ToString());

            uint totalPlaneCount = FTL.ChannelCount * FTL.ChipNoPerChannel * FTL.DieNoPerChip * FTL.PlaneNoPerDie;

            for (int i = 0; i < pageNoPerBlock + 1; i++)
            {
                writer.WriteStartElement("ValidPageDistributionPDF");
                writer.WriteAttributeString("Count", i.ToString());
                writer.WriteAttributeString("Probability", ValidPageDistributionOnBlocksPDF[i].ToString());
                writer.WriteEndElement();
            }
            for (int i = 0; i < pageNoPerBlock + 1; i++)
            {
                writer.WriteStartElement("InvalidPageDistributionPDF");
                writer.WriteAttributeString("Count", i.ToString());
                writer.WriteAttributeString("Probability", InvalidPageDistributionOnBlocksPDF[i].ToString());
                writer.WriteEndElement();
            }
            for (int i = 0; i < pageNoPerBlock + 1; i++)
            {
                writer.WriteStartElement("SelectionFunctionPDF");
                writer.WriteAttributeString("Count", i.ToString());
                writer.WriteAttributeString("Probability", SelectionFunctionPDF[i].ToString());
                writer.WriteEndElement();
            }
            writer.WriteEndElement();

            if (LoggingEnabled)
            {
                GCLogFile.Close();
                /*for (int i = 0; i < FTL.TotalChipNo; i++)
                    GCLogSpecialFiles[i].Close();*/
            }
        }

        public void ResetTemporaryLoggingVariables()
        {
            this.ThisRoundEmergencyGCExecutionCount = 0; 
            this.thisRoundSumOfEmergencyGCArrivalTime = 0;
            this.thisRoundTotalPageMovementsForEmergencyGC = 0;
        }

        public double WriteAmplification
        {
            get
            {
                UpdateStatistics();
                return (FTL.PageProgramsForWorkload == 0 ? 0 : (double)(FTL.PageProgramsForWorkload + FTL.PageProgramForGC) / (double)FTL.PageProgramsForWorkload);
            }
        }
        public double WearLevelingFairness
        {
            get
            {
                UpdateStatistics();
                return wearLevelingFairness;
            }
        }
        public double AgingRate
        {
            get
            {
                return ((double)FTL.TotalBlockErases / (double)FTL.PageProgramsForWorkload);
            }
        }
        public double BlockEraseCountAverage
        {
            get
            {
                UpdateStatistics();
                return averageBlockEraseCount;
            }
        }
        public double BlockEraseCountStdDev
        {
            get
            {
                UpdateStatistics();
                return blockEraseStdDev;
            }
        }
        public ulong BlockEraseCountMax
        {
            get
            {
                UpdateStatistics();
                return this.blockEraseCountMax;
            }
        }
        public ulong BlockEraseCountMin
        {
            get
            {
                UpdateStatistics();
                return this.blockEraseCountMin;
            }
        }
        public double[] SelectionFunctionPDF
        {
            get
            {
                UpdateStatistics();
                return this.selectionFunctionPDF;
            }
        }
        //For a sample block size of (p) this property give the discrete probability distribution
        //function for number of valid pages per block.
        public double[] ValidPageDistributionOnBlocksPDF
        {
            get
            {
                UpdateStatistics();
                return this.validPageDistributionOnBlocksPDF;
            }
        }
        public double BlockValidPagesCountAverage
        {
            get
            {
                UpdateStatistics();
                return this.averageBlockValidPagesCount;
            }
        }
        public double BlockValidPagesCountStdDev
        {
            get
            {
                UpdateStatistics();
                return this.blockValidPagesCountStdDev;
            }
        }
        public uint BlockValidPagesCountMax
        {
            get
            {
                UpdateStatistics();
                return this.maxBlockValidPagesCount;
            }
        }
        public uint BlockValidPagesCountMin
        {
            get
            {
                UpdateStatistics();
                return this.minBlockValidPagesCount;
            }
        }
        public double[] InvalidPageDistributionOnBlocksPDF
        {
            get
            {
                UpdateStatistics();
                return this.invalidPageDistributionOnBlocksPDF;
            }
        }
        public double BlockInvalidPagesCountAverage
        {
            get
            {
                UpdateStatistics();
                return this.averageBlockInvalidPagesCount;
            }
        }
        public double BlockInvalidPagesCountStdDev
        {
            get
            {
                UpdateStatistics();
                return this.blockInvalidPagesCountStdDev;
            }
        }
        public uint BlockInvalidPagesCountMax
        {
            get
            {
                UpdateStatistics();
                return this.maxBlockInvalidPagesCount;
            }
        }
        public uint BlockInvalidPagesCountMin
        {
            get
            {
                UpdateStatistics();
                return this.minBlockInvalidPagesCount;
            }
        }
        
        public double AverageNoOfComparisonsToFindCandidateBlock
        {
            get { return (TotalGCExecutionCount == 0 ? 0 : (double)totalNumberOfComparisonsToFindCandidateBlock / (double)TotalGCExecutionCount); }
        }
        public double AverageNoOfComparisonsToCreateRandomSet
        {
            get { return (TotalGCExecutionCount == 0 ? 0 : (double) totalNumberOfComparisonToCreateRandomSet / (double)TotalGCExecutionCount); }
        }
        public double AverageNoOfRandomNumberGenerationToCreateRandomSet
        {
            get { return (TotalGCExecutionCount == 0 ? 0 : (double)totalNumberOfRandomNumberGeneration / (double)TotalGCExecutionCount); }
        }
        public double AverageGCCost
        {
            get { return  (TotalGCExecutionCount == 0 ? 0 : (double)totalPageMovements / (double)TotalGCExecutionCount); }
        }
        public double AverageEmergencyGCCost
        {
            get { return (EmergencyGCExecutionCount == 0 ? 0 : (double)totalPageMovementsForEmergencyGC / (double)EmergencyGCExecutionCount); }
        }
        public double ThisRoundAverageEmergencyGCCost
        {
            get { return (ThisRoundEmergencyGCExecutionCount == 0 ? 0 : (double)thisRoundTotalPageMovementsForEmergencyGC / (double)ThisRoundEmergencyGCExecutionCount); }
        }
        public double AverageEmergencyGCTriggerInterval
        {
            get { return (EmergencyGCExecutionCount == 0 ? 0 : (double)sumOfEmergencyGCArrivalTime / (double)EmergencyGCExecutionCount); }
        }
        public double ThisRoundAverageEmergencyGCTriggerInterval//used for logging purposes, when replay is used
        {
            get { return (ThisRoundEmergencyGCExecutionCount == 0 ? 0 : (double)thisRoundSumOfEmergencyGCArrivalTime / (double)ThisRoundEmergencyGCExecutionCount); }
        }
        //gc/second
        public double AverageEmergencyGCRate
        {
            get { return (AverageEmergencyGCTriggerInterval == 0 ? 0 : 1000000000 / AverageEmergencyGCTriggerInterval); }
        }
        //gc/second
        public double ThisRoundAverageEmergencyGCRate
        {
            get { return (ThisRoundAverageEmergencyGCTriggerInterval == 0 ? 0 : 1000000000 / ThisRoundAverageEmergencyGCTriggerInterval); }
        }
        //gc/second
        public double FisrtEmergencyGCRelativeStartPoint
        {
            get { return (double)firstEmergencyGCStartTime / (double)XEngineFactory.XEngine.Time; }
        }
        #endregion
    }
}
