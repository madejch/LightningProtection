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
            // Здание — зона Б на +9,000; свеча — зона Б на +11,000; клапан — зона А на +6,500.
            Assert.Equal(3, result.Sections.Count);
            Assert.Contains(result.Sections, s => s.Level == ZoneLevel.RdB && s.Hx == 11);
            Assert.Contains(result.Sections, s => s.Level == ZoneLevel.RdB && s.Hx == 9);
            Assert.Contains(result.Sections, s => s.Level == ZoneLevel.RdA && s.Hx == 6.5);
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
        public void GasVent_DefaultsToZoneB()
        {
            var vent = ObjectPresets.GasVent("Свеча", new Point2(0, 0), 6);
            Assert.Equal(RdZone.B, vent.RdZone);
            Assert.Equal(SpaceShape.Cylinder, vent.Shape);
        }

        [Fact]
        public void Hemisphere_IsCheckedOverItsHeight_AndIsLessStrictThanCylinder()
        {
            // Мачта h = 20 м, зона Б; свеча с обрезом +6,000 на расстоянии d.
            // Цилиндр 5 × 5 м защищён при d ≤ 7,07 м, полусфера R = 5 м — примерно до 10,6 м.
            ProtectionProject Project(double d, SpaceShape shape)
            {
                var project = new ProtectionProject { Norm = NormKind.Rd87 };
                project.Rods.Add(new LightningRod { Name = "Мл1", MastHeight = 20 });
                var vent = ObjectPresets.GasVent("Свеча", new Point2(d, 0), 6);
                vent.Shape = shape;
                project.Objects.Add(vent);
                return project;
            }

            Assert.False(ProtectionCalculator.Calculate(Project(9, SpaceShape.Cylinder)).AllProtected);
            Assert.True(ProtectionCalculator.Calculate(Project(9, SpaceShape.Hemisphere)).AllProtected);
            Assert.False(ProtectionCalculator.Calculate(Project(12, SpaceShape.Hemisphere)).AllProtected);

            var hemisphere = ProtectionCalculator.Calculate(Project(9, SpaceShape.Hemisphere));
            Assert.Equal(6, Assert.Single(hemisphere.Sections).Hx);
            Assert.Contains("полусфера R = 5,00 м",
                ReportBuilder.BuildTables(Project(9, SpaceShape.Hemisphere), hemisphere)[0].Rows[0][2]);
        }

        [Fact]
        public void Neighbors_CollinearRods_PairOnlyAdjacent()
        {
            var rods = new List<LightningRod>
            {
                new LightningRod { X = 0, Y = 0 },
                new LightningRod { X = 40, Y = 0 },
                new LightningRod { X = 20, Y = 0 },
            };
            var neighbors = RodNeighbors.Build(rods);
            Assert.Equal(2, neighbors.Pairs.Count);
            Assert.Contains((0, 2), neighbors.Pairs);
            Assert.Contains((1, 2), neighbors.Pairs);
            Assert.Empty(neighbors.Triangles);
        }

        [Fact]
        public void Neighbors_RodInsideTriangle_IsNotPairedAcross()
        {
            // Треугольник из трёх мачт и четвёртая в центре: пары только по рёбрам триангуляции.
            var rods = new List<LightningRod>
            {
                new LightningRod { X = 0, Y = 0 },
                new LightningRod { X = 40, Y = 0 },
                new LightningRod { X = 20, Y = 34 },
                new LightningRod { X = 20, Y = 12 },
            };
            var neighbors = RodNeighbors.Build(rods);
            Assert.Equal(6, neighbors.Pairs.Count);
            Assert.Equal(3, neighbors.Triangles.Count);
            Assert.DoesNotContain(neighbors.Triangles, t => t == (0, 1, 2));
        }

        [Fact]
        public void Rd87_HasNoSpurAboveHc_So153_Has()
        {
            // Две мачты h = 20 м на 30 м: зона Б, hc = 18,4 − 0,14·10 = 17 м. На hx = 17,5 м (выше hc)
            // по РД-87 остаются только круги одиночных мачт, по СО 153 добавляется выступ lx.
            var rods = new List<LightningRod>
            {
                new LightningRod { X = 0, Y = 0, MastHeight = 20 },
                new LightningRod { X = 30, Y = 0, MastHeight = 20 },
            };
            var rd = new PlanZoneBuilder(Norms.Rd87Norm.Instance).Build(rods, ZoneLevel.RdB, 17.5);
            var rx = Norms.Rd87Norm.Instance.Single(20, ZoneLevel.RdB).Rx(17.5);
            Assert.Equal(2 * System.Math.PI * rx * rx, rd.Area, 1);

            var so = new PlanZoneBuilder(Norms.So153Norm.Instance).Build(rods, ZoneLevel.So090, 16);
            var soRx = Norms.So153Norm.Instance.Single(20, ZoneLevel.So090).Rx(16);
            Assert.True(so.Area > 2 * System.Math.PI * soRx * soRx * 1.01);
        }

        [Fact]
        public void Description_NamesNormItems()
        {
            var project = GruProject();
            HeightOptimizer.Optimize(project);
            var text = DescriptionBuilder.Build(project, ProtectionCalculator.Calculate(project));
            Assert.Contains("прил. 3, п. 1.1", text);
            Assert.Contains("пп. 2.1, 2.2", text);
            Assert.Contains("прил. 2", text);
            Assert.Contains("п. 2.6", text);
            Assert.Contains("п. 2.18", text);
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
        public void Serializer_RoundTrip_KeepsSystems()
        {
            var project = new ProtectionProject();
            var boiler = project.AddSystem("Котельная");
            boiler.Objects.AddRange(GruProject().Objects);
            boiler.Rods.AddRange(GruProject().Rods);
            boiler.TableInsertPoint = new Point2(1, 2);
            var warehouse = project.AddSystem("Склад");
            warehouse.Rods.Add(new LightningRod { Name = "Мл3", X = 500 });

            var copy = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));
            Assert.Equal(new[] { "Котельная", "Склад" }, copy.Systems.Select(s => s.Name));
            Assert.Equal(boiler.Id, copy.Systems[0].Id);
            Assert.Equal(3, copy.Systems[0].Objects.Count);
            Assert.Equal(4, copy.Systems[0].Objects[0].Contour.Count);
            Assert.Equal(ObjectKind.GasVent, copy.Systems[0].Objects[1].Kind);
            Assert.Equal(2, copy.Systems[0].TableInsertPoint!.Y);
            Assert.Equal(500, Assert.Single(copy.Systems[1].Rods).X);
            Assert.Empty(copy.Objects);
            Assert.Empty(copy.Rods);
        }

        [Fact]
        public void LegacyProject_IsMigratedToOneSystem()
        {
            // Чертёж версии 0.2: объекты, молниеприёмники и точки вставки лежат прямо в проекте.
            var legacy = GruProject();
            legacy.Drawing.SideViewInsertPoint = new Point2(0, -60000);
            legacy.Drawing.SideViewAngleDeg = 90;
            var json = ProjectSerializer.ToJson(legacy).Replace("\"Systems\":[],", "");

            var project = ProjectSerializer.FromJson(json);
            var system = Assert.Single(project.Systems);
            Assert.Equal("Участок 1", system.Name);
            Assert.Equal(3, system.Objects.Count);
            Assert.Equal(2, system.Rods.Count);
            Assert.Equal(-60000, system.SideViewInsertPoint!.Y);
            Assert.Equal(90, system.SideViewAngleDeg);
            Assert.Empty(project.Objects);
            Assert.Null(project.Drawing.SideViewInsertPoint);
        }

        [Fact]
        public void Systems_AreCalculatedIndependently()
        {
            // Два здания в 200 м друг от друга, у каждого свои мачты. Мачта склада не должна
            // попадать в пары, таблицы и зону котельной, и наоборот.
            var project = new ProtectionProject { Norm = NormKind.Rd87 };
            var boiler = project.AddSystem("Котельная");
            var building = ObjectPresets.Building("Котельная", 9);
            building.Contour = new List<Point2> { new(0, 0), new(24, 0), new(24, 12), new(0, 12) };
            boiler.Objects.Add(building);
            boiler.Rods.Add(new LightningRod { Name = "Мл1", X = -2, Y = 6, MastHeight = 18 });
            boiler.Rods.Add(new LightningRod { Name = "Мл2", X = 26, Y = 6, MastHeight = 18 });

            var store = project.AddSystem("Склад");
            var shed = ObjectPresets.Building("Склад", 6);
            shed.Contour = new List<Point2> { new(200, 0), new(212, 0), new(212, 8), new(200, 8) };
            store.Objects.Add(shed);
            store.Rods.Add(new LightningRod { Name = "Мл3", X = 206, Y = 4, MastHeight = 14 });

            var boilerResult = ProtectionCalculator.Calculate(project.ForSystem(boiler));
            var storeResult = ProtectionCalculator.Calculate(project.ForSystem(store));

            Assert.All(boilerResult.Rods, r => Assert.Contains(r.Rod, boiler.Rods));
            Assert.All(boilerResult.Pairs, p => Assert.DoesNotContain(p.Second, store.Rods));
            Assert.Equal(new[] { "Мл3" }, storeResult.Rods.Select(r => r.Rod.Name).Distinct());
            Assert.Empty(storeResult.Pairs);
            Assert.Equal("hx", Assert.Single(storeResult.Sections).Name);
            Assert.True(boilerResult.Sections[0].Zone.EnvelopeInternal.MaxX < 150);

            // Подбор высоты меняет мачты только своего участка.
            HeightOptimizer.Optimize(project.ForSystem(store));
            Assert.Equal(18, boiler.Rods[0].MastHeight);
            Assert.Equal("Склад", project.ForSystem(store).Title);
        }
    }
}
