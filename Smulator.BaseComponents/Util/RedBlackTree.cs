using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Smulator.BaseComponents;

namespace Smulator.Util
{
    public class RedBlackNodeLong
	{
        // tree node colors
		/*public static int	RED		= 0;
		public static int	BLACK	= 1;*/

		// key provided by the calling class
        public ulong Key;
		// the data or value associated with the key
        public XEvent FirstXEvent;
        public XEvent LastXEvent;
		// color - used to balance the tree
        public int Color;
		// left node 
        public RedBlackNodeLong Left;
		// right node 
        public RedBlackNodeLong Right;
        // parent node 
        public RedBlackNodeLong Parent;
		
		public RedBlackNodeLong()
		{
			Color = 0;
		}
	}

    public class RedBlackTree
    {
        // the number of nodes contained in the tree
        public int Count;
        // a simple randomized hash code. The hash code could be used as a key
        // if it is "unique" enough. Note: The IComparable interface would need to 
        // be replaced with int.
        private int intHashCode;
        // identifies the owner of the tree
        private string strIdentifier;
        // the tree
        private RedBlackNodeLong rbTree;
        //  sentinelNode is convenient way of indicating a leaf node.
        public static RedBlackNodeLong sentinelNode;
        // the node that was last found; used to optimize searches
        private RedBlackNodeLong lastNodeFound;
        private Random rand = new Random();

        public RedBlackTree() 
        {
            strIdentifier       = base.ToString() + rand.Next();
            intHashCode         = rand.Next();

            // set up the sentinel node. the sentinel node is the key to a successfull
            // implementation and for understanding the red-black tree properties.
            sentinelNode        = new RedBlackNodeLong();
            sentinelNode.Left   = null;
            sentinelNode.Right  = null;
            sentinelNode.Parent = null;
            sentinelNode.Color  = 1;
            rbTree              = sentinelNode;
            lastNodeFound       = sentinelNode;
        }

		///<summary>
		/// Add
		/// args: ByVal key As IComparable, ByVal data As Object
		/// key is object that implements IComparable interface
		/// performance tip: change to use use int type (such as the hashcode)
		///</summary>
        public void Add(ulong key, XEvent data)
		{
			/*if( data == null)
				throw(new RedBlackException("RedBlackNodeLong key and data must not be null"));
			
			if( double.IsNaN(key))
				throw(new RedBlackException("key is NaN"));*/


			// traverse tree - find where node belongs
			// create new node
            RedBlackNodeLong node = new RedBlackNodeLong();
            RedBlackNodeLong temp = rbTree;				// grab the rbTree node of the tree

			while(temp != sentinelNode)
			{	// find Parent
				node.Parent	= temp;
				/*if(result == 0)
					throw(new RedBlackException("A Node with the same key already exists"));*/
				if(key > temp.Key)
					temp = temp.Right;
				else
					temp = temp.Left;
			}
			
            // setup node
			node.Key			=	key;
            node.FirstXEvent    = data;
            node.LastXEvent     = data;
            node.Left           =   sentinelNode;
			node.Right          =   sentinelNode;

			// insert node into tree starting at parent's location
			if(node.Parent != null)	
			{
				if(node.Key > (node.Parent.Key))
					node.Parent.Right = node;
				else
					node.Parent.Left = node;
			}
			else
				rbTree = node;					// first node added

            RestoreAfterInsert(node);           // restore red-black properities

			lastNodeFound = node;

            Count++;
		}
        ///<summary>
        /// RestoreAfterInsert
        /// Additions to red-black trees usually destroy the red-black 
        /// properties. Examine the tree and restore. Rotations are normally 
        /// required to restore it
        ///</summary>
        private void RestoreAfterInsert(RedBlackNodeLong x)
		{   
            // x and y are used as variable names for brevity, in a more formal
            // implementation, you should probably change the names

            RedBlackNodeLong y;

			// maintain red-black tree properties after adding x
			while(x != rbTree && x.Parent.Color == 0)
			{
				// Parent node is .Colored red; 
				if(x.Parent == x.Parent.Parent.Left)	// determine traversal path			
				{										// is it on the Left or Right subtree?
					y = x.Parent.Parent.Right;			// get uncle
					if(y!= null && y.Color == 0)
					{	// uncle is red; change x's Parent and uncle to black
						x.Parent.Color			= 1;
						y.Color					= 1;
						// grandparent must be red. Why? Every red node that is not 
						// a leaf has only black children 
						x.Parent.Parent.Color	= 0;	
						x						= x.Parent.Parent;	// continue loop with grandparent
					}	
					else
					{
						// uncle is black; determine if x is greater than Parent
						if(x == x.Parent.Right) 
						{	// yes, x is greater than Parent; rotate Left
							// make x a Left child
							x = x.Parent;
							RotateLeft(x);
						}
						// no, x is less than Parent
						x.Parent.Color			= 1;	// make Parent black
						x.Parent.Parent.Color	= 0;		// make grandparent black
						RotateRight(x.Parent.Parent);					// rotate right
					}
				}
				else
				{	// x's Parent is on the Right subtree
					// this code is the same as above with "Left" and "Right" swapped
					y = x.Parent.Parent.Left;
					if(y!= null && y.Color == 0)
					{
						x.Parent.Color			= 1;
						y.Color					= 1;
						x.Parent.Parent.Color	= 0;
						x						= x.Parent.Parent;
					}
					else
					{
						if(x == x.Parent.Left)
						{
							x = x.Parent;
							RotateRight(x);
						}
						x.Parent.Color			= 1;
						x.Parent.Parent.Color	= 0;
						RotateLeft(x.Parent.Parent);
					}
				}																													
			}
			rbTree.Color = 1;		// rbTree should always be black
		}
		
