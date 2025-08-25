using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ULTRA.ViewModels;

namespace ULTRA.Views
{
    public partial class WarehouseView : UserControl
    {
        public WarehouseView()
        {
            InitializeComponent();
        }

        private WarehouseViewModel VM => DataContext as WarehouseViewModel;

        // 좌측 제품 → 드래그 시작
        private Point _startPt;
        private void ProductGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _startPt = e.GetPosition(null); return; }
            var diff = e.GetPosition(null) - _startPt;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var row = (sender as DataGrid)?.SelectedItem as WarehouseViewModel.Product;
            if (row == null) return;

            var data = new DataObject();
            data.SetData("ULTRA/ProductNo", row.No);
            data.SetData("ULTRA/ProductName", row.Name ?? "");
            data.SetData("ULTRA/ProductUnit", row.Unit ?? "");

            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }

        // 슬롯에서 드래그 시작(이동)
        private void SlotCell_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _startPt = e.GetPosition(null); return; }
            var diff = e.GetPosition(null) - _startPt;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            if ((sender as Border)?.DataContext is WarehouseViewModel.SlotBox box && !box.IsEmpty)
            {
                var data = new DataObject();
                data.SetData("ULTRA/SlotFrom", $"{box.Col};{box.Index}");
                DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
            }
        }

        // 슬롯 드롭 처리: 제품 / 슬롯 이동
        private void SlotCell_Drop(object sender, DragEventArgs e)
        {
            if ((sender as Border)?.DataContext is not WarehouseViewModel.SlotBox target || VM == null) return;

            if (e.Data.GetDataPresent("ULTRA/ProductNo"))
            {
                var no = (string)e.Data.GetData("ULTRA/ProductNo");
                var name = (string)e.Data.GetData("ULTRA/ProductName");
                var unit = (string)e.Data.GetData("ULTRA/ProductUnit");
                VM.DropProductToCellByProduct(no, name, unit, target.Col, target.Index);
                return;
            }

            if (e.Data.GetDataPresent("ULTRA/SlotFrom"))
            {
                var s = (string)e.Data.GetData("ULTRA/SlotFrom");
                var parts = s.Split(';');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var fromCol) &&
                    int.TryParse(parts[1], out var fromSlot))
                {
                    VM.MoveSlot(fromCol, fromSlot, target.Col, target.Index);
                }
            }
        }

        private void ClearSlot_Click(object sender, RoutedEventArgs e)
        {
            if (VM is null) return;
            if ((sender as FrameworkElement)?.DataContext is WarehouseViewModel.SlotBox box)
            {
                VM.ClearSlot(box.Col, box.Index);
            }
        }
    }
}
