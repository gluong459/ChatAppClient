using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static ChatAppClient.MainWindow;

namespace ChatAppClient
{
    public partial class UserProfileWindow : Window
    {
        private MainWindow _parent;
        private string _originalName = "";
        private string _currentAvatarBase64 = "";
        private bool _isAvatarChanged = false;

        public UserProfileWindow(MainWindow parent, string displayName, string username, ImageSource currentAvatar, string email = "")
        {
            InitializeComponent();

            // --- GỌI TRỢ THỦ ĐỔI MÀU THANH TIÊU ĐỀ ---
            ThemeHelper.ApplyTitleBarTheme(this, MessageModel.IsDarkMode);

            _parent = parent;
            _originalName = displayName;

            txtDisplayName.Text = displayName;

            if (currentAvatar != null)
            {
                var brush = new ImageBrush(currentAvatar);
                brush.Stretch = Stretch.UniformToFill;
                imgAvatar.Fill = brush;
            }
            else
            {
                // Màu nền mặc định nếu không có ảnh (xám nhạt)
                imgAvatar.Fill = new SolidColorBrush(Color.FromRgb(220, 220, 220));
            }

            // GÁN EMAIL LÊN GIAO DIỆN
            if (FindName("txtEmail") is TextBlock txt) txt.Text = email;
        }

        // 1. KHI BẤM VÀO ẢNH ĐẠI DIỆN
        private void btnChangeAvatar_Click(object sender, MouseButtonEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";
            if (dlg.ShowDialog() == true)
            {
                string filePath = dlg.FileName;
                _currentAvatarBase64 = ImageUtils.ImageToBase64(filePath);
                _isAvatarChanged = true;

                var newImage = ImageUtils.Base64ToImage(_currentAvatarBase64);
                if (newImage != null)
                {
                    var brush = new ImageBrush(newImage);
                    brush.Stretch = Stretch.UniformToFill;
                    imgAvatar.Fill = brush;
                }
                ShowActionButtons();
            }
        }

        // 2. KHI BẤM NÚT "SỬA TÊN"
        private void btnEdit_Click(object sender, RoutedEventArgs e)
        {
            txtDisplayName.IsReadOnly = false;

            // SỬA: Lấy màu theo Theme hiện tại thay vì gán chết màu Trắng/Đen
            txtDisplayName.SetResourceReference(TextBox.BackgroundProperty, "BgMain");
            txtDisplayName.SetResourceReference(TextBox.ForegroundProperty, "TextMain");
            txtDisplayName.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)); // Viền xanh khi focus

            txtDisplayName.Focus();
            txtDisplayName.SelectAll();

            btnEdit.Visibility = Visibility.Collapsed;
            ShowActionButtons();
        }

        private void ShowActionButtons()
        {
            pnlActions.Visibility = Visibility.Visible;
        }

        // 3. KHI BẤM "LƯU THAY ĐỔI"
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            string newName = txtDisplayName.Text.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("Tên hiển thị không được để trống.");
                return;
            }

            string avatarFlag = _isAvatarChanged ? "1" : "0";
            string avatarData = _isAvatarChanged ? _currentAvatarBase64 : "";

            _parent.SendToServer($"UPDATE_PROFILE|{newName}|{avatarFlag}|{avatarData}");
            this.Close();
        }

        // 4. KHI BẤM "HỦY" 
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            txtDisplayName.Text = _originalName;

            txtDisplayName.IsReadOnly = true;

            // SỬA: Quay về màu động của Theme (BgInput) thay vì màu xám chết cứng
            txtDisplayName.SetResourceReference(TextBox.BackgroundProperty, "BgInput");
            txtDisplayName.SetResourceReference(TextBox.ForegroundProperty, "TextMain");
            txtDisplayName.SetResourceReference(TextBox.BorderBrushProperty, "BorderColor");

            btnEdit.Visibility = Visibility.Visible;
            pnlActions.Visibility = Visibility.Collapsed;

            // Đóng cửa sổ luôn cho gọn
            this.Close();
        }
    }
}