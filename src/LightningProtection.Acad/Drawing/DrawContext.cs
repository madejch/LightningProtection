using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LightningProtection.Core.Calculation;
using LightningProtection.Core.Model;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LightningProtection.Acad.Drawing
{
    /// <summary>
    /// Транзакция, пространство модели, слои, стили и масштаб для отрисовки молниезащиты.
    /// Чертёж ведётся в натуральную величину; высота текста, стрелок и размеров
    /// увеличивается под масштаб плана, чтобы на печати получить заданную высоту в мм.
    /// </summary>
    internal sealed class DrawContext : IDisposable
    {
        public const string AppName = "LPROT";

        public const string LayerRods = "МЗ_Молниеприемники";
        public const string LayerZones = "МЗ_Зоны защиты";
        public const string LayerSpaces = "МЗ_Защищаемое пространство";
        public const string LayerBuildings = "МЗ_Здания";
        public const string LayerSideView = "МЗ_Вид сбоку";
        public const string LayerDimensions = "МЗ_Размеры";
        public const string LayerText = "МЗ_Текст";

        private const string TextStyleName = "МЗ_GOST";
        private const string DimStyleName = "МЗ_Размеры";

        /// <summary>Цвета уровней защиты: красный, зелёный, синий, пурпурный, оранжевый, голубой.</summary>
        private static readonly short[] LevelColors = { 1, 3, 5, 6, 30, 4 };

        private readonly DrawingSettings _settings;
        private ObjectId _dashed = ObjectId.Null;

        public DrawContext(Database db, DrawingSettings settings, double planScale)
        {
            Db = db;
            _settings = settings;
            PlanScale = planScale;
            Tr = db.TransactionManager.StartTransaction();
            var blockTable = (BlockTable)Tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            Space = (BlockTableRecord)Tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            EnsureRegApp();
            EnsureLayer(LayerRods, 1, LineWeight.LineWeight035);
            EnsureLayer(LayerZones, 1, LineWeight.ByLineWeightDefault);
            EnsureLayer(LayerSpaces, 2, LineWeight.ByLineWeightDefault);
            EnsureLayer(LayerBuildings, 7, LineWeight.LineWeight050);
            EnsureLayer(LayerSideView, 7, LineWeight.ByLineWeightDefault);
            EnsureLayer(LayerDimensions, 7, LineWeight.LineWeight018);
            EnsureLayer(LayerText, 7, LineWeight.ByLineWeightDefault);
            LoadDashed();
            TextStyleId = EnsureTextStyle();
            DimStyleId = EnsureDimStyle();
        }

        /// <summary>Код рисуемого расчётного участка — записывается в XData каждого объекта.</summary>
        public string? SystemId { get; set; }

        public Database Db { get; }
        public Transaction Tr { get; }
        public BlockTableRecord Space { get; }
        public ObjectId TextStyleId { get; }
        public ObjectId DimStyleId { get; }

        /// <summary>Знаменатель масштаба плана, под который подбирается высота текста.</summary>
        public double PlanScale { get; }

        public double Upm => _settings.UnitsPerMeter;

        /// <summary>Высота текста в единицах чертежа.</summary>
        public double TextHeight => _settings.TextHeightMm * PlanScale * Upm / 1000;

        /// <summary>Цвет уровня защиты — одинаковый на плане и на виде сбоку.</summary>
        public static short LevelColor(CalculationResult result, ZoneLevel level)
        {
            var index = result.Sections.Select(s => s.Level).Distinct().ToList().IndexOf(level);
            return LevelColors[Math.Max(0, index) % LevelColors.Length];
        }

        public Point3d ModelPoint(double xMeters, double yMeters) => new Point3d(xMeters * Upm, yMeters * Upm, 0);

        public T Add<T>(T entity, string layer, short? color = null, bool dashed = false) where T : Entity
        {
            entity.Layer = layer;
            if (color.HasValue)
                entity.ColorIndex = color.Value;
            if (dashed && !_dashed.IsNull)
            {
                entity.LinetypeId = _dashed;
                // Штрихи acadiso.lin заданы в мм листа.
                entity.LinetypeScale = PlanScale * Upm / 1000;
            }
            entity.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "lightning-protection"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, SystemId ?? ""));
            Space.AppendEntity(entity);
            Tr.AddNewlyCreatedDBObject(entity, true);
            Capture?.Add(entity.ObjectId);
            return entity;
        }

        /// <summary>
        /// Если задан — сюда собираются все создаваемые объекты (для предпросмотра и переноса
        /// вида сбоку, таблиц и описания при размещении).
        /// </summary>
        public List<ObjectId>? Capture { get; set; }

        /// <summary>Полилиния по точкам в единицах чертежа.</summary>
        public Polyline Polyline(IReadOnlyList<Point2d> points, bool closed, string layer, short? color = null, bool dashed = false)
        {
            var polyline = new Polyline();
            for (var i = 0; i < points.Count; i++)
                polyline.AddVertexAt(i, points[i], 0, 0, 0);
            polyline.Closed = closed;
            return Add(polyline, layer, color, dashed);
        }

        public MText Text(Point3d location, string contents, string layer = LayerText, short? color = null,
            AttachmentPoint attachment = AttachmentPoint.BottomLeft, double? height = null, double width = 0)
        {
            var text = new MText
            {
                Location = location,
                Contents = Escape(contents),
                TextHeight = height ?? TextHeight,
                TextStyleId = TextStyleId,
                Attachment = attachment,
                Width = width,
            };
            return Add(text, layer, color);
        }

        public void Line(Point3d from, Point3d to, string layer, short? color = null, bool dashed = false)
            => Add(new Line(from, to), layer, color, dashed);

        /// <summary>
        /// Выноска с полкой: первая строка над полкой, остальные под ней (ГОСТ 2.316).
        /// Текст располагается справа от точки <paramref name="landing"/>.
        /// </summary>
        public MLeader Leader(Point3d arrow, Point3d landing, string contents, string layer, short? color = null)
        {
            var text = new MText
            {
                Contents = Escape(contents),
                TextHeight = TextHeight,
                TextStyleId = TextStyleId,
                Location = landing,
                Attachment = AttachmentPoint.MiddleLeft,
            };
            var leader = new MLeader();
            leader.SetDatabaseDefaults(Db);
            leader.ContentType = ContentType.MTextContent;
            leader.TextStyleId = TextStyleId;
            leader.TextHeight = TextHeight;
            leader.MText = text;
            leader.ArrowSize = TextHeight * 0.8;
            leader.EnableLanding = true;
            leader.EnableDogleg = true;
            leader.DoglegLength = TextHeight * 0.6;
            leader.LandingGap = TextHeight * 0.25;
            leader.SetTextAttachmentType(TextAttachmentType.AttachmentBottomOfTopLine, LeaderDirectionType.LeftLeader);
            leader.SetTextAttachmentType(TextAttachmentType.AttachmentBottomOfTopLine, LeaderDirectionType.RightLeader);
            leader.AddLeaderLine(arrow);
            if (color.HasValue)
                leader.TextColor = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, color.Value);
            return Add(leader, layer, color);
        }

        /// <summary>Линейный размер. <paramref name="lengthFactor"/> — множитель измеренной длины (DIMLFAC).</summary>
        public RotatedDimension LinearDim(Point3d p1, Point3d p2, Point3d dimLine, double rotation, string text = "",
            double lengthFactor = 1)
        {
            var dimension = new RotatedDimension(rotation, p1, p2, dimLine, text, DimStyleId);
            if (Math.Abs(lengthFactor - 1) > 1e-9)
                dimension.Dimlfac = lengthFactor;
            Add(dimension, LayerDimensions);
            // Блок размера строится сразу — иначе в предпросмотре размеров не видно.
            dimension.RecomputeDimensionBlock(true);
            return dimension;
        }

        public AlignedDimension AlignedDim(Point3d p1, Point3d p2, Point3d dimLine, string text = "")
        {
            var dimension = Add(new AlignedDimension(p1, p2, dimLine, text, DimStyleId), LayerDimensions);
            dimension.RecomputeDimensionBlock(true);
            return dimension;
        }

        /// <summary>
        /// Удаляет прежнюю отрисовку текущего участка <see cref="SystemId"/>, а также объекты
        /// старых версий плагина, не привязанные к участку. Отрисовку других участков не трогает.
        /// </summary>
        public int EraseOwned() => EraseOwned(Tr, Space, SystemId);

        /// <summary>Удаление всей отрисовки без создания слоёв и стилей — для команды очистки.</summary>
        public static int EraseOwned(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var space = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            var count = EraseOwned(tr, space, null);
            tr.Commit();
            return count;
        }

        private static int EraseOwned(Transaction tr, BlockTableRecord space, string? systemId)
        {
            var count = 0;
            foreach (ObjectId id in space)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                using var xdata = entity?.GetXDataForApplication(AppName);
                if (xdata == null)
                    continue;
                if (systemId != null)
                {
                    var owner = OwnerOf(xdata);
                    if (owner != null && owner != systemId)
                        continue;
                }
                entity!.UpgradeOpen();
                entity.Erase();
                count++;
            }
            return count;
        }

        /// <summary>Код участка из XData: вторая строка после метки плагина (у старых версий её нет).</summary>
        private static string? OwnerOf(ResultBuffer xdata)
        {
            var strings = xdata.AsArray()
                .Where(v => v.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                .Select(v => v.Value as string)
                .ToList();
            return strings.Count > 1 && !string.IsNullOrEmpty(strings[1]) ? strings[1] : null;
        }

        public void Commit() => Tr.Commit();

        public void Dispose() => Tr.Dispose();

        /// <summary>Текст для MText и ячеек таблиц: экранирование служебных символов и переносы строк.</summary>
        public static string Escape(string text)
            => text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
                .Replace("\r\n", "\n").Replace("\n", "\\P");

        private void EnsureRegApp()
        {
            var table = (RegAppTable)Tr.GetObject(Db.RegAppTableId, OpenMode.ForRead);
            if (table.Has(AppName))
                return;
            table.UpgradeOpen();
            var record = new RegAppTableRecord { Name = AppName };
            table.Add(record);
            Tr.AddNewlyCreatedDBObject(record, true);
        }

        private void EnsureLayer(string name, short color, LineWeight lineWeight)
        {
            var table = (LayerTable)Tr.GetObject(Db.LayerTableId, OpenMode.ForRead);
            if (table.Has(name))
                return;
            table.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, color),
                LineWeight = lineWeight,
            };
            table.Add(layer);
            Tr.AddNewlyCreatedDBObject(layer, true);
        }

        /// <summary>Текстовый стиль на шрифте GOST Common (TrueType).</summary>
        private ObjectId EnsureTextStyle()
        {
            var table = (TextStyleTable)Tr.GetObject(Db.TextStyleTableId, OpenMode.ForRead);
            TextStyleTableRecord style;
            if (table.Has(TextStyleName))
            {
                style = (TextStyleTableRecord)Tr.GetObject(table[TextStyleName], OpenMode.ForWrite);
            }
            else
            {
                table.UpgradeOpen();
                style = new TextStyleTableRecord { Name = TextStyleName };
                table.Add(style);
                Tr.AddNewlyCreatedDBObject(style, true);
            }
            // Наклонный вариант: в прямом GOST Common.ttf на местах «, » и ± нарисованы ¼ и ½.
            style.FileName = FontInstaller.ItalicFontFile;
            style.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(FontInstaller.FontFace, false, true, 204, 34);
            style.XScale = 1;
            style.TextSize = 0;
            return style.ObjectId;
        }

        /// <summary>Размерный стиль: засечки, текст над размерной линией, GOST Common.</summary>
        private ObjectId EnsureDimStyle()
        {
            var tick = ArrowBlock("_ARCHTICK");
            var table = (DimStyleTable)Tr.GetObject(Db.DimStyleTableId, OpenMode.ForRead);
            DimStyleTableRecord style;
            if (table.Has(DimStyleName))
            {
                style = (DimStyleTableRecord)Tr.GetObject(table[DimStyleName], OpenMode.ForWrite);
            }
            else
            {
                table.UpgradeOpen();
                style = new DimStyleTableRecord { Name = DimStyleName };
                table.Add(style);
                Tr.AddNewlyCreatedDBObject(style, true);
            }

            style.Dimtxsty = TextStyleId;
            style.Dimtxt = _settings.TextHeightMm;
            style.Dimscale = PlanScale * Upm / 1000;
            if (!tick.IsNull)
            {
                style.Dimsah = false;
                style.Dimblk = tick;
            }
            style.Dimasz = 2;
            style.Dimexe = 2;
            style.Dimexo = 0;
            style.Dimdle = 2;
            style.Dimgap = 1;
            style.Dimtad = 1;
            style.Dimtih = false;
            style.Dimtoh = false;
            style.Dimtmove = 2;
            style.Dimdec = Upm >= 1000 ? 0 : 2;
            style.Dimdsep = ',';
            style.Dimzin = 8;
            return style.ObjectId;
        }

        /// <summary>Блок стандартной стрелки AutoCAD; создаётся через системную переменную DIMBLK.</summary>
        private ObjectId ArrowBlock(string name)
        {
            var blockTable = (BlockTable)Tr.GetObject(Db.BlockTableId, OpenMode.ForRead);
            if (!blockTable.Has(name))
            {
                var previous = AcApp.GetSystemVariable("DIMBLK") as string;
                try
                {
                    AcApp.SetSystemVariable("DIMBLK", name);
                }
                finally
                {
                    AcApp.SetSystemVariable("DIMBLK", string.IsNullOrEmpty(previous) ? "." : previous);
                }
            }
            return blockTable.Has(name) ? blockTable[name] : ObjectId.Null;
        }

        private void LoadDashed()
        {
            var table = (LinetypeTable)Tr.GetObject(Db.LinetypeTableId, OpenMode.ForRead);
            if (!table.Has("DASHED"))
            {
                try
                {
                    Db.LoadLineTypeFile("DASHED", "acadiso.lin");
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    return;
                }
            }
            if (table.Has("DASHED"))
                _dashed = table["DASHED"];
        }
    }
}
