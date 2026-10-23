using System;
using LightningProtection.Core.Model;

namespace LightningProtection.Core.Norms
{
    /// <summary>Конусная зона одиночного стержневого молниеприёмника.</summary>
    public readonly struct ConeZone
    {
        public ConeZone(double h, double h0, double r0)
        {
            H = h;
            H0 = h0;
            R0 = r0;
        }

        /// <summary>Высота молниеприёмника h, м.</summary>
        public double H { get; }
        /// <summary>Высота вершины конуса h0, м.</summary>
        public double H0 { get; }
        /// <summary>Радиус конуса на уровне земли r0, м.</summary>
        public double R0 { get; }

        /// <summary>Радиус rx горизонтального сечения на высоте hx.</summary>
        public double Rx(double hx) => hx >= H0 ? 0 : R0 * (H0 - hx) / H0;
    }

    /// <summary>Общая зона двух молниеприёмников (двойной молниеприёмник).</summary>
    public readonly struct PairZone
    {
        public static readonly PairZone NotJoined = default;

        public PairZone(double hc, double rc)
        {
            Joined = true;
            Hc = hc;
            Rc = rc;
        }

        /// <summary>false — молниеприёмники стоят слишком далеко и работают как одиночные.</summary>
        public bool Joined { get; }
        /// <summary>Высота зоны в середине между молниеприёмниками hc, м.</summary>
        public double Hc { get; }
        /// <summary>Полуширина зоны в середине на уровне земли rc, м.</summary>
        public double Rc { get; }

        /// <summary>Полуширина rcx зоны в середине на высоте hx.</summary>
        public double Rcx(double hx) => !Joined || hx >= Hc ? 0 : Rc * (Hc - hx) / Hc;
    }

    public abstract class ZoneNorm
    {
        public abstract NormKind Kind { get; }

        /// <summary>Наибольшая высота молниеприёмника, для которой заданы формулы, м.</summary>
        public abstract double MaxRodHeight { get; }

        public string Title => Kind.Title();

        public abstract ConeZone Single(double h, ZoneLevel level);

        /// <summary>Зона двух молниеприёмников одинаковой высоты h на расстоянии L.</summary>
        protected abstract PairZone PairEqual(double h, double distance, ZoneLevel level);

        /// <summary>
        /// Зона двух молниеприёмников. Для разной высоты обе нормы берут среднее:
        /// hc = (hc1 + hc2) / 2, rc = (r01 + r02) / 2, где hc1 и hc2 — как для пар равной высоты.
        /// </summary>
        public PairZone Pair(double h1, double h2, double distance, ZoneLevel level)
        {
            if (Math.Abs(h1 - h2) < 1e-6)
                return PairEqual(h1, distance, level);

            var p1 = PairEqual(h1, distance, level);
            var p2 = PairEqual(h2, distance, level);
            if (!p1.Joined || !p2.Joined)
                return PairZone.NotJoined;
            return new PairZone((p1.Hc + p2.Hc) / 2, (Single(h1, level).R0 + Single(h2, level).R0) / 2);
        }

        /// <summary>
        /// Продолжается ли зона пары выше hc вдоль оси пары (выступ lx). Есть только в СО 153
        /// (формула 3.5); в РД-87 выше hc остаются зоны одиночных молниеприёмников.
        /// </summary>
        public virtual bool HasSpurAboveHc => false;

        /// <summary>Ссылка на пункт нормы для зоны одиночного молниеприёмника.</summary>
        public abstract string SingleSource { get; }

        /// <summary>Ссылка на пункт нормы для зоны двойного молниеприёмника.</summary>
        public abstract string PairSource { get; }

        /// <summary>
        /// Принятые коэффициенты h0 и r0 для высоты h, например «0,92h» и «(1,1 − 0,002h)h = 1,060h».
        /// </summary>
        public abstract (string H0, string R0) SingleCoefficients(double h, ZoneLevel level);

        /// <summary>Условие по расстоянию L и формула hc для пары равной высоты h.</summary>
        protected abstract (string Condition, string Hc) PairFormulaEqual(double h, double distance, ZoneLevel level);

        /// <summary>Условие и формула hc для пары; для разной высоты — среднее hc1 и hc2.</summary>
        public (string Condition, string Hc) PairFormula(double h1, double h2, double distance, ZoneLevel level)
        {
            if (Math.Abs(h1 - h2) < 1e-6)
                return PairFormulaEqual(h1, distance, level);

            var p1 = PairEqual(h1, distance, level);
            var p2 = PairEqual(h2, distance, level);
            return ("h1 ≠ h2",
                $"hc = (hc1 + hc2)/2, hc1 = {Report.Fmt.M(p1.Hc)}, hc2 = {Report.Fmt.M(p2.Hc)}; rc = (r01 + r02)/2");
        }

        /// <summary>Коэффициент при h с подстановкой: «[0,8 − 1,43·10⁻³(h − 30)]h = 0,771h».</summary>
        protected static string Coefficient(string expression, double value) => $"{expression} = {Report.Fmt.N(value, "0.000")}h";

        public static ZoneNorm For(NormKind kind) => kind == NormKind.Rd87 ? Rd87Norm.Instance : So153Norm.Instance;

        protected void CheckHeight(double h)
        {
            if (h <= 0 || h > MaxRodHeight)
                throw new ArgumentOutOfRangeException(nameof(h), h,
                    $"{Title}: формулы применимы для высоты молниеприёмника до {MaxRodHeight} м.");
        }
    }
}
