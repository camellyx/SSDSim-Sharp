using System;
using System.Collections.Generic;
using System.Text;

namespace Smulator.SSD.Components
{
    public class CacheUnit
    {
        public CacheUnit Prev;
        public CacheUnit Next;
        public CacheUnit Root;
        public CacheUnit LeftChild;
        public CacheUnit RightChild;
        public int BF;

        public CacheUnit LRU_link_next;	// next node in LRU list
        public CacheUnit LRU_link_pre;	// previous node in LRU list

        public ulong LPN;                 //the first data logic sector number of a group stored in buffer 
        public uint ValidSectors;         //indicate the sector is stored in buffer or not. 1 indicates the sector is stored and 0 indicate the sector isn't stored.EX.  00110011 indicates the first, second, fifth, sixth sector is stored in buffer.
        public uint DirtyClean;           //it is flag of the data has been modified, one bit indicates one subpage. EX. 0001 indicates the first subpage is dirty
        public int Flag;			      //indicates if this node is the last 20% of the LRU list	
    }
    public abstract class AVLTree
    {
        const int EH_FACTOR = 0;
        const int LH_FACTOR = 1;
        const int RH_FACTOR = -1;
        const int LEFT_MINUS = 0;
        const int RIGHT_MINUS = 1;
        const int INSERT_PREV = 0;
        const int INSERT_NEXT = 1;
        CacheUnit pTreeHeader = null;     				 /*for search target lsn is LRU table*/

        public CacheUnit pListHeader = null;
        public CacheUnit pListTail = null;
        public uint Count;

        public AVLTree()
        {
        }

        public int Height(CacheUnit pNode)
        {
            int lh = 0, rh = 0;
            if (pNode == null)
                return 0;

            lh = Height(pNode.LeftChild);
            rh = Height(pNode.RightChild);

            return (1 + ((lh > rh) ? lh : rh));
        }

        public int Check(CacheUnit pNode)
        {
            int lh = 0, rh = 0;

            if (pNode == null)
                return 0;

            lh = Height(pNode.LeftChild);
            rh = Height(pNode.RightChild);
            if (pNode.BF != lh - rh)
                return 0;

            if (pNode.LeftChild != null && (pNode.LPN <= pNode.LeftChild.LPN))
                return 0;

            if (pNode.RightChild != null && (pNode.LPN >= pNode.RightChild.LPN))
                return 0;

            CacheUnit tree_root = pNode.Root;
            if (tree_root == null && (this.pTreeHeader != pNode))
                return 0;

            if (tree_root != null)
            {
                if ((tree_root.LeftChild != pNode && tree_root.RightChild != pNode) ||
                    (tree_root.LeftChild == pNode && tree_root.RightChild == pNode))
                    return 0;
            }

            if ((pNode.LeftChild != null && pNode.LeftChild.LeftChild.Root != pNode) ||
                (pNode.RightChild != null && pNode.RightChild.Root != pNode))
                return 0;

            if (pNode.LeftChild != null && Check(pNode.LeftChild) == 0)
                return 0;

            if (pNode.RightChild != null && Check(pNode.RightChild) == 0)
                return 0;

            return 1;
        }

        public void R_Rotate(CacheUnit ppNode)
        {
            CacheUnit l_child = null;
            CacheUnit pNode = ppNode;

            l_child = pNode.LeftChild;
            pNode.LeftChild = l_child.RightChild;
            if (l_child.RightChild != null)
                l_child.RightChild.Root = pNode;
            l_child.RightChild = pNode;
            l_child.Root = pNode.Root;
            pNode.Root = l_child;
            ppNode = l_child;
        }

        public void L_Rotate(CacheUnit ppNode)
        {
            CacheUnit r_child = null;
            CacheUnit pNode = ppNode;

            r_child = pNode.RightChild;
            pNode.RightChild = r_child.LeftChild;
            if (r_child.LeftChild != null)
                r_child.LeftChild.Root = pNode;
            r_child.LeftChild = pNode;
            r_child.Root = pNode.Root;
            pNode.Root = r_child;
            ppNode = r_child;
        }

