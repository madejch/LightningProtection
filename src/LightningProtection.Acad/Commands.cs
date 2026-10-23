using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using LightningProtection.Acad.Drawing;
using LightningProtection.Acad.UI;
using LightningProtection.Core.Calculation;
using LightningProtection.Core.Model;
using LightningProtection.Core.Report;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LightningProtection.Acad
{
    public sealed class Commands
    {
        /// <summary>Главное окно: исходные данные, расчёт, подбор высоты, отрисовка.</summary>
        [CommandMethod("LPROT", CommandFlags.Modal)]
        public void Main()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            var project = ProjectStore.Load(doc.Database);

            while (true)
            {
                var working = ProjectSerializer.Clone(project);
                var window = new MainWindow(working);
                AcApp.ShowModalWindow(window);
                if (window.Action == DialogAction.Cancel)
                    return;

                project = working;
                var picker = new Picker(doc.Editor, doc.Database, project);
                switch (window.Action)
                {
                    case DialogAction.AddBuilding:
                        picker.AddBuilding();
                        break;
                    case DialogAction.AddGasVent:
                        picker.AddGasVent();
                        break;
                    case DialogAction.AddBreathingValve:
                        picker.AddBreathingValve();
                        break;
                    case DialogAction.AddRods:
                        picker.AddRods();
                        break;
                    case DialogAction.Draw:
                        if (window.ResetPlacement)
                        {
                            project.Drawing.SideViewInsertPoint = null;
                            project.Drawing.TableInsertPoint = null;
                            project.Drawing.DescriptionInsertPoint = null;
                        }
                        Draw(doc, project);
                        ProjectStore.Save(doc.Database, project);
                        return;
                    case DialogAction.Save:
                        ProjectStore.Save(doc.Database, project);
                        return;
                }
                ProjectStore.Save(doc.Database, project);
            }
        }

        /// <summary>Перерисовать зоны, вид сбоку, таблицы и текст по сохранённому проекту.</summary>
        [CommandMethod("LPROTDRAW", CommandFlags.Modal)]
        public void Redraw()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            var project = ProjectStore.Load(doc.Database);
            Draw(doc, project);
            ProjectStore.Save(doc.Database, project);
        }

        /// <summary>Удалить всё, что нарисовал плагин.</summary>
        [CommandMethod("LPROTCLEAR", CommandFlags.Modal)]
        public void Clear()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            var count = DrawContext.EraseOwned(doc.Database);
            doc.Editor.WriteMessage($"\nУдалено объектов молниезащиты: {count}.");
        }

        /// <summary>Протокол расчёта в командную строку.</summary>
        [CommandMethod("LPROTREPORT", CommandFlags.Modal)]
        public void Report()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            var project = ProjectStore.Load(doc.Database);
            var result = ProtectionCalculator.Calculate(project);
            doc.Editor.WriteMessage("\n" + ReportBuilder.BuildText(project, result).Replace("\r\n", "\n"));
        }

        private static void Draw(Document doc, ProtectionProject project)
        {
            var ed = doc.Editor;
            var result = ProtectionCalculator.Calculate(project);
            foreach (var error in result.Errors)
                ed.WriteMessage($"\nОшибка: {error}");
            if (result.Errors.Count > 0)
                return;

            var picker = new Picker(ed, doc.Database, project);
            var settings = project.Drawing;
            settings.SideViewInsertPoint ??= picker.AskPlacement("Точка вставки вида сбоку (левый край, отметка ±0,000): ");
            settings.TableInsertPoint ??= picker.AskPlacement("Точка вставки таблиц расчёта (левый верхний угол): ");
            settings.DescriptionInsertPoint ??= picker.AskPlacement("Точка вставки описательной части (левый верхний угол): ");

            var planScale = DrawingScales.PlanScale(project, result);
            var sideScale = DrawingScales.SideViewScale(project, planScale);
            using (var ctx = new DrawContext(doc.Database, settings, planScale))
            {
                ctx.EraseOwned();
                PlanDrawer.Draw(ctx, project, result);
                if (settings.SideViewInsertPoint != null)
                    SideViewDrawer.Draw(ctx, project, result, settings.SideViewInsertPoint, sideScale);
                if (settings.TableInsertPoint != null)
                    SheetDrawer.DrawTables(ctx, ReportBuilder.BuildTables(project, result), settings.TableInsertPoint);
                if (settings.DescriptionInsertPoint != null)
                    SheetDrawer.DrawDescription(ctx, DescriptionBuilder.Build(project, result, LoadTemplate()),
                        settings.DescriptionInsertPoint);
                ctx.Commit();
            }

            ed.WriteMessage($"\nМасштаб плана 1:{planScale:0}, вида сбоку 1:{sideScale:0}" +
                            (settings.Scale > 1 ? "." : " (масштаб плана подобран по размерам)."));
            ed.WriteMessage(result.AllProtected
                ? "\nМолниезащита отрисована. Все объекты защищены."
                : "\nМолниезащита отрисована. ЕСТЬ НЕЗАЩИЩЁННЫЕ ОБЪЕКТЫ — см. таблицу «Защищаемые объекты».");
            ed.Regen();
        }

        /// <summary>Шаблон description.txt рядом со сборкой; если файла нет — встроенный.</summary>
        private static string? LoadTemplate()
        {
            var folder = Path.GetDirectoryName(typeof(Commands).Assembly.Location) ?? "";
            var path = Path.Combine(folder, "description.txt");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
