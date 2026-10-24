using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using LightningProtection.Core.Calculation;
using LightningProtection.Core.Model;
using LightningProtection.Core.Report;

namespace LightningProtection.Acad.Drawing
{
    /// <summary>
    /// Вид сбоку, таблицы и описательная часть участка. Уже размещённые части рисуются на своих
    /// местах; новые строятся во временном положении и ставятся указанием с предпросмотром.
    /// Несколько новых частей ставятся одной компоновкой, как на листе: таблицы слева вверху,
    /// описание справа, вид сбоку под ними. Ключ «Отдельно» — ставить по одной.
    /// </summary>
    internal sealed class SheetPlacement
    {
        private enum PartKind
        {
            Tables,
            Description,
            SideView,
        }

        private sealed class Part
        {
            public PartKind Kind;
            public string Title = "";
            public List<ObjectId> Ids = new List<ObjectId>();
            /// <summary>Точка вставки части (для вида сбоку — левый край на ±0,000, для таблиц и текста — левый верхний угол).</summary>
            public Point3d Base;
            public Extents3d Extents;
        }

        private readonly DrawContext _ctx;
        private readonly Editor _ed;
        private readonly ProtectionSystem _system;
        private readonly ProtectionProject _project;
        private readonly CalculationResult _result;
        private readonly double _sideScale;
        private readonly string? _template;

        public SheetPlacement(DrawContext ctx, Editor ed, ProtectionSystem system, ProtectionProject project,
            CalculationResult result, double sideScale, string? template)
        {
            _ctx = ctx;
            _ed = ed;
            _system = system;
            _project = project;
            _result = result;
            _sideScale = sideScale;
            _template = template;
        }

        private double Gap => _ctx.TextHeight * 6;

        public void Draw()
        {
            var parts = new List<Part>();
            foreach (var kind in new[] { PartKind.Tables, PartKind.Description, PartKind.SideView })
            {
                var placed = PointOf(kind);
                if (placed != null)
                    DrawPart(kind, placed);
                else
                    parts.Add(Build(kind));
            }
            if (parts.Count == 0)
                return;

            if (parts.Count > 1)
            {
                Arrange(parts);
                var status = Place(parts, parts[0].Base,
                    $"{_system.Name}. Точка вставки листа (левый верхний угол таблиц)", true);
                if (status == PromptStatus.OK)
                    return;
                if (status != PromptStatus.Keyword)
                {
                    Discard(parts);
                    return;
                }
            }

            foreach (var part in parts)
            {
                var status = Place(new[] { part }, part.Base, $"{_system.Name}. Точка вставки: {part.Title}", false);
                if (status != PromptStatus.OK)
                    Discard(new[] { part });
            }
        }

        private Point2? PointOf(PartKind kind) => kind switch
        {
            PartKind.Tables => _system.TableInsertPoint,
            PartKind.Description => _system.DescriptionInsertPoint,
            _ => _system.SideViewInsertPoint,
        };

        private void SetPoint(PartKind kind, Point3d point)
        {
            var value = new Point2(point.X, point.Y);
            switch (kind)
            {
                case PartKind.Tables:
                    _system.TableInsertPoint = value;
                    break;
                case PartKind.Description:
                    _system.DescriptionInsertPoint = value;
                    break;
                default:
                    _system.SideViewInsertPoint = value;
                    break;
            }
        }

        private void DrawPart(PartKind kind, Point2 origin)
        {
            switch (kind)
            {
                case PartKind.Tables:
                    SheetDrawer.DrawTables(_ctx, _system.Name, ReportBuilder.BuildTables(_project, _result), origin);
                    break;
                case PartKind.Description:
                    SheetDrawer.DrawDescription(_ctx, _system.Name, DescriptionBuilder.Build(_project, _result, _template), origin);
                    break;
                default:
                    SideViewDrawer.Draw(_ctx, _project, _result, origin, _sideScale);
                    break;
            }
        }

        /// <summary>Строит часть в начале координат и запоминает созданные объекты и габариты.</summary>
        private Part Build(PartKind kind)
        {
            var part = new Part
            {
                Kind = kind,
                Base = Point3d.Origin,
                Title = kind switch
                {
                    PartKind.Tables => "таблицы расчёта",
                    PartKind.Description => "описательная часть",
                    _ => "вид сбоку (левый край, отметка ±0,000)",
                },
            };
            _ctx.Capture = part.Ids;
            try
            {
                DrawPart(kind, new Point2(0, 0));
            }
            finally
            {
                _ctx.Capture = null;
            }
            part.Extents = ExtentsOf(part.Ids);
            return part;
        }

