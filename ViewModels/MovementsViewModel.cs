#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ULTRA.Helpers;
using ULTRA.Services;
using ULTRA.ViewModels.Base;

namespace ULTRA.ViewModels
{
    public sealed class MovementsViewModel : ObservableObject
    {
        public sealed class Row : ObservableObject
        {
            public DateTime Date { get; set; }
            public string KindRaw { get; set; } = "";
            public string OrderNo { get; set; } = "";
            public decimal Price { get; set; }
            public string Memo { get; set; } = "";
            public string PartnerCode { get; set; } = "";
            public string PartnerName { get; set; } = "";

            public ICommand OpenDetailCommand { get; internal set; }

            public string KindText
            {
                get
                {
                    var s = (KindRaw ?? "").Trim().ToUpperInvariant();
                    return s switch
                    {
                        "1" or "IN" or "입고" => "입고",
                        "2" or "OUT" or "출고" => "출고",
                        _ => string.IsNullOrWhiteSpace(KindRaw) ? "" : KindRaw
                    };
                }
            }

            public Brush KindBrush =>
                KindText == "입고" ? new SolidColorBrush(Color.FromRgb(96, 165, 250)) :
                KindText == "출고" ? new SolidColorBrush(Color.FromRgb(248, 113, 113)) :
                                         new SolidColorBrush(Color.FromRgb(203, 213, 225));

            public string PriceDisplay =>
                string.Format(CultureInfo.GetCultureInfo("ko-KR"), "{0:N0} 원", Price);
        }

        public ObservableCollection<Row> Items { get; } = new();
        public ListCollectionView ItemsView { get; private set; }
        public IReadOnlyList<string> TypeOptions { get; } = new[] { "전체", "입고", "출고" };

        private DateTime _from = DateTime.Today.AddDays(-7);
        public DateTime From { get => _from; set { if (Set(ref _from, value)) ItemsView?.Refresh(); } }

        private DateTime _to = DateTime.Today;
        public DateTime To { get => _to; set { if (Set(ref _to, value)) ItemsView?.Refresh(); } }

        private string _kindFilter = "전체";
        public string KindFilter { get => _kindFilter; set { if (Set(ref _kindFilter, value)) ItemsView?.Refresh(); } }

        private string _partnerCodeFilter = "";
        public string PartnerCodeFilter { get => _partnerCodeFilter; private set { if (Set(ref _partnerCodeFilter, value)) ItemsView?.Refresh(); } }

        private string _partnerNameFilter = "";
        public string PartnerNameFilter { get => _partnerNameFilter; private set { if (Set(ref _partnerNameFilter, value)) ItemsView?.Refresh(); } }

        public ICommand SortRecentCommand { get; }
        public ICommand SortPriceAscCommand { get; }
        public ICommand SortPriceDescCommand { get; }
        public ICommand RefreshCommand { get; }

        private readonly string _csvPath;
        private readonly INavigationService _navigationService;

        public MovementsViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;

            _csvPath = ResolveCsv(new[]
            {
                "ULTRA-입출고내역.csv", "ULTRA - 입출고내역.csv", "ULTRA-입출고내역-sample.csv",
                "ULTRA - 입출고내역 (샘플).csv", "ULTRA-거래내역.csv", "ULTRA - 거래내역.csv", "ULTRA-Movements.csv"
            }) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ULTRA-입출고내역.csv");

            Build();

            ItemsView = (ListCollectionView)CollectionViewSource.GetDefaultView(Items);
            ItemsView.Filter = Filter;
            ItemsView.SortDescriptions.Clear();
            ItemsView.SortDescriptions.Add(new SortDescription(nameof(Row.Date), ListSortDirection.Descending));
            Raise(nameof(ItemsView));

            SortRecentCommand = new RelayCommand(() => ApplySort(nameof(Row.Date), true));
            SortPriceAscCommand = new RelayCommand(() => ApplySort(nameof(Row.Price), false));
            SortPriceDescCommand = new RelayCommand(() => ApplySort(nameof(Row.Price), true));
            RefreshCommand = new RelayCommand(() => { Build(); ItemsView?.Refresh(); });
        }

        public MovementsViewModel(INavigationService navigationService, string partnerCode, string partnerName = "") : this(navigationService)
        {
            ApplyPartnerFilter(partnerCode, partnerName);
        }

