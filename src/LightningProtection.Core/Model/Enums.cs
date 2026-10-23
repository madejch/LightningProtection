namespace LightningProtection.Core.Model
{
    /// <summary>Нормативный документ, по которому выполняется расчёт.</summary>
    public enum NormKind
    {
        /// <summary>РД 34.21.122-87.</summary>
        Rd87 = 0,
        /// <summary>СО 153-34.21.122-2003.</summary>
        So153 = 1,
    }

    /// <summary>Тип зоны защиты по РД 34.21.122-87.</summary>
    public enum RdZone
    {
        /// <summary>Зона А — надёжность 99,5 % и выше.</summary>
        A = 0,
        /// <summary>Зона Б — надёжность 95 % и выше.</summary>
        B = 1,
    }

    /// <summary>Надёжность защиты Pз по СО 153-34.21.122-2003.</summary>
    public enum SoReliability
    {
        P090 = 0,
        P099 = 1,
        P0999 = 2,
    }

    /// <summary>Уровень защиты в терминах конкретной нормы — ключ для формул зоны.</summary>
    public enum ZoneLevel
    {
        RdA,
        RdB,
        So090,
        So099,
        So0999,
    }

    public enum ObjectKind
    {
        /// <summary>Здание или сооружение, заданное контуром.</summary>
        Building = 0,
        /// <summary>Продувочная (сбросная) свеча, газоотводная труба.</summary>
        GasVent = 1,
        /// <summary>Дыхательный клапан резервуара.</summary>
        BreathingValve = 2,
        /// <summary>Произвольное пространство: цилиндр или призма.</summary>
        Other = 3,
    }

    public static class EnumTitles
    {
        public static string Title(this NormKind norm) => norm switch
        {
            NormKind.Rd87 => "РД 34.21.122-87",
            _ => "СО 153-34.21.122-2003",
        };

        public static string Title(this ObjectKind kind) => kind switch
        {
            ObjectKind.Building => "Здание",
            ObjectKind.GasVent => "Продувочная свеча",
            ObjectKind.BreathingValve => "Дыхательный клапан",
            _ => "Пространство",
        };

        public static string Title(this ZoneLevel level) => level switch
        {
            ZoneLevel.RdA => "зона А",
            ZoneLevel.RdB => "зона Б",
            ZoneLevel.So090 => "Pз = 0,9",
            ZoneLevel.So099 => "Pз = 0,99",
            _ => "Pз = 0,999",
        };

        public static string Title(this SoReliability r) => r switch
        {
            SoReliability.P090 => "0,9",
            SoReliability.P099 => "0,99",
            _ => "0,999",
        };
    }
}
