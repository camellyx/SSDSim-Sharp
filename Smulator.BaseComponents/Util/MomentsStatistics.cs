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
	/// <title>MomentsStatistics</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2006</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2006/08/11</date>
	/// </summary>			
	public class MomentsStatistics : RealStatistics
	{
		double lastObservationTime;
		bool timedObservation = true;
		long nObservations;
		public double defConfProb = 0.95;

		double[] moments;
		public MomentsStatistics(int nMoments, bool timedObservation, bool isHistoryKeeping)
			:base (isHistoryKeeping)
		{
			moments = new double[nMoments];
			this.timedObservation = timedObservation;
			Clear();
		}

		public MomentsStatistics(int nMoments, bool timedObservation)
			:this(nMoments, timedObservation, false)
		{
		}

        public override void Observe(double val)
        {
            double t;
            double x = val;
            if (timedObservation)
            {
                double now = XEngineFactory.XEngine.Time;
                t = now - lastObservationTime;
                lastObservationTime = now;
            }
            else
                t = 1.0;

            for (int i = 0; i < moments.Length; i++)
            {
                moments[i] += x * t;
                x *= val;
            }
            nObservations++;
            if (IsHistoryKeeping)
            {
                History.Add(val);
            }
        }

		public override void Clear()
		{
			base.Clear ();
			ResetStatistics();
		}

		public override void ResetStatistics()
		{
			base.ResetStatistics ();
			nObservations = 0;
			lastObservationTime = XEngineFactory.XEngine.Time;
		}


		public virtual double GetMoment(int momentNumber)
		{
			if (timedObservation)
				return moments[momentNumber - 1] / (XEngineFactory.XEngine.Time - startTime);
			else
				return moments[momentNumber - 1] / nObservations;		
		}

		public override double GetMean()
		{
			return GetMoment(1);
		}

		public override double GetVariance()
		{
			double ex2 = GetMoment(1);
			return GetMoment(2) - ex2 * ex2;
		}

		public virtual double GetConfidenceInterval(double confProb)
		{
			if (timedObservation)
				return -1;

			double sigma = Math.Sqrt(GetVariance());
			double pc = (1.0 - confProb) / 2;
			long df = this.nObservations - 1;
			double t;
			if (df > 0)
				t = tDistribution(df, pc);
			else
				t = NormalZ(pc);
			return t * sigma;
		}

		/// <summary>
		/// From AKOARA 2.0 code
		/// </summary>
		/// <param name="ndf"></param>
		/// <param name="p"></param>
		/// <returns></returns>
		public double tDistribution(long ndf, double p) 
		{ 
			int i; 
			double z1, z2, x=0.0;
			double[] h = new double[4];

			z1 = Math.Abs(NormalZ(p)); 
			z2 = z1 * z1;
			h[0] = 0.25 * z1 * (z2 + 1.0); 
			h[1] = 0.010416667 * z1 * ((5.0 * z2 + 16.0) * z2 + 3.0);
			h[2] = 0.002604167 * z1 * (((3.0 * z2 + 19.0) * z2 + 17.0) * z2 - 15.0);
			h[3] = z1*((((79.0*z2+776.0)*z2+1482.0)*z2-1920.0)*z2-945.0);
			h[3] *= 0.000010851;
			for (i = 3; i >= 0; i--) 
				x = (x + h[i]) / (ndf);
			z1 += x; 
			if (p > 0.5)
				z1 = -z1;
			return z1;                                                            
		}

		/// <summary>
		/// From AKOARA 2.0 code
		/// </summary>
		/// <param name="p"></param>
		/// <returns></returns>
		/*           COMPUTE pth QUANTILE OF THE NORMAL DISTRIBUTION         	*/

		/* 	This function computes the pth upper quantile of the stand-  	*/  
		/* 	ard normal distribution (i.e., the value of z for which the  	*/
		/* 	are under the curve from z to +infinity is equal to p).  'Z' 	*/
		/* 	is a transliteration of the 'STDZ' function in Appendix C of 	*/   
		/* 	"Principles of Discrete Event Simulation", G. S. Fishman,    	*/
		/* 	Wiley, 1978.   The  approximation used initially appeared in 	*/
		/* 	in  "Approximations for Digital Computers", C. Hastings, Jr.,	*/
		/* 	Princeton U. Press, 1955. 					*/   
		double NormalZ(double p)
			{                                       
				double q, z1, n, d;
				q = (p > 0.5) ? (1 - p) : p;
				double logq = Math.Log(q);
				z1 = Math.Sqrt(-2.0 * logq);                             
				n = (0.010328 * z1 + 0.802853) * z1 + 2.515517;                       
				d = ((0.001308 * z1 + 0.189269) * z1 + 1.43278) * z1 + 1;    
				z1 -= n / d; 
				if (p > 0.5)
					z1 =- z1;                                         
				return z1;
			}

		public virtual void Snapshot(string id, System.Xml.XmlTextWriter writer)
		{
			string id2 = id + "_MomentsStatistics";
			string id3 = id2 + "_Mom";
			string id4 = id2 + "_History";
			string id5 = id4 + "_Item";
			writer.WriteStartElement(id2);
			writer.WriteAttributeString("Mean", GetMean().ToString());
			writer.WriteAttributeString("Var", GetVariance().ToString());
			writer.WriteAttributeString("ConfInt", GetConfidenceInterval(defConfProb).ToString());
			writer.WriteAttributeString("ConfIntProb", defConfProb.ToString());
			writer.WriteAttributeString("LastObservationTime", lastObservationTime.ToString());

			for (int i=0; i < moments.Length; i++)
			{
				writer.WriteStartElement(id3);
				writer.WriteAttributeString("ind", "" + (i + 1));
				writer.WriteAttributeString("mom", GetMoment(i+1).ToString());
				writer.WriteEndElement();		
			}

			if (IsHistoryKeeping)
			{
				writer.WriteStartElement(id4);
				for (int i=0; i < History.Count; i++)
				{
					writer.WriteStartElement(id5);
					writer.WriteAttributeString("ind", "" + (i));
					writer.WriteAttributeString("val", History[i].ToString());
					writer.WriteEndElement();		
				}			
				writer.WriteEndElement();
			}

			writer.WriteEndElement();		
		}
	}
}
