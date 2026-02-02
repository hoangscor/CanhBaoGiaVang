using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using GoldPriceAlertWinForms.Models;

namespace GoldPriceAlertWinForms.Services
{
    public sealed class SettingsStore
    {
        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        public string ConfigPath { get; }
        public string TemplatePath { get; }

        public SettingsStore()
        {
            string dir = AppContext.BaseDirectory; // thư mục exe
            ConfigPath = Path.Combine(dir, "settings.json");
            TemplatePath = Path.Combine(dir, "settings.template.json");
        }

        public AppSettings EnsureAndLoad()
        {
            if (!File.Exists(ConfigPath))
            {
                var sample = new AppSettings
                {
                    AutoStart = false,
                    PollMinutes = 60,
                    BaseCurrency = "USD",
                    DisplayCurrency = "USD",
                    Metal = new MetalPriceApiSettings { ApiKey = "PASTE_METALPRICEAPI_KEY_HERE", Region = "US" },
                    Email = new EmailSettings
                    {
                        Enabled = false,
                        FromAddress = "yourgmail@gmail.com",
                        AppPassword = "PASTE_GMAIL_APP_PASSWORD",
                        ToAddresses = "receiver@gmail.com",
                        SmtpHost = "smtp.gmail.com",
                        SmtpPort = 587,
                        EnableSsl = true
                    }
                };

                File.WriteAllText(TemplatePath, JsonSerializer.Serialize(sample, WriteOptions));
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(sample, WriteOptions));
            }

            return Load();
        }

        public AppSettings Load()
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            string json = JsonSerializer.Serialize(settings, WriteOptions);
            File.WriteAllText(ConfigPath, json);
        }
    }
}