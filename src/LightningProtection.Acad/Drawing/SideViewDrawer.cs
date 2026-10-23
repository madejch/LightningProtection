using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LightningProtection.Core.Calculation;
using LightningProtection.Core.Model;
using LightningProtection.Core.Report;
using LightningProtection.Core.Zones;

namespace LightningProtection.Acad.Drawing
{
    /// <summary>
    /// Вид сбоку. Начало вида — точка вставки на уровне ±0,000 у левого края.
    /// Если масштаб вида отличается от масштаба плана, геометрия вида увеличивается
    /// в k = Mплана / Mвида раз, а размеры показывают натуральные значения (DIMLFAC = 1 / k).
    /// Текст и размеры — той же высоты, что на плане, так как лист печатается в масштабе плана.
    /// </summary>
    internal sealed class SideViewDrawer
    {
        private const double Margin = 3;

        private readonly DrawContext _ctx;
        private readonly ProtectionProject _project;
        private readonly CalculationResult _result;
        private readonly Point2 _origin;
        private readonly double _sideScale;
        private readonly double _k;
        private readonly SideViewModel _view;
        private readonly double _uStart;
        private readonly double _uEnd;

        private SideViewDrawer(DrawContext ctx, ProtectionProject project, CalculationResult result, Point2 origin, double sideScale)
        {
            _ctx = ctx;
            _project = project;
            _result = result;
            _origin = origin;
            _sideScale = sideScale;
            _k = ctx.PlanScale / sideScale;
            _view = SideViewBuilder.Build(project, project.Drawing.SideViewAngleDeg);
            _uStart = _view.UMin - Margin;
            _uEnd = _view.UMax + Margin;
        }

        public static void Draw(DrawContext ctx, ProtectionProject project, CalculationResult result, Point2 origin, double sideScale)
            => new SideViewDrawer(ctx, project, result, origin, sideScale).Draw();

        private double Th => _ctx.TextHeight;

        /// <summary>Длина в метрах в единицах чертежа вида сбоку.</summary>
        private double L(double meters) => meters * _ctx.Upm * _k;

        private Point2d P(double u, double z) => new Point2d(_origin.X + L(u - _uStart), _origin.Y + L(z));

        private Point3d P3(double u, double z)
        {
            var p = P(u, z);
            return new Point3d(p.X, p.Y, 0);
        }

        private void Draw()
        {
            _ctx.Polyline(new[] { P(_uStart, 0), P(_uEnd, 0) }, false, DrawContext.LayerSideView);
            _ctx.Text(P3(_uStart, 0) + new Vector3d(0, -Th * 0.5, 0), "±0,000", DrawContext.LayerSideView,
                attachment: AttachmentPoint.TopLeft);

            DrawProfiles();
            foreach (var obj in _view.Objects)
            {
                if (obj.Source.IsCircular)
                    DrawSpace(obj);
                else
                    DrawBuilding(obj);
            }
            foreach (var rod in _view.Rods)
                DrawRod(rod);
            DrawLevels();
            DrawHorizontalDimensions();

            _ctx.Text(P3((_uStart + _uEnd) / 2, 0) + new Vector3d(0, -Th * 9, 0),
                $"Вид сбоку   М 1:{_sideScale:0}", DrawContext.LayerText,
                attachment: AttachmentPoint.TopCenter, height: Th * 1.4);
        }

        private void DrawProfiles()
        {
            for (var i = 0; i < _view.Profiles.Count; i++)
            {
                var profile = _view.Profiles[i];
                var color = DrawContext.LevelColor(_result, profile.Level);
                foreach (var outline in profile.Outlines)
                {
                    // Нижняя сторона контура совпадает с линией земли.
                    var points = outline.Take(outline.Count - 1).Select(p => P(p.X, p.Y)).ToList();
                    _ctx.Polyline(points, true, DrawContext.LayerZones, color);
                }

                // Выноска к левому склону зоны на половине высоты вершины.
                var apex = profile.Outlines.SelectMany(o => o).OrderByDescending(p => p.Y).FirstOrDefault();
                var left = profile.Outlines.SelectMany(o => o).OrderBy(p => p.X).FirstOrDefault();
                if (apex == null || left == null)
                    continue;
                var t = 0.35 + 0.15 * i;
                var arrow = P3(left.X + (apex.X - left.X) * t, apex.Y * t);
                _ctx.Leader(arrow, arrow + new Vector3d(-Th * 14, Th * (3 + 2 * i), 0),
                    $"Граница зоны защиты\n{profile.Level.Title()}", DrawContext.LayerZones, color);
            }
        }

