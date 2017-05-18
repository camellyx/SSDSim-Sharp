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
	/// <title>HyperGeometricDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>			
	public class  HyperGeometricDistribution : RealDistribution
	{
		double mean;
		double stdDev;

		public HyperGeometricDistribution():this(null, 1.0, 1.5, 0)
		{
		}

		public HyperGeometricDistribution(string id, double mean, double stdDev, int seed):base(id)
		{
			this.mean = mean;
			this.stdDev = stdDev;
			this.Seed = seed;
		}

		protected override double GetValueInternal()
		{
			return randomGenerator.HyperGeometric(mean, stdDev);
		}

		[System.Xml.Serialization.XmlIgnore]
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
