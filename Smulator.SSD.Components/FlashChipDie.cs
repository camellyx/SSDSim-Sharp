using System;
using System.IO;
using System.Collections;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    #region Change info
    /// <change>
    /// <author>Arash Tavakkol</author>
    /// <description>Structure generally changed to conform to real die implementation</description>
    /// <date>Copyright(c)2013</date>
    /// </change>
    #endregion
    /// <summary>
    /// <title>Die</title>
    /// <description> 
    /// </description>
    /// <copyright>Copyright(c)2011</copyright>
    /// <company></company>
    /// <author>Arash Tavakkol ( www.arasht.com )</author>
    /// <version>Version 1.0</version>
    /// <date>2011/12/18</date>
    /// </summary>
    public enum DieStatus { Busy, Idle };
    public class FlashChipDie
    {
        #region StructuralParameters
        public uint CurrentActivePlaneID;
        public FlashChipPlane[] Planes;
        public BlockGroupData[] BlockInfoAbstract;
        public DieStatus Status = DieStatus.Idle;
        public uint CurrentExecutingOperationCount = 0;//Used to determine if chips Idle status
        public uint CurrentActiveBlockID = 0;          //Used in Fast GC to hold current active block of flashchip
        #endregion

        #region StatisticParameters
        public ulong TotalProgramExecutionPeriod = 0, TotalReadExecutionPeriod = 0, TotalEraseExecutionPeriod = 0, TotalTransferPeriod = 0;
        #endregion

        #region SetupFunctions
        public FlashChipDie(uint channelID, uint overallChipID, uint localChipID, uint dieID,
            uint PlanesNoPerDie, uint BlocksNoPerPlane, uint PagesNoPerBlock)
        {
            CurrentActivePlaneID = 0;
            Planes = new FlashChipPlane[PlanesNoPerDie];
            for (uint i = 0; i < PlanesNoPerDie; i++)
                Planes[i] = new FlashChipPlane(channelID, overallChipID, localChipID, dieID, i, BlocksNoPerPlane, PagesNoPerBlock);
            BlockInfoAbstract = new BlockGroupData[BlocksNoPerPlane];
            for (uint i = 0; i < BlocksNoPerPlane; i++)
                BlockInfoAbstract[i] = new BlockGroupData(i, PagesNoPerBlock * PlanesNoPerDie);
        }
        #endregion


    }
}
