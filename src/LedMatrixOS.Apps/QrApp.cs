using System.Text;
using LedMatrixOS.Core;
using LedMatrixOS.Core.Settings;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Microsoft.Extensions.Configuration;

namespace LedMatrixOS.Apps;

/// <summary>
/// A QR code beside a label. In <c>Text</c> mode it encodes the Text / URL setting; in <c>WiFi</c> mode (also the <c>wifi</c> alias) it builds the
/// standard <c>WIFI:T:WPA;S:ssid;P:password;;</c> payload that phone cameras offer to join. The Wi-Fi password is configuration only
/// (<c>Qr:WifiPassword</c>, picked up on activation) so it can never be read back through the settings API.
/// </summary>
public sealed class QrApp : WidgetApp
{
    private const int MaxLine = 40;

    private string _password = "";
    private string _payload = "";
    private string? _shownLabel, _shownCaption;
    private bool _shownInvert, _shownWifi;
    private string? _shownSsid, _shownSecurity, _shownPassword, _shownText;

    private QrCode _code = null!;
    private Label _title = null!;
    private MarqueeLabel _caption = null!;
    private Label _hint = null!;

    public override string Id => "qr";
    public override string Name => "QR Code";

    [Setting("Mode", Description = "Encode plain text or a URL, or a Wi-Fi network to join.", Options = ["Text", "WiFi"])]
    public string Mode { get; set; } = "Text";

    [Setting("Text / URL", Description = "What the code contains (Text mode).")]
    public string Text { get; set; } = "https://example.com";

    [Setting("Label", Description = "Heading shown beside the code.")]
    public string Label { get; set; } = "Scan me";

    [Setting("Network Name", Description = "Wi-Fi network name (WiFi mode). The password is the Qr:WifiPassword configuration value.")]
    public string Ssid { get; set; } = "";

    [Setting("Security", Description = "Wi-Fi security type (WiFi mode).", Options = ["WPA", "WEP", "None"])]
    public string Security { get; set; } = "WPA";

    [Setting("Error Correction", Description = "Higher survives more damage but needs a bigger code.", Options = ["M", "L"])]
    public string Correction { get; set; } = "M";

    [Setting("Invert", Description = "Light modules on a dark background (some scanners cannot read this).")]
    public bool Invert { get; set; }

    /// <summary>The string currently encoded (tests and diagnostics).</summary>
    public string Payload => _payload;

    public override async Task OnActivatedAsync((int height, int width) dimensions, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await base.OnActivatedAsync(dimensions, configuration, cancellationToken);
        _password = configuration["Qr:WifiPassword"] ?? "";
    }

    /// <summary>Sets the Wi-Fi password without a configuration source (tests).</summary>
    internal void UseWifiPassword(string password) => _password = password;

    /// <summary>The <c>WIFI:</c> payload for a network, with the characters the format reserves escaped.</summary>
    public static string WifiPayload(string ssid, string password, string security)
    {
        bool open = security.Equals("None", StringComparison.OrdinalIgnoreCase) || password.Length == 0;
        var sb = new StringBuilder("WIFI:T:");
        sb.Append(open ? "nopass" : security.ToUpperInvariant()).Append(";S:");
        Escape(sb, ssid);
        if (!open)
        {
            sb.Append(";P:");
            Escape(sb, password);
        }
        return sb.Append(";;").ToString();
    }

    private static void Escape(StringBuilder sb, string value)
    {
        foreach (char c in value)
        {
            if (c is '\\' or ';' or ',' or ':' or '"') sb.Append('\\');
            sb.Append(c);
        }
    }

    protected override Node Build()
    {
        var title = new TextStyle(Fonts.Small, new Pixel(255, 255, 255), Shadow: false);
        var muted = new TextStyle(Fonts.QuiteSmall, new Pixel(150, 160, 190), Shadow: false);

        _code = new QrCode { Width = 64, Height = 64, HAlign = Align.Start, VAlign = Align.Center };
        _title = new Label("") { Style = title };
        _caption = new MarqueeLabel("") { Style = muted, HAlign = Align.Stretch };
        _hint = new Label("") { Style = muted };
        _shownLabel = _shownCaption = _shownText = null;
        _payload = "";

        var side = new Stack(Orientation.Vertical, gap: 4)
        {
            VAlign = Align.Center,
            Padding = new Thickness(6, 0, 4, 0),
            Children = { _title, _caption, _hint },
        };
        return new Dock { Left = _code, Fill = side, HAlign = Align.Stretch, VAlign = Align.Stretch };
    }

    public override void Update(FrameContext context, CancellationToken cancellationToken)
    {
        _ = Host;   // builds the tree on the first frame
        bool wifi = Mode.Equals("WiFi", StringComparison.OrdinalIgnoreCase);
        var ecc = Correction == "L" ? QrEcc.L : QrEcc.M;

        // Rebuilding strings and the matrix happens only when an input changed, so a steady frame allocates nothing.
        // (Comparing the Wi-Fi payload would allocate, so the inputs are compared instead.)
        if (wifi != _shownWifi || (wifi ? (Ssid != _shownSsid || Security != _shownSecurity || _password != _shownPassword) : Text != _shownText) || ecc != _code.Ecc)
        {
            _shownWifi = wifi;
            _shownSsid = Ssid;
            _shownSecurity = Security;
            _shownPassword = _password;
            _shownText = Text;
            _payload = wifi ? (Ssid.Length == 0 ? "" : WifiPayload(Ssid, _password, Security)) : Text;
            _code.Ecc = ecc;
            _code.Text = _payload;
            // Spec says 4 modules of quiet zone; trade down when it buys a larger integer scale on a 64 pixel tall display.
            int size = _code.Matrix?.Size ?? 0;
            _code.QuietZone = size == 0 ? 2 : (64 / (size + 4) < 64 / (size + 2) ? 1 : 2);
            _hint.Text = _code.Matrix is null ? (_payload.Length == 0 ? "" : "TOO LONG") : wifi ? "Join Wi-Fi" : "";
            _hint.Visible = _hint.Text.Length > 0;
        }

        if (Invert != _shownInvert || _shownLabel is null)
        {
            _shownInvert = Invert;
            _code.On = Invert ? Pixel.White : Pixel.Black;
            _code.Off = Invert ? Pixel.Black : Pixel.White;
        }

        string label = Label ?? "";
        if (label != _shownLabel)
        {
            _shownLabel = label;
            _title.Text = label;
            _title.Visible = label.Length > 0;
        }

        string caption = wifi ? (Ssid.Length == 0 ? "No network name set" : Ssid) : Text;
        if (caption != _shownCaption)
        {
            _shownCaption = caption;
            _caption.Text = caption.Length > MaxLine * 4 ? caption[..(MaxLine * 4)] : caption;
        }

        base.Update(context, cancellationToken);
    }
}