		///<summary>
		/// RotateLeft
		/// Rebalance the tree by rotating the nodes to the left
		///</summary>
		public void RotateLeft(RedBlackNodeLong x)
		{
			// pushing node x down and to the Left to balance the tree. x's Right child (y)
			// replaces x (since y > x), and y's Left child becomes x's Right child 
			// (since it's < y but > x).
            
			RedBlackNodeLong y = x.Right;			// get x's Right node, this becomes y

			// set x's Right link
			x.Right = y.Left;					// y's Left child's becomes x's Right child

			// modify parents
			if(y.Left != sentinelNode) 
				y.Left.Parent = x;				// sets y's Left Parent to x

            if(y != sentinelNode)
			    y.Parent = x.Parent;			// set y's Parent to x's Parent

			if(x.Parent != null)		
			{	// determine which side of it's Parent x was on
				if(x == x.Parent.Left)			
					x.Parent.Left = y;			// set Left Parent to y
				else
					x.Parent.Right = y;			// set Right Parent to y
			} 
			else 
				rbTree = y;						// at rbTree, set it to y

			// link x and y 
			y.Left = x;							// put x on y's Left 
			if(x != sentinelNode)						// set y as x's Parent
				x.Parent = y;		
		}
		///<summary>
		/// RotateRight
		/// Rebalance the tree by rotating the nodes to the right
		///</summary>
		public void RotateRight(RedBlackNodeLong x)
		{
			// pushing node x down and to the Right to balance the tree. x's Left child (y)
			// replaces x (since x < y), and y's Right child becomes x's Left child 
			// (since it's < x but > y).
            
			RedBlackNodeLong y = x.Left;			// get x's Left node, this becomes y

			// set x's Right link
			x.Left = y.Right;					// y's Right child becomes x's Left child

			// modify parents
			if(y.Right != sentinelNode) 
				y.Right.Parent = x;				// sets y's Right Parent to x

            if(y != sentinelNode)
                y.Parent = x.Parent;			// set y's Parent to x's Parent

			if(x.Parent != null)				// null=rbTree, could also have used rbTree
			{	// determine which side of it's Parent x was on
				if(x == x.Parent.Right)			
					x.Parent.Right = y;			// set Right Parent to y
				else
					x.Parent.Left = y;			// set Left Parent to y
			} 
			else 
				rbTree = y;						// at rbTree, set it to y

			// link x and y 
			y.Right = x;						// put x on y's Right
			if(x != sentinelNode)				// set y as x's Parent
				x.Parent = y;		
		}		
		///<summary>
		/// GetData
		/// Gets the data object associated with the specified key
		///<summary>
        public XEvent GetData(ulong key)
		{
            RedBlackNodeLong treeNode = rbTree;     // begin at root
            
            // traverse tree until node is found
            while(treeNode != sentinelNode)
			{
				if(key == treeNode.Key)
				{
					lastNodeFound = treeNode;
					return treeNode.FirstXEvent;
				}
				if(key < (treeNode.Key))
					treeNode = treeNode.Left;
				else
					treeNode = treeNode.Right;
			}
			return null;
			//throw(new RedBlackException("RedBlackNodeDouble key was not found"));
		}
        