        public void LeftBalance(CacheUnit ppNode)
        {
            CacheUnit left_child = null;
            CacheUnit right_child = null;
            CacheUnit tree_root = null;
            CacheUnit pNode = ppNode;

            tree_root = pNode.Root;
            left_child = pNode.LeftChild;
            switch (left_child.BF)
            {
                case LH_FACTOR:
                    pNode.BF = left_child.BF = EH_FACTOR;
                    R_Rotate(ppNode);
                    break;
                case RH_FACTOR:
                    right_child = left_child.RightChild;
                    switch (right_child.BF)
                    {
                        case LH_FACTOR:
                            pNode.BF = RH_FACTOR;
                            left_child.BF = EH_FACTOR;
                            break;
                        case EH_FACTOR:
                            pNode.BF = left_child.BF = EH_FACTOR;
                            break;
                        case RH_FACTOR:
                            pNode.BF = EH_FACTOR;
                            left_child.BF = LH_FACTOR;
                            break;
                    }
                    right_child.BF = EH_FACTOR;
                    L_Rotate(pNode.LeftChild);
                    R_Rotate(ppNode);
                    break;
                case EH_FACTOR:
                    pNode.BF = LH_FACTOR;
                    left_child.BF = RH_FACTOR;
                    R_Rotate(ppNode);
                    break;
            }
            ppNode.Root = tree_root;
            if (tree_root != null && tree_root.LeftChild == pNode)
                tree_root.LeftChild = ppNode;
            if (tree_root != null && tree_root.RightChild == pNode)
                tree_root.RightChild = ppNode;
        }

        public void RightBalance(CacheUnit ppNode)
        {
            CacheUnit left_child = null;
            CacheUnit right_child = null;
            CacheUnit tree_root = null;
            CacheUnit pNode = ppNode;

            tree_root = pNode.Root;
            right_child = pNode.RightChild;
            switch (right_child.BF)
            {
                case RH_FACTOR:
                    pNode.BF = right_child.BF = EH_FACTOR;
                    L_Rotate(ppNode);
                    break;
                case LH_FACTOR:
                    left_child = right_child.LeftChild;
                    switch (left_child.BF)
                    {
                        case RH_FACTOR:
                            pNode.BF = LH_FACTOR;
                            right_child.BF = EH_FACTOR;
                            break;
                        case EH_FACTOR:
                            pNode.BF = right_child.BF = EH_FACTOR;
                            break;
                        case LH_FACTOR:
                            pNode.BF = EH_FACTOR;
                            right_child.BF = RH_FACTOR;
                            break;
                    }
                    left_child.BF = EH_FACTOR;
                    R_Rotate(pNode.RightChild);
                    L_Rotate(ppNode);
                    break;
                case EH_FACTOR:
                    pNode.BF = RH_FACTOR;
                    right_child.BF = LH_FACTOR;
                    L_Rotate(ppNode);
                    break;
            }
            ppNode.Root = tree_root;
            if (tree_root != null && tree_root.LeftChild == pNode)
                tree_root.LeftChild = ppNode;
            if (tree_root != null && tree_root.RightChild == pNode)
                tree_root.RightChild = ppNode;
        }

        public int avlDelBalance(CacheUnit pNode, int L_R_MINUS)
        {
            CacheUnit tree_root = null;

            tree_root = pNode.Root;
            if (L_R_MINUS == LEFT_MINUS)
            {
                switch (pNode.BF)
                {
                    case EH_FACTOR:
                        pNode.BF = RH_FACTOR;
                        break;
                    case RH_FACTOR:
                        RightBalance(pNode);
                        if (tree_root == null)
                            this.pTreeHeader = pNode;
                        if (pNode.Root != null && pNode.BF == EH_FACTOR)
                        {
                            if (pNode.Root.LeftChild == pNode)
                                avlDelBalance(pNode.Root, LEFT_MINUS);
                            else
                                avlDelBalance(pNode.Root, RIGHT_MINUS);
                        }
                        break;
                    case LH_FACTOR:
                        pNode.BF = EH_FACTOR;
                        if (pNode.Root != null && pNode.BF == EH_FACTOR)
                        {
                            if (pNode.Root.LeftChild == pNode)
                                avlDelBalance(pNode.Root, LEFT_MINUS);
                            else
                                avlDelBalance(pNode.Root, RIGHT_MINUS);
                        }
                        break;
                }
            }

            if (L_R_MINUS == RIGHT_MINUS)
            {
                switch (pNode.BF)
                {
                    case EH_FACTOR:
                        pNode.BF = LH_FACTOR;
                        break;
                    case LH_FACTOR:
                        LeftBalance(pNode);
                        if (tree_root == null)
                            this.pTreeHeader = pNode;
                        if (pNode.Root != null && pNode.BF == EH_FACTOR)
                        {
                            if (pNode.Root.LeftChild == pNode)
                                avlDelBalance(pNode.Root, LEFT_MINUS);
                            else
                                avlDelBalance(pNode.Root, RIGHT_MINUS);
                        }
                        break;
                    case RH_FACTOR:
                        pNode.BF = EH_FACTOR;
                        if (pNode.Root != null && pNode.BF == EH_FACTOR)
                        {
                            if (pNode.Root.LeftChild == pNode)
                                avlDelBalance(pNode.Root, LEFT_MINUS);
                            else
                                avlDelBalance(pNode.Root, RIGHT_MINUS);
                        }
                        break;
                }
            }

            return 1;
        }

