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
using Microsoft.VisualBasic;                 // Interaction.InputBox
using ULTRA.Helpers;                        // CsvDictExtensions
using ULTRA.ViewModels.Base;                // ObservableObject, RelayCommand

namespace ULTRA.ViewModels
{
    public sealed class WarehouseViewModel : ObservableObject
    {
        // ===== 내부 모델 =====
        public sealed class Product : ObservableObject
        {
            public string No { get => _no; set => Set(ref _no, value ?? ""); }
            public string Name { get => _name; set => Set(ref _name, value ?? ""); }
            public string Attr { get => _attr; set => Set(ref _attr, value ?? ""); }
            public string Category { get => _cat; set => Set(ref _cat, value ?? ""); }
            public string SubCategory { get => _scat; set => Set(ref _scat, value ?? ""); }
            public string Unit { get => _unit; set => Set(ref _unit, value ?? ""); } // From Products CSV
            public string WarehouseUnit { get => _whUnit; set => Set(ref _whUnit, value); } // From Status CSV

            public int TotalQty { get => _total; set { if (Set(ref _total, value)) Raise(nameof(TotalQtyDisplay)); } }
            public string TotalQtyDisplay => TotalQty.ToString("N0", CultureInfo.CurrentCulture);

            // 앱에서 사용하지 않는 나머지 CSV 데이터를 보관하기 위한 저장소
            public Dictionary<string, string> OtherData { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string _no = "", _name = "", _attr = "", _cat = "", _scat = "", _unit = "", _whUnit = "";
            int _total = 0;
        }

        public sealed class SlotKey : IEquatable<SlotKey>
        {
            public string Warehouse;
            public int Col;     // 열 번호
            public int Slot;    // 1..10

            public bool Equals(SlotKey o) =>
                o != null &&
                string.Equals(Warehouse ?? "", o.Warehouse ?? "", StringComparison.OrdinalIgnoreCase) &&
                Col == o.Col && Slot == o.Slot;
            public override bool Equals(object obj) => Equals(obj as SlotKey);
            public override int GetHashCode() => ((Warehouse ?? "").ToLowerInvariant(), Col, Slot).GetHashCode();
        }

        public sealed class SlotData : ObservableObject
        {
            public string ProductNo { get => _pno; set => Set(ref _pno, value ?? ""); }
            public string ProductName { get => _pname; set => Set(ref _pname, value ?? ""); }
            public int Qty { get => _qty; set => Set(ref _qty, value); }
            public string Unit { get => _unit; set => Set(ref _unit, value ?? ""); }
            public string Memo { get => _memo; set => Set(ref _memo, value ?? ""); }

            string _pno = "", _pname = "", _unit = "", _memo = "";
            int _qty = 0;
        }

        public sealed class SlotBox : ObservableObject
        {
            public int Col { get => _col; set => Set(ref _col, value); }
            public int Index { get => _idx; set => Set(ref _idx, value); } // 1..10
            public SlotData Data { get => _data; private set => Set(ref _data, value); }

            public string Name => Data?.ProductName ?? "";
            public string QtyUnit =>
                Data == null ? "" :
                string.Format(CultureInfo.GetCultureInfo("ko-KR"),
                              "{0:N0}{1}",
                              Data.Qty,
                              string.IsNullOrWhiteSpace(Data.Unit) ? "" : $" {Data.Unit}");
            public bool IsEmpty => Data == null || (string.IsNullOrWhiteSpace(Data.ProductNo) && string.IsNullOrWhiteSpace(Data.ProductName));

            public void SetData(SlotData d)
            {
                Data = d;
                Raise(nameof(Name)); Raise(nameof(QtyUnit)); Raise(nameof(IsEmpty));
            }

            int _col, _idx;
            SlotData _data;
        }

