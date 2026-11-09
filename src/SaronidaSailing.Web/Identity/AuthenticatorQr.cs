using System.Drawing;
using QRCoder;

namespace SaronidaSailing.Web.Identity;

public static class AuthenticatorQr
{
    public const string Issuer = "Saronida Sailing";

    public static string BuildUri(string email, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString($"{Issuer}:{email}")}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

    public static string RenderSvg(string uri, string label)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(4, Color.Black, Color.White, true, SvgQRCode.SizingMode.ViewBoxAttribute);
        var attributes = $"<svg role=\"img\" aria-label=\"{System.Net.WebUtility.HtmlEncode(label)}\"";
        return svg.Replace("<svg", attributes, StringComparison.Ordinal);
    }

    public static string FormatKey(string key) =>
        string.Join(' ', key.ToUpperInvariant().Chunk(4).Select(chunk => new string(chunk)));
}
