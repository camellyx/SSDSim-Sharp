using System;
using System.Collections;

namespace Smulator.BaseComponents
{
	#region Change info
	/// <author>Abbas Nayebi</author>
	/// <description>Status added.</description>
	/// <date>2006/04/05</date>
	/// <author>Abbas Nayebi</author>
	/// <description>Parent added.</description>
	/// <date>2006/06/14</date>
	/// <author>Abbas Nayebi</author>
	/// <description>ExtraParasmeters added.</description>
	/// <date>2007/04/01</date>
	#endregion 
	/// <summary>
	/// <title>XObject</title>
	/// <description> The main root of inheritance tree for simulation objects
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/27</date>
	/// <version>Version 1.3</version>
	/// <date>2005/08/18</date>
	/// </summary>	

	public class XObject
	{
		MetaComponent parent;
		private string id;
		static int lastId = 0;
		bool delegatesIsSetUp = false;

		public XObject() : this(null)
		{
		}
		
		public XObject(string id)
		{
			if ( id == null )
				id = "X" + ++lastId;
			this.ID = id;

		}

		public string ID
		{
			get
			{
				return id;
			}
			set
			{
				if ( id != null )
					XEngineFactory.XEngine.RemoveXObject(this);
				id = value;
				XEngineFactory.XEngine.RegisterXObject(this);
			}
		}

		/// <summary>
		/// Start is called by XEngine when it starts
		/// </summary>
		public virtual void Start()
		{
		
		}

		/// <summary>
		/// Sets up this objects' delegates and below objects in hierarchy
		/// </summary>
		public virtual void SetupDelegates(bool propagateToChilds)
		{
			delegatesIsSetUp = true;
		}

		/// <summary>
		/// Removes all the delegates of this object and below objects in hierarchy
		/// </summary>
		public virtual void ResetDelegates(bool propagateToChilds)
		{
			delegatesIsSetUp = false;
		}

		/// <summary>
		/// Resets this objects' state
		/// </summary>
		public virtual void ResetState()
		{
		
		}


		/// <summary>
		/// Validates the object throw exception if there is a fatal problem
		/// and log a warning if there is a low risk problem.
		/// Validation is called after SetupDelegates and before Start methods
		/// </summary>
		public virtual void Validate()
		{

		}

		/// <summary>
		/// It is called by XEngine when an event is ready to be processed
		/// </summary>
		public virtual void ProcessXEvent(XEvent e)
		{
		}

		public override string ToString()
		{
			return "Id=" + ID;
		}


		#region Snapshot
		public virtual void Snapshot(System.Xml.XmlTextWriter writer)
		{
			Snapshot("", writer);
		}

		public virtual void Snapshot(string id, System.Xml.XmlTextWriter writer)
		{
			writer.WriteStartElement(id + "_XObject");
			writer.WriteAttributeString("ID", ID.ToString());
			writer.WriteEndElement();
		}
		#endregion
	
		public bool DelegatesIsSetUp
		{
			get { return this.delegatesIsSetUp; }
			set { this.delegatesIsSetUp = value; }
		}

		public MetaComponent Parent
		{
			get { return this.parent; }
			set { this.parent = value as MetaComponent; }
		}
	}
}