        public sealed class ColumnTile : ObservableObject
        {
            public int Col { get => _col; set { if (Set(ref _col, value)) Raise(nameof(Header)); } }
            public string Alias { get => _alias; set { if (Set(ref _alias, value ?? "")) Raise(nameof(Header)); } }
            public ObservableCollection<SlotBox> Slots { get; } = new();
            public string Header => string.IsNullOrWhiteSpace(Alias) ? $"열 {Col:00}" : $"열 {Col:00}  ({Alias})";
            int _col; string _alias = "";
        }

        // ===== 바인딩 소스 =====
        public ObservableCollection<Product> Products { get; } = new();
        public ListCollectionView ProductsView { get; private set; }

        public ObservableCollection<string> Warehouses { get; } = new();
        public string SelectedWarehouse
        {
            get => _selectedWarehouse;
            set
            {
                if (Set(ref _selectedWarehouse, value ?? "")) { SyncColCountForWarehouse(); RebuildColumns(); RecalcTotals(); }
            }
        }
        string _selectedWarehouse = "";

        // 좌측 검색/필터
        public string SearchText { get => _search; set { if (Set(ref _search, value)) ProductsView?.Refresh(); } }
        public ObservableCollection<string> CategoryOptions { get; } = new(new[] { "전체" });
        public ObservableCollection<string> SubCategoryOptions { get; } = new(new[] { "전체" });
        public string SelectedCategory
        {
            get => _selCat;
            set { if (Set(ref _selCat, value ?? "전체")) { RebuildSubCategoryOptions(); ProductsView?.Refresh(); } }
        }
        public string SelectedSubCategory { get => _selSub; set { if (Set(ref _selSub, value ?? "전체")) ProductsView?.Refresh(); } }
        string _search = "", _selCat = "전체", _selSub = "전체";

        // 열 수 (기본 4)
        public int ColCount { get => _colCount; set { if (Set(ref _colCount, Math.Max(1, value))) { _zoneCols[SelectedWarehouse] = _colCount; RebuildColumns(); } } }
        int _colCount = 4;

        public ObservableCollection<ColumnTile> Columns { get; } = new();

        // ===== 명령 =====
        public ICommand AddWarehouseCommand { get; }
        public ICommand RemoveWarehouseCommand { get; }
        public ICommand RenameWarehouseCommand { get; }

        public ICommand AddColumnCommand { get; }
        public ICommand RemoveColumnCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public ICommand AddStockCommand { get; }          // 수량 입력으로 추가
        public ICommand SetUnitCommand { get; }           // 단위 추가/변경
        public ICommand ClearSlotCommand { get; }         // (col, slot)
        public ICommand RenameColumnCommand { get; }      // col

        // ===== CSV 경로 =====
        readonly string _productsCsvPath;
        readonly string _zonesCsvPath;
        readonly string _statusCsvPath;

        // 구역: 창고 → 열수 & 열이름
        readonly Dictionary<string, int> _zoneCols = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<(string wh, int col), string> _colAliases = new();

        // 현황: (wh,col,slot) → data
        readonly Dictionary<SlotKey, SlotData> _status = new();

        public WarehouseViewModel()
        {
            _productsCsvPath = ResolveCsv(new[]
            { "ULTRA-제품정보.csv","ULTRA-제품정보-sample.csv","ULTRA - 제품정보.csv","ULTRA-제품정보 (샘플).csv" })
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ULTRA-제품정보-sample.csv");

            _zonesCsvPath = ResolveCsv(new[]
            { "ULTRA-창고구역.csv","ULTRA - 창고구역.csv" })
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ULTRA-창고구역.csv");

            _statusCsvPath = ResolveCsv(new[]
            { "ULTRA-창고현황.csv","ULTRA-창고현황-sample.csv","ULTRA - 창고현황.csv" })
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "ULTRA-창고현황-sample.csv");

            LoadAll();

