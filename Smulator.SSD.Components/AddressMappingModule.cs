using System;
using System.Collections;
using System.Collections.Generic;
using Smulator.Util;

namespace Smulator.SSD.Components
{
    /// <summary>
    /// <title>MappingStrategy</title>
    /// <description> 
    /// </description>
    /// <copyright>Copyright(c)2016</copyright>
    /// <company></company>
    /// <author>Arash Tavakkol</author>
    /// <version>Version 2.0</version>
    /// <date>25/11/2016</date>
    /// </summary>
    //C:channel, W:way, D:die, P:plane
    public enum PlaneAllocationSchemeType
    {
        CWDP, CWPD, CDWP, CDPW, CPWD, CPDW,
        WCDP, WCPD, WDCP, WDPC, WPCD, WPDC,
        DCWP, DCPW, DWCP, DWPC, DPCW, DPWC,
        PCWD, PCDW, PWCD, PWDC, PDCW, PDWC,
        F,//Fully dynamic
        C, CW, CD, CP, CWD, CWP, CDW, CDP, CPW, CPD,
        W, WC, WD, WP, WCD, WCP, WDC, WDP, WPC, WPD,
        D, DC, DW, DP, DCW, DCP, DWC, DWP, DPC, DPW,
        P, PC, PW, PD, PCW, PCD, PWC, PWD, PDC, PDW
    };
    public enum BlockAllocationSchemeType { FirstFit };
    public class MappingTable
    {
        public ulong LargestLSN = 0;
        public ulong[] PPN;                   //Physical numbers, either physical page number, can also be physical sub-page number, the physical block number can also be expressed
        public uint[] State;                //Hexadecimal representation if the 0000-FFFF, each of the corresponding sub-page whether (page mapping). For example, in this page, 0,1 sub-pages, 2,3 invalid, this should be 0x0003.
        public MappingTable(uint totalPageNo)
        {
            PPN = new ulong[totalPageNo];
            State = new uint[totalPageNo];
        }
        public bool IsInCMT(bool LPN)
        {
            return false;
        }
    }
    public class DieDynamicMapping
    {
        public uint CurrentActivePlane;
    }
    public class ChipDynamicMapping
    {
        public uint CurrentActiveDie;
        public DieDynamicMapping[] DieDynamicMappings = null;
    }
    public class ChannelDynamicMapping
    {
        public uint CurrentActiveChip;
        public ChipDynamicMapping[] ChipDynamicMappings = null;
    }
    public class AddressMappingDomain
    {
        public MappingTable MappingTable = null;
        public PlaneAllocationSchemeType PlaneAllocationScheme = PlaneAllocationSchemeType.F;
        public BlockAllocationSchemeType BlockAllocationScheme = BlockAllocationSchemeType.FirstFit;
        public bool UsesStaticMappingStrategy = false;
        public ulong LargestLSN = 0;

        public uint ChannelNo = 0;
        public uint ChipNo = 0;
        public uint DieNo = 0;
        public uint PlaneNo = 0;

        public uint[] Channels = { 0 };
        public uint[] Chips = { 0 };
        public uint[] Dies = { 0 };
        public uint[] Planes = { 0 };

        public uint CurrentActiveChannel = 0;
        public ChannelDynamicMapping[] ChannelDynamicMappings = null;

        public uint TotalPagesNo = 0;
        public uint PagesNoPerChannel = 0; //Number of pages in a channel
        public uint PagesNoPerChip = 0;
        public uint PagesNoPerDie = 0;
        public uint PagesNoPerPlane = 0;
        public uint TotalPlaneNo = 0;
        public uint TotalLogicalPagesNo = 0;

        bool _DFTLEnabled = false;
        uint _CMTCapacity = 0;

