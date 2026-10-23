using System;
using System.Collections.Generic;
using System.Linq;
using LightningProtection.Core.Model;
using LightningProtection.Core.Norms;
using LightningProtection.Core.Zones;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

namespace LightningProtection.Core.Calculation
{
    public sealed class ObjectCheck
    {
        public ProtectedObject Object { get; set; } = null!;
        public ZoneLevel Level { get; set; }
        public double Hx { get; set; }
        public bool IsProtected { get; set; }
        /// <summary>Площадь объекта в плане, не попавшая в зону, м².</summary>
        public double UncoveredArea { get; set; }
        public Geometry Zone { get; set; } = null!;
        public Geometry Footprint { get; set; } = null!;
        public ZoneSection Section { get; set; } = null!;
    }

    /// <summary>Сечение зоны на одном уровне: уникальная пара «уровень защиты + отметка hx».</summary>
    public sealed class ZoneSection
    {
        /// <summary>Обозначение отметки: hx, если сечение одно, иначе hx1, hx2… по возрастанию отметки.</summary>
        public string Name { get; set; } = "hx";
        public ZoneLevel Level { get; set; }
        public double Hx { get; set; }
        public List<ProtectedObject> Objects { get; set; } = new List<ProtectedObject>();
        public Geometry Zone { get; set; } = null!;
    }

    public sealed class RodCheck
    {
        public LightningRod Rod { get; set; } = null!;
        public ZoneLevel Level { get; set; }
        public ConeZone Cone { get; set; }
        public double Hx { get; set; }
        public double Rx { get; set; }
        public ZoneSection Section { get; set; } = null!;
    }

    public sealed class PairCheck
    {
        public LightningRod First { get; set; } = null!;
        public LightningRod Second { get; set; } = null!;
        public double Distance { get; set; }
        public ZoneLevel Level { get; set; }
        public PairZone Zone { get; set; }
        public double Hx { get; set; }
        public double Rcx { get; set; }
        public ZoneSection Section { get; set; } = null!;
    }

    public sealed class StrikeEstimate
    {
        public ProtectedObject Object { get; set; } = null!;
        public double Width { get; set; }
        public double Length { get; set; }
        public double Height { get; set; }
        public double ExpectedStrikes { get; set; }
    }

    public sealed class CalculationResult
    {
        public NormKind Norm { get; set; }
        public double GroundDensity { get; set; }
        public List<ObjectCheck> Objects { get; } = new List<ObjectCheck>();
        public List<ZoneSection> Sections { get; } = new List<ZoneSection>();
        public List<RodCheck> Rods { get; } = new List<RodCheck>();
        public List<PairCheck> Pairs { get; } = new List<PairCheck>();
        public List<StrikeEstimate> Strikes { get; } = new List<StrikeEstimate>();
        public List<string> Errors { get; } = new List<string>();

        public bool AllProtected => Errors.Count == 0 && Objects.Count > 0 && Objects.All(o => o.IsProtected);
    }

    public static class ProtectionCalculator
    {
        /// <summary>Допуск на погрешность аппроксимации окружностей, м².</summary>
        private const double AreaTolerance = 1e-4;

        public static CalculationResult Calculate(ProtectionProject project)
        {
            var norm = ZoneNorm.For(project.Norm);
            var result = new CalculationResult
            {
                Norm = project.Norm,
                GroundDensity = StrikeFrequency.GroundDensity(project.Norm, project.ThunderstormHours),
            };

            foreach (var obj in project.Objects.Where(o => o.Kind == ObjectKind.Building && !o.IsCircular))
                result.Strikes.Add(EstimateStrikes(obj, result.GroundDensity));

            if (project.Objects.Count == 0)
                result.Errors.Add("Не задано ни одного защищаемого объекта.");
            if (project.Rods.Count == 0)
                result.Errors.Add("Не задано ни одного молниеприёмника.");
            if (result.Errors.Count > 0)
                return result;

            try
            {
                var builder = new PlanZoneBuilder(norm);
                var groups = project.Objects
                    .GroupBy(o => (Level: o.GetLevel(project.Norm), Hx: Math.Round(o.CheckElevation, 3)))
                    .OrderBy(g => g.Key.Hx).ThenBy(g => g.Key.Level)
                    .ToList();

                var zones = new Dictionary<(ZoneLevel, double), Geometry>();
                Geometry ZoneAt(ZoneLevel level, double hx)
                {
                    var key = (level, Math.Round(hx, 4));
                    if (!zones.TryGetValue(key, out var cached))
                        zones[key] = cached = builder.Build(project.Rods, level, hx);
                    return cached;
                }

                foreach (var group in groups)
                {
                    var zone = ZoneAt(group.Key.Level, group.Key.Hx);
                    var section = new ZoneSection
                    {
                        Name = groups.Count > 1 ? $"hx{result.Sections.Count + 1}" : "hx",
                        Level = group.Key.Level,
                        Hx = group.Key.Hx,
                        Objects = group.ToList(),
                        Zone = zone,
                    };
                    result.Sections.Add(section);

                    foreach (var obj in group)
                    {
                        var footprint = PlanZoneBuilder.Footprint(obj);
                        var uncovered = obj.IsHemisphere
                            ? HemisphereUncovered(obj, z => ZoneAt(group.Key.Level, z))
                            : Uncovered(footprint, zone);
                        result.Objects.Add(new ObjectCheck
                        {
                            Object = obj,
                            Level = group.Key.Level,
                            Hx = group.Key.Hx,
                            Zone = zone,
                            Footprint = footprint,
                            UncoveredArea = uncovered,
                            IsProtected = uncovered <= AreaTolerance,
                            Section = section,
                        });
                    }

                    AddRodAndPairRows(result, norm, project.Rods, section);
                }
            }
            catch (ArgumentOutOfRangeException ex)
            {
                result.Errors.Add(ex.Message.Split('\n')[0].Trim());
            }

            // Порядок объектов в отчёте — как в проекте.
            result.Objects.Sort((a, b) => project.Objects.IndexOf(a.Object).CompareTo(project.Objects.IndexOf(b.Object)));
            return result;
        }

