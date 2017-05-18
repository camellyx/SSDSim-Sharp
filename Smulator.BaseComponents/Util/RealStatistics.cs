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
	/// <title>RealStatistics</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2006</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2006/08/11</date>
	/// </summary>			
	public abstract class RealStatistics
	{
		protected double startTime;
		bool isHistoryKeeping = false;

		public ArrayList History = new ArrayList();

		public RealStatistics()
		{
		}

		public RealStatistics (bool isHistoryKeeping)
		{
			this.isHistoryKeeping = isHistoryKeeping;
		}

		public abstract void Observe(double val);

		public abstract double GetMean();

		public abstract double GetVariance();

		public virtual void Clear()
		{
			History.Clear();
		}

		public virtual void ResetStatistics()
		{
			History.Clear();
			startTime = XEngineFactory.XEngine.Time;
		}

		
		public double StartTime
		{
			get { return this.startTime; }
			set { this.startTime = value; }
		}

		public bool IsHistoryKeeping
		{
			get { return this.isHistoryKeeping; }
			set { this.isHistoryKeeping = value; }
		}
	}


	public class RealObservation
	{
		double val;
		double time;

		public RealObservation(double val, double time)
		{
			this.val = val;
			this.time = time;
		}
	}
}