        public AddressMappingDomain(PlaneAllocationSchemeType planeAllocationScheme, BlockAllocationSchemeType blockAllocationScheme,
            uint[] channels, uint[] chips, uint[] dies, uint[] planes,
            uint blockNoPerPlane, uint pageNoPerBlock, uint subpageNoPerPage, double overprovisioningRatio, bool dftlEnabled, uint CMTCapacity)
        {
            PlaneAllocationScheme = planeAllocationScheme;
            BlockAllocationScheme = blockAllocationScheme;
            ChannelNo = (uint)channels.Length;
            Channels = new uint[ChannelNo];
            ChannelDynamicMappings = new ChannelDynamicMapping[ChannelNo];
            for (int channelCntr = 0; channelCntr < ChannelNo; channelCntr++)
            {
                Channels[channelCntr] = channels[channelCntr];
                ChannelDynamicMappings[channelCntr] = new ChannelDynamicMapping();
                ChannelDynamicMappings[channelCntr].CurrentActiveChip = 0;
                ChannelDynamicMappings[channelCntr].ChipDynamicMappings = new ChipDynamicMapping[chips.Length];
                for (int chipCntr = 0; chipCntr < chips.Length; chipCntr++)
                {
                    ChannelDynamicMappings[channelCntr].ChipDynamicMappings[chipCntr] = new ChipDynamicMapping();
                    ChannelDynamicMappings[channelCntr].ChipDynamicMappings[chipCntr].CurrentActiveDie = 0;
                    ChannelDynamicMappings[channelCntr].ChipDynamicMappings[chipCntr].DieDynamicMappings = new DieDynamicMapping[dies.Length];
                    for (int dieCntr = 0; dieCntr < dies.Length; dieCntr++)
                    {
                        ChannelDynamicMappings[channelCntr].ChipDynamicMappings[chipCntr].DieDynamicMappings[dieCntr] = new DieDynamicMapping();
                        ChannelDynamicMappings[channelCntr].ChipDynamicMappings[chipCntr].DieDynamicMappings[dieCntr].CurrentActivePlane = 0;
                    }
                }
            }


            UsesStaticMappingStrategy = planeAllocationScheme == PlaneAllocationSchemeType.CWDP || planeAllocationScheme == PlaneAllocationSchemeType.CWPD || planeAllocationScheme == PlaneAllocationSchemeType.CDWP
                            || planeAllocationScheme == PlaneAllocationSchemeType.CDPW || planeAllocationScheme == PlaneAllocationSchemeType.CPWD || planeAllocationScheme == PlaneAllocationSchemeType.CPDW
                            || planeAllocationScheme == PlaneAllocationSchemeType.WCDP || planeAllocationScheme == PlaneAllocationSchemeType.WCPD || planeAllocationScheme == PlaneAllocationSchemeType.WDCP
                            || planeAllocationScheme == PlaneAllocationSchemeType.WDPC || planeAllocationScheme == PlaneAllocationSchemeType.WPCD || planeAllocationScheme == PlaneAllocationSchemeType.WPDC
                            || planeAllocationScheme == PlaneAllocationSchemeType.DCWP || planeAllocationScheme == PlaneAllocationSchemeType.DCPW || planeAllocationScheme == PlaneAllocationSchemeType.DWCP
                            || planeAllocationScheme == PlaneAllocationSchemeType.DWPC || planeAllocationScheme == PlaneAllocationSchemeType.DPCW || planeAllocationScheme == PlaneAllocationSchemeType.DPWC
                            || planeAllocationScheme == PlaneAllocationSchemeType.PCWD || planeAllocationScheme == PlaneAllocationSchemeType.PCDW || planeAllocationScheme == PlaneAllocationSchemeType.PWCD
                            || planeAllocationScheme == PlaneAllocationSchemeType.PWDC || planeAllocationScheme == PlaneAllocationSchemeType.PDCW || planeAllocationScheme == PlaneAllocationSchemeType.PDWC;

            ChipNo = (uint)chips.Length;
            Chips = new uint[ChipNo];
            for (int i = 0; i < ChipNo; i++)
            {
                Chips[i] = chips[i];
            }

            DieNo = (uint)dies.Length;
            Dies = new uint[DieNo];
            for (int i = 0; i < DieNo; i++)
                Dies[i] = dies[i];

            PlaneNo = (uint)planes.Length;
            Planes = new uint[PlaneNo];
            for (int i = 0; i < PlaneNo; i++)
                Planes[i] = planes[i];

            PagesNoPerPlane = pageNoPerBlock * blockNoPerPlane;
            PagesNoPerDie = PagesNoPerPlane * PlaneNo;
            PagesNoPerChip = PagesNoPerDie * DieNo;
            PagesNoPerChannel = PagesNoPerChip * ChipNo;
            TotalPagesNo = PagesNoPerChannel * ChannelNo;
            LargestLSN = (ulong)(((double)(subpageNoPerPage * TotalPagesNo)) * (1 - overprovisioningRatio));

            TotalPlaneNo = PlaneNo * DieNo * ChipNo * ChannelNo;
            TotalLogicalPagesNo = Convert.ToUInt32(LargestLSN / subpageNoPerPage);

            MappingTable = new MappingTable(TotalPagesNo);
            _DFTLEnabled = dftlEnabled;
            _CMTCapacity = CMTCapacity;
        }
    }
    public class PlaneAddressHolder
    {
        ulong[] LPNs;
        public uint TotalValidLPNs;

