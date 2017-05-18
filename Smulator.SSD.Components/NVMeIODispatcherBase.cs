using System;
using System.Collections.Generic;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public abstract class NVMeIODispatcherBase : XObject
    {
        protected FTL FTL;
        protected HostInterfaceNVMe HostInterface;

        public NVMeIODispatcherBase(string id, FTL ftl, HostInterfaceNVMe HI)
            : base(id)
        {
            FTL = ftl;
            HostInterface = HI;
        }
        protected abstract void IORequestCompletedHandler(uint streamID);
        protected abstract void IORequestArrivedHandler(uint streamID);
    }
}
