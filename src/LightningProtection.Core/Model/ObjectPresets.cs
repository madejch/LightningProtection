namespace LightningProtection.Core.Model
{
    /// <summary>
    /// Значения по умолчанию для типовых объектов. Проектировщик уточняет их по таблице 1 и п. 2.x
    /// РД 34.21.122-87 (категория, тип зоны, размеры защищаемого пространства над обрезом трубы).
    /// </summary>
    public static class ObjectPresets
    {
        public static ProtectedObject Building(string name, double topElevation) => new ProtectedObject
        {
            Name = name,
            Kind = ObjectKind.Building,
            SourceElevation = topElevation,
            RdZone = RdZone.B,
            SoReliability = SoReliability.P090,
        };

        /// <summary>
        /// Продувочная свеча с природным газом (легче воздуха). Зона Б — наружная установка
        /// класса В-Iг (РД-87, табл. 1, п. 3). Пространство над обрезом по п. 2.6: для трубы
        /// с колпаком при давлении более 25,25 кПа — цилиндр H = 5 м, R = 5 м (по умолчанию);
        /// для прямой трубы — полусфера R = 5 м (форма «Полусфера»).
        /// </summary>
        public static ProtectedObject GasVent(string name, Point2 axis, double outletElevation) => new ProtectedObject
        {
            Name = name,
            Kind = ObjectKind.GasVent,
            Center = axis,
            SourceElevation = outletElevation,
            SpaceHeight = 5,
            Radius = 5,
            Shape = SpaceShape.Cylinder,
            RdZone = RdZone.B,
            SoReliability = SoReliability.P099,
        };

        /// <summary>
        /// Дыхательный клапан резервуара: цилиндр высотой 2,5 м и радиусом 5 м над клапаном
        /// (РД-87, п. 2.18 — горючие газы и ЛВЖ; для горючих жидкостей с температурой вспышки
        /// выше 61 °C п. 2.29 требует защиты только корпуса резервуара).
        /// </summary>
        public static ProtectedObject BreathingValve(string name, Point2 axis, double valveElevation) => new ProtectedObject
        {
            Name = name,
            Kind = ObjectKind.BreathingValve,
            Center = axis,
            SourceElevation = valveElevation,
            SpaceHeight = 2.5,
            Radius = 5,
            RdZone = RdZone.A,
            SoReliability = SoReliability.P099,
        };
    }
}
