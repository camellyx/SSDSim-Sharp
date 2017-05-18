using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

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
	/// <title>IParameterSetBasedExecutable</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2006 </copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2006/04/28</date>
	/// </summary>	
	
	public interface IParameterSetBasedExecutable
	{
		void Build(ExecutionParameterSet param);
		void Simulate(ExecutionParameterSet param, ExecutionParameterSet nextParam);
        void Simulate1(ExecutionParameterSet param, ExecutionParameterSet nextParam);
    }
}
