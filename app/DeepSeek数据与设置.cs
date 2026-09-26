// 独立 Windows 悬浮挂件：不依赖 DSH。余额接口实现见同包的 DeepSeek余额查询.cs。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DeepSeekBalanceViewer;

namespace DeepSeekWhaleStandalone
{
    public sealed class WhaleSettings
    {
        public string ProtectedKey { get; set; }
        public decimal AlertThreshold { get; set; }
        public decimal BudgetThreshold { get; set; }
        public int RefreshSeconds { get; set; }
        public int ScalePercent { get; set; }
        public int TextSize { get; set; }
        public bool HideDataPanel { get; set; }
        public string PetPose { get; set; }
        public bool AutoPose { get; set; }
        public bool LockPosition { get; set; }
        public bool HideInput { get; set; }
        public bool QuietMode { get; set; }
        public int TargetFps { get; set; }
        public string ModelPath { get; set; }
        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public string LedgerDate { get; set; }
        public Dictionary<string, string> Previous { get; set; }
        public Dictionary<string, string> Spent { get; set; }
        public Dictionary<string, Dictionary<string, string>> UsageDays { get; set; }

        public WhaleSettings()
        {
            ProtectedKey = "";
            WindowX = Int32.MinValue;
            WindowY = Int32.MinValue;
            LedgerDate = "";
            RefreshSeconds = 60;
            ScalePercent = 100;
            TextSize = 14;
            PetPose = "sit";
            AutoPose = true;
            TargetFps = 120;
            ModelPath = "builtin";
            Previous = new Dictionary<string, string>();
            Spent = new Dictionary<string, string>();
            UsageDays = new Dictionary<string, Dictionary<string, string>>();
        }
    }

    public static class SettingsStore
    {
        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekWhaleStandalone", "settings.json"); }
        }

        public static WhaleSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new WhaleSettings();
                WhaleSettings result = new JavaScriptSerializer().Deserialize<WhaleSettings>(
                    File.ReadAllText(FilePath, Encoding.UTF8));
                return result ?? new WhaleSettings();
            }
            catch (Exception) { return new WhaleSettings(); }
        }

        public static void Save(WhaleSettings settings)
        {
            string directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(settings), Encoding.UTF8);
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }

        public static string ProtectKey(string key)
        {
            byte[] source = Encoding.UTF8.GetBytes(key);
            try
            {
                return Convert.ToBase64String(ProtectedData.Protect(source, null,
                    DataProtectionScope.CurrentUser));
            }
            finally { Array.Clear(source, 0, source.Length); }
        }

        public static string UnprotectKey(string protectedKey)
        {
            if (String.IsNullOrWhiteSpace(protectedKey)) return "";
            try
            {
                byte[] source = ProtectedData.Unprotect(Convert.FromBase64String(protectedKey),
                    null, DataProtectionScope.CurrentUser);
                try { return Encoding.UTF8.GetString(source); }
                finally { Array.Clear(source, 0, source.Length); }
            }
            catch (Exception) { return ""; }
        }
    }

    public sealed class UsageLedger
    {
        public DateTime Day { get; private set; }
        public Dictionary<string, decimal> Previous { get; private set; }
        public Dictionary<string, decimal> Spent { get; private set; }
        public Dictionary<string, Dictionary<string, decimal>> History { get; private set; }

        public UsageLedger()
        {
            Previous = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            Spent = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            History = new Dictionary<string, Dictionary<string, decimal>>();
        }

        public static UsageLedger FromSettings(WhaleSettings settings)
        {
            UsageLedger ledger = new UsageLedger();
            DateTime day;
            if (!DateTime.TryParseExact(settings.LedgerDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) return ledger;
            ledger.Day = day.Date;
            ReadAmounts(settings.Previous, ledger.Previous);
            ReadAmounts(settings.Spent, ledger.Spent);
            if (settings.UsageDays != null)
                foreach (KeyValuePair<string, Dictionary<string, string>> dayEntry in settings.UsageDays)
                {
                    Dictionary<string, decimal> amounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                    ReadAmounts(dayEntry.Value, amounts);
                    ledger.History[dayEntry.Key] = amounts;
                }
            return ledger;
        }

        private static void ReadAmounts(Dictionary<string, string> source, Dictionary<string, decimal> target)
        {
            if (source == null) return;
            foreach (KeyValuePair<string, string> pair in source)
            {
                decimal amount;
                if (Decimal.TryParse(pair.Value, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out amount)) target[pair.Key] = amount;
            }
        }

        public void WriteTo(WhaleSettings settings)
        {
            settings.LedgerDate = Day == DateTime.MinValue ? "" :
                Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            settings.Previous = WriteAmounts(Previous);
            settings.Spent = WriteAmounts(Spent);
            settings.UsageDays = new Dictionary<string, Dictionary<string, string>>();
            foreach (KeyValuePair<string, Dictionary<string, decimal>> dayEntry in History)
                settings.UsageDays[dayEntry.Key] = WriteAmounts(dayEntry.Value);
        }

        private static Dictionary<string, string> WriteAmounts(Dictionary<string, decimal> source)
        {
            Dictionary<string, string> target = new Dictionary<string, string>();
            foreach (KeyValuePair<string, decimal> pair in source)
                target[pair.Key] = pair.Value.ToString(CultureInfo.InvariantCulture);
            return target;
        }

        public void Observe(BalanceResult result, DateTime observedAt)
        {
            if (Day != observedAt.Date)
            {
                Day = observedAt.Date;
                Previous.Clear();
                Spent.Clear();
            }
            foreach (BalanceInfo item in result.Items)
            {
                decimal balance;
                if (!Decimal.TryParse(item.Total, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out balance)) continue;
                decimal previous;
                if (Previous.TryGetValue(item.Currency, out previous) && balance < previous)
                {
                    decimal spent;
                    Spent.TryGetValue(item.Currency, out spent);
                    Spent[item.Currency] = spent + previous - balance;
                }
                Previous[item.Currency] = balance;
            }
            string dateKey = Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!History.ContainsKey(dateKey))
                History[dateKey] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, decimal> entry in Spent)
                History[dateKey][entry.Key] = entry.Value;
            List<string> remove = new List<string>();
            foreach (string key in History.Keys)
            {
                DateTime historyDay;
                if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out historyDay) || historyDay < Day.AddDays(-29)) remove.Add(key);
            }
            foreach (string key in remove) History.Remove(key);
        }

        public decimal Used(string currency)
        {
            decimal amount;
            return Spent.TryGetValue(currency, out amount) ? amount : 0m;
        }

        public decimal UsedOn(string currency, DateTime date)
        {
            Dictionary<string, decimal> amounts;
            decimal amount;
            string key = date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return History.TryGetValue(key, out amounts) && amounts.TryGetValue(currency, out amount)
                ? amount : 0m;
        }

        public void Reset()
        {
            Day = DateTime.MinValue;
            Previous.Clear();
            Spent.Clear();
            History.Clear();
        }
    }

}

