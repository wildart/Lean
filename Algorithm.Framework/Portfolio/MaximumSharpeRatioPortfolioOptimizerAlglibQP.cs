using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math;
using Accord.Math.Optimization;
using Accord.Statistics;

namespace QuantConnect.Algorithm.Framework.Portfolio
{
    public class MaximumSharpeRatioPortfolioOptimizerAlglibQP : IPortfolioOptimizer
    {
        private double _lower;
        private double _upper;
        private double _riskFreeRate;

        public MaximumSharpeRatioPortfolioOptimizerAlglibQP(double lower = -1, double upper = 1, double riskFreeRate = 0.0)
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

        public double[] Optimize(double[,] historicalReturns, double[] expectedReturns = null)
        {
            var cov = historicalReturns.Covariance();
            var size = cov.GetLength(0);
            var returns = (expectedReturns ?? historicalReturns.Mean(0)).Subtract(_riskFreeRate);
            var constraints = new List<LinearConstraint>();

            // Setup parameters
            alglib.minqpstate state;
            var x0 = Vector.Create(size, 1.0 / size);
            alglib.minqpcreate(size, out state);
            alglib.minqpsetstartingpoint(state, x0);
            alglib.minqpsetquadraticterm(state, cov);

            // (µ − r_f)^T w = 1
            constraints.Add(new LinearConstraint(size)
            {
                CombinedAs = returns,
                ShouldBe = ConstraintType.EqualTo,
                Value = 1.0
            });

            // Σw = 1
            constraints.Add(GetBudgetConstraint(size));

            // lw ≤ w ≤ up
            alglib.minqpsetbc(state, Vector.Create(size, _lower), Vector.Create(size, _upper));

            // wire all constraints            
            alglib.minqpsetlc(state, GetConstraintMatrix(constraints), GetConstraintTypes(constraints));

            // Stopping conditions for the optimizer. 
            alglib.minqpsetscaleautodiag(state);
            alglib.minqpsetalgobleic(state, 0, 0, 0, 0);
            //alglib.minqpsetalgodenseaul(state, 1.0e-9, 1.0e+4, 5);
            //alglib.minqpsetalgodenseaul(state, 0, 1.0e+4, 0);
            // Optimize
            alglib.minqpoptimize(state);

            double[] x;
            alglib.minqpreport rep;
            alglib.minqpresults(state, out x, out rep);
            var wsum = x.Sum();
            return !Double.IsNaN(wsum) || Math.Abs(wsum - 1.0) < 1e-12 ? x : x0;
        }
    }
}