        public void ApplyPartnerFilter(string partnerCode, string partnerName = "")
        {
            PartnerCodeFilter = partnerCode ?? "";
            PartnerNameFilter = partnerName ?? "";
        }

        private void ApplySort(string property, bool descending)
        {
            ItemsView.SortDescriptions.Clear();
            ItemsView.SortDescriptions.Add(new SortDescription(property, descending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        }

        private void OpenDetail(Row row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.OrderNo)) return;

            // 상세 페이지로 네비게이션
            _navigationService.Navigate(new MovementsDetailViewModel(_navigationService, row.OrderNo));
        }

        private ICommand MakeOpenDetail(Row r) => new RelayCommand(() => OpenDetail(r));


        private void Build()
        {
            try
            {
                var rows = LoadRaw().ToList();
                var grouped = GroupByOrder(rows).OrderByDescending(x => x.Date).ToList();

                Items.Clear();
                foreach (var g in grouped)
                {
                    g.OpenDetailCommand = MakeOpenDetail(g);
                    Items.Add(g);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("입출고 CSV 로드 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool Filter(object obj)
        {
            if (obj is not Row m) return false;

            if (m.Date.Date < From.Date || m.Date.Date > To.Date) return false;

            if (!string.Equals(KindFilter, "전체", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(m.KindText, KindFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(PartnerCodeFilter) &&
                !string.Equals(m.PartnerCode ?? "", PartnerCodeFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        #region CSV Utilities
        private sealed class Raw { public DateTime Date; public string Kind; public string OrderNo; public decimal Amount; public int Qty; public decimal Wholesale; public decimal Retail; public decimal Option; public string Memo; public string PartnerCode; public string PartnerName; }

        private IEnumerable<Raw> LoadRaw()
        {
            var list = new List<Raw>();
            if (!File.Exists(_csvPath)) return list;
            foreach (var r in CsvRead(_csvPath))
            {
                try
                {
                    list.Add(new Raw
                    {
                        Date = ParseDateTime(r.GetAnyOrEmpty("일자", "date"), r.GetAnyOrEmpty("시간", "time", "시간 (HH:mm:ss)")),
                        Kind = r.GetAnyOrEmpty("구분", "입고/출고", "type", "kind"),
                        OrderNo = r.GetAnyOrEmpty("주문번호", "orderno", "no"),
                        Amount = ParseMoney(r.GetAnyOrEmpty("총금액", "가격", "금액", "amount", "price", "total")),
                        Qty = ParseInt(r.GetAnyOrEmpty("수량", "qty")),
                        Wholesale = ParseMoney(r.GetAnyOrEmpty("도매가격", "도매", "wholesale", "wholesale_price")),
                        Retail = ParseMoney(r.GetAnyOrEmpty("소매가격", "소매", "retail", "retail_price")),
                        Option = ParseMoney(r.GetAnyOrEmpty("옵션가", "option")),
                        Memo = r.GetAnyOrEmpty("메모", "memo", "비고"),
                        PartnerCode = r.GetAnyOrEmpty("거래처코드", "partner", "partnercode", "code"),
                        PartnerName = r.GetAnyOrEmpty("거래처명", "partnername", "name")
                    });
                }
                catch { }
            }
            return list;
        }

        private IEnumerable<Row> GroupByOrder(IEnumerable<Raw> raws)
        {
            var dict = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in raws)
            {
                if (r.Date == DateTime.MinValue) continue;
                if (string.IsNullOrWhiteSpace(r.OrderNo)) continue;

                var amount = r.Amount;
                if (amount == 0m)
                {
                    var k = NormalizeKind(r.Kind);
                    if (k == "IN")
                        amount = r.Qty * (r.Wholesale != 0 ? r.Wholesale : r.Retail);
                    else if (k == "OUT")
                        amount = r.Qty * (r.Retail != 0 ? r.Retail : r.Wholesale) + (r.Qty * r.Option);
                }

                if (!dict.TryGetValue(r.OrderNo, out var row))
                {
                    row = new Row
                    {
                        Date = r.Date,
                        KindRaw = r.Kind,
                        OrderNo = r.OrderNo,
                        Price = 0m,
                        Memo = r.Memo ?? "",
                        PartnerCode = r.PartnerCode ?? "",
                        PartnerName = r.PartnerName ?? ""
                    };
                    dict[r.OrderNo] = row;
                }

                if (r.Date > row.Date) row.Date = r.Date;
                if (!string.IsNullOrWhiteSpace(r.Kind)) row.KindRaw = r.Kind;
                if (string.IsNullOrWhiteSpace(row.Memo) && !string.IsNullOrWhiteSpace(r.Memo)) row.Memo = r.Memo;
                if (string.IsNullOrWhiteSpace(row.PartnerCode)) row.PartnerCode = r.PartnerCode ?? "";
                if (string.IsNullOrWhiteSpace(row.PartnerName)) row.PartnerName = r.PartnerName ?? "";

                row.Price += amount;
            }

            var values = dict.Values.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(PartnerCodeFilter))
                values = values.Where(v => string.Equals(v.PartnerCode ?? "", PartnerCodeFilter, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(PartnerNameFilter))
                values = values.Where(v => string.Equals(v.PartnerName ?? "", PartnerNameFilter, StringComparison.OrdinalIgnoreCase));

            return values;
        }

        private static string NormalizeKind(string kind)
        {
            var s = (kind ?? "").Trim().ToUpperInvariant();
            if (s is "1" or "IN" or "입고") return "IN";
            if (s is "2" or "OUT" or "출고") return "OUT";
            return "";
        }

        private static string ResolveCsv(IEnumerable<string> candidates)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var name in candidates)
            {
                var p1 = Path.Combine(baseDir, "Data", name);
                if (File.Exists(p1)) return p1;
                var p2 = Path.Combine(baseDir, name);
                if (File.Exists(p2)) return p2;
            }
            return null;
        }

        private static IEnumerable<Dictionary<string, string>> CsvRead(string path)
        {
            using var sr = new StreamReader(path, DetectEncoding(path));
            var header = sr.ReadLine();
            if (header == null) yield break;
            var cols = SplitCsv(header);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                var vals = SplitCsv(line);
                if (vals.Count < cols.Count) vals.AddRange(Enumerable.Repeat("", cols.Count - vals.Count));
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cols.Count; i++)
                {
                    var key = cols[i].Trim();
                    var val = i < vals.Count ? vals[i] : "";
                    dict[key] = val;
                }
                yield return dict;
            }
        }

        private static Encoding DetectEncoding(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            if (fs.Length >= 3)
            {
                var bom3 = new byte[3]; fs.Read(bom3, 0, 3);
                if (bom3[0] == 0xEF && bom3[1] == 0xBB && bom3[2] == 0xBF) return new UTF8Encoding(true);
            }
            fs.Seek(0, SeekOrigin.Begin);
            if (fs.Length >= 2)
            {
                var bom2 = new byte[2]; fs.Read(bom2, 0, 2);
                if (bom2[0] == 0xFF && bom2[1] == 0xFE) return Encoding.Unicode;
                if (bom2[0] == 0xFE && bom2[1] == 0xFF) return Encoding.BigEndianUnicode;
            }
            try { return Encoding.GetEncoding(949); } catch { return Encoding.Default; }
        }

        private static List<string> SplitCsv(string line)
        {
            var res = new List<string>();
            bool inQ = false; var cur = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQ && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQ = !inQ;
                }
                else if (ch == ',' && !inQ) { res.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            res.Add(cur.ToString());
            return res;
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Replace(",", "").Trim();
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private static decimal ParseMoney(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            var filtered = new string((s ?? "").Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
            if (string.IsNullOrWhiteSpace(filtered)) return 0m;
            return decimal.TryParse(filtered,
                NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var v) ? v : 0m;
        }

        private static DateTime ParseDateTime(string date, string time)
        {
            if (string.IsNullOrWhiteSpace(date)) return DateTime.MinValue;
            var d = date.Trim().Replace(".", "-").Replace("/", "-");
            if (string.IsNullOrWhiteSpace(time)) time = "00:00:00";
            if (DateTime.TryParse($"{d} {time.Trim()}", CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
                return dt;
            var parts = d.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var mm) && int.TryParse(parts[1], out var dd))
            {
                var y = DateTime.Today.Year;
                if (DateTime.TryParse($"{y:D4}-{mm:D2}-{dd:D2} {time}", out var dt2)) return dt2;
            }
            return DateTime.MinValue;
        }
        #endregion
    }
}