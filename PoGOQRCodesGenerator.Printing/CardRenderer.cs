using QRCoder;
using SkiaSharp;

namespace PoGOQRCodesGenerator.Printing;

/// <summary>Uses the same drawing path for previews and print output. Owns its native resources.</summary>
public sealed class CardRenderer : IDisposable
{
    private readonly CardTemplate template;
    private readonly SKImage? front;
    private readonly SKImage? back;
    private readonly SKTypeface typeface;

    public CardRenderer(CardTemplate template, byte[] defaultFont, bool requireImages = true)
    {
        template.Validate(requireImages);
        this.template = template;
        try
        {
            front = DecodeImage(template.FrontImage);
            back = DecodeImage(template.BackImage);
            using SKData fontData = SKData.CreateCopy(template.FontData ?? defaultFont);
            typeface = SKTypeface.FromData(fontData) ?? throw new ArgumentException("The font file could not be opened.");
        }
        catch
        {
            front?.Dispose();
            back?.Dispose();
            throw;
        }
    }

    public static SKImage? DecodeImage(byte[]? data)
    {
        if (data is not { Length: > 0 }) return null;
        using var stream = new SKMemoryStream(data);
        using SKCodec codec = SKCodec.Create(stream) ?? throw new ArgumentException("Choose a valid PNG or JPEG image.");
        if ((long)codec.Info.Width * codec.Info.Height > 25_000_000)
            throw new ArgumentException("Images must contain at most 25 million pixels.");
        return SKImage.FromEncodedData(data) ?? throw new ArgumentException("The image could not be opened.");
    }

    public byte[] RenderCardPreview(string url, DateTime expiry, bool showBack = true, bool guides = true)
    {
        using var bitmap = new SKBitmap(600, 800);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        DrawCard(canvas, new SKRect(0, 0, 600, 800), showBack, url, expiry, guides);
        return EncodePng(bitmap);
    }

    public byte[] RenderPagePreview(PrintPage page, IReadOnlyList<string> urls, DateTime expiry)
    {
        using var bitmap = new SKBitmap(840, 1188);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(840 / Mm(PrintLayout.PageWidthMm));
        DrawPage(canvas, page, urls, expiry, CancellationToken.None);
        return EncodePng(bitmap);
    }

    public byte[] RenderPdf(IReadOnlyList<PrintPage> pages, IReadOnlyList<string> urls, DateTime expiry,
        CancellationToken token = default, IProgress<double>? progress = null)
    {
        using var stream = new MemoryStream();
        // Skia quantizes the PDF page box on its raster grid. At 254 dpi there
        // are exactly 10 grid units per mm, so A4 retains its physical size and
        // duplex reflection uses the same axis as the exported page box.
        SKDocumentPdfMetadata metadata = SKDocumentPdfMetadata.Default;
        metadata.RasterDpi = 254;
        metadata.EncodingQuality = 101; // Keep backgrounds lossless; zero-initialized metadata would use low-quality JPEG.
        using SKDocument document = SKDocument.CreatePdf(stream, metadata)
            ?? throw new InvalidOperationException("PDF creation is not available on this device.");
        try
        {
            for (int i = 0; i < pages.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                SKCanvas canvas = document.BeginPage(Mm(PrintLayout.PageWidthMm), Mm(PrintLayout.PageHeightMm));
                DrawPage(canvas, pages[i], urls, expiry, token);
                document.EndPage();
                progress?.Report((i + 1d) / pages.Count);
            }
            token.ThrowIfCancellationRequested();
            document.Close();
            return stream.ToArray();
        }
        catch
        {
            document.Abort();
            throw;
        }
    }

    private void DrawPage(SKCanvas canvas, PrintPage page, IReadOnlyList<string> urls, DateTime expiry, CancellationToken token)
    {
        foreach (CardPlacement card in page.Cards)
        {
            token.ThrowIfCancellationRequested();
            PrintRect r = card.Bounds;
            // All transforms have positive scale: the placement is mirrored, not QR/text/artwork.
            DrawCard(canvas, SKRect.Create(Mm(r.X), Mm(r.Y), Mm(r.Width), Mm(r.Height)), page.IsBack,
                page.IsBack ? urls[card.CodeIndex] : null, expiry, false);
        }
        if (template.Layout.CropMarks) DrawCropMarks(canvas, page.IsBack);
    }

