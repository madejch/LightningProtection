using System.Collections.Generic;
using System.Linq;
using LightningProtection.Core.Calculation;
using LightningProtection.Core.Model;
using LightningProtection.Core.Report;
using LightningProtection.Core.Zones;
using Xunit;

namespace LightningProtection.Core.Tests
{
    public class CalculationTests
    {
        private static ProtectionProject GruProject()
        {
            // Здание котельной 24 × 12 м высотой 9 м, свеча ГРУ у торца, резервуар ДТ в стороне.
            var project = new ProtectionProject { Norm = NormKind.Rd87, ThunderstormHours = 40 };
            var building = ObjectPresets.Building("Котельная", 9);
            building.Contour = new List<Point2> { new(0, 0), new(24, 0), new(24, 12), new(0, 12) };
            project.Objects.Add(building);
            project.Objects.Add(ObjectPresets.GasVent("Продувочная свеча ГРУ", new Point2(28, 6), 6));
            project.Objects.Add(ObjectPresets.BreathingValve("Дыхательный клапан резервуара ДТ", new Point2(40, 6), 4));
            project.Rods.Add(new LightningRod { Name = "М1", X = -2, Y = 6, MastHeight = 10 });
            project.Rods.Add(new LightningRod { Name = "М2", X = 32, Y = 6, MastHeight = 10 });
            return project;
        }

        [Fact]
        public void SingleRod_CoversCircleInside_AndNotOutside()
        {
            var project = new ProtectionProject { Norm = NormKind.Rd87 };
            project.Rods.Add(new LightningRod { Name = "М1", MastHeight = 20 });
            // На hx = 7,5 м радиус зоны Б: 1,5 (20 − 7,5 / 0,92) ≈ 17,77 м
            project.Objects.Add(ObjectPresets.GasVent("Свеча", new Point2(10, 0), 2.5));
            project.Objects[0].RdZone = RdZone.B;
            var ok = ProtectionCalculator.Calculate(project);
            Assert.True(ok.AllProtected);

            project.Objects[0].Center = new Point2(14, 0);
            var fail = ProtectionCalculator.Calculate(project);
            Assert.False(fail.AllProtected);
        }

        [Fact]
        public void TwoRods_ProtectBuildingBetween_OnlyThroughBridge()
        {
            var project = new ProtectionProject { Norm = NormKind.Rd87 };
            var building = ObjectPresets.Building("Здание", 6);
            building.Contour = new List<Point2> { new(-1, -2), new(31, -2), new(31, 2), new(-1, 2) };
            project.Objects.Add(building);
            project.Rods.Add(new LightningRod { Name = "М1", X = 0, Y = 0, MastHeight = 15 });
            project.Rods.Add(new LightningRod { Name = "М2", X = 30, Y = 0, MastHeight = 15 });

            var result = ProtectionCalculator.Calculate(project);
            Assert.True(result.AllProtected);
            Assert.Single(result.Pairs);
            // Круги rx = 1,5 (15 − 6 / 0,92) ≈ 12,7 м не дотягиваются до середины — защищает перемычка.
            Assert.True(result.Rods.First().Rx < 15);
        }

        [Fact]
        public void Optimizer_FindsMinimalHeight()
        {
            var project = GruProject();
            var optimization = HeightOptimizer.Optimize(project);
            Assert.True(optimization.Success, optimization.Message);

            var found = optimization.MastHeight;
            Assert.All(project.Rods, r => Assert.Equal(found, r.MastHeight));

            foreach (var rod in project.Rods)
                rod.MastHeight = found - project.HeightStep;
            Assert.False(ProtectionCalculator.Calculate(project).AllProtected);
        }

        [Fact]
        public void Optimizer_KeepsFixedRods()
        {
            var project = GruProject();
            project.Rods[0].IsHeightFixed = true;
            project.Rods[0].MastHeight = 25;
            var optimization = HeightOptimizer.Optimize(project);
            Assert.True(optimization.Success, optimization.Message);
            Assert.Equal(25, project.Rods[0].MastHeight);
        }