        public PlaneAddressHolder(uint capacity)
        {
            LPNs = new ulong[capacity];
            TotalValidLPNs = 0;
        }
        public void Insert(ulong lpn)
        {
            LPNs[TotalValidLPNs] = lpn;
            TotalValidLPNs++;
        }
        public void RemoveAt(uint index)
        {
            TotalValidLPNs--;
            LPNs[index] = LPNs[TotalValidLPNs];
        }
        public ulong this[uint i]
        { get { return LPNs[i]; } }
    }
    public class AddressMappingModule
    {
        public AddressMappingDomain[] AddressMappingDomains;

        private FTL _FTL;
        private uint[,] localToOverallChipMapping;


        private uint _channelNo;           //Number of channels
        private uint _chipNoPerChannel;
        private uint _dieNoPerChip;           //Number of dies per flash chip
        private uint _planeNoPerDie;          //indicate how many planes in a die
        private uint _blockNoPerPlane;        //indicate how many blocks in a plane
        private uint _pagesNoPerBlock = 0;
        private uint _pagesNoPerPlane = 0;
        private uint _pagesNoPerDie = 0;
        private uint _pagesNoPerChip = 0;
        private uint _pagesNoPerChannel = 0;
        private double _overprovisioningRatio;
        public const uint DefaultStreamID = 0;
        PlaneAddressHolder[][] planeLPNs;//the logical page addresses that could be allocated to each plane
        uint lpnNoPerPlane = 0;
        bool lpnTablePopulated = false;

        //public uint TotalPagesNo = 0;

