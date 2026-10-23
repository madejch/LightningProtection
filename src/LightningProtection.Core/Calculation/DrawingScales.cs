using System;
using System.Linq;
using LightningProtection.Core.Model;

namespace LightningProtection.Core.Calculation
{
    /// <summary>Масштабы плана и вида сбоку для оформления.</summary>
    public static class DrawingScales
    {
        /// <summary>Стандартные масштабы по ГОСТ 2.302.</summary>
        public static readonly double[] Standard = { 10, 20, 25, 50, 100, 200, 250, 400, 500, 800, 1000, 2000, 5000 };

        /// <summary>Наибольший размер плана на листе при автоподборе масштаба, мм.</summary>
        public const double PlanPaperSizeMm = 400;

        /// <summary>Масштаб плана: заданный пользователем или подобранный по размерам зон и объектов.</summary>
        public static double PlanScale(ProtectionProject project, CalculationResult result)
        {
            if (project.Drawing.Scale > 1)
                return project.Drawing.Scale;

            var envelope = new NetTopologySuite.Geometries.Envelope();
            foreach (var section in result.Sections)
                envelope.ExpandToInclude(section.Zone.EnvelopeInternal);
            foreach (var check in result.Objects)
                envelope.ExpandToInclude(check.Footprint.EnvelopeInternal);
            foreach (var rod in project.Rods)
                envelope.ExpandToInclude(rod.X, rod.Y);

            var extent = envelope.IsNull ? 0 : Math.Max(envelope.Width, envelope.Height);
            return Pick(extent, PlanPaperSizeMm);
        }

        /// <summary>Масштаб вида сбоку: заданный или равный масштабу плана.</summary>
        public static double SideViewScale(ProtectionProject project, double planScale)
            => project.Drawing.SideViewScale > 1 ? project.Drawing.SideViewScale : planScale;

        /// <summary>Наименьший стандартный масштаб, при котором размер в метрах помещается на листе.</summary>
        public static double Pick(double extentMeters, double paperMm)
        {
            var scale = Standard.FirstOrDefault(s => extentMeters * 1000 / s <= paperMm);
            return scale > 0 ? scale : Standard[Standard.Length - 1];
        }
    }
}
