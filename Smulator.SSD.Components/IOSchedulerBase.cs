using System;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public enum IOSchedulingPolicy { Sprinkler, MultiStageAllFair, MultiStageSoftQoS, MultiStageMultiplePriorities };
    public abstract class IOSchedulerBase : XObject
    {
        protected FTL _FTL;

        public IOSchedulerBase(string id, FTL ftl) : base(id)
        {
            _FTL = ftl;
        }
        public abstract void Schedule(uint priorityClass, uint streamID);
        public abstract void OnBusChannelIdle(BusChannelBase channel);
        public abstract void OnFlashchipIdle(FlashChip targetFlashchip);
    }
}
