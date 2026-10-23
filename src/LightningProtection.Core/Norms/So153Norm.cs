using System;
using LightningProtection.Core.Model;

namespace LightningProtection.Core.Norms
{
    /// <summary>
    /// СО 153-34.21.122-2003, п. 3.3.2: таблица 3.4 (одиночный стержневой молниеприёмник)
    /// и таблица 3.6 (двойной стержневой молниеприёмник), высота до 150 м.
    /// </summary>
    public sealed class So153Norm : ZoneNorm
    {
        public static readonly So153Norm Instance = new So153Norm();

        public override NormKind Kind => NormKind.So153;
        public override double MaxRodHeight => 150;

        public override ConeZone Single(double h, ZoneLevel level)
        {
            CheckHeight(h);
            double h0, r0;
            switch (level)
            {
                case ZoneLevel.So090:
                    h0 = 0.85 * h;
                    r0 = h <= 100 ? 1.2 * h : (1.2 - 1e-3 * (h - 100)) * h;
                    break;
                case ZoneLevel.So099:
                    if (h <= 30) { h0 = 0.8 * h; r0 = 0.8 * h; }
                    else if (h <= 100) { h0 = 0.8 * h; r0 = (0.8 - 1.43e-3 * (h - 30)) * h; }
                    else { h0 = (0.8 - 1e-3 * (h - 100)) * h; r0 = 0.7 * h; }
                    break;
                case ZoneLevel.So0999:
                    if (h <= 30) { h0 = 0.7 * h; r0 = 0.6 * h; }
                    else if (h <= 100) { h0 = (0.7 - 7.14e-4 * (h - 30)) * h; r0 = (0.6 - 1.43e-3 * (h - 30)) * h; }
                    else { h0 = (0.65 - 1e-3 * (h - 100)) * h; r0 = (0.5 - 2e-3 * (h - 100)) * h; }
                    break;
                default:
                    throw new ArgumentException($"Уровень {level} не относится к СО 153-34.21.122-2003.");
            }
            return new ConeZone(h, h0, r0);
        }

        public override string SingleSource => "СО 153-34.21.122-2003, табл. 3.4, ф. (3.1)";
        public override string PairSource => "СО 153-34.21.122-2003, табл. 3.6, ф. (3.3), (3.6)";

        public override (string H0, string R0) SingleCoefficients(double h, ZoneLevel level)
        {
            switch (level)
            {
                case ZoneLevel.So090:
                    return h <= 100
                        ? ("0,85h", "1,2h")
                        : ("0,85h", Coefficient("[1,2 − 10⁻³(h − 100)]h", 1.2 - 1e-3 * (h - 100)));
                case ZoneLevel.So099:
                    if (h <= 30)
                        return ("0,8h", "0,8h");
                    if (h <= 100)
                        return ("0,8h", Coefficient("[0,8 − 1,43·10⁻³(h − 30)]h", 0.8 - 1.43e-3 * (h - 30)));
                    return (Coefficient("[0,8 − 10⁻³(h − 100)]h", 0.8 - 1e-3 * (h - 100)), "0,7h");
                default:
                    if (h <= 30)
                        return ("0,7h", "0,6h");
                    if (h <= 100)
                        return (Coefficient("[0,7 − 7,14·10⁻⁴(h − 30)]h", 0.7 - 7.14e-4 * (h - 30)),
                            Coefficient("[0,6 − 1,43·10⁻³(h − 30)]h", 0.6 - 1.43e-3 * (h - 30)));
                    return (Coefficient("[0,65 − 10⁻³(h − 100)]h", 0.65 - 1e-3 * (h - 100)),
                        Coefficient("[0,5 − 2·10⁻³(h − 100)]h", 0.5 - 2e-3 * (h - 100)));
            }
        }

