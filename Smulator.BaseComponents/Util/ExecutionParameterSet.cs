using System;
using System.Xml.Serialization;
using System.IO;
using Smulator.BaseComponents;
using Smulator.BaseComponents.Distributions;


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
    /// <title>ExecutionParameterSet</title>
    /// <description> 
    /// </description>
    /// <copyright>Copyright(c)2007 </copyright>
    /// <company></company>
    /// <author>Abbas Nayebi ( www.nayebi.com )</author>
    /// <version>Version 1.0</version>
    /// <date>2007/03/29</date>
    /// </summary>	

    public class NotListedExecutionParameterset
    {
        public static string outputPath;
        public static string responseTimeLoggingFile;
        public static string responseTimeAnalysisFile;
    }
    public class ExecutionParameterSet : BaseParameterSet
    {
    }
}
