using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using SkiaSharp;
using Xunit;
using ZXing;

namespace PoGOQRCodesGenerator.Printing.Tests;

public class RenderingTests
{
    private static byte[] Font => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "PrintFont.ttf"));
    private static readonly DateTime Expiry = new(2026, 11, 30);

    [Fact]
    public void RenderedQrDecodesToTheExactEscapedRedemptionUrl()
    {
        var template = TestTemplate();
        string url = PromoCodes.CreateUrl(PromoCodes.DefaultUrlTemplate, "SAMPLE0000001");
        using var renderer = new CardRenderer(template, Font);
        using SKBitmap bitmap = SKBitmap.Decode(renderer.RenderCardPreview(url, Expiry, guides: false));
        byte[] rgb = new byte[bitmap.Width * bitmap.Height * 3];
        int offset = 0;
        foreach (SKColor pixel in bitmap.Pixels)
        {
            rgb[offset++] = pixel.Red; rgb[offset++] = pixel.Green; rgb[offset++] = pixel.Blue;
        }
        var reader = new BarcodeReaderGeneric();
        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        Assert.Equal(url, reader.Decode(rgb, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.RGB24)?.Text);
    }

    [Fact]
    public void LastDuplexSheetRendersEmptySlotsOnCorrespondingSides()
    {
        var template = TestTemplate();
        var pages = template.Layout.CreatePages(30, PdfExportMode.Duplex);
        var urls = Enumerable.Range(1, 30).Select(i => PromoCodes.CreateUrl(PromoCodes.DefaultUrlTemplate, $"SAMPLE{i:0000000}")).ToArray();
        using var renderer = new CardRenderer(template, Font);
        using SKBitmap front = SKBitmap.Decode(renderer.RenderPagePreview(pages[2], urls, Expiry));
        using SKBitmap back = SKBitmap.Decode(renderer.RenderPagePreview(pages[3], urls, Expiry));
        // Front slots 14,15 and back slots 12,13 must be white; the other side of
        // each occupied front must contain back artwork at the reflected position.
        foreach (int slot in new[] { 14, 15 }) Assert.Equal(SKColors.White, Sample(front, template.Layout.FrontBounds(slot)));
        foreach (int slot in new[] { 12, 13 }) Assert.Equal(SKColors.White, Sample(back, template.Layout.FrontBounds(slot)));
        foreach (int slot in new[] { 14, 15 }) Assert.NotEqual(SKColors.White, Sample(back, template.Layout.FrontBounds(slot)));
        // The asymmetric front marker stays at top-left; artwork is not mirrored.
        PrintRect first = pages[2].Cards[0].Bounds;
        Assert.Equal(SKColors.DarkViolet, front.GetPixel((int)((first.X + 2) * 4), (int)((first.Y + 2) * 4)));
    }

    [Fact]
    public void ExportsBothDuplexModesAndSeparateDocuments()
    {
        var urls = Enumerable.Range(1, 30).Select(i => PromoCodes.CreateUrl(PromoCodes.DefaultUrlTemplate, $"SAMPLE{i:0000000}")).ToArray();
        foreach (FlipEdge edge in Enum.GetValues<FlipEdge>())
        {
            var template = TestTemplate() with { Layout = new PrintLayout { FlipEdge = edge } };
            using var renderer = new CardRenderer(template, Font);
            var pages = template.Layout.CreatePages(30, PdfExportMode.Duplex);
            byte[] pdf = renderer.RenderPdf(pages, urls, Expiry);
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 8));
            Assert.True(pdf.Length > 1000);
            var boxes = Regex.Matches(Encoding.ASCII.GetString(pdf), @"/MediaBox\s*\[0 0 ([\d.]+) ([\d.]+)\]");
            Assert.Equal(4, boxes.Count);
            foreach (Match box in boxes)
            {
                Assert.InRange(Math.Abs(double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture) - 210 * 72 / 25.4), 0, .001);
                Assert.InRange(Math.Abs(double.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture) - 297 * 72 / 25.4), 0, .001);
            }
            SaveFixture(edge == FlipEdge.LongEdge ? "duplex-long.pdf" : "duplex-short.pdf", pdf);
            if (edge == FlipEdge.LongEdge)
            {
                SaveFixture("last-front.png", renderer.RenderPagePreview(pages[2], urls, Expiry));
                SaveFixture("last-back.png", renderer.RenderPagePreview(pages[3], urls, Expiry));
                SaveFixture("card-back.png", renderer.RenderCardPreview(urls[0], Expiry, guides: false));
                var separate = template.Layout.CreatePages(30, PdfExportMode.Separate);
                SaveFixture("front.pdf", renderer.RenderPdf(separate.Where(p => !p.IsBack).ToArray(), urls, Expiry));
                SaveFixture("backs.pdf", renderer.RenderPdf(separate.Where(p => p.IsBack).ToArray(), urls, Expiry));
            }
        }
    }

    [Fact]
    public void CancelledGenerationDoesNotReturnAPartialPdf()
    {
        var template = TestTemplate();
        using var renderer = new CardRenderer(template, Font);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => renderer.RenderPdf(template.Layout.CreatePages(1, PdfExportMode.Duplex),
            [PromoCodes.CreateUrl(PromoCodes.DefaultUrlTemplate, "SAMPLE0000001")], Expiry, cancellation.Token));
    }

    [Fact]
    public async Task SavedTemplateSurvivesReloadWithImagesFontAndDuplexCalibration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pogo-template-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TemplateStore(directory);
            var template = TestTemplate() with
            {
                FontData = Font,
                Layout = new PrintLayout { FlipEdge = FlipEdge.ShortEdge, BackOffsetXmm = 1.2, BackOffsetYmm = -.5 }
            };
            await store.SaveAsync(template);
            await store.SaveAsync(template with { Name = "Updated template" });
            var catalog = await store.LoadAsync();
            Assert.Empty(catalog.Errors);
            var loaded = Assert.Single(catalog.Templates);
            Assert.Equal("Updated template", loaded.Name);
            Assert.Equal(template.FrontImage, loaded.FrontImage);
            Assert.Equal(template.BackImage, loaded.BackImage);
            Assert.Equal(template.FontData, loaded.FontData);
            Assert.Equal(template.Layout, loaded.Layout);
            Assert.Equal("exp. 30 Nov", loaded.FormatDate(Expiry));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static SKColor Sample(SKBitmap image, PrintRect cell) => image.GetPixel((int)((cell.X + 1) * 4), (int)((cell.Y + 1) * 4));

    private static CardTemplate TestTemplate() => new()
    {
        Name = "Print validation", FrontImage = Panel(false), BackImage = Panel(true), DateColor = "#6B5470"
    };

    private static byte[] Panel(bool isBack)
    {
        using var bitmap = new SKBitmap(600, 800);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(isBack ? new SKColor(250, 238, 248) : new SKColor(238, 230, 250));
        using var paint = new SKPaint { Color = SKColors.DarkViolet, IsAntialias = false };
        canvas.DrawRect(0, 0, 45, 45, paint);
        using SKData data = SKData.CreateCopy(Font);
        using SKTypeface typeface = SKTypeface.FromData(data);
        using var font = new SKFont(typeface, isBack ? 48 : 40);
        paint.Color = new SKColor(65, 34, 95); paint.IsAntialias = true;
        canvas.DrawText(isBack ? "KPI Gang" : "COMMUNITY", 300, 86, SKTextAlign.Center, font, paint);
        if (!isBack)
        {
            canvas.DrawText("GIFT CARD", 300, 390, SKTextAlign.Center, font, paint);
            font.Size = 25;
            canvas.DrawText("PRINT TEST", 300, 460, SKTextAlign.Center, font, paint);
        }
        font.Size = 24;
        canvas.DrawText(isBack ? "t.me/pokemon_go_kpi" : "KPI GANG", 300, 757, SKTextAlign.Center, font, paint);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static void SaveFixture(string name, byte[] bytes)
    {
        string? output = Environment.GetEnvironmentVariable("POGO_PRINT_TEST_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, name), bytes);
    }
}