        public AddressMappingModule(
            FTL ftl,
            AddressMappingDomain[] addressMappingDomains,
            FlashChip[][] flashChips,
            uint channelNo,
            uint chipNoPerChannel,
            uint dieNoPerChip,
            uint planeNoPerDie,
            uint blockNoPerPlane,
            uint pageNoPerBlock,
            double overprovisioningRatio)
        {
            _FTL = ftl;
            AddressMappingDomains = addressMappingDomains;

            localToOverallChipMapping = new uint[_FTL.ChannelCount, _FTL.ChipNoPerChannel];
            for (uint i = 0; i < channelNo; i++)
                for (uint j = 0; j < chipNoPerChannel; j++)
                    localToOverallChipMapping[i, j] = flashChips[i][j].OverallChipID;

            for (uint streamID = 0; streamID < addressMappingDomains.Length; streamID++)
                for (uint channelID = 0; channelID < addressMappingDomains[streamID].ChannelNo; channelID++)
                    for (uint chipID = 0; chipID < addressMappingDomains[streamID].ChipNo; chipID++)
                        for (uint dieID = 0; dieID < addressMappingDomains[streamID].DieNo; dieID++)
                            for (uint planeID = 0; planeID < addressMappingDomains[streamID].PlaneNo; planeID++)
                                flashChips[addressMappingDomains[streamID].Channels[channelID]][addressMappingDomains[streamID].Chips[chipID]].
                                    Dies[addressMappingDomains[streamID].Dies[dieID]].Planes[addressMappingDomains[streamID].Planes[planeID]].AllocatedStreamsTemp.Add(streamID);

            for (uint channelID = 0; channelID < channelNo; channelID++)
                for (uint chipID = 0; chipID < chipNoPerChannel; chipID++)
                    for (uint dieID = 0; dieID < dieNoPerChip; dieID++)
                        for (uint planeID = 0; planeID < planeNoPerDie; planeID++)
                        {
                            FlashChipPlane targetPlane = flashChips[channelID][chipID].Dies[dieID].Planes[planeID];
                            targetPlane.AllocatedStreams = new uint[targetPlane.AllocatedStreamsTemp.Count];
                            for (int i = 0; i < targetPlane.AllocatedStreams.Length; i++)
                                targetPlane.AllocatedStreams[i] = (uint)targetPlane.AllocatedStreamsTemp[i];
                            targetPlane.AllocatedStreamsTemp = null;
                        }

            _channelNo = channelNo;
            _chipNoPerChannel = chipNoPerChannel;
            _dieNoPerChip = dieNoPerChip;
            _planeNoPerDie = planeNoPerDie;
            _blockNoPerPlane = blockNoPerPlane;
            _pagesNoPerBlock = pageNoPerBlock;
            _overprovisioningRatio = overprovisioningRatio;

            _pagesNoPerPlane = _pagesNoPerBlock * _blockNoPerPlane;
            _pagesNoPerDie = _pagesNoPerPlane * _planeNoPerDie;
            _pagesNoPerChip = _pagesNoPerDie * _dieNoPerChip;
            _pagesNoPerChannel = _pagesNoPerChip * _chipNoPerChannel;

            lpnTablePopulated = false;
        }
        public IntegerPageAddress CreateMappingEntryForMissingRead(ulong lsn, uint streamID)
        {
            if (lpnTablePopulated)
                throw new Exception("CreateMappingEntryForMissingRead function should not be called after populating LPN tables!");

            IntegerPageAddress targetAddress = new IntegerPageAddress(0, 0, 0, 0, 0, 0, 0);
            ulong lpn;

            lpn = lsn / _FTL.SubpageNoPerPage;
            switch (AddressMappingDomains[streamID].PlaneAllocationScheme)
            {
                #region DynamicAllocation
                case PlaneAllocationSchemeType.F:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[AddressMappingDomains[streamID].CurrentActiveChannel];
                    AddressMappingDomains[streamID].CurrentActiveChannel = (AddressMappingDomains[streamID].CurrentActiveChannel + 1) % AddressMappingDomains[streamID].ChannelNo;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                #region ChannelFirst
                case PlaneAllocationSchemeType.C:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.CW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / _FTL.ChannelCount) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.CD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / _FTL.ChannelCount) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.CP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / _FTL.ChannelCount) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.CWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / _FTL.ChannelCount) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (_FTL.ChannelCount * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.CWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / _FTL.ChannelCount) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (_FTL.ChannelCount * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.CDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (_FTL.ChannelCount * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / _FTL.ChannelCount) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.CDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / _FTL.ChannelCount) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (_FTL.ChannelCount * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.CPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (_FTL.ChannelCount * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / _FTL.ChannelCount) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.CPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (_FTL.ChannelCount * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / _FTL.ChannelCount) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                #endregion
                #region WayFirst
                case PlaneAllocationSchemeType.W:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.WC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.WD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.WP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.WCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * _FTL.ChannelCount)) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.WCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * _FTL.ChannelCount)) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.WDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.WDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.WPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.WPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                #endregion
                #region DieFirst
                case PlaneAllocationSchemeType.D:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.DC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.DW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.DP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.DCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * _FTL.ChannelCount)) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.DCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * _FTL.ChannelCount)) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.DWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].DieDynamicMappings[targetAddress.DieID].CurrentActivePlane = (targetAddress.PlaneID + 1) % AddressMappingDomains[streamID].PlaneNo;
                    break;
                case PlaneAllocationSchemeType.DWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.DPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.DPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                #endregion
                #region PlaneFirst
                case PlaneAllocationSchemeType.P:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.PC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.PW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.PD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.PCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * _FTL.ChannelCount)) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.PCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * _FTL.ChannelCount)) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.PWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].ChipDynamicMappings[targetAddress.LocalFlashChipID].CurrentActiveDie = (targetAddress.DieID + 1) % AddressMappingDomains[streamID].DieNo;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.PWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.PDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % _FTL.ChannelCount)];//##Static##
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip];
                    AddressMappingDomains[streamID].ChannelDynamicMappings[targetAddress.ChannelID].CurrentActiveChip = (targetAddress.LocalFlashChipID + 1) % AddressMappingDomains[streamID].ChipNo;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                case PlaneAllocationSchemeType.PDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[_FTL.CurrentActiveChannel];
                    _FTL.CurrentActiveChannel = (_FTL.CurrentActiveChannel + 1) % _FTL.ChannelCount;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];//##Static##
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];//##Static##
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];//##Static##
                    break;
                #endregion
                #endregion
                #region StaticAllocation
                #region ChannelFirst
                //Static: Channel first
                case PlaneAllocationSchemeType.CWDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.CWPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.CDWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.CDPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.CPWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.CPDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                #endregion
                #region WayFirst
                //Static: Way first
                case PlaneAllocationSchemeType.WCDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.WCPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.WDCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.WDPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.WPCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.WPDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                #endregion
                #region DieFirst
                //Static: Die first
                case PlaneAllocationSchemeType.DCWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.DCPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.DWCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.DWPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.DPCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.DPWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                #endregion
                #region PlaneFirst
                //Static: Plane first
                case PlaneAllocationSchemeType.PCWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.PCDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.PWCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.PWDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.PDCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                case PlaneAllocationSchemeType.PDWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    break;
                #endregion
                #endregion
                default:
                    throw new GeneralException("Unhandled Allocation Scheme Type");
            }
            targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
            return (targetAddress);
        }
        public IntegerPageAddress CreateMappingEntryForMissingRead(ulong lsn)
        {
            return this.CreateMappingEntryForMissingRead(lsn, 0);
        }
        public void AllocatePlaneForWrite(uint streamID, InternalRequest internalReq)
        {
            IntegerPageAddress targetAddress = internalReq.TargetPageAddress;
            ulong lpn = internalReq.LPN;

            switch (AddressMappingDomains[streamID].PlaneAllocationScheme)
            {
                #region DynamicAllocation
                case PlaneAllocationSchemeType.F:
                    targetAddress.ChannelID = 0;//for correct insertion of request to the WaitingInternalWriteReqs, it will be changed in the future
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #region ChannelFirst
                case PlaneAllocationSchemeType.C:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.CW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.CP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.CWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.CPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #endregion
                #region WayFirst
                case PlaneAllocationSchemeType.W:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.WC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WD:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.WP:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.WCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WDP:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.WPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WPD:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #endregion
                #region DieFirst
                case PlaneAllocationSchemeType.D:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DW:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DP:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DWP:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DPW:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #endregion
                #region PlaneFirst
                case PlaneAllocationSchemeType.P:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PW:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PD:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PWD:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PDW:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #endregion
                #endregion
                #region StaticAllocation
                #region ChannelFirst
                case PlaneAllocationSchemeType.CWDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CWPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CDWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CDPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CPWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CPDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                #endregion
                #region WayFirst
                //Static: Way first
                case PlaneAllocationSchemeType.WCDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WCPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WDCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WDPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WPCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WPDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                #endregion
                #region DieFirst
                //Static: Die first
                case PlaneAllocationSchemeType.DCWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DCPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DWCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DWPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DPCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DPWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                #endregion
                #region PlaneFirst
                //Static: Plane first
                case PlaneAllocationSchemeType.PCWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PCDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PWCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PWDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PDCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PDWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    internalReq.TargetFlashChip = _FTL.FlashChips[targetAddress.OverallFlashChipID];
                    break;
                #endregion
                default:
                    throw new Exception("Unhandled allocation scheme type!");
                    #endregion
            }
        }
        private IntegerPageAddress allocatePlane(uint streamID, ulong lpn)
        {
            IntegerPageAddress targetAddress = new IntegerPageAddress(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue);

            switch (AddressMappingDomains[streamID].PlaneAllocationScheme)
            {
                #region DynamicAllocation
                case PlaneAllocationSchemeType.F:
                    targetAddress.ChannelID = 0;//for correct insertion of request to the WaitingInternalWriteReqs, it will be changed in the future
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #region ChannelFirst
                case PlaneAllocationSchemeType.C:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.CW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.CP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.CWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.CPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #endregion
                #region WayFirst
                case PlaneAllocationSchemeType.W:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.WC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WD:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.WP:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.WCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WDP:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.WPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WPD:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #endregion
                #region DieFirst
                case PlaneAllocationSchemeType.D:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DW:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DP:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = uint.MaxValue;
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DWP:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.DPW:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #endregion
                #region PlaneFirst
                case PlaneAllocationSchemeType.P:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PW:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PD:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = uint.MaxValue;
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PWD:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = uint.MaxValue;
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                case PlaneAllocationSchemeType.PDW:
                    targetAddress.ChannelID = uint.MaxValue;
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.BlockID = uint.MaxValue;
                    targetAddress.PageID = uint.MaxValue;
                    targetAddress.OverallFlashChipID = uint.MaxValue;
                    break;
                #endregion
                #endregion
                #region StaticAllocation
                #region ChannelFirst
                case PlaneAllocationSchemeType.CWDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CWPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CDWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CDPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CPWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.CPDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)(lpn % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChannelNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = this.localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                #endregion
                #region WayFirst
                //Static: Way first
                case PlaneAllocationSchemeType.WCDP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WCPD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WDCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WDPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WPCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.WPDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)(lpn % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].ChipNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                #endregion
                #region DieFirst
                //Static: Die first
                case PlaneAllocationSchemeType.DCWP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DCPW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DWCP:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DWPC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DPCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.DPWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].PlaneNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)(lpn % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)((lpn / AddressMappingDomains[streamID].DieNo) % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                #endregion
                #region PlaneFirst
                //Static: Plane first
                case PlaneAllocationSchemeType.PCWD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PCDW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PWCD:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PWDC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PDCW:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChannelNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                case PlaneAllocationSchemeType.PDWC:
                    targetAddress.ChannelID = AddressMappingDomains[streamID].Channels[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo)) % AddressMappingDomains[streamID].ChannelNo)];
                    targetAddress.LocalFlashChipID = AddressMappingDomains[streamID].Chips[(uint)((lpn / (AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo)) % AddressMappingDomains[streamID].ChipNo)];
                    targetAddress.DieID = AddressMappingDomains[streamID].Dies[(uint)((lpn / AddressMappingDomains[streamID].PlaneNo) % AddressMappingDomains[streamID].DieNo)];
                    targetAddress.PlaneID = AddressMappingDomains[streamID].Planes[(uint)(lpn % AddressMappingDomains[streamID].PlaneNo)];
                    targetAddress.OverallFlashChipID = localToOverallChipMapping[targetAddress.ChannelID, targetAddress.LocalFlashChipID];
                    break;
                #endregion
                default:
                    throw new Exception("Unhandled allocation scheme type!");
                    #endregion
            }
            return targetAddress;
        }
        public void PopulateLPNTableForSteadyStateSimulation()
        {
            planeLPNs = new PlaneAddressHolder[AddressMappingDomains.Length][];

            for (uint streamCntr = 0; streamCntr < AddressMappingDomains.Length; streamCntr++)
            {
                if (!AddressMappingDomains[streamCntr].UsesStaticMappingStrategy)
                    throw new Exception("I cannot handle dynamic allocation strategies!");

                //we assume static allocation
                planeLPNs[streamCntr] = new PlaneAddressHolder[AddressMappingDomains[streamCntr].TotalPlaneNo];
                for (uint planeCntr = 0; planeCntr < AddressMappingDomains[streamCntr].TotalPlaneNo; planeCntr++)
                {
                    lpnNoPerPlane = AddressMappingDomains[streamCntr].TotalLogicalPagesNo / AddressMappingDomains[streamCntr].TotalPlaneNo;
                    planeLPNs[streamCntr][planeCntr] = new PlaneAddressHolder(lpnNoPerPlane);
                }

                //in static allocation, if we know the lowest page address going to chip, then we can determine the remaining ones
                for (uint basePageAddress = 0; basePageAddress < AddressMappingDomains[streamCntr].TotalPlaneNo; basePageAddress++)
                {
                    IntegerPageAddress address = allocatePlane(streamCntr, basePageAddress);
                    uint index = address.ChannelID * AddressMappingDomains[streamCntr].PlaneNo * AddressMappingDomains[streamCntr].DieNo * AddressMappingDomains[streamCntr].ChipNo
                        + address.LocalFlashChipID * AddressMappingDomains[streamCntr].PlaneNo * AddressMappingDomains[streamCntr].DieNo
                        + address.DieID * AddressMappingDomains[streamCntr].PlaneNo + address.PlaneID;
                    if (AddressMappingDomains[streamCntr].MappingTable.State[basePageAddress] == 0)
                        planeLPNs[streamCntr][index].Insert(basePageAddress);
                    ulong lastLPN = basePageAddress;
                    for (int pageCntr = 1; pageCntr < lpnNoPerPlane; pageCntr++)
                    {
                        lastLPN += AddressMappingDomains[streamCntr].TotalPlaneNo;
                        if (AddressMappingDomains[streamCntr].MappingTable.State[lastLPN] == 0)
                            planeLPNs[streamCntr][index].Insert(lastLPN);
                    }
                }
            }

            lpnTablePopulated = true;
        }
        public ulong GetValidLPNForAddress(RandomGenerator randomAddressGenerator, uint streamID, uint channelID, uint chipID, uint dieID, uint planeID, uint blockID, uint pageID)
        {
            if (!lpnTablePopulated)
                throw new Exception("LPN table is not populated yet!");

            uint planeIndex = channelID * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo * AddressMappingDomains[streamID].ChipNo
                + chipID * AddressMappingDomains[streamID].PlaneNo * AddressMappingDomains[streamID].DieNo
                + dieID * AddressMappingDomains[streamID].PlaneNo + planeID;
            uint lpnIndex = randomAddressGenerator.UniformUInt(0, (uint)planeLPNs[streamID][planeIndex].TotalValidLPNs - 1);
            ulong lpn = planeLPNs[streamID][planeIndex][lpnIndex];
            planeLPNs[streamID][planeIndex].RemoveAt(lpnIndex);

            return lpn;
        }
        public void FreeLPNTables()
        {
            for (uint streamCntr = 0; streamCntr < AddressMappingDomains.Length; streamCntr++)
            {
                for (uint planeCntr = 0; planeCntr < AddressMappingDomains[streamCntr].TotalPlaneNo; planeCntr++)
                    planeLPNs[streamCntr][planeCntr] = null;
                planeLPNs[streamCntr] = null;
            }
        }

        /// <summary>
        /// Converts Physical Page Number to IntegerPageAddress (ChannelID, FlashChipID, DieID, ...)
        /// </summary>
        /// <param name="ppn">Physical Page Number</param>
        /// <returns>Related IntegerPageAddress</returns>
        public IntegerPageAddress ConvertPPNToPageAddress(ulong ppn)
        {
            IntegerPageAddress target = new IntegerPageAddress(0, 0, 0, 0, 0, 0, 0);
            target.ChannelID = (uint)(ppn / _pagesNoPerChannel);
            target.LocalFlashChipID = (uint)((ppn % _pagesNoPerChannel) / _pagesNoPerChip);
            target.OverallFlashChipID = localToOverallChipMapping[target.ChannelID, target.LocalFlashChipID];
            target.DieID = (uint)(((ppn % _pagesNoPerChannel) % _pagesNoPerChip) / _pagesNoPerDie);
            target.PlaneID = (uint)((((ppn % _pagesNoPerChannel) % _pagesNoPerChip) % _pagesNoPerDie) / _pagesNoPerPlane);
            target.BlockID = (uint)(((((ppn % _pagesNoPerChannel) % _pagesNoPerChip) % _pagesNoPerDie) % _pagesNoPerPlane) / _pagesNoPerBlock);
            target.PageID = (uint)((((((ppn % _pagesNoPerChannel) % _pagesNoPerChip) % _pagesNoPerDie) % _pagesNoPerPlane) % _pagesNoPerBlock) % _pagesNoPerBlock);

            return target;
        }
        public void ConvertPPNToPageAddress(ulong ppn, IntegerPageAddress target)
        {
            target.ChannelID = (uint)(ppn / _pagesNoPerChannel);
            target.LocalFlashChipID = (uint)((ppn % _pagesNoPerChannel) / _pagesNoPerChip);
            target.OverallFlashChipID = localToOverallChipMapping[target.ChannelID, target.LocalFlashChipID];
            target.DieID = (uint)(((ppn % _pagesNoPerChannel) % _pagesNoPerChip) / _pagesNoPerDie);
            target.PlaneID = (uint)((((ppn % _pagesNoPerChannel) % _pagesNoPerChip) % _pagesNoPerDie) / _pagesNoPerPlane);
            target.BlockID = (uint)(((((ppn % _pagesNoPerChannel) % _pagesNoPerChip) % _pagesNoPerDie) % _pagesNoPerPlane) / _pagesNoPerBlock);
            target.PageID = (uint)((((((ppn % _pagesNoPerChannel) % _pagesNoPerChip) % _pagesNoPerDie) % _pagesNoPerPlane) % _pagesNoPerBlock) % _pagesNoPerBlock);
        }
        public ulong ConvertPageAddressToPPN(IntegerPageAddress pageAddress)
        {
            return (ulong)this._pagesNoPerChip * (ulong)(pageAddress.ChannelID * this._chipNoPerChannel+ pageAddress.LocalFlashChipID)
                + this._pagesNoPerDie * pageAddress.DieID + this._pagesNoPerPlane * pageAddress.PlaneID
                + this._pagesNoPerBlock * pageAddress.BlockID + pageAddress.PageID;
        }
        public bool CheckRequestAddress(uint streamID, ulong lsn, uint size, bool foldAddressEnabled)
        {
            if (foldAddressEnabled)
                return true;

            if ((lsn + size * FTL.SubPageCapacity) <= this.AddressMappingDomains[streamID].LargestLSN)
                return true;
            else
                return false;
        }
        public bool CheckReadRequest(uint streamID, ulong lsn, uint size)
        {
            uint handledSize = 0, subSize = 0;

            while (handledSize < size)
            {
                lsn = lsn % this.AddressMappingDomains[streamID].LargestLSN;
                subSize = _FTL.SubpageNoPerPage - (uint)(lsn % _FTL.SubpageNoPerPage);
                if (handledSize + subSize >= size)
                {
                    subSize = size - handledSize;
                    handledSize += subSize;
                }
                ulong lpn = lsn / _FTL.SubpageNoPerPage;
                if (this.AddressMappingDomains[streamID].MappingTable.State[lpn] == 0)
                    return false;
                lsn = lsn + subSize;
                handledSize += subSize;
            }
            return true;
        }
        public bool CheckRequestAddress(ulong lsn, uint size, bool foldAddressEnabled)
        {
            return CheckRequestAddress(0, lsn, size, foldAddressEnabled);
        }
        public bool CheckReadRequest(ulong lsn, uint size)
        {
            return CheckReadRequest(0, lsn, size);
        }
        public ulong LargestLSN
        {
            get { return this.AddressMappingDomains[DefaultStreamID].LargestLSN; }
        }
        public ulong PagesNoPerPlane
        {
            get { return _pagesNoPerPlane; }
        }
        public uint GetOveralFlashchipID(uint channelID, uint localChipID)
        {
            return this.localToOverallChipMapping[channelID, localChipID];
        }
    }
}
