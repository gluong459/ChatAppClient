using System;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChatAppClient
{
    public partial class LoginWindow : Window
    {
        // CÀI CỨNG IP SERVER
        private const string SERVER_IP = "10.16.6.210";
        private const int SERVER_PORT = 8888;

        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string user = txtUsername.Text;
            string pass = txtPassword.Password;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ thông tin!");
                return;
            }

            // Khóa nút để tránh bấm nhiều lần
            btnLogin.IsEnabled = false;

            try
            {
                TcpClient client = new TcpClient();

                // Kết nối tới Server (Async để không đơ màn hình)
                await client.ConnectAsync(SERVER_IP, SERVER_PORT);

                StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                StreamReader reader = new StreamReader(client.GetStream());

                // Gửi lệnh đăng nhập
                await writer.WriteLineAsync($"LOGIN|{user}|{pass}");

                // Đợi phản hồi
                string response = await reader.ReadLineAsync();

                if (string.IsNullOrEmpty(response))
                {
                    MessageBox.Show("Server không phản hồi.");
                    btnLogin.IsEnabled = true;
                    return;
                }

                string[] parts = response.Split('|');

                // XỬ LÝ KẾT QUẢ: OK | UserID | DisplayName | Avatar
                if (parts[0] == "OK")
                {
                    string userId = parts[1];
                    string displayName = (parts.Length > 2) ? parts[2] : user;

                    // --- LẤY AVATAR ---
                    // Kiểm tra xem Server có gửi kèm Avatar không (phần tử thứ 3)
                    string userAvatar = (parts.Length > 3) ? parts[3] : "";

                    // Khởi tạo MainWindow với 4 tham số (bao gồm cả Avatar)
                    MainWindow main = new MainWindow(userId, displayName, client, userAvatar);
                    main.Show();

                    // Đóng cửa sổ đăng nhập
                    this.Close();
                }
                else
                {
                    // Nếu lỗi (ví dụ: FAIL|Sai pass)
                    MessageBox.Show("Lỗi: " + (parts.Length > 1 ? parts[1] : "Sai thông tin"));
                    client.Close();
                    btnLogin.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không kết nối được tới {SERVER_IP}.\nLỗi: {ex.Message}");
                btnLogin.IsEnabled = true;
            }
        }
        private void lblForgotPass_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ForgotPasswordWindow forgotWin = new ForgotPasswordWindow();
            forgotWin.ShowDialog();
        }

        private void LinkRegister_Click(object sender, MouseButtonEventArgs e)
        {
            RegisterWindow reg = new RegisterWindow();
            reg.Show();
            this.Close();
        }
    }
}