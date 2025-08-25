using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ULTRA.Helpers;
using ULTRA.ViewModels.Base;

namespace ULTRA.ViewModels
{
    public sealed class PosEstimateViewModel : ObservableObject
    {
        // 전체 제품 목록
        private List<Product> _allProducts;

        // 화면에 표시되는 제품 목록
        private ObservableCollection<Product> _displayedProducts;
        public ObservableCollection<Product> DisplayedProducts
        {
            get => _displayedProducts;
            set => Set(ref _displayedProducts, value);
        }

        // 장바구니
        private ObservableCollection<Product> _cartItems;
        public ObservableCollection<Product> CartItems
        {
            get => _cartItems;
            set => Set(ref _cartItems, value);
        }

        // 검색/분류
        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set { Set(ref _searchText, value); ApplyFilters(); }
        }

        public ObservableCollection<string> MajorCategories { get; set; }
        public ObservableCollection<string> MiddleCategories { get; set; }

        private string _selectedMajorCategory;
        public string SelectedMajorCategory
        {
            get => _selectedMajorCategory;
            set { Set(ref _selectedMajorCategory, value); ApplyFilters(); }
        }

        private string _selectedMiddleCategory;
        public string SelectedMiddleCategory
        {
            get => _selectedMiddleCategory;
            set { Set(ref _selectedMiddleCategory, value); ApplyFilters(); }
        }

        // 페이지
        private int _currentPage = 1;
        public int CurrentPage
        {
            get => _currentPage;
            set => Set(ref _currentPage, value);
        }

        private const int _itemsPerPage = 5;
        private int _totalPages;
        public int TotalPages
        {
            get => _totalPages;
            set => Set(ref _totalPages, value);
        }

        // 총액
        public int TotalAmount => CartItems.Sum(p => p.소계);

        // 창고 재고
        private Dictionary<string, int> _warehouseStock = new Dictionary<string, int>();

        // Commands
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand AddToCartCommand { get; }
        public ICommand RemoveFromCartCommand { get; }
        public ICommand ClearCartCommand { get; }
        public ICommand IncreaseQuantityCommand { get; }
        public ICommand DecreaseQuantityCommand { get; }
        public ICommand CheckoutCommand { get; }

        public PosEstimateViewModel()
        {
            _allProducts = new List<Product>();
            _displayedProducts = new ObservableCollection<Product>();
            _cartItems = new ObservableCollection<Product>();

            MajorCategories = new ObservableCollection<string>();
            MiddleCategories = new ObservableCollection<string>();

            PreviousPageCommand = new RelayCommand(PreviousPage, () => CurrentPage > 1);
            NextPageCommand = new RelayCommand(NextPage, () => CurrentPage < TotalPages);
            AddToCartCommand = new RelayCommand(AddToCart);
            RemoveFromCartCommand = new RelayCommand(RemoveFromCart);
            ClearCartCommand = new RelayCommand(ClearCart);
            IncreaseQuantityCommand = new RelayCommand(IncreaseQuantity);
            DecreaseQuantityCommand = new RelayCommand(DecreaseQuantity);
            CheckoutCommand = new RelayCommand(Checkout);

            LoadWarehouseStock();
            LoadProductsFromCsv();
        }

        private Dictionary<string, (int Quantity, string Location)> _warehouseData = new Dictionary<string, (int, string)>();

        private void LoadWarehouseStock()
        {
            string[] paths = new string[]
            {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data\\ULTRA-창고현황-sample.csv"),
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\Data\\ULTRA-창고현황-sample.csv")
            };

            string filePath = paths.FirstOrDefault(File.Exists);
            if (filePath == null) return;

            var lines = File.ReadAllLines(filePath).Skip(1);
            _warehouseData.Clear();

            foreach (var line in lines)
            {
                var v = line.Split(',');
                if (v.Length < 6) continue;

                string productNo = v[3];
                if (int.TryParse(v[5], out int qty) && qty > 0)
                {
                    // Combine Warehouse, Column, and Slot to get the location code
                    string location = $"{v[0]}{v[1]}{v[2]}";
                    _warehouseData[productNo] = (qty, location);
                }
            }
        }