    private void DrawCard(SKCanvas canvas, SKRect target, bool isBack, string? url, DateTime expiry, bool guides)
    {
        float width = Mm(template.Layout.CardWidthMm);
        float height = Mm(template.Layout.CardHeightMm);
        canvas.Save();
        try
        {
            canvas.Translate(target.Left, target.Top);
            canvas.Scale(target.Width / width, target.Height / height);
            canvas.ClipRect(SKRect.Create(width, height));
            using var white = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(SKRect.Create(width, height), white);
            SKImage? panel = isBack ? back : front;
            if (panel is not null)
            {
                // Aspect-fit preserves the imported artwork without stretching.
                float scale = Math.Min(width / panel.Width, height / panel.Height);
                float w = panel.Width * scale;
                float h = panel.Height * scale;
                canvas.DrawImage(panel, SKRect.Create((width - w) / 2, (height - h) / 2, w, h),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }
            if (!isBack) return;
            SKRect qrBounds = SKRect.Create((float)template.QrLeft * width, (float)template.QrTop * height,
                (float)template.QrWidth * width, (float)template.QrWidth * width);
            DrawQr(canvas, qrBounds, url!);
            SKRect dateBounds = SKRect.Create((float)template.DateLeft * width, (float)template.DateTop * height,
                (float)template.DateWidth * width, (float)template.DateHeight * height);
            DrawDate(canvas, dateBounds, expiry);
            if (guides)
            {
                using var guide = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = .6f, Color = SKColors.DodgerBlue };
                canvas.DrawRect(qrBounds, guide);
                guide.Color = SKColors.DarkOrange;
                canvas.DrawRect(dateBounds, guide);
            }
        }
        finally { canvas.Restore(); }
    }

    private void DrawQr(SKCanvas canvas, SKRect bounds, string url)
    {
        using QRCodeData qr = PromoCodes.CreateQr(url);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = false };
        canvas.DrawRect(bounds, paint);
        paint.Color = SKColor.Parse(template.QrColor);
        // QRCoder's ModuleMatrix includes the four-module quiet zone on each side.
        int count = qr.ModuleMatrix.Count;
        using var path = new SKPath();
        for (int row = 0; row < count; row++)
        for (int col = 0; col < count; col++)
            if (qr.ModuleMatrix[row][col])
                path.AddRect(new SKRect(bounds.Left + col * bounds.Width / count,
                    bounds.Top + row * bounds.Height / count,
                    bounds.Left + (col + 1) * bounds.Width / count,
                    bounds.Top + (row + 1) * bounds.Height / count));
        canvas.DrawPath(path, paint);
    }

    private void DrawDate(SKCanvas canvas, SKRect bounds, DateTime expiry)
    {
        string text = template.FormatDate(expiry);
        using var font = new SKFont(typeface, (float)template.DateFontSize);
        using var paint = new SKPaint { Color = SKColor.Parse(template.DateColor), IsAntialias = true };
        float textWidth = font.MeasureText(text, out _, paint);
        SKFontMetrics metrics = font.Metrics;
        float fit = Math.Min(1, Math.Min(bounds.Width / Math.Max(1, textWidth), bounds.Height / (metrics.Descent - metrics.Ascent)));
        font.Size *= fit;
        metrics = font.Metrics;
        float x = template.DateAlignment switch
        {
            DateAlignment.Left => bounds.Left,
            DateAlignment.Right => bounds.Right,
            _ => bounds.MidX
        };
        SKTextAlign alignment = template.DateAlignment switch
        {
            DateAlignment.Left => SKTextAlign.Left,
            DateAlignment.Right => SKTextAlign.Right,
            _ => SKTextAlign.Center
        };
        float baseline = bounds.MidY - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(text, x, baseline, alignment, font, paint);
    }

    private void DrawCropMarks(SKCanvas canvas, bool isBack)
    {
        PrintLayout layout = template.Layout;
        PrintRect[] cells = Enumerable.Range(0, layout.Capacity).Select(layout.FrontBounds)
            .Select(r => isBack ? layout.BackBounds(r) : r).ToArray();
        double left = cells.Min(r => r.X), right = cells.Max(r => r.X + r.Width);
        double top = cells.Min(r => r.Y), bottom = cells.Max(r => r.Y + r.Height);
        using var paint = new SKPaint { Color = SKColors.Gray, StrokeWidth = .25f, IsAntialias = true };
        foreach (double x in cells.SelectMany(r => new[] { r.X, r.X + r.Width }).Distinct())
        {
            canvas.DrawLine(Mm(x), Mm(top - 3), Mm(x), Mm(top - 1), paint);
            canvas.DrawLine(Mm(x), Mm(bottom + 1), Mm(x), Mm(bottom + 3), paint);
        }
        foreach (double y in cells.SelectMany(r => new[] { r.Y, r.Y + r.Height }).Distinct())
        {
            canvas.DrawLine(Mm(left - 3), Mm(y), Mm(left - 1), Mm(y), paint);
            canvas.DrawLine(Mm(right + 1), Mm(y), Mm(right + 3), Mm(y), paint);
        }
    }

    private static float Mm(double value) => (float)(value * PrintLayout.PointsPerMm);
    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        front?.Dispose();
        back?.Dispose();
        typeface.Dispose();
    }
}
