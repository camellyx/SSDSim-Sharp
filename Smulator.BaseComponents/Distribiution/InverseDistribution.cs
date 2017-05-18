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
	/// <title>InverseDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2006/09/16</date>
	/// </summary>	
	[Serializable]		
	public class  InverseDistribution : RealDistribution
	{
		double min;
		double max;

		public InverseDistribution()
		{
		}

		public InverseDistribution(string id, double min, double max, int seed):base(id)
		{
			this.Min = min;
			this.Max = max;
			this.Seed = seed;
		}

		protected override double GetValueInternal()
		{
			return randomGenerator.Inverse(Min, Max);
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
				throw new NotImplementedException();				
			}
			set
			{
				throw new NotImplementedException();
			}
		}	
	}
}