            AddWarehouseCommand = new RelayCommand(AddWarehouse);
            RemoveWarehouseCommand = new RelayCommand(RemoveWarehouse, () => !string.IsNullOrWhiteSpace(SelectedWarehouse));
            RenameWarehouseCommand = new RelayCommand(RenameWarehouse, () => !string.IsNullOrWhiteSpace(SelectedWarehouse));

            AddColumnCommand = new RelayCommand(() => ColCount += 1);
            RemoveColumnCommand = new RelayCommand(() => ColCount = Math.Max(1, ColCount - 1));

            SaveCommand = new RelayCommand(SaveAll);
            CancelCommand = new RelayCommand(CancelChanges);

            AddStockCommand = new RelayCommand<object>(AddStockByInput);
            SetUnitCommand = new RelayCommand<object>(SetUnitForProduct);

            ClearSlotCommand = new RelayCommand<object>(o => { if (o is Tuple<int, int> t) ClearSlot(t.Item1, t.Item2); });
            RenameColumnCommand = new RelayCommand<object>(o => { if (o is int c) RenameColumn(c); });
        }

        private void CancelChanges()
        {
            // 현재 선택된 창고를 기억
            var currentWarehouse = SelectedWarehouse;

            // 창고/열 정보와 슬롯 정보를 파일에서 다시 로드하여 변경사항을 되돌림
            LoadZones();
            LoadStatus();

            // 이전에 선택했던 창고가 여전히 존재하면 다시 선택
            if (Warehouses.Contains(currentWarehouse))
            {
                SelectedWarehouse = currentWarehouse;
            }
            else
            {
                // (예: 추가했다가 취소한 경우) 사라졌으면 첫 번째 창고를 선택
                SelectedWarehouse = Warehouses.FirstOrDefault();
            }
        }

        // ===== 로드/저장 =====
        void LoadAll()
        {
            LoadProducts(); InitProductView();
            LoadZones(); LoadStatus();

            if (Warehouses.Count == 0) Warehouses.Add("기본창고");
            SelectedWarehouse = Warehouses[0];
            SyncColCountForWarehouse();
            RebuildColumns();
            RecalcTotals();
        }

        void LoadProducts()
        {
            Products.Clear();
            if (!File.Exists(_productsCsvPath)) return;

            var propertyMap = new Dictionary<string, Action<Product, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "번호", (p, val) => p.No = val }, { "제품코드", (p, val) => p.No = val }, { "코드", (p, val) => p.No = val }, { "productno", (p, val) => p.No = val },
                { "품명", (p, val) => p.Name = val }, { "제품명", (p, val) => p.Name = val }, { "name", (p, val) => p.Name = val },
                { "속성", (p, val) => p.Attr = val }, { "attribute", (p, val) => p.Attr = val },
                { "대분류", (p, val) => p.Category = val }, { "category", (p, val) => p.Category = val }, { "대", (p, val) => p.Category = val },
                { "중분류", (p, val) => p.SubCategory = val }, { "subcategory", (p, val) => p.SubCategory = val }, { "중", (p, val) => p.SubCategory = val },
                { "단위", (p, val) => p.Unit = val }, { "unit", (p, val) => p.Unit = val },
            };

