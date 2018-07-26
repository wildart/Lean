using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math;
using Accord.Math.Optimization;
using Accord.Statistics;

namespace QuantConnect.Algorithm.Framework.Portfolio
{
    public class MaximumSharpeRatioPortfolioOptimizerAlglib : IPortfolioOptimizer
    {
        private double _lower;
        private double _upper;
        private double _riskFreeRate;
        private double[] _returns;
        private double[,] _cov;

        public MaximumSharpeRatioPortfolioOptimizerAlglib(double lower = -1, double upper = 1, double riskFreeRate = 0.0)
        {
            _lower = lower;
            _upper = upper;
            _riskFreeRate = riskFreeRate;
        }

        /// <summary>
        /// Sum of all weight is one: 1^T w = 1 / Σw = 1
        /// </summary>
        /// <param name="size">number of variables</param>
        /// <returns>linear constaraint object</returns>
        protected LinearConstraint GetBudgetConstraint(int size)
        {
            return new LinearConstraint(size)
            {
                CombinedAs = Vector.Create(size, 1.0),
                ShouldBe = ConstraintType.EqualTo,
                Value = 1.0
            };
        }

        public static double[,] GetConstraintMatrix(IEnumerable<LinearConstraint> constraints)
        {
            return Matrix.Create(constraints.Select(c =>
            {                
                var cc = Vector.Create(c.CombinedAs.Length + 1, c.Value);
                c.CombinedAs.CopyTo(cc, 0);
                return cc;
            }).ToArray());
        }

        public static int[] GetConstraintTypes(IEnumerable<LinearConstraint> constraints)
        {
            return constraints.Select(c => (c.ShouldBe == ConstraintType.EqualTo ? 0 : (c.ShouldBe == ConstraintType.GreaterThanOrEqualTo ? 1 : -1))).ToArray();
        }

        public static void SharpeRatio(double[] x, ref double func, object obj)
        {
            var opt = (MaximumSharpeRatioPortfolioOptimizerAlglib)obj;
            var annual_return = opt._returns.Dot(x);
            var annual_volatility = Math.Sqrt(x.Dot(opt._cov).Dot(x));
            func = (annual_return - opt._riskFreeRate) / annual_volatility;
            func = Double.IsInfinity(func) || Double.IsNaN(func) ? 1.0E+300 : -func;
        }

        public double[] Optimize(double[,] historicalReturns, double[] expectedReturns = null)
        {
            var cov = historicalReturns.Covariance();
            _cov = cov;
            var size = cov.GetLength(0);
            var returns = (expectedReturns ?? historicalReturns.Mean(0)).Subtract(_riskFreeRate);
            _returns = returns;
            var constraints = new List<LinearConstraint>();

            // Setup parameters
            alglib.minbleicstate state;
            var x0 = Vector.Create(size, 1.0 / size);
            double diffstep = 1.0e-6; // This variable contains differentiation step            
            alglib.minbleiccreatef(x0, diffstep, out state);

            // Σw = 1
            constraints.Add(GetBudgetConstraint(size));

            // lw ≤ w ≤ up
            alglib.minbleicsetbc(state, Vector.Create(size, _lower), Vector.Create(size, _upper));

            // wire all constraints            
            alglib.minbleicsetlc(state, GetConstraintMatrix(constraints), GetConstraintTypes(constraints));

            // Stopping conditions for the optimizer. 
            alglib.minbleicsetcond(state, 0, 0, 0, 0);
            // Optimize
            alglib.minbleicoptimize(state, SharpeRatio, null, this);

            double[] x;
            alglib.minbleicreport rep;
            alglib.minbleicresults(state, out x, out rep);
            return Double.IsNaN(x.Sum()) ? x0 : x;
        }
    }
}
