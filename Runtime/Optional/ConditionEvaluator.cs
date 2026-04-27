namespace ShadowLib.RNG.Modifiers
{
    using NCalc;
    using NCalc.Handlers;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Evaluates condition strings using NCalc with custom functions.
    /// </summary>
    public class ConditionEvaluator
    {
        private struct Vector3
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }

            public Vector3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        public bool Evaluate(string condition, Dictionary<string, object> context)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return true;

            try
            {
                var expression = new Expression(condition);

                // Add all context parameters
                foreach (var kvp in context)
                {
                    expression.Parameters[kvp.Key] = kvp.Value;
                }

                // Register custom functions
                expression.EvaluateFunction += HandleCustomFunction;

                var result = expression.Evaluate();
                return Convert.ToBoolean(result);
            }
            catch (Exception ex)
            {
                // Log error in production
                Console.WriteLine($"Failed to evaluate condition '{condition}': {ex.Message}");
                return false;
            }
        }

        private void HandleCustomFunction(string name, FunctionArgs args)
        {
            switch (name.ToLower())
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

                default:
                    throw new ArgumentException($"Unknown function: {name}");
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

        // Assumes vect3 similar to Unity's Vector3 or System.Numerics.Vector3
        private void EvaluateDistance(FunctionArgs args)
        {
            if (args.Parameters.Length < 2)
                throw new ArgumentException("distance() requires 2 arguments");

            var p1 = (Vector3)args.Parameters[0].Evaluate();
            var p2 = (Vector3)args.Parameters[1].Evaluate();

            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float dz = p2.Z - p1.Z;

            args.Result = Math.Sqrt(dx * dx + dy * dy + dz * dz);
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
