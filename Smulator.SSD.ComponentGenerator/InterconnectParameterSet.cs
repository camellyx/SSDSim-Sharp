using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Smulator.Util;
using Smulator.SSD.Components;

namespace Smulator.Disk.SSD.NetworkGenerator
{
    /// <summary>
    /// I have added this class to scape from large input files. Anytime we prefer these set of parameters to be
    /// listed in the input file, we can simply move them to InterconnectParameterSet
    /// </summary>
    public class NotListedNetworkParameters
    {
        /*All of the values are from M73A_32Gb_64Gb_128Gb_256Gb_AsyncSync_NAND.pdf manual, from Micron Technology, 2010.*/
        public static ulong dummyBusyTime = 500; /*Dummy busy time: this time period is important in multiplane commands, when writing consecutive commands and addresses.*/
        public static ulong readDataOutputReadyTime = 20; /*The time it takes to inform the controller that read data is ready*/
        public static ulong chipEnableTime = 25; /*The time to drive chipe enable (CE#) signal of a target flash chip. This time is equal to (t_CS + t_CH: setup time + hold time)*/
        public static ulong WEtoRBTransitionTime = 100; /*WE# HIGH to R/B# LOW: According to manual, this time should be spent between command and data transfer time and operation execution time*/
        public static ulong ALEtoDataStartTransitionTime = 70; /*ALE to data start: According to manual, this time should be spent between command and data transfer time and page data transfer time*/
        public static ulong readCommandAddressCycleCount = 7; /*The number of cycles that is required to fill command and address registers of a die for read operation*/
        public static ulong writeCommandAddressCycleCount = 7; /*The number of cycles that is required to fill command and address registers of a die for write operation*/
        public static ulong eraseCommandAddressCycleCount = 5; /*The number of cycles that is required to fill command and address registers of a die for erase operation*/

        public static uint readResponseHeaderSize = 4, writeAckSize = 3, cleanAckSize = 3;
        public static uint readReqSize = 5, writeReqHeaderSize = 3, cleanReqSize = 5;
        public static ulong pcDelay = 5, swDelay = 2; /*in nano-seconds*/
        public static uint channelWidth = 8;
    }
    public enum RoutingFunctionEnum { Adaptive, Deterministic };
    public class InterconnectParameterSet : BaseParameterSet
    {
        //I assume two dimensional array of chips on the SSD board
        public uint BusChannelCount = 8; //Number of parallel channels that are connected to the controller
        public uint ChipCountPerChannel = 8; //Number of flash chips in a bus channel
        public ulong readTransferCycleTime = 3; /*The bus cycle time (in nano-seconds) that takes to perform a read transfer from flash chip to controller*/
        public ulong writeTransferCycleTime = 3; /*The bus cycle time (in nano-seconds) that takes to perform a write transfer from controller to flash chip*/
    }
}
