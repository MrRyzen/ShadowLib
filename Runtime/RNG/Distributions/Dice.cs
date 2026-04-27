namespace ShadowLib.RNG.Distributions
{
    using System;
    using ShadowLib.RNG.Sources;

    /// <summary>
    /// Represents a dice with a specified number of sides and associated values.
    /// Can be used to simulate rolling a dice of any type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Dice<T>
    {
        private readonly IRandom _rng;
        private readonly int _sides;
        private readonly T[] _values;

        /// <summary>
        ///  Initializes a new instance of the <see cref="Dice{T}"/> class.
        /// </summary>
        /// <param name="rng"></param>
        /// <param name="values"></param>
        /// <exception cref="ArgumentException"></exception>
        public Dice(IRandom rng, T[] values)
        {
            if (values.Length <= 0)
                throw new ArgumentException("Number of sides must be positive.", nameof(values));
            if (values.Length != values.Length)
                throw new ArgumentException("Values array length must match the number of sides.", nameof(values));
            _rng = rng;
            _sides = values.Length;
            _values = values;
        }

        /// <summary>
        /// Rolls the dice and returns the result.
        /// </summary>
        /// <returns></returns>
        public T Sample()
        {
            int roll = _rng.Range(0, _sides);
            return _values[roll];
        }

        /// <summary>
        /// Rolls the dice multiple times and returns the results.
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public T[] SampleMultiple(int count)
        {
            if (count <= 0)
                throw new ArgumentException("Count must be positive.", nameof(count));

            T[] results = new T[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = Sample();
            }
            return results;
        }

        /// <summary>
        /// Updates the values associated with each side of the dice.
        /// </summary>
        /// <param name="newValues"></param>
        /// <exception cref="ArgumentException"></exception>
        public void UpdateDice(T[] newValues)
        {
            if (newValues.Length != _sides)
                throw new ArgumentException("New values array length must match the number of sides.", nameof(newValues));
            Array.Copy(newValues, _values, _sides);
        }
    }
}
