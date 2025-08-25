using System;
using System.Windows;
using ULTRA.Services;
using ULTRA.Stores;
using ULTRA.ViewModels;
using ULTRA.ViewModels.Base;

namespace ULTRA
{
    public partial class App : Application
    {
        public static NavigationStore NavStore { get; private set; } = null!;
        public static INavigationService Navigator { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var store = new NavigationStore();
            var nav = new NavigationService(store);

            NavStore = store;
            Navigator = nav;

            Func<string, object> vmFactory = key => key switch
            {
                "Dashboard" => new DashboardViewModel(nav),
                "Products" => new ProductsViewModel(),
                "Warehouse" => new WarehouseViewModel(),
                "Movements" => new MovementsViewModel(nav),
                "PosEstimate" => new PosEstimateViewModel(),
                "Partners" => new PartnersViewModel(nav),
                "ReceiptsInvoices" => new ReceiptsInvoicesViewModel(),
                "Settings" => new SettingsViewModel(),
                "Logs" => new LogsViewModel(),
                _ => new DashboardViewModel(nav)
            };

            var mainVm = new MainViewModel(store, vmFactory);

            // NavigateTo 메서드 대신, 스토어에 직접 첫 화면을 할당합니다.
            store.CurrentViewModel = (ObservableObject)vmFactory("Dashboard");

            var win = new MainWindow { DataContext = mainVm };
            win.Show();
        }
    }
}