        public void InsertXEvent(XEvent data)
        {
            if (data.FireTime < XEngineFactory.XEngine.Time)
                throw new XEventException("Event time is before NOW");
            ulong key = data.FireTime;
            RedBlackNodeLong treeNode = rbTree;     // begin at root

            // traverse tree until node is found
            while (treeNode != sentinelNode)
            {
                if (key == treeNode.Key)
                {
                    treeNode.LastXEvent.NextEvent = data;
                    treeNode.LastXEvent = data;
                    return;
                }
                if (key < (treeNode.Key))
                    treeNode = treeNode.Left;
                else
                    treeNode = treeNode.Right;
            }
            this.Add(data.FireTime, data);
        }
		
        ///<summary>
		/// GetMinKey
		/// Returns the minimum key value
		///<summary>
		public ulong GetMinKey()
		{
			return GetMinNode().Key;
		}

		///<summary>
		/// GetMinValue
		/// Returns the object having the minimum key value
		///<summary>
        public XEvent GetMinValue()
		{
//			return GetData(GetMinKey());
			return GetMinNode().FirstXEvent;
		}

		///<summary>
		/// GetMinValue
		/// Returns the object having the minimum key value
		///<summary>
		public RedBlackNodeLong GetMinNode()
		{
			//			return GetData(GetMinKey());
			RedBlackNodeLong treeNode = rbTree;
			
			/*if(treeNode == null || treeNode == sentinelNode)
				throw(new RedBlackException("RedBlack tree is empty"));*/
			
			// traverse to the extreme left to find the smallest key
			while(treeNode.Left != sentinelNode)
				treeNode = treeNode.Left;
			
			lastNodeFound = treeNode;
			
			return treeNode;
		}

		///<summary>
		/// Remove
		/// removes the key and data object (delete)
		///<summary>
		public void Remove(ulong key)
		{
//            if(key == null)
//                throw(new RedBlackException("RedBlackNodeLong key is null"));
//		
			// find node
			RedBlackNodeLong node;

			// see if node to be deleted was the last one found
			if(key == lastNodeFound.Key)
				node = lastNodeFound;
			else
			{	// not found, must search		
				node = rbTree;
				while(node != sentinelNode)
				{
					if(key == lastNodeFound.Key)
						break;
					if(key < lastNodeFound.Key)
						node = node.Left;
					else
						node = node.Right;
				}

				if(node == sentinelNode)
					return;				// key not found
			}

			Delete(node);

            Count--;
		}

        public void Remove(RedBlackNodeLong node)
		{
			Delete(node);
            Count--;
		}

