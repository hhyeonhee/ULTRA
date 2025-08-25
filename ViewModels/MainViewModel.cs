using System;
using ULTRA.Services;
using ULTRA.ViewModels.Base;
using ULTRA.Stores;

namespace ULTRA.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        public NavigationStore Nav { get; }
        public object? Current => Nav.CurrentViewModel;

        public RelayCommand GoDashboard { get; }
        public RelayCommand GoProducts { get; }
        public RelayCommand GoWarehouse { get; }
        public RelayCommand GoMovements { get; }
        public RelayCommand GoPosEstimate { get; }
        public RelayCommand GoPartners { get; }
        public RelayCommand GoReceiptsInvoices { get; }
        public RelayCommand GoSettings { get; }
        public RelayCommand GoLogs { get; }


        public MainViewModel(NavigationStore store, Func<string, object> vmFactory)
        {
            Nav = store;

            // CurrentViewModel이 변경될 때마다 Current 속성도 변경되었음을 알립니다.
            Nav.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NavigationStore.CurrentViewModel))
                {
                    Raise(nameof(Current));
                }
            };

            GoDashboard = new(() => Nav.CurrentViewModel = vmFactory("Dashboard"));
            GoProducts = new(() => Nav.CurrentViewModel = vmFactory("Products"));
            GoWarehouse = new(() => Nav.CurrentViewModel = vmFactory("Warehouse"));
            GoMovements = new(() => Nav.CurrentViewModel = vmFactory("Movements"));
            GoPosEstimate = new(() => Nav.CurrentViewModel = vmFactory("PosEstimate"));
            GoPartners = new(() => Nav.CurrentViewModel = vmFactory("Partners"));
            GoReceiptsInvoices = new(() => Nav.CurrentViewModel = vmFactory("ReceiptsInvoices"));
            GoSettings = new(() => Nav.CurrentViewModel = vmFactory("Settings"));
            GoLogs = new(() => Nav.CurrentViewModel = vmFactory("Logs"));
        }
    }
}