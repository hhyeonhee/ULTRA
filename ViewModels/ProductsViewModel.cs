#nullable disable
using System;
using System.Collections;
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
using ULTRA.Helpers;
using ULTRA.ViewModels.Base;

namespace ULTRA.ViewModels
{
    public sealed class ProductsViewModel : ObservableObject
    {
        // ===== 목록 =====
        private readonly ObservableCollection<ProductRow> _items = new ObservableCollection<ProductRow>();
        public ObservableCollection<ProductRow> Items { get { return _items; } }
        public ListCollectionView ItemsView { get; private set; }

        // CSV 저장 경로
        private string _csvPath;

        // ===== 선택/편집 =====
        private ProductRow _selected;
        public ProductRow Selected
        {
            get { return _selected; }
            set
            {
                if (Set(ref _selected, value))
                {
                    if (!IsEditing) EditModel = value;
                }
            }
        }

        private ProductRow _editModel;
        public ProductRow EditModel
        {
            get { return _editModel; }
            private set
            {
                if (_editModel != null) _editModel.PropertyChanged -= EditModel_PropertyChanged;
                if (Set(ref _editModel, value))
                {
                    if (_editModel != null) _editModel.PropertyChanged += EditModel_PropertyChanged;
                }
            }
        }

        private void EditModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (EditModel == null) return;

            if (e.PropertyName == nameof(ProductRow.도매가) ||
                e.PropertyName == nameof(ProductRow.소매가))
            {
                EditModel.차익금 = Math.Max(0, EditModel.소매가 - EditModel.도매가);
            }

            if (e.PropertyName == nameof(ProductRow.대분류) ||
                e.PropertyName == nameof(ProductRow.대분류코드) ||
                e.PropertyName == nameof(ProductRow.중분류) ||
                e.PropertyName == nameof(ProductRow.중분류코드) ||
                e.PropertyName == nameof(ProductRow.소분류) ||
                e.PropertyName == nameof(ProductRow.소분류코드) ||
                e.PropertyName == nameof(ProductRow.속성) ||
                e.PropertyName == nameof(ProductRow.속성코드))
            {
                SyncPairs(EditModel);
            }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get { return _isEditing; }
            private set { Set(ref _isEditing, value); }
        }

        // ===== 검색/필터/정렬 =====
        private string _searchText = "";
        public string SearchText
        {
            get { return _searchText; }
            set { if (Set(ref _searchText, value)) ItemsView.Refresh(); }
        }

        private string _majorFilter = "전체";
        public string MajorFilter
        {
            get { return _majorFilter; }
            set { if (Set(ref _majorFilter, value)) ItemsView.Refresh(); }
        }

        private string _middleFilter = "전체";
        public string MiddleFilter
        {
            get { return _middleFilter; }
            set { if (Set(ref _middleFilter, value)) ItemsView.Refresh(); }
        }

        public ObservableCollection<string> SortOptions { get; private set; } =
            new ObservableCollection<string>
            {
                "번호순",
                "도매가 높은순","도매가 낮은순",
                "소매가 높은순","소매가 낮은순",
                "대분류별","중분류별","제조사별"
            };

        private string _selectedSortOption = "번호순";
        public string SelectedSortOption
        {
            get { return _selectedSortOption; }
            set { if (Set(ref _selectedSortOption, value)) ApplySort(); }
        }

