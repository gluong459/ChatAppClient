using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace ChatAppClient
{
    public class RequestItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public partial class AddFriendWindow : Window
    {
        private MainWindow _main;
        private int _currentSearchResultId = -1;

        public ObservableCollection<RequestItem> Requests { get; set; }

        public AddFriendWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            Requests = new ObservableCollection<RequestItem>();
            lstRequests.ItemsSource = Requests;

            // Khi mở lên thì xin danh sách lời mời ngay
            _main.SendToServer("GET_REQ_LIST");
        }

        // --- GỬI LỆNH TÌM KIẾM ---
        private void btnSearch_Click(object sender, RoutedEventArgs e)
        {
            string keyword = txtSearchUser.Text.Trim();
            if (!string.IsNullOrEmpty(keyword))
            {
                _main.SendToServer($"SEARCH_USER|{keyword}");
            }
        }

        // --- CẬP NHẬT KẾT QUẢ TÌM KIẾM (Được gọi từ MainWindow) ---
        public void UpdateSearchResult(string id, string name, string status)
        {
            if (id == "NOT_FOUND")
            {
                pnlResult.Visibility = Visibility.Visible;
                lblResultName.Text = "Không tìm thấy người dùng này.";
                lblResultStatus.Text = "";
                btnAction.Visibility = Visibility.Collapsed;
                return;
            }

            pnlResult.Visibility = Visibility.Visible;
            _currentSearchResultId = int.Parse(id);
            lblResultName.Text = name;
            btnAction.Visibility = Visibility.Visible;

            if (status == "ME")
            {
                lblResultStatus.Text = "Đây là bạn.";
                btnAction.Visibility = Visibility.Collapsed;
            }
            else if (status == "FRIEND")
            {
                lblResultStatus.Text = "Đã là bạn bè.";
                btnAction.Content = "Bạn bè";
                btnAction.IsEnabled = false; // Nút mờ đi
            }
            else if (status == "PENDING")
            {
                lblResultStatus.Text = "Đang chờ duyệt.";
                btnAction.Content = "Đã gửi";
                btnAction.IsEnabled = false;
            }
            else // NONE
            {
                lblResultStatus.Text = "Chưa kết bạn.";
                btnAction.Content = "Thêm";
                btnAction.IsEnabled = true;
            }
        }

        // --- NÚT THÊM BẠN (Kết quả tìm kiếm) ---
        private void btnAction_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSearchResultId != -1)
            {
                _main.SendToServer($"SEND_REQ|{_currentSearchResultId}");
                btnAction.Content = "Đã gửi";
                btnAction.IsEnabled = false;
            }
        }

        // --- CẬP NHẬT DANH SÁCH LỜI MỜI (Được gọi từ MainWindow) ---
        public void UpdateRequestList(string[] parts)
        {
            Requests.Clear();
            // Format: REQ_LIST|Id:Name|Id:Name...
            for (int i = 1; i < parts.Length; i++)
            {
                string[] item = parts[i].Split(':');
                if (item.Length >= 2)
                {
                    Requests.Add(new RequestItem { Id = int.Parse(item[0]), Name = item[1] });
                }
            }
        }

        // --- XỬ LÝ ĐỒNG Ý / TỪ CHỐI ---
        private void btnAccept_Click(object sender, RoutedEventArgs e)
        {
            int reqId = (int)((Button)sender).Tag;
            _main.SendToServer($"RESP_REQ|ACCEPT|{reqId}");
        }

        private void btnDecline_Click(object sender, RoutedEventArgs e)
        {
            int reqId = (int)((Button)sender).Tag;
            _main.SendToServer($"RESP_REQ|DECLINE|{reqId}");
        }
    }
}