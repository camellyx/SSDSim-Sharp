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
	/// <title>StdDistributionParameterSet</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2006/02/13</date>
	/// </summary>
	
	public class StdDistributionParameterSet : BaseParameterSet
	{
		public double mean = 1.0;
		public double stdDev = 1.5;
		public StandardDistributions.DistributionEnum distributionType = 
			StandardDistributions.DistributionEnum.Exp;
		public double extraParam0 = 1.6;  //shape parameter if any 

		public StdDistributionParameterSet()
		{
		}

		public StdDistributionParameterSet(
			double mean,
			double stdDev,
			StandardDistributions.DistributionEnum distributionType,
			double extraParam0
			)
		{
			this.mean = mean;
			this.stdDev = stdDev;
			this.distributionType = distributionType;
			this.extraParam0 = extraParam0;
		}
	}
}