            var cats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rowDict in CsvRead(_productsCsvPath))
            {
                var product = new Product();

                foreach (var kvp in rowDict)
                {
                    if (propertyMap.TryGetValue(kvp.Key, out var setter))
                    {
                        setter(product, kvp.Value?.Trim() ?? "");
                    }
                    else
                    {
                        product.OtherData[kvp.Key] = kvp.Value;
                    }
                }

                if (string.IsNullOrWhiteSpace(product.No) || string.IsNullOrWhiteSpace(product.Name)) continue;

                Products.Add(product);

                if (!string.IsNullOrWhiteSpace(product.Category)) cats.Add(product.Category.Trim());
                if (!string.IsNullOrWhiteSpace(product.SubCategory)) scats.Add(product.SubCategory.Trim());
            }

            CategoryOptions.Clear(); CategoryOptions.Add("전체");
            foreach (var c in cats.OrderBy(x => x)) CategoryOptions.Add(c);

            SubCategoryOptions.Clear(); SubCategoryOptions.Add("전체");
            foreach (var s in scats.OrderBy(x => x)) SubCategoryOptions.Add(s);
        }

        void SaveProducts()
        {
            var dir = Path.GetDirectoryName(_productsCsvPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            List<string> headers;
            if (File.Exists(_productsCsvPath))
            {
                using var sr = new StreamReader(_productsCsvPath, DetectEncoding(_productsCsvPath));
                var headerLine = sr.ReadLine() ?? "";
                headers = SplitCsv(headerLine).Select(h => h.Trim()).ToList();
            }
            else
            {
                headers = new List<string> { "번호", "품명", "속성", "대분류", "중분류", "단위" };
            }

            var valueGetters = new Dictionary<string, Func<Product, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "번호", p => p.No }, { "제품코드", p => p.No }, { "코드", p => p.No }, { "productno", p => p.No },
                { "품명", p => p.Name }, { "제품명", p => p.Name }, { "name", p => p.Name },
                { "속성", p => p.Attr }, { "attribute", p => p.Attr },
                { "대분류", p => p.Category }, { "category", p => p.Category }, { "대", p => p.Category },
                { "중분류", p => p.SubCategory }, { "subcategory", p => p.SubCategory }, { "중", p => p.SubCategory },
                { "단위", p => p.Unit }, { "unit", p => p.Unit },
            };

            using (var sw = new StreamWriter(_productsCsvPath, false, new UTF8Encoding(true)))
            {
                sw.WriteLine(string.Join(",", headers.Select(h => Csv(h))));

                foreach (var p in Products)
                {
                    var rowValues = new List<string>();
                    foreach (var header in headers)
                    {
                        string value;
                        if (valueGetters.TryGetValue(header, out var getter))
                        {
                            value = getter(p);
                        }
                        else
                        {
                            p.OtherData.TryGetValue(header, out value);
                        }
                        rowValues.Add(Csv(value ?? ""));
                    }
                    sw.WriteLine(string.Join(",", rowValues));
                }
            }
        }

        // ... 이하 기존 코드와 동일 ...

        void InitProductView()
        {
            ProductsView = (ListCollectionView)CollectionViewSource.GetDefaultView(Products);
            ProductsView.Filter = o =>
            {
                if (o is not Product p) return false;

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var t = SearchText.Trim();
                    bool hit = (p.No?.IndexOf(t, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                               || (p.Name?.IndexOf(t, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                               || (p.Attr?.IndexOf(t, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
                    if (!hit) return false;
                }
                if (!string.Equals(SelectedCategory, "전체", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(p.Category ?? "", SelectedCategory ?? "", StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(SelectedSubCategory, "전체", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(p.SubCategory ?? "", SelectedSubCategory ?? "", StringComparison.OrdinalIgnoreCase)) return false;

                return true;
            };
        }

        void RebuildSubCategoryOptions()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Products)
            {
                if (!string.Equals(SelectedCategory, "전체", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(p.Category ?? "", SelectedCategory ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(p.SubCategory)) set.Add(p.SubCategory);
            }
            SubCategoryOptions.Clear(); SubCategoryOptions.Add("전체");
            foreach (var s in set.OrderBy(x => x)) SubCategoryOptions.Add(s);
            if (!SubCategoryOptions.Contains(SelectedSubCategory)) SelectedSubCategory = "전체";
        }

        void LoadZones()
        {
            Warehouses.Clear(); _zoneCols.Clear(); _colAliases.Clear();
            Warehouses.Add("기본창고");
            _zoneCols["기본창고"] = 4;
            if (!File.Exists(_zonesCsvPath)) return;
            foreach (var r in CsvRead(_zonesCsvPath))
            {
                var wh = r.GetAnyOrEmpty("창고", "warehouse");
                if (string.IsNullOrWhiteSpace(wh)) continue;
                int cols = ParseInt(r.GetAnyOrEmpty("열수", "열", "columns", "colcount"));
                if (cols <= 0) cols = 4;
                if (!_zoneCols.ContainsKey(wh)) _zoneCols[wh] = cols;
                if (!Warehouses.Contains(wh)) Warehouses.Add(wh);
                var colStr = r.GetAnyOrEmpty("열번호", "col");
                var alias = r.GetAnyOrEmpty("열이름", "이름", "alias");
                if (int.TryParse(colStr, out var c) && !string.IsNullOrWhiteSpace(alias))
                    _colAliases[(wh, c)] = alias.Trim();
            }
        }

        void LoadStatus()
        {
            _status.Clear();
            if (!File.Exists(_statusCsvPath)) return;
            foreach (var r in CsvRead(_statusCsvPath))
            {
                var wh = r.GetAnyOrEmpty("창고", "warehouse");
                int col = ParseInt(r.GetAnyOrEmpty("열", "column", "col"));
                int slot = ParseInt(r.GetAnyOrEmpty("슬롯", "slot", "칸"));
                if (string.IsNullOrWhiteSpace(wh) || col <= 0 || slot <= 0) continue;
                var key = new SlotKey { Warehouse = wh.Trim(), Col = col, Slot = slot };
                var pno = r.GetAnyOrEmpty("제품번호", "번호", "productno");
                var pname = r.GetAnyOrEmpty("제품명", "품명", "name");
                int qty = ParseInt(r.GetAnyOrEmpty("수량", "qty", "quantity"));
                var unit = r.GetAnyOrEmpty("단위", "unit");
                var memo = r.GetAnyOrEmpty("메모", "비고", "memo");
                _status[key] = new SlotData
                {
                    ProductNo = pno?.Trim() ?? "",
                    ProductName = pname?.Trim() ?? "",
                    Qty = qty,
                    Unit = unit?.Trim() ?? "",
                    Memo = memo?.Trim() ?? ""
                };
            }
        }

        void SaveAll()
        {
            try
            {
                var dirStatus = Path.GetDirectoryName(_statusCsvPath);
                if (!Directory.Exists(dirStatus)) Directory.CreateDirectory(dirStatus);
                using (var sw = new StreamWriter(_statusCsvPath, false, new UTF8Encoding(true)))
                {
                    sw.WriteLine("창고,열,슬롯,제품번호,제품명,수량,단위,메모");
                    foreach (var kv in _status.OrderBy(k => k.Key.Warehouse).ThenBy(k => k.Key.Col).ThenBy(k => k.Key.Slot))
                    {
                        var k = kv.Key; var d = kv.Value;
                        if (d == null || (string.IsNullOrWhiteSpace(d.ProductNo) && string.IsNullOrWhiteSpace(d.ProductName))) continue;
                        sw.WriteLine(string.Join(",", Csv(k.Warehouse), k.Col, k.Slot, Csv(d.ProductNo), Csv(d.ProductName), d.Qty, Csv(d.Unit), Csv(d.Memo)));
                    }
                }
                var dirZones = Path.GetDirectoryName(_zonesCsvPath);
                if (!Directory.Exists(dirZones)) Directory.CreateDirectory(dirZones);
                using (var sw = new StreamWriter(_zonesCsvPath, false, new UTF8Encoding(true)))
                {
                    sw.WriteLine("창고,열수,열번호,열이름");
                    foreach (var wh in Warehouses)
                    {
                        var cols = _zoneCols.TryGetValue(wh, out var cc) ? cc : 4;
                        sw.WriteLine(string.Join(",", Csv(wh), cols, "", ""));
                        for (int c = 1; c <= cols; c++)
                        {
                            if (_colAliases.TryGetValue((wh, c), out var alias) && !string.IsNullOrWhiteSpace(alias))
                                sw.WriteLine(string.Join(",", Csv(wh), "", c, Csv(alias)));
                        }
                    }
                }
                SaveProducts();
                MessageBox.Show("저장되었습니다.", "안내");
            }
            catch (Exception ex)
            {
                MessageBox.Show("저장 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void SyncColCountForWarehouse()
        {
            if (string.IsNullOrWhiteSpace(SelectedWarehouse)) return;
            if (_zoneCols.TryGetValue(SelectedWarehouse, out var cnt)) _colCount = cnt;
            else _colCount = 4;
            Raise(nameof(ColCount));
        }

        void RebuildColumns()
        {
            if (string.IsNullOrWhiteSpace(SelectedWarehouse)) return;
            Columns.Clear();
            for (int c = 1; c <= ColCount; c++)
            {
                var tile = new ColumnTile { Col = c };
                if (_colAliases.TryGetValue((SelectedWarehouse, c), out var alias)) tile.Alias = alias;
                for (int s = 1; s <= 10; s++)
                {
                    var box = new SlotBox { Col = c, Index = s };
                    var key = new SlotKey { Warehouse = SelectedWarehouse, Col = c, Slot = s };
                    if (_status.TryGetValue(key, out var data))
                        box.SetData(data);
                    else
                        box.SetData(null);
                    tile.Slots.Add(box);
                }
                Columns.Add(tile);
            }
        }

        void RecalcTotals()
        {
            var qtyMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var unitMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var query = _status.Where(kv => string.Equals(kv.Key.Warehouse ?? "", SelectedWarehouse ?? "", StringComparison.OrdinalIgnoreCase) && kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.ProductNo));
            foreach (var kv in query)
            {
                var pno = kv.Value.ProductNo;
                qtyMap[pno] = (qtyMap.TryGetValue(pno, out var v) ? v : 0) + Math.Max(0, kv.Value.Qty);
                if (!unitMap.ContainsKey(pno) && !string.IsNullOrWhiteSpace(kv.Value.Unit))
                {
                    unitMap[pno] = kv.Value.Unit;
                }
            }
            foreach (var p in Products)
            {
                p.TotalQty = qtyMap.TryGetValue(p.No, out var v) ? v : 0;
                p.WarehouseUnit = unitMap.TryGetValue(p.No, out var u) ? u : p.Unit;
            }
            ProductsView?.Refresh();
        }

        public void DropProductToCellByProduct(string no, string name, string unit, int col, int slot)
        {
            if (string.IsNullOrWhiteSpace(SelectedWarehouse)) return;
            var key = new SlotKey { Warehouse = SelectedWarehouse, Col = col, Slot = slot };
            if (!_status.TryGetValue(key, out var d)) { d = new SlotData(); _status[key] = d; }
            d.ProductNo = no ?? "";
            d.ProductName = name ?? "";
            var product = Products.FirstOrDefault(p => p.No == no);
            d.Unit = product?.Unit ?? unit;
            if (d.Qty <= 0) d.Qty = 1;
            var box = Columns.SelectMany(t => t.Slots).FirstOrDefault(x => x.Col == col && x.Index == slot);
            box?.SetData(d);
            RecalcTotals();
        }

        public void MoveSlot(int fromCol, int fromSlot, int toCol, int toSlot)
        {
            if (fromCol == toCol && fromSlot == toSlot) return;
            var kFrom = new SlotKey { Warehouse = SelectedWarehouse, Col = fromCol, Slot = fromSlot };
            var kTo = new SlotKey { Warehouse = SelectedWarehouse, Col = toCol, Slot = toSlot };
            _status.TryGetValue(kFrom, out var src);
            if (src == null) return;
            _status.TryGetValue(kTo, out var dst);
            if (dst != null && dst.ProductNo == src.ProductNo)
            {
                dst.Qty += src.Qty;
                _status.Remove(kFrom);
            }
            else
            {
                _status[kTo] = src;
                if (dst != null) _status[kFrom] = dst;
                else _status.Remove(kFrom);
            }
            RebuildColumns();
            RecalcTotals();
        }

        public void ClearSlot(int col, int slot)
        {
            var k = new SlotKey { Warehouse = SelectedWarehouse, Col = col, Slot = slot };
            if (_status.ContainsKey(k))
            {
                _status.Remove(k);
            }
            var box = Columns.SelectMany(t => t.Slots).FirstOrDefault(x => x.Col == col && x.Index == slot);
            box?.SetData(null);
            RecalcTotals();
        }

        void AddStockByInput(object parameter)
        {
            var p = parameter as Product;
            if (p == null) { MessageBox.Show("좌측 리스트에서 재고를 추가할 제품을 먼저 선택하세요.", "안내"); return; }
            var s = Interaction.InputBox("추가할 수량을 입력하세요.", "재고 추가", "1");
            if (string.IsNullOrWhiteSpace(s)) return;
            if (!int.TryParse(s.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var add) || add <= 0)
            { MessageBox.Show("1 이상의 정수를 입력하세요.", "안내"); return; }
            var kvFirst = _status.Where(kv => string.Equals(kv.Key.Warehouse ?? "", SelectedWarehouse ?? "", StringComparison.OrdinalIgnoreCase) && kv.Value != null && string.Equals(kv.Value.ProductNo ?? "", p.No ?? "", StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key.Col).ThenBy(kv => kv.Key.Slot).FirstOrDefault();
            if (!kvFirst.Equals(default(KeyValuePair<SlotKey, SlotData>)))
            {
                kvFirst.Value.Qty += add;
                var b = Columns.SelectMany(t => t.Slots).FirstOrDefault(x => x.Col == kvFirst.Key.Col && x.Index == kvFirst.Key.Slot);
                b?.SetData(kvFirst.Value);
            }
            else
            {
                var empty = Columns.SelectMany(t => t.Slots).FirstOrDefault(x => x.IsEmpty);
                if (empty == null) { MessageBox.Show("빈 칸이 없습니다. '열 +' 로 칸을 늘려주세요.", "안내"); return; }
                var key = new SlotKey { Warehouse = SelectedWarehouse, Col = empty.Col, Slot = empty.Index };
                var d = new SlotData { ProductNo = p.No, ProductName = p.Name, Unit = p.Unit, Qty = add };
                _status[key] = d;
                empty.SetData(d);
            }
            RecalcTotals();
        }

        void SetUnitForProduct(object parameter)
        {
            var p = parameter as Product;
            if (p == null) { MessageBox.Show("좌측 리스트에서 단위를 변경할 제품을 선택하세요.", "안내"); return; }
            var next = Interaction.InputBox("단위를 입력하세요 (예: EA, BOX, mm 등)", "단위 추가/변경", p.WarehouseUnit);
            if (next is null) return;
            next = next.Trim();
            foreach (var kv in _status.Where(kv => string.Equals(kv.Key.Warehouse ?? "", SelectedWarehouse ?? "", StringComparison.OrdinalIgnoreCase) && kv.Value != null && string.Equals(kv.Value.ProductNo ?? "", p.No ?? "", StringComparison.OrdinalIgnoreCase)))
            {
                kv.Value.Unit = next;
            }
            RebuildColumns();
            RecalcTotals();
        }

        void RenameColumn(int col)
        {
            var cur = _colAliases.TryGetValue((SelectedWarehouse, col), out var a) ? a : "";
            var s = Interaction.InputBox("열의 표시 이름을 입력하세요. (비우면 삭제)", $"열 {col:00} 이름", cur);
            if (s is null) return;
            s = s.Trim();
            if (string.IsNullOrWhiteSpace(s)) _colAliases.Remove((SelectedWarehouse, col));
            else _colAliases[(SelectedWarehouse, col)] = s;
            var tile = Columns.FirstOrDefault(t => t.Col == col);
            if (tile != null) tile.Alias = _colAliases.TryGetValue((SelectedWarehouse, col), out var v) ? v : "";
        }

        void AddWarehouse()
        {
            var name = Interaction.InputBox("추가할 창고 이름을 입력하세요.", "창고 추가", "새창고");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (Warehouses.Contains(name, StringComparer.OrdinalIgnoreCase)) { MessageBox.Show("이미 존재하는 창고입니다.", "안내"); return; }
            Warehouses.Add(name);
            _zoneCols[name] = 4;
            SelectedWarehouse = name;
        }

        void RemoveWarehouse()
        {
            if (string.IsNullOrWhiteSpace(SelectedWarehouse)) return;
            if (MessageBox.Show($"[{SelectedWarehouse}] 창고를 삭제할까요?\n(현황 데이터도 함께 삭제됩니다)", "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var keys = _status.Keys.Where(k => string.Equals(k.Warehouse ?? "", SelectedWarehouse ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in keys) _status.Remove(k);
            _zoneCols.Remove(SelectedWarehouse);
            var aliasKeys = _colAliases.Keys.Where(k => string.Equals(k.wh ?? "", SelectedWarehouse ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in aliasKeys) _colAliases.Remove(k);
            var idx = Warehouses.IndexOf(SelectedWarehouse);
            Warehouses.Remove(SelectedWarehouse);
            if (Warehouses.Count == 0) { Warehouses.Add("기본창고"); _zoneCols["기본창고"] = 4; }
            SelectedWarehouse = Warehouses[Math.Max(0, Math.Min(idx, Warehouses.Count - 1))];
        }

        void RenameWarehouse()
        {
            if (string.IsNullOrWhiteSpace(SelectedWarehouse)) return;
            var next = Interaction.InputBox("창고 이름을 변경하세요.", "창고 이름변경", SelectedWarehouse);
            if (string.IsNullOrWhiteSpace(next) || string.Equals(next, SelectedWarehouse, StringComparison.OrdinalIgnoreCase)) return;
            next = next.Trim();
            if (Warehouses.Contains(next, StringComparer.OrdinalIgnoreCase)) { MessageBox.Show("이미 존재하는 이름입니다.", "안내"); return; }
            var old = SelectedWarehouse;
            var i = Warehouses.IndexOf(old); Warehouses[i] = next;
            if (_zoneCols.TryGetValue(old, out var cnt)) _zoneCols.Remove(old);
            _zoneCols[next] = cnt > 0 ? cnt : 4;
            var alias = _colAliases.Where(kv => string.Equals(kv.Key.wh ?? "", old ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var kv in alias) { _colAliases.Remove(kv.Key); _colAliases[(next, kv.Key.col)] = kv.Value; }
            var moved = new Dictionary<SlotKey, SlotData>();
            foreach (var kv in _status)
            {
                var k = kv.Key;
                if (!string.Equals(k.Warehouse ?? "", old ?? "", StringComparison.OrdinalIgnoreCase)) { moved[k] = kv.Value; continue; }
                moved[new SlotKey { Warehouse = next, Col = k.Col, Slot = k.Slot }] = kv.Value;
            }
            _status.Clear(); foreach (var kv in moved) _status[kv.Key] = kv.Value;
            SelectedWarehouse = next;
        }

        static string ResolveCsv(IEnumerable<string> candidates)
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

        static IEnumerable<Dictionary<string, string>> CsvRead(string path)
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

        static Encoding DetectEncoding(string path)
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

        static List<string> SplitCsv(string line)
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

        static int ParseInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Replace(",", "").Trim();
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        static string Csv(string d)
        {
            if (d is null) return "";
            return (d.Contains(",") || d.Contains("\"")) ? "\"" + d.Replace("\"", "\"\"") + "\"" : d;
        }
    }
}