        private void LoadProductsFromCsv()
        {
            string[] paths = new string[]
            {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data\\ULTRA-제품정보-sample.csv"),
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\Data\\ULTRA-제품정보-sample.csv")
            };
            string filePath = paths.FirstOrDefault(File.Exists);
            if (filePath == null) return;

            var lines = File.ReadAllLines(filePath).Skip(1);
            _allProducts = lines.Select(line =>
            {
                var v = line.Split(',');
                string 번호 = v[0];

                if (!_warehouseData.ContainsKey(번호)) return null;

                var stockData = _warehouseData[번호];

                return new Product
                {
                    번호 = 번호,
                    품명 = v[1],
                    제조사 = v[2],
                    공급업체명 = v[4],
                    원산지 = v[7],
                    소매가 = int.TryParse(v[11], out int r) ? r : 0,
                    대분류 = v[13],
                    중분류 = v[15],
                    기본옵션 = v[21],
                    이미지1첨부 = v[35],
                    수량 = 1,
                    재고 = stockData.Quantity,
                    위치코드 = stockData.Location,

                    // 정확한 인덱스로 수정
                    도매가격 = int.TryParse(v[10], out int w) ? w : 0,
                    속성 = v[19],
                    속성코드 = v[20],
                    보증기간 = v[33],
                    선택옵션 = v.Length > 23 ? v[23] : "", // 옵션 필드가 없는 경우를 대비
                    옵션가 = v.Length > 24 && int.TryParse(v[24], out int op) ? op : 0
                };
            }).Where(x => x != null).ToList();

            MajorCategories = new ObservableCollection<string>(new[] { "전체" }.Concat(_allProducts.Select(p => p.대분류).Distinct()));
            MiddleCategories = new ObservableCollection<string>(new[] { "전체" }.Concat(_allProducts.Select(p => p.중분류).Distinct()));

            ApplyFilters();
        }
        private void ApplyFilters()
        {
            IEnumerable<Product> filtered = _allProducts;

            if (!string.IsNullOrWhiteSpace(SearchText))
                filtered = filtered.Where(p => p.품명.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(SelectedMajorCategory) && SelectedMajorCategory != "전체")
                filtered = filtered.Where(p => p.대분류 == SelectedMajorCategory);

            if (!string.IsNullOrEmpty(SelectedMiddleCategory) && SelectedMiddleCategory != "전체")
                filtered = filtered.Where(p => p.중분류 == SelectedMiddleCategory);

            var list = filtered.ToList();
            TotalPages = (int)Math.Ceiling((double)list.Count / _itemsPerPage);
            if (CurrentPage > TotalPages) CurrentPage = 1;

            DisplayedProducts.Clear();
            foreach (var item in list.Skip((CurrentPage - 1) * _itemsPerPage).Take(_itemsPerPage))
                DisplayedProducts.Add(item);
        }

        private void PreviousPage() { if (CurrentPage > 1) { CurrentPage--; ApplyFilters(); } }
        private void NextPage() { if (CurrentPage < TotalPages) { CurrentPage++; ApplyFilters(); } }

        private void AddToCart(object parameter)
        {
            if (parameter is Product product)
            {
                if (!CartItems.Contains(product))
                    CartItems.Add(product);
                Raise(nameof(TotalAmount));
            }
        }

        private void RemoveFromCart(object parameter)
        {
            if (parameter is Product product && CartItems.Contains(product))
            {
                CartItems.Remove(product);
                Raise(nameof(TotalAmount));
            }
        }

