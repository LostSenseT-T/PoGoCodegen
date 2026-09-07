using Xunit;

namespace PoGOQRCodesGenerator.Printing.Tests;

public class PrintLayoutTests
{
    [Theory]
    [InlineData(1, 2)] [InlineData(15, 2)] [InlineData(16, 2)]
    [InlineData(17, 4)] [InlineData(30, 4)] [InlineData(32, 4)] [InlineData(33, 6)]
    public void DuplexPairsEveryCodeOnceAndPreservesBlankPositions(int count, int expectedPages)
    {
        foreach (FlipEdge edge in Enum.GetValues<FlipEdge>())
        {
            var layout = new PrintLayout { FlipEdge = edge };
            var pages = layout.CreatePages(count, PdfExportMode.Duplex);
            Assert.Equal(expectedPages, pages.Count);
            Assert.Equal(Enumerable.Range(0, count), pages.Where(p => p.IsBack).SelectMany(p => p.Cards).Select(c => c.CodeIndex));
            for (int page = 0; page < pages.Count; page += 2)
            {
                Assert.False(pages[page].IsBack);
                Assert.True(pages[page + 1].IsBack);
                Assert.Equal(pages[page].Cards.Count, pages[page + 1].Cards.Count);
                foreach (var front in pages[page].Cards)
                {
                    var back = Assert.Single(pages[page + 1].Cards, b => b.CodeIndex == front.CodeIndex);
                    if (edge == FlipEdge.LongEdge)
                    {
                        Assert.Equal(210, front.Bounds.X + back.Bounds.X + front.Bounds.Width, 8);
                        Assert.Equal(front.Bounds.Y, back.Bounds.Y, 8);
                    }
                    else
                    {
                        Assert.Equal(297, front.Bounds.Y + back.Bounds.Y + front.Bounds.Height, 8);
                        Assert.Equal(front.Bounds.X, back.Bounds.X, 8);
                    }
                    Assert.Equal(front.Bounds.Width, back.Bounds.Width);
                    Assert.Equal(front.Bounds.Height, back.Bounds.Height);
                }
            }
        }
    }

    [Fact]
    public void FifteenCodesLeaveOppositeCornerEmptyForLongEdge()
    {
        var layout = new PrintLayout();
        var pages = layout.CreatePages(15, PdfExportMode.Duplex);
        Assert.DoesNotContain(pages[0].Cards, c => c.Bounds == layout.FrontBounds(15));
        Assert.DoesNotContain(pages[1].Cards, c => c.Bounds == layout.FrontBounds(12));
        Assert.Contains(pages[1].Cards, c => c.CodeIndex == 12 && c.Bounds == layout.FrontBounds(15));
    }

    [Fact]
    public void ShortEdgeReversesRowsWithoutReversingColumns()
    {
        var layout = new PrintLayout { FlipEdge = FlipEdge.ShortEdge };
        var pages = layout.CreatePages(1, PdfExportMode.Duplex);
        Assert.Equal(layout.FrontBounds(12), Assert.Single(pages[1].Cards).Bounds);
    }

    [Fact]
    public void SeparateOutputHasOneFullFrontAndOnlyOccupiedMirroredBacks()
    {
        var pages = new PrintLayout().CreatePages(30, PdfExportMode.Separate);
        Assert.Equal(3, pages.Count);
        Assert.False(pages[0].IsBack);
        Assert.Equal(16, pages[0].Cards.Count);
        Assert.Equal(16, pages[1].Cards.Count);
        Assert.Equal(14, pages[2].Cards.Count);
        Assert.Equal(Enumerable.Range(0, 30), pages.Where(p => p.IsBack).SelectMany(p => p.Cards).Select(c => c.CodeIndex));
    }

    [Fact]
    public void MirroringUsesThePageNotTheGridAndAppliesCalibrationAfterReflection()
    {
        var rect = new PrintRect(11, 19, 30, 40);
        var layout = new PrintLayout { BackOffsetXmm = 1.5, BackOffsetYmm = -2 };
        Assert.Equal(new PrintRect(170.5, 17, 30, 40), layout.BackBounds(rect));
        Assert.Equal(new PrintRect(12.5, 236, 30, 40), (layout with { FlipEdge = FlipEdge.ShortEdge }).BackBounds(rect));
    }

    [Theory]
    [InlineData(3, 4, 12)] [InlineData(3, 3, 9)] [InlineData(1, 1, 1)]
    public void GridDimensionsAreConfigurable(int columns, int rows, int capacity)
    {
        var layout = new PrintLayout { Columns = columns, Rows = rows };
        Assert.Equal(capacity, layout.Capacity);
        Assert.Equal(4, layout.CreatePages(capacity + 1, PdfExportMode.Duplex).Count);
    }

    [Fact]
    public void RejectsInvalidOrClippedLayouts()
    {
        Assert.Throws<ArgumentException>(() => new PrintLayout { Columns = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new PrintLayout { CardWidthMm = 60 }.Validate());
        Assert.Throws<ArgumentException>(() => new PrintLayout { BackOffsetXmm = 100 }.Validate());
        Assert.Throws<ArgumentException>(() => new PrintLayout { GapMm = -1 }.Validate());
        Assert.Throws<ArgumentException>(() => new PrintLayout { CardWidthMm = double.NaN }.Validate());
        Assert.Throws<ArgumentException>(() => new PrintLayout().CreatePages(0, PdfExportMode.Duplex));
    }

    [Fact]
    public void CodesSupportMixedSeparatorsAndPreserveOrder()
    {
        Assert.Equal(new[] { "ABC123", "DEF456", "GHI789" }, PromoCodes.Parse(" ,ABC123,\r\nDEF456\tGHI789\u00a0ABC123\f,, "));
        Assert.Empty(PromoCodes.Parse(null));
        Assert.Empty(PromoCodes.Parse(" ,\r\n\t "));
        Assert.Throws<ArgumentException>(() => PromoCodes.CreateUrl("https://example.com", "ABC"));
        Assert.Throws<ArgumentException>(() => PromoCodes.CreateUrl("file:///{code}", "ABC"));
        Assert.Equal("https://example.com/?code=A%26B", PromoCodes.CreateUrl("https://example.com/?code={code}", "A&B"));
    }

    [Fact]
    public void TemplateRejectsAreasOutsideCard()
    {
        Assert.Throws<ArgumentException>(() => new CardTemplate { QrLeft = .3, QrWidth = .85 }.Validate(false));
        Assert.Throws<ArgumentException>(() => new CardTemplate { DateTop = .99 }.Validate(false));
        Assert.Throws<ArgumentException>(() => new CardTemplate { QrColor = "bad-color" }.Validate(false));
    }
}
