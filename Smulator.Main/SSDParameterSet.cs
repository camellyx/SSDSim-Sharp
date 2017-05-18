using System;
using Smulator.Util;
using Smulator.BaseComponents;
using Smulator.BaseComponents.Distributions;
using Smulator.Disk.SSD.NetworkGenerator;

namespace Smulator.Disk.SSD
{
    /// <summary>
    /// Summary description for MeshParameterSet.
    /// </summary>

    [Serializable]

    public class SSDParameterSet : ExecutionParameterSet
    {
        public ControllerParameterSet ControllerParameters = new ControllerParameterSet();
        public FlashChipParameterSet FlashChipParameters = new FlashChipParameterSet();
        public InterconnectParameterSet NetParameters = new InterconnectParameterSet();

        public SSDParameterSet()
        {
        }
    }
}
