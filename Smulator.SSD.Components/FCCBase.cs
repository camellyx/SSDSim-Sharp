using System;
using System.Collections.Generic;
using System.Text;
using Smulator.BaseComponents;
//:D :D
namespace Smulator.SSD.Components
{
    /// <summary>
    /// Flash Chip Controller: Provides connectivity between flash chips and main SSD controller
    /// </summary>
    public abstract class FCCBase : XObject
    {
        public HostInterface HostInterface;

        //Used by FTL to calculate best optimum multiplane or multidie advanced command issue
        public ulong InterleaveReadSetup = 0, InterleaveProgramSetup = 0;
        public ulong MultiplaneReadSetup = 0, MultiPlaneProgramSetup = 0;
        public ulong ReadTransferCycleTime = 0, WriteTransferCycleTime = 0;//transfer time for

        public FCCBase(string id)
            :base(id)
        {
        }

        #region NetworkFunctions
        /// <summary>
        /// Provides communication between controller and flash chip for a simple read/write/erase command.
        /// </summary>
        /// <param name="internalReq">The internal request that should be sent to the target flash chip.</param>
        public abstract void SendSimpleCommandToChip(InternalRequest internalReq);
        /// <summary>
        /// Provides communication between controller and flash chip for a multiplane or interleaved read command execution.
        /// </summary>
        /// <param name="internalReqList">The list of internal requests that are executed in multiplane or interleaved mode.</param>
        public abstract void SendAdvCommandToChipRD(InternalReadRequest firstInternalReq, InternalReadRequest secondInternalReq);
        /// <summary>
        /// Provides communication between controller and flash chip for a multiplane/interleaved write command execution.
        /// </summary>
        /// <param name="internalReqList">The list of internal requests that are executed in multiplane or interleaved mode.</param>
        public abstract void SendAdvCommandToChipWR(InternalWriteRequestLinkedList internalReqList);
        /// <summary>
        /// Provides communication between controller and flash chip for a multiplane/interleaved erase command execution.
        /// </summary>
        /// <param name="internalReqList">The list of internal requests that are executed in multiplane or interleaved mode.</param>
        public abstract void SendAdvCommandToChipER(InternalCleanRequestLinkedList internalReqList);
        #endregion
    }
}
