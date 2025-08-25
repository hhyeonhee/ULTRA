#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ULTRA.Helpers;           // CsvDictExtensions
using ULTRA.Services;          // INavigationService
using ULTRA.ViewModels.Base;   // ObservableObject, RelayCommand

namespace ULTRA.ViewModels
{
    public sealed class PartnersViewModel : ObservableObject
    {
        // ===== 엔티티 =====
        public sealed class Partner : ObservableObject
        {
            public string 코드 { get => _코드; set => Set(ref _코드, value ?? ""); }
            public string 거래처명 { get => _거래처명; set => Set(ref _거래처명, value ?? ""); }
            public string 연락처 { get => _연락처; set => Set(ref _연락처, value ?? ""); }
            public string 담당자 { get => _담당자; set => Set(ref _담당자, value ?? ""); }
            public string 주소 { get => _주소; set => Set(ref _주소, value ?? ""); }
            public string 메모 { get => _메모; set => Set(ref _메모, value ?? ""); }

            private string _코드 = "";
            private string _거래처명 = "";
            private string _연락처 = "";
            private string _담당자 = "";
            private string _주소 = "";
            private string _메모 = "";

            public Partner Clone() => new Partner
            {
                코드 = this.코드,
                거래처명 = this.거래처명,
                연락처 = this.연락처,
                담당자 = this.담당자,
                주소 = this.주소,
                메모 = this.메모
            };
            public void CopyFrom(Partner p)
            {
                코드 = p.코드; 거래처명 = p.거래처명; 연락처 = p.연락처; 담당자 = p.담당자; 주소 = p.주소; 메모 = p.메모;
            }
        }

        // ===== 바인딩 소스 =====
        public ObservableCollection<Partner> Partners { get; } = new();

        private ListCollectionView _partnersView;
        public ListCollectionView PartnersView
        {
            get => _partnersView;
            private set => Set(ref _partnersView, value);
        }

