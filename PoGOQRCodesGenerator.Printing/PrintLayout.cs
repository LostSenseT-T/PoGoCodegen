namespace PoGOQRCodesGenerator.Printing;

public enum FlipEdge { LongEdge, ShortEdge }
public enum PdfExportMode { Duplex, Separate }
public readonly record struct PrintRect(double X, double Y, double Width, double Height);
public readonly record struct CardPlacement(int CodeIndex, PrintRect Bounds);

public sealed record PrintPage(bool IsBack, int SheetIndex, IReadOnlyList<CardPlacement> Cards);

public sealed record PrintLayout
{
    public const double PageWidthMm = 210;
    public const double PageHeightMm = 297;
    public const double PointsPerMm = 72 / 25.4;
    public int Columns { get; init; } = 4;
    public int Rows { get; init; } = 4;
    public double CardWidthMm { get; init; } = 45;
    public double CardHeightMm => CardWidthMm * 4 / 3;
    public double GapMm { get; init; } = 2;
    public FlipEdge FlipEdge { get; init; } = FlipEdge.LongEdge;
    // Calibration is measured on the back page as viewed in the PDF.
    public double BackOffsetXmm { get; init; }
    public double BackOffsetYmm { get; init; }
    public bool CropMarks { get; init; } = true;
    public int Capacity => checked(Columns * Rows);

    public void Validate()
    {
        if (Columns is < 1 or > 12 || Rows is < 1 or > 12)
            throw new ArgumentException("Rows and columns must be between 1 and 12.");
        if (!double.IsFinite(CardWidthMm) || CardWidthMm <= 0 ||
            !double.IsFinite(GapMm) || GapMm < 0 ||
            !double.IsFinite(BackOffsetXmm) || !double.IsFinite(BackOffsetYmm))
            throw new ArgumentException("Enter valid card dimensions, spacing and offsets.");
        if (!Enum.IsDefined(FlipEdge)) throw new ArgumentException("Choose the duplex flip edge.");
        double width = Columns * CardWidthMm + (Columns - 1) * GapMm;
        double height = Rows * CardHeightMm + (Rows - 1) * GapMm;
        double marginX = (PageWidthMm - width) / 2;
        double marginY = (PageHeightMm - height) / 2;
        // Reserve printable margins, including calibration and external cut marks.
        double requiredMargin = CropMarks ? 8 : 5;
        if (marginX - Math.Abs(BackOffsetXmm) < requiredMargin ||
            marginY - Math.Abs(BackOffsetYmm) < requiredMargin)
            throw new ArgumentException("This grid does not fit A4 with printable margins. Reduce card width, spacing, rows or columns.");
    }

    public PrintRect FrontBounds(int slot)
    {
        if (slot < 0 || slot >= Capacity) throw new ArgumentOutOfRangeException(nameof(slot));
        double width = Columns * CardWidthMm + (Columns - 1) * GapMm;
        double height = Rows * CardHeightMm + (Rows - 1) * GapMm;
        return new PrintRect((PageWidthMm - width) / 2 + slot % Columns * (CardWidthMm + GapMm),
            (PageHeightMm - height) / 2 + slot / Columns * (CardHeightMm + GapMm), CardWidthMm, CardHeightMm);
    }

    public PrintRect BackBounds(PrintRect front) => FlipEdge switch
    {
        FlipEdge.LongEdge => front with
        {
            X = PageWidthMm - front.X - front.Width + BackOffsetXmm,
            Y = front.Y + BackOffsetYmm
        },
        FlipEdge.ShortEdge => front with
        {
            X = front.X + BackOffsetXmm,
            Y = PageHeightMm - front.Y - front.Height + BackOffsetYmm
        },
        _ => throw new ArgumentOutOfRangeException(nameof(FlipEdge))
    };

    public IReadOnlyList<PrintPage> CreatePages(int codeCount, PdfExportMode mode)
    {
        Validate();
        if (codeCount <= 0) throw new ArgumentException("Enter at least one promo code.");
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Choose a PDF export mode.");
        var pages = new List<PrintPage>();
        if (mode == PdfExportMode.Separate)
            pages.Add(new PrintPage(false, 0, Enumerable.Range(0, Capacity)
                .Select(slot => new CardPlacement(-1, FrontBounds(slot))).ToArray()));
        int sheets = (codeCount - 1) / Capacity + 1;
        for (int sheet = 0; sheet < sheets; sheet++)
        {
            int first = sheet * Capacity;
            var fronts = Enumerable.Range(0, Math.Min(Capacity, codeCount - first))
                .Select(slot => new CardPlacement(first + slot, FrontBounds(slot))).ToArray();
            if (mode == PdfExportMode.Duplex) pages.Add(new PrintPage(false, sheet, fronts));
            // Reflect each occupied front position. Never reflect the artwork itself,
            // and never compact an incomplete back sheet into different slots.
            pages.Add(new PrintPage(true, sheet, fronts.Select(card => card with { Bounds = BackBounds(card.Bounds) }).ToArray()));
        }
        return pages;
    }
}
