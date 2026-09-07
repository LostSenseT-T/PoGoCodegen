using SkiaSharp;
using Xunit;
using ZXing;

namespace PoGOQRCodesGenerator.Printing.Tests;

public sealed class RenderedPdfFactAttribute : FactAttribute
{
    public RenderedPdfFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POGO_VERIFY_RENDERED_PDFS")))
            Skip = "Set POGO_VERIFY_RENDERED_PDFS to the Poppler 300 dpi fixture output directory.";
    }
}

public class RenderedPdfTests
{
    [RenderedPdfFact]
    public void EveryQrInActualPdfRastersHasTheCorrectValueAndPhysicalPosition()
    {
        string root = Environment.GetEnvironmentVariable("POGO_VERIFY_RENDERED_PDFS")!;
        const double scale = 300 / 25.4;
        var reader = new BarcodeReaderGeneric();
        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        reader.Options.TryHarder = true;
        foreach (string edge in new[] { "long", "short" })
        for (int sheet = 0; sheet < 2; sheet++)
        {
            using SKBitmap image = SKBitmap.Decode(Path.Combine(root, $"rendered-duplex-{edge}-{sheet * 2 + 2}.png"));
            Assert.NotNull(image);
            for (int slot = 0; slot < 16; slot++)
            {
                int index = sheet * 16 + slot;
                // Independent expected physical mapping for the default 4x4 fixture.
                int col = edge == "long" ? 3 - slot % 4 : slot % 4;
                int row = edge == "short" ? 3 - slot / 4 : slot / 4;
                int x = (int)Math.Round((12 + col * 47) * scale);
                int y = (int)Math.Round((25.5 + row * 62) * scale);
                int width = (int)Math.Round(45 * scale), height = (int)Math.Round(60 * scale);
                using var card = new SKBitmap();
                Assert.True(image.ExtractSubset(card, SKRectI.Create(x, y, width, height)));
                if (index >= 30)
                {
                    Assert.All(card.Pixels, pixel => Assert.Equal(SKColors.White, pixel));
                    continue;
                }
                byte[] rgb = new byte[card.Width * card.Height * 3];
                int pos = 0;
                foreach (SKColor pixel in card.Pixels)
                {
                    rgb[pos++] = pixel.Red; rgb[pos++] = pixel.Green; rgb[pos++] = pixel.Blue;
                }
                Result? decoded = reader.Decode(rgb, card.Width, card.Height, RGBLuminanceSource.BitmapFormat.RGB24);
                Assert.True(decoded is not null, $"Could not decode {edge} edge, sheet {sheet + 1}, code {index + 1}, physical row {row + 1}, column {col + 1}.");
                Assert.Equal($"https://store.pokemongo.com/offer-redemption?passcode=SAMPLE{index + 1:0000000}", decoded?.Text);
            }
        }
    }
}
