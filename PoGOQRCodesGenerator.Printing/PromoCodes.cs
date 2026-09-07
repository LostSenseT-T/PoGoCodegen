using System.Text.RegularExpressions;
using QRCoder;

namespace PoGOQRCodesGenerator.Printing;

public static class PromoCodes
{
    public const string DefaultUrlTemplate = "https://store.pokemongo.com/offer-redemption?passcode={code}";

    public static List<string> Parse(string? text) => Regex.Split(text ?? string.Empty, @"[,\s]+")
        .Where(code => code.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    public static string CreateUrl(string template, string code)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{code}", StringComparison.Ordinal))
            throw new ArgumentException("The URL template must contain {code}.");
        string url = template.Trim().Replace("{code}", Uri.EscapeDataString(code), StringComparison.Ordinal);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Enter a valid HTTP or HTTPS URL template.");
        return url;
    }

    public static QRCodeData CreateQr(string url) => QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q);
}
