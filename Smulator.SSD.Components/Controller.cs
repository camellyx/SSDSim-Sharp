using System;
using Smulator.BaseComponents;
using Smulator.Util;

namespace Smulator.SSD.Components
{
    /// <summary>
    /// <title>Controller</title>
    /// <description> 
    /// </description>
    /// <copyright>Copyright(c)2011</copyright>
    /// <company></company>
    /// <author>Arash Tavakkol ( www.arasht.com )</author>
    /// <version>Version 1.0</version>
    /// <date>2011/12/18</date>
    /// </summary>

    public enum InitialStatus { Empty, SteadyState };//Full: All pages contain valid data, Empty: All pages are free, Aged: Some pages are invalidated

    public class Controller : MetaComponent
    {
        public DRAMDataCache DRAM;
		public FTL FTL;
        public HostInterface HostInterface;
        public FCCBase FCC;
        public InitialStatus InitialSSDStatus = InitialStatus.Empty;
        private uint initialPercentageOfValidPages = 0, initialPercentagesOfValidPagesStdDev = 0;
        private RandomGenerator pageStatusRandomGenerator = null;


        #region SetupFunctions
        public Controller(string id, InitialStatus initialSSDStatus, uint percentageOfValidPages, uint validPagesStdDev, FCCBase fcc, int seed)
            : base(id)
		{
            InitialSSDStatus = initialSSDStatus;
            this.initialPercentageOfValidPages = percentageOfValidPages;
            this.initialPercentagesOfValidPagesStdDev = validPagesStdDev;
            FCC = fcc;
            pageStatusRandomGenerator = new RandomGenerator(seed);
        }

        public override void Validate()
		{
			base.Validate ();
			if ( this.FTL == null )
                throw new ValidationException(string.Format("Controller has no FTL", ID));
            if (this.HostInterface == null)
                throw new ValidationException(string.Format("Controller has no Host Interface", ID));
			if ( this.FCC == null )
                throw new ValidationException(string.Format("Controller has no FCC", ID));
        }

		public override void SetupDelegates(bool propagateToChilds)
		{
			base.SetupDelegates (propagateToChilds);
			if ( propagateToChilds )
			{
				if ( FTL != null )
					FTL.SetupDelegates(true);
			}
		}

		public override void ResetDelegates(bool propagateToChilds)
		{
			if ( propagateToChilds )
			{
				if ( FTL != null )
					FTL.ResetDelegates(true);
			}
			base.ResetDelegates (propagateToChilds);
        }

        public override void Start()
        {
            Console.WriteLine("\n");
            Console.WriteLine(".................................................\n");
            Console.WriteLine("Preparing address space");
            prepare();
            HostInterface.Preprocess();
            FTL.ManageUnallocatedValidPages();
            Console.WriteLine("Prepration finished\n");
            Console.WriteLine(".................................................\n");
        }

        public void prepare()
        {
            IntegerPageAddress tempAddress = new IntegerPageAddress(0, 0, 0, 0, 0, 0, 0);
            switch (InitialSSDStatus)
            {
                case InitialStatus.SteadyState:
                    double validPagesAverage = ((double)initialPercentageOfValidPages / 100) * FTL.PagesNoPerBlock;
                    double validPagesStdDev = ((double)initialPercentagesOfValidPagesStdDev / 100) * FTL.PagesNoPerBlock;
                    uint totalAvailableBlocks = FTL.BlockNoPerPlane - (FTL.GarbageCollector.EmergencyThreshold_PlaneFreePages / FTL.PagesNoPerBlock) - 1;
                    for (uint channelID = 0; channelID < FTL.ChannelCount; channelID++)
                    {
                        for (uint chipID = 0; chipID < FTL.ChipNoPerChannel; chipID++)
                            for (uint dieID = 0; dieID < FTL.DieNoPerChip; dieID++)
                                for (uint planeID = 0; planeID < FTL.PlaneNoPerDie; planeID++)
                                {
                                    FlashChipPlane targetPlane = FTL.ChannelInfos[channelID].FlashChips[chipID].Dies[dieID].Planes[planeID];
                                    for (uint blockID = 0; blockID < totalAvailableBlocks; blockID++)
                                    {
                                        double randomValue = pageStatusRandomGenerator.Normal(validPagesAverage, validPagesStdDev);
                                        if (randomValue < 0)
                                            randomValue = validPagesAverage + (validPagesAverage - randomValue);
                                        uint numberOfValidPages = Convert.ToUInt32(randomValue);// * FTL.PagesNoPerBlock);
                                        if (numberOfValidPages > FTL.PagesNoPerBlock)
                                            numberOfValidPages = FTL.PagesNoPerBlock;
                                        for (uint pageID = 0; pageID < numberOfValidPages; pageID++)
                                            FTL.MakePageValid(channelID, chipID, dieID, planeID, blockID, pageID);
                                        for (uint pageID = numberOfValidPages; pageID < FTL.PagesNoPerBlock; pageID++)
                                            FTL.MakePageInvalid(channelID, chipID, dieID, planeID, blockID, pageID);

                                        if (FTL.ChannelInfos[channelID].FlashChips[chipID].Dies[dieID].Planes[planeID].Blocks[blockID].FreePageNo != 0)
                                            throw new Exception("Huh for free pages");
                                        if (FTL.ChannelInfos[channelID].FlashChips[chipID].Dies[dieID].Planes[planeID].Blocks[blockID].LastWrittenPageNo != 255)
                                            throw new Exception("Huh for last written page no");
                                    }
                                }
                    }
                    break;
                case InitialStatus.Empty:
                    break;
                default:
                    throw new Exception("Unhandled initial status!");
            }
        }
        #endregion
    }
}
