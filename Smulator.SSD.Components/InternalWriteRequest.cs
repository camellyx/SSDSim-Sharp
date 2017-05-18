using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    public class InternalWriteRequest : InternalRequest
    {
        public InternalReadRequest UpdateRead = null;//If a write request has update read, this field is used to point to the regarding update request
        public LinkedListNode<InternalWriteRequest> RelatedNodeInList = null;//Just used for high performance linkedlist insertion/deletion
    }

    public class InternalWriteRequestLinkedList : LinkedList<InternalWriteRequest>
    {
        public InternalWriteRequestLinkedList()
            : base()
        {
        }
    }
}
