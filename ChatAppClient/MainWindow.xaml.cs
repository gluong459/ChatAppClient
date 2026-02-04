using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Windows;

namespace ChatAppClient
{
    public partial class MainWindow : Window
    {
        private string UserName;
        private string DisplayName;
        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;
        // Constructor nhận vào tên người dùng từ màn hình Login
        public MainWindow(string userName, string displayName, TcpClient existingClient)
        {
            InitializeComponent();
            this.UserName = userName;
            this.DisplayName = displayName;
            this.client = existingClient; // Dùng lại kết nối từ LoginWindow
            this.Title = "Chat App - " + userName;
            lblWelcome.Text = "Hi " + displayName;

            // Thiết lập luồng đọc/ghi
            var stream = client.GetStream();
            reader = new StreamReader(stream);
            writer = new StreamWriter(stream) { AutoFlush = true };

            // Bắt đầu lắng nghe tin nhắn luôn
            Thread t = new Thread(ReceiveMessages);
            t.IsBackground = true;
            t.Start();
        }
        // Hàm nhận tin nhắn (Chạy liên tục ở luồng riêng)
        private void ReceiveMessages()
        {
            try
            {
                while (true)
                {
                    string message = reader.ReadLine();
                    if (message == null) break;
                    // Xử lý đổi tên "Tôi"
                    string myPrefix = this.DisplayName + ": ";
                    if (message.StartsWith(myPrefix))
                    {
                        string content = message.Substring(myPrefix.Length);
                        message = "Tôi: " + content;
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (!this.IsActive)
                            {
                                try
                                {
                                    // Đường dẫn tương đối (vì đã Copy to Output)
                                    System.Media.SoundPlayer player = new System.Media.SoundPlayer("tinnhan.wav");
                                    player.Play();
                                }
                                catch
                                {
                                    // Nếu lỗi file thì phát tiếng mặc định cho đỡ buồn
                                    System.Media.SystemSounds.Exclamation.Play();
                                }
                                FlashWindow();
                            }
                        });
                    }

                    Dispatcher.Invoke(() =>
                    {
                        listMessages.Items.Add(message);
                        listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                    });
                }
            }
            catch
            {
                // Ngắt kết nối
            }
        }
        // Sự kiện nút Gửi
        private void btnSend_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }
        // Hàm gửi tin nhắn
        private void SendMessage()
        {
            if (!string.IsNullOrEmpty(txtMessage.Text) && client != null)
            {
                // Format: "Tên: Nội dung"
                string msgToSend = txtMessage.Text;

                // Gửi lên Server
                writer.WriteLine(msgToSend);

                // Xóa ô nhập liệu
                txtMessage.Clear();
            }
        }
        private void txtMessage_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Kiểm tra nếu phím vừa nhấn là Enter
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SendMessage(); // Gọi lại hàm gửi tin nhắn đã viết trước đó
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);
        private void FlashWindow()
        {
            // Lấy tay cầm (Handle) của cửa sổ hiện tại
            System.Windows.Interop.WindowInteropHelper wih = new System.Windows.Interop.WindowInteropHelper(this);
            // Ra lệnh cho Windows nháy taskbar
            FlashWindow(wih.Handle, true);
        }
        private void btnLogout_Click(object sender, RoutedEventArgs e)
        {
            // 1. Ngắt kết nối mạng an toàn
            try
            {
                if (client != null) client.Close(); // Đóng socket
                if (reader != null) reader.Close();
                if (writer != null) writer.Close();
            }
            catch
            {
                // Kệ lỗi nếu có, mục đích chính là thoát
            }

            // 2. Mở lại màn hình Đăng nhập
            LoginWindow login = new LoginWindow();
            login.Show();
            // 3. Đóng cửa sổ Chat hiện tại
            this.Close();
        }
    }
}