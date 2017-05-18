using System;
using System.Collections.Generic;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.SSD.Components
{
    public class DRAMDataCache
    {
        public uint DRAMCapacity;
        public Cache Buffer;
        public bool CachingEnabled = false;
        public ulong DRAMServiceTime = 1000; //1000-ns is the service time of DRAM module
        public FTL _FTL;

        #region SetupFunctions
        public DRAMDataCache(FTL ftl, uint cacheCapacityInSecotr)
        {
            _FTL = ftl;
            DRAMCapacity = cacheCapacityInSecotr;
            Buffer = new Cache(cacheCapacityInSecotr / FTL.SubPageCapacity);
        }
        #endregion

        public void CheckCache(IORequest currentRequest)
        {
          /*  uint index, state;
            uint flag = 0, need_distb_flag, lsn_flag;
            CacheUnit buffer_node;
            CacheUnit key = new CacheUnit();
            uint mask = 0;
            uint offset1 = 0, offset2 = 0;

            bool requestedCompletedInDRAM = true;
            uint full_page = ~(0xffffffff << (int)_FTL.SubpageNoPerPage);
            ulong lsn = currentRequest.LSN;
            ulong lpn = lsn / _FTL.SubpageNoPerPage;
            ulong lastLpn = (lsn + currentRequest.SizeInSubpages - 1) / _FTL.SubpageNoPerPage;
            ulong firstLpn = lpn;

            currentRequest.NeedDistrFlag = new uint[(lastLpn - firstLpn + 1) * _FTL.SubpageNoPerPage / 32 + 1];

            if (currentRequest.Type == IORequestType.Read)
            {
                while (lpn <= lastLpn)
                {
                    need_distb_flag = full_page;
                    key.LPN = lpn;
                    buffer_node = Buffer.Find(key);
                    while ((buffer_node != null) && (lsn < (lpn + 1) * _FTL.SubpageNoPerPage) && (lsn <= (currentRequest.LSN + currentRequest.SizeInSubpages - 1)))
                    {
                        lsn_flag = full_page;
                        mask = (uint)(1 << (int)(lsn % _FTL.SubpageNoPerPage));
                        if (mask > 31)
                        {
                            Console.WriteLine("the subpage number is larger than 32!add some cases");
                            Console.Read();
                        }
                        else if ((buffer_node.ValidSectors & mask) == mask)
                        {
                            flag = 1;
                            lsn_flag = (uint)lsn & (~mask);
                            throw new Exception("ulong casted to uint. Needs considration");
                        }

                        if (flag == 1)
                        {
                            if (Buffer.buffer_head != buffer_node)
                            {
                                if (Buffer.buffer_tail == buffer_node)
                                {
                                    buffer_node.LRU_link_pre.LRU_link_next = null;
                                    Buffer.buffer_tail = buffer_node.LRU_link_pre;
                                }
                                else
                                {
                                    buffer_node.LRU_link_pre.LRU_link_next = buffer_node.LRU_link_next;
                                    buffer_node.LRU_link_next.LRU_link_pre = buffer_node.LRU_link_pre;
                                }
                                buffer_node.LRU_link_next = Buffer.buffer_head;
                                Buffer.buffer_head.LRU_link_pre = buffer_node;
                                buffer_node.LRU_link_pre = null;
                                Buffer.buffer_head = buffer_node;
                            }
                            Buffer.read_hit++;
                            currentRequest.CompleteLSNCount++;
                        }
                        else if (flag == 0)
                        {
                            Buffer.read_miss_hit++;
                        }
                        need_distb_flag = need_distb_flag & lsn_flag;

                        flag = 0;
                        lsn++;
                    }

                    index = (uint)((lpn - firstLpn) / (32 / _FTL.SubpageNoPerPage));
                    currentRequest.NeedDistrFlag[index] = currentRequest.NeedDistrFlag[index] | (need_distb_flag << (int)(((lpn - firstLpn) % (32 / _FTL.SubpageNoPerPage)) * _FTL.SubpageNoPerPage));
                    lpn++;
                }

                for (int j = 0; j <= (int)((lastLpn - firstLpn + 1) * (_FTL.SubpageNoPerPage) / 32); j++)//? maybe: checks that the operation done completely and nothig remain. 
                    if (currentRequest.NeedDistrFlag[j] != 0)
                        requestedCompletedInDRAM = false;
            }
            else// if (currentRequest.Type == IORequestType.Write)
            {
                while (lpn <= lastLpn)
                {
                    mask = ~(0xffffffff << (int)(_FTL.SubpageNoPerPage));
                    state = mask;
                    if (lpn == firstLpn)
                    {
                        offset1 = (uint)((_FTL.SubpageNoPerPage) - ((lpn + 1) * _FTL.SubpageNoPerPage - currentRequest.LSN));
                        state = state & (0xffffffff << (int)offset1);
                    }
                    if (lpn == lastLpn)
                    {
                        offset2 = (uint)(_FTL.SubpageNoPerPage - ((lpn + 1) * _FTL.SubpageNoPerPage - (currentRequest.LSN + currentRequest.SizeInSubpages)));
                        state = state & (~(0xffffffff << (int)offset2));
                    }
                    CacheUpdate(lpn, state, currentRequest);
                    lpn++;
                }
            }

            if (requestedCompletedInDRAM && (currentRequest.InternalRequestList.Count == 0))
                XEngineFactory.XEngine.EventList.InsertXEvent(new XEvent(XEngineFactory.XEngine.Time + this.DRAMServiceTime, _FTL.HostInterface, currentRequest, (int)HostInterface.HostInterfaceEventType.RequestCompletedByDRAM));
*/
        }
        private void CacheUpdate(ulong lpn, uint state, IORequest currentRequest)
        {

            /*ulong write_back_count;
            uint i, hit_flag, add_flag;
            ulong lsn;
            CacheUnit buffer_node = null;
            CacheUnit pt;
            CacheUnit new_node = null;
            CacheUnit key = new CacheUnit();

            uint sub_req_state = 0, sub_req_size = 0;
            ulong sub_req_lpn = 0;

            uint sectorCount = FTL.SizeInSubpages(state);
            key.LPN = lpn;
            buffer_node = Buffer.Find(key);

            if (buffer_node == null)
            {
                if (Buffer.MaxBufferSector - Buffer.OccupiedSectorCount < sectorCount)
                {
                    write_back_count = sectorCount - (Buffer.MaxBufferSector - Buffer.OccupiedSectorCount);
                    Buffer.write_miss_hit += write_back_count;
                    while (write_back_count > 0)
                    {
                        sub_req_state = Buffer.buffer_tail.ValidSectors;
                        sub_req_size = _FTL.SizeInSubpages(Buffer.buffer_tail.ValidSectors);
                        sub_req_lpn = Buffer.buffer_tail.LPN;
                        FTL.CreatFlashTransaction(sub_req_lpn, sub_req_size, sub_req_state, currentRequest);//?create_sub_request done'nt exist.

                        Buffer.OccupiedSectorCount = Buffer.OccupiedSectorCount - sub_req_size;
                        pt = Buffer.buffer_tail;
                        Buffer.Del(pt);
                        if (Buffer.buffer_head.LRU_link_next == null)
                        {
                            Buffer.buffer_head = null;
                            Buffer.buffer_tail = null;
                        }
                        else
                        {
                            Buffer.buffer_tail = Buffer.buffer_tail.LRU_link_pre;
                            Buffer.buffer_tail.LRU_link_next = null;
                        }
                        write_back_count -= sub_req_size;
                    }
                }
                new_node = new CacheUnit();
                new_node.LPN = lpn;
                new_node.ValidSectors = state;
                new_node.DirtyClean = state;
                new_node.LRU_link_pre = null;
                new_node.LRU_link_next = Buffer.buffer_head;
                if (Buffer.buffer_head != null)
                {
                    Buffer.buffer_head.LRU_link_pre = new_node;
                }
                else
                {
                    Buffer.buffer_tail = new_node;
                }
                Buffer.buffer_head = new_node;
                new_node.LRU_link_pre = null;
                Buffer.Add(new_node);
                Buffer.OccupiedSectorCount += sectorCount;
            }

            else
            {
                for (i = 0; i < _FTL.SubpageNoPerPage; i++)
                {
                    if ((state >> (int)i) % 2 != 0)
                    {
                        lsn = lpn * _FTL.SubpageNoPerPage + i;
                        hit_flag = 0;
                        hit_flag = (uint)((buffer_node.ValidSectors) & (0x00000001 << (int)i));

                        if (hit_flag != 0)
                        {
                            if (currentRequest != null)
                            {
                                if (Buffer.buffer_head != buffer_node)
                                {
                                    if (Buffer.buffer_tail == buffer_node)
                                    {
                                        Buffer.buffer_tail = buffer_node.LRU_link_pre;
                                        buffer_node.LRU_link_pre.LRU_link_next = null;
                                    }
                                    else if (buffer_node != Buffer.buffer_head)
                                    {
                                        buffer_node.LRU_link_pre.LRU_link_next = buffer_node.LRU_link_next;
                                        buffer_node.LRU_link_next.LRU_link_pre = buffer_node.LRU_link_pre;
                                    }
                                    buffer_node.LRU_link_next = Buffer.buffer_head;
                                    Buffer.buffer_head.LRU_link_pre = buffer_node;
                                    buffer_node.LRU_link_pre = null;
                                    Buffer.buffer_head = buffer_node;
                                }
                                Buffer.write_hit++;
                                currentRequest.CompleteLSNCount++;//in moteghaiier vojood nadare.
                            }
                        }
                        else
                        {
                            Buffer.write_miss_hit++;
                            if (Buffer.OccupiedSectorCount >= Buffer.MaxBufferSector)
                            {
                                if (buffer_node == Buffer.buffer_tail)
                                {
                                    pt = Buffer.buffer_tail.LRU_link_pre;
                                    Buffer.buffer_tail.LRU_link_pre = pt.LRU_link_pre;
                                    Buffer.buffer_tail.LRU_link_pre.LRU_link_next = Buffer.buffer_tail;
                                    Buffer.buffer_tail.LRU_link_next = pt;
                                    pt.LRU_link_next = null;
                                    pt.LRU_link_pre = Buffer.buffer_tail;
                                    Buffer.buffer_tail = pt;
                                }
                                sub_req_state = Buffer.buffer_tail.ValidSectors;
                                sub_req_size = _FTL.SizeInSubpages(Buffer.buffer_tail.ValidSectors);
                                sub_req_lpn = Buffer.buffer_tail.LPN;
                                FTL.CreatFlashTransaction(sub_req_lpn, sub_req_size, sub_req_state, currentRequest);//create sub request deosn'g exsist

                                Buffer.OccupiedSectorCount = Buffer.OccupiedSectorCount - sub_req_size;
                                pt = Buffer.buffer_tail;
                                Buffer.Del(pt);

                                if (Buffer.buffer_head.LRU_link_next == null)
                                {
                                    Buffer.buffer_head = null;
                                    Buffer.buffer_tail = null;
                                }
                                else
                                {
                                    Buffer.buffer_tail = Buffer.buffer_tail.LRU_link_pre;
                                    Buffer.buffer_tail.LRU_link_next = null;
                                }
                                pt.LRU_link_next = null;
                                pt.LRU_link_pre = null;
                                //AVL_TREENODE_FREE(ssd->dram->buffer, (TREE_NODE*)pt);
                                pt = null;
                            }

                            add_flag = (uint)(0x00000001 << (int)(lsn % _FTL.SubpageNoPerPage));
                            if (Buffer.buffer_head != buffer_node)
                            {
                                if (Buffer.buffer_tail == buffer_node)
                                {
                                    buffer_node.LRU_link_pre.LRU_link_next = null;
                                    Buffer.buffer_tail = buffer_node.LRU_link_pre;
                                }
                                else
                                {
                                    buffer_node.LRU_link_pre.LRU_link_next = buffer_node.LRU_link_next;
                                    buffer_node.LRU_link_next.LRU_link_pre = buffer_node.LRU_link_pre;
                                }
                                buffer_node.LRU_link_next = Buffer.buffer_head;
                                Buffer.buffer_head.LRU_link_pre = buffer_node;
                                buffer_node.LRU_link_pre = null;
                                Buffer.buffer_head = buffer_node;
                            }
                            buffer_node.ValidSectors = buffer_node.ValidSectors | add_flag;
                            buffer_node.DirtyClean = buffer_node.DirtyClean | add_flag;//no dirty clean flag
                            Buffer.OccupiedSectorCount++;
                        }
                    }
                }
            }*/
        }
    }
}