        /// <summary>Число ступеней, которыми полусфера описывается снаружи при проверке.</summary>
        private const int HemisphereSteps = 12;

        private static double Uncovered(Geometry footprint, Geometry zone)
            => zone.IsEmpty ? footprint.Area : footprint.Difference(zone).Area;

        /// <summary>
        /// Полусфера заменяется описанной ступенчатой фигурой: на высоте z(k+1) проверяется круг радиуса
        /// полусферы на высоте z(k). Сечения зоны с высотой только уменьшаются, поэтому проверка
        /// с запасом. Возвращает наибольшую незащищённую площадь среди ступеней.
        /// </summary>
        private static double HemisphereUncovered(ProtectedObject obj, Func<double, Geometry> zoneAt)
        {
            var center = new NetTopologySuite.Geometries.Coordinate(obj.Center.X, obj.Center.Y);
            var worst = 0.0;
            for (var k = 0; k < HemisphereSteps; k++)
            {
                var z = obj.Radius * k / HemisphereSteps;
                var radius = Math.Sqrt(Math.Max(0, obj.Radius * obj.Radius - z * z));
                var hx = obj.SourceElevation + obj.Radius * (k + 1) / HemisphereSteps;
                worst = Math.Max(worst, Uncovered(PlanZoneBuilder.CircumscribedCircle(center, radius), zoneAt(hx)));
            }
            return worst;
        }

        private static void AddRodAndPairRows(CalculationResult result, ZoneNorm norm, IReadOnlyList<LightningRod> rods,
            ZoneSection section)
        {
            var level = section.Level;
            var hx = section.Hx;
            foreach (var rod in rods)
            {
                var cone = norm.Single(rod.TotalHeight, level);
                result.Rods.Add(new RodCheck { Rod = rod, Level = level, Cone = cone, Hx = hx, Rx = cone.Rx(hx), Section = section });
            }

            foreach (var (i, j) in RodNeighbors.Build(rods).Pairs)
            {
                var distance = Math.Sqrt(Math.Pow(rods[i].X - rods[j].X, 2) + Math.Pow(rods[i].Y - rods[j].Y, 2));
                var pair = norm.Pair(rods[i].TotalHeight, rods[j].TotalHeight, distance, level);
                if (!pair.Joined)
                    continue;
                result.Pairs.Add(new PairCheck
                {
                    First = rods[i],
                    Second = rods[j],
                    Distance = distance,
                    Level = level,
                    Zone = pair,
                    Hx = hx,
                    Rcx = pair.Rcx(hx),
                    Section = section,
                });
            }
        }

        private static StrikeEstimate EstimateStrikes(ProtectedObject building, double density)
        {
            // Габариты здания — по минимальному описанному прямоугольнику контура.
            var rectangle = MinimumDiameter.GetMinimumRectangle(PlanZoneBuilder.Footprint(building));
            var c = rectangle.Coordinates;
            double a = 0, b = 0;
            if (c.Length >= 3)
            {
                a = c[0].Distance(c[1]);
                b = c[1].Distance(c[2]);
            }
            var height = building.SourceElevation;
            return new StrikeEstimate
            {
                Object = building,
                Width = Math.Min(a, b),
                Length = Math.Max(a, b),
                Height = height,
                ExpectedStrikes = StrikeFrequency.ExpectedStrikes(Math.Min(a, b), Math.Max(a, b), height, density),
            };
        }
    }
}
