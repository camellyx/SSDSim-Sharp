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
	/// <title>GeometricBase0Distribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>			
	public class  GeometricBase0Distribution : RealDistribution
	{
		double mean;

		public GeometricBase0Distribution():this(null, 1.0, 0)
		{
		}

		public GeometricBase0Distribution(string id, double mean, int seed):base(id)
		{
			this.mean = mean;
			this.Seed = seed;
		}

		protected override double GetValueInternal()
		{
			return randomGenerator.Geometric0(mean);
		}

		public override double Mean
		{
			get { return this.mean; }
			set { this.mean = value; }
		}
	}
}
