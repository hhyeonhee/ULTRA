using ULTRA.Stores;
using ULTRA.ViewModels.Base;

namespace ULTRA.Services
{
    public interface INavigationService
    {
        void Navigate(object viewModel);
    }

    public sealed class NavigationService : INavigationService
    {
        private readonly NavigationStore _store;
        public NavigationService(NavigationStore store) => _store = store;

        public void Navigate(object vm)
        {
            if (vm is ObservableObject observableVm)
            {
                _store.CurrentViewModel = observableVm;
            }
        }
    }
}