        private Partner _selectedPartner;
        public Partner SelectedPartner
        {
            get => _selectedPartner;
            set
            {
                if (Set(ref _selectedPartner, value))
                {
                    if (value != null) EditPartner = value.Clone();
                    Raise(nameof(CanEdit));
                    (EditCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (OpenMovementsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private Partner _editPartner = new();
        public Partner EditPartner { get => _editPartner; set => Set(ref _editPartner, value); }

        private bool _isEditing = false;
        public bool IsEditing { get => _isEditing; set { if (Set(ref _isEditing, value)) Raise(nameof(IsReadOnly)); } }
        public bool IsReadOnly => !IsEditing;
        public bool CanEdit => SelectedPartner != null;

        private string _searchText = "";
        public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) PartnersView?.Refresh(); } }

        // ===== 명령 =====
        public ICommand SearchCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand OpenMovementsCommand { get; }

        // ===== CSV 경로 및 서비스 =====
        private readonly string _partnersCsvPath;
        private readonly INavigationService _navigationService;

        // 생성자에서 INavigationService를 주입받도록 변경
        public PartnersViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;

            _partnersCsvPath = ResolveCsv(new[]
            {
                "ULTRA - 거래처정보.csv",
                "ULTRA-거래처정보.csv",
                "ULTRA-거래처정보-sample.csv",
                "ULTRA - 거래처정보 (샘플).csv"
            }, "ULTRA-거래처정보-sample.csv");

            LoadPartners();

            PartnersView = (ListCollectionView)CollectionViewSource.GetDefaultView(Partners);
            PartnersView.Filter = FilterPartner;

            SearchCommand = new RelayCommand(() => PartnersView?.Refresh());
            AddCommand = new RelayCommand(AddPartner);
            EditCommand = new RelayCommand(() =>
            {
                if (SelectedPartner != null)
                {
                    EditPartner = SelectedPartner.Clone();
                    IsEditing = true;
                }
            }, () => CanEdit);

            DeleteCommand = new RelayCommand(DeletePartner, () => CanEdit);
            CancelCommand = new RelayCommand(CancelEdit);
            SaveCommand = new RelayCommand(SaveAll);
            OpenMovementsCommand = new RelayCommand(OpenMovements, () => SelectedPartner != null);
        }

        private bool FilterPartner(object obj)
        {
            if (obj is not Partner p) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            var t = SearchText.Trim();
            return (p.코드?.IndexOf(t, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (p.거래처명?.IndexOf(t, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || (p.연락처?.IndexOf(t, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private void AddPartner()
        {
            string next = NextCode();
            var np = new Partner { 코드 = next, 거래처명 = "", 연락처 = "", 담당자 = "", 주소 = "", 메모 = "" };
            Partners.Add(np);
            SelectedPartner = np;
            EditPartner = np.Clone();
            IsEditing = true;
        }

        private string NextCode()
        {
            int max = 0;
            foreach (var c in Partners.Select(p => p.코드))
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                if (c.StartsWith("C", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(c.Substring(1), out var n)) max = Math.Max(max, n);
            }
            return "C" + (max + 1).ToString("000");
        }

        private void DeletePartner()
        {
            if (SelectedPartner == null) return;
            if (MessageBox.Show($"[{SelectedPartner.코드}] {SelectedPartner.거래처명} 을(를) 삭제할까요?",
                "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            Partners.Remove(SelectedPartner);
            SelectedPartner = null;
            EditPartner = new Partner();
            IsEditing = false;
        }

        private void CancelEdit()
        {
            if (SelectedPartner != null) EditPartner = SelectedPartner.Clone();
            IsEditing = false;
        }

        private void SaveAll()
        {
            try
            {
                if (SelectedPartner != null && IsEditing)
                    SelectedPartner.CopyFrom(EditPartner);

                var dir = Path.GetDirectoryName(_partnersCsvPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using var sw = new StreamWriter(_partnersCsvPath, false, new UTF8Encoding(true));
                sw.WriteLine("코드,거래처명,연락처,담당자,주소,메모");
                foreach (var p in Partners.OrderBy(p => p.코드))
                {
                    sw.WriteLine(string.Join(",",
                        CsvEscape(p.코드), CsvEscape(p.거래처명), CsvEscape(p.연락처),
                        CsvEscape(p.담당자), CsvEscape(p.주소), CsvEscape(p.메모)));
                }

                IsEditing = false;
                MessageBox.Show("저장되었습니다.", "안내");
            }
            catch (Exception ex)
            {
                MessageBox.Show("저장 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenMovements()
        {
            if (SelectedPartner is null) return;
            var vm = new MovementsViewModel(_navigationService, SelectedPartner.코드, SelectedPartner.거래처명);
            _navigationService.Navigate(vm);
        }

        private void LoadPartners()
        {
            Partners.Clear();
            if (!File.Exists(_partnersCsvPath)) return;

            try
            {
                foreach (var row in CsvRead(_partnersCsvPath))
                {
                    try
                    {
                        var code = (row.GetAnyOrEmpty("코드", "거래처코드", "고객코드") ?? "").Trim();
                        var name = (row.GetAnyOrEmpty("거래처명", "상호", "업체명") ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(code)) continue;

                        Partners.Add(new Partner
                        {
                            코드 = code,
                            거래처명 = name,
                            연락처 = (row.GetAnyOrEmpty("연락처", "전화", "전화번호") ?? "").Trim(),
                            담당자 = (row.GetAnyOrEmpty("담당자", "대표자") ?? "").Trim(),
                            주소 = (row.GetAnyOrEmpty("주소", "주소지") ?? "").Trim(),
                            메모 = (row.GetAnyOrEmpty("메모", "비고") ?? "").Trim()
                        });
                    }
                    catch { /* 라인 오류는 무시 */ }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("거래처 CSV 로드 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== CSV 유틸 (기존과 동일) =====
        private static string ResolveCsv(IEnumerable<string> candidates, string fallbackFileName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var name in candidates)
            {
                var p1 = Path.Combine(baseDir, "Data", name);
                if (File.Exists(p1)) return p1;
                var p2 = Path.Combine(baseDir, name);
                if (File.Exists(p2)) return p2;
            }
            return Path.Combine(baseDir, "Data", fallbackFileName);
        }

        private static IEnumerable<Dictionary<string, string>> CsvRead(string path)
        {
            using var sr = new StreamReader(path, DetectEncoding(path));
            var header = sr.ReadLine();
            if (header == null) yield break;
            var cols = SplitCsv(header).Select(h => h.Trim().Trim('\uFEFF')).ToList();
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                var vals = SplitCsv(line);
                if (vals.Count < cols.Count)
                    vals.AddRange(Enumerable.Repeat("", cols.Count - vals.Count));
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cols.Count; i++)
                {
                    var key = cols[i];
                    var val = i < vals.Count ? vals[i] : "";
                    dict[key] = (val ?? "").Trim();
                }
                yield return dict;
            }
        }

        private static Encoding DetectEncoding(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            if (fs.Length >= 3)
            {
                var bom3 = new byte[3];
                fs.Read(bom3, 0, 3);
                if (bom3[0] == 0xEF && bom3[1] == 0xBB && bom3[2] == 0xBF)
                    return new UTF8Encoding(true); // UTF-8 BOM
            }
            fs.Seek(0, SeekOrigin.Begin);
            if (fs.Length >= 2)
            {
                var bom2 = new byte[2];
                fs.Read(bom2, 0, 2);
                if (bom2[0] == 0xFF && bom2[1] == 0xFE) return Encoding.Unicode;       // UTF-16 LE
                if (bom2[0] == 0xFE && bom2[1] == 0xFF) return Encoding.BigEndianUnicode;  // UTF-16 BE
            }
            try { return Encoding.GetEncoding(949); }  // CP949(엑셀 기본)
            catch { return Encoding.Default; }
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

        private static string CsvEscape(string s)
        {
            if (s == null) return "";
            return (s.Contains(",") || s.Contains("\""))
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }
    }
}