        public int orderListInsert(CacheUnit pNode, CacheUnit pInsertNode, int prev_or_next)
        {
            CacheUnit p = null;

            if (pNode == null)
                return 0;

            if (prev_or_next == INSERT_PREV)
            {
                p = pNode.Prev;
                if (p != null) p.Next = pInsertNode;
                else this.pListHeader = pInsertNode;

                pInsertNode.Prev = p;
                pInsertNode.Next = pNode;
                pNode.Prev = pInsertNode;
            }

            if (prev_or_next == INSERT_NEXT)
            {
                p = pNode.Next;
                if (p != null) p.Prev = pInsertNode;
                else this.pListTail = pInsertNode;

                pInsertNode.Prev = pNode;
                pInsertNode.Next = p;
                pNode.Next = pInsertNode;
            }
            return 1;
        }

        public int orderListRemove(CacheUnit pRemoveNode)
        {
            CacheUnit pPrev = null;
            CacheUnit pNext = null;

            if (pRemoveNode == null)
                return 0;

            pPrev = pRemoveNode.Prev;
            pNext = pRemoveNode.Next;
            if (pPrev == null && pNext == null)
            {
                this.pListHeader = this.pListTail = null;
                return 1;
            }
            if (pPrev != null && pNext != null)
            {
                pPrev.Next = pNext;
                pNext.Prev = pPrev;
                return 1;
            }

            if (pPrev != null)
            {
                pPrev.Next = null;
                this.pListTail = pPrev;
                return 1;
            }

            if (pNext != null)
            {
                pNext.Prev = null;
                this.pListHeader = pNext;
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public CacheUnit First()
        {
            if (this.Count == 0 || this.pTreeHeader == null)
                return null;

            return this.pListHeader;
        }

        public CacheUnit Last()
        {

            if (this.Count == 0 || this.pTreeHeader == null)
                return null;

            return this.pListTail;
        }

        public CacheUnit Next(CacheUnit pNode)
        {
            if (pNode == null)
                return null;

            return pNode.Next;
        }

        public CacheUnit Prev(CacheUnit pNode)
        {
            if (pNode == null)
                return null;

            return pNode.Prev;
        }

        public int Insert(CacheUnit ppNode, CacheUnit pInsertNode, ref int growthFlag)
        {
            int compFlag = 0;
            CacheUnit pNode = ppNode;

            if (this.Count == 0)
            {
                this.pTreeHeader = pInsertNode;
                pInsertNode.BF = EH_FACTOR;
                pInsertNode.LeftChild = pInsertNode.RightChild = null;
                pInsertNode.Root = null;
                this.pListHeader = this.pListTail = pInsertNode;
                pInsertNode.Prev = pInsertNode.Next = null;
                return 1;
            }

            if (pNode.LPN < pInsertNode.LPN)
                compFlag = 1;
            else if (pNode.LPN > pInsertNode.LPN)
                compFlag = -1;

            if (compFlag == 0)
            {
                growthFlag = 0;
                return 0;
            }

            if (compFlag < 0)
            {
                if (pNode.LeftChild == null)
                {
                    pNode.LeftChild = pInsertNode;
                    pInsertNode.BF = EH_FACTOR;
                    pInsertNode.LeftChild = pInsertNode.RightChild = null;
                    pInsertNode.Root = pNode;
                    orderListInsert(pNode, pInsertNode, INSERT_PREV);
                    switch (pNode.BF)
                    {
                        case EH_FACTOR:
                            pNode.BF = LH_FACTOR;
                            growthFlag = 1;
                            break;
                        case RH_FACTOR:
                            pNode.BF = EH_FACTOR;
                            growthFlag = 0;
                            break;
                    }
                }
                else
                {
                    if (Insert(pNode.LeftChild, pInsertNode, ref growthFlag) == 0)
                        return 0;

                    if (growthFlag != 0)
                    {
                        switch (pNode.BF)
                        {
                            case LH_FACTOR:
                                LeftBalance(ppNode);
                                growthFlag = 0;
                                break;
                            case EH_FACTOR:
                                pNode.BF = LH_FACTOR;
                                growthFlag = 1;
                                break;
                            case RH_FACTOR:
                                pNode.BF = EH_FACTOR;
                                growthFlag = 0;
                                break;
                        }
                    }
                }
            }

            if (compFlag > 0)
            {
                if (pNode.RightChild == null)
                {
                    pNode.RightChild = pInsertNode;
                    pInsertNode.BF = EH_FACTOR;
                    pInsertNode.LeftChild = pInsertNode.RightChild = null;
                    pInsertNode.Root = pNode;
                    orderListInsert(pNode, pInsertNode, INSERT_NEXT);
                    switch (pNode.BF)
                    {
                        case EH_FACTOR:
                            pNode.BF = RH_FACTOR;
                            growthFlag = 1;
                            break;
                        case LH_FACTOR:
                            pNode.BF = EH_FACTOR;
                            growthFlag = 0;
                            break;
                    }
                }
                else
                {
                    if (Insert(pNode.RightChild, pInsertNode, ref growthFlag) != 0)
                        return 0;

                    if (growthFlag != 0)
                    {
                        switch (pNode.BF)
                        {
                            case LH_FACTOR:
                                pNode.BF = EH_FACTOR;
                                growthFlag = 0;
                                break;
                            case EH_FACTOR:
                                pNode.BF = RH_FACTOR;
                                growthFlag = 1;
                                break;
                            case RH_FACTOR:
                                RightBalance(ppNode);
                                growthFlag = 0;
                                break;
                        }
                    }
                }
            }

            return 1;
        }

        public int Remove(CacheUnit pRemoveNode)
        {
            CacheUnit tree_root = null;
            CacheUnit p = null;
            CacheUnit root_p = null;
            CacheUnit swapNode;

            tree_root = pRemoveNode.Root;

            if (pRemoveNode.LeftChild == null && pRemoveNode.RightChild == null)
            {
                if (tree_root == null)
                {
                    this.pTreeHeader = null;
                    this.pListHeader = this.pListTail = null;
                    return 1;
                }
                else if (tree_root.LeftChild == pRemoveNode)
                {
                    orderListRemove(pRemoveNode);
                    tree_root.LeftChild = null;
                    avlDelBalance(tree_root, LEFT_MINUS);
                }
                else
                {
                    orderListRemove(pRemoveNode);
                    tree_root.RightChild = null;
                    avlDelBalance(tree_root, RIGHT_MINUS);
                }
            }

            if (pRemoveNode.LeftChild != null && pRemoveNode.RightChild != null)
            {
                CacheUnit prev = null;
                CacheUnit next = null;
                CacheUnit r_child = null;
                root_p = pRemoveNode;
                p = pRemoveNode.RightChild;

                while (p.LeftChild != null)
                {
                    root_p = p;
                    p = p.LeftChild;
                }
                if (p == pRemoveNode.RightChild)
                {
                    p.Root = p;
                    pRemoveNode.RightChild = pRemoveNode;
                }

                swapNode = p;
                prev = p.Prev;
                next = p.Next;
                p = pRemoveNode;
                p.Prev = prev;
                p.Next = next;
                prev = pRemoveNode.Prev;
                next = pRemoveNode.Next;
                pRemoveNode = swapNode;
                pRemoveNode.Prev = prev;
                pRemoveNode.Next = next;

                if (tree_root == null)
                    this.pTreeHeader = p;
                else if (tree_root.LeftChild == pRemoveNode)
                    tree_root.LeftChild = p;
                else
                    tree_root.RightChild = p;

                if (p.LeftChild != null)
                    p.LeftChild.Root = p;
                if (p.RightChild != null)
                    p.RightChild.Root = p;

                if (pRemoveNode.LeftChild != null)
                    pRemoveNode.LeftChild.Root = pRemoveNode;
                if (pRemoveNode.RightChild != null)
                    pRemoveNode.RightChild.Root = pRemoveNode;

                if (root_p != pRemoveNode)
                {
                    if (root_p.LeftChild == p)
                        root_p.LeftChild = pRemoveNode;
                    else
                        root_p.RightChild = pRemoveNode;
                }

                return Remove(pRemoveNode);
            }

            if (pRemoveNode.LeftChild != null)
            {
                orderListRemove(pRemoveNode);
                if (tree_root == null)
                {
                    this.pTreeHeader = pRemoveNode.LeftChild;
                    pRemoveNode.LeftChild.Root = null;
                    return 1;
                }

                if (tree_root.LeftChild == pRemoveNode)
                {
                    tree_root.LeftChild = pRemoveNode.LeftChild;
                    pRemoveNode.LeftChild.Root = tree_root;
                    avlDelBalance(tree_root, LEFT_MINUS);
                }
                else
                {
                    tree_root.RightChild = pRemoveNode.LeftChild;
                    pRemoveNode.LeftChild.Root = tree_root;
                    avlDelBalance(tree_root, RIGHT_MINUS);
                }

                return 1;
            }

            if (pRemoveNode.RightChild != null)
            {
                orderListRemove(pRemoveNode);
                if (tree_root == null)
                {
                    this.pTreeHeader = pRemoveNode.RightChild;
                    pRemoveNode.RightChild.Root = null;
                    return 1;
                }

                if (tree_root.LeftChild == pRemoveNode)
                {
                    tree_root.LeftChild = pRemoveNode.RightChild;
                    pRemoveNode.RightChild.Root = tree_root;
                    avlDelBalance(tree_root, LEFT_MINUS);
                }
                else
                {
                    tree_root.RightChild = pRemoveNode.RightChild;
                    pRemoveNode.RightChild.Root = tree_root;
                    avlDelBalance(tree_root, RIGHT_MINUS);
                }

                return 1;
            }

            return 1;
        }

        public CacheUnit Lookup(CacheUnit pNode, CacheUnit pSearchKey)
        {
            int compFlag = 0;
            if (pNode == null)
                return null;

            if (pNode.LPN < pSearchKey.LPN)
                compFlag = 1;
            else if (pNode.LPN > pSearchKey.LPN)
                compFlag = -1;
            
            if (compFlag == 0)
                return pNode;

            if (compFlag > 0) pNode = pNode.RightChild;
            else pNode = pNode.LeftChild;

            return Lookup(pNode, pSearchKey);
        }

        public int Del(CacheUnit pDelNode)
        {
            int ret = 0;

            if (pDelNode == null || this.Count == 0)
                return 0;

            ret = Remove(pDelNode);
            if (ret != 0)
                this.Count--;

            return 1;
        }

        public int Add(CacheUnit pInsertNode)
        {
            int growthFlag = 0, ret = 0;

            if (pInsertNode == null)
                return 0;

            ret = Insert(this.pTreeHeader, pInsertNode, ref growthFlag);
            if (ret != 0)
                this.Count++;
            return ret;
        }

        public CacheUnit Find(CacheUnit pKeyNode)
        {
            if (this.Count ==0 || this.pTreeHeader == null)
                return null;

            return Lookup(this.pTreeHeader, pKeyNode);
        }
    }
    public class Cache : AVLTree
    {
        public ulong read_hit;
        public ulong read_miss_hit;
        public ulong write_hit;
        public ulong write_miss_hit;

        public CacheUnit buffer_head;
        public CacheUnit buffer_tail;
        public CacheUnit BufferGroup;

        public uint MaxBufferSector;
        public uint OccupiedSectorCount;
        public Cache(uint maxBufferSector)
        {
            MaxBufferSector = maxBufferSector;
        }
    }

}
