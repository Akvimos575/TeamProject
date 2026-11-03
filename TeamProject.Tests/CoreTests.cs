using System;
using System.IO;
using TeamProject.Core;
using Xunit;

namespace TeamProject.Tests
{
    public class HudParserTests
    {
        [Theory]
        [InlineData("Sector 1234 87%", 1234)]
        [InlineData("SECTOR #56 - 12.5 %", 56)]
        [InlineData("Сектор 163,842  99%", 163842)]
        [InlineData("98% 777", 777)]
        [InlineData("Sector 1O4", 104)]
        [InlineData("Region: l23", 123)]
        public void ParsesSector(string text, int expected) => Assert.Equal(expected, HudParser.ParseSector(text));

        [Theory]
        [InlineData("")]
        [InlineData("Sector")]
        [InlineData(null)]
        public void SectorMissing(string? text) => Assert.Null(HudParser.ParseSector(text));

        [Theory]
        [InlineData("Sector 1234 87%", 87.0)]
        [InlineData("12.5 %", 12.5)]
        [InlineData("99,2%", 99.2)]
        [InlineData("1OO%", 100.0)]
        public void ParsesPercent(string text, double expected) => Assert.Equal(expected, HudParser.ParsePercent(text)!.Value, 3);

        [Theory]
        [InlineData("Sector 1234")]
        [InlineData("250%")]
        public void PercentMissingOrInvalid(string text) => Assert.Null(HudParser.ParsePercent(text));

        [Theory]
        [InlineData("$12,345", 12345L)]
        [InlineData("12 345 $", 12345L)]
        [InlineData("€1.200", 1200L)]
        [InlineData("$ 30.50", 30L)]
        public void ParsesMoney(string text, long expected) => Assert.Equal(expected, HudParser.ParseMoney(text));

        [Fact]
        public void PromptMatchIgnoresCaseAndSpaces()
        {
            Assert.True(HudParser.ContainsPrompt("[E] Load  Packager", "load packager"));
            Assert.False(HudParser.ContainsPrompt("[E] Open garage", "packager"));
        }
    }

    public class AnglesTests
    {
        [Theory]
        [InlineData(190, -170)]
        [InlineData(-190, 170)]
        [InlineData(180, 180)]
        [InlineData(720, 0)]
        public void Normalize180(double input, double expected) => Assert.Equal(expected, Angles.Normalize180(input), 6);

        [Fact]
        public void DeltaTakesShortestWay()
        {
            Assert.Equal(20, Angles.Delta(350, 10), 6);
            Assert.Equal(-20, Angles.Delta(10, 350), 6);
        }

        [Fact]
        public void TurnToPutBaseAt()
        {
            // База справа на 30° — чтобы смотреть на неё, повернуть вправо на 30.
            Assert.Equal(30, Angles.TurnToPutBaseAt(30, 0), 6);
            // Смотрим на базу, хотим чтобы она была слева (−90): повернуть вправо на 90.
            Assert.Equal(90, Angles.TurnToPutBaseAt(0, -90), 6);
            // Спиной к базе.
            Assert.Equal(180, Math.Abs(Angles.TurnToPutBaseAt(0, 180)), 6);
        }

        [Fact]
        public void PolarAroundBase()
        {
            // Смотрим на базу (bearing 0) при yaw 0 — значит мы «за» базой по направлению 180.
            Assert.Equal(180, Angles.PolarAroundBase(0, 0), 6);
            // Если смотреть по направлению polar (yaw = polar), база окажется ровно сзади (bearing 180).
            double polar = Angles.PolarAroundBase(37, 12);
            Assert.Equal(polar, Angles.PolarAroundBase(polar, 180), 6);
        }

        [Fact]
        public void TangentKeepsBaseOnCorrectSide()
        {
            Assert.True(SpiralPlanner.TangentBaseBearing(circleLeft: true, inwardBiasDeg: 6) < 0);
            Assert.True(SpiralPlanner.TangentBaseBearing(circleLeft: false, inwardBiasDeg: 6) > 0);
            Assert.Equal(-84, SpiralPlanner.TangentBaseBearing(true, 6), 6);
        }
    }

    public class SpiralPlannerTests
    {
        private static SpiralPlanner NewPlanner(out BotState state)
        {
            state = new BotState();
            return new SpiralPlanner(state, 99.0);
        }

        [Fact]
        public void DirtySectorIsCleaned()
        {
            var p = NewPlanner(out _);
            Assert.Equal(PlannerMove.CleanHere, p.Decide(10, 40));
        }

        [Fact]
        public void CleanHomeSectorStepsOutward()
        {
            var p = NewPlanner(out var s);
            Assert.Equal(PlannerMove.Outward, p.Decide(10, 99.5));
            Assert.Contains(10, s.CleanedSectors);
        }

        [Fact]
        public void RingClosesWhenReturningToStart()
        {
            var p = NewPlanner(out _);
            p.BeginNextRing(100);
            Assert.Equal(1, p.Ring);
            foreach (var id in new[] { 101, 102, 103, 104, 105 })
            {
                p.OnEnterSector(id);
                Assert.Equal(PlannerMove.Tangential, p.Decide(id, 100));
            }
            p.OnEnterSector(100); // снова первый сектор кольца
            Assert.True(p.RingClosed);
            Assert.Equal(PlannerMove.Outward, p.Decide(100, 100));

            p.BeginNextRing(200);
            Assert.Equal(2, p.Ring);
            Assert.False(p.RingClosed);
        }

