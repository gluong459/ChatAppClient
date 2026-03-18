using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ChatAppClient
{
    public class RequestItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class SearchResultItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }

        private string _status;
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged("StatusText");
                OnPropertyChanged("ButtonText");
                OnPropertyChanged("ButtonVisibility");
                OnPropertyChanged("IsButtonEnabled");
            }
        }

        public string StatusText
        {
            get
            {
                if (Status == "ME") return "Đây là bạn";
                if (Status == "FRIEND") return "Đã là bạn bè";
                if (Status == "PENDING") return "Đang chờ duyệt...";
                if (Status == "NOT_FOUND") return "Không tìm thấy người dùng nào.";
                return "Chưa kết bạn";
            }
        }

        public string ButtonText
        {
            get
            {
                if (Status == "FRIEND") return "Bạn bè";
                if (Status == "PENDING") return "Đã gửi";
                return "Thêm";
            }
        }

        public Visibility ButtonVisibility => (Status == "ME" || Status == "NOT_FOUND") ? Visibility.Collapsed : Visibility.Visible;
        public bool IsButtonEnabled => Status == "NONE";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class AddFriendWindow : Window
    {
        private MainWindow _main;

        public ObservableCollection<RequestItem> Requests { get; set; }
        public ObservableCollection<SearchResultItem> SearchResults { get; set; }

        public AddFriendWindow(MainWindow main)
        {
            InitializeComponent();

            // --- GỌI TRỢ THỦ ĐỔI MÀU THANH TIÊU ĐỀ ---
            ThemeHelper.ApplyTitleBarTheme(this, MessageModel.IsDarkMode);

            _main = main;

            Requests = new ObservableCollection<RequestItem>();
            lstRequests.ItemsSource = Requests;

            SearchResults = new ObservableCollection<SearchResultItem>();
            lstSearchResults.ItemsSource = SearchResults;

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
        public void UpdateSearchResult(string[] parts)
        {
            Dispatcher.Invoke(() => {
                SearchResults.Clear();
                pnlResultGroup.Visibility = Visibility.Visible;

                if (parts[1] == "NOT_FOUND")
                {
                    SearchResults.Add(new SearchResultItem { Name = "Không tìm thấy người dùng nào phù hợp.", Email = "", Status = "NOT_FOUND" });
                    return;
                }

                // Format: SEARCH_RES|Id:Name:Email:Status|Id:Name...
                for (int i = 1; i < parts.Length; i++)
                {
                    string[] item = parts[i].Split(':');
                    if (item.Length >= 4)
                    {
                        SearchResults.Add(new SearchResultItem
                        {
                            Id = int.Parse(item[0]),
                            Name = item[1],
                            Email = item[2],
                            Status = item[3]
                        });
                    }
                }
            });
        }

        // --- NÚT THÊM BẠN TRONG DANH SÁCH KẾT QUẢ ---
        private void btnAction_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag != null)
            {
                int targetId = (int)btn.Tag;
                _main.SendToServer($"SEND_REQ|{targetId}");

                // Cập nhật UI ngay lập tức: Đổi nút thành "Đã gửi"
                foreach (var item in SearchResults)
                {
                    if (item.Id == targetId)
                    {
                        item.Status = "PENDING";
                        break;
                    }
                }
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