using System;
using System.Collections.Generic;
using System.Linq;
using LightningProtection.Core.Model;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace LightningProtection.Core.Zones
{
    /// <summary>
    /// Соседние молниеприёмники. РД 34.21.122-87, прил. 3, п. 3: зона многократного молниеприёмника
    /// определяется по попарно взятым соседним молниеприёмникам. Соседними считаются пары,
    /// соединённые ребром триангуляции Делоне; внутренние треугольники — её треугольники.
    /// </summary>
    public sealed class RodNeighbors
    {
        private RodNeighbors(List<(int A, int B)> pairs, List<(int A, int B, int C)> triangles)
        {
            Pairs = pairs;
            Triangles = triangles;
        }

        /// <summary>Пары соседних молниеприёмников (индексы в списке, A &lt; B).</summary>
        public IReadOnlyList<(int A, int B)> Pairs { get; }

        /// <summary>Треугольники из трёх попарно соседних молниеприёмников.</summary>
        public IReadOnlyList<(int A, int B, int C)> Triangles { get; }

        public static RodNeighbors Build(IReadOnlyList<LightningRod> rods)
        {
            var pairs = new List<(int, int)>();
            var triangles = new List<(int, int, int)>();
            if (rods.Count < 2)
                return new RodNeighbors(pairs, triangles);
            if (rods.Count == 2)
            {
                pairs.Add((0, 1));
                return new RodNeighbors(pairs, triangles);
            }

            var factory = PlanZoneBuilder.Factory;
            var points = rods.Select(r => new Coordinate(r.X, r.Y)).ToArray();
            int IndexOf(Coordinate c)
            {
                for (var i = 0; i < points.Length; i++)
                    if (points[i].Distance(c) < 1e-6)
                        return i;
                return -1;
            }

            var builder = new DelaunayTriangulationBuilder();
            builder.SetSites(factory.CreateMultiPointFromCoords(points));
            var edges = builder.GetEdges(factory);
            for (var g = 0; g < edges.NumGeometries; g++)
            {
                var line = edges.GetGeometryN(g).Coordinates;
                AddPair(pairs, IndexOf(line[0]), IndexOf(line[line.Length - 1]));
            }

            var polygons = builder.GetTriangles(factory);
            for (var g = 0; g < polygons.NumGeometries; g++)
            {
                var c = polygons.GetGeometryN(g).Coordinates;
                var ids = new[] { IndexOf(c[0]), IndexOf(c[1]), IndexOf(c[2]) };
                if (ids.All(i => i >= 0) && ids.Distinct().Count() == 3)
                {
                    Array.Sort(ids);
                    triangles.Add((ids[0], ids[1], ids[2]));
                }
            }

            // Все молниеприёмники на одной прямой: треугольников нет, соседние — подряд вдоль прямой.
            if (pairs.Count == 0)
            {
                var direction = new Coordinate(points.Last().X - points[0].X, points.Last().Y - points[0].Y);
                var order = Enumerable.Range(0, points.Length)
                    .OrderBy(i => points[i].X * direction.X + points[i].Y * direction.Y).ToList();
                for (var k = 0; k + 1 < order.Count; k++)
                    AddPair(pairs, order[k], order[k + 1]);
            }

            return new RodNeighbors(pairs, triangles);
        }

        private static void AddPair(List<(int, int)> pairs, int a, int b)
        {
            if (a < 0 || b < 0 || a == b)
                return;
            var pair = a < b ? (a, b) : (b, a);
            if (!pairs.Contains(pair))
                pairs.Add(pair);
        }
    }
}
