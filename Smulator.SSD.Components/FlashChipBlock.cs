using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    public class BlockGroupData
    {
        public BlockGroupData(uint blockID, uint pageNoPerUnit)
        {
            this.BlockID = blockID;
            this.FreePageNo = pageNoPerUnit;
        }
        public uint BlockID;
        public uint FreePageNo;
        public uint EraseCount;
        public uint InvalidPageNo;
    }
    public class FlashChipBlock
    {
        public uint EraseCount;         //Recods the number of erase operations occurred
        public uint FreePageNo;         //Records the number of free pages in the block
        public uint InvalidPageNo;      //Records the the INVALID pages in the block
        public int LastWrittenPageNo;   //-1 Indicates that the block has not written since last erase operation
        public FlashChipPage[] Pages;                   //Records the status of each sub-page
        public FlashChipBlock Next = null;  //Used for list based garbage collections such as FIFO and WindowedGreedy
        public uint BlockID = 0;            //Again this variable is required in list based garbage collections

        public FlashChipBlock(uint PagesNoPerBlock, uint BlockID)
        {
            this.EraseCount = 0;
            this.InvalidPageNo = 0;
            this.FreePageNo = PagesNoPerBlock;
            this.LastWrittenPageNo = -1;
            this.BlockID = BlockID;
            Pages = new FlashChipPage[PagesNoPerBlock];
            for (int i = 0; i < PagesNoPerBlock; i++)
                Pages[i] = new FlashChipPage();
        }
    }
}
