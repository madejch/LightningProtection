using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LightningProtection.Core.Model;
using LightningProtection.Core.Report;

namespace LightningProtection.Acad.Drawing
{
    /// <summary>Таблицы расчёта и описательная часть.</summary>
    internal static class SheetDrawer
    {
        /// <summary>Ширина описательной части на листе, мм.</summary>
        private const double DescriptionWidthMm = 185;

        /// <summary>Таблицы участка под заголовком с его наименованием.</summary>
        public static void DrawTables(DrawContext ctx, string systemName, IReadOnlyList<ReportTable> tables, Point2 origin)
        {
            var header = ctx.Text(new Point3d(origin.X, origin.Y, 0), $"Расчёт молниезащиты: {systemName}",
                attachment: AttachmentPoint.TopLeft, height: ctx.TextHeight * 1.4);
            var y = origin.Y - header.TextHeight * 2.5;
            foreach (var report in tables)
            {
                var table = DrawTable(ctx, report, new Point3d(origin.X, y, 0));
                y -= table.Height + ctx.TextHeight * 3;
            }
        }

        public static void DrawDescription(DrawContext ctx, string systemName, string text, Point2 origin)
        {
            var width = DescriptionWidthMm * ctx.TextHeight / 2.5;
            var title = ctx.Text(new Point3d(origin.X, origin.Y, 0), $"Описание системы молниезащиты: {systemName}",
                attachment: AttachmentPoint.TopLeft, height: ctx.TextHeight * 1.4, width: width);
            ctx.Text(new Point3d(origin.X, origin.Y - title.TextHeight * 2.5, 0), text,
                attachment: AttachmentPoint.TopLeft, width: width);
        }

        private static Table DrawTable(DrawContext ctx, ReportTable report, Point3d position)
        {
            var th = ctx.TextHeight;
            var columns = report.Headers.Count;
            var table = new Table
            {
                TableStyle = ctx.Db.Tablestyle,
                Position = position,
            };
            table.SetSize(report.Rows.Count + 2, columns);
            ctx.Add(table, DrawContext.LayerText);

            try
            {
                table.MergeCells(CellRange.Create(table, 0, 0, 0, columns - 1));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // В стиле таблицы строка заголовка уже объединена.
            }
            SetCell(table, 0, 0, report.Title, th * 1.2, ctx.TextStyleId);

            for (var c = 0; c < columns; c++)
                SetCell(table, 1, c, report.Headers[c], th, ctx.TextStyleId);
            for (var r = 0; r < report.Rows.Count; r++)
                for (var c = 0; c < columns; c++)
                    SetCell(table, r + 2, c, report.Rows[r][c], th, ctx.TextStyleId);

            for (var c = 0; c < columns; c++)
            {
                var chars = report.Rows.Select(row => row[c].Length).DefaultIfEmpty(0).Max();
                chars = System.Math.Max(chars, report.Headers[c].Length);
                table.Columns[c].Width = (System.Math.Min(chars, 48) * 0.75 + 2) * th;
            }
            for (var r = 0; r < table.Rows.Count; r++)
                table.Rows[r].Height = th * 2.2;

            table.GenerateLayout();
            return table;
        }

        private static void SetCell(Table table, int row, int column, string text, double height, ObjectId textStyle)
        {
            var cell = table.Cells[row, column];
            cell.TextStyleId = textStyle;
            cell.TextString = DrawContext.Escape(text);
            cell.TextHeight = height;
            cell.Alignment = CellAlignment.MiddleCenter;
        }
    }
}
