using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;

namespace ChatAppClient
{
    // 1. Class để lưu thông tin Bạn bè / Nhóm
    public class ChatItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }

        private bool _isOnline;
        public bool IsOnline
        {
            get { return _isOnline; }
            set
            {
                _isOnline = value;
                OnPropertyChanged("IsOnline");
                OnPropertyChanged("StatusColor"); // Khi đổi trạng thái thì báo đổi màu luôn
            }
        }

        // Màu sắc: Online = LimeGreen (Xanh lá), Offline = LightGray (Xám nhạt)
        public string StatusColor => IsOnline ? "LimeGreen" : "LightGray";
        private bool _hasNewMessage;
        public bool HasNewMessage
        {
            get { return _hasNewMessage; }
            set
            {
                _hasNewMessage = value;
                OnPropertyChanged("HasNewMessage");
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // 2. Class MỚI để quản lý tin nhắn (cho giao diện Bong bóng chat)
    public class MessageModel
    {
        public string Content { get; set; }
        public bool IsMe { get; set; } // True = Mình gửi, False = Người khác gửi

        // Các thuộc tính để Giao diện tự đổi màu/vị trí
        public HorizontalAlignment Alignment => IsMe ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public string BackgroundColor => IsMe ? "#0084FF" : "#E4E6EB"; // Xanh hoặc Xám
        public string TextColor => IsMe ? "White" : "Black";
    }

    public partial class MainWindow : Window
    {
        private string UserName; // Tên đăng nhập (ID)
        private string DisplayName; // Tên hiển thị (Hùng, Nam...)
        private int MyUserId; // ID số của mình (để so sánh lịch sử)

        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;

        // Danh sách dữ liệu hiển thị lên màn hình
        public ObservableCollection<ChatItem> Friends { get; set; }
        public ObservableCollection<ChatItem> Groups { get; set; }
        public ObservableCollection<MessageModel> Messages { get; set; } // <--- Dùng List mới

        private ChatItem currentTarget = null;
        private AddFriendWindow _addFriendWin;

        // Constructor nhận thêm MyUserId để biết "Tôi là ai"
        public MainWindow(string userName, string displayName, TcpClient existingClient)
        {
            InitializeComponent();
            this.DataContext = this;

            this.UserName = userName;
            this.DisplayName = displayName;
            this.client = existingClient;

            // --- SỬA LỖI 1: HIỂN THỊ TÊN NGƯỜI DÙNG ---
            lblWelcome.Text = DisplayName;

            // Khởi tạo các danh sách
            Friends = new ObservableCollection<ChatItem>();
            Groups = new ObservableCollection<ChatItem>();
            Messages = new ObservableCollection<MessageModel>();

            lstFriends.ItemsSource = Friends;
            lstGroups.ItemsSource = Groups;
            listMessages.ItemsSource = Messages; // Binding vào giao diện mới

            var stream = client.GetStream();
            reader = new StreamReader(stream);
            writer = new StreamWriter(stream) { AutoFlush = true };

            // Lấy danh sách bạn bè
            SendToServer("GET_LIST");

            // Chạy luồng nhận tin nhắn
            Thread t = new Thread(ReceiveMessages);
            t.IsBackground = true;
            t.Start();
        }

        public void SendToServer(string msg)
        {
            try { if (client.Connected) writer.WriteLine(msg); } catch { }
        }

        // --- XỬ LÝ NHẬN TIN TỪ SERVER ---
        private void ReceiveMessages()
        {
            try
            {
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;

                    string[] parts = line.Split('|');
                    string command = parts[0];

                    Dispatcher.Invoke(() =>
                    {
                        // 1. NHẬN DANH SÁCH (Sắp xếp ưu tiên Online)
                        if (command == "LIST")
                        {
                            Friends.Clear(); Groups.Clear();
                            var tempFriends = new System.Collections.Generic.List<ChatItem>();

                            for (int i = 1; i < parts.Length; i++)
                            {
                                string[] item = parts[i].Split(':');
                                if (item.Length >= 3)
                                {
                                    if (item[0] == "F")
                                    {
                                        bool onl = (item.Length > 3 && item[3] == "1");
                                        tempFriends.Add(new ChatItem { Type = "F", Id = int.Parse(item[1]), Name = item[2], IsOnline = onl });
                                    }
                                    else if (item[0] == "G")
                                    {
                                        Groups.Add(new ChatItem { Type = "G", Id = int.Parse(item[1]), Name = item[2] });
                                    }
                                }
                            }
                            // Sắp xếp: Online lên đầu, sau đó đến Tên
                            var sortedFriends = System.Linq.Enumerable.OrderByDescending(tempFriends, x => x.IsOnline).ThenBy(x => x.Name);
                            foreach (var f in sortedFriends) Friends.Add(f);
                        }
                        // 2. CẬP NHẬT TRẠNG THÁI ONLINE/OFFLINE
                        else if (command == "FRIEND_STATUS")
                        {
                            int friendId = int.Parse(parts[1]);
                            bool isOnline = (parts[2] == "1");

                            for (int i = 0; i < Friends.Count; i++)
                            {
                                if (Friends[i].Id == friendId)
                                {
                                    Friends[i].IsOnline = isOnline;
                                    // Logic tự động đẩy lên đầu nếu Online
                                    if (isOnline) Friends.Move(i, 0);
                                    else
                                    {
                                        int lastOnlineIndex = 0;
                                        foreach (var f in Friends) if (f.IsOnline) lastOnlineIndex++;
                                        if (lastOnlineIndex > 0) lastOnlineIndex--;
                                        Friends.Move(i, lastOnlineIndex);
                                    }
                                    break;
                                }
                            }
                        }
                        // 3. NHẬN TIN NHẮN RIÊNG
                        else if (command == "MSG_PRIVATE")
                        {
                            int fromId = int.Parse(parts[1]);
                            string senderName = parts[2];
                            string content = parts[3];

                            // TRƯỜNG HỢP A: Đang mở cửa sổ chat với đúng người gửi
                            if (currentTarget != null && currentTarget.Type == "F" && currentTarget.Id == fromId)
                            {
                                Messages.Add(new MessageModel { Content = content, IsMe = false });
                                ScrollToBottom();
                            }
                            // TRƯỜNG HỢP B: Đang chat với người khác hoặc đang ẩn -> HIỆN CHẤM XANH DƯƠNG
                            else
                            {
                                foreach (var f in Friends)
                                {
                                    if (f.Id == fromId)
                                    {
                                        f.HasNewMessage = true; // Bật cờ tin nhắn mới
                                        break;
                                    }
                                }
                                System.Media.SystemSounds.Beep.Play(); // Phát tiếng bíp
                            }
                        }

                        // 4. NHẬN TIN NHẮN NHÓM
                        else if (command == "MSG_GROUP")
                        {
                            int groupId = int.Parse(parts[1]);
                            string senderName = parts[2];
                            string content = parts[3];

                            if (currentTarget != null && currentTarget.Type == "G" && currentTarget.Id == groupId)
                            {
                                Messages.Add(new MessageModel { Content = $"{senderName}:\n{content}", IsMe = false });
                                ScrollToBottom();
                            }
                        }

                        // 5. CÁC LỆNH KHÁC
                        else if (command == "HISTORY")
                        {
                            Messages.Clear();
                            for (int i = 1; i < parts.Length; i++)
                            {
                                string[] msgParts = parts[i].Split(new char[] { ':' }, 2);
                                int msgSenderId = int.Parse(msgParts[0]);
                                string msgContent = msgParts[1];
                                bool isMe = (msgSenderId != currentTarget.Id);
                                Messages.Add(new MessageModel { Content = msgContent, IsMe = isMe });
                            }
                            ScrollToBottom();
                        }
                        else if (command == "NEW_REQ")
                        {
                            btnAddFriend.Background = Brushes.Red;
                            btnAddFriend.Foreground = Brushes.White;
                            btnAddFriend.Content = "Thêm Bạn (!)";
                            System.Media.SystemSounds.Beep.Play();
                            if (_addFriendWin != null && _addFriendWin.IsVisible) SendToServer("GET_REQ_LIST");
                        }
                        else if (command == "SEARCH_RES" || command == "REQ_LIST")
                        {
                            if (_addFriendWin != null)
                            {
                                if (command == "SEARCH_RES") _addFriendWin.UpdateSearchResult(parts[1], parts.Length > 2 ? parts[2] : "", parts.Length > 3 ? parts[3] : "");
                                else _addFriendWin.UpdateRequestList(parts);
                            }
                        }
                    });
                }
            }
            catch { }
        }

        private void ScrollToBottom()
        {
            if (listMessages.Items.Count > 0)
                listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
        }

        private void btnSend_Click(object sender, RoutedEventArgs e)
        {
            if (currentTarget == null) return;
            string content = txtMessage.Text;
            if (string.IsNullOrWhiteSpace(content)) return;

            // Thêm ngay vào giao diện của mình (Màu xanh, bên phải)
            Messages.Add(new MessageModel { Content = content, IsMe = true });
            ScrollToBottom();

            if (currentTarget.Type == "F")
                SendToServer($"SEND_PRIVATE|{currentTarget.Id}|{content}");
            else
                SendToServer($"SEND_GROUP|{currentTarget.Id}|{content}");

            txtMessage.Clear();
        }

        // --- KHI BẤM CHỌN BẠN BÈ -> TẢI LỊCH SỬ ---
        private void lstFriends_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstFriends.SelectedItem is ChatItem item)
            {
                lstGroups.UnselectAll();
                item.HasNewMessage = false;
                currentTarget = item;
                lblCurrentChat.Text = $"Chat với: {item.Name}";
                // Gửi lệnh lấy lịch sử
                SendToServer($"GET_HISTORY|{item.Id}");

            }
        }        private void btnAddFriend_Click(object sender, RoutedEventArgs e)
        {
            btnAddFriend.Background = Brushes.White;
            btnAddFriend.Foreground = Brushes.Black;
            btnAddFriend.Content = "Thêm Bạn (+)";
            if (_addFriendWin == null || !_addFriendWin.IsVisible) { _addFriendWin = new AddFriendWindow(this); _addFriendWin.Show(); }
            else _addFriendWin.Activate();
        }
        private void btnCreateGroup_Click(object sender, RoutedEventArgs e) { /* ... */ }
        private void txtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                btnSend_Click(sender, null);
            }
        }
        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Ngắt kết nối mạng trước
                if (client != null) client.Close();
            }
            catch { }

            try
            {
                // 2. Mở lại màn hình đăng nhập
                LoginWindow login = new LoginWindow();
                login.Show();

                // 3. Đóng màn hình chat hiện tại
                this.Close();
            }
            catch (Exception ex)
            {
                // Nếu lỗi (ví dụ chưa tạo file LoginWindow), nó sẽ báo lên
                MessageBox.Show("Lỗi khi đăng xuất: " + ex.Message);
            }
        }
        private void lstGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstGroups.SelectedItem is ChatItem item)
            {
                lstFriends.UnselectAll();
                currentTarget = item;
                lblCurrentChat.Text = $"Nhóm: {item.Name}";
                Messages.Clear(); // Nhóm chưa làm lịch sử nên clear tạm
            }
        }
    }
}