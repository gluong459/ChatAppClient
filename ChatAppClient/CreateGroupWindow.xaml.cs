using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ChatAppClient
{
    public partial class CreateGroupWindow : Window
    {
        private MainWindow _main;

        public CreateGroupWindow(MainWindow main)
        {
            InitializeComponent();

            // --- GỌI TRỢ THỦ ĐỔI MÀU THANH TIÊU ĐỀ ---
            ThemeHelper.ApplyTitleBarTheme(this, MessageModel.IsDarkMode);

            _main = main;

            // Lấy danh sách bạn bè từ MainWindow đổ vào ListBox
            lstFriendsToSelect.ItemsSource = _main.Friends;

            txtGroupName.TextChanged += (s, e) => {
                lblPlaceholder.Visibility = string.IsNullOrEmpty(txtGroupName.Text) ? Visibility.Visible : Visibility.Collapsed;
            };
        }

        private void btnCreate_Click(object sender, RoutedEventArgs e)
        {
            string groupName = txtGroupName.Text.Trim();
            List<string> selectedIds = new List<string>();

            // Quét các checkbox được tick
            foreach (var item in lstFriendsToSelect.Items)
            {
                ListBoxItem lbi = (ListBoxItem)lstFriendsToSelect.ItemContainerGenerator.ContainerFromItem(item);
                if (lbi != null)
                {
                    CheckBox cb = FindVisualChild<CheckBox>(lbi);
                    if (cb != null && cb.IsChecked == true)
                    {
                        selectedIds.Add(cb.Tag.ToString());
                    }
                }
            }

            string memberString = string.Join(",", selectedIds);
            _main.SendToServer($"CREATE_GROUP|{groupName}|{memberString}");
            this.Close();
        }

        private void btnJoin_Click(object sender, RoutedEventArgs e)
        {
            string code = txtInviteCode.Text.Trim();
            if (code.Length == 8)
            {
                _main.SendToServer($"JOIN_GROUP|{code}");
                this.Close();
            }
            else
            {
                MessageBox.Show("Mã nhóm phải gồm 8 ký tự!");
            }
        }

        // Hàm hỗ trợ tìm Checkbox trong ListBoxItem
        private T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(obj, i);
                if (child != null && child is T) return (T)child;
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null) return childOfChild;
                }
            }
            return null;
        }
    }
}