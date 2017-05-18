using System;
using System.Reflection;
using Smulator.SSD.Components;
using Smulator.BaseComponents;
using Smulator.BaseComponents.Distributions;
using Smulator.Util;

namespace Smulator.Disk.SSD.NetworkGenerator
{
    /// <summary>
    /// I have added this class to scape from large input files. Anytime we prefer these set of parameters to be
    /// listed in the input file, we can simply move them to FlashChipParameterSet
    /// </summary>
    public class NotListedFlashChipParameters
    {
        public static uint blockEraseLimit = 60000;
        public static ulong SuspendWriteSetup = 5000;
        public static ulong SuspendEraseSetup = 20000;
    }
    public class FlashChipParameterSet : BaseParameterSet
    {
        public ulong pageWriteLatency = 1600000; //nano-seconds
        public ulong pageReadLatency = 75000; //nano-seconds
        public ulong blockEraseLatency = 5000000; //nano-seconds
        public uint dieNoPerChip = 2;
        public uint planeNoPerDie = 2;
        public uint blockNoPerPlane = 2048;
        public uint pageNoPerBlock = 256;
        public uint pageCapacity = 8192; //in bytes: between 512 to 15872

    }
}
