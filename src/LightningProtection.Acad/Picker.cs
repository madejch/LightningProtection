using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using LightningProtection.Core.Model;

namespace LightningProtection.Acad
{
    /// <summary>Указание объектов и точек на чертеже. Координаты переводятся в метры МСК.</summary>
    internal sealed class Picker
    {
        private readonly Editor _ed;
        private readonly Database _db;
        private readonly ProtectionProject _project;

        public Picker(Editor ed, Database db, ProtectionProject project)
        {
            _ed = ed;
            _db = db;
            _project = project;
        }

        private double Upm => _project.Drawing.UnitsPerMeter;

        public void AddBuilding()
        {
            var options = new PromptEntityOptions("\nВыберите замкнутую полилинию контура здания: ");
            options.SetRejectMessage("\nНужна полилиния.");
            options.AddAllowedClass(typeof(Polyline), true);
            var selection = _ed.GetEntity(options);
            if (selection.Status != PromptStatus.OK)
                return;

            List<Point2> contour;
            using (var tr = _db.TransactionManager.StartTransaction())
            {
                var polyline = (Polyline)tr.GetObject(selection.ObjectId, OpenMode.ForRead);
                contour = ReadContour(polyline);
                tr.Commit();
            }
            if (contour.Count < 3)
            {
                _ed.WriteMessage("\nУ контура меньше трёх вершин.");
                return;
            }

            var name = AskString("Наименование здания", $"Здание {_project.Objects.Count(o => o.Kind == ObjectKind.Building) + 1}");
            if (name == null)
                return;
            var top = AskDouble("Отметка верха здания (кровля, парапет), м", 10);
            if (top == null)
                return;

            var building = ObjectPresets.Building(name, top.Value);
            building.Contour = contour;
            _project.Objects.Add(building);
        }

        public void AddGasVent()
        {
            var axis = AskPoint("\nУкажите ось продувочной свечи: ");
            if (axis == null)
                return;
            var name = AskString("Наименование", "Продувочная свеча ГРУ");
            if (name == null)
                return;
            var outlet = AskDouble("Отметка обреза свечи, м", 6);
            if (outlet == null)
                return;
            _project.Objects.Add(ObjectPresets.GasVent(name, axis, outlet.Value));
        }

        public void AddBreathingValve()
        {
            var axis = AskPoint("\nУкажите ось дыхательного клапана: ");
            if (axis == null)
                return;
            var name = AskString("Наименование", "Дыхательный клапан резервуара ДТ");
            if (name == null)
                return;
            var valve = AskDouble("Отметка дыхательного клапана, м", 4);
            if (valve == null)
                return;
            _project.Objects.Add(ObjectPresets.BreathingValve(name, axis, valve.Value));
        }

        public void AddRods()
        {
            var baseElevation = AskDouble("Отметка установки молниеприёмников (0 — земля), м", 0);
            if (baseElevation == null)
                return;
            while (true)
            {
                var point = AskPoint($"\nУкажите место молниеприёмника Мл{_project.Rods.Count + 1} или Enter для завершения: ");
                if (point == null)
                    return;
                _project.Rods.Add(new LightningRod
                {
                    Name = $"Мл{_project.Rods.Count + 1}",
                    X = point.X,
                    Y = point.Y,
                    BaseElevation = baseElevation.Value,
                    MastHeight = 10,
                });
            }
        }

        /// <summary>Точка в единицах чертежа (МСК) для размещения вида, таблиц и текста.</summary>
        public Point2? AskPlacement(string message)
        {
            var result = _ed.GetPoint(new PromptPointOptions("\n" + message) { AllowNone = true });
            if (result.Status != PromptStatus.OK)
                return null;
            var wcs = result.Value.TransformBy(_ed.CurrentUserCoordinateSystem);
            return new Point2(wcs.X, wcs.Y);
        }

        private Point2? AskPoint(string message)
        {
            var placement = AskPlacement(message.TrimStart('\n'));
            return placement == null ? null : new Point2(placement.X / Upm, placement.Y / Upm);
        }

        private string? AskString(string message, string defaultValue)
        {
            var result = _ed.GetString(new PromptStringOptions($"\n{message} <{defaultValue}>: ") { AllowSpaces = true });
            if (result.Status != PromptStatus.OK)
                return null;
            return string.IsNullOrWhiteSpace(result.StringResult) ? defaultValue : result.StringResult.Trim();
        }

        private double? AskDouble(string message, double defaultValue)
        {
            var options = new PromptDoubleOptions($"\n{message}")
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowNegative = true,
            };
            var result = _ed.GetDouble(options);
            return result.Status == PromptStatus.OK ? result.Value : (double?)null;
        }

        /// <summary>Вершины полилинии в метрах; дуговые сегменты разбиваются на хорды.</summary>
        private List<Point2> ReadContour(Polyline polyline)
        {
            const int arcDivisions = 8;
            var points = new List<Point2>();
            var segments = polyline.Closed ? polyline.NumberOfVertices : polyline.NumberOfVertices - 1;

            for (var i = 0; i < polyline.NumberOfVertices; i++)
            {
                Add(polyline.GetPoint3dAt(i));
                if (i < segments && System.Math.Abs(polyline.GetBulgeAt(i)) > 1e-9)
                    for (var k = 1; k < arcDivisions; k++)
                        Add(polyline.GetPointAtParameter(i + (double)k / arcDivisions));
            }
            return points;

            void Add(Point3d p) => points.Add(new Point2(p.X / Upm, p.Y / Upm));
        }
    }
}
