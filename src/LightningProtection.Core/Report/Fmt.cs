using System.Globalization;

namespace LightningProtection.Core.Report
{
    /// <summary>Форматирование чисел по правилам оформления проектной документации.</summary>
    public static class Fmt
    {
        public static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        /// <summary>Длина или высота в метрах: 12,35.</summary>
        public static string M(double value) => value.ToString("0.00", Ru);

        /// <summary>Высотная отметка: +12,500.</summary>
        public static string Elev(double value) => (value > 0 ? "+" : value < 0 ? "" : "±") + value.ToString("0.000", Ru);

        public static string N(double value, string format) => value.ToString(format, Ru);
    }
}
