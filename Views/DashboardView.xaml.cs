using System;
using System.Windows;
using System.Windows.Controls;

namespace ULTRA.Views
{
    public partial class DashboardView : UserControl
    {
        // 보여줄 행 수(요구: 15건)
        private const int RowsToShow = 15;

        public DashboardView()
        {
            InitializeComponent();

            // 그리드가 화면에 그려진 뒤 높이 계산
            Loaded += (_, __) => FitRecentGrid();
            // 창 크기 변경 시 재계산
            SizeChanged += (_, __) => FitRecentGrid();
            // 호스트 영역 변화에도 반응
            RecentGridHost.SizeChanged += (_, __) => FitRecentGrid();
        }

        private void FitRecentGrid()
        {
            if (RecentGrid == null || RecentGridHost == null)
                return;

            // 기본값
            double rowHeight = RecentGrid.RowHeight > 0 ? RecentGrid.RowHeight : 36.0;
            double headerHeight = !double.IsNaN(RecentGrid.ColumnHeaderHeight) && RecentGrid.ColumnHeaderHeight > 0
                                  ? RecentGrid.ColumnHeaderHeight
                                  : 36.0;

            int rows = Math.Min(RowsToShow, Math.Max(RecentGrid.Items.Count, RowsToShow));
            double desired = headerHeight + rows * rowHeight + 2; // 약간의 보더 보정

            double hostAvail = RecentGridHost.ActualHeight;
            if (hostAvail > 0)
                desired = Math.Min(desired, hostAvail);

            RecentGrid.Height = desired;
            RecentGrid.VerticalAlignment = VerticalAlignment.Top;
        }
    }
}