        [Fact]
        public void DifferentLevels_ProduceSeparateSections()
        {
            var project = GruProject();
            HeightOptimizer.Optimize(project);
            var result = ProtectionCalculator.Calculate(project);
            // Здание — зона Б на +9,000; свеча — зона А на +11,000; клапан — зона А на +6,500.
            Assert.Equal(3, result.Sections.Count);
            Assert.Contains(result.Sections, s => s.Level == ZoneLevel.RdA && s.Hx == 11);
            Assert.Contains(result.Sections, s => s.Level == ZoneLevel.RdB && s.Hx == 9);
        }

        [Fact]
        public void Sections_AreNumberedByElevation()
        {
            var project = GruProject();
            HeightOptimizer.Optimize(project);
            var result = ProtectionCalculator.Calculate(project);
            Assert.Equal(new[] { "hx1", "hx2", "hx3" }, result.Sections.Select(s => s.Name));
            Assert.Equal(new[] { 6.5, 9, 11 }, result.Sections.Select(s => s.Hx));
            Assert.All(result.Rods, r => Assert.NotNull(r.Section));
        }

        [Fact]
        public void SingleSection_IsNamedHx()
        {
            var project = GruProject();
            project.Objects.RemoveRange(1, 2);
            var result = ProtectionCalculator.Calculate(project);
            Assert.Equal("hx", Assert.Single(result.Sections).Name);
        }

        [Fact]
        public void PlanScale_AutoForNaturalSizeDrawing()
        {
            var project = GruProject();
            HeightOptimizer.Optimize(project);
            var result = ProtectionCalculator.Calculate(project);
            project.Drawing.Scale = 1;
            var auto = DrawingScales.PlanScale(project, result);
            Assert.Contains(auto, DrawingScales.Standard);
            Assert.True(auto >= 100);
            Assert.Equal(auto, DrawingScales.SideViewScale(project, auto));

            project.Drawing.Scale = 200;
            project.Drawing.SideViewScale = 100;
            Assert.Equal(200, DrawingScales.PlanScale(project, result));
            Assert.Equal(100, DrawingScales.SideViewScale(project, 200));
        }

        [Fact]
        public void StrikeEstimate_UsesBoundingRectangle()
        {
            var project = GruProject();
            var result = ProtectionCalculator.Calculate(project);
            var strike = Assert.Single(result.Strikes);
            Assert.Equal(12, strike.Width, 6);
            Assert.Equal(24, strike.Length, 6);
            var expected = ((12 + 54) * (24 + 54) - 7.7 * 81) * 4 * 1e-6;
            Assert.Equal(expected, strike.ExpectedStrikes, 9);
        }

        [Fact]
        public void SideView_HasProfilePerLevel()
        {
            var project = GruProject();
            var view = SideViewBuilder.Build(project, 0);
            Assert.Equal(2, view.Profiles.Count);
            Assert.All(view.Profiles, p => Assert.NotEmpty(p.Outlines));
            Assert.Equal(3, view.Objects.Count);
        }

        [Fact]
        public void Report_AndDescription_AreFilled()
        {
            var project = GruProject();
            HeightOptimizer.Optimize(project);
            var result = ProtectionCalculator.Calculate(project);
            var tables = ReportBuilder.BuildTables(project, result);
            Assert.Equal(3, tables.Count);
            Assert.All(tables, t => Assert.All(t.Rows, r => Assert.Equal(t.Headers.Count, r.Length)));

            var text = DescriptionBuilder.Build(project, result);
            Assert.DoesNotContain("{", text);
            Assert.Contains("РД 34.21.122-87", text);
            Assert.Contains("Продувочная свеча ГРУ", text);
            // Принятые коэффициенты — явно, для экспертизы.
            Assert.Contains("h0 = 0,85h", text);
            Assert.Contains("r0 = (1,1 − 0,002h)h", text);
            Assert.Contains("h0 = 0,92h", text);
            Assert.Contains("hc = h0 − 0,14(L − h)", text);
            Assert.Contains(tables[1].Rows, r => r.Contains("h0 = 0,92h"));
            Assert.Contains("прил. 3", tables[1].Title);
        }

        [Fact]
        public void Serializer_RoundTrip()
        {
            var project = GruProject();
            var copy = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));
            Assert.Equal(project.Objects.Count, copy.Objects.Count);
            Assert.Equal(project.Rods[1].X, copy.Rods[1].X);
            Assert.Equal(4, copy.Objects[0].Contour.Count);
            Assert.Equal(ObjectKind.GasVent, copy.Objects[1].Kind);
        }
    }
}