        /// <summary>Компоновка как на листе: таблицы, справа описание, ниже вид сбоку.</summary>
        private void Arrange(List<Part> parts)
        {
            var tables = parts.FirstOrDefault(p => p.Kind == PartKind.Tables);
            var description = parts.FirstOrDefault(p => p.Kind == PartKind.Description);
            var side = parts.FirstOrDefault(p => p.Kind == PartKind.SideView);

            var top = parts[0];
            var right = top.Extents.MinPoint.X;
            var bottom = top.Extents.MinPoint.Y;
            if (tables != null)
            {
                right = tables.Extents.MaxPoint.X;
                bottom = tables.Extents.MinPoint.Y;
            }
            if (description != null && tables != null)
            {
                Move(description, new Point3d(right + Gap, tables.Base.Y, 0) - description.Base);
                bottom = Math.Min(bottom, description.Extents.MinPoint.Y);
            }
            else if (description != null)
            {
                bottom = description.Extents.MinPoint.Y;
            }

            if (side != null && side != top)
            {
                var left = parts.Where(p => p != side).Min(p => p.Extents.MinPoint.X);
                var target = new Point3d(left - side.Extents.MinPoint.X + side.Base.X,
                    bottom - Gap - (side.Extents.MaxPoint.Y - side.Base.Y), 0);
                Move(side, target - side.Base);
            }
        }

        /// <summary>Предпросмотр и указание точки; при успехе части переносятся на место.</summary>
        private PromptStatus Place(IReadOnlyList<Part> parts, Point3d basePoint, string message, bool offerSeparate)
        {
            var extents = parts[0].Extents;
            foreach (var part in parts.Skip(1))
                extents.AddExtents(part.Extents);
            var size = extents.MaxPoint - extents.MinPoint;
            var mm = 1000 / _ctx.Upm / _ctx.PlanScale;
            message += $" — займёт {size.X * mm:0} × {size.Y * mm:0} мм на листе М 1:{_ctx.PlanScale:0}";

            var entities = parts.SelectMany(p => p.Ids)
                .Select(id => (Entity)_ctx.Tr.GetObject(id, OpenMode.ForRead))
                .ToList();
            var jig = new PlacementJig(entities, basePoint, extents, message, offerSeparate);
            var result = _ed.Drag(jig);
            if (result.Status == PromptStatus.Keyword)
                return PromptStatus.Keyword;
            if (result.Status != PromptStatus.OK)
                return result.Status;

            foreach (var part in parts)
            {
                Move(part, jig.Displacement);
                SetPoint(part.Kind, part.Base);
            }
            return PromptStatus.OK;
        }

        private void Move(Part part, Vector3d displacement)
        {
            if (displacement.Length < 1e-9)
                return;
            var matrix = Matrix3d.Displacement(displacement);
            foreach (var id in part.Ids)
                ((Entity)_ctx.Tr.GetObject(id, OpenMode.ForWrite)).TransformBy(matrix);
            part.Base += displacement;
            part.Extents = new Extents3d(part.Extents.MinPoint + displacement, part.Extents.MaxPoint + displacement);
        }

        /// <summary>Отказ от размещения: построенные во временном положении объекты удаляются.</summary>
        private void Discard(IEnumerable<Part> parts)
        {
            foreach (var id in parts.SelectMany(p => p.Ids))
                _ctx.Tr.GetObject(id, OpenMode.ForWrite).Erase();
        }

        private Extents3d ExtentsOf(IEnumerable<ObjectId> ids)
        {
            Extents3d? extents = null;
            foreach (var id in ids)
            {
                var entity = (Entity)_ctx.Tr.GetObject(id, OpenMode.ForRead);
                Extents3d box;
                try
                {
                    box = entity.GeometricExtents;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    continue;
                }
                if (extents == null)
                    extents = box;
                else
                {
                    var e = extents.Value;
                    e.AddExtents(box);
                    extents = e;
                }
            }
            return extents ?? new Extents3d(Point3d.Origin, Point3d.Origin);
        }
    }
}
