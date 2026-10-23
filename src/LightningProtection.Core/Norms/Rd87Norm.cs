using System;
using LightningProtection.Core.Model;

namespace LightningProtection.Core.Norms
{
    /// <summary>
    /// РД 34.21.122-87, приложение 3: зоны защиты стержневых молниеприёмников высотой до 150 м.
    /// </summary>
    public sealed class Rd87Norm : ZoneNorm
    {
        public static readonly Rd87Norm Instance = new Rd87Norm();

        public override NormKind Kind => NormKind.Rd87;
        public override double MaxRodHeight => 150;

        public override ConeZone Single(double h, ZoneLevel level)
        {
            CheckHeight(h);
            return level switch
            {
                // Зона А: h0 = 0,85h; r0 = (1,1 − 0,002h)h
                ZoneLevel.RdA => new ConeZone(h, 0.85 * h, (1.1 - 0.002 * h) * h),
                // Зона Б: h0 = 0,92h; r0 = 1,5h
                ZoneLevel.RdB => new ConeZone(h, 0.92 * h, 1.5 * h),
                _ => throw new ArgumentException($"Уровень {level} не относится к РД 34.21.122-87."),
            };
        }

        public override string SingleSource => "РД 34.21.122-87, прил. 3, п. 1.1";
        public override string PairSource => "РД 34.21.122-87, прил. 3, пп. 2.1, 2.2";

        public override (string H0, string R0) SingleCoefficients(double h, ZoneLevel level) => level switch
        {
            ZoneLevel.RdA => ("0,85h", Coefficient("(1,1 − 0,002h)h", 1.1 - 0.002 * h)),
            _ => ("0,92h", "1,5h"),
        };

        protected override (string Condition, string Hc) PairFormulaEqual(double h, double distance, ZoneLevel level)
        {
            if (distance <= h)
                return ("L ≤ h", "hc = h0");
            if (level == ZoneLevel.RdA)
            {
                if (distance <= 2 * h)
                    return ("h < L ≤ 2h", "hc = h0 − (0,17 + 3·10⁻⁴h)(L − h)");
                return distance <= 4 * h
                    ? ("2h < L ≤ 4h", "hc = h0 − (0,17 + 3·10⁻⁴h)(L − h); rc = r0[1 − 0,2(L − 2h)/h]")
                    : ("L > 4h", "общей зоны нет");
            }
            return distance <= 6 * h
                ? ("h < L ≤ 6h", "hc = h0 − 0,14(L − h)")
                : ("L > 6h", "общей зоны нет");
        }

        protected override PairZone PairEqual(double h, double distance, ZoneLevel level)
        {
            var single = Single(h, level);
            if (distance <= h)
                return new PairZone(single.H0, single.R0);

            if (level == ZoneLevel.RdA)
            {
                // h < L ≤ 4h: hc = h0 − (0,17 + 3·10⁻⁴h)(L − h);
                // при 2h < L ≤ 4h полуширина уменьшается: rc = r0[1 − 0,2(L − 2h)/h].
                if (distance > 4 * h)
                    return PairZone.NotJoined;
                var hc = single.H0 - (0.17 + 3e-4 * h) * (distance - h);
                var rc = distance <= 2 * h ? single.R0 : single.R0 * (1 - 0.2 * (distance - 2 * h) / h);
                return new PairZone(hc, rc);
            }

            // Зона Б, h < L ≤ 6h: hc = h0 − 0,14(L − h)
            if (distance > 6 * h)
                return PairZone.NotJoined;
            return new PairZone(single.H0 - 0.14 * (distance - h), single.R0);
        }
    }
}
