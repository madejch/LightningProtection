using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LightningProtection.Core.Calculation;
using LightningProtection.Core.Model;
using LightningProtection.Core.Report;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;

namespace LightningProtection.Acad.Drawing
{
    /// <summary>
    /// Вид сверху: границы зон на отметках hx с выносками (радиус и высота), защищаемые
    /// пространства, молниеприёмники Мл1…Млn с выносками и размеры между ними.
    /// </summary>
    internal static class PlanDrawer
    {
        public static void Draw(DrawContext ctx, ProtectionProject project, CalculationResult result)
        {
            // Все точки границ зон: выноски выводятся за пределы самой внешней зоны.
            var outline = result.Sections.SelectMany(s => s.Zone.Coordinates).ToList();
            for (var i = 0; i < result.Sections.Count; i++)
                DrawSection(ctx, result, result.Sections[i], i, outline);

            var spaces = project.Objects.Where(o => o.IsCircular).ToList();
            var bottom = outline.Count > 0 ? outline.Min(c => c.Y) : 0;
            for (var i = 0; i < spaces.Count; i++)
                DrawSpace(ctx, spaces[i], bottom - ctx.TextHeight / ctx.Upm * (3 + 4 * i));

            foreach (var rod in project.Rods)
                DrawRod(ctx, rod);

            DrawDimensions(ctx, project, result);
        }

        private static void DrawSection(DrawContext ctx, CalculationResult result, ZoneSection section, int index,
            IReadOnlyList<NtsCoordinate> outline)
        {
            var color = DrawContext.LevelColor(result, section.Level);
            var rings = new List<NtsCoordinate[]>();
            for (var g = 0; g < section.Zone.NumGeometries; g++)
            {
                if (!(section.Zone.GetGeometryN(g) is NtsPolygon polygon))
                    continue;
                rings.Add(polygon.ExteriorRing.Coordinates);
                DrawRing(ctx, polygon.ExteriorRing.Coordinates, color);
                foreach (var hole in polygon.InteriorRings)
                    DrawRing(ctx, hole.Coordinates, color);
            }
            if (rings.Count == 0)
                return;

            // Выноски разных сечений смотрят в разные стороны, чтобы не накладываться.
            var angle = (75 - 35 * index) * Math.PI / 180;
            var dx = Math.Cos(angle);
            var dy = Math.Sin(angle);
            var anchor = rings.SelectMany(r => r).OrderByDescending(c => c.X * dx + c.Y * dy).First();
            var arrow = ctx.ModelPoint(anchor.X, anchor.Y);
            // Полка — на 3 высоты текста дальше самой внешней зоны в этом направлении.
            var outer = outline.Max(c => c.X * dx + c.Y * dy);
            var offset = (outer - (anchor.X * dx + anchor.Y * dy)) * ctx.Upm + ctx.TextHeight * 3;
            ctx.Leader(arrow, arrow + new Vector3d(dx * offset + ctx.TextHeight, dy * offset, 0),
                SectionLabel(result, section), DrawContext.LayerZones, color);
        }

        /// <summary>Подпись зоны: высота hx, уровень защиты, объекты и радиусы rx / rcx.</summary>
        private static string SectionLabel(CalculationResult result, ZoneSection section)
        {
            var lines = new List<string>
            {
                $"Зона защиты на высоте {section.Name} = {Fmt.M(section.Hx)} м ({section.Level.Title()})",
                string.Join(", ", section.Objects.Select(o => o.Name)),
            };

            var rods = result.Rods.Where(r => r.Section == section).ToList();
            var radii = rods.Select(r => Math.Round(r.Rx, 2)).Distinct().ToList();
            lines.Add(radii.Count == 1
                ? $"rx = {Fmt.M(radii[0])} м"
                : "rx: " + string.Join("; ", rods.Select(r => $"{r.Rod.Name} — {Fmt.M(r.Rx)} м")));

            foreach (var pair in result.Pairs.Where(p => p.Section == section && p.Rcx > 0))
                lines.Add($"rcx ({pair.First.Name}–{pair.Second.Name}) = {Fmt.M(pair.Rcx)} м");
            return string.Join("\n", lines);
        }

