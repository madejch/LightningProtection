using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
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
        /// <summary>Главное окно: участки, исходные данные, расчёт, подбор высоты, отрисовка.</summary>
        [CommandMethod("LPROT", CommandFlags.Modal)]
        public void Main()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            var project = ProjectStore.Load(doc.Database);
            string? selectedId = null;

            while (true)
            {
                var working = ProjectSerializer.Clone(project);
                var window = new MainWindow(working, selectedId);
                AcApp.ShowModalWindow(window);
                if (window.Action == DialogAction.Cancel)
                    return;

                project = working;
                var system = window.SelectedSystem;
                selectedId = system.Id;
                var picker = new Picker(doc.Editor, doc.Database, project, system);
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
                    case DialogAction.DrawAll:
                        var targets = window.Action == DialogAction.DrawAll ? project.Systems : new List<ProtectionSystem> { system };
                        if (window.ResetPlacement)
                        {
                            foreach (var target in targets)
                            {
                                target.SideViewInsertPoint = null;
                                target.TableInsertPoint = null;
                                target.DescriptionInsertPoint = null;
                            }
                        }
                        Draw(doc, project, targets);
                        ProjectStore.Save(doc.Database, project);
                        return;
                    case DialogAction.Save:
                        ProjectStore.Save(doc.Database, project);
                        return;
                }
                ProjectStore.Save(doc.Database, project);
            }
        }

        /// <summary>Перерисовать все участки по сохранённому проекту.</summary>
        [CommandMethod("LPROTDRAW", CommandFlags.Modal)]
        public void Redraw()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            var project = ProjectStore.Load(doc.Database);
            Draw(doc, project, project.Systems);
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

        /// <summary>Протокол расчёта всех участков в командную строку.</summary>
        [CommandMethod("LPROTREPORT", CommandFlags.Modal)]
        public void Report()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            var project = ProjectStore.Load(doc.Database);
            foreach (var system in project.Systems)
            {
                var systemProject = project.ForSystem(system);
                var result = ProtectionCalculator.Calculate(systemProject);
                doc.Editor.WriteMessage($"\n===== {system.Name} =====\n" +
                                        ReportBuilder.BuildText(systemProject, result).Replace("\r\n", "\n"));
            }
        }

        /// <summary>
        /// Отрисовка участков. Каждый участок считается отдельно и рисуется со своими видом сбоку,
        /// таблицами и описанием; прежняя отрисовка этих участков удаляется, остальных — сохраняется.
        /// Масштаб плана общий для чертежа.
        /// </summary>
        private static void Draw(Document doc, ProtectionProject project, IReadOnlyList<ProtectionSystem> systems)
        {
            var ed = doc.Editor;
            var calculations = new List<(ProtectionSystem System, ProtectionProject Project, CalculationResult Result)>();
            foreach (var system in systems)
            {
                var systemProject = project.ForSystem(system);
                var result = ProtectionCalculator.Calculate(systemProject);
                foreach (var error in result.Errors)
                    ed.WriteMessage($"\n{system.Name}: {error}");
                if (result.Errors.Count == 0)
                    calculations.Add((system, systemProject, result));
            }
            if (calculations.Count == 0)
                return;

            // Масштаб плана — по всем участкам, чтобы текст и размеры на листе были одной высоты.
            var planScale = project.Systems
                .Select(s => (Project: project.ForSystem(s), System: s))
                .Where(x => x.System.Objects.Count > 0 && x.System.Rods.Count > 0)
                .Select(x => DrawingScales.PlanScale(x.Project, ProtectionCalculator.Calculate(x.Project)))
                .DefaultIfEmpty(DrawingScales.Standard[0])
                .Max();
            var sideScale = DrawingScales.SideViewScale(project, planScale);

            // Неразмещённые вид сбоку, таблицы и описание ставятся указанием с предпросмотром.
            var template = LoadTemplate();
            using (var ctx = new DrawContext(doc.Database, project.Drawing, planScale))
            {
                foreach (var (system, systemProject, result) in calculations)
                {
                    ctx.SystemId = system.Id;
                    ctx.EraseOwned();
                    PlanDrawer.Draw(ctx, systemProject, result);
                    new SheetPlacement(ctx, ed, system, systemProject, result, sideScale, template).Draw();
                }
                ctx.Commit();
            }

            ed.WriteMessage($"\nМасштаб плана 1:{planScale:0}, вида сбоку 1:{sideScale:0}" +
                            (project.Drawing.Scale > 1 ? "." : " (масштаб плана подобран по размерам)."));
            foreach (var (system, _, result) in calculations)
                ed.WriteMessage(result.AllProtected
                    ? $"\n{system.Name}: отрисовано, все объекты защищены."
                    : $"\n{system.Name}: отрисовано. ЕСТЬ НЕЗАЩИЩЁННЫЕ ОБЪЕКТЫ — см. таблицу «Защищаемые объекты».");
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
