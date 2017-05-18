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
	/// <title>Distributions</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>	
	

	public class  StandardDistributions 
	{
		public enum DistributionEnum
		{
			Fixed=1, Uniform, Exp, Erlang, HyperExponential, Normal, LogNormal, 
			Geometric0, Geometric1, HyperGeometric, Pareto
		};
		
		public static RealDistribution NewDistribution
			(
			string id,
			int seed,
			StdDistributionParameterSet parameters
			)
		{
			return NewDistribution(
				parameters.distributionType, 
				id, seed,
				parameters.mean,
				parameters.stdDev,
				parameters.extraParam0
				);
		}
		
		
		
		public static RealDistribution NewDistribution
			(DistributionEnum distributionEnum, 
			string id,
			int seed,
			double mean,
			double standardDeviation,
			double extraParam0    // shape parameter for example
			)
		{
			switch(distributionEnum) 
			{
				case DistributionEnum.Fixed : return new FixedDistribution(id, mean); 
				case DistributionEnum.Uniform : return new UniformDistribution(id, mean, seed, extraParam0);
				case DistributionEnum.Exp : return new ExpDistribution(id, mean, seed); 
				case DistributionEnum.Erlang : return new ErlangDistribution(id, mean, standardDeviation, seed); 
				case DistributionEnum.HyperExponential : return new HyperExponentialDistribution(id, mean, standardDeviation, seed);
				case DistributionEnum.Normal : return new NormalDistribution(id, mean, standardDeviation, seed); 
				case DistributionEnum.LogNormal : return new LogNormalDistribution(id, mean, standardDeviation, seed); 
				case DistributionEnum.Geometric0 : return new GeometricBase0Distribution(id, mean, seed);
				case DistributionEnum.Geometric1 : return new GeometricBase1Distribution(id, mean, seed);
				case DistributionEnum.HyperGeometric : return new HyperGeometricDistribution(id, mean, standardDeviation, seed);
				case DistributionEnum.Pareto : return new ParetoDistribution(id, extraParam0, mean, seed); 
			}
			return null;
		}
	}
}
