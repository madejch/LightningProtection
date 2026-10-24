using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using LightningProtection.Core.Calculation;
using LightningProtection.Core.Model;
using LightningProtection.Core.Report;

namespace LightningProtection.Acad.UI
{
    public enum DialogAction
    {
        Cancel,
        Save,
        AddBuilding,
        AddGasVent,
        AddBreathingValve,
        AddRods,
        /// <summary>Отрисовать выбранный участок.</summary>
        Draw,
        /// <summary>Отрисовать все участки.</summary>
        DrawAll,
    }

    public sealed class Choice
    {
        public Choice(object value, string title)
        {
            Value = value;
            Title = title;
        }

        public object Value { get; }
        public string Title { get; }
    }

    /// <summary>
    /// Окно работает с копией проекта. Действия, требующие указания на чертеже, закрывают окно
    /// с кодом <see cref="Action"/> — команда выполняет указание в выбранном участке
    /// <see cref="SelectedSystem"/> и открывает окно снова.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ProtectionProject _project;
        private ProtectionSystem _system;
        private bool _switching;

        public MainWindow(ProtectionProject project, string? selectedSystemId = null)
        {
            InitializeComponent();
            Language = XmlLanguage.GetLanguage("ru-RU");
            _project = project;

            NormBox.ItemsSource = new[]
            {
                new Choice(NormKind.So153, NormKind.So153.Title()),
                new Choice(NormKind.Rd87, NormKind.Rd87.Title()),
            };
            RdZoneColumn.ItemsSource = new[] { new Choice(RdZone.A, "А"), new Choice(RdZone.B, "Б") };
            ShapeColumn.ItemsSource = new[]
            {
                new Choice(SpaceShape.Cylinder, "Цилиндр"),
                new Choice(SpaceShape.Hemisphere, "Полусфера"),
            };
            SoColumn.ItemsSource = new[]
            {
                new Choice(SoReliability.P090, SoReliability.P090.Title()),
                new Choice(SoReliability.P099, SoReliability.P099.Title()),
                new Choice(SoReliability.P0999, SoReliability.P0999.Title()),
            };

            NormBox.SelectedValue = project.Norm;
            TdBox.Text = Num(project.ThunderstormHours);
            StepBox.Text = Num(project.HeightStep);
            MinMastBox.Text = Num(project.MinMastHeight);
            MaxHeightBox.Text = Num(project.MaxRodHeight);
            UpmBox.Text = Num(project.Drawing.UnitsPerMeter);
            ScaleBox.Text = Num(project.Drawing.Scale);
            SideScaleBox.Text = Num(project.Drawing.SideViewScale);
            TextHeightBox.Text = Num(project.Drawing.TextHeightMm);

            project.EnsureSystems();
            _system = project.Systems.FirstOrDefault(s => s.Id == selectedSystemId) ?? project.Systems[0];
            SystemBox.ItemsSource = project.Systems;
            MoveTargetBox.ItemsSource = project.Systems;
            ShowSystem(_system);
        }

        public DialogAction Action { get; private set; } = DialogAction.Cancel;

        /// <summary>Участок, с которым работает окно: в него добавляются объекты и молниеприёмники.</summary>
        public ProtectionSystem SelectedSystem => _system;

        public bool ResetPlacement => ResetPlacementBox.IsChecked == true;

