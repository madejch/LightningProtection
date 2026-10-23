using System.Collections.Generic;
using System.Linq;
using System.Text;
using LightningProtection.Core.Calculation;
using LightningProtection.Core.Model;
using LightningProtection.Core.Norms;

namespace LightningProtection.Core.Report
{
    public sealed class ReportTable
    {
        public string Title { get; set; } = "";
        public List<string> Headers { get; set; } = new List<string>();
        public List<string[]> Rows { get; set; } = new List<string[]>();
    }

    /// <summary>Формирует таблицы расчёта для листа и текстовый протокол для окна и командной строки.</summary>
    public static class ReportBuilder
    {
        public static List<ReportTable> BuildTables(ProtectionProject project, CalculationResult result)
        {
            var tables = new List<ReportTable>();
            var norm = ZoneNorm.For(project.Norm);

            var objects = new ReportTable
            {
                Title = "Защищаемые объекты",
                Headers = { "№", "Наименование", "Защищаемое пространство", "Отметка проверки, м", "Требуемая защита", "Результат" },
            };
            var index = 1;
            foreach (var check in result.Objects)
            {
                objects.Rows.Add(new[]
                {
                    (index++).ToString(),
                    check.Object.Name,
                    DescribeSpace(check.Object),
                    $"{check.Section.Name} = {Fmt.Elev(check.Hx)}",
                    check.Level.Title(),
                    check.IsProtected ? "защищён" : $"НЕ ЗАЩИЩЁН ({Fmt.N(check.UncoveredArea, "0.0")} м²)",
                });
            }
            tables.Add(objects);

            var rods = new ReportTable
            {
                Title = $"Расчёт зон защиты одиночных стержневых молниеприёмников ({norm.SingleSource})",
                Headers =
                {
                    "Молниеприёмник", "Отм. установки", "Мачта, м", "h, м", "Защита",
                    "Коэф. h0", "h0, м", "Коэф. r0", "r0, м", "hx, м", "rx = r0(h0 − hx)/h0, м",
                },
            };
            foreach (var group in result.Rods.GroupBy(r => r.Rod))
            {
                foreach (var row in group)
                {
                    var (h0Coefficient, r0Coefficient) = norm.SingleCoefficients(row.Cone.H, row.Level);
                    rods.Rows.Add(new[]
                    {
                        row.Rod.Name,
                        Fmt.Elev(row.Rod.BaseElevation),
                        Fmt.M(row.Rod.MastHeight),
                        Fmt.M(row.Cone.H),
                        row.Level.Title(),
                        "h0 = " + h0Coefficient,
                        Fmt.M(row.Cone.H0),
                        "r0 = " + r0Coefficient,
                        Fmt.M(row.Cone.R0),
                        $"{row.Section.Name} = {Fmt.M(row.Hx)}",
                        row.Rx > 0 ? Fmt.M(row.Rx) : "—",
                    });
                }
            }
            tables.Add(rods);

            if (result.Pairs.Count > 0)
            {
                var pairs = new ReportTable
                {
                    Title = $"Расчёт зон защиты двойных стержневых молниеприёмников ({norm.PairSource})",
                    Headers = { "Пара", "L, м", "Защита", "Условие", "Формула", "hc, м", "rc, м", "hx, м", "rcx = rc(hc − hx)/hc, м" },
                };
                foreach (var row in result.Pairs.OrderBy(p => project.Rods.IndexOf(p.First)).ThenBy(p => project.Rods.IndexOf(p.Second)))
                {
                    var (condition, formula) = norm.PairFormula(row.First.TotalHeight, row.Second.TotalHeight, row.Distance, row.Level);
                    if (!formula.Contains("rc"))
                        formula += "; rc = r0";
                    pairs.Rows.Add(new[]
                    {
                        $"{row.First.Name}–{row.Second.Name}",
                        Fmt.M(row.Distance),
                        row.Level.Title(),
                        condition,
                        formula,
                        Fmt.M(row.Zone.Hc),
                        Fmt.M(row.Zone.Rc),
                        $"{row.Section.Name} = {Fmt.M(row.Hx)}",
                        row.Rcx > 0 ? Fmt.M(row.Rcx) : "—",
                    });
                }
                tables.Add(pairs);
            }

            return tables;
        }

        public static string DescribeSpace(ProtectedObject obj)
        {
            if (!obj.IsCircular)
                return $"кровля, отм. {Fmt.Elev(obj.SourceElevation)}";
            var source = obj.Kind == ObjectKind.BreathingValve ? "клапаном" : "обрезом";
            if (obj.IsHemisphere)
                return $"полусфера R = {Fmt.M(obj.Radius)} м над {source} {Fmt.Elev(obj.SourceElevation)}";
            return $"цилиндр R = {Fmt.M(obj.Radius)} м, H = {Fmt.M(obj.SpaceHeight)} м над {source} {Fmt.Elev(obj.SourceElevation)}";
        }

        /// <summary>Текстовый протокол расчёта.</summary>
        public static string BuildText(ProtectionProject project, CalculationResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Норма: {project.Norm.Title()}");
            sb.AppendLine($"Продолжительность гроз Td = {Fmt.N(project.ThunderstormHours, "0.#")} ч/год, " +
                          $"плотность ударов n = {Fmt.N(result.GroundDensity, "0.0##")} 1/(км²·год)");
            foreach (var s in result.Strikes)
                sb.AppendLine($"{s.Object.Name}: {Fmt.M(s.Width)} × {Fmt.M(s.Length)} м, h = {Fmt.M(s.Height)} м, " +
                              $"ожидаемое число поражений N = {Fmt.N(s.ExpectedStrikes, "0.0000")} 1/год");
            foreach (var e in result.Errors)
                sb.AppendLine("ОШИБКА: " + e);
            sb.AppendLine();

            foreach (var table in BuildTables(project, result))
            {
                sb.AppendLine(table.Title);
                sb.AppendLine("  " + string.Join(" | ", table.Headers));
                foreach (var row in table.Rows)
                    sb.AppendLine("  " + string.Join(" | ", row));
                sb.AppendLine();
            }

            sb.AppendLine(result.AllProtected
                ? "Все объекты находятся в зоне защиты."
                : "Есть незащищённые объекты.");
            return sb.ToString();
        }
    }
}
