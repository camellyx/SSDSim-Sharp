using System;
using Smulator.Util;

namespace Smulator.BaseComponents.Distributions
{
	#region Change info
	/// <change>
	/// <author></author>
	/// <description> </description>
	/// <date></date>
	/// </change>
	#endregion 
	/// <summary>
	/// <title>GeometricBase1Distribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>			
	public class  GeometricBase1Distribution : RealDistribution
	{
		double mean;

		public GeometricBase1Distribution():this(null, 1.0, 0)
		{
		}

		public GeometricBase1Distribution(string id, double mean, int seed):base(id)
		{
			this.mean = mean;
			this.Seed = seed;
		}

		protected override double GetValueInternal()
		{
			return randomGenerator.Geometric1(mean);
		}

		public override double Mean
		{
			get { return this.mean; }
			set { this.mean = value; }
		}
	}
}