        private static void DrawRing(DrawContext ctx, NtsCoordinate[] coordinates, short color)
        {
            // Последняя точка кольца совпадает с первой — полилиния замыкается флагом.
            var points = coordinates.Take(coordinates.Length - 1).Select(c => new Point2d(c.X * ctx.Upm, c.Y * ctx.Upm)).ToList();
            if (points.Count >= 3)
                ctx.Polyline(points, true, DrawContext.LayerZones, color);
        }

        /// <summary>Защищаемое пространство; полка выноски — ниже всех зон на отметке <paramref name="landingY"/> (м).</summary>
        private static void DrawSpace(DrawContext ctx, ProtectedObject obj, double landingY)
        {
            var center = ctx.ModelPoint(obj.Center.X, obj.Center.Y);
            var radius = obj.Radius * ctx.Upm;
            ctx.Add(new Circle(center, Vector3d.ZAxis, radius), DrawContext.LayerSpaces, dashed: true);
            ctx.Add(new Circle(center, Vector3d.ZAxis, ctx.TextHeight * 0.4), DrawContext.LayerSpaces);

            var diagonal = new Vector3d(-Math.Sqrt(0.5), -Math.Sqrt(0.5), 0);
            var arrow = center + diagonal * radius;
            ctx.Leader(arrow, new Point3d(arrow.X + ctx.TextHeight * 2, landingY * ctx.Upm, 0),
                obj.IsHemisphere
                    ? $"{obj.Name}\nзащищаемое пространство: полусфера R = {Fmt.M(obj.Radius)} м"
                    : $"{obj.Name}\nзащищаемое пространство: R = {Fmt.M(obj.Radius)} м, H = {Fmt.M(obj.SpaceHeight)} м",
                DrawContext.LayerSpaces);
        }

        private static void DrawRod(DrawContext ctx, LightningRod rod)
        {
            var center = ctx.ModelPoint(rod.X, rod.Y);
            var size = ctx.TextHeight * 0.8;
            ctx.Add(new Circle(center, Vector3d.ZAxis, size), DrawContext.LayerRods);
            ctx.Line(center + new Vector3d(-size, -size, 0), center + new Vector3d(size, size, 0), DrawContext.LayerRods);
            ctx.Line(center + new Vector3d(-size, size, 0), center + new Vector3d(size, -size, 0), DrawContext.LayerRods);
            ctx.Leader(center + new Vector3d(size * 0.7, -size * 0.7, 0),
                center + new Vector3d(ctx.TextHeight * 3, -ctx.TextHeight * 3, 0),
                $"{rod.Name}\nh = {Fmt.M(rod.TotalHeight)} м", DrawContext.LayerRods);
        }

        /// <summary>Расстояния между молниеприёмниками и от осей свечей и клапанов до ближайшего молниеприёмника.</summary>
        private static void DrawDimensions(DrawContext ctx, ProtectionProject project, CalculationResult result)
        {
            var rods = project.Rods;
            var pairs = result.Pairs.Select(p => (p.First, p.Second)).Distinct().ToList();
            for (var i = 0; i + 1 < rods.Count; i++)
                if (!pairs.Contains((rods[i], rods[i + 1])))
                    pairs.Add((rods[i], rods[i + 1]));

            foreach (var (first, second) in pairs)
                Aligned(ctx, new Point2(first.X, first.Y), new Point2(second.X, second.Y), ctx.TextHeight * 5);

            foreach (var obj in project.Objects.Where(o => o.IsCircular))
            {
                var nearest = rods.OrderBy(r => Math.Pow(r.X - obj.Center.X, 2) + Math.Pow(r.Y - obj.Center.Y, 2)).FirstOrDefault();
                if (nearest != null)
                    Aligned(ctx, new Point2(nearest.X, nearest.Y), obj.Center, -ctx.TextHeight * 3);
            }
        }

        private static void Aligned(DrawContext ctx, Point2 a, Point2 b, double offset)
        {
            var p1 = ctx.ModelPoint(a.X, a.Y);
            var p2 = ctx.ModelPoint(b.X, b.Y);
            var direction = p2 - p1;
            if (direction.Length < 1e-6)
                return;
            var normal = new Vector3d(-direction.Y, direction.X, 0).GetNormal();
            ctx.AlignedDim(p1, p2, p1 + direction * 0.5 + normal * offset);
        }
    }
}
