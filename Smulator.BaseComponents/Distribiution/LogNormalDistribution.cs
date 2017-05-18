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
	/// <title>LogNormalDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>			
	public class  LogNormalDistribution : RealDistribution
	{
		double mean;
		double stdDev;

		public LogNormalDistribution():this(null, 1.0, 1.0, 0)
		{
		}

		public LogNormalDistribution(string id, double mean, double stdDev, int seed):base(id)
		{
			this.mean = mean;
			this.stdDev = stdDev;
			this.Seed = seed;
		}

		protected override double GetValueInternal()
		{
//TODO: mean and variance must be calculated for log normal
			return 0; //randomGenerator.LogNormal(mean, stdDev);
		}

		public override double Mean
		{
			get { return this.mean; }
			set { this.mean = value; }
		}

		public double StdDev
		{
			get { return this.stdDev; }
			set { this.stdDev = value; }
		}
	}
}
