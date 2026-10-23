using System;
using System.Collections.Generic;
using System.Linq;
using LightningProtection.Core.Model;
using LightningProtection.Core.Norms;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace LightningProtection.Core.Zones
{
    public sealed class SideRod
    {
        public string Name { get; set; } = "";
        public double U { get; set; }
        public double Base { get; set; }
        public double Top { get; set; }
    }

    public sealed class SideObject
    {
        public ProtectedObject Source { get; set; } = null!;
        public double UMin { get; set; }
        public double UMax { get; set; }
        public double U => (UMin + UMax) / 2;
    }

    public sealed class SideProfile
    {
        public ZoneLevel Level { get; set; }
        /// <summary>Контуры зоны в координатах вида: X — вдоль оси вида, Y — отметка.</summary>
        public List<List<Point2>> Outlines { get; set; } = new List<List<Point2>>();
    }

    public sealed class SideViewModel
    {
        public List<SideRod> Rods { get; } = new List<SideRod>();
        public List<SideObject> Objects { get; } = new List<SideObject>();
        public List<SideProfile> Profiles { get; } = new List<SideProfile>();
        public double UMin { get; set; }
        public double UMax { get; set; }
    }

    /// <summary>
    /// Вид сбоку: проекция молниеприёмников, объектов и границ зон на вертикальную плоскость.
    /// Конус одиночного молниеприёмника проецируется в треугольник (r0 у земли, h0 в вершине).
    /// Зона двойного молниеприёмника добавляет «конёк» — ломаную h0 → hc → h0 над серединой пары.
    /// </summary>
    public static class SideViewBuilder
    {
        public static SideViewModel Build(ProtectionProject project, double angleDeg)
        {
            var norm = ZoneNorm.For(project.Norm);
            var angle = angleDeg * Math.PI / 180;
            var ax = Math.Cos(angle);
            var ay = Math.Sin(angle);
            double Project(double x, double y) => x * ax + y * ay;

            var model = new SideViewModel();
            foreach (var rod in project.Rods)
                model.Rods.Add(new SideRod { Name = rod.Name, U = Project(rod.X, rod.Y), Base = rod.BaseElevation, Top = rod.TotalHeight });

            foreach (var obj in project.Objects)
            {
                double uMin, uMax;
                if (obj.IsCircular)
                {
                    var u = Project(obj.Center.X, obj.Center.Y);
                    uMin = u - obj.Radius;
                    uMax = u + obj.Radius;
                }
                else
                {
                    var us = obj.Contour.Select(p => Project(p.X, p.Y)).ToList();
                    uMin = us.Min();
                    uMax = us.Max();
                }
                model.Objects.Add(new SideObject { Source = obj, UMin = uMin, UMax = uMax });
            }

            foreach (var level in project.Objects.Select(o => o.GetLevel(project.Norm)).Distinct().OrderBy(l => l))
                model.Profiles.Add(BuildProfile(project.Rods, norm, level, Project));

            var all = model.Rods.Select(r => r.U).Concat(model.Objects.SelectMany(o => new[] { o.UMin, o.UMax }))
                .Concat(model.Profiles.SelectMany(p => p.Outlines.SelectMany(o => o.Select(pt => pt.X)))).ToList();
            model.UMin = all.Count > 0 ? all.Min() : 0;
            model.UMax = all.Count > 0 ? all.Max() : 0;
            return model;
        }

        private static SideProfile BuildProfile(IReadOnlyList<LightningRod> rods, ZoneNorm norm, ZoneLevel level,
            Func<double, double, double> project)
        {
            var factory = PlanZoneBuilder.Factory;
            var parts = new List<Geometry>();
            var cones = rods.Select(r => norm.Single(r.TotalHeight, level)).ToArray();
            var us = rods.Select(r => project(r.X, r.Y)).ToArray();

            for (var i = 0; i < rods.Count; i++)
            {
                parts.Add(factory.CreatePolygon(new[]
                {
                    new Coordinate(us[i] - cones[i].R0, 0),
                    new Coordinate(us[i], cones[i].H0),
                    new Coordinate(us[i] + cones[i].R0, 0),
                    new Coordinate(us[i] - cones[i].R0, 0),
                }));
            }

            for (var i = 0; i < rods.Count; i++)
            {
                for (var j = i + 1; j < rods.Count; j++)
                {
                    var distance = Math.Sqrt(Math.Pow(rods[i].X - rods[j].X, 2) + Math.Pow(rods[i].Y - rods[j].Y, 2));
                    var pair = norm.Pair(cones[i].H, cones[j].H, distance, level);
                    if (!pair.Joined || Math.Abs(us[i] - us[j]) < 1e-3)
                        continue;

                    var (a, b) = us[i] < us[j] ? (i, j) : (j, i);
                    var mid = (us[a] + us[b]) / 2;
                    var ridge = factory.CreatePolygon(new[]
                    {
                        new Coordinate(us[a], 0),
                        new Coordinate(us[a], cones[a].H0),
                        new Coordinate(mid, pair.Hc),
                        new Coordinate(us[b], cones[b].H0),
                        new Coordinate(us[b], 0),
                        new Coordinate(us[a], 0),
                    });
                    parts.Add(ridge.IsValid ? ridge : ridge.Buffer(0));
                }
            }

            var union = UnaryUnionOp.Union(parts);
            var profile = new SideProfile { Level = level };
            if (union == null)
                return profile;
            for (var g = 0; g < union.NumGeometries; g++)
            {
                if (union.GetGeometryN(g) is Polygon polygon)
                    profile.Outlines.Add(polygon.ExteriorRing.Coordinates.Select(c => new Point2(c.X, c.Y)).ToList());
            }
            return profile;
        }
    }
}
