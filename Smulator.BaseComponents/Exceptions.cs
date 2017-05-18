using System;

namespace Smulator
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
	public class GeneralException : Exception
	{
		public GeneralException():base()
		{
		}

		public GeneralException(string message):base(message)
		{
		}
	}

	public class ConnectionException : Exception
	{
		public ConnectionException():base()
		{
		}

		public ConnectionException(string message):base(message)
		{
		}
	}

	public class BufferFullException : Exception
	{
		public BufferFullException():base()
		{
		}

		public BufferFullException(string message):base(message)
		{
		}
	}

	public class BufferEmptyException : Exception
	{
		public BufferEmptyException():base()
		{
		}

		public BufferEmptyException(string message):base(message)
		{
		}
	}

	public class XEventException : Exception
	{
		public XEventException():base()
		{
		}

		public XEventException(string message):base(message)
		{
		}
	}

	public class ValidationException : Exception
	{
		public ValidationException():base()
		{
		}

		public ValidationException(string message):base(message)
		{
		}
	}

	public class XObjectException : Exception
	{
		public XObjectException():base()
		{
		}

		public XObjectException(string message):base(message)
		{
		}
	}

	public class TimeoutException : Exception
	{
		public TimeoutException():base()
		{
		}

		public TimeoutException(string message):base(message)
		{
		}
	}
}
