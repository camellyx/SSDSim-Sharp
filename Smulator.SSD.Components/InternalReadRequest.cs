using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    public class InternalReadRequest : InternalRequest
    {
        public bool IsUpdate = false; //Is this read request related to another write request and provides update data (for partial page write)
        public InternalWriteRequest RelatedWrite = null;//If this is an update read request, then RelatedWrite points to its related write request
        public LinkedListNode<InternalReadRequest> RelatedNodeInList = null;//Just used for high performance linkedlist insertion/deletion
    }

    public class InternalReadRequestLinkedList : LinkedList<InternalReadRequest>
    {
        public InternalReadRequestLinkedList()
            : base()
        {
        }
    }
}
