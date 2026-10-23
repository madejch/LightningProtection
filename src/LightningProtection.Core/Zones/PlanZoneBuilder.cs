using System;
using System.Collections.Generic;
using System.Linq;
using LightningProtection.Core.Model;
using LightningProtection.Core.Norms;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace LightningProtection.Core.Zones
{
    /// <summary>
    /// Строит горизонтальное сечение зоны защиты системы молниеприёмников на высоте hx.
    /// Сечение — объединение кругов rx одиночных молниеприёмников, перемычек двойных
    /// молниеприёмников и треугольников внутри групп из трёх попарно связанных стержней.
    /// </summary>
    public sealed class PlanZoneBuilder
    {
        public const int QuadrantSegments = 32;

        public static readonly GeometryFactory Factory = new GeometryFactory(new PrecisionModel(), 0);

        private readonly ZoneNorm _norm;

        public PlanZoneBuilder(ZoneNorm norm)
        {
            _norm = norm;
        }

        public Geometry Build(IReadOnlyList<LightningRod> rods, ZoneLevel level, double hx)
        {
            var parts = new List<Geometry>();
            var cones = rods.Select(r => _norm.Single(r.TotalHeight, level)).ToArray();
            var centers = rods.Select(r => new Coordinate(r.X, r.Y)).ToArray();
            var radii = cones.Select(c => c.Rx(hx)).ToArray();

            for (var i = 0; i < rods.Count; i++)
                if (radii[i] > 1e-9)
                    parts.Add(Circle(centers[i], radii[i]));

            // Двойные зоны — только для соседних молниеприёмников (РД-87, прил. 3, п. 3).
            var neighbors = RodNeighbors.Build(rods);
            var joinedBelowHc = new bool[rods.Count, rods.Count];
            foreach (var (i, j) in neighbors.Pairs)
            {
                var distance = centers[i].Distance(centers[j]);
                if (distance < 1e-6)
                    continue;
                var pair = _norm.Pair(cones[i].H, cones[j].H, distance, level);
                if (!pair.Joined)
                    continue;

                if (hx < pair.Hc)
                {
                    joinedBelowHc[i, j] = joinedBelowHc[j, i] = true;
                    AddIfNotNull(parts, Bridge(centers[i], radii[i], centers[j], radii[j], pair.Rcx(hx)));
                }
                else if (_norm.HasSpurAboveHc)
                {
                    // Выше hc зона распадается, но вдоль оси пары каждая часть вытянута на lx
                    // (СО 153, формула 3.5): lx = L (h0 − hx) / (2 (h0 − hc)).
                    AddIfNotNull(parts, Spur(centers[i], radii[i], cones[i].H0, centers[j], pair.Hc, hx, distance));
                    AddIfNotNull(parts, Spur(centers[j], radii[j], cones[j].H0, centers[i], pair.Hc, hx, distance));
                }
            }

            // Внутренняя область защищена, если rcx > 0 у всех трёх пар треугольника.
            foreach (var (i, j, k) in neighbors.Triangles)
                if (joinedBelowHc[i, j] && joinedBelowHc[j, k] && joinedBelowHc[i, k])
                    AddIfNotNull(parts, Polygon(centers[i], centers[j], centers[k]));

            if (parts.Count == 0)
                return Factory.CreatePolygon();
            return UnaryUnionOp.Union(parts) ?? Factory.CreatePolygon();
        }

        /// <summary>Контур защищаемого объекта в плане.</summary>
        public static Geometry Footprint(ProtectedObject obj)
        {
            if (obj.IsCircular)
                return CircumscribedCircle(new Coordinate(obj.Center.X, obj.Center.Y), obj.Radius);

            var coords = obj.Contour.Select(p => new Coordinate(p.X, p.Y)).ToList();
            if (!coords[0].Equals2D(coords[coords.Count - 1]))
                coords.Add(coords[0].Copy());
            var polygon = Factory.CreatePolygon(coords.ToArray());
            return polygon.IsValid ? polygon : polygon.Buffer(0);
        }

        public static Geometry Circle(Coordinate center, double radius)
            => Factory.CreatePoint(center).Buffer(radius, QuadrantSegments);

        /// <summary>Описанный многоугольник круга — чтобы аппроксимация не занижала размер объекта.</summary>
        public static Geometry CircumscribedCircle(Coordinate center, double radius)
            => Circle(center, radius / Math.Cos(Math.PI / (QuadrantSegments * 4)));

        /// <summary>Перемычка между молниеприёмниками: касательные к кругам rx через точки ±rcx в середине.</summary>
        private static Geometry? Bridge(Coordinate c1, double r1, Coordinate c2, double r2, double rcx)
        {
            var dx = c2.X - c1.X;
            var dy = c2.Y - c1.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var nx = -dy / length;
            var ny = dx / length;
            var mid = new Coordinate((c1.X + c2.X) / 2, (c1.Y + c2.Y) / 2);

            var left = new Coordinate(mid.X + nx * rcx, mid.Y + ny * rcx);
            var right = new Coordinate(mid.X - nx * rcx, mid.Y - ny * rcx);

            return Polygon(
                c1.Copy(),
                SideTangent(left, c1, r1, nx, ny),
                left,
                SideTangent(left, c2, r2, nx, ny),
                c2.Copy(),
                SideTangent(right, c2, r2, -nx, -ny),
                right,
                SideTangent(right, c1, r1, -nx, -ny));
        }

        /// <summary>Выступ зоны одного молниеприёмника в сторону другого выше уровня hc.</summary>
        private static Geometry? Spur(Coordinate center, double rx, double h0, Coordinate toward,
            double hc, double hx, double distance)
        {
            if (hx >= h0 || h0 - hc < 1e-9)
                return null;
            var lx = distance * (h0 - hx) / (2 * (h0 - hc));
            if (lx <= rx + 1e-9)
                return null;

            var ux = (toward.X - center.X) / distance;
            var uy = (toward.Y - center.Y) / distance;
            var tip = new Coordinate(center.X + ux * lx, center.Y + uy * lx);
            if (rx <= 1e-9)
                return null;

            Tangents(tip, center, rx, out var t1, out var t2);
            return Polygon(center.Copy(), t1, tip, t2);
        }

        /// <summary>Точка касания из внешней точки p, лежащая со стороны нормали (nx, ny).</summary>
        private static Coordinate SideTangent(Coordinate p, Coordinate c, double r, double nx, double ny)
        {
            if (r <= 1e-9)
                return c.Copy();
            if (p.Distance(c) <= r)
                return p.Copy();

            Tangents(p, c, r, out var t1, out var t2);
            var d1 = (t1.X - c.X) * nx + (t1.Y - c.Y) * ny;
            var d2 = (t2.X - c.X) * nx + (t2.Y - c.Y) * ny;
            return d1 >= d2 ? t1 : t2;
        }

        private static void Tangents(Coordinate p, Coordinate c, double r, out Coordinate t1, out Coordinate t2)
        {
            var d = p.Distance(c);
            var baseAngle = Math.Atan2(p.Y - c.Y, p.X - c.X);
            var delta = Math.Acos(Math.Min(1, r / d));
            t1 = new Coordinate(c.X + r * Math.Cos(baseAngle + delta), c.Y + r * Math.Sin(baseAngle + delta));
            t2 = new Coordinate(c.X + r * Math.Cos(baseAngle - delta), c.Y + r * Math.Sin(baseAngle - delta));
        }

        private static Geometry? Polygon(params Coordinate[] points)
        {
            var ring = points.Concat(new[] { points[0].Copy() }).ToArray();
            var polygon = Factory.CreatePolygon(ring);
            var result = polygon.IsValid ? polygon : polygon.Buffer(0);
            return result.IsEmpty || result.Area < 1e-9 ? null : result;
        }

        private static void AddIfNotNull(List<Geometry> parts, Geometry? geometry)
        {
            if (geometry != null)
                parts.Add(geometry);
        }
    }
}
