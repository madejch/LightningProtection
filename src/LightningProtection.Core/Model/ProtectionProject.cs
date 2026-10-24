using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LightningProtection.Core.Model
{
    /// <summary>Стержневой молниеприёмник. Высота h отсчитывается от земли.</summary>
    [DataContract]
    public sealed class LightningRod
    {
        [DataMember] public string Name { get; set; } = "";
        [DataMember] public double X { get; set; }
        [DataMember] public double Y { get; set; }

        /// <summary>Отметка установки (земля, кровля, площадка), м.</summary>
        [DataMember] public double BaseElevation { get; set; }

        /// <summary>Высота мачты над отметкой установки, м.</summary>
        [DataMember] public double MastHeight { get; set; } = 10;

        /// <summary>Высота задана вручную, подбор её не меняет.</summary>
        [DataMember] public bool IsHeightFixed { get; set; }

        public double TotalHeight => BaseElevation + MastHeight;
    }

    /// <summary>Параметры оформления на чертеже.</summary>
    [DataContract]
    public sealed class DrawingSettings
    {
        /// <summary>Единиц чертежа в одном метре (1000 — чертёж в мм).</summary>
        [DataMember] public double UnitsPerMeter { get; set; } = 1000;

        /// <summary>
        /// Знаменатель масштаба плана (100 для 1:100). 0 или 1 — подобрать по размерам плана:
        /// чертёж в натуральную величину, текст и размеры увеличиваются под масштаб печати.
        /// </summary>
        [DataMember] public double Scale { get; set; }

        /// <summary>Знаменатель масштаба вида сбоку. 0 или 1 — как у плана.</summary>
        [DataMember] public double SideViewScale { get; set; }

        /// <summary>Высота текста на листе, мм.</summary>
        [DataMember] public double TextHeightMm { get; set; } = 2.5;

        /// <summary>Направление взгляда для вида сбоку: ось вида повёрнута на угол от X, град.</summary>
        [DataMember] public double SideViewAngleDeg { get; set; }

        [DataMember] public Point2? SideViewInsertPoint { get; set; }
        [DataMember] public Point2? TableInsertPoint { get; set; }
        [DataMember] public Point2? DescriptionInsertPoint { get; set; }
    }

    /// <summary>
    /// Расчётный участок: здание (или группа объектов) со своими молниеприёмниками. Участки считаются
    /// и рисуются независимо: мачты одного участка не участвуют в зонах другого.
    /// </summary>
    [DataContract]
    public sealed class ProtectionSystem
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Name { get; set; } = "";
        [DataMember] public List<ProtectedObject> Objects { get; set; } = new List<ProtectedObject>();
        [DataMember] public List<LightningRod> Rods { get; set; } = new List<LightningRod>();

        /// <summary>Направление взгляда для вида сбоку: ось вида повёрнута на угол от X, град.</summary>
        [DataMember] public double SideViewAngleDeg { get; set; }

        [DataMember] public Point2? SideViewInsertPoint { get; set; }
        [DataMember] public Point2? TableInsertPoint { get; set; }
        [DataMember] public Point2? DescriptionInsertPoint { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Проект молниезащиты чертежа: общие параметры (норма, Td, подбор, оформление) и расчётные участки.
    /// Расчёт ведётся по одному участку: <see cref="ForSystem"/> даёт проект, в котором
    /// <see cref="Objects"/> и <see cref="Rods"/> — объекты и молниеприёмники этого участка.
    /// </summary>
    [DataContract]
    public sealed class ProtectionProject
    {
        [DataMember] public NormKind Norm { get; set; } = NormKind.So153;

        /// <summary>Среднегодовая продолжительность гроз, ч/год.</summary>
        [DataMember] public double ThunderstormHours { get; set; } = 40;

        [DataMember] public List<ProtectionSystem> Systems { get; set; } = new List<ProtectionSystem>();

        /// <summary>Объекты рассчитываемого участка (в сохранённом проекте — только у старых чертежей).</summary>
        [DataMember] public List<ProtectedObject> Objects { get; set; } = new List<ProtectedObject>();

        /// <summary>Молниеприёмники рассчитываемого участка (в сохранённом проекте — только у старых чертежей).</summary>
        [DataMember] public List<LightningRod> Rods { get; set; } = new List<LightningRod>();

        /// <summary>Наименование рассчитываемого участка — для заголовков на чертеже.</summary>
        public string Title { get; set; } = "";

        /// <summary>Шаг подбора высоты мачты, м.</summary>
        [DataMember] public double HeightStep { get; set; } = 0.5;

        /// <summary>Минимальная высота мачты при подборе, м.</summary>
        [DataMember] public double MinMastHeight { get; set; } = 2;

        /// <summary>Максимальная полная высота молниеприёмника при подборе, м.</summary>
        [DataMember] public double MaxRodHeight { get; set; } = 60;

        [DataMember] public DrawingSettings Drawing { get; set; } = new DrawingSettings();

        /// <summary>
        /// Проект для расчёта одного участка: общие параметры и списки объектов и молниеприёмников
        /// участка (те же экземпляры — подбор высоты меняет мачты участка).
        /// </summary>
        public ProtectionProject ForSystem(ProtectionSystem system) => new ProtectionProject
        {
            Norm = Norm,
            ThunderstormHours = ThunderstormHours,
            HeightStep = HeightStep,
            MinMastHeight = MinMastHeight,
            MaxRodHeight = MaxRodHeight,
            Objects = system.Objects,
            Rods = system.Rods,
            Title = system.Name,
            Drawing = new DrawingSettings
            {
                UnitsPerMeter = Drawing.UnitsPerMeter,
                Scale = Drawing.Scale,
                SideViewScale = Drawing.SideViewScale,
                TextHeightMm = Drawing.TextHeightMm,
                SideViewAngleDeg = system.SideViewAngleDeg,
                SideViewInsertPoint = system.SideViewInsertPoint,
                TableInsertPoint = system.TableInsertPoint,
                DescriptionInsertPoint = system.DescriptionInsertPoint,
            },
        };

        public ProtectionSystem AddSystem(string? name = null)
        {
            var system = new ProtectionSystem { Name = name ?? $"Участок {Systems.Count + 1}" };
            Systems.Add(system);
            return system;
        }

        /// <summary>
        /// Старые чертежи хранили объекты и молниеприёмники прямо в проекте — переносит их
        /// в первый участок. Гарантирует, что в проекте есть хотя бы один участок.
        /// </summary>
        public void EnsureSystems()
        {
            Systems ??= new List<ProtectionSystem>();
            if (Systems.Count > 0 && Objects.Count == 0 && Rods.Count == 0)
                return;

            var system = Systems.Count > 0 ? Systems[0] : AddSystem("Участок 1");
            system.Objects.AddRange(Objects);
            system.Rods.AddRange(Rods);
            Objects.Clear();
            Rods.Clear();
            if (system.SideViewInsertPoint == null && system.TableInsertPoint == null && system.DescriptionInsertPoint == null)
            {
                system.SideViewAngleDeg = Drawing.SideViewAngleDeg;
                system.SideViewInsertPoint = Drawing.SideViewInsertPoint;
                system.TableInsertPoint = Drawing.TableInsertPoint;
                system.DescriptionInsertPoint = Drawing.DescriptionInsertPoint;
            }
            Drawing.SideViewInsertPoint = null;
            Drawing.TableInsertPoint = null;
            Drawing.DescriptionInsertPoint = null;
        }
    }
}
