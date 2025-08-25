using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace ULTRA.Views
{
    public partial class PosEstimateView : UserControl
    {
        public PosEstimateView()
        {
            InitializeComponent();

            if (DataContext is ViewModels.PosEstimateViewModel vm)
            {
                vm.CartItems.CollectionChanged += CartItems_CollectionChanged;
            }
        }

        private void CartItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // 외부 ScrollViewer를 직접 사용하여 스크롤
                this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    CartScrollViewer?.ScrollToEnd();
                }));
            }
        }

        /// <summary>
        /// 시각적 트리에서 특정 타입의 자식 요소를 찾습니다.
        /// </summary>
        /// <typeparam name="T">찾을 자식 요소의 타입</typeparam>
        /// <param name="parent">탐색을 시작할 부모 요소</param>
        /// <returns>찾은 자식 요소 또는 null</returns>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T typedChild)
                {
                    return typedChild;
                }
                else
                {
                    T? result = FindVisualChild<T>(child);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }
    }
}