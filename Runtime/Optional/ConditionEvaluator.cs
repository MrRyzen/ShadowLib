namespace ShadowLib.RNG.Utilities
{
    using NCalc;
    using NCalc.Handlers;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// Evaluates condition strings using NCalc with custom functions.
    /// </summary>
    public class ConditionEvaluator
    {
        /// <summary>
        /// Evaluates an NCalc condition string against named values. An empty condition is true.
        /// Besides NCalc's built-ins, <c>contains(collection, item)</c>, <c>count(collection)</c> and
        /// <c>distance(a, b)</c> (two <see cref="Vector3"/> values) are available.
        /// </summary>
        /// <param name="condition">The expression, for example <c>PlayerLevel &gt; 10</c>.</param>
        /// <param name="context">The values the expression can refer to by name.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
        /// <exception cref="Exception">
        /// The condition does not parse, names a value missing from <paramref name="context"/>,
        /// misuses a function, or does not produce a boolean. The exception is NCalc's own or an <see cref="ArgumentException"/>.
        /// </exception>
        public bool Evaluate(string condition, Dictionary<string, object> context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(condition))
                return true;

            var expression = new Expression(condition);
            foreach (var kvp in context)
                expression.Parameters[kvp.Key] = kvp.Value;

            expression.EvaluateFunction += HandleCustomFunction;
            return Convert.ToBoolean(expression.Evaluate());
        }

        private void HandleCustomFunction(string name, FunctionArgs args)
        {
            switch (name.ToLowerInvariant())
            {
                case "contains":
                    EvaluateContains(args);
                    break;

                case "distance":
                    EvaluateDistance(args);
                    break;

                case "count":
                    EvaluateCount(args);
                    break;

                // Anything else is left without a result so NCalc falls back to its built-ins.
            }
        }

        private void EvaluateContains(FunctionArgs args)
        {
            if (args.Parameters.Length < 2)
                throw new ArgumentException("contains() requires 2 arguments");

            var collection = args.Parameters[0].Evaluate();
            var target = args.Parameters[1].Evaluate();

            if (collection is Array array)
            {
                args.Result = array.Cast<object>().Contains(target);
            }
            else if (collection is string str && target is string targetStr)
            {
                args.Result = str.Contains(targetStr);
            }
            else
            {
                args.Result = false;
            }
        }

        private void EvaluateDistance(FunctionArgs args)
        {
            if (args.Parameters.Length < 2)
                throw new ArgumentException("distance() requires 2 arguments");
            if (!(args.Parameters[0].Evaluate() is Vector3 from) || !(args.Parameters[1].Evaluate() is Vector3 to))
                throw new ArgumentException("distance() takes two System.Numerics.Vector3 values");

            args.Result = (double)Vector3.Distance(from, to);
        }

        private void EvaluateCount(FunctionArgs args)
        {
            if (args.Parameters.Length < 1)
                throw new ArgumentException("count() requires 1 argument");

            var collection = args.Parameters[0].Evaluate();

            if (collection is Array array)
                args.Result = array.Length;
            else if (collection is string str)
                args.Result = str.Length;
            else
                args.Result = 0;
        }
    }
}