        /// <summary>Здание — жирный контур, выноска с наименованием и габаритами, размер высоты.</summary>
        private void DrawBuilding(SideObject obj)
        {
            var source = obj.Source;
            var outline = _ctx.Polyline(Rectangle(obj.UMin, obj.UMax, source.BaseElevation, source.SourceElevation), true,
                DrawContext.LayerBuildings, 7);
            outline.LineWeight = LineWeight.LineWeight050;

            var envelope = PlanSize(source);
            var arrow = P3(obj.UMin + (obj.UMax - obj.UMin) * 0.3, source.SourceElevation);
            _ctx.Leader(arrow, arrow + new Vector3d(Th * 4, Th * 6, 0),
                $"Здание «{source.Name}»\nв плане {Fmt.M(envelope.Width)} × {Fmt.M(envelope.Length)} м\n" +
                $"высота H = {Fmt.M(source.SourceElevation - source.BaseElevation)} м, верх {Fmt.Elev(source.SourceElevation)}",
                DrawContext.LayerBuildings);

            var (edge, dimX) = VerticalDimPlace(obj.UMin, obj.UMax);
            _ctx.LinearDim(P3(edge, source.BaseElevation), P3(edge, source.SourceElevation),
                new Point3d(dimX, P3(edge, 0).Y, 0), Math.PI / 2, "H=<>", 1 / _k);
        }

        private (double Width, double Length) PlanSize(ProtectedObject building)
        {
            var strike = _result.Strikes.FirstOrDefault(s => s.Object == building);
            if (strike != null)
                return (strike.Width, strike.Length);
            var xs = building.Contour.Select(p => p.X).ToList();
            var ys = building.Contour.Select(p => p.Y).ToList();
            var a = xs.Max() - xs.Min();
            var b = ys.Max() - ys.Min();
            return (Math.Min(a, b), Math.Max(a, b));
        }

        /// <summary>Свеча или клапан: труба, пунктирный цилиндр защищаемого пространства, выноска.</summary>
        private void DrawSpace(SideObject obj)
        {
            var source = obj.Source;
            _ctx.Polyline(Rectangle(obj.U - 0.15, obj.U + 0.15, source.BaseElevation, source.SourceElevation), true,
                DrawContext.LayerSideView);
            _ctx.Polyline(Rectangle(obj.UMin, obj.UMax, source.SourceElevation, source.CheckElevation), true,
                DrawContext.LayerSpaces, dashed: true);

            var arrow = P3(obj.UMax, (source.SourceElevation + source.CheckElevation) / 2);
            _ctx.Leader(arrow, arrow + new Vector3d(Th * 4, Th * 3, 0),
                $"{source.Name}\nобрез {Fmt.Elev(source.SourceElevation)}; R = {Fmt.M(source.Radius)} м, H = {Fmt.M(source.SpaceHeight)} м",
                DrawContext.LayerSpaces);
            var (edge, dimX) = VerticalDimPlace(obj.UMin, obj.UMax);
            _ctx.LinearDim(P3(edge, source.SourceElevation), P3(edge, source.CheckElevation),
                new Point3d(dimX, P3(edge, 0).Y, 0), Math.PI / 2, "H=<>", 1 / _k);
        }

        /// <summary>Молниеприёмник: мачта, выноска Млn, размер полной высоты h.</summary>
        private void DrawRod(SideRod rod)
        {
            _ctx.Polyline(new[] { P(rod.U, rod.Base), P(rod.U, rod.Top) }, false, DrawContext.LayerRods);
            var top = P3(rod.U, rod.Top);
            _ctx.Leader(top, top + new Vector3d(Th * 3, Th * 3, 0), $"{rod.Name}\nh = {Fmt.M(rod.Top)} м", DrawContext.LayerRods);
            var (_, dimX) = VerticalDimPlace(rod.U, rod.U);
            _ctx.LinearDim(P3(rod.U, 0), top, new Point3d(dimX, top.Y, 0), Math.PI / 2, "h=<>", 1 / _k);
        }

