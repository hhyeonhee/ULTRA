#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ULTRA.Helpers;
using ULTRA.Services; // INavigationService를 사용하기 위해 추가
using ULTRA.ViewModels.Base;

namespace ULTRA.ViewModels
{
    /// <summary>
    /// 입출고 상세 VM
    /// </summary>
    public sealed class MovementsDetailViewModel : ObservableObject
    {
        // ===== 헤더 바인딩 =====
        private string _dateText = "-";
        public string DateText { get => _dateText; private set => Set(ref _dateText, value); }

        private string _typeText = "-";  // "입고" / "출고"
        public string TypeText { get => _typeText; private set => Set(ref _typeText, value); }

        private string _orderNo = "-";
        public string OrderNo { get => _orderNo; private set => Set(ref _orderNo, value); }

        private decimal _totalAmount;
        public string TotalAmountDisplay => FormatWon(_totalAmount);

        private string _partnerDisplay = "-";
        public string PartnerDisplay { get => _partnerDisplay; private set => Set(ref _partnerDisplay, value); }

        // ===== 하단 테이블 =====
        public ObservableCollection<ItemRow> Items { get; } = new();

        // ===== 명령 =====
        public ICommand BackCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand PrintCommand { get; }
        public ICommand ConfirmCommand { get; }

        private readonly INavigationService _navigationService;

        // ===== 생성자 =====
        public MovementsDetailViewModel(INavigationService navigationService, string orderNo)
        {
            _navigationService = navigationService; // 네비게이션 서비스 주입
            OrderNo = orderNo ?? "";

            BackCommand = new RelayCommand(() =>
            {
                // 주입받은 네비게이션 서비스를 사용하여 화면 전환
                _navigationService.Navigate(new MovementsViewModel(_navigationService));
            });

            EditCommand = new RelayCommand(() => MessageBox.Show("수정 기능은 추후 구현 예정입니다.", "알림"));
            PrintCommand = new RelayCommand(() => MessageBox.Show("출력 기능은 추후 구현 예정입니다.", "알림"));
            ConfirmCommand = new RelayCommand(() => MessageBox.Show("확인 처리(저장/완료표시 등)는 추후 구현 예정입니다.", "알림"));

            LoadDocument(OrderNo);
        }

