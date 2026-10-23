using System;
using System.Linq;
using LightningProtection.Core.Model;

namespace LightningProtection.Core.Calculation
{
    public sealed class OptimizationResult
    {
        public bool Success { get; set; }
        /// <summary>Подобранная высота мачт (для молниеприёмников без фиксированной высоты), м.</summary>
        public double MastHeight { get; set; }
        public CalculationResult Calculation { get; set; } = null!;
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Подбор минимальной высоты мачт: все молниеприёмники без фиксированной высоты получают
    /// одинаковую высоту мачты, которая перебирается с шагом проекта до полной защиты всех объектов.
    /// </summary>
    public static class HeightOptimizer
    {
        public static OptimizationResult Optimize(ProtectionProject project)
        {
            var adjustable = project.Rods.Where(r => !r.IsHeightFixed).ToList();
            if (adjustable.Count == 0)
            {
                var fixedResult = ProtectionCalculator.Calculate(project);
                return new OptimizationResult
                {
                    Success = fixedResult.AllProtected,
                    Calculation = fixedResult,
                    Message = "У всех молниеприёмников высота задана вручную — выполнена только проверка.",
                };
            }

            var original = adjustable.Select(r => r.MastHeight).ToArray();
            var step = project.HeightStep > 0 ? project.HeightStep : 0.5;
            var maxMast = project.MaxRodHeight - adjustable.Max(r => r.BaseElevation);
            CalculationResult? last = null;

            for (var k = 0; ; k++)
            {
                var mast = Math.Round(project.MinMastHeight + k * step, 3);
                if (mast > maxMast + 1e-9)
                    break;

                foreach (var rod in adjustable)
                    rod.MastHeight = mast;
                last = ProtectionCalculator.Calculate(project);
                if (last.AllProtected)
                {
                    return new OptimizationResult
                    {
                        Success = true,
                        MastHeight = mast,
                        Calculation = last,
                        Message = $"Подобрана высота мачт {mast:0.0#} м.",
                    };
                }
            }

            for (var i = 0; i < adjustable.Count; i++)
                adjustable[i].MastHeight = original[i];

            return new OptimizationResult
            {
                Success = false,
                Calculation = last ?? ProtectionCalculator.Calculate(project),
                Message = $"Не удалось защитить все объекты при полной высоте молниеприёмников до {project.MaxRodHeight:0.#} м. " +
                          "Добавьте молниеприёмники или измените их расстановку.",
            };
        }
    }
}
