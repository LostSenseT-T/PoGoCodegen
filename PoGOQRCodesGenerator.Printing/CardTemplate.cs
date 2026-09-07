using System.Globalization;
using SkiaSharp;

namespace PoGOQRCodesGenerator.Printing;

public enum DateAlignment { Left, Center, Right }

public sealed record CardTemplate
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "KPI Gang";
    public byte[]? FrontImage { get; init; }
    public byte[]? BackImage { get; init; }
    public byte[]? FontData { get; init; }
    public string FontName { get; init; } = "Open Sans";
    public double QrLeft { get; init; } = .075;
    public double QrTop { get; init; } = .18;
    public double QrWidth { get; init; } = .85;
    public double DateLeft { get; init; } = .05;
    public double DateTop { get; init; } = .825;
    public double DateWidth { get; init; } = .9;
    public double DateHeight { get; init; } = .065;
    public double DateFontSize { get; init; } = 7;
    public string DateColor { get; init; } = "#BDAEB8";
    public string QrColor { get; init; } = "#26133A";
    public string DateFormat { get; init; } = "dd MMM";
    public string DatePrefix { get; init; } = "exp. ";
    public DateAlignment DateAlignment { get; init; } = DateAlignment.Center;
    public PrintLayout Layout { get; init; } = new();

    public string FormatDate(DateTime date) => DatePrefix + date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public void Validate(bool requireImages = true)
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("Enter a template name.");
        if (requireImages && (FrontImage is not { Length: > 0 } || BackImage is not { Length: > 0 }))
            throw new ArgumentException("Load both front and back panel images.");
        if (Layout is null) throw new ArgumentException("Template layout is missing.");
        Layout.Validate();
        ValidateBox(QrLeft, QrTop, QrWidth, QrWidth * 3 / 4, "QR");
        ValidateBox(DateLeft, DateTop, DateWidth, DateHeight, "Date");
        if (!double.IsFinite(DateFontSize) || DateFontSize is < 1 or > 100)
            throw new ArgumentException("Date font size must be between 1 and 100 pt.");
        if (!SKColor.TryParse(DateColor, out _) || !SKColor.TryParse(QrColor, out _))
            throw new ArgumentException("Enter colors as hex values, for example #26133A.");
        if (!Enum.IsDefined(DateAlignment)) throw new ArgumentException("Choose a date alignment.");
        if (string.IsNullOrWhiteSpace(DateFormat)) throw new ArgumentException("Enter a date format, for example dd MMM.");
        try { _ = FormatDate(new DateTime(2026, 11, 30)); }
        catch (FormatException) { throw new ArgumentException("Invalid date format. Use dd MMM, dd MMM yyyy or yyyy-MM-dd."); }
    }

    private static void ValidateBox(double x, double y, double w, double h, string name)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h) ||
            x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > 1.000001 || y + h > 1.000001)
            throw new ArgumentException($"The {name} area must stay inside the card.");
    }
}