        // ===== 드롭다운 소스 =====
        public ObservableCollection<string> Manufacturers { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<string> Suppliers { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<string> Origins { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<string> Certs { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<string> Warranties { get; private set; } = new ObservableCollection<string>();

        public ObservableCollection<string> MajorNames { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<string> MiddleNames { get; private set; } = new ObservableCollection<string>();

        // 편집 콤보에서 '전체' 제외한 뷰
        public IEnumerable<string> MajorEditChoices => MajorNames.Where(x => x != "전체");
        public IEnumerable<string> MiddleEditChoices => MiddleNames.Where(x => x != "전체");

        // ===== 분류 매핑 =====
        private readonly Dictionary<string, string> _majorNameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _majorCodeToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _middleNameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _middleCodeToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _smallNameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _smallCodeToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _attrNameToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _attrCodeToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ===== 명령 =====
        public ICommand AddCommand { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }

        public ProductsViewModel()
        {
            LoadProducts();

            ItemsView = (ListCollectionView)CollectionViewSource.GetDefaultView(Items);
            ItemsView.Filter = FilterPredicate;
            ApplySort();

            BuildDropdownSources();

            AddCommand = new RelayCommand(AddNew);
            EditCommand = new RelayCommand(EditSelected, CanEditOrDelete);
            DeleteCommand = new RelayCommand(DeleteSelected, CanEditOrDelete);
            CancelCommand = new RelayCommand(CancelEdit, () => IsEditing);
            SaveCommand = new RelayCommand(SaveEdit, () => IsEditing);
        }

        private bool FilterPredicate(object obj)
        {
            var r = obj as ProductRow;
            if (r == null) return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var t = SearchText.Trim();
                bool hit =
                    (!string.IsNullOrEmpty(r.번호) && r.번호.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(r.품명) && r.품명.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(r.제조사) && r.제조사.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hit) return false;
            }

            if (!string.Equals(MajorFilter, "전체", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r.대분류 ?? "", MajorFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(MiddleFilter, "전체", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(r.중분류 ?? "", MiddleFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private void ApplySort()
        {
            ItemsView.SortDescriptions.Clear();
            ItemsView.CustomSort = null;

            switch (SelectedSortOption)
            {
                case "번호순":
                    ItemsView.CustomSort = new ProductComparerByNo();
                    break;
                case "도매가 높은순":
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.도매가), ListSortDirection.Descending));
                    break;
                case "도매가 낮은순":
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.도매가), ListSortDirection.Ascending));
                    break;
                case "소매가 높은순":
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.소매가), ListSortDirection.Descending));
                    break;
                case "소매가 낮은순":
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.소매가), ListSortDirection.Ascending));
                    break;
                case "대분류별":
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.대분류), ListSortDirection.Ascending));
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.중분류), ListSortDirection.Ascending));
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.번호), ListSortDirection.Ascending));
                    break;
                case "중분류별":
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.중분류), ListSortDirection.Ascending));
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.번호), ListSortDirection.Ascending));
                    break;
                case "제조사별":
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.제조사), ListSortDirection.Ascending));
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(ProductRow.번호), ListSortDirection.Ascending));
                    break;
            }
            ItemsView.Refresh();
        }

        // ===== 편집 =====
        private void AddNew()
        {
            Selected = null;
            EditModel = new ProductRow();
            IsEditing = true;
        }

        private bool CanEditOrDelete()
        {
            return Selected != null && !IsEditing;
        }

        private void EditSelected()
        {
            if (Selected == null) return;
            EditModel = Selected.Clone();
            IsEditing = true;
        }

        private void DeleteSelected()
        {
            if (Selected == null) return;

            if (MessageBox.Show(
                string.Format("선택한 제품 '{0} / {1}'을 삭제할까요?", Selected.번호, Selected.품명),
                "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            int idx = Items.IndexOf(Selected);
            if (idx >= 0) Items.RemoveAt(idx);
            Selected = null;

            TrySaveCsv();
        }

        private void CancelEdit()
        {
            EditModel = Selected;
            IsEditing = false;
        }

        private void SaveEdit()
        {
            if (EditModel == null) return;

            SyncPairs(EditModel);
            EditModel.차익금 = Math.Max(0, EditModel.소매가 - EditModel.도매가);

            ProductRow target;
            if (Selected == null || !Items.Contains(Selected))
            {
                target = EditModel.Clone();
                Items.Add(target);
            }
            else
            {
                target = Selected;
                target.CopyFrom(EditModel);
            }

            MergeDropdownValue(Manufacturers, target.제조사);
            MergeDropdownValue(Suppliers, target.공급업체명);
            MergeDropdownValue(Origins, target.원산지);
            MergeDropdownValue(Certs, target.인증);
            MergeDropdownValue(Warranties, target.보증기간);
            MergeDropdownValue(MajorNames, target.대분류);
            MergeDropdownValue(MiddleNames, target.중분류);

            ApplySort();
            ItemsView.Refresh();

            IsEditing = false;
            Selected = target;

            TrySaveCsv();
        }

        private static void MergeDropdownValue(ObservableCollection<string> col, string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return;
            if (!col.Contains(v)) col.Add(v);
        }

        private void SyncPairs(ProductRow m)
        {
            string code, name;

            if (!string.IsNullOrWhiteSpace(m.대분류) && _majorNameToCode.TryGetValue(m.대분류, out code))
                m.대분류코드 = code;
            if (!string.IsNullOrWhiteSpace(m.중분류) && _middleNameToCode.TryGetValue(m.중분류, out code))
                m.중분류코드 = code;
            if (!string.IsNullOrWhiteSpace(m.소분류) && _smallNameToCode.TryGetValue(m.소분류, out code))
                m.소분류코드 = code;
            if (!string.IsNullOrWhiteSpace(m.속성) && _attrNameToCode.TryGetValue(m.속성, out code))
                m.속성코드 = code;

            if (string.IsNullOrWhiteSpace(m.대분류) && !string.IsNullOrWhiteSpace(m.대분류코드) &&
                _majorCodeToName.TryGetValue(m.대분류코드, out name)) m.대분류 = name;
            if (string.IsNullOrWhiteSpace(m.중분류) && !string.IsNullOrWhiteSpace(m.중분류코드) &&
                _middleCodeToName.TryGetValue(m.중분류코드, out name)) m.중분류 = name;
            if (string.IsNullOrWhiteSpace(m.소분류) && !string.IsNullOrWhiteSpace(m.소분류코드) &&
                _smallCodeToName.TryGetValue(m.소분류코드, out name)) m.소분류 = name;
            if (string.IsNullOrWhiteSpace(m.속성) && !string.IsNullOrWhiteSpace(m.속성코드) &&
                _attrCodeToName.TryGetValue(m.속성코드, out name)) m.속성 = name;
        }

        // ===== 드롭다운 소스 =====
        private void BuildDropdownSources()
        {
            Manufacturers.Clear();
            foreach (var s in Items.Select(x => x.제조사).Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s))
                Manufacturers.Add(s);

            Suppliers.Clear();
            foreach (var s in Items.Select(x => x.공급업체명).Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s))
                Suppliers.Add(s);

            Origins.Clear();
            foreach (var s in Items.Select(x => x.원산지).Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s))
                Origins.Add(s);

            Certs.Clear();
            foreach (var s in Items.Select(x => x.인증).Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s))
                Certs.Add(s);

            Warranties.Clear();
            foreach (var s in Items.Select(x => x.보증기간).Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s))
                Warranties.Add(s);

            MajorNames.Clear(); MajorNames.Add("전체");
            foreach (var s in Items.Select(x => x.대분류).Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s))
                MajorNames.Add(s);

            MiddleNames.Clear(); MiddleNames.Add("전체");
            foreach (var s in Items.Select(x => x.중분류).Where(s => !string.IsNullOrWhiteSpace(s))
                                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s))
                MiddleNames.Add(s);
        }

        // ===== CSV 로드 =====
        private void LoadProducts()
        {
            Items.Clear();

            var candidates = new[]
            {
                "ULTRA - 제품정보.csv",
                "ULTRA-제품정보.csv",
                "ULTRA-제품정보-sample.csv",
                "ULTRA - 제품정보 (샘플).csv"
            };

            _csvPath = ResolveCsv(candidates);

            if (_csvPath == null)
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dataDir = Path.Combine(baseDir, "Data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                _csvPath = Path.Combine(dataDir, "ULTRA - 제품정보.csv");
            }

            if (File.Exists(_csvPath))
            {
                foreach (var row in CsvRead(_csvPath))
                {
                    try
                    {
                        var p = new ProductRow
                        {
                            번호 = row.GetAnyOrEmpty("번호", "코드"),
                            품명 = row.GetAnyOrEmpty("품명", "제품명"),
                            제조사 = row.GetOrEmpty("제조사"),
                            제조사코드 = row.GetOrEmpty("제조사코드"),
                            공급업체명 = row.GetAnyOrEmpty("공급업체명", "공급사"),
                            공급업체코드 = row.GetOrEmpty("공급업체코드"),
                            공급업체번호 = row.GetOrEmpty("공급업체번호"),
                            원산지 = row.GetOrEmpty("원산지"),
                            인증 = row.GetOrEmpty("인증"),
                            인증번호 = row.GetOrEmpty("인증번호"),
                            도매가 = ParseMoney(row.GetAnyOrEmpty("도매가(원)", "도매가격")),
                            소매가 = ParseMoney(row.GetAnyOrEmpty("소매가(원)", "소매가격")),
                            차익금 = ParseMoney(row.GetAnyOrEmpty("차익금(원)", "차익금")),
                            대분류 = row.GetOrEmpty("대분류"),
                            대분류코드 = row.GetOrEmpty("대분류코드"),
                            중분류 = row.GetOrEmpty("중분류"),
                            중분류코드 = row.GetOrEmpty("중분류코드"),
                            소분류 = row.GetOrEmpty("소분류"),
                            소분류코드 = row.GetOrEmpty("소분류코드"),
                            속성 = row.GetOrEmpty("속성"),
                            속성코드 = row.GetOrEmpty("속성코드"),
                            기본옵션 = row.GetOrEmpty("기본옵션"),
                            옵션갯수 = ParseInt(row.GetAnyOrEmpty("옵션갯수(개)", "옵션갯수")),
                            옵션1 = row.GetOrEmpty("옵션1"),
                            옵션1가격 = ParseMoney(row.GetAnyOrEmpty("옵션1가격(원)", "옵션1가격")),
                            옵션2 = row.GetOrEmpty("옵션2"),
                            옵션2가격 = ParseMoney(row.GetAnyOrEmpty("옵션2가격(원)", "옵션2가격")),
                            옵션3 = row.GetOrEmpty("옵션3"),
                            옵션3가격 = ParseMoney(row.GetAnyOrEmpty("옵션3가격(원)", "옵션3가격")),
                            옵션4 = row.GetOrEmpty("옵션4"),
                            옵션4가격 = ParseMoney(row.GetAnyOrEmpty("옵션4가격(원)", "옵션4가격")),
                            옵션5 = row.GetOrEmpty("옵션5"),
                            옵션5가격 = ParseMoney(row.GetAnyOrEmpty("옵션5가격(원)", "옵션5가격")),
                            보증기간 = row.GetOrEmpty("보증기간"),
                            단종여부 = ParseBool(row.GetOrEmpty("단종여부")),
                            이미지1첨부 = row.GetOrEmpty("이미지1첨부"),
                            이미지2첨부 = row.GetOrEmpty("이미지2첨부"),
                            이미지3첨부 = row.GetOrEmpty("이미지3첨부"),
                            특이사항 = row.GetOrEmpty("특이사항"),
                        };

                        if (p.차익금 <= 0) p.차익금 = Math.Max(0, p.소매가 - p.도매가);

                        Items.Add(p);

                        AddPair(_majorNameToCode, _majorCodeToName, p.대분류, p.대분류코드);
                        AddPair(_middleNameToCode, _middleCodeToName, p.중분류, p.중분류코드);
                        AddPair(_smallNameToCode, _smallCodeToName, p.소분류, p.소분류코드);
                        AddPair(_attrNameToCode, _attrCodeToName, p.속성, p.속성코드);
                    }
                    catch { /* 한 줄 오류 무시 */ }
                }
            }
        }

        private static void AddPair(Dictionary<string, string> n2c, Dictionary<string, string> c2n, string name, string code)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code))
            {
                n2c[name] = code;
                c2n[code] = name;
            }
        }

        // ===== CSV 저장 =====
        private void TrySaveCsv()
        {
            try
            {
                var dir = Path.GetDirectoryName(_csvPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var sw = new StreamWriter(_csvPath, false, new UTF8Encoding(true)))
                {
                    // 고정 헤더
                    sw.WriteLine(string.Join(",",
                        "번호", "품명", "제조사", "제조사코드",
                        "공급업체명", "공급업체코드", "공급업체번호",
                        "원산지", "인증", "인증번호",
                        "도매가(원)", "소매가(원)", "차익금(원)",
                        "대분류", "대분류코드", "중분류", "중분류코드", "소분류", "소분류코드",
                        "속성", "속성코드",
                        "기본옵션", "옵션갯수(개)",
                        "옵션1", "옵션1가격(원)", "옵션2", "옵션2가격(원)", "옵션3", "옵션3가격(원)",
                        "옵션4", "옵션4가격(원)", "옵션5", "옵션5가격(원)",
                        "보증기간", "단종여부",
                        "이미지1첨부", "이미지2첨부", "이미지3첨부",
                        "특이사항"
                    ));

                    foreach (var r in Items)
                    {
                        string Line(params object[] parts) { return string.Join(",", parts.Select(CsvEscape)); }

                        sw.WriteLine(Line(
                            r.번호, r.품명, r.제조사, r.제조사코드,
                            r.공급업체명, r.공급업체코드, r.공급업체번호,
                            r.원산지, r.인증, r.인증번호,
                            MoneyOut(r.도매가), MoneyOut(r.소매가), MoneyOut(r.차익금),
                            r.대분류, r.대분류코드, r.중분류, r.중분류코드, r.소분류, r.소분류코드,
                            r.속성, r.속성코드,
                            r.기본옵션, r.옵션갯수,
                            r.옵션1, MoneyOut(r.옵션1가격),
                            r.옵션2, MoneyOut(r.옵션2가격),
                            r.옵션3, MoneyOut(r.옵션3가격),
                            r.옵션4, MoneyOut(r.옵션4가격),
                            r.옵션5, MoneyOut(r.옵션5가격),
                            r.보증기간, (r.단종여부 ? "Y" : "N"),
                            r.이미지1첨부, r.이미지2첨부, r.이미지3첨부,
                            r.특이사항
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("CSV 저장 중 오류: " + ex.Message, "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string MoneyOut(decimal v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private static string CsvEscape(object o)
        {
            var s = (o == null) ? "" : o.ToString();
            s = s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0)
            {
                s = s.Replace("\"", "\"\"");
                s = "\"" + s + "\"";
            }
            return s;
        }

        // ===== 공통 유틸 =====
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
                if (vals.Count < cols.Count)
                {
                    vals.AddRange(Enumerable.Repeat("", cols.Count - vals.Count));
                }

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
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var bom = new byte[3];
                fs.Read(bom, 0, 3);
                if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
                return new UTF8Encoding(false);
            }
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
            decimal v;
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out v) ? v : 0m;
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Replace(",", "").Trim();
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static bool ParseBool(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            return s.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("1", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class ProductComparerByNo : IComparer
    {
        public int Compare(object x, object y)
        {
            var a = x as ProductRow;
            var b = y as ProductRow;
            if (a == null || b == null) return 0;
            return ExtractNum(a.번호).CompareTo(ExtractNum(b.번호));
        }

        private static int ExtractNum(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return int.MinValue;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i])) sb.Append(s[i]);
            int v;
            return int.TryParse(sb.ToString(), out v) ? v : int.MinValue;
        }
    }

    // ===== 바인딩 모델 =====
    public sealed class ProductRow : ObservableObject
    {
        private string _번호; public string 번호 { get { return _번호; } set { Set(ref _번호, value); } }
        private string _품명; public string 품명 { get { return _품명; } set { Set(ref _품명, value); } }
        private string _제조사; public string 제조사 { get { return _제조사; } set { Set(ref _제조사, value); } }
        private string _제조사코드; public string 제조사코드 { get { return _제조사코드; } set { Set(ref _제조사코드, value); } }
        private string _공급업체명; public string 공급업체명 { get { return _공급업체명; } set { Set(ref _공급업체명, value); } }
        private string _공급업체코드; public string 공급업체코드 { get { return _공급업체코드; } set { Set(ref _공급업체코드, value); } }
        private string _공급업체번호; public string 공급업체번호 { get { return _공급업체번호; } set { Set(ref _공급업체번호, value); } }
        private string _원산지; public string 원산지 { get { return _원산지; } set { Set(ref _원산지, value); } }
        private string _인증; public string 인증 { get { return _인증; } set { Set(ref _인증, value); } }
        private string _인증번호; public string 인증번호 { get { return _인증번호; } set { Set(ref _인증번호, value); } }

        private decimal _도매가; public decimal 도매가 { get { return _도매가; } set { if (Set(ref _도매가, value)) Raise(nameof(도매가표시)); } }
        private decimal _소매가; public decimal 소매가 { get { return _소매가; } set { if (Set(ref _소매가, value)) Raise(nameof(소매가표시)); } }
        private decimal _차익금; public decimal 차익금 { get { return _차익금; } set { if (Set(ref _차익금, value)) Raise(nameof(차익금표시)); } }

        private string _대분류; public string 대분류 { get { return _대분류; } set { Set(ref _대분류, value); } }
        private string _대분류코드; public string 대분류코드 { get { return _대분류코드; } set { Set(ref _대분류코드, value); } }
        private string _중분류; public string 중분류 { get { return _중분류; } set { Set(ref _중분류, value); } }
        private string _중분류코드; public string 중분류코드 { get { return _중분류코드; } set { Set(ref _중분류코드, value); } }
        private string _소분류; public string 소분류 { get { return _소분류; } set { Set(ref _소분류, value); } }
        private string _소분류코드; public string 소분류코드 { get { return _소분류코드; } set { Set(ref _소분류코드, value); } }
        private string _속성; public string 속성 { get { return _속성; } set { Set(ref _속성, value); } }
        private string _속성코드; public string 속성코드 { get { return _속성코드; } set { Set(ref _속성코드, value); } }

        private string _기본옵션; public string 기본옵션 { get { return _기본옵션; } set { Set(ref _기본옵션, value); } }
        private int _옵션갯수; public int 옵션갯수 { get { return _옵션갯수; } set { Set(ref _옵션갯수, value); } }

        private string _옵션1; public string 옵션1 { get { return _옵션1; } set { Set(ref _옵션1, value); } }
        private decimal _옵션1가격; public decimal 옵션1가격 { get { return _옵션1가격; } set { Set(ref _옵션1가격, value); } }
        private string _옵션2; public string 옵션2 { get { return _옵션2; } set { Set(ref _옵션2, value); } }
        private decimal _옵션2가격; public decimal 옵션2가격 { get { return _옵션2가격; } set { Set(ref _옵션2가격, value); } }
        private string _옵션3; public string 옵션3 { get { return _옵션3; } set { Set(ref _옵션3, value); } }
        private decimal _옵션3가격; public decimal 옵션3가격 { get { return _옵션3가격; } set { Set(ref _옵션3가격, value); } }
        private string _옵션4; public string 옵션4 { get { return _옵션4; } set { Set(ref _옵션4, value); } }
        private decimal _옵션4가격; public decimal 옵션4가격 { get { return _옵션4가격; } set { Set(ref _옵션4가격, value); } }
        private string _옵션5; public string 옵션5 { get { return _옵션5; } set { Set(ref _옵션5, value); } }
        private decimal _옵션5가격; public decimal 옵션5가격 { get { return _옵션5가격; } set { Set(ref _옵션5가격, value); } }

        private string _보증기간; public string 보증기간 { get { return _보증기간; } set { Set(ref _보증기간, value); } }
        private bool _단종여부; public bool 단종여부 { get { return _단종여부; } set { Set(ref _단종여부, value); } }

        private string _이미지1첨부; public string 이미지1첨부 { get { return _이미지1첨부; } set { Set(ref _이미지1첨부, value); } }
        private string _이미지2첨부; public string 이미지2첨부 { get { return _이미지2첨부; } set { Set(ref _이미지2첨부, value); } }
        private string _이미지3첨부; public string 이미지3첨부 { get { return _이미지3첨부; } set { Set(ref _이미지3첨부, value); } }

        private string _특이사항; public string 특이사항 { get { return _특이사항; } set { Set(ref _특이사항, value); } }

        public string 도매가표시 { get { return FormatWon(도매가); } }
        public string 소매가표시 { get { return FormatWon(소매가); } }
        public string 차익금표시 { get { return FormatWon(차익금); } }

        private static string FormatWon(decimal v)
        {
            return string.Format(CultureInfo.GetCultureInfo("ko-KR"), "₩{0:N0}", v);
        }

        public ProductRow Clone() { return (ProductRow)this.MemberwiseClone(); }

        public void CopyFrom(ProductRow src)
        {
            번호 = src.번호; 품명 = src.품명; 제조사 = src.제조사; 제조사코드 = src.제조사코드;
            공급업체명 = src.공급업체명; 공급업체코드 = src.공급업체코드; 공급업체번호 = src.공급업체번호;
            원산지 = src.원산지; 인증 = src.인증; 인증번호 = src.인증번호;
            도매가 = src.도매가; 소매가 = src.소매가; 차익금 = src.차익금;
            대분류 = src.대분류; 대분류코드 = src.대분류코드; 중분류 = src.중분류; 중분류코드 = src.중분류코드;
            소분류 = src.소분류; 소분류코드 = src.소분류코드; 속성 = src.속성; 속성코드 = src.속성코드;
            기본옵션 = src.기본옵션; 옵션갯수 = src.옵션갯수;
            옵션1 = src.옵션1; 옵션1가격 = src.옵션1가격;
            옵션2 = src.옵션2; 옵션2가격 = src.옵션2가격;
            옵션3 = src.옵션3; 옵션3가격 = src.옵션3가격;
            옵션4 = src.옵션4; 옵션4가격 = src.옵션4가격;
            옵션5 = src.옵션5; 옵션5가격 = src.옵션5가격;
            보증기간 = src.보증기간; 단종여부 = src.단종여부;
            이미지1첨부 = src.이미지1첨부; 이미지2첨부 = src.이미지2첨부; 이미지3첨부 = src.이미지3첨부;
            특이사항 = src.특이사항;
        }
    }
}