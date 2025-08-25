using ULTRA.ViewModels.Base;

namespace ULTRA.Stores // 네임스페이스를 Services에서 Stores로 변경
{
    public sealed class NavigationStore : ObservableObject
    {
        private object _currentViewModel;
        public object CurrentViewModel
        {
            get => _currentViewModel;
            set => Set(ref _currentViewModel, value);
        }
    }
}