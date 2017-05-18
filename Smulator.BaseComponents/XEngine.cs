using System;
using System.Collections;
using System.Collections.Generic;
using Smulator.Util;

namespace Smulator.BaseComponents
{
	#region Change info
	/// <change>
	/// <author>Nayebi</author>
	/// <description>Changes to support IEventStorage</description>
	/// <date>2006/04/28</date>
	/// </change>
	#endregion 
	/// <summary>
	/// <title>XEngine</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/27</date>
	/// </summary>	


	public class XEngine
	{
        public ulong Time = 0;
		System.Collections.Hashtable xObjects = new System.Collections.Hashtable();
        public RedBlackTree EventList;
        private bool stop = false;
		
		public XEngine()
		{
            EventList = new RedBlackTree();
		}

        public void Reset()
        {
            xObjects.Clear();
            EventList.Clear();
            Time = 0;
            stop = false;
        }

		public void RegisterXObject(XObject obj)
		{
			if (xObjects.ContainsKey(obj.ID))
				throw new XObjectException("Duplicate XObject key \r\n" + obj.ID + "\r\n");
			xObjects.Add(obj.ID, obj);
		}

		public void RemoveXObject(XObject obj)
		{
			if (! xObjects.ContainsKey(obj.ID))
				throw new XObjectException("Not registered");
			xObjects.Remove(obj.ID);
        }

        /// <summary>
        /// This is the main method of simulator which starts simulation process.
        /// </summary>
        /// <param name="stopTime">
        /// Simulation will continue till virtual time reach to this value.</param>
        /// <param name="steps">
        /// Simulation will continue till this number of XEvents are processed.</param>
        /// <returns>Number of actually processed XEvents.</returns>
        public void StartSimulation()
        {
            #region Prepration
            foreach (Object obj in xObjects.Values)
            {
                if (!((XObject)obj).DelegatesIsSetUp)
                    ((XObject)obj).SetupDelegates(false);
            }

            foreach (Object obj in xObjects.Values)
            {
                ((XObject)obj).Validate();
            }

            foreach (Object obj in xObjects.Values)
            {
                ((XObject)obj).Start();
            }
            #endregion

            XEvent ev;
            while (true)
            {
                if (EventList.Count == 0 || stop)
                    break;

                RedBlackNodeLong minNode = EventList.GetMinNode();
                ev = minNode.FirstXEvent;

                Time = ev.FireTime;

                while (ev != null)
                {
                    ev.TargetXObject.ProcessXEvent(ev);
                    ev = ev.NextEvent;
                }
                EventList.Remove(minNode);
            }
        }

        public void StopSimulation()
        {
            this.stop = true;
        }
	}
}
