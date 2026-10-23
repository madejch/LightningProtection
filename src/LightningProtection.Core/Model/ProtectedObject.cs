using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LightningProtection.Core.Model
{
    [DataContract]
    public sealed class Point2
    {
        public Point2() { }
        public Point2(double x, double y) { X = x; Y = y; }

        [DataMember] public double X { get; set; }
        [DataMember] public double Y { get; set; }
    }

    /// <summary>
    /// Защищаемый объект: вертикальная призма (контур здания) или цилиндр (пространство
    /// над свечой или дыхательным клапаном). Все размеры в метрах, отметки от уровня земли.
    /// </summary>
    [DataContract]
    public sealed class ProtectedObject
    {
        [DataMember] public string Name { get; set; } = "";
        [DataMember] public ObjectKind Kind { get; set; }

        /// <summary>Контур в плане (для здания). Меньше трёх точек — объект круглый.</summary>
        [DataMember] public List<Point2> Contour { get; set; } = new List<Point2>();

        /// <summary>Центр круглого объекта (ось свечи или клапана).</summary>
        [DataMember] public Point2 Center { get; set; } = new Point2();

        /// <summary>Радиус защищаемого пространства в плане, м.</summary>
        [DataMember] public double Radius { get; set; }

        /// <summary>Отметка низа объекта для вида сбоку, м.</summary>
        [DataMember] public double BaseElevation { get; set; }

        /// <summary>Отметка верха здания или обреза трубы/клапана, м.</summary>
        [DataMember] public double SourceElevation { get; set; }

        /// <summary>Высота защищаемого пространства над обрезом, м (для здания 0).</summary>
        [DataMember] public double SpaceHeight { get; set; }

        [DataMember] public RdZone RdZone { get; set; } = RdZone.B;
        [DataMember] public SoReliability SoReliability { get; set; } = SoReliability.P090;

        public bool IsCircular => Contour == null || Contour.Count < 3;

        /// <summary>Отметка hx, на которой проверяется защищённость объекта.</summary>
        public double CheckElevation => SourceElevation + SpaceHeight;

        public string KindTitle => Kind.Title();

        public ZoneLevel GetLevel(NormKind norm)
        {
            if (norm == NormKind.Rd87)
                return RdZone == RdZone.A ? ZoneLevel.RdA : ZoneLevel.RdB;
            return SoReliability switch
            {
                SoReliability.P090 => ZoneLevel.So090,
                SoReliability.P099 => ZoneLevel.So099,
                _ => ZoneLevel.So0999,
            };
        }
    }
}
