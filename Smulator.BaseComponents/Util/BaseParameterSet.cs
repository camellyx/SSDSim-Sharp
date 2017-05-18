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
	/// <title>BaseParameterSet</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005 </copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/27</date>
	/// </summary>	
	
	public interface IFieldDump
	{
		void Snapshot(TextWriter writer); 
	}

	public class BaseParameterSet : IFieldDump
	{
		static int indent = 0;

		private static void writeLineIndented(TextWriter writer, string text)
		{
			for  (int i=0; i < indent; i++)
				writer.Write("\t");
			writer.WriteLine(text);
		}

		public static void FieldDump(IFieldDump obj, TextWriter writer)
		{
			foreach (System.Reflection.FieldInfo fi in obj.GetType().GetFields())
			{
				if ( fi.GetValue(obj) is IFieldDump )
				{
					writeLineIndented(writer, fi.Name + ":");
					writeLineIndented(writer, "{");
					indent++;
					(fi.GetValue(obj) as IFieldDump).Snapshot(writer);
					indent--;
					writeLineIndented(writer, "}");
				}
				else
					if ( fi.GetValue(obj) is Array )
				{
					writeLineIndented(writer, fi.Name + "[]:");
					writeLineIndented(writer, "{");
					indent++;
					Array ar = fi.GetValue(obj) as Array;
					for (int i=0; i<ar.Length; i++)
					{
					{
						writeLineIndented(writer, ar.GetValue(i).GetType() + "{");
						indent++;
						if (ar.GetValue(i) is IFieldDump)
							(ar.GetValue(i) as IFieldDump).Snapshot(writer);
						else
							writeLineIndented(writer, ar.GetValue(i).ToString());
						indent--;
						writeLineIndented(writer, "}");
					}
					}

					indent--;
					writeLineIndented(writer, "}");
				} 
				else
					writeLineIndented(writer, fi.Name + "=" + fi.GetValue(obj));
			}

			foreach (System.Reflection.PropertyInfo pi in obj.GetType().GetProperties())
			{
				try
				{
					if ( pi.GetValue(obj,null) is IFieldDump )
					{
						writeLineIndented(writer, pi.Name + ":");
						writeLineIndented(writer, "{");
						indent++;
						(pi.GetValue(obj,null) as IFieldDump).Snapshot(writer);
						indent--;
						writeLineIndented(writer, "}");
					}
					else
						if ( pi.GetValue(obj,null) is Array )
						{
							writeLineIndented(writer, pi.Name + "[]:");
							writeLineIndented(writer, "{");
							indent++;
							Array ar = pi.GetValue(obj,null) as Array;
							for (int i=0; i<ar.Length; i++)
							{
								{
									writeLineIndented(writer, "{");
									indent++;
									if (ar.GetValue(i) is IFieldDump)
										(ar.GetValue(i) as IFieldDump).Snapshot(writer);
									else
										writeLineIndented(writer, ar.GetValue(i).ToString());
									indent--;
									writeLineIndented(writer, "}");
								}
							}

							indent--;
							writeLineIndented(writer, "}");
						} 
						else
							writeLineIndented(writer, pi.Name + "=" + pi.GetValue(obj,null));
				}
				catch (Exception e)
				{
					writeLineIndented(writer, pi.Name + ":" + e.Message);
				}
			}
		}

        public void Snapshot(TextWriter writer)
        {
            FieldDump(this, writer);
        }
	}
}
