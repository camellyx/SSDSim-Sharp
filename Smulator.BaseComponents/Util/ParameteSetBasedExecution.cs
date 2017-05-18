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
	/// <title>ParameteSetBasedExecution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2006 </copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2006/04/28</date>
	/// </summary>	
	
	public class ParameteSetBasedExecution
	{
        public static void Execute(string[] args, ExecutionParameterSet[] param, string runPath, IParameterSetBasedExecutable network)
		{
			bool usingDefault = false;

			string procName = System.Threading.Thread.CurrentThread.Name;
			if ( args.Length == 0 )
				Console.WriteLine("Usage: {0}.exe <run.xml path> [parameter-set-index]\r\nUsing defaut path : {1}", procName, runPath);
			else
			{
				runPath = args[0];
				Console.WriteLine("Using parameters path : {0}", runPath);
			}
		 
			XmlSerializer serializer = new XmlSerializer(param.GetType());
			StreamReader reader = null;
			try 
			{
				reader = new StreamReader(runPath);
				//reader.XmlResolver = null;
				param = (ExecutionParameterSet [])serializer.Deserialize(reader);
			} 
			catch (Exception ex)
			{
				Console.WriteLine("Can not read from {0} : {1} \r\nUsing default parameters and making {2} file as a sample for you.", 
					runPath,
					ex.Message,
					runPath);
				if ( ex is System.IO.FileNotFoundException ||
					ex.InnerException is System.IO.FileNotFoundException )
					usingDefault = true;
				else
					return;
			} 
			finally
			{
				if (reader != null)
					reader.Close();
			}

			if ( usingDefault )
			{
				TextWriter writer = new StreamWriter(runPath);
				serializer.Serialize(writer, param);
				writer.Close();
			}
						
			Console.WriteLine("Switching to low priority execution ...");
			System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Lowest;

			/*if ( args.Length > 1)
			{
				int paramSet = Int32.Parse(args[1]);
				param = new ExecutionParameterSet[]{param[paramSet]};
			}*/

            for (int i = 0; i < param.Length; i++)
            {
                //network.Build(param[i]);
                network.Simulate1(param[i], (i < param.Length - 1) ? param[i + 1] : null);
            }
		}
	}
}