        [Fact]
        public void EarlyWobbleBackToStartDoesNotCloseRing()
        {
            var p = NewPlanner(out _);
            p.BeginNextRing(100);
            p.OnEnterSector(101);
            p.OnEnterSector(100); // покачнулись через границу назад — это не замыкание
            Assert.False(p.RingClosed);
        }

        [Fact]
        public void RingClosesBySafetyLimit()
        {
            var p = NewPlanner(out _);
            p.BeginNextRing(1);
            for (int i = 2; i < 2 + SpiralPlanner.MaxSectorsInRing(1); i++) p.OnEnterSector(i);
            Assert.True(p.RingClosed);
        }

        [Fact]
        public void UnknownPercentFallsBackToSavedState()
        {
            var p = NewPlanner(out var s);
            Assert.Equal(PlannerMove.CleanHere, p.Decide(5, null));
            s.CleanedSectors.Add(5);
            Assert.Equal(PlannerMove.Outward, p.Decide(5, null));
        }

        [Fact]
        public void MaxRingsFinishes()
        {
            var s = new BotState();
            var p = new SpiralPlanner(s, 99, maxRings: 1);
            p.BeginNextRing(1);
            for (int i = 2; i < 2 + SpiralPlanner.MaxSectorsInRing(1); i++) p.OnEnterSector(i);
            Assert.Equal(PlannerMove.Finished, p.Decide(999, 100));
        }
    }

    public class StableReadingTests
    {
        [Fact]
        public void IgnoresSingleGlitch()
        {
            var f = new StableReading<int>(3, 2);
            f.Push(1234);
            Assert.Equal(1234, f.Push(1234));
            Assert.Equal(1234, f.Push(1284)); // одиночная ошибка OCR
            Assert.Equal(1234, f.Push(null));
        }
    }

    public class ImageMathTests
    {
        private static byte[] Image(int w, int h, System.Func<int, int, (byte r, byte g, byte b)> px)
        {
            var a = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, b) = px(x, y);
                int i = (y * w + x) * 4;
                a[i] = b; a[i + 1] = g; a[i + 2] = r; a[i + 3] = 255;
            }
            return a;
        }

        [Fact]
        public void FindsMarkerAndIgnoresNoise()
        {
            var orange = new ColorSpec { R = 255, G = 140, B = 0, Tolerance = 20 };
            var img = Image(100, 10, (x, y) => (x >= 60 && x <= 64) || (x == 5 && y == 0) ? ((byte)250, (byte)145, (byte)5) : ((byte)0, (byte)0, (byte)0));
            var cx = ImageMath.FindMarkerCenterX(img, 100, 10, 400, orange, 4);
            Assert.NotNull(cx);
            Assert.Equal(62, cx!.Value, 3);
        }

        [Fact]
        public void NoMarker()
        {
            var orange = new ColorSpec { R = 255, G = 140, B = 0, Tolerance = 20 };
            var img = Image(50, 5, (x, y) => ((byte)0, (byte)0, (byte)0));
            Assert.Null(ImageMath.FindMarkerCenterX(img, 50, 5, 200, orange, 4));
        }

        [Fact]
        public void FillFractionBar()
        {
            var green = new ColorSpec { R = 0, G = 200, B = 0, Tolerance = 30 };
            var img = Image(100, 6, (x, y) => x < 40 ? ((byte)0, (byte)200, (byte)0) : ((byte)30, (byte)30, (byte)30));
            Assert.Equal(0.40, ImageMath.FillFraction(img, 100, 6, 400, green, barMode: true), 3);
            Assert.Equal(0.40, ImageMath.FillFraction(img, 100, 6, 400, green, barMode: false), 3);
        }

        [Fact]
        public void BearingSign()
        {
            Assert.Equal(10, ImageMath.BearingFromCompassX(120, 100, 0.5), 6);
            Assert.Equal(-10, ImageMath.BearingFromCompassX(80, 100, 0.5), 6);
        }
    }

    public class PersistenceTests
    {
        [Fact]
        public void StateRoundTrip()
        {
            var path = Path.Combine(Path.GetTempPath(), "botstate_test_" + System.Guid.NewGuid() + ".json");
            try
            {
                var s = new BotState { HomeSectorId = 42, Ring = 3, RingClosed = true };
                s.RingVisited.AddRange(new[] { 1, 2, 3 });
                s.CleanedSectors.Add(7);
                s.PurchasedLevels["Section Bonus"] = 2;
                s.Save(path);

                var r = BotState.Load(path);
                Assert.Equal(42, r.HomeSectorId);
                Assert.Equal(3, r.Ring);
                Assert.True(r.RingClosed);
                Assert.Equal(new[] { 1, 2, 3 }, r.RingVisited);
                Assert.Contains(7, r.CleanedSectors);
                Assert.Equal(2, r.PurchasedLevel("Section Bonus"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void ShippedConfigParses()
        {
            var path = Path.Combine(FindRepoRoot(), "TeamProject", "config.json");
            var cfg = BotConfig.Load(path);
            Assert.Equal("W", cfg.Keys.Forward);
            Assert.Equal("Toggle", cfg.Keys.SectorBordersMode);
            Assert.True(cfg.CircleLeft);
            Assert.NotEmpty(cfg.Upgrades);
            Assert.Equal(30, cfg.Upgrades[0].Cost);
            Assert.Null(cfg.Upgrades[1].Cost);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TeamProject.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("TeamProject.sln не найден");
        }
    }
}
