using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;

namespace Smulator.SSD.Components
{
    public class ResourceAccessTableEntry
    {
        public uint WaitingReadCount = 0;
        public uint WaitingWriteCount = 0;
        public uint WaitingGCReadCount = 0;
        public uint WaitingGCWriteCount = 0;
        public uint WaitingGCReqsCount = 0;
        public Hashtable WaitingReadRequests = new Hashtable();
        public Hashtable WaitingWriteRequests = new Hashtable();
        public InternalReadRequest OnTheFlyRead = null;
        public InternalWriteRequest OnTheFlyWrite = null;
        public InternalCleanRequest OnTheFlyErase = null;
    }
    public class RPBResourceAccessTable
    {
        public ResourceAccessTableEntry[][][][] PlaneEntries;

        public ResourceAccessTableEntry[][][] DieEntries;

        public ResourceAccessTableEntry[] ChannelEntries;
    }
}
