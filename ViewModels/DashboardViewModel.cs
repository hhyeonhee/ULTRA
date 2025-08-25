#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public sealed class DashboardViewModel : ObservableObject
    {
        // ===== 기간 바인딩 =====
        private DateTime _startDate;
        public DateTime StartDate { get => _startDate; set { if (Set(ref _startDate, value)) RecentView?.Refresh(); } }

        private DateTime _endDate;
        public DateTime EndDate { get => _endDate; set { if (Set(ref _endDate, value)) RecentView?.Refresh(); } }

        // ===== KPI =====
        private int _inCount;
        public int InCount { get => _inCount; private set => Set(ref _inCount, value); }

        private int _outCount;
        public int OutCount { get => _outCount; private set => Set(ref _outCount, value); }

        private decimal _salesAmount;
        public string SalesAmountDisplay => FormatCurrency(_salesAmount);

        private decimal _profit;
        public string ProfitDisplay => FormatCurrency(_profit);

        public ObservableCollection<RecentRow> Recent { get; } = new();
        public ListCollectionView RecentView { get; private set; }

        // ===== 명령 =====
        public ICommand RefreshCommand { get; }
        public ICommand OpenDetailCommand { get; }
        public ICommand Range1WCommand { get; }
        public ICommand Range1MCommand { get; }
        public ICommand Range3MCommand { get; }
        public ICommand Range6MCommand { get; }
        public ICommand Range1YCommand { get; }
        public ICommand Range3YCommand { get; }

        private readonly INavigationService _navigationService;

        private static readonly string[] MOVE_FILES =
        {
            "ULTRA - 입출고내역.csv",
            "ULTRA-입출고내역-sample.csv",
            "ULTRA - 입출고내역 (샘플).csv"
        };

        public DashboardViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
            var now = DateTime.Now;
            StartDate = new DateTime(now.Year, now.Month, 1);
            EndDate = now.Date;

           

            RefreshCommand = new RelayCommand(Build);
            OpenDetailCommand = new RelayCommand<RecentRow>(r =>
            {
                if (r is null) return;
                _navigationService.Navigate(new MovementsDetailViewModel(_navigationService, r.OrderNo));
            });

            Range1WCommand = new RelayCommand(() => SetRangeDays(7));
            Range1MCommand = new RelayCommand(() => SetRangeDays(30));
            Range3MCommand = new RelayCommand(() => SetRangeDays(90));
            Range6MCommand = new RelayCommand(() => SetRangeDays(180));
            Range1YCommand = new RelayCommand(() => SetRangeDays(365));
            Range3YCommand = new RelayCommand(() => SetRangeDays(365 * 3));

            RecentView = (ListCollectionView)CollectionViewSource.GetDefaultView(Recent);
            RecentView.Filter = Filter;

            Build();
        }

        private void SetRangeDays(int days)
        {
            EndDate = DateTime.Today;
            StartDate = EndDate.AddDays(-(days - 1));
        }

        private void Build()
        {
            try
            {
                var rows = LoadRaw().ToList();
                var grouped = GroupByOrder(rows).ToList();

                // === KPI 계산 ===
                InCount = grouped.Count(r => r.KindText == "입고");
                OutCount = grouped.Count(r => r.KindText == "출고");

                // 출고 총액
                var outTotal = grouped.Where(r => r.KindText == "출고").Sum(r => r.Price);

                // 입고 총액
                var inTotal = grouped.Where(r => r.KindText == "입고").Sum(r => r.Price);

                // 출고 총액 = 매출액
                _salesAmount = grouped.Where(r => r.KindText == "출고").Sum(r => r.Price);

                // 순이익 = 출고 항목들의 (소매-도매)*수량 합
                _profit = grouped.Where(r => r.KindText == "출고").Sum(r => r.Profit);


                Raise(nameof(InCount));
                Raise(nameof(OutCount));
                Raise(nameof(SalesAmountDisplay));
                Raise(nameof(ProfitDisplay));
                // =================

                // 최근 10건만 보여주기
                var top = grouped.OrderByDescending(x => x.Date).Take(10).ToList();
                Recent.Clear();
                foreach (var g in top)
                {
                    g.OpenDetailCommand = new RelayCommand(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(g.OrderNo))
                            _navigationService.Navigate(new MovementsDetailViewModel(_navigationService, g.OrderNo));
                    });
                    Recent.Add(g);
                }
                RecentView.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("대시보드 데이터 오류: " + ex.Message, "오류");
            }
        }


        private bool Filter(object obj)
        {
            if (obj is not RecentRow m) return false;
            return m.Date.Date >= StartDate.Date && m.Date.Date <= EndDate.Date;
        }

        #region CSV Reading Logic
        private sealed class RawLine { public DateTime DateTime; public string Type; public string OrderNo; public int Qty; public decimal Wholesale; public decimal Retail; public string Memo; }

        private IEnumerable<RawLine> LoadRaw()
        {
            var path = ResolveCsv(MOVE_FILES);
            if (path is null) return Enumerable.Empty<RawLine>();

            var list = new List<RawLine>();
            foreach (var row in CsvRead(path))
            {
                try
                {
                    var dateStr = row.GetAnyOrEmpty("일자", "date");
                    var orderNo = row.GetAnyOrEmpty("주문번호", "참조번호", "orderno");
                    if (string.IsNullOrWhiteSpace(dateStr) || string.IsNullOrWhiteSpace(orderNo)) continue;
                    list.Add(new RawLine
                    {
                        DateTime = ParseDateTime(dateStr, row.GetOrEmpty("시간 (HH:mm:ss)")),
                        Type = NormalizeType(row.GetAnyOrEmpty("IN/OUT", "입고/출고", "구분")),
                        OrderNo = orderNo.Trim(),
                        Qty = ParseInt(row.GetAnyOrEmpty("수량", "qty")),
                        Wholesale = ParseMoney(row.GetAnyOrEmpty("도매가격", "도매가(원)", "wholesale")),
                        Retail = ParseMoney(row.GetAnyOrEmpty("소매가격", "소매가(원)", "retail")),
                        Memo = row.GetAnyOrEmpty("메모", "memo", "비고") ?? ""
                    });
                }
                catch { /* Line errors are passed */ }
            }
            return list;
        }

        private IEnumerable<RecentRow> GroupByOrder(IEnumerable<RawLine> raws)
        {
            return raws
                .GroupBy(r => r.OrderNo)
                .Select(g =>
                {
                    var first = g.First();
                    return new RecentRow
                    {
                        Date = g.Max(x => x.DateTime),
                        KindRaw = first.Type,
                        OrderNo = g.Key,
                        Price = g.Sum(x => (x.Type == "OUT" ? x.Retail : x.Wholesale) * x.Qty),
                        Profit = g.Where(x => x.Type == "OUT")
                                  .Sum(x => (x.Retail - x.Wholesale) * x.Qty), // ✅ 순이익 계산
                        Memo = string.Join(" / ", g.Select(x => x.Memo).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()),
                        PartnerCode = "",
                        PartnerName = ""
                    };
                });
        }

        private static string NormalizeType(string v)
        {
            v = (v ?? "").Trim().ToUpperInvariant();
            if (v.Equals("입고", StringComparison.OrdinalIgnoreCase) || v.Equals("IN", StringComparison.OrdinalIgnoreCase)) return "IN";
            return "OUT";
        }

        public sealed class RecentRow : ObservableObject
        {
            public DateTime Date { get; set; }
            public string KindRaw { get; set; } = "";
            public string OrderNo { get; set; } = "";
            public decimal Price { get; set; }   // 매출액(출고금액) or 입고금액
            public decimal Profit { get; set; }  // 순이익(출고일 때만 계산)
            public string Memo { get; set; } = "";
            public string PartnerCode { get; set; } = "";
            public string PartnerName { get; set; } = "";

            public ICommand OpenDetailCommand { get; internal set; }

            public string KindText =>
                (KindRaw ?? "").Trim().ToUpperInvariant() switch
                {
                    "1" or "IN" or "입고" => "입고",
                    "2" or "OUT" or "출고" => "출고",
                    _ => string.IsNullOrWhiteSpace(KindRaw) ? "" : KindRaw
                };

            public Brush KindBrush =>
                KindText == "입고" ? new SolidColorBrush(Color.FromRgb(96, 165, 250)) :
                KindText == "출고" ? new SolidColorBrush(Color.FromRgb(248, 113, 113)) :
                                     new SolidColorBrush(Color.FromRgb(203, 213, 225));

            public string PriceDisplay =>
                string.Format(CultureInfo.GetCultureInfo("ko-KR"), "{0:N0} 원", Price);
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
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cols.Count; i++)
                    dict[cols[i].Trim()] = i < vals.Count ? vals[i] : "";
                yield return dict;
            }
        }

        private static Encoding DetectEncoding(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var bom = new byte[3];
            fs.Read(bom, 0, 3);
            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
            return new UTF8Encoding(false);
        }

        private static List<string> SplitCsv(string line)
        {
            var res = new List<string>();
            bool inQ = false;
            var cur = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQ && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQ = !inQ;
                }
                else if (ch == ',' && !inQ)
                {
                    res.Add(cur.ToString()); cur.Clear();
                }
                else cur.Append(ch);
            }
            res.Add(cur.ToString());
            return res;
        }

        private static decimal ParseMoney(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            s = s.Replace(",", "").Replace("₩", "").Trim();
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Replace(",", "").Trim();
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private static DateTime ParseDateTime(string date, string time)
        {
            date = date.Trim().Replace('.', '-');
            var t = string.IsNullOrWhiteSpace(time) ? "00:00:00" : time.Trim();
            if (DateTime.TryParse($"{date} {t}", out var dt)) return dt;
            return DateTime.Today;
        }

        private static string FormatCurrency(decimal v) => string.Format(CultureInfo.GetCultureInfo("ko-KR"), "₩{0:N0}", v);
        #endregion
    }
}