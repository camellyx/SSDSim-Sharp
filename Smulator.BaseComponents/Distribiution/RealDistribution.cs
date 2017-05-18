using System;
using System.IO;
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
	/// <title>RealDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>		
	
	[XmlInclude(typeof(ErlangDistribution))]
	[XmlInclude(typeof(ExpDistribution))]
	[XmlInclude(typeof(FixedDistribution))]
	[XmlInclude(typeof(GeometricBase0Distribution))]
	[XmlInclude(typeof(GeometricBase1Distribution))]
	[XmlInclude(typeof(HyperExponentialDistribution))]
	[XmlInclude(typeof(HyperGeometricDistribution))]
	[XmlInclude(typeof(InverseDistribution))]
	[XmlInclude(typeof(LogNormalDistribution))]
	[XmlInclude(typeof(NormalDistribution))]
	[XmlInclude(typeof(ParetoDistribution))]
	[XmlInclude(typeof(UniformDistribution))]
	[XmlInclude(typeof(TriangularDistribution))]
	public abstract class RealDistribution : IFieldDump
	{
		string id;
		protected int seed = 0;
		protected RandomGenerator randomGenerator;

		public RealDistribution()
		{
		}

		public RealDistribution(string id)
		{
			this.id = id;
		}

		protected virtual void init()
		{
			int s = seed;
			if ( s == 0 )
				s = SeedGeneratorFactory.GetSeed();
			randomGenerator = new RandomGenerator(s);
		}

		public double GetValue()
		{
			if ( randomGenerator == null )
				init();
			return GetValueInternal();
		}

		protected abstract double GetValueInternal();

		[System.Xml.Serialization.XmlIgnore]
		public abstract double Mean
		{
			get;
			set;
		}
		
		public string Id
		{
			get { return this.id; }
			set { this.id = value; }
		}

/// <summary>
/// If Seed is set to 0, auto seed generator from SeedGeneratorFactory
/// </summary>
		public virtual int Seed
		{
			get { return this.seed; }
			set 
			{ 
				seed = value;
				if (randomGenerator != null)
					init();
			}
		}

        public void Snapshot(TextWriter writer)
        {
            BaseParameterSet.FieldDump(this, writer);
        }
	}
}
