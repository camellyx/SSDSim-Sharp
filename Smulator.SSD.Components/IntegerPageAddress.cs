using System;
using System.Collections.Generic;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public class IntegerPageAddress
    {
        public uint ChannelID;
        public uint LocalFlashChipID;        //The flashchip ID inside its channel
        public uint OverallFlashChipID; //The flashchip ID in the entire list of SSD flashchips
        public uint DieID;
        public uint PlaneID;
        public uint BlockID;
        public uint PageID;

        public IntegerPageAddress(uint ChannelID, uint LocalFlashChipID, uint DieID, uint PlaneID, uint BlockID, uint PageID, uint OverallFlashChipID)
        {
            this.ChannelID = ChannelID;
            this.LocalFlashChipID = LocalFlashChipID;
            this.DieID = DieID;
            this.PlaneID = PlaneID;
            this.BlockID = BlockID;
            this.PageID = PageID;
            this.OverallFlashChipID = OverallFlashChipID;
        }

        public IntegerPageAddress(uint ChannelID, uint LocalFlashChipID, uint DieID, uint PlaneID, uint BlockID, uint PageID)
        {
            this.ChannelID = ChannelID;
            this.LocalFlashChipID = LocalFlashChipID;
            this.DieID = DieID;
            this.PlaneID = PlaneID;
            this.BlockID = BlockID;
            this.PageID = PageID;
            this.OverallFlashChipID = 0;
        }

        public IntegerPageAddress(IntegerPageAddress addressToCopy)
        {
            this.ChannelID = addressToCopy.ChannelID;
            this.LocalFlashChipID = addressToCopy.LocalFlashChipID;
            this.DieID = addressToCopy.DieID;
            this.PlaneID = addressToCopy.PlaneID;
            this.BlockID = addressToCopy.BlockID;
            this.PageID = addressToCopy.PageID;
            this.OverallFlashChipID = addressToCopy.OverallFlashChipID;
        }

        public bool EqualsForMultiplane(IntegerPageAddress referenceAddress, bool BAConstraint)
        {
            if (   (this.PageID   != referenceAddress.PageID)
                || ((this.BlockID != referenceAddress.BlockID) && BAConstraint)
                || (this.PlaneID  == referenceAddress.PlaneID)
                || (this.DieID    != referenceAddress.DieID)
                || (this.LocalFlashChipID != referenceAddress.LocalFlashChipID)
                || (this.ChannelID != referenceAddress.ChannelID)
                || (this.OverallFlashChipID != referenceAddress.OverallFlashChipID)
                )
                return false;
            return true;
        }

        public bool EqualsForMultiplaneErase(IntegerPageAddress referenceAddress, bool BAConstraint)
        {
            if (   ((this.BlockID != referenceAddress.BlockID) && BAConstraint)
                || (this.PlaneID == referenceAddress.PlaneID)
                || (this.DieID != referenceAddress.DieID)
                || (this.LocalFlashChipID != referenceAddress.LocalFlashChipID)
                || (this.ChannelID != referenceAddress.ChannelID)
                || (this.OverallFlashChipID != referenceAddress.OverallFlashChipID)
                )
                return false;
            return true;
        }

        public bool EqualsForInterleaved(IntegerPageAddress referenceAddress)
        {
            if (   (this.DieID == referenceAddress.DieID)
                || (this.LocalFlashChipID != referenceAddress.LocalFlashChipID)
                || (this.ChannelID != referenceAddress.ChannelID)
                || (this.OverallFlashChipID != referenceAddress.OverallFlashChipID)
                )
                return false;
            return true;
        }
    }
}
