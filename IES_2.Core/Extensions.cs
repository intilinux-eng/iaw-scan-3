using System;

namespace IES_2
{
    public static class Extensions
    {
        /// <summary>
        /// Limit value to a certain min/max interval.
        /// </summary>
        /// <param name="value">The original value.</param>
        /// <param name="minValue">The minimum allowed value.</param>
        /// <param name="maxValue">The maximum allowed value.</param>
        /// <returns>The resulting value.</returns>
        public static decimal Limit(this decimal value, decimal minValue, decimal maxValue)
        {
            return Math.Max(minValue, Math.Min(maxValue, value));
        }

        /// <summary>
        /// Get bit flag from a byte value.
        /// </summary>
        /// <param name="value">The original value.</param>
        /// <param name="index">Bit index.</param>
        /// <returns>The resulting value.</returns>
        public static bool GetBit(this byte value, byte index)
        {
            return ((value & (byte)(1 << index)) != 0);
        }
    }
}
