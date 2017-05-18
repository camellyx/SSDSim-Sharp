using System;
using System.Xml.Serialization;
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
	/// <title>TriangularDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2007/06/05</date>
	/// </summary>	
	[Serializable]		
	public class  TriangularDistribution : RealDistribution
	{
		double min;
		double middle;
		double max;

		public TriangularDistribution()
		{
		}

		public TriangularDistribution(string id, double min, double middle, double max, int seed):base(id)
		{
			this.Min = min;
			this.Middle = middle;
			this.Max = max;
			this.Seed = seed;
		}

		protected override double GetValueInternal()
		{
			return randomGenerator.Triangular(Min, Middle, Max);
		}

		public double Max
		{
			get { return this.max; }
			set { this.max = value; }
		}

		public double Min
		{
			get { return this.min; }
			set { this.min = value; }
		}

		[System.Xml.Serialization.XmlIgnore]
		public override double Mean
		{
			get
			{
				return (min  + middle + max) / 3;				
			}
			set
			{
				throw new NotImplementedException();
			}
		}

		public double Middle
		{
			get { return this.middle; }
			set { this.middle = value; }
		}	
	}
}