        // ===== 데이터 로드 =====
        private void LoadDocument(string docNo)
        {
            try
            {
                // 1) CSV 로드
                var moves = LoadMovements();
                if (moves.Count == 0)
                {
                    MessageBox.Show("입출고 내역 CSV를 찾을 수 없거나 비어 있습니다.", "알림");
                    return;
                }

                // 2) 주문번호 기준 필터
                var lines = moves.Where(m => string.Equals(m.OrderNo, docNo, StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(m => m.LineNo)
                                 .ToList();

                if (lines.Count == 0)
                {
                    MessageBox.Show($"문서번호 '{docNo}' 데이터가 없습니다.", "알림");
                    return;
                }

                // 3) 헤더 결정 (동일 주문번호는 동일 값이라고 가정)
                var first = lines.First();
                DateText = first.DateTime.ToString("MM-dd", CultureInfo.InvariantCulture);
                TypeText = first.Type == "IN" ? "입고" : "출고";

                // 4) 거래처 매핑
                var partners = LoadPartners();  // key: code
                if (first.PartnerCode != null && partners.TryGetValue(first.PartnerCode, out var p))
                    PartnerDisplay = $"{p.name} / {p.phone}";
                else
                    PartnerDisplay = "-";

                // 5) 품목 목록 구성
                Items.Clear();
                foreach (var l in lines)
                {
                    Items.Add(new ItemRow
                    {
                        Code = l.ProductCode,
                        Name = l.ProductName,
                        Attr = l.Attr,
                        AttrCode = l.AttrCode,
                        Qty = l.Qty,
                        Wholesale = l.Wholesale,
                        Retail = l.Retail,
                        OptionName = l.OptionName,
                        OptionPrice = l.OptionPrice,
                        Warranty = l.Warranty
                    });
                }

                // 6) 총액 계산
                decimal headerTotal = lines.Where(x => x.HeaderTotal.HasValue).Select(x => x.HeaderTotal!.Value).DefaultIfEmpty(0m).Max();
                if (headerTotal <= 0m)
                {
                    if (first.Type == "OUT")
                        headerTotal = lines.Sum(x => x.Retail * x.Qty + x.OptionPrice * x.Qty);
                    else
                        headerTotal = lines.Sum(x => x.Wholesale * x.Qty + x.OptionPrice * x.Qty);
                }
                _totalAmount = headerTotal;
                Raise(nameof(TotalAmountDisplay));
            }
            catch (Exception ex)
            {
                MessageBox.Show("상세 데이터 로드 중 오류: " + ex.Message, "오류");
            }
        }

        // ... 이하 코드는 기존과 동일 ...

        #region CSV Reading Logic
        private sealed class MoveLine
        {
            public int LineNo { get; set; }
            public DateTime DateTime { get; set; }
            public string Type { get; set; } = "OUT"; // IN/OUT
            public string OrderNo { get; set; } = "";
            public string ProductCode { get; set; } = "";
            public string ProductName { get; set; } = "";
            public string Attr { get; set; } = "";
            public string AttrCode { get; set; } = "";
            public int Qty { get; set; }
            public decimal Wholesale { get; set; }
            public decimal Retail { get; set; }
            public string OptionName { get; set; } = "";
            public decimal OptionPrice { get; set; }
            public string Warranty { get; set; } = "";
            public string? PartnerCode { get; set; }
            public decimal? HeaderTotal { get; set; }
        }

        private List<MoveLine> LoadMovements()
        {
            var list = new List<MoveLine>();
            var candidates = new[] { "ULTRA-입출고내역.csv", "ULTRA-입출고내역-sample.csv", "ULTRA - 입출고내역 (샘플).csv" };
            var path = ResolveCsv(candidates);
            if (path == null) return list;

            int lineNo = 0;
            foreach (var row in CsvRead(path))
            {
                lineNo++;
                try
                {
                    if (string.IsNullOrWhiteSpace(row.GetAnyOrEmpty("주문번호", "문서번호", "ORDERNO"))) continue;
                    list.Add(new MoveLine
                    {
                        LineNo = lineNo,
                        DateTime = ParseDateTime(row.GetAnyOrEmpty("일자", "DATE"), row.GetAnyOrEmpty("시간 (HH:mm:ss)", "시간", "TIME")),
                        Type = NormalizeType(row.GetAnyOrEmpty("IN/OUT", "입고/출고", "TYPE")),
                        OrderNo = (row.GetAnyOrEmpty("주문번호", "문서번호", "ORDERNO") ?? "").Trim(),
                        ProductCode = row.GetAnyOrEmpty("품목코드", "코드") ?? "",
                        ProductName = row.GetAnyOrEmpty("품목명", "제품명", "품명") ?? "",
                        Attr = row.GetOrEmpty("속성") ?? "",
                        AttrCode = row.GetOrEmpty("속성코드") ?? "",
                        Qty = ParseInt(row.GetOrEmpty("수량")),
                        Wholesale = ParseMoney(row.GetAnyOrEmpty("도매가격", "도매가(원)")),
                        Retail = ParseMoney(row.GetAnyOrEmpty("소매가격", "소매가(원)")),
                        OptionName = row.GetAnyOrEmpty("선택옵션", "옵션명") ?? "",
                        OptionPrice = ParseMoney(row.GetAnyOrEmpty("옵션가", "옵션가격(원)")),
                        Warranty = row.GetAnyOrEmpty("보증기간", "보증") ?? "",
                        PartnerCode = (row.GetOrEmpty("거래처코드")?.Trim()),
                        HeaderTotal = ParseMoneyNullable(row.GetAnyOrEmpty("총금액", "합계금액"))
                    });
                }
                catch { /* Line errors are ignored */ }
            }
            return list;
        }

        private Dictionary<string, (string name, string phone)> LoadPartners()
        {
            var dict = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            var candidates = new[] { "ULTRA-거래처정보.csv", "ULTRA-거래처정보-sample.csv", "ULTRA - 거래처정보 (샘플).csv" };
            var path = ResolveCsv(candidates);
            if (path == null) return dict;

            foreach (var row in CsvRead(path))
            {
                var code = (row.GetAnyOrEmpty("거래처코드", "코드") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(code)) continue;
                dict[code] = ((row.GetAnyOrEmpty("거래처명", "상호") ?? ""), (row.GetAnyOrEmpty("연락처", "전화") ?? ""));
            }
            return dict;
        }

        private static string? ResolveCsv(IEnumerable<string> candidates)
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
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                var vals = SplitCsv(line);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cols.Count; i++)
                {
                    dict[cols[i]] = i < vals.Count ? vals[i] : "";
                }
                yield return dict;
            }
        }

        private static Encoding DetectEncoding(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var bom = new byte[3];
            _ = fs.Read(bom, 0, 3);
            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return new UTF8Encoding(true);
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
                    res.Add(cur.ToString());
                    cur.Clear();
                }
                else cur.Append(ch);
            }
            res.Add(cur.ToString());
            return res;
        }

        private static DateTime ParseDateTime(string? date, string? time)
        {
            date = (date ?? "").Trim().Replace('.', '-').Replace('/', '-');
            var t = string.IsNullOrWhiteSpace(time) ? "00:00:00" : time!.Trim();
            if (DateTime.TryParse($"{date} {t}", out var dt)) return dt;
            return DateTime.Today;
        }

        private static int ParseInt(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Replace(",", "").Trim();
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private static decimal ParseMoney(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            s = s.Replace(",", "").Replace("₩", "").Trim();
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;
        }

        private static decimal? ParseMoneyNullable(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Replace(",", "").Replace("₩", "").Trim();
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        private static string NormalizeType(string? s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s == "IN" || s == "입고") return "IN";
            if (s == "OUT" || s == "출고") return "OUT";
            return "OUT";
        }

        private static string FormatWon(decimal v)
            => string.Format(CultureInfo.GetCultureInfo("ko-KR"), "₩{0:N0}", v);
        #endregion

        public sealed class ItemRow
        {
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public string Attr { get; set; } = "";
            public string AttrCode { get; set; } = "";
            public int Qty { get; set; }
            public decimal Wholesale { get; set; }
            public decimal Retail { get; set; }
            public string OptionName { get; set; } = "";
            public decimal OptionPrice { get; set; }
            public string Warranty { get; set; } = "";

            public string WholesaleDisplay => FormatWon(Wholesale);
            public string RetailDisplay => FormatWon(Retail);
            public string OptionPriceDisplay => FormatWon(OptionPrice);
        }
    }
}