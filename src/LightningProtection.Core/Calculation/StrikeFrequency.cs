using System;
using LightningProtection.Core.Model;

namespace LightningProtection.Core.Calculation
{
    /// <summary>Плотность ударов молнии в землю и ожидаемое число поражений объекта.</summary>
    public static class StrikeFrequency
    {
        /// <summary>
        /// Среднегодовое число ударов молнии в 1 км² земли, 1/(км²·год).
        /// РД-87 — по таблице 2 от продолжительности гроз; СО 153 — формула (2.1): 6,7·Td/100.
        /// </summary>
        public static double GroundDensity(NormKind norm, double thunderstormHours)
        {
            if (norm == NormKind.So153)
                return 6.7 * thunderstormHours / 100;

            if (thunderstormHours < 10) return 0;
            if (thunderstormHours < 20) return 1;
            if (thunderstormHours < 40) return 2;
            if (thunderstormHours < 60) return 4;
            if (thunderstormHours < 80) return 5.5;
            if (thunderstormHours < 100) return 7;
            return 8.5;
        }

        /// <summary>
        /// Ожидаемое число поражений в год объекта прямоугольной формы:
        /// N = [(S + 6h)(L + 6h) − 7,7h²]·n·10⁻⁶.
        /// </summary>
        public static double ExpectedStrikes(double width, double length, double height, double density)
            => Math.Max(0, ((width + 6 * height) * (length + 6 * height) - 7.7 * height * height) * density * 1e-6);

        /// <summary>Ожидаемое число поражений сосредоточенного объекта (труба, башня): N = 9πh²·n·10⁻⁶.</summary>
        public static double ExpectedStrikesConcentrated(double height, double density)
            => 9 * Math.PI * height * height * density * 1e-6;
    }
}
