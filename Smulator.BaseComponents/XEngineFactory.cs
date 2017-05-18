using System;

namespace Smulator.BaseComponents
{
	#region Change info
	/// <change>
	/// <author></author>
	/// <description> </description>
	/// <date></date>
	/// </change>
	#endregion 
	/// <summary>
	/// <title>XEngineFactory</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/27</date>
	/// </summary>	

	public class XEngineFactory
	{
		public static XEngine XEngine = new XEngine();
		
		private XEngineFactory()
		{
		}
	}
}
