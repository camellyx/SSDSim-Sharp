using System;

namespace Smulator.Util
{
	#region Change info
	/// <change>
	/// <author>Abbas Nayebi</author>
	/// <description>Triangular dist. added </description>
	/// <date>2007/06/05</date>
	/// </change>
	#endregion 
	/// <summary>
	/// <title>RandomGenerator</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005
	/// Part of this code is converted from Akaroa 2.7.4
	/// </copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/06/28</date>
	/// </summary>
	public class RandomGenerator
	{

		public enum RandomGeneratorType {DotNet, CMR};

		IRandomGeneratorSource rand;

		public RandomGenerator(int seed) : this (seed, RandomGenerator.RandomGeneratorType.CMR) 
		{
		}

		public RandomGenerator(int seed, RandomGeneratorType randomGeneratorType)
		{
			if ( randomGeneratorType == RandomGeneratorType.DotNet )
                rand = new DotNetRandomGeneratorSource(seed);
			else if ( randomGeneratorType == RandomGeneratorType.CMR )
				rand = new CMRRandomGeneratorSource(seed / 200 + 1, seed % 200);
		}
		
		public uint GetInteger(uint maxValue)
		{
			uint v = (uint) (FloatRandom() * (maxValue + 1));
			//Console.WriteLine(v);
			return v;
		}

        public int GetInteger(int maxValue)
        {
            int v = (int)(FloatRandom() * (maxValue + 1));
            //Console.WriteLine(v);
            return v;
        }

		public double FloatRandom() 
		{
			return rand.NextDouble();
		}
  

		public double Uniform(double a, double b) 
		{
			return a + (b - a) * FloatRandom();
		}

		/*
		 *   Return a random integer uniformly distributed
		 *   in the range m to n, inclusive
		 */

		public long UniformInt(long m, long n) 
		{
			return m + (long)((n - m + 1.0) * FloatRandom());
		}

        public uint UniformUInt(uint m, uint n)
        {
            return m + (uint)((n - m + 1.0) * FloatRandom());
        }

        public ulong UniformULong(ulong m, ulong n)
        {
            return m + (ulong)((n - m + 1.0) * FloatRandom());
        }

		/*
		 *   Return a random double number from a negative exponential
		 *   distribution with mean 'mean'
		 */

		public double Exponential(double mean) 
		{
			return - mean * Math.Log(FloatRandom());
		}

		/*
		 *   Return a random double number from an erlang distribution
		 *   with mean x and standard deviation s
		 */

		public double Erlang(double x, double s) 
		{
			long i, k;
			double z;
			z = x / s; 
			k = (long)((long)(z) * z);
			z = 1.0; 
			for (i = 0; i < k; i++) 
				z *= FloatRandom();
			return -(x / k) * Math.Log(z);
		}

		/*
		 *   Return a random double number from a hyperexponential
		 *   distribution with mean x and standard deviation s > x
		 */

		public double HyperExponential(double x, double s) 
		{
			if ( s < x )
				throw new RandomGenerationException("HyperExponential : Mean must be greater than standard deviation.");

			double cv,z,p;   
			cv = s / x; 
			z = cv * cv; 
			p = 0.5 * (1.0 - Math.Sqrt((z - 1.0) / (z + 1.0)));
			z = (FloatRandom() > p) ? (x / (1.0 - p)) : (x / p);
			return -0.5 * z * Math.Log(FloatRandom());
		}

		/*
		 *    Return a random double number from a normal 
		 *    distribution with mean x and standard deviation s
		 */

		double Normal_z2 = 0.0;
		public double Normal(double x, double s) 
		{
			double v1, v2, w, z1; 
			if (Normal_z2 != 0.0) 
			{
				/* use value from previous call */
				z1 = Normal_z2; 
				Normal_z2 = 0.0;
			}
			else 
			{
				do 
				{
					v1 = 2.0 * FloatRandom() -1.0; 
					v2 = 2.0 * FloatRandom() - 1.0; 
					w = v1 * v1 + v2 * v2;
				} while (w >= 1.0);
				w = Math.Sqrt((-2.0*Math.Log(w))/w); 
				z1 = v1 * w; 
				Normal_z2 = v2 * w;
			}
			return x + z1 * s;
		}

		/*
		 *    Return a random double number from a log-normal 
		 *    distribution with mean x and standard deviation s.
		 */

		public double LogNormal(double x, double s) 
		{
			return Math.Exp(Normal(x, s));
		}


		/*
		 *   Return a random number from a geometric distribution
		 *   with mean x (x > 0).
		 */

		public long Geometric0(double x) 
		{
			double p = 1/x;
			long i = 0;
			while (FloatRandom() < p)
				++i;
			return i;
		}

		public long Geometric1(double x) 
		{
			return Geometric0(x) + 1;
		}

		/*
		 *   Return a random number from a hypergeometric distribution
		 *   with mean x and standard deviation s
		 */

		public double HyperGeometric(double x, double s) 
		{
			double sqrtz = s / x;
			double z = sqrtz * sqrtz;
			double p = 0.5 * (1 - Math.Sqrt((z - 1) / ( z + 1)));
			double d = (FloatRandom() > p) ? (1 - p) : p;
			return -x * Math.Log(FloatRandom()) / (2 * d);
		}

		/*
		 *   Return a random number from a binomial distribution
		 *   of n items where each item has a probability u of
		 *   being drawn.
		 */

		public long Binomial(long n, double u) 
		{
			long k = 0;
			for (long i = 0; i < n; i++)
				if (FloatRandom() < u)
					++k;
			return k;
		}

		/*
		 *   Return a random number from a Poisson distribution
		 *   with mean x.
		 */

		public long Poisson(double x) 
		{
			double b = Math.Exp(-x);
			long k = 0;
			double p = 1;
			while (p >= b) 
			{
				++k;
				p *= FloatRandom();
			}
			return k - 1;
		}

		/*
		 *   Return a random number from a Weibull distribution
		 *   with parameters alpha and beta.
		 */

		public double Weibull(double alpha, double beta) 
		{
			return Math.Pow(-beta * Math.Log(1 - FloatRandom()), 1/alpha);
		}

		/*
		 *   Return a random number from a Pareto distribution
		 *   with parameters alpha(shape) and beta(position).
		 */

		public double Pareto(double alpha, double beta) 
		{
			return beta * Math.Pow(FloatRandom(), -1.0/alpha);
		}

		/*
		 *   Return a random number from a 1/x * 1/ln(max/min) distribution
		 *   with min and max parameters.
		 * mean = (max-min)/(ln(max)-ln(min))
		 */

		public double Inverse(double min, double max) 
		{
			return min*Math.Exp(FloatRandom()*Math.Log(max/min));
		}

		/*
		 *   Return a random number from a triangular distribution
		 *   with min and max and middle parameters.
		 * 
		 */

		public double Triangular(double min, double middle, double max) 
		{
			double y = FloatRandom();
			if ( y <= (middle-min)/(max-min) )
			{
				return min + Math.Sqrt(y*max*middle - y*max*min - y*min*middle + y*min*min);
			}
			return max-
				Math.Sqrt(max*max+y*max*middle-y*max*max-y*min*middle+y*max*min-max*min-max*middle+min*middle);
		}
	}
}
