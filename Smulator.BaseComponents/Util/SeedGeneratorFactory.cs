using System;
using System.Collections;
using Smulator.BaseComponents;
using Smulator.Util;


namespace Smulator.Util
{
	#region Change info
	/// <change>
	/// <author></author>
	/// <description> </description>
	/// <date></date>
	/// </change>
	#endregion 
	/// <summary>
	/// <title>SeedGeneratorFactory</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2007</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2007/04/29</date>
	/// </summary>			
	public class SeedGeneratorFactory
	{
		protected static RandomGenerator seedGenrator;

		static SeedGeneratorFactory()
		{
			SetGlobalSeed(1232239);
		}
		
		public static void SetGlobalSeed(int seed)
		{
			seedGenrator = new RandomGenerator(seed);
		}

		public static int GetSeed()
		{
			return seedGenrator.GetInteger(Int16.MaxValue);
		}

		public static RandomGenerator SeedGenrator
		{
			get
			{
				return seedGenrator;
			}
		}

	}
}