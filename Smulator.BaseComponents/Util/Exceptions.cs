using System;

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
	/// <title>Exceptions</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/27</date>
	/// </summary>
	public class NoSuchElementException : Exception
	{
		public NoSuchElementException():base()
		{
		}

		public NoSuchElementException(string message):base(message)
		{
		}
	}

	
	public class IndexOutOfBoundsException : Exception
	{
		public IndexOutOfBoundsException():base()
		{
		}

		public IndexOutOfBoundsException(string message):base(message)
		{
		}
	}

	public class RandomGenerationException : Exception
	{
		public RandomGenerationException():base()
		{
		}

		public RandomGenerationException(string message):base(message)
		{
		}
	}
}
