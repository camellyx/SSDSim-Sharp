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
	/// <title>ParetoDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>			
	public class  ParetoDistribution : RealDistribution
	{
		double alpha;
		double beta;

		public ParetoDistribution():this(null, 1.5, 1.0, 0)
		{
		}

		public ParetoDistribution(string id, double alpha, double mean, int seed):base(id)
		{
			this.alpha = alpha;
			this.beta = mean * (alpha - 1.0) / alpha;
			this.Seed = seed;
		}

		protected override double GetValueInternal()
		{
			return randomGenerator.Pareto(alpha, beta);
		}

		public double Beta
		{
			get { return this.beta; }
			set { this.beta = value; }
		}

		public double Alpha
		{
			get { return this.alpha; }
			set { this.alpha = value; }
		}
		
		[System.Xml.Serialization.XmlIgnore]
		public override double Mean
		{
			get
			{
				throw new NotImplementedException();				
			}
			set
			{
				throw new NotImplementedException();
			}
		}

	}
}
