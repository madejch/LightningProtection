using LightningProtection.Core.Model;
using LightningProtection.Core.Norms;
using Xunit;

namespace LightningProtection.Core.Tests
{
    public class NormTests
    {
        [Fact]
        public void Rd87_ZoneB_SingleRod_MatchesTextbookFormula()
        {
            var cone = Rd87Norm.Instance.Single(20, ZoneLevel.RdB);
            Assert.Equal(18.4, cone.H0, 6);
            Assert.Equal(30, cone.R0, 6);
            // rx = 1,5 (h − hx / 0,92)
            Assert.Equal(1.5 * (20 - 10 / 0.92), cone.Rx(10), 6);
        }

        [Fact]
        public void Rd87_ZoneB_InverseFormula_GivesSameHeight()
        {
            // h = (rx + 1,63 hx) / 1,5
            const double rx = 12, hx = 8;
            var h = (rx + 1.63 * hx) / 1.5;
            Assert.Equal(rx, Rd87Norm.Instance.Single(h, ZoneLevel.RdB).Rx(hx), 2);
        }

        [Fact]
        public void Rd87_ZoneA_SingleRod()
        {
            var cone = Rd87Norm.Instance.Single(20, ZoneLevel.RdA);
            Assert.Equal(17, cone.H0, 6);
            Assert.Equal((1.1 - 0.04) * 20, cone.R0, 6);
            // rx = (1,1 − 0,002h)(h − hx / 0,85)
            Assert.Equal((1.1 - 0.04) * (20 - 6 / 0.85), cone.Rx(6), 6);
        }

        [Fact]
        public void Rd87_ZoneB_Pair()
        {
            var norm = Rd87Norm.Instance;
            Assert.Equal(18.4, norm.Pair(20, 20, 15, ZoneLevel.RdB).Hc, 6);
            Assert.Equal(18.4 - 0.14 * 40, norm.Pair(20, 20, 60, ZoneLevel.RdB).Hc, 6);
            Assert.False(norm.Pair(20, 20, 121, ZoneLevel.RdB).Joined);
        }

        [Fact]
        public void Rd87_ZoneA_Pair()
        {
            var norm = Rd87Norm.Instance;
            Assert.Equal(17 - (0.17 + 3e-4 * 20) * 10, norm.Pair(20, 20, 30, ZoneLevel.RdA).Hc, 6);
            Assert.False(norm.Pair(20, 20, 41, ZoneLevel.RdA).Joined);
        }

        [Theory]
        [InlineData(ZoneLevel.So090, 20, 17, 24)]
        [InlineData(ZoneLevel.So099, 20, 16, 16)]
        [InlineData(ZoneLevel.So0999, 20, 14, 12)]
        public void So153_SingleRod_Table34(ZoneLevel level, double h, double h0, double r0)
        {
            var cone = So153Norm.Instance.Single(h, level);
            Assert.Equal(h0, cone.H0, 6);
            Assert.Equal(r0, cone.R0, 6);
        }

        [Fact]
        public void So153_SingleRod_Over30m()
        {
            var cone = So153Norm.Instance.Single(50, ZoneLevel.So099);
            Assert.Equal(40, cone.H0, 6);
            Assert.Equal((0.8 - 1.43e-3 * 20) * 50, cone.R0, 6);
        }

        [Fact]
        public void So153_Pair_Table36()
        {
            var norm = So153Norm.Instance;
            // Pз = 0,99, h = 20: Lc = 45, Lmax = 95
            Assert.Equal(16, norm.Pair(20, 20, 45, ZoneLevel.So099).Hc, 6);
            Assert.Equal(16 * (95 - 70) / 50.0, norm.Pair(20, 20, 70, ZoneLevel.So099).Hc, 6);
            Assert.False(norm.Pair(20, 20, 95, ZoneLevel.So099).Joined);
        }

        [Fact]
        public void Coefficients_AreShownWithSubstitution()
        {
            Assert.Equal(("0,92h", "1,5h"), Rd87Norm.Instance.SingleCoefficients(20, ZoneLevel.RdB));
            Assert.Equal(("0,85h", "(1,1 − 0,002h)h = 1,060h"), Rd87Norm.Instance.SingleCoefficients(20, ZoneLevel.RdA));
            Assert.Equal(("0,8h", "0,8h"), So153Norm.Instance.SingleCoefficients(20, ZoneLevel.So099));
            Assert.Equal(("0,8h", "[0,8 − 1,43·10⁻³(h − 30)]h = 0,771h"), So153Norm.Instance.SingleCoefficients(50, ZoneLevel.So099));
        }

        [Fact]
        public void PairFormula_NamesAppliedCase()
        {
            Assert.Equal(("h < L ≤ 6h", "hc = h0 − 0,14(L − h)"), Rd87Norm.Instance.PairFormula(20, 20, 34, ZoneLevel.RdB));
            Assert.Equal(("L ≤ h", "hc = h0"), Rd87Norm.Instance.PairFormula(20, 20, 15, ZoneLevel.RdA));
            var (condition, hc) = So153Norm.Instance.PairFormula(20, 20, 70, ZoneLevel.So099);
            Assert.Equal("Lc < L < Lmax (Lmax = 4,75h = 95,00 м, Lc = 2,25h = 45,00 м)", condition);
            Assert.Equal("hc = h0(Lmax − L)/(Lmax − Lc)", hc);
            Assert.StartsWith("hc = (hc1 + hc2)/2", Rd87Norm.Instance.PairFormula(20, 25, 30, ZoneLevel.RdB).Hc);
        }

        [Fact]
        public void Pair_DifferentHeights_AveragesHc()
        {
            var norm = Rd87Norm.Instance;
            var p1 = norm.Pair(20, 20, 30, ZoneLevel.RdB);
            var p2 = norm.Pair(25, 25, 30, ZoneLevel.RdB);
            var mixed = norm.Pair(20, 25, 30, ZoneLevel.RdB);
            Assert.Equal((p1.Hc + p2.Hc) / 2, mixed.Hc, 6);
            Assert.Equal((30 + 37.5) / 2, mixed.Rc, 6);
        }
    }
}
