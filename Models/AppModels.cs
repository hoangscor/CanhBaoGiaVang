using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GoldPriceAlertWinForms.Models
{
    public enum PriceUnit
    {
        Ounce = 0,
        Gram = 1
    }

    public sealed class PriceQuote
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string Currency { get; set; } = "USD"; // display currency
        public string Unit { get; set; } = "oz";      // provider is per troy ounce
        public double Price { get; set; }             // price per oz in display currency
        public string Source { get; set; } = "";
    }

    public sealed class PriceHistoryRow
    {
        public DateTime TimeLocal { get; set; }
        public string Currency { get; set; } = "";
        public string Unit { get; set; } = "";
        public double Price { get; set; }
        public double? Delta { get; set; }
        public double? DeltaPercent { get; set; }
        public string Source { get; set; } = "";
    }

    public sealed class MetalPriceApiSettings
    {
        public string ApiKey { get; set; } = "";
        public string Region { get; set; } = "US"; // US or EU
    }

    public sealed class AlertSettings
    {
        public bool EnableMin { get; set; } = false;
        public double MinPrice { get; set; } = 0;

        public bool EnableMax { get; set; } = false;
        public double MaxPrice { get; set; } = 0;

        public bool EnableDropAbs { get; set; } = true;  // alert if decrease >= DropAbs
        public double DropAbs { get; set; } = 20;

        public bool EnableDropPct { get; set; } = false; // alert if decrease >= DropPct
        public double DropPct { get; set; } = 0;

        public int CooldownMinutes { get; set; } = 60;
    }

    public sealed class EmailSettings
    {
        public bool Enabled { get; set; } = false;

        public string FromAddress { get; set; } = "";
        public string FromDisplayName { get; set; } = "Gold Alert";
        public string AppPassword { get; set; } = ""; // Gmail App Password

        public string ToAddresses { get; set; } = "";

        public string SmtpHost { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;

        public string SubjectTemplate { get; set; } = "ALERT: Gold {reason} ({currency})";

        public string BodyTemplate { get; set; } =
@"Gold Price Alert

Reason: {reason}
Time: {time}
Price: {price} {currency}/{unit}
Delta: {deltaAbs} ({deltaPct}%)

Rules:
- Min: {min}
- Max: {max}
- DropAbs: {dropAbs}
- DropPct: {dropPct}

Source: {source}
";
    }

    public sealed class RemoteConfigSettings
    {
        public bool Enabled { get; set; } = true;
        public bool AutoLoadOnStart { get; set; } = true;
        public bool AutoLoadEachPoll { get; set; } = false;

        // Web thật: Postman Echo trả JSON có args. :contentReference[oaicite:5]{index=5}
        public string Url { get; set; } =
            "https://postman-echo.com/get?pollMin=1&base=USD&display=USD&unit=ounce&dropAbs=5&cooldownMin=2&to=receiver@gmail.com&enableDropAbs=true&region=US&metalApiKey=YOUR_METALPRICEAPI_KEY";
    }

    public sealed class AppSettings
    {
        // Chỉ cần sửa các mục này trong settings.json là chạy
        public MetalPriceApiSettings Metal { get; set; } = new MetalPriceApiSettings();

        public string BaseCurrency { get; set; } = "USD";
        public string DisplayCurrency { get; set; } = "USD";
        public PriceUnit DisplayUnit { get; set; } = PriceUnit.Ounce;

        public int PollMinutes { get; set; } = 60;
        public int HistoryMaxPoints { get; set; } = 200;

        public bool AutoStart { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool DarkMode { get; set; } = false;
        public bool SoundOnAlert { get; set; } = true;

        public AlertSettings Alert { get; set; } = new AlertSettings();
        public EmailSettings Email { get; set; } = new EmailSettings();
        public RemoteConfigSettings RemoteConfig { get; set; } = new RemoteConfigSettings();
    }

    public sealed class RemoteConfig
    {
        public int? PollMinutes { get; set; }
        public string? BaseCurrency { get; set; }
        public string? DisplayCurrency { get; set; }
        public string? Unit { get; set; }

        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? DropAbs { get; set; }
        public double? DropPct { get; set; }
        public int? CooldownMinutes { get; set; }

        public bool? EnableMin { get; set; }
        public bool? EnableMax { get; set; }
        public bool? EnableDropAbs { get; set; }
        public bool? EnableDropPct { get; set; }

        public string? ToAddresses { get; set; }
        public string? MetalApiKey { get; set; }
        public string? Region { get; set; }
    }
}
