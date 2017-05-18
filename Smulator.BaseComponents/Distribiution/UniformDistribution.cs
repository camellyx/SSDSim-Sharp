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
	/// <title>UniformDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/07/10</date>
	/// </summary>			
	public class  UniformDistribution : RealDistribution
	{
		double lowerBound;
		double upperBound;

		public UniformDistribution()
		{
		}

		public UniformDistribution(string id, double mean, int seed):this(id, mean, seed, 0)
		{
		}

		public UniformDistribution(string id, double mean, int seed,double lowerBound):base(id)
		{
			this.lowerBound = lowerBound;
			this.Mean = mean;
			this.Seed = seed;
		}

		protected override double GetValueInternal()
		{
			return randomGenerator.Uniform(lowerBound, upperBound);
		}

		[System.Xml.Serialization.XmlIgnore]
		public override double Mean
		{
			get { return (lowerBound + upperBound) / 2; }
			set { 
				this.upperBound =  lowerBound + 2.0 * (value-lowerBound);
			}
		}

		public double UpperBound
		{
			get { return this.upperBound; }
			set { this.upperBound = value; }
		}

		public double LowerBound
		{
			get { return this.lowerBound; }
			set { this.lowerBound = value; }
		}
	}
}
