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
	/// <title>ExpDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>			
	public class  ExpDistribution : RealDistribution
	{
		double mean;

		public ExpDistribution():this(null, 1.0, 0)
		{
		}

		public ExpDistribution(string id, double mean, int seed):base(id)
		{
			this.mean = mean;
			this.Seed = seed;
		}

		protected override double GetValueInternal()
		{
			return randomGenerator.Exponential(mean);
		}

		public override double Mean
		{
			get { return this.mean; }
			set { this.mean = value; }
		}
	}
}
