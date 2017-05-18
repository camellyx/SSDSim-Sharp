using System;
using System.Reflection;
using Smulator.SSD.Components;
using Smulator.BaseComponents;
using Smulator.Util;
using Smulator.BaseComponents.Distributions;
using System.Xml;
using System.Xml.Serialization;

namespace Smulator.Disk.SSD.NetworkGenerator
{
    /// <summary>
    /// <title>FlashChipGenerator</title>
    /// <description> 
    /// </description>
    /// <copyright>Copyright(c)2011</copyright>
    /// <company></company>
    /// <author>Arash Tavakkol ( www.arasht.ir )</author>
    /// <version>Version 1.0</version>
    /// <date>2011/10/237</date>
    /// </summary>
    public class FlashChipGenerator
    {
        public FlashChipGenerator()
		{
		}

        public FlashChip BuildNormalFlashChip(
            string id,
            uint rowID,
            uint chipID,
            uint overallChipID,
            FlashChipParameterSet flashChipParams,
            ulong readDataOutputReadyTime,
            ref FlashChip flashChip
            )
        {
            return BuildNormalFlashChip(
                id,
                rowID,
                chipID,
                overallChipID,
                flashChipParams.dieNoPerChip,
                flashChipParams.planeNoPerDie,
                flashChipParams.blockNoPerPlane,
                flashChipParams.pageNoPerBlock,
                NotListedFlashChipParameters.blockEraseLimit,
                flashChipParams.pageReadLatency,
                flashChipParams.pageWriteLatency,
                flashChipParams.blockEraseLatency,
                readDataOutputReadyTime);
        }

        public FlashChip BuildNormalFlashChip(
            string id,
            uint channelID,
            uint localChipID,
            uint overallChipID,
            uint dieNoPerChip,
            uint planeNoPerDie,
            uint blockNoPerPlane,
            uint pageNoPerBlock,
            uint blockEraseLimit,
            ulong readDelay,/*page read delay in nano-seconds*/
            ulong writeDelay,/*page write delay in nano-seconds*/
            ulong eraseDelay,
            ulong readDataOutputReadyTime
            )
        {
            return new FlashChip(id, channelID, localChipID, overallChipID,
                dieNoPerChip, planeNoPerDie, blockNoPerPlane, pageNoPerBlock, blockEraseLimit,
                readDelay, writeDelay, eraseDelay, readDataOutputReadyTime);
        }
    }
}
