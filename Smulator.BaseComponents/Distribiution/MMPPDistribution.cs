using System;
using Smulator.Util;

namespace Smulator.BaseComponents.Distributions
{
	#region Change info
	/// <change>
	/// <author></author>
	/// <description> </description>
	/// <date></date>
	/// </change>
	#endregion 
	/// <summary>
	/// <title>MMPPDistribution</title>
	/// <description> 
	/// </description>
	/// <copyright>Copyright(c)2005</copyright>
	/// <company></company>
	/// <author>Abbas Nayebi ( www.nayebi.com )</author>
	/// <version>Version 1.0</version>
	/// <date>2006/06/05</date>
	/// </summary>			
	public class  MMPPDistribution : RealDistribution
	{
//		static int lastSeed = 12;
		double []forwardTransitionRates;
		double []backwardTransitionRates;
		double []generationRates;
		int curState = 0;
		int nextState = 0;
		double now = 0.0;
		double nextTransitionTime;
		RandomGenerator transitionDistribution;
		RandomGenerator generationDistribution;

		public MMPPDistribution():this(null, new double []{0.5}, new double []{0.5}, new double []{1,0}, 0, 0)
		{
		}

		public MMPPDistribution(string id, 
			double []forwardTransitionRates, 
			double []backwardTransitionRates,
			double []generationRates,
			int initialState,
			int seed):base(id)
		{
			this.curState = initialState;
			this.forwardTransitionRates = forwardTransitionRates;
			this.backwardTransitionRates = backwardTransitionRates;
			this.generationRates = generationRates;
			this.Seed = seed;
			setNextTransition();
		}


		private void setNextTransition()
		{
			double backTime, foreTime;
			if (curState > 0 && backwardTransitionRates[curState - 1] != 0)
			{
				backTime = transitionDistribution.Exponential(1/backwardTransitionRates[curState - 1]);
			} 
			else
				backTime = double.PositiveInfinity;

			if (curState < NStates - 1 && forwardTransitionRates[curState] != 0)
			{
				foreTime = transitionDistribution.Exponential(1/forwardTransitionRates[curState]);
			} 
			else
				foreTime = double.PositiveInfinity;

			if ( backTime < foreTime )
			{
				nextState = curState - 1;
				nextTransitionTime = now + backTime;
			} 
			else
			{
				nextState = curState + 1;
				nextTransitionTime = now + foreTime;
			}
		}

        protected override double GetValueInternal()
        {
            double prevTime = now;
            while (true)
            {
                double ng = generationDistribution.Exponential(1 / generationRates[curState]);
                if (ng + now < nextTransitionTime)
                {
                    now += ng;
                    double res = now - prevTime;
                    timeShift();
                    return (res);
                }
                stateTransition();
            }
        }

        private void timeShift()
        {
            nextTransitionTime -= now;
            now = 0;
        }

		private void stateTransition()
		{
			now = nextTransitionTime;
			curState = nextState;
			setNextTransition();

		}

		public int NStates
		{
			get { return this.generationRates.Length; }
		}

		protected override void init()
		{
			int s1 = seed;
			int s2 = seed * 2 + 4843;
			if ( seed == 0 )
			{
				s1 = SeedGeneratorFactory.GetSeed();
				s2 = SeedGeneratorFactory.GetSeed();
			}
			transitionDistribution = new RandomGenerator(s1);
			generationDistribution = new RandomGenerator(s2);
		}

		[System.Xml.Serialization.XmlIgnore]
		public override double Mean
		{
			get
			{
				throw new NotImplementedException();				
			}
			set
			{
				throw new NotImplementedException();
			}
		}	
	}
}
