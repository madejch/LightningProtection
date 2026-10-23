using System.Globalization;
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
        Draw,
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
    /// с кодом <see cref="Action"/> — команда выполняет указание и открывает окно снова.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ProtectionProject _project;

        public MainWindow(ProtectionProject project)
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
            AngleBox.Text = Num(project.Drawing.SideViewAngleDeg);

            ObjectsGrid.ItemsSource = project.Objects;
            RodsGrid.ItemsSource = project.Rods;
        }

        public DialogAction Action { get; private set; } = DialogAction.Cancel;

        public bool ResetPlacement => ResetPlacementBox.IsChecked == true;

        private void AddBuilding_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.AddBuilding);
        private void AddGasVent_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.AddGasVent);
        private void AddValve_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.AddBreathingValve);
        private void AddRods_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.AddRods);
        private void Draw_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.Draw);
        private void Save_Click(object sender, RoutedEventArgs e) => Finish(DialogAction.Save);

        private void DeleteObject_Click(object sender, RoutedEventArgs e)
        {
            if (ObjectsGrid.SelectedItem is ProtectedObject obj)
            {
                _project.Objects.Remove(obj);
                ObjectsGrid.Items.Refresh();
            }
        }

        private void DeleteRod_Click(object sender, RoutedEventArgs e)
        {
            if (RodsGrid.SelectedItem is LightningRod rod)
            {
                _project.Rods.Remove(rod);
                RodsGrid.Items.Refresh();
            }
        }

        private void Calculate_Click(object sender, RoutedEventArgs e)
        {
            if (!Commit())
                return;
            var result = ProtectionCalculator.Calculate(_project);
            ResultBox.Text = ReportBuilder.BuildText(_project, result);
        }

        private void Optimize_Click(object sender, RoutedEventArgs e)
        {
            if (!Commit())
                return;
            var optimization = HeightOptimizer.Optimize(_project);
            RodsGrid.Items.Refresh();
            ResultBox.Text = optimization.Message + "\n\n" + ReportBuilder.BuildText(_project, optimization.Calculation);
        }

        private void Finish(DialogAction action)
        {
            if (!Commit())
                return;
            Action = action;
            DialogResult = true;
        }

        /// <summary>Завершает редактирование таблиц и переносит общие поля в проект.</summary>
        private bool Commit()
        {
            ObjectsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            RodsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (!TryParse(TdBox, "Td", out var td) || !TryParse(StepBox, "шаг подбора", out var step) ||
                !TryParse(MinMastBox, "минимальная мачта", out var minMast) || !TryParse(MaxHeightBox, "наибольшая высота", out var maxHeight) ||
                !TryParse(UpmBox, "единиц в метре", out var upm) || !TryParse(ScaleBox, "масштаб плана", out var scale) ||
                !TryParse(SideScaleBox, "масштаб вида сбоку", out var sideScale) ||
                !TryParse(TextHeightBox, "высота текста", out var textHeight) || !TryParse(AngleBox, "угол вида", out var angle))
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
            _project.Drawing.SideViewAngleDeg = angle;
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
