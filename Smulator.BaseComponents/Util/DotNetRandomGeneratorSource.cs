using System;
using System.Collections;

namespace Smulator.Util
{
	#region Change info
	/// <change>
	/// <author></author>
	/// <description> </description>
	/// <date></date>
	/// </change>
	#endregion 
	/// <summary>
	/// <title>DotNetRandomGeneratorSource</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005
	/// </copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/09/02</date>
	/// </summary>
	public class DotNetRandomGeneratorSource : IRandomGeneratorSource
	{

		System.Random rand;

		public DotNetRandomGeneratorSource(int seed)
		{ 
			rand = new Random(seed);
		}

		public double NextDouble() 
		{
			return rand.NextDouble();
		}
	}
}
