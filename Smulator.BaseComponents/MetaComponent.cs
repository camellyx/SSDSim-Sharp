using System;
using System.Collections;
using System.Collections.Generic;

namespace Smulator.BaseComponents
{
    #region Change info
    /// <author>Arash Tavakkol</author>
    /// <description>Private members links and middlePorts removed</description>
    /// <date>2013/01/13</date>
    
    /// <author>Abbas Nayebi</author>
	/// <description>Parent support.</description>
	/// <date>2006/06/14</date>
	#endregion 
	/// <summary>
	/// <title>Network</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/08/18</date>
	/// </summary>
	public class MetaComponent : XObject
	{
		Hashtable xObjects = new Hashtable();

		public MetaComponent():this(null)
		{			
		}

		public MetaComponent(string id):base(id)
		{
		}

		public override void SetupDelegates(bool propagateToChilds)
		{
			base.SetupDelegates (propagateToChilds);
		}

		public object this[string key]
		{
			get
			{
				object ob = xObjects[key];
				if ( ob == null )
					throw new Exception("Meta component " + ID + " has no object with key " + key);
				return ob;
			}
		}

		public void AddXObject(XObject obj)
		{
			xObjects.Add(obj.ID, obj);
			obj.Parent = this;
		}

		public void Clear()
		{
			xObjects.Clear();
		}

		public ICollection XObjects
		{
			get { return this.xObjects.Values; }
		}
	}
}
