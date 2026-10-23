using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LightningProtection.Acad.UI
{
    /// <summary>
    /// Вкладка «Молниезащита» на ленте. Создаётся при загрузке плагина и пересоздаётся,
    /// когда AutoCAD перестраивает ленту (смена рабочего пространства, загрузка CUI).
    /// </summary>
    internal static class RibbonBuilder
    {
        private const string TabId = "LPROT_TAB";

        public static void Initialize()
        {
            if (ComponentManager.Ribbon != null)
                CreateTab();
            else
                ComponentManager.ItemInitialized += OnItemInitialized;

            AcApp.SystemVariableChanged += (sender, e) =>
            {
                if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase))
                    CreateTab();
            };
        }

        private static void OnItemInitialized(object? sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null)
                return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            CreateTab();
        }

        private static void CreateTab()
        {
            try
            {
                CreateTabCore();
            }
            catch (Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\nВкладка «Молниезащита» не создана: {ex.Message}\n");
            }
        }

        private static void CreateTabCore()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
                return;
            var existing = ribbon.Tabs.FirstOrDefault(t => t.Id == TabId);
            if (existing != null && existing.Panels.Count > 0)
                return;
            // Пустая вкладка могла остаться от неудачной загрузки — пересоздаём.
            if (existing != null)
                ribbon.Tabs.Remove(existing);

            // Вкладка добавляется на ленту только полностью собранной.
            var tab = new RibbonTab { Title = "Молниезащита", Id = TabId };

            var main = new RibbonPanelSource { Title = "Расчёт" };
            main.Items.Add(Button("Расчёт\nмолниезащиты", "LPROT", "Исходные данные, расчёт зон, подбор высоты и отрисовка",
                Color.FromRgb(0xE8, 0x8A, 0x00), true));
            tab.Panels.Add(new RibbonPanel { Source = main });

            var drawing = new RibbonPanelSource { Title = "Чертёж" };
            drawing.Items.Add(Button("Перерисовать", "LPROTDRAW", "Перерисовать план, вид сбоку, таблицы и текст по данным чертежа",
                Color.FromRgb(0x1E, 0x6F, 0xC8), true));
            drawing.Items.Add(Button("Протокол", "LPROTREPORT", "Вывести протокол расчёта в командную строку",
                Color.FromRgb(0x2E, 0x8B, 0x3E), true));
            drawing.Items.Add(Button("Очистить", "LPROTCLEAR", "Удалить всё, что нарисовал плагин",
                Color.FromRgb(0xC0, 0x39, 0x2B), true));
            tab.Panels.Add(new RibbonPanel { Source = drawing });

            ribbon.Tabs.Add(tab);
        }

        private static RibbonButton Button(string text, string command, string tooltip, Color color, bool large)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = Icon(32, color),
                Image = Icon(16, color),
                ToolTip = tooltip,
                CommandParameter = "\x03\x03_" + command + " ",
                CommandHandler = new CommandHandler(),
            };
        }

        /// <summary>Значок: молния на цветной плашке.</summary>
        private static ImageSource Icon(int size, Color background)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(background), null, new Rect(0, 0, size, size), size / 6.0, size / 6.0);
                // Geometry.Parse возвращает замороженную геометрию — масштабируем через контекст.
                dc.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
                dc.DrawGeometry(Brushes.White, null, Geometry.Parse("M 18,2 L 7,18 H 15 L 12,30 L 25,12 H 17 Z"));
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private sealed class CommandHandler : ICommand
        {
#pragma warning disable CS0067 // событие требуется интерфейсом
            public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter)
            {
                if (parameter is RibbonButton button && button.CommandParameter is string command)
                    AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute(command, true, false, true);
            }
        }
    }
}
