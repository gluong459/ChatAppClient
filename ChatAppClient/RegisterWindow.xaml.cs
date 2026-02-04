using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;

namespace ChatAppClient
{
    public partial class RegisterWindow : Window
    {
        public RegisterWindow()
        {
            InitializeComponent();
        }

        private void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            string user = txtRegUsername.Text;
            string display = txtDisplayname.Text;
            string pass = txtRegPassword.Password;
            string rePass = txtRePassword.Password;

            if (pass != rePass)
            {
                MessageBox.Show("Mật khẩu nhập lại không khớp!");
                return;
            }

            try
            {
                // 1. Kết nối tạm đến Server để gửi đơn đăng ký
                TcpClient client = new TcpClient("192.168.56.1", 8888); // Đổi IP nếu chạy khác máy
                StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                StreamReader reader = new StreamReader(client.GetStream());

                // 2. Gửi lệnh: REGISTER|user|pass
                writer.WriteLine($"REGISTER|{user}|{pass}|{display}");

                // 3. Đọc phản hồi từ Server
                string response = reader.ReadLine(); // Nhận về "OK|..." hoặc "FAIL|..."
                string[] parts = response.Split('|');

                if (parts[0] == "OK")
                {
                    MessageBox.Show("Đăng ký thành công! Vui lòng đăng nhập.");
                    // Quay về màn hình đăng nhập
                    LoginWindow login = new LoginWindow();
                    login.Show();
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Lỗi: " + parts[1]);
                }
                client.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không kết nối được Server: " + ex.Message);
            }
        }

        private void LinkLogin_Click(object sender, MouseButtonEventArgs e)
        {
            LoginWindow login = new LoginWindow();
            login.Show();
            this.Close();
        }
    }
}