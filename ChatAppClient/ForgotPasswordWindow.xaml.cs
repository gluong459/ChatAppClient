using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;

namespace ChatAppClient
{
    public partial class ForgotPasswordWindow : Window
    {
        public ForgotPasswordWindow()
        {
            InitializeComponent();
        }
        private async void btnSendCode_Click(object sender, RoutedEventArgs e)
        {
            string email = txtEmail.Text.Trim();
            if (string.IsNullOrEmpty(email)) return;

            btnSendCode.IsEnabled = false;
            btnSendCode.Content = "Đang gửi email...";

            // Kết nối tạm thời đến Server để xin mã
            string response = await SendCommandToServer($"FORGOT_PASS|{email}");

            if (response.StartsWith("FORGOT_OK"))
            {
                MessageBox.Show("Mã xác nhận đã được gửi đến Email của bạn!", "Thành công");
                pnlReset.Visibility = Visibility.Visible;
                txtEmail.IsEnabled = false; // Khóa ô email lại
                btnSendCode.Content = "Đã gửi mã";
            }
            else
            {
                MessageBox.Show(response.Split('|')[1], "Lỗi");
                btnSendCode.IsEnabled = true;
                btnSendCode.Content = "Gửi mã xác nhận";
            }
        }

        private async void btnReset_Click(object sender, RoutedEventArgs e)
        {
            string email = txtEmail.Text.Trim();
            string code = txtCode.Text.Trim();
            string newPass = txtNewPass.Password;

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(newPass)) return;

            string response = await SendCommandToServer($"RESET_PASS|{email}|{code}|{newPass}");

            if (response.StartsWith("RESET_OK"))
            {
                MessageBox.Show("Đổi mật khẩu thành công! Hãy đăng nhập lại bằng mật khẩu mới.", "Thành công");
                this.Close();
            }
            else
            {
                MessageBox.Show(response.Split('|')[1], "Lỗi");
            }
        }
        private Task<string> SendCommandToServer(string command)
        {
            return Task.Run(() =>
            {
                try
                {
                    TcpClient client = new TcpClient("localhost", 8888);
                    StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                    StreamReader reader = new StreamReader(client.GetStream());

                    writer.WriteLine(command);
                    string response = reader.ReadLine();
                    client.Close();

                    return response ?? "FAIL|Không nhận được phản hồi từ Server";
                }
                catch
                {
                    return "FAIL|Không thể kết nối đến máy chủ!";
                }
            });
        }
    }
}