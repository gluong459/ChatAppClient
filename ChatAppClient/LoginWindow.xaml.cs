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
        // CÀI CỨNG IP TẠI ĐÂY
        private const string SERVER_IP = "192.168.10.125";
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

            btnLogin.IsEnabled = false;

            try
            {
                TcpClient client = new TcpClient();

                // Kết nối thẳng tới IP đã cài cứng
                await client.ConnectAsync(SERVER_IP, SERVER_PORT);

                StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                StreamReader reader = new StreamReader(client.GetStream());

                await writer.WriteLineAsync($"LOGIN|{user}|{pass}");

                string response = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(response))
                {
                    MessageBox.Show("Server không phản hồi.");
                    btnLogin.IsEnabled = true;
                    return;
                }

                string[] parts = response.Split('|');

                if (parts[0] == "OK")
                {
                    string displayName = parts.Length > 2 ? parts[2] : user;
                    MainWindow main = new MainWindow(user, displayName, client);
                    main.Show();
                    this.Close();
                }
                else
                {
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

        private void LinkRegister_Click(object sender, MouseButtonEventArgs e)
        {
            RegisterWindow reg = new RegisterWindow();
            reg.Show();
            this.Close();
        }
    }
}