        /// <summary>
        /// Отметки hx1, hx2…: пунктир через проверяемые объекты, размеры от земли справа
        /// и радиусы rx на каждой отметке у крайних молниеприёмников.
        /// </summary>
        private void DrawLevels()
        {
            var rightEdge = P3(_uEnd, 0);
            for (var i = 0; i < _result.Sections.Count; i++)
            {
                var section = _result.Sections[i];
                var color = DrawContext.LevelColor(_result, section.Level);
                var objects = _view.Objects.Where(o => section.Objects.Contains(o.Source)).ToList();
                var from = objects.Min(o => o.UMin);
                _ctx.Line(P3(from, section.Hx), P3(_uEnd, section.Hx), DrawContext.LayerSideView, color, dashed: true);

                var dimX = rightEdge.X + Th * (3 + 4 * i);
                _ctx.LinearDim(P3(_uEnd, 0), P3(_uEnd, section.Hx), new Point3d(dimX, rightEdge.Y, 0), Math.PI / 2,
                    $"{section.Name}=<>", 1 / _k);

                DrawRx(section, color);
            }
        }

        private void DrawRx(ZoneSection section, short color)
        {
            var rows = _result.Rods.Where(r => r.Section == section && r.Rx > 0).ToList();
            if (rows.Count == 0)
                return;
            // Порядок молниеприёмников в модели вида совпадает с проектом.
            double U(LightningRod rod) => _view.Rods[_project.Rods.IndexOf(rod)].U;
            var leftmost = rows.OrderBy(r => U(r.Rod)).First();
            var rightmost = rows.OrderBy(r => U(r.Rod)).Last();
            var label = section.Name.Replace("hx", "rx");

            var lu = U(leftmost.Rod);
            _ctx.LinearDim(P3(lu - leftmost.Rx, section.Hx), P3(lu, section.Hx), P3(lu, section.Hx) + new Vector3d(0, Th * 0.2, 0),
                0, $"{label}=<>", 1 / _k).ColorIndex = color;
            if (rightmost == leftmost)
                return;
            var ru = U(rightmost.Rod);
            _ctx.LinearDim(P3(ru, section.Hx), P3(ru + rightmost.Rx, section.Hx), P3(ru, section.Hx) + new Vector3d(0, Th * 0.2, 0),
                0, $"{label}=<>", 1 / _k).ColorIndex = color;
        }

        /// <summary>Цепочка размеров под землёй: оси молниеприёмников, грани зданий, оси свечей и клапанов.</summary>
        private void DrawHorizontalDimensions()
        {
            var stations = new List<double>();
            stations.AddRange(_view.Rods.Select(r => r.U));
            foreach (var obj in _view.Objects)
            {
                if (obj.Source.IsCircular)
                    stations.Add(obj.U);
                else
                {
                    stations.Add(obj.UMin);
                    stations.Add(obj.UMax);
                }
            }

            var sorted = stations.OrderBy(s => s).ToList();
            var unique = new List<double>();
            foreach (var s in sorted)
                if (unique.Count == 0 || s - unique[unique.Count - 1] > 0.05)
                    unique.Add(s);

            var lineY = P3(_uStart, 0).Y - Th * 4;
            for (var i = 0; i + 1 < unique.Count; i++)
            {
                var a = P3(unique[i], 0);
                _ctx.LinearDim(a, P3(unique[i + 1], 0), new Point3d(a.X, lineY, 0), 0, "", 1 / _k);
            }
        }

        /// <summary>
        /// Место вертикального размера элемента [uLeft; uRight]: слева, если там свободно,
        /// иначе справа, иначе левее ближайшей помехи. Возвращает измеряемую грань и X размерной линии.
        /// </summary>
        private (double Edge, double DimX) VerticalDimPlace(double uLeft, double uRight)
        {
            var gap = Th * 2.5;
            var clearance = Th * 4 / (_ctx.Upm * _k);
            if (!Busy(uLeft - clearance, uLeft))
                return (uLeft, P3(uLeft, 0).X - gap);
            if (!Busy(uRight, uRight + clearance))
                return (uRight, P3(uRight, 0).X + gap);
            // Левее помехи, с запасом на её собственный размер (у мачты он тоже слева).
            var obstacle = Obstacles(uLeft - clearance, uLeft).Min();
            return (uLeft, P3(obstacle, 0).X - gap * 2.2);
        }

        /// <summary>Вертикали мачт и грани объектов строго внутри интервала (a; b).</summary>
        private IEnumerable<double> Obstacles(double a, double b)
            => _view.Rods.Select(r => r.U)
                .Concat(_view.Objects.SelectMany(o => new[] { o.UMin, o.UMax }))
                .Where(u => u > a + 1e-6 && u < b - 1e-6);

        private bool Busy(double a, double b) => Obstacles(a, b).Any();

        private IReadOnlyList<Point2d> Rectangle(double u1, double u2, double z1, double z2)
            => new[] { P(u1, z1), P(u2, z1), P(u2, z2), P(u1, z2) };
    }
}
