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

    [DataContract]
    public sealed class ProtectionProject
    {
        [DataMember] public NormKind Norm { get; set; } = NormKind.So153;

        /// <summary>Среднегодовая продолжительность гроз, ч/год.</summary>
        [DataMember] public double ThunderstormHours { get; set; } = 40;

        [DataMember] public List<ProtectedObject> Objects { get; set; } = new List<ProtectedObject>();
        [DataMember] public List<LightningRod> Rods { get; set; } = new List<LightningRod>();

        /// <summary>Шаг подбора высоты мачты, м.</summary>
        [DataMember] public double HeightStep { get; set; } = 0.5;

        /// <summary>Минимальная высота мачты при подборе, м.</summary>
        [DataMember] public double MinMastHeight { get; set; } = 2;

        /// <summary>Максимальная полная высота молниеприёмника при подборе, м.</summary>
        [DataMember] public double MaxRodHeight { get; set; } = 60;

        [DataMember] public DrawingSettings Drawing { get; set; } = new DrawingSettings();
    }
}
