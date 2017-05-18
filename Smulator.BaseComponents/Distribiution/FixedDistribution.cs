using System;

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
	/// <title>FixedDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>			
	public class  FixedDistribution : RealDistribution
	{
		double fixedValue = 1.0;

		public FixedDistribution():this(1.0)
		{
		}

		public FixedDistribution(Double fixedValue):this(null, fixedValue)
		{
		}

		public FixedDistribution(string id, Double fixedValue):base(id)
		{
			this.fixedValue = fixedValue;
		}

		protected override double GetValueInternal()
		{
			return fixedValue;
		}

		public double FixedValue
		{
			get { return this.fixedValue; }
			set { this.fixedValue = value; }
		}

		[System.Xml.Serialization.XmlIgnore]
		public override double Mean
		{
			get
			{
				return fixedValue;
			}
			set
			{
				fixedValue = value;
			}
		}

	}
}
