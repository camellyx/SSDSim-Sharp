using System;
using System.Collections.Generic;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public class NVMeIODispatcherSimple : NVMeIODispatcherBase
    {
        private uint RequestWindow = 0;
        private uint currentStreamID = 0;
        private uint streamCount = 0;
        private uint[] executingRequestsPerStream;
        private bool dispatcherLocked = false;

        #region SetupFunctions
        public NVMeIODispatcherSimple(string id, FTL ftl, HostInterface HI, uint streamCount, uint requestWindow) : base(id, ftl, HI as HostInterfaceNVMe)
        {
            RequestWindow = requestWindow;
            this.streamCount = streamCount;
            executingRequestsPerStream = new uint[streamCount];
            for (int i = 0; i < streamCount; i++)
                executingRequestsPerStream[i] = 0;
            dispatcherLocked = false;
        }
        public override void SetupDelegates(bool propagateToChilds)
        {
            base.SetupDelegates(propagateToChilds);
            HostInterface.onIORequestArrived += new HostInterfaceNVMe.IORequestArrivedHandler(IORequestArrivedHandler);
            HostInterface.onIORequestCompleted += new HostInterfaceNVMe.RequestCompletedHandler(IORequestCompletedHandler);
        }
        public override void ResetDelegates(bool propagateToChilds)
        {
            HostInterface.onIORequestArrived -= new HostInterfaceNVMe.IORequestArrivedHandler(IORequestArrivedHandler);
            HostInterface.onIORequestCompleted -= new HostInterfaceNVMe.RequestCompletedHandler(IORequestCompletedHandler);

            base.ResetDelegates(propagateToChilds);
        }
        #endregion

        public void DispatchRequests()
        {
            dispatcherLocked = true;

            int cntr = 0;
            while (((HostInterface.InputStreams[currentStreamID].SubmissionQueue.Count - executingRequestsPerStream[currentStreamID]) < 1
                || executingRequestsPerStream[currentStreamID] == RequestWindow) && cntr < streamCount)
            {
                currentStreamID = (currentStreamID + 1) % streamCount;
                cntr++;
            }
            if (cntr == streamCount)
            {
                dispatcherLocked = false;
                return;
            }
            LinkedListNode<IORequest> request = HostInterface.InputStreams[currentStreamID].HeadRequest;
            int possibleNumberofRequests = (int)RequestWindow - (int)executingRequestsPerStream[currentStreamID];
            for (int reqCntr = 0; reqCntr < possibleNumberofRequests && request != null; reqCntr++)
            {
                executingRequestsPerStream[currentStreamID]++;
                HostInterface.InputStreams[currentStreamID].HeadRequest = request.Next;
                HostInterface.SegmentIORequestNoCache_Sprinkler(request.Value);
                FTL.IOScheduler.Schedule((uint)HostInterface.InputStreams[currentStreamID].PriorityClass, currentStreamID);
                request = HostInterface.InputStreams[currentStreamID].HeadRequest;
            }
            currentStreamID = (currentStreamID + 1) % streamCount;

            dispatcherLocked = false;
        }
        protected override void IORequestCompletedHandler(uint streamID)
        {
            executingRequestsPerStream[streamID]--;
            if (!dispatcherLocked)
                DispatchRequests();
        }
        protected override void IORequestArrivedHandler(uint streamID)
        {
            if (!dispatcherLocked)
                DispatchRequests();
        }
    }
}