		///<summary>
		/// Delete
		/// Delete a node from the tree and restore red black properties
		///<summary>
		private void Delete(RedBlackNodeLong z)
		{
			// A node to be deleted will be: 
			//		1. a leaf with no children
			//		2. have one child
			//		3. have two children
			// If the deleted node is red, the red black properties still hold.
			// If the deleted node is black, the tree needs rebalancing

			RedBlackNodeLong x = new RedBlackNodeLong();	// work node to contain the replacement node
			RedBlackNodeLong y;					// work node 

			// find the replacement node (the successor to x) - the node one with 
			// at *most* one child. 
			if(z.Left == sentinelNode || z.Right == sentinelNode) 
				y = z;						// node has sentinel as a child
			else 
			{
				// z has two children, find replacement node which will 
				// be the leftmost node greater than z
				y = z.Right;				        // traverse right subtree	
				while(y.Left != sentinelNode)		// to find next node in sequence
					y = y.Left;
			}

			// at this point, y contains the replacement node. it's content will be copied 
			// to the valules in the node to be deleted

			// x (y's only child) is the node that will be linked to y's old parent. 
            if(y.Left != sentinelNode)
                x = y.Left;					
            else
                x = y.Right;					

			// replace x's parent with y's parent and
			// link x to proper subtree in parent
			// this removes y from the chain
			x.Parent = y.Parent;
			if(y.Parent != null)
				if(y == y.Parent.Left)
					y.Parent.Left = x;
				else
					y.Parent.Right = x;
			else
				rbTree = x;			// make x the root node

			// copy the values from y (the replacement node) to the node being deleted.
			// note: this effectively deletes the node. 
			if(y != z) 
			{
				z.Key	= y.Key;
                z.FirstXEvent = y.FirstXEvent;
                z.LastXEvent  = y.LastXEvent;
			}

			if(y.Color == 1)
				RestoreAfterDelete(x);

			lastNodeFound = sentinelNode;
		}

        ///<summary>
        /// RestoreAfterDelete
        /// Deletions from red-black trees may destroy the red-black 
        /// properties. Examine the tree and restore. Rotations are normally 
        /// required to restore it
        ///</summary>
		private void RestoreAfterDelete(RedBlackNodeLong x)
		{
			// maintain Red-Black tree balance after deleting node 			

			RedBlackNodeLong y;

			while(x != rbTree && x.Color == 1) 
			{
				if(x == x.Parent.Left)			// determine sub tree from parent
				{
					y = x.Parent.Right;			// y is x's sibling 
					if(y.Color == 0) 
					{	// x is black, y is red - make both black and rotate
						y.Color			= 1;
						x.Parent.Color	= 0;
						RotateLeft(x.Parent);
						y = x.Parent.Right;
					}
					if(y.Left.Color == 1 && 
						y.Right.Color == 1) 
					{	// children are both black
						y.Color = 0;		// change parent to red
						x = x.Parent;					// move up the tree
					} 
					else 
					{
						if(y.Right.Color == 1) 
						{
							y.Left.Color	= 1;
							y.Color			= 0;
							RotateRight(y);
							y				= x.Parent.Right;
						}
						y.Color			= x.Parent.Color;
						x.Parent.Color	= 1;
						y.Right.Color	= 1;
						RotateLeft(x.Parent);
						x = rbTree;
					}
				} 
				else 
				{	// right subtree - same as code above with right and left swapped
					y = x.Parent.Left;
					if(y.Color == 0) 
					{
						y.Color			= 1;
						x.Parent.Color	= 0;
						RotateRight (x.Parent);
						y = x.Parent.Left;
					}
					if(y.Right.Color == 1 && 
						y.Left.Color == 1) 
					{
						y.Color = 0;
						x		= x.Parent;
					} 
					else 
					{
						if(y.Left.Color == 1) 
						{
							y.Right.Color	= 1;
							y.Color			= 0;
							RotateLeft(y);
							y				= x.Parent.Left;
						}
						y.Color			= x.Parent.Color;
						x.Parent.Color	= 1;
						y.Left.Color	= 1;
						RotateRight(x.Parent);
						x = rbTree;
					}
				}
			}
			x.Color = 1;
		}
		
		///<summary>
		/// RemoveMin
		/// removes the node with the minimum key
		///<summary>
		public void RemoveMin()
		{
            /*if(rbTree == null)
                throw(new RedBlackException("RedBlackNodeLong is null"));*/
            
             Remove(GetMinKey());
		}
		
        ///<summary>
		/// Clear
		/// Empties or clears the tree
		///<summary>
		public void Clear ()
		{
			rbTree      = sentinelNode;
            Count = 0;
		}
    }
}
