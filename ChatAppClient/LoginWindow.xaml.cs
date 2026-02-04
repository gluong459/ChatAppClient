using System;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChatAppClient
{
    public partial class LoginWindow : Window
    {
        // Biến lưu IP mặc định
        private string defaultIP = "10.238.133.210";

        public LoginWindow()
        {
            InitializeComponent();
        }

        // --- 1. XỬ LÝ ĐĂNG NHẬP ---
        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string user = txtUsername.Text;
            string pass = txtPassword.Password;

            // Lấy IP từ ô nhập liệu
            string ipAddress = txtIpServer.Text;

            // Nếu người dùng chưa nhập gì hoặc vẫn để chữ mờ -> Dùng IP mặc định
            if (string.IsNullOrWhiteSpace(ipAddress) || txtIpServer.Foreground == Brushes.Gray)
            {
                ipAddress = defaultIP;
            }

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ thông tin!");
                return;
            }

            try
            {
                // Kết nối
                TcpClient client = new TcpClient(ipAddress, 8888);

                StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                StreamReader reader = new StreamReader(client.GetStream());

                // Gửi lệnh LOGIN
                writer.WriteLine($"LOGIN|{user}|{pass}");

                // Đọc phản hồi
                string response = reader.ReadLine();
                if (string.IsNullOrEmpty(response)) return;

                string[] parts = response.Split('|');

                if (parts[0] == "OK")
                {
                    string displayName = parts.Length > 1 ? parts[1] : user;

                    // Mở màn hình Chat
                    MainWindow main = new MainWindow(user, displayName, client);
                    main.Show();
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Đăng nhập thất bại: " + (parts.Length > 1 ? parts[1] : "Lỗi xác thực"));
                    client.Close();
                }
            }
            catch (SocketException)
            {
                MessageBox.Show($"Không tìm thấy Server tại {ipAddress}! Hãy kiểm tra lại IP hoặc Tường lửa.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi kết nối: " + ex.Message);
            }
        }

        // --- 2. XỬ LÝ CHUYỂN QUA MÀN HÌNH ĐĂNG KÝ (Hàm bị thiếu lúc nãy) ---
        private void LinkRegister_Click(object sender, RoutedEventArgs e)
        {
            // Mở màn hình đăng ký
            RegisterWindow reg = new RegisterWindow();
            reg.Show();

            // Đóng màn hình đăng nhập hiện tại
            this.Close();
        }

        // --- 3. CÁC HÀM XỬ LÝ PLACEHOLDER (CHỮ MỜ) ---

        // Khi bấm vào -> Xóa chữ mờ
        private void RemovePlaceholder(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb.Text == defaultIP && tb.Foreground == Brushes.Gray)
            {
                tb.Text = "";
                tb.Foreground = Brushes.Black;
            }
        }
        // Khi bấm ra -> Hiện chữ mờ nếu trống
        private void AddPlaceholder(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                tb.Text = defaultIP;
                tb.Foreground = Brushes.Gray;
            }
        }
    }
}