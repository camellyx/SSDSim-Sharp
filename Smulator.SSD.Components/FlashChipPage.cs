using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    public class FlashChipPage
    {
        public uint ValidStatus; //each bit indicates the subpage contains valid or invalid data. 1 indicates valid and 0 indicates invalid
        public ulong LPN;        //Recods the logical address of the stored page
        public ushort StreamID;
        public static uint PG_FREE = 0x00000000;
        public static uint PG_INVALID = 0xffffffff;
        public static ushort PG_NOSTREAM = 0xff;
        public FlashChipPage()
        {
            ValidStatus = PG_FREE;
            LPN = ulong.MaxValue;
            StreamID = PG_NOSTREAM;
        }
    }
}
