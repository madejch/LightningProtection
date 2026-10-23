using System.Collections.Generic;
using System.Linq;
using LightningProtection.Core.Calculation;
using LightningProtection.Core.Model;
using LightningProtection.Core.Norms;

namespace LightningProtection.Core.Report
{
    /// <summary>
    /// Описательная часть по шаблону. Метки в фигурных скобках заменяются значениями расчёта;
    /// шаблон можно переопределить файлом description.txt рядом с плагином.
    /// </summary>
    public static class DescriptionBuilder
    {
        public const string DefaultTemplate =
@"1. Молниезащита запроектирована в соответствии с {NORM}.
2. Среднегодовая продолжительность гроз в районе строительства {TD} ч/год, плотность ударов молнии в землю {DENSITY} 1/(км²·год).{STRIKES}
3. Защите от прямых ударов молнии подлежат:
{OBJECTS}
4. Защита выполняется стержневыми молниеприёмниками, {RODS_COUNT} шт.:
{RODS}
5. {FORMULAS}
6. {CONCLUSION}
7. Зоны защиты показаны на плане на отметках защищаемых объектов и на виде сбоку. Расчёт зон защиты приведён в таблицах.
8. Токоотводы и заземлители выполнить в соответствии с {NORM}. Конструкцию и сопротивление заземлителя уточнить при проектировании.";

        public static string Build(ProtectionProject project, CalculationResult result, string? template = null)
        {
            var values = new Dictionary<string, string>
            {
                ["NORM"] = project.Norm.Title(),
                ["TD"] = Fmt.N(project.ThunderstormHours, "0.#"),
                ["DENSITY"] = Fmt.N(result.GroundDensity, "0.0##"),
                ["STRIKES"] = string.Concat(result.Strikes.Select(s =>
                    $" Ожидаемое число поражений молнией в год для объекта «{s.Object.Name}» (габариты {Fmt.M(s.Width)} × {Fmt.M(s.Length)} м, " +
                    $"высота {Fmt.M(s.Height)} м) N = {Fmt.N(s.ExpectedStrikes, "0.0000")}.")),
                ["OBJECTS"] = string.Join("\n", result.Objects.Select(o =>
                    $"   — {o.Object.Name}: {ReportBuilder.DescribeSpace(o.Object)}, высота проверки {o.Section.Name} = {Fmt.M(o.Hx)} м, требуемая защита — {o.Level.Title()};")),
                ["RODS_COUNT"] = project.Rods.Count.ToString(),
                ["RODS"] = string.Join("\n", project.Rods.Select(r =>
                    $"   — {r.Name}: мачта {Fmt.M(r.MastHeight)} м на отм. {Fmt.Elev(r.BaseElevation)}, полная высота h = {Fmt.M(r.TotalHeight)} м;")),
                ["FORMULAS"] = Formulas(project, result),
                ["CONCLUSION"] = Conclusion(result),
            };

            var text = template ?? DefaultTemplate;
            foreach (var pair in values)
                text = text.Replace("{" + pair.Key + "}", pair.Value);
            return text.Replace("\r\n", "\n");
        }

        /// <summary>Принятые формулы и коэффициенты h0, r0, hc с подстановкой значений — для экспертизы.</summary>
        private static string Formulas(ProtectionProject project, CalculationResult result)
        {
            var norm = ZoneNorm.For(project.Norm);
            var lines = new List<string>
            {
                $"Зоны защиты одиночных стержневых молниеприёмников определены по {norm.SingleSource}; " +
                "радиус зоны на высоте hx: rx = r0(h0 − hx)/h0. Принятые коэффициенты:",
            };

            foreach (var cone in result.Rods
                         .GroupBy(r => (r.Level, H: System.Math.Round(r.Cone.H, 3)))
                         .Select(g => g.First()))
            {
                var (h0, r0) = norm.SingleCoefficients(cone.Cone.H, cone.Level);
                var rods = string.Join(", ", result.Rods
                    .Where(r => r.Level == cone.Level && System.Math.Abs(r.Cone.H - cone.Cone.H) < 1e-3)
                    .Select(r => r.Rod.Name).Distinct());
                lines.Add($"   — {cone.Level.Title()}, h = {Fmt.M(cone.Cone.H)} м ({rods}): " +
                          $"h0 = {h0} = {Fmt.M(cone.Cone.H0)} м, r0 = {r0} = {Fmt.M(cone.Cone.R0)} м;");
            }

            var pairs = result.Pairs.GroupBy(p => (p.First, p.Second, p.Level)).Select(g => g.First()).ToList();
            if (pairs.Count > 0)
            {
                lines.Add($"Зоны защиты двойных стержневых молниеприёмников определены по {norm.PairSource}; " +
                          "полуширина зоны на высоте hx: rcx = rc(hc − hx)/hc:");
                foreach (var pair in pairs)
                {
                    var (condition, formula) = norm.PairFormula(pair.First.TotalHeight, pair.Second.TotalHeight, pair.Distance, pair.Level);
                    if (!formula.Contains("rc"))
                        formula += "; rc = r0";
                    lines.Add($"   — {pair.First.Name}–{pair.Second.Name}, {pair.Level.Title()}, L = {Fmt.M(pair.Distance)} м: " +
                              $"{condition}, {formula}; hc = {Fmt.M(pair.Zone.Hc)} м, rc = {Fmt.M(pair.Zone.Rc)} м;");
                }
            }
            return string.Join("\n", lines);
        }

        private static string Conclusion(CalculationResult result)
        {
            if (result.AllProtected)
                return "Все защищаемые объекты и пространства находятся в пределах зон защиты молниеприёмников.";
            var failed = result.Objects.Where(o => !o.IsProtected).Select(o => $"«{o.Object.Name}»");
            return "ВНИМАНИЕ: не обеспечена защита объектов " + string.Join(", ", failed) + ".";
        }
    }
}
