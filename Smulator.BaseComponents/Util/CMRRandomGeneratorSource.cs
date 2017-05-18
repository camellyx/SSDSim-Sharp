using System;
using System.Collections;

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
	/// <title>CMRRandomGeneratorSource</title>
	/// <description> 
	///  Combined Multiple Recursive random number generator.
	///
	/// This is an implementation of Pierre L'Ecuyer's
	/// MRG32k3a generator, described in:
	///
	///   Pierre L'Ecuyer, Good Parameters and Implementations for
	///   Combined Multiple Recursive Random Number Generators,
	///   Operations Research v47 no1 Jan-Feb 1999 
	///
	/// </description>
	/// <copyright>Copyright(c)2005
	/// Part of this code is converted from Akaroa 2.7.4
	/// </copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2005/08/26</date>
	/// </summary>
	public class CMRRandomGeneratorSource : IRandomGeneratorSource
	{

		double [][]s=
				{
					new double[] {0, 0, 0},
					new double[] {0, 0, 0}
				};


		static readonly double
			norm = 2.328306549295728e-10,
			m1   = 4294967087.0,
			m2   = 4294944443.0,
			a12  =    1403580.0,
			a13  =    -810728.0,
			a21  =     527612.0,
			a23  =   -1370589.0;
		
		static double [][][]a = 
		{
			new double[][] 
			{
				new double[] {0.0, 1.0, 0.0},
				new double[] {0.0, 0.0, 1.0},
				new double[] {a13, a12, 0.0}
			},
			new double[][] 
			{
				new double[] {0.0, 1.0, 0.0},
				new double[] {0.0, 0.0, 1.0},
				new double[] {a23, 0.0, a21}
			}
		};

		static double []m = 
		{
			m1,
			m2
		};

		static double [][]init_s = 
		{
			new double[] {1.0, 1.0, 1.0},
			new double[] {1.0, 1.0, 1.0}
		};
	
		public CMRRandomGeneratorSource(long n, int e)
		{ 
			for (int i = 0; i <= 1; i++)
				for (int j = 0; j <= 2; j++)
					s[i][j] = init_s[i][j];
			Advance(n, e);
		}

		public CMRRandomGeneratorSource():this(0,0)
		{ 
		}

		static double mod(double x, double m) 
		{
			long k = (long)(x / m);
			x -= k * m;
			if (x < 0.0)
				x += m;
			return x;
		}

		/*
		 *   Advance CMRG one step and return next number
		 */

		public double NextDouble() 
								   
		{
			double p1 = mod(a12 * s[0][1] + a13 * s[0][0], m1);
			s[0][0] = s[0][1];
			s[0][1] = s[0][2];
			s[0][2] = p1;
			double p2 = mod(a21 * s[1][2] + a23 * s[1][0], m2);
			s[1][0] = s[1][1];
			s[1][1] = s[1][2];
			s[1][2] = p2;
			double p = p1 - p2;
			if (p < 0.0)
				p += m1;
			return (p + 1) * norm;
		}

		static Int64 ftoi(double x, double m) 
		{
			if (x >= 0.0)
				return (Int64)(x);
			else
				return (Int64)((double)x + (double)m);
		}

		static double itof(Int64 i, Int64 m) 
		{
			return i;
		}

		static void v_ftoi(double []u, Int64 [] v, double m) 
		{
			for (int i = 0; i <= 2; i++)
				v[i] = ftoi(u[i], m);
		}

		static void v_itof(Int64 [] u, double []v, Int64 m) 
		{
			for (int i = 0; i <= 2; i++)
				v[i] = itof((Int64)u[i], m);
		}

		static void v_copy(Int64 [] u, Int64 [] v) 
		{
			for (int i = 0; i <= 2; i++)
				v[i] = u[i];
		}

		static void m_ftoi(double [][]a, Int64 [][] b, double m) 
		{
			for (int i = 0; i <= 2; i++)
				for (int j = 0; j <= 2; j++)
					b[i][j] = ftoi(a[i][j], m);
		}

		static void m_copy(Int64 [][] a, Int64 [][] b) 
		{
			for (int i = 0; i <= 2; i++)
				for (int j = 0; j <= 2; j++)
					b[i][j] = a[i][j];
		}

		static void mv_mul(Int64 [][] a, Int64 [] u, Int64 [] v, Int64 m) 
		{
			Int64 [] w = new Int64[3];
			int i, j;
			for (i = 0; i <= 2; i++) 
			{
				w[i] = 0;
				for (j = 0; j <= 2; j++)
					w[i] = (a[i][j] * u[j] + w[i]) % m;
			}
			v_copy(w, v);
		}

		static void mm_mul(Int64 [][] a, Int64 [][] b, Int64 [][] c, Int64 m) 
		{
			Int64 [][] d = new Int64[][] {new Int64[3], new Int64[3], new Int64[3]};
			int i, j, k;
			for (i = 0; i <= 2; i++) 
			{
				for (j = 0; j <= 2; j++) 
				{
					d[i][j] = 0;
					for (k = 0; k <= 2; k++)
						d[i][j] = (a[i][k] * b[k][j] + d[i][j]) % m;
				}
			}
			m_copy(d, c);
		}

		/*
		 *   Advance the CMRG by n*2^e steps
		 */

		void Advance(long n, int e) 
		{
			Int64 [][][] B = new Int64[2][][] 
				{
					new Int64[3][]
					{ new Int64[3], new Int64[3], new Int64[3]},
					new Int64[3][]
					{ new Int64[3], new Int64[3], new Int64[3]}

				};
			Int64 [][] S = new Int64[2][] {new Int64[3], new Int64[3]};
			Int64 []M = new Int64[2];
			int i;
			for (i = 0; i <= 1; i++) 
			{
				m_ftoi(a[i], B[i], m[i]);
				v_ftoi(s[i], S[i], m[i]);
				M[i] = (Int64)(m[i]);
			}
			while (e-- != 0) 
			{
				for (i = 0; i <= 1; i++)
					mm_mul(B[i], B[i], B[i], M[i]);
			}
			while (n != 0) 
			{
				if ((n & 1) != 0)
					for (i = 0; i <= 1; i++)
						mv_mul(B[i], S[i], S[i], M[i]);
				n >>= 1;
				if (n != 0)
					for (i = 0; i <= 1; i++)
						mm_mul(B[i], B[i], B[i], M[i]);
			}
			for (i = 0; i <= 1; i++)
				v_itof(S[i], s[i], M[i]);
		}
		}
}