        protected override (string Condition, string Hc) PairFormulaEqual(double h, double distance, ZoneLevel level)
        {
            GetLimits(h, level, out var lMax, out var lc);
            var (lMaxText, lcText) = LimitCoefficients(h, level);
            var limits = $"Lmax = {lMaxText} = {Report.Fmt.M(lMax)} м, Lc = {lcText} = {Report.Fmt.M(lc)} м";
            if (distance <= lc)
                return ($"L ≤ Lc ({limits})", "hc = h0");
            if (distance < lMax)
                return ($"Lc < L < Lmax ({limits})", "hc = h0(Lmax − L)/(Lmax − Lc)");
            return ($"L ≥ Lmax ({limits})", "общей зоны нет");
        }

        protected override PairZone PairEqual(double h, double distance, ZoneLevel level)
        {
            var single = Single(h, level);
            GetLimits(h, level, out var lMax, out var lc);

            if (distance <= lc)
                return new PairZone(single.H0, single.R0);
            if (distance >= lMax)
                return PairZone.NotJoined;
            // Формула (3.3): hc = h0 (Lmax − L) / (Lmax − Lc)
            return new PairZone(single.H0 * (lMax - distance) / (lMax - lc), single.R0);
        }

        /// <summary>Коэффициенты Lmax и Lc по таблице 3.6 в виде текста.</summary>
        private static (string LMax, string Lc) LimitCoefficients(double h, ZoneLevel level)
        {
            const string lcOver30 = "[2,25 − 0,01007(h − 30)]h";
            switch (level)
            {
                case ZoneLevel.So090:
                    if (h <= 30) return ("5,75h", "2,5h");
                    if (h <= 100) return (Coefficient("[5,75 − 3,57·10⁻³(h − 30)]h", 5.75 - 3.57e-3 * (h - 30)), "2,5h");
                    return ("5,5h", "2,5h");
                case ZoneLevel.So099:
                    if (h <= 30) return ("4,75h", "2,25h");
                    if (h <= 100)
                        return (Coefficient("[4,75 − 3,57·10⁻³(h − 30)]h", 4.75 - 3.57e-3 * (h - 30)),
                            Coefficient(lcOver30, 2.25 - 0.01007 * (h - 30)));
                    return ("4,5h", "1,5h");
                default:
                    if (h <= 30) return ("4,25h", "2,25h");
                    if (h <= 100)
                        return (Coefficient("[4,25 − 3,57·10⁻³(h − 30)]h", 4.25 - 3.57e-3 * (h - 30)),
                            Coefficient(lcOver30, 2.25 - 0.01007 * (h - 30)));
                    return ("4,0h", "1,5h");
            }
        }

        /// <summary>Расстояния Lmax и Lc по таблице 3.6.</summary>
        public static void GetLimits(double h, ZoneLevel level, out double lMax, out double lc)
        {
            switch (level)
            {
                case ZoneLevel.So090:
                    if (h <= 30) { lMax = 5.75 * h; lc = 2.5 * h; }
                    else if (h <= 100) { lMax = (5.75 - 3.57e-3 * (h - 30)) * h; lc = 2.5 * h; }
                    else { lMax = 5.5 * h; lc = 2.5 * h; }
                    break;
                case ZoneLevel.So099:
                    if (h <= 30) { lMax = 4.75 * h; lc = 2.25 * h; }
                    else if (h <= 100) { lMax = (4.75 - 3.57e-3 * (h - 30)) * h; lc = (2.25 - 0.01007 * (h - 30)) * h; }
                    else { lMax = 4.5 * h; lc = 1.5 * h; }
                    break;
                case ZoneLevel.So0999:
                    if (h <= 30) { lMax = 4.25 * h; lc = 2.25 * h; }
                    else if (h <= 100) { lMax = (4.25 - 3.57e-3 * (h - 30)) * h; lc = (2.25 - 0.01007 * (h - 30)) * h; }
                    else { lMax = 4.0 * h; lc = 1.5 * h; }
                    break;
                default:
                    throw new ArgumentException($"Уровень {level} не относится к СО 153-34.21.122-2003.");
            }
        }
    }
}