        private void ClearCart()
        {
            if (CartItems.Count == 0) return;
            if (MessageBox.Show("장바구니를 모두 삭제하시겠습니까?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                CartItems.Clear();
                Raise(nameof(TotalAmount));
            }
        }

        private void IncreaseQuantity(object parameter)
        {
            if (parameter is Product product && product.수량 < product.재고)
            {
                product.수량++;
                Raise(nameof(TotalAmount));
            }
        }

        private void DecreaseQuantity(object parameter)
        {
            if (parameter is Product product && product.수량 > 1)
            {
                product.수량--;
                Raise(nameof(TotalAmount));
            }
        }

        private void Checkout()
        {
            if (CartItems.Count == 0) return;

            var inputName = Microsoft.VisualBasic.Interaction.InputBox("이름을 입력하세요:", "결제", "");
            var inputNumber = Microsoft.VisualBasic.Interaction.InputBox("번호를 입력하세요:", "결제", "");

            if (string.IsNullOrEmpty(inputName) || string.IsNullOrEmpty(inputNumber)) return;

            string orderNumber = $"EST-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";

            string outFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data\\ULTRA-입출고내역-sample.csv");

            using (var sw = new StreamWriter(outFilePath, true))
            {
                int itemIndex = 1;
                var now = DateTime.Now;

                string memo = $"{inputName}:{inputNumber}";
                string partnerCode = "NORMAL";

                foreach (var item in CartItems)
                {
                    int totalAmount = item.수량 * item.소매가;

                    string line = $"출고,OUT,{orderNumber},{itemIndex},{item.번호},{totalAmount},{item.번호},{item.품명},{item.속성},{item.속성코드},{item.수량},{item.도매가격},{item.소매가},{item.선택옵션},{item.옵션가},{item.보증기간},{now:yyyy-MM-dd},{now:HH:mm:ss},{item.위치코드},{partnerCode},{memo}";

                    sw.WriteLine(line);

                    if (_warehouseData.ContainsKey(item.번호))
                    {
                        var currentStock = _warehouseData[item.번호].Quantity;
                        _warehouseData[item.번호] = (currentStock - item.수량, _warehouseData[item.번호].Location);
                    }

                    itemIndex++;
                }
            }

            // 총 결제 금액을 포함한 메시지 박스 표시
            MessageBox.Show($"결제가 완료되었습니다.\n\n총 결제금액: {TotalAmount:N0}원", "알림", MessageBoxButton.OK, MessageBoxImage.Information);

            UpdateWarehouseCsv();
            CartItems.Clear();
            Raise(nameof(TotalAmount));
            ApplyFilters();
        }

        private void UpdateWarehouseCsv()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data\\ULTRA-창고현황-sample.csv");
            if (!File.Exists(path)) return;

            var lines = File.ReadAllLines(path).ToList();
            for (int i = 1; i < lines.Count; i++)
            {
                var v = lines[i].Split(',');
                string no = v[3];
                if (_warehouseData.ContainsKey(no))
                {
                    v[5] = _warehouseData[no].Quantity.ToString();
                }
                lines[i] = string.Join(",", v);
            }
            File.WriteAllLines(path, lines);
        }

        public class Product : ObservableObject
        {
            public string 번호 { get; set; }
            public string 품명 { get; set; }
            public string 제조사 { get; set; }
            public string 공급업체명 { get; set; }
            public string 원산지 { get; set; }
            public int 소매가 { get; set; }
            public string 대분류 { get; set; }
            public string 중분류 { get; set; }
            public string 기본옵션 { get; set; }
            public string 이미지1첨부 { get; set; }
            public int 재고 { get; set; }

            // Add these new properties
            public string 위치코드 { get; set; }
            public string 보증기간 { get; set; }

            public string 속성 { get; set; }
            public string 속성코드 { get; set; }
            public int 도매가격 { get; set; }
            public string 선택옵션 { get; set; }
            public int 옵션가 { get; set; }

            private int _수량;
            public int 수량
            {
                get => _수량;
                set
                {
                    Set(ref _수량, value);
                    Raise(nameof(소계));
                }
            }

            public int 소계 => 소매가 * 수량;

            public BitmapImage 이미지1
            {
                get
                {
                    if (string.IsNullOrEmpty(이미지1첨부)) return null;
                    try
                    {
                        // 실행 파일 경로를 기준으로 assets 폴더를 직접 참조
                        // 예: "assets/products/PT001/P-002.jpg"
                        string relativePath = 이미지1첨부.Replace('/', Path.DirectorySeparatorChar);
                        string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

                        if (!File.Exists(fullPath))
                        {
                            // 경로를 찾지 못했을 경우, 디버깅을 위해 로깅 또는 메시지 박스를 추가할 수 있습니다.
                            // MessageBox.Show($"File not found: {fullPath}");
                            return null;
                        }

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        return bitmap;
                    }
                    catch (Exception ex)
                    {
                        // 예외 발생 시 디버깅을 위해 로깅
                        // MessageBox.Show($"Error loading image: {ex.Message}");
                        return null;
                    }
                }
            }
        }
    }
}