        private void AddBuilding_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.AddBuilding);
        private void AddGasVent_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.AddGasVent);
        private void AddValve_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.AddBreathingValve);
        private void AddRods_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.AddRods);
        private void Draw_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.Draw);
        private void DrawAll_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.DrawAll);

        private void ShowSystem(ProtectionSystem system)
        {
            _switching = true;
            _system = system;
            SystemBox.SelectedItem = system;
            SystemNameBox.Text = system.Name;
            AngleBox.Text = Num(system.SideViewAngleDeg);
            ObjectsGrid.ItemsSource = system.Objects;
            RodsGrid.ItemsSource = system.Rods;
            MoveTargetBox.SelectedItem = _project.Systems.FirstOrDefault(s => s != system);
            ResultBox.Text = "";
            _switching = false;
        }

        private void SystemBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_switching || !(SystemBox.SelectedItem is ProtectionSystem next) || next == _system)
                return;
            if (!CommitSystem())
            {
                ShowSystem(_system);
                return;
            }
            ShowSystem(next);
        }

        private void AddSystem_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitSystem())
                return;
            var system = _project.AddSystem();
            RefreshSystemLists();
            ShowSystem(system);
            SystemNameBox.Focus();
            SystemNameBox.SelectAll();
        }

        private void DeleteSystem_Click(object sender, RoutedEventArgs e)
        {
            if (_project.Systems.Count < 2)
            {
                MessageBox.Show(this, "В проекте должен остаться хотя бы один участок.", Title);
                return;
            }
            var count = _system.Objects.Count + _system.Rods.Count;
            if (count > 0 && MessageBox.Show(this,
                    $"Удалить участок «{_system.Name}» вместе с его объектами и молниеприёмниками ({count} шт.)?\n" +
                    "Нарисованное на чертеже удалится при отрисовке всех участков или командой «Очистить».",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _project.Systems.Remove(_system);
            RefreshSystemLists();
            ShowSystem(_project.Systems[0]);
        }

        private void SystemNameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var name = SystemNameBox.Text.Trim();
            if (name.Length == 0 || name == _system.Name)
                return;
            _system.Name = name;
            RefreshSystemLists();
        }

        /// <summary>Переносит выбранный объект или молниеприёмник в другой участок.</summary>
        private void MoveSelected_Click(object sender, RoutedEventArgs e)
        {
            ObjectsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            RodsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (!(MoveTargetBox.SelectedItem is ProtectionSystem target) || target == _system)
            {
                MessageBox.Show(this, "Выберите другой участок, в который нужно перенести.", Title);
                return;
            }
            if (ObjectsGrid.SelectedItem is ProtectedObject obj)
            {
                _system.Objects.Remove(obj);
                target.Objects.Add(obj);
                ObjectsGrid.Items.Refresh();
            }
            else if (RodsGrid.SelectedItem is LightningRod rod)
            {
                _system.Rods.Remove(rod);
                target.Rods.Add(rod);
                RodsGrid.Items.Refresh();
            }
            else
            {
                MessageBox.Show(this, "Выделите строку в таблице объектов или молниеприёмников.", Title);
            }
        }

        private void RefreshSystemLists()
        {
            _switching = true;
            SystemBox.Items.Refresh();
            MoveTargetBox.Items.Refresh();
            SystemBox.SelectedItem = _system;
            _switching = false;
        }
        private void Save_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.Save);

        private void DeleteObject_Click(object sender, RoutedEventArgs e)
        {
            if (ObjectsGrid.SelectedItem is ProtectedObject obj)
            {
                _system.Objects.Remove(obj);
                ObjectsGrid.Items.Refresh();
            }
        }

        private void DeleteRod_Click(object sender, RoutedEventArgs e)
        {
            if (RodsGrid.SelectedItem is LightningRod rod)
            {
                _system.Rods.Remove(rod);
                RodsGrid.Items.Refresh();
            }
        }

        private void Calculate_Click(object sender, RoutedEventArgs e)
        {
            if (!Commit())
                return;
            var systemProject = _project.ForSystem(_system);
            var result = ProtectionCalculator.Calculate(systemProject);
            ResultBox.Text = $"Участок: {_system.Name}\n" + ReportBuilder.BuildText(systemProject, result);
        }

        private void Optimize_Click(object sender, RoutedEventArgs e)
        {
            if (!Commit())
                return;
            var systemProject = _project.ForSystem(_system);
            var optimization = HeightOptimizer.Optimize(systemProject);
            RodsGrid.Items.Refresh();
            ResultBox.Text = $"Участок: {_system.Name}\n" + optimization.Message + "\n\n" +
                             ReportBuilder.BuildText(systemProject, optimization.Calculation);
        }

        private void Finish(DialogAction action)
        {
            if (!Commit())
                return;
            Action = action;
            DialogResult = true;
        }

        /// <summary>Завершает редактирование таблиц и переносит поля участка в участок.</summary>
        private bool CommitSystem()
        {
            ObjectsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            RodsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (!TryParse(AngleBox, "угол вида", out var angle))
                return false;
            _system.SideViewAngleDeg = angle;
            var name = SystemNameBox.Text.Trim();
            if (name.Length > 0)
                _system.Name = name;
            return true;
        }

        /// <summary>Завершает редактирование и переносит общие поля и поля участка в проект.</summary>
        private bool Commit()
        {
            if (!CommitSystem())
                return false;

            if (!TryParse(TdBox, "Td", out var td) || !TryParse(StepBox, "шаг подбора", out var step) ||
                !TryParse(MinMastBox, "минимальная мачта", out var minMast) || !TryParse(MaxHeightBox, "наибольшая высота", out var maxHeight) ||
                !TryParse(UpmBox, "единиц в метре", out var upm) || !TryParse(ScaleBox, "масштаб плана", out var scale) ||
                !TryParse(SideScaleBox, "масштаб вида сбоку", out var sideScale) ||
                !TryParse(TextHeightBox, "высота текста", out var textHeight))
                return false;

            if (step <= 0 || upm <= 0 || scale < 0 || sideScale < 0 || textHeight <= 0)
            {
                MessageBox.Show(this, "Шаг подбора, единицы и высота текста должны быть больше нуля, масштабы — не меньше нуля.", Title);
                return false;
            }

            _project.Norm = (NormKind)(NormBox.SelectedValue ?? NormKind.So153);
            _project.ThunderstormHours = td;
            _project.HeightStep = step;
            _project.MinMastHeight = minMast;
            _project.MaxRodHeight = maxHeight;
            _project.Drawing.UnitsPerMeter = upm;
            _project.Drawing.Scale = scale;
            _project.Drawing.SideViewScale = sideScale;
            _project.Drawing.TextHeightMm = textHeight;
            return true;
        }

        private bool TryParse(TextBox box, string name, out double value)
        {
            var text = box.Text.Trim().Replace('.', ',');
            if (double.TryParse(text, NumberStyles.Float, Fmt.Ru, out value))
                return true;
            MessageBox.Show(this, $"Неверное число в поле «{name}».", Title);
            box.Focus();
            return false;
        }

        private static string Num(double value) => value.ToString("0.###", Fmt.Ru);
    }
}
