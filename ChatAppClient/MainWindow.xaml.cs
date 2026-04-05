using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ChatAppClient
{
    public class NotificationItem : INotifyPropertyChanged
    {
        public string Message { get; set; }
        public string Time { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class MemberItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ImageSource Avatar { get; set; }
        public Visibility KickVisibility { get; set; }
        public Visibility AddFriendVisibility { get; set; }
    }

    public class ChatItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        private string _name;
        public string Name { get { return _name; } set { _name = value; OnPropertyChanged("Name"); } }
        public string Type { get; set; }
        private ImageSource _avatarSource;
        public ImageSource AvatarSource { get { return _avatarSource; } set { _avatarSource = value; OnPropertyChanged("AvatarSource"); } }
        private bool _isOnline;
        public bool IsOnline { get { return _isOnline; } set { _isOnline = value; OnPropertyChanged("IsOnline"); OnPropertyChanged("StatusColor"); } }
        public string StatusColor => IsOnline ? "LimeGreen" : "LightGray";
        private bool _hasNewMessage;
        public bool HasNewMessage { get { return _hasNewMessage; } set { _hasNewMessage = value; OnPropertyChanged("HasNewMessage"); } }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MessageModel : INotifyPropertyChanged
    {
        public static bool IsDarkMode = false;
        public static bool IsAutoTheme = false;

        private int _remainingSeconds;
        public int RemainingSeconds { get => _remainingSeconds; set { _remainingSeconds = value; OnPropertyChanged("RemainingSeconds"); } }

        private bool _isSelfDestructing;
        public bool IsSelfDestructing { get => _isSelfDestructing; set { _isSelfDestructing = value; OnPropertyChanged("IsSelfDestructing"); } }

        private Visibility _timerVisibility = Visibility.Collapsed;
        public Visibility TimerVisibility { get => _timerVisibility; set { _timerVisibility = value; OnPropertyChanged("TimerVisibility"); } }

        private string _timerText;
        public string TimerText { get => _timerText; set { _timerText = value; OnPropertyChanged("TimerText"); } }

        public string RawPayload { get; set; }

        private bool _isBurnStarted = false;
        public bool IsBurnStarted
        {
            get => _isBurnStarted;
            set
            {
                _isBurnStarted = value;
                UpdateBurnState(); // Ép cập nhật lại UI khi bắt đầu cháy
                OnPropertyChanged("IsBurnStarted");
            }
        }

        private bool _isContentRevealed = true;
        public bool IsContentRevealed
        {
            get => _isContentRevealed;
            set
            {
                _isContentRevealed = value;
                OnPropertyChanged("IsContentRevealed");
                OnPropertyChanged("ContentVisibility");
                OnPropertyChanged("HiddenOverlayVisibility");
            }
        }
        public Visibility ContentVisibility => IsContentRevealed ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HiddenOverlayVisibility => IsContentRevealed ? Visibility.Collapsed : Visibility.Visible;

        // --- HÀM MỚI: QUẢN LÝ TRẠNG THÁI Ổ KHÓA UI ---
        public void UpdateBurnState()
        {
            if (IsSelfDestructing)
            {
                if (!IsMe) // Nếu là người nhận
                {
                    if (IsBurnStarted)
                    {
                        IsContentRevealed = true;
                        TimerVisibility = Visibility.Visible;
                    }
                    else
                    {
                        IsContentRevealed = false; // Bắt buộc giấu chữ đi
                        TimerVisibility = Visibility.Collapsed;
                    }
                }
                else // Nếu là người gửi
                {
                    IsContentRevealed = true;
                    TimerVisibility = Visibility.Visible;
                    if (!IsBurnStarted) TimerText = "Chờ người nhận đọc...";
                }
            }
        }

        private string _content;
        public string Content
        {
            get => _content;
            set
            {
                if (_content == value) return;
                string safeValue = value;

                if (safeValue != null)
                {
                    if (safeValue.Contains("[E2EE]"))
                    {
                        if (string.IsNullOrEmpty(RawPayload)) RawPayload = safeValue;

                        try
                        {
                            string prefix = "";
                            int e2eeIndex = safeValue.IndexOf("[E2EE]");
                            if (e2eeIndex > 0)
                            {
                                prefix = safeValue.Substring(0, e2eeIndex);
                                safeValue = safeValue.Substring(e2eeIndex);
                            }

                            string[] dataParts = safeValue.Substring(6).Split('*');
                            if (dataParts.Length == 4)
                            {
                                string encKeyThem = dataParts[0];
                                string encKeyMe = dataParts[1];
                                string iv = dataParts[2];
                                string encMsg = dataParts[3];

                                string decryptedAesKey = EncryptionHelper.DecryptRSA(encKeyMe, EncryptionHelper.MyPrivateKey);

                                if (string.IsNullOrEmpty(decryptedAesKey))
                                {
                                    decryptedAesKey = EncryptionHelper.DecryptRSA(encKeyThem, EncryptionHelper.MyPrivateKey);
                                }

                                if (!string.IsNullOrEmpty(decryptedAesKey))
                                {
                                    string decryptedText = AesEncryptionHelper.Decrypt(encMsg, decryptedAesKey, iv);
                                    safeValue = prefix + decryptedText;
                                }
                                else
                                {
                                    safeValue = prefix + "[Tin nhắn bảo mật không thể hiển thị do sai khóa]";
                                }
                            }
                        }
                        catch { safeValue = "[Lỗi giải mã tin nhắn bảo mật]"; }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(RawPayload)) RawPayload = safeValue;
                    }

                    Match burnMatch = Regex.Match(safeValue, @"\[BURN:(\d+)\]");
                    if (burnMatch.Success)
                    {
                        int seconds = int.Parse(burnMatch.Groups[1].Value);
                        IsSelfDestructing = true;

                        if (BurnCacheManager.IsBurning(RawPayload))
                        {
                            RemainingSeconds = BurnCacheManager.GetRemainingSeconds(RawPayload);
                            IsBurnStarted = true;
                        }
                        else
                        {
                            RemainingSeconds = seconds;
                        }

                        TimerVisibility = Visibility.Visible;

                        if (RemainingSeconds >= 3600) TimerText = TimeSpan.FromSeconds(RemainingSeconds).ToString(@"h\:mm\:ss");
                        else TimerText = TimeSpan.FromSeconds(RemainingSeconds).ToString(@"mm\:ss");

                        safeValue = safeValue.Replace(burnMatch.Value, "");

                        // Cập nhật ổ khóa ngay lập tức sau khi phân tích xong nội dung
                        UpdateBurnState();
                    }

                    string actualPayload = safeValue;

                    int newlineIndex = safeValue.IndexOf(":\n");
                    if (newlineIndex > 0 && newlineIndex < 50)
                    {
                        SenderName = safeValue.Substring(0, newlineIndex);
                        actualPayload = safeValue.Substring(newlineIndex + 2);
                    }

                    if (actualPayload.StartsWith("[IMG]"))
                    {
                        string base64 = actualPayload.Substring(5);
                        AttachedFileBase64 = base64;
                        AttachedImage = ImageUtils.GetImageFromCacheOrBase64(base64);
                        TextVisibility = Visibility.Collapsed; ImageVisibility = Visibility.Visible;
                        FileVisibility = Visibility.Collapsed; VideoVisibility = Visibility.Collapsed;
                        BackgroundColor = "Transparent"; safeValue = "[Hình ảnh]";
                    }
                    else if (actualPayload.StartsWith("[FILE]"))
                    {
                        string data = actualPayload.Substring(6);
                        int splitIndex = data.IndexOf('*');
                        if (splitIndex == -1) splitIndex = data.IndexOf('|');

                        if (splitIndex > 0)
                        {
                            AttachedFileBase64 = data.Substring(0, splitIndex);
                            FileName = data.Substring(splitIndex + 1);
                        }
                        else
                        {
                            AttachedFileBase64 = data;
                            FileName = "Tai_Lieu_Cu";
                        }

                        TextVisibility = Visibility.Collapsed; ImageVisibility = Visibility.Collapsed;
                        FileVisibility = Visibility.Visible; VideoVisibility = Visibility.Collapsed;
                        BackgroundColor = "Transparent"; safeValue = "[Tệp tin đính kèm]";
                    }
                    else if (actualPayload.StartsWith("[VID]"))
                    {
                        string data = actualPayload.Substring(5);
                        int splitIndex = data.IndexOf('*');
                        if (splitIndex == -1) splitIndex = data.IndexOf('|');

                        if (splitIndex > 0)
                        {
                            AttachedFileBase64 = data.Substring(0, splitIndex);
                            FileName = data.Substring(splitIndex + 1);
                        }
                        else
                        {
                            AttachedFileBase64 = data;
                            FileName = "Video_Cu.mp4";
                        }

                        if (!string.IsNullOrEmpty(AttachedFileBase64))
                        {
                            string cacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatCache");
                            string filePath = Path.Combine(cacheFolder, AttachedFileBase64);
                            if (File.Exists(filePath)) VideoLocalPath = filePath;
                        }

                        TextVisibility = Visibility.Collapsed; ImageVisibility = Visibility.Collapsed;
                        FileVisibility = Visibility.Collapsed; VideoVisibility = Visibility.Visible;
                        BackgroundColor = "Transparent"; safeValue = "[Video]";
                    }
                    else
                    {
                        safeValue = Regex.Replace(actualPayload, @"(\S{20})", "$1\u200B");
                        TextVisibility = Visibility.Visible; ImageVisibility = Visibility.Collapsed;
                        FileVisibility = Visibility.Collapsed; VideoVisibility = Visibility.Collapsed;
                        BackgroundColor = null;
                    }
                }

                _content = safeValue;
                OnPropertyChanged("Content");
            }
        }

        private string _senderName;
        public string SenderName { get => _senderName; set { _senderName = value; OnPropertyChanged("SenderName"); } }
        private Visibility _senderNameVisibility = Visibility.Collapsed;
        public Visibility SenderNameVisibility { get => _senderNameVisibility; set { _senderNameVisibility = value; OnPropertyChanged("SenderNameVisibility"); } }

        private string _fileName;
        public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged("FileName"); } }
        public string AttachedFileBase64 { get; set; }

        public Visibility FileVisibility { get; set; } = Visibility.Collapsed;
        public Visibility VideoVisibility { get; set; } = Visibility.Collapsed;

        private string _videoLocalPath;
        public string VideoLocalPath { get => _videoLocalPath; set { _videoLocalPath = value; OnPropertyChanged("VideoLocalPath"); OnPropertyChanged("VideoLocalPathUri"); } }
        public Uri VideoLocalPathUri => string.IsNullOrEmpty(VideoLocalPath) ? null : new Uri(VideoLocalPath);

        private string _videoStatusText;
        public string VideoStatusText { get => _videoStatusText; set { _videoStatusText = value; OnPropertyChanged("VideoStatusText"); } }
        private Visibility _videoStatusVis = Visibility.Collapsed;
        public Visibility VideoStatusVisibility { get => _videoStatusVis; set { _videoStatusVis = value; OnPropertyChanged("VideoStatusVisibility"); } }

        private bool _isMe;
        public bool IsMe
        {
            get => _isMe;
            set
            {
                // Đã xóa lỗi "if (_isMe == value) return;"
                _isMe = value;

                UpdateBurnState();

                OnPropertyChanged("IsMe");
                OnPropertyChanged("Alignment");
                OnPropertyChanged("TextColor");
                OnPropertyChanged("BackgroundColor");
            }
        }
        public HorizontalAlignment Alignment => IsMe ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        public string TextColor => IsMe ? "White" : (IsDarkMode ? "#E4E6EB" : "Black");
        private string _backgroundColor;
        public string BackgroundColor { get => _backgroundColor ?? (IsMe ? "#0084FF" : (IsDarkMode ? "#3E4042" : "#E4E6EB")); set { if (_backgroundColor != value) { _backgroundColor = value; OnPropertyChanged("BackgroundColor"); } } }

        public ImageSource AttachedImage { get; set; }
        public Visibility TextVisibility { get; set; } = Visibility.Visible;
        public Visibility ImageVisibility { get; set; } = Visibility.Collapsed;
        private CornerRadius _bubbleRadius;
        public CornerRadius BubbleRadius { get => _bubbleRadius; set { if (_bubbleRadius != value) { _bubbleRadius = value; OnPropertyChanged("BubbleRadius"); } } }
        private Visibility _avatarVisibility;
        public Visibility AvatarVisibility { get => _avatarVisibility; set { if (_avatarVisibility != value) { _avatarVisibility = value; OnPropertyChanged("AvatarVisibility"); } } }
        private Thickness _messageMargin;
        public Thickness MessageMargin { get => _messageMargin; set { if (_messageMargin != value) { _messageMargin = value; OnPropertyChanged("MessageMargin"); } } }
        private ImageSource _avatarSource;
        public ImageSource AvatarSource { get => _avatarSource; set { if (_avatarSource != value) { _avatarSource = value; OnPropertyChanged("AvatarSource"); } } }

        public void RefreshTheme()
        {
            OnPropertyChanged("TextColor");
            OnPropertyChanged("BackgroundColor");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Window
    {
        private string UserName;
        private string DisplayName;
        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;
        private System.Media.SoundPlayer _soundPlayer;
        public ObservableCollection<ChatItem> Friends { get; set; }
        public ObservableCollection<ChatItem> Groups { get; set; }
        public ObservableCollection<MessageModel> Messages { get; set; }
        public ObservableCollection<NotificationItem> Notifications { get; set; }

        private ChatItem currentTarget = null;
        private AddFriendWindow _addFriendWin;
        private List<ChatItem> _allFriends = new List<ChatItem>();
        private List<ChatItem> _allGroups = new List<ChatItem>();

        public Dictionary<int, string> FriendPublicKeys = new Dictionary<int, string>();

        private bool isInfoPanelVisible = false;
        private bool isMuted = false;
        public string UserEmail = "";

        public string CurrentBackupEncrypted = "";
        public int SelfDestructSeconds = 0;

        DispatcherTimer _videoTimer;
        DispatcherTimer _burnTimer;
        MediaElement _activeVideoPlayer;
        Slider _activeVideoSlider;
        bool _isDraggingSlider = false;
        bool _isDraggingFS = false;

        private Button _lastCopiedButton = null;
        private CancellationTokenSource _copyCts = null;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private void SetTitleBarTheme(bool isDark)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int dark = isDark ? 1 : 0;
                int result = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                if (result != 0) DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
            }
            catch { }
        }

        public MainWindow(string userName, string displayName, TcpClient existingClient, string userAvatar = "")
        {
            InitializeComponent();
            this.DataContext = this;
            this.UserName = userName;
            this.DisplayName = displayName;
            this.client = existingClient;
            lblWelcome.Text = DisplayName;

            BurnCacheManager.Load(); // Khởi động Local Cache

            var myImg = ImageUtils.Base64ToImage(userAvatar);
            if (myImg != null) imgMyAvatar.Fill = new ImageBrush(myImg) { Stretch = Stretch.UniformToFill };
            imgMyAvatar.MouseLeftButtonDown += ImgMyAvatar_MouseLeftButtonDown;

            Friends = new ObservableCollection<ChatItem>();
            Groups = new ObservableCollection<ChatItem>();
            Messages = new ObservableCollection<MessageModel>();
            Notifications = new ObservableCollection<NotificationItem>();

            lstFriends.ItemsSource = Friends;
            lstGroups.ItemsSource = Groups;
            listMessages.ItemsSource = Messages;
            lstNotifications.ItemsSource = Notifications;

            var stream = client.GetStream();
            reader = new StreamReader(stream); writer = new StreamWriter(stream) { AutoFlush = true };

            LoadTheme();

            SendToServer("GET_LIST");
            SendToServer("GET_REQ_LIST");
            SendToServer("GET_MY_EMAIL");
            SendToServer("GET_NOTIFS");
            SendToServer("GET_BACKUP_STATUS");

            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatAppSecurity");
            if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);

            string keyPath = Path.Combine(appDataPath, $"private_key_{this.UserName}.xml");
            if (File.Exists(keyPath))
            {
                EncryptionHelper.MyPrivateKey = File.ReadAllText(keyPath);
                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048))
                {
                    rsa.FromXmlString(EncryptionHelper.MyPrivateKey);
                    EncryptionHelper.MyPublicKey = rsa.ToXmlString(false);
                }
            }
            else
            {
                EncryptionHelper.GenerateRSAKeys();
                File.WriteAllText(keyPath, EncryptionHelper.MyPrivateKey);
            }

            string base64PubKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(EncryptionHelper.MyPublicKey));
            SendToServer($"UPDATE_PUBLICKEY|{base64PubKey}");

            Thread t = new Thread(ReceiveMessages) { IsBackground = true }; t.Start();
            try { if (File.Exists("tinnhan.wav")) { _soundPlayer = new System.Media.SoundPlayer("tinnhan.wav"); _soundPlayer.Load(); } } catch { }

            _videoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _videoTimer.Tick += (s, e) => {
                if (_activeVideoPlayer != null && _activeVideoSlider != null && !_isDraggingSlider)
                {
                    if (_activeVideoPlayer.NaturalDuration.HasTimeSpan)
                    {
                        _activeVideoSlider.Maximum = _activeVideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                        _activeVideoSlider.Value = _activeVideoPlayer.Position.TotalSeconds;
                    }
                }

                if (gridFullscreenVideo != null && gridFullscreenVideo.Visibility == Visibility.Visible && !_isDraggingFS)
                {
                    if (vidFullscreen.NaturalDuration.HasTimeSpan)
                    {
                        sliderTimelineFS.Maximum = vidFullscreen.NaturalDuration.TimeSpan.TotalSeconds;
                        sliderTimelineFS.Value = vidFullscreen.Position.TotalSeconds;
                    }
                }
            };
            _videoTimer.Start();

            _burnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _burnTimer.Tick += BurnTimer_Tick;
            _burnTimer.Start();

            if (Notifications.Count == 0 && FindName("txtEmptyNotif") is TextBlock txtEmpty) txtEmpty.Visibility = Visibility.Visible;

            if (FindName("popNotifications") is Popup popNotif)
            {
                popNotif.Closed += (s, e) => { if (FindName("btnNotifications") is Button btnNotif) btnNotif.Content = "✉️"; };
            }
        }

        private void LoadTheme()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatAppSecurity");
            if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);
            string themeFile = Path.Combine(appDataPath, $"theme_{this.UserName}.txt");

            if (File.Exists(themeFile))
            {
                string[] parts = File.ReadAllText(themeFile).Split('|');
                MessageModel.IsDarkMode = (parts[0] == "DARK");
                if (parts.Length > 1) MessageModel.IsAutoTheme = (parts[1] == "AUTO");
            }
            else
            {
                MessageModel.IsDarkMode = false;
                MessageModel.IsAutoTheme = false;
            }

            if (MessageModel.IsAutoTheme) CheckAutoTheme();
            else ApplyCurrentTheme();
        }

        private void SaveTheme()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatAppSecurity");
            string themeFile = Path.Combine(appDataPath, $"theme_{this.UserName}.txt");
            string autoStr = MessageModel.IsAutoTheme ? "AUTO" : "MANUAL";
            File.WriteAllText(themeFile, $"{(MessageModel.IsDarkMode ? "DARK" : "LIGHT")}|{autoStr}");
        }

        private void ApplyCurrentTheme()
        {
            if (MessageModel.IsDarkMode)
            {
                ThemeHelper.ApplyTitleBarTheme(this, MessageModel.IsDarkMode);
                SetTitleBarTheme(MessageModel.IsDarkMode);
                if (btnThemeToggle != null) btnThemeToggle.Content = "☀️";
                Application.Current.Resources["BgMain"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18191A"));
                Application.Current.Resources["BgPanel"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#242526"));
                Application.Current.Resources["TextMain"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4E6EB"));
                Application.Current.Resources["TextSub"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B3B8"));
                Application.Current.Resources["BorderColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#393A3B"));
                Application.Current.Resources["BgInput"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3B3C"));
                Application.Current.Resources["BgHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3B3C"));
                Application.Current.Resources["BgSelected"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#263951"));
            }
            else
            {
                if (btnThemeToggle != null) btnThemeToggle.Content = "🌙";
                Application.Current.Resources["BgMain"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                Application.Current.Resources["BgPanel"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"));
                Application.Current.Resources["TextMain"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1E21"));
                Application.Current.Resources["TextSub"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#65676B"));
                Application.Current.Resources["BorderColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4E6EB"));
                Application.Current.Resources["BgInput"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F2F5"));
                Application.Current.Resources["BgHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F2F2F2"));
                Application.Current.Resources["BgSelected"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6F2FF"));
            }

            if (Messages != null)
            {
                foreach (var msg in Messages) msg.RefreshTheme();
            }
        }

        private void CheckAutoTheme()
        {
            var now = DateTime.Now.TimeOfDay;
            var startDark = new TimeSpan(19, 0, 0);
            var endDark = new TimeSpan(6, 0, 0);

            bool shouldBeDark = (now >= startDark || now < endDark);

            if (MessageModel.IsDarkMode != shouldBeDark)
            {
                MessageModel.IsDarkMode = shouldBeDark;
                ApplyCurrentTheme();
                SaveTheme();
            }
        }

        private void btnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            MessageModel.IsDarkMode = !MessageModel.IsDarkMode;
            MessageModel.IsAutoTheme = false;
            if (FindName("chkAutoTheme") is CheckBox chk) chk.IsChecked = false;

            ApplyCurrentTheme();
            SaveTheme();
        }

        private void btnThemeSettings_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("chkAutoTheme") is CheckBox chk) chk.IsChecked = MessageModel.IsAutoTheme;
            if (FindName("popThemeSettings") is Popup pop) pop.IsOpen = !pop.IsOpen;
        }

        private void chkAutoTheme_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                MessageModel.IsAutoTheme = chk.IsChecked == true;
                if (MessageModel.IsAutoTheme)
                {
                    CheckAutoTheme();
                }
                SaveTheme();
            }
        }

        public static T FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                T childType = child as T;
                if (childType != null && (string.IsNullOrEmpty(childName) || ((FrameworkElement)child).Name == childName)) return childType;
                T foundChild = FindChild<T>(child, childName);
                if (foundChild != null) return foundChild;
            }
            return null;
        }

        public static T FindParent<T>(DependencyObject child, string name = null) where T : FrameworkElement
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null && (name == null || parent.Name == name)) return parent;
            else return FindParent<T>(parentObject, name);
        }

        private void SetChatInputState(bool isBlocked)
        {
            if (FindName("pnlChatInput") is Grid pnlInput) pnlInput.Visibility = isBlocked ? Visibility.Collapsed : Visibility.Visible;
            if (FindName("pnlChatBlocked") is Border pnlBlocked) pnlBlocked.Visibility = isBlocked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddNotification(string message)
        {
            Notifications.Insert(0, new NotificationItem { Message = message, Time = DateTime.Now.ToString("HH:mm - dd/MM/yyyy") });
            if (FindName("bdgUnreadNotif") is Border bdg) bdg.Visibility = Visibility.Visible;
            if (FindName("txtEmptyNotif") is TextBlock txtEmpty) txtEmpty.Visibility = Visibility.Collapsed;
        }

        private void btnNotifications_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("popNotifications") is Popup pop)
            {
                pop.IsOpen = !pop.IsOpen;
                if (sender is Button btn) btn.Content = pop.IsOpen ? "📩" : "✉️";
            }
            if (FindName("bdgUnreadNotif") is Border bdg) bdg.Visibility = Visibility.Collapsed;
            if (Notifications.Count == 0 && FindName("txtEmptyNotif") is TextBlock txtEmpty) txtEmpty.Visibility = Visibility.Visible;
        }

        private string PromptForPin(string title, string message)
        {
            Window prompt = new Window()
            {
                Width = 320,
                Height = 200,
                Title = title,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this,
                Background = Brushes.White
            };
            StackPanel stack = new StackPanel() { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock() { Text = message, Margin = new Thickness(0, 0, 0, 15), TextWrapping = TextWrapping.Wrap, FontSize = 14 });
            TextBox txtPin = new TextBox() { MaxLength = 6, Height = 35, FontSize = 18, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            txtPin.PreviewTextInput += (s, e) => { e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$"); };
            stack.Children.Add(txtPin);
            Button btnOk = new Button() { Content = "Xác nhận", Width = 120, Height = 35, Margin = new Thickness(0, 15, 0, 0), Background = (Brush)new BrushConverter().ConvertFrom("#0084FF"), Foreground = Brushes.White, FontWeight = FontWeights.Bold };
            bool isOk = false;
            btnOk.Click += (s, e) => { isOk = true; prompt.Close(); };
            stack.Children.Add(btnOk);
            prompt.Content = stack;
            prompt.ShowDialog();
            return isOk ? txtPin.Text : null;
        }

        private void btnE2EESettings_Click(object sender, RoutedEventArgs e)
        {
            Window syncWin = new Window()
            {
                Width = 350,
                Height = 300,
                Title = "Đồng bộ Khóa Bảo Mật",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this,
                Background = Brushes.White
            };
            StackPanel stack = new StackPanel() { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock() { Text = "🔒 Tùy chọn Đồng bộ Khóa Bảo Mật", FontWeight = FontWeights.Bold, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) });

            Button btnBackup = new Button() { Content = "Tạo / Cập nhật sao lưu (Mã PIN)", Height = 40, Background = (Brush)new BrushConverter().ConvertFrom("#0084FF"), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10), Cursor = Cursors.Hand };
            btnBackup.Click += (s, ev) => {
                syncWin.Close();

                if (string.IsNullOrEmpty(CurrentBackupEncrypted))
                {
                    string newPin = PromptForPin("Tạo mã PIN", "Nhập mã PIN 6 số để KHÓA bản sao lưu. VUI LÒNG NHỚ KỸ:");
                    if (!string.IsNullOrEmpty(newPin))
                    {
                        if (newPin.Length != 6) { MessageBox.Show("Mã PIN phải đúng 6 chữ số!"); return; }
                        string encKey = AesEncryptionHelper.EncryptPrivateKey(EncryptionHelper.MyPrivateKey, newPin);
                        CurrentBackupEncrypted = encKey;
                        SendToServer($"BACKUP_KEY|{encKey}");
                    }
                }
                else
                {
                    bool isVerified = false;
                    while (!isVerified)
                    {
                        string oldPin = PromptForPin("Xác minh PIN cũ", "Bạn đã có bản sao lưu. Vui lòng nhập mã PIN CŨ để xác minh:");

                        if (string.IsNullOrEmpty(oldPin)) return;

                        string decryptedKey = AesEncryptionHelper.DecryptPrivateKey(CurrentBackupEncrypted, oldPin);
                        if (!string.IsNullOrEmpty(decryptedKey) && decryptedKey.Contains("<RSAKeyValue>"))
                        {
                            isVerified = true;
                        }
                        else
                        {
                            MessageBox.Show("Mã PIN cũ không chính xác! Vui lòng thử lại.", "Lỗi");
                        }
                    }

                    string newPin = PromptForPin("Tạo mã PIN mới", "Mã PIN cũ chính xác! Nhập mã PIN 6 số MỚI:");
                    if (!string.IsNullOrEmpty(newPin))
                    {
                        if (newPin.Length != 6) { MessageBox.Show("Mã PIN phải đúng 6 chữ số!"); return; }
                        string encKey = AesEncryptionHelper.EncryptPrivateKey(EncryptionHelper.MyPrivateKey, newPin);
                        CurrentBackupEncrypted = encKey;
                        SendToServer($"BACKUP_KEY|{encKey}");
                    }
                }
            };

            Button btnRestore = new Button() { Content = "Khôi phục Khóa từ Server", Height = 40, Background = (Brush)new BrushConverter().ConvertFrom("#42B72A"), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 10) };
            btnRestore.Click += (s, ev) => {
                syncWin.Close();
                SendToServer("RECOVER_KEY");
            };

            Button btnReset = new Button() { Content = "Quên PIN? Tạo Khóa Mới", Height = 40, Background = (Brush)new BrushConverter().ConvertFrom("#E53935"), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            btnReset.Click += (s, ev) => {
                if (MessageBox.Show("CẢNH BÁO: Nếu tạo Khóa mới, toàn bộ tin nhắn bảo mật cũ sẽ vĩnh viễn không thể đọc được nữa.\n\nBạn có chắc chắn muốn tiếp tục?", "Mất dữ liệu cũ", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    syncWin.Close();

                    EncryptionHelper.GenerateRSAKeys();

                    string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatAppSecurity");
                    if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);
                    File.WriteAllText(Path.Combine(appDataPath, $"private_key_{this.UserName}.xml"), EncryptionHelper.MyPrivateKey);

                    string base64PubKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(EncryptionHelper.MyPublicKey));
                    SendToServer($"UPDATE_PUBLICKEY|{base64PubKey}");
                    FriendPublicKeys.Clear();

                    MessageBox.Show("Đã tạo Khóa bảo mật mới! Các tin nhắn từ thời điểm này sẽ được mã hóa bằng khóa mới.", "Hoàn tất");

                    string newPin = PromptForPin("Tạo mã PIN mới", "Nhập mã PIN 6 số để KHÓA bản sao lưu MỚI của bạn:");
                    if (!string.IsNullOrEmpty(newPin))
                    {
                        if (newPin.Length != 6) { MessageBox.Show("Mã PIN phải đúng 6 chữ số!"); return; }
                        string encKey = AesEncryptionHelper.EncryptPrivateKey(EncryptionHelper.MyPrivateKey, newPin);
                        CurrentBackupEncrypted = encKey;
                        SendToServer($"BACKUP_KEY|{encKey}");
                    }
                }
            };

            stack.Children.Add(btnBackup);
            stack.Children.Add(btnRestore);
            stack.Children.Add(btnReset);
            syncWin.Content = stack;
            syncWin.ShowDialog();
        }

        private void btnSelfDestruct_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (FindName("popSelfDestruct") is Popup pop) pop.IsOpen = !pop.IsOpen;
        }

        private void SelfDestruct_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag != null)
            {
                SelfDestructSeconds = int.Parse(rb.Tag.ToString());
                if (FindName("txtSelfDestructStatus") is TextBlock txt)
                {
                    txt.Text = rb.Content.ToString();
                    if (SelfDestructSeconds > 0) txt.Foreground = Brushes.Red;
                    else txt.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#65676B");
                }
                if (FindName("popSelfDestruct") is Popup pop) pop.IsOpen = false;
            }
        }

        private void btnRevealMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MessageModel msg)
            {
                msg.IsContentRevealed = true;
                msg.TimerVisibility = Visibility.Visible;
                msg.IsBurnStarted = true;

                // Lưu vào Cache ngay khi bấm
                BurnCacheManager.StartBurn(msg.RawPayload, msg.RemainingSeconds);

                if (currentTarget != null)
                {
                    SendToServer($"START_BURN|{currentTarget.Type}|{currentTarget.Id}|{msg.RawPayload}");
                }
            }
        }

        private void BurnTimer_Tick(object sender, EventArgs e)
        {
            // Kiểm tra Theme tự động
            if (MessageModel.IsAutoTheme)
            {
                CheckAutoTheme();
            }

            if (Messages == null) return;

            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                var msg = Messages[i];
                if (msg.IsSelfDestructing)
                {
                    if (msg.IsMe && !msg.IsBurnStarted) continue;
                    if (!msg.IsMe && !msg.IsContentRevealed) continue;

                    msg.RemainingSeconds--;

                    if (msg.RemainingSeconds <= 0)
                    {
                        string raw = msg.RawPayload;
                        Messages.RemoveAt(i);
                        BurnCacheManager.RemoveBurn(raw); // Dọn dẹp bộ nhớ đệm

                        if (currentTarget != null)
                        {
                            SendToServer($"BURN_MSG|{currentTarget.Type}|{currentTarget.Id}|{raw}");
                        }
                    }
                    else
                    {
                        if (msg.RemainingSeconds >= 3600) msg.TimerText = TimeSpan.FromSeconds(msg.RemainingSeconds).ToString(@"h\:mm\:ss");
                        else msg.TimerText = TimeSpan.FromSeconds(msg.RemainingSeconds).ToString(@"mm\:ss");
                    }
                }
            }
        }

        private async void btnCopyMessage_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is Button btn && btn.Tag is string textToCopy)
            {
                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy);

                    if (_copyCts != null)
                    {
                        _copyCts.Cancel();
                    }

                    if (_lastCopiedButton != null && _lastCopiedButton != btn)
                    {
                        _lastCopiedButton.Content = "📋";
                    }

                    _lastCopiedButton = btn;
                    btn.Content = "✔️";

                    _copyCts = new CancellationTokenSource();
                    var token = _copyCts.Token;

                    try
                    {
                        await Task.Delay(1500, token);
                        if (!token.IsCancellationRequested)
                        {
                            btn.Content = "📋";
                            if (_lastCopiedButton == btn) _lastCopiedButton = null;
                        }
                    }
                    catch (TaskCanceledException) { }
                }
            }
        }

        private void btnSaveMedia_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Button btn = sender as Button;
            if (btn?.DataContext is MessageModel msg)
            {
                if (msg.ImageVisibility == Visibility.Visible)
                {
                    Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog();
                    sfd.Title = "Lưu hình ảnh";
                    sfd.Filter = "JPEG Image|*.jpg|PNG Image|*.png";
                    sfd.FileName = "image_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg";

                    if (sfd.ShowDialog() == true)
                    {
                        try
                        {
                            byte[] imageBytes = Convert.FromBase64String(msg.AttachedFileBase64);
                            File.WriteAllBytes(sfd.FileName, imageBytes);
                            MessageBox.Show("Đã lưu ảnh về máy thành công!", "Hoàn tất");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Lỗi khi lưu ảnh: " + ex.Message, "Lỗi");
                        }
                    }
                }
                else if (msg.VideoVisibility == Visibility.Visible)
                {
                    if (string.IsNullOrEmpty(msg.VideoLocalPath) || !File.Exists(msg.VideoLocalPath))
                    {
                        MessageBox.Show("Vui lòng ấn nút Play để tải video về trước khi lưu!", "Thông báo");
                        return;
                    }

                    Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog();
                    sfd.Title = "Lưu video";
                    sfd.Filter = "Video File|*.mp4;*.mkv;*.avi";
                    sfd.FileName = msg.FileName ?? ("video_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");

                    if (sfd.ShowDialog() == true)
                    {
                        try
                        {
                            File.Copy(msg.VideoLocalPath, sfd.FileName, true);
                            MessageBox.Show("Đã lưu video về máy thành công!", "Hoàn tất");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Lỗi khi lưu video: " + ex.Message, "Lỗi");
                        }
                    }
                }
            }
        }

        private void VideoContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            Border pnlControls = FindChild<Border>(sender as Border, "pnlVideoControls");
            if (pnlControls != null) pnlControls.Visibility = Visibility.Visible;
        }

        private void VideoContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            Border pnlControls = FindChild<Border>(sender as Border, "pnlVideoControls");
            if (pnlControls != null) pnlControls.Visibility = Visibility.Collapsed;
        }

        private void pnlVideoControls_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { e.Handled = true; }

        private void VideoContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var msg = (sender as Border)?.DataContext as MessageModel;
            if (msg != null && !string.IsNullOrEmpty(msg.VideoLocalPath))
            {
                MediaElement player = FindChild<MediaElement>(sender as DependencyObject, "videoPlayer");
                Button btnPlayPause = FindChild<Button>(sender as DependencyObject, "btnPlayPause");
                Button btnCenter = FindChild<Button>(sender as DependencyObject, "btnCenterPlay");

                if (player != null) player.Pause();
                if (btnPlayPause != null) btnPlayPause.Content = "▶";
                if (btnCenter != null) btnCenter.Visibility = Visibility.Visible;

                vidFullscreen.Source = new Uri(msg.VideoLocalPath);
                gridFullscreenVideo.Visibility = Visibility.Visible;
                imgFullscreen.Visibility = Visibility.Collapsed;
                pnlImagePreview.Visibility = Visibility.Visible;

                vidFullscreen.Play();
                btnPlayPauseFS.Content = "⏸";
            }
        }

        private async void btnCenterPlay_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Button btn = sender as Button;
            MessageModel msg = btn.DataContext as MessageModel;

            if (msg == null || string.IsNullOrEmpty(msg.AttachedFileBase64))
            {
                MessageBox.Show("Video này quá cũ hoặc bị lỗi dữ liệu, không thể phát!", "Lỗi Video");
                return;
            }

            Grid parentGrid = FindParent<Grid>(btn, "MainVideoGrid");
            MediaElement player = FindChild<MediaElement>(parentGrid, "videoPlayer");
            Slider slider = FindChild<Slider>(parentGrid, "sliderTimeline");
            Button btnPlayPause = FindChild<Button>(parentGrid, "btnPlayPause");
            TextBlock txtStatus = FindChild<TextBlock>(parentGrid, "txtVideoStatus");

            if (player == null) return;
            if (_activeVideoPlayer != null && _activeVideoPlayer != player) _activeVideoPlayer.Pause();

            if (string.IsNullOrEmpty(msg.VideoLocalPath))
            {
                txtStatus.Visibility = Visibility.Visible;
                msg.VideoStatusText = "Đang tải video...";
                btn.Visibility = Visibility.Collapsed;

                string serverIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                string uniqueName = msg.AttachedFileBase64;
                string cacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatCache");
                if (!Directory.Exists(cacheFolder)) Directory.CreateDirectory(cacheFolder);
                string savePath = Path.Combine(cacheFolder, uniqueName);

                try
                {
                    await Task.Run(() =>
                    {
                        using (TcpClient fileClient = new TcpClient(serverIp, 8889))
                        using (NetworkStream stream = fileClient.GetStream())
                        {
                            byte[] req = Encoding.UTF8.GetBytes($"DOWNLOAD|{uniqueName}\n");
                            stream.Write(req, 0, req.Length);
                            string res = ReadLineManual(stream);
                            if (res.StartsWith("FILE_SIZE|"))
                            {
                                long fileSize = long.Parse(res.Split('|')[1]);
                                byte[] buffer = new byte[81920];
                                long totalRead = 0;
                                using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                                {
                                    int read;
                                    while (totalRead < fileSize && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, fileSize - totalRead))) > 0)
                                    { fs.Write(buffer, 0, read); totalRead += read; }
                                }
                            }
                        }
                    });
                    msg.VideoLocalPath = savePath;
                }
                catch (Exception ex)
                {
                    msg.VideoStatusText = "Lỗi: " + ex.Message;
                    btn.Visibility = Visibility.Visible;
                    return;
                }
                finally { txtStatus.Visibility = Visibility.Collapsed; }
            }

            _activeVideoPlayer = player;
            _activeVideoSlider = slider;
            player.Play();
            btn.Visibility = Visibility.Collapsed;
            if (btnPlayPause != null) btnPlayPause.Content = "⏸";
        }

        private void btnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Button btn = sender as Button;
            Grid parentGrid = FindParent<Grid>(btn, "MainVideoGrid");
            MediaElement player = FindChild<MediaElement>(parentGrid, "videoPlayer");
            Button btnCenter = FindChild<Button>(parentGrid, "btnCenterPlay");

            if (player != null)
            {
                if (btn.Content.ToString() == "⏸") { player.Pause(); btn.Content = "▶"; if (btnCenter != null) btnCenter.Visibility = Visibility.Visible; }
                else
                {
                    if (_activeVideoPlayer != null && _activeVideoPlayer != player) { _activeVideoPlayer.Pause(); }
                    _activeVideoPlayer = player;
                    _activeVideoSlider = FindChild<Slider>(parentGrid, "sliderTimeline");
                    player.Play(); btn.Content = "⏸";
                    if (btnCenter != null) btnCenter.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void btnVolume_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Button btn = sender as Button;
            MediaElement player = FindChild<MediaElement>(FindParent<Grid>(btn, "MainVideoGrid"), "videoPlayer");
            if (player != null) { player.IsMuted = !player.IsMuted; btn.Content = player.IsMuted ? "🔇" : "🔊"; }
        }

        private void Volume_MouseEnter(object sender, MouseEventArgs e) { Popup pop = FindChild<Popup>(sender as Grid, "popVolume"); if (pop != null) pop.IsOpen = true; }
        private void Volume_MouseLeave(object sender, MouseEventArgs e)
        {
            Popup pop = FindChild<Popup>(sender as Grid, "popVolume");
            if (pop != null && !pop.IsMouseOver) pop.IsOpen = false;
        }
        private void popVolume_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Popup pop) pop.IsOpen = false;
        }

        private void sliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Slider slider = sender as Slider;
            Grid parentGrid = FindParent<Grid>(slider, "MainVideoGrid");
            MediaElement player = FindChild<MediaElement>(parentGrid, "videoPlayer");
            Button btnVol = FindChild<Button>(parentGrid, "btnVolume");
            if (player != null) { player.Volume = slider.Value; player.IsMuted = (player.Volume == 0); if (btnVol != null) btnVol.Content = player.IsMuted ? "🔇" : "🔊"; }
        }

        private void sliderTimeline_MouseDown(object sender, MouseButtonEventArgs e) { _isDraggingSlider = true; }
        private void sliderTimeline_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            Slider slider = sender as Slider;
            MediaElement player = FindChild<MediaElement>(FindParent<Grid>(slider, "MainVideoGrid"), "videoPlayer");
            if (player != null && player.NaturalDuration.HasTimeSpan) player.Position = TimeSpan.FromSeconds(slider.Value);
        }

        private void videoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            MediaElement player = sender as MediaElement;
            player.Position = TimeSpan.Zero; player.Pause();
            Grid parentGrid = FindParent<Grid>(player, "MainVideoGrid");
            Button btnCenter = FindChild<Button>(parentGrid, "btnCenterPlay");
            Button btnPlayPause = FindChild<Button>(parentGrid, "btnPlayPause");
            if (btnCenter != null) btnCenter.Visibility = Visibility.Visible;
            if (btnPlayPause != null) btnPlayPause.Content = "▶";
        }

        private void videoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            MediaElement player = sender as MediaElement;
            Grid parentGrid = FindParent<Grid>(player, "MainVideoGrid");
            Slider slider = FindChild<Slider>(parentGrid, "sliderTimeline");
            if (player.NaturalDuration.HasTimeSpan && slider != null) slider.Maximum = player.NaturalDuration.TimeSpan.TotalSeconds;
        }

        private void btnPlayPauseFS_Click(object sender, RoutedEventArgs e)
        {
            if (btnPlayPauseFS.Content.ToString() == "⏸") { vidFullscreen.Pause(); btnPlayPauseFS.Content = "▶"; }
            else { vidFullscreen.Play(); btnPlayPauseFS.Content = "⏸"; }
        }

        private void sliderTimelineFS_MouseDown(object sender, MouseButtonEventArgs e) { _isDraggingFS = true; }
        private void sliderTimelineFS_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingFS = false;
            if (vidFullscreen.NaturalDuration.HasTimeSpan) vidFullscreen.Position = TimeSpan.FromSeconds(sliderTimelineFS.Value);
        }

        private void btnVolumeFS_Click(object sender, RoutedEventArgs e)
        {
            vidFullscreen.IsMuted = !vidFullscreen.IsMuted;
            btnVolumeFS.Content = vidFullscreen.IsMuted ? "🔇" : "🔊";
        }

        private void VolumeFS_MouseEnter(object sender, MouseEventArgs e) { if (popVolumeFS != null) popVolumeFS.IsOpen = true; }
        private void VolumeFS_MouseLeave(object sender, MouseEventArgs e) { if (popVolumeFS != null && !popVolumeFS.IsMouseOver) popVolumeFS.IsOpen = false; }
        private void popVolumeFS_MouseLeave(object sender, MouseEventArgs e) { if (sender is Popup pop) pop.IsOpen = false; }

        private void sliderVolumeFS_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (vidFullscreen != null && btnVolumeFS != null)
            {
                vidFullscreen.Volume = sliderVolumeFS.Value;
                vidFullscreen.IsMuted = (vidFullscreen.Volume == 0);
                btnVolumeFS.Content = vidFullscreen.IsMuted ? "🔇" : "🔊";
            }
        }

        private void vidFullscreen_MediaEnded(object sender, RoutedEventArgs e)
        {
            vidFullscreen.Position = TimeSpan.Zero; vidFullscreen.Pause();
            btnPlayPauseFS.Content = "▶";
        }

        private void vidFullscreen_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (vidFullscreen.NaturalDuration.HasTimeSpan) sliderTimelineFS.Maximum = vidFullscreen.NaturalDuration.TimeSpan.TotalSeconds;
        }

        private void vidFullscreen_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            btnPlayPauseFS_Click(null, null);
        }

        private void UpdateInfoPanelMedia()
        {
            if (FindName("lstInfoMedia") is ItemsControl lstMedia)
            {
                var mediaMsgs = Messages.Where(m => m.ImageVisibility == Visibility.Visible || m.VideoVisibility == Visibility.Visible).ToList();
                lstMedia.ItemsSource = null;
                lstMedia.ItemsSource = mediaMsgs;
            }

            if (FindName("lstInfoFiles") is ItemsControl lstFiles)
            {
                var fileMsgs = Messages.Where(m => m.FileVisibility == Visibility.Visible).ToList();
                lstFiles.ItemsSource = null;
                lstFiles.ItemsSource = fileMsgs;
            }
        }

        private void InfoMediaVideo_Click(object sender, MouseButtonEventArgs e)
        {
            var msg = (sender as FrameworkElement)?.DataContext as MessageModel;
            if (msg != null && !string.IsNullOrEmpty(msg.VideoLocalPath))
            {
                vidFullscreen.Source = new Uri(msg.VideoLocalPath);
                gridFullscreenVideo.Visibility = Visibility.Visible;
                imgFullscreen.Visibility = Visibility.Collapsed;
                pnlImagePreview.Visibility = Visibility.Visible;
                vidFullscreen.Play();
                btnPlayPauseFS.Content = "⏸";
            }
            else
            {
                MessageBox.Show("Vui lòng phát video trên khung chat một lần trước để ứng dụng tải video về máy!", "Thông báo");
            }
        }

        private void txtInfoInviteCode_Click(object sender, MouseButtonEventArgs e)
        {
            if (FindName("txtInfoInviteCode") is TextBlock txtCode && txtCode.Text != "Đang tải...")
            {
                Clipboard.SetText(txtCode.Text);
                MessageBox.Show("Đã copy mã mời vào bộ nhớ tạm!");
            }
        }

        private void btnToggleInfo_Click(object sender, RoutedEventArgs e)
        {
            if (currentTarget == null) return;
            if (!isInfoPanelVisible) { colInfoPanel.Width = new GridLength(300); pnlInfo.DataContext = currentTarget; isInfoPanelVisible = true; }
            else { colInfoPanel.Width = new GridLength(0); isInfoPanelVisible = false; }
        }

        private void btnMute_Click(object sender, MouseButtonEventArgs e)
        {
            isMuted = !isMuted;
            if (FindName("txtMuteIcon") is TextBlock txtIcon) txtIcon.Text = isMuted ? "🔕" : "🔔";
            if (FindName("txtMuteText") is TextBlock txtText)
            {
                txtText.Text = isMuted ? "Đã tắt" : "Thông báo";
                if (isMuted) txtText.Foreground = Brushes.Red;
                else txtText.SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
            }

            if (FindName("txtMuteIcon2") is TextBlock txtIcon2) txtIcon2.Text = isMuted ? "🔕" : "🔔";
            if (FindName("txtMuteText2") is TextBlock txtText2)
            {
                txtText2.Text = isMuted ? "Đã tắt" : "Tắt thông báo";
                if (isMuted) txtText2.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E53935");
                else txtText2.SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
            }
        }

        private void btnShowSearchMsg_Click(object sender, MouseButtonEventArgs e)
        {
            if (FindName("pnlSearchMsg") is StackPanel pnl)
            {
                pnl.Visibility = pnl.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (pnl.Visibility == Visibility.Visible && FindName("txtSearchMsg") is TextBox txt) txt.Focus();
            }
        }

        private void txtSearchMsg_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox txt)
            {
                string keyword = txt.Text.ToLower().Trim(); MessageModel firstMatch = null;
                foreach (var msg in Messages)
                {
                    if (!string.IsNullOrEmpty(keyword) && msg.Content.ToLower().Contains(keyword)) { msg.BackgroundColor = "OrangeRed"; if (firstMatch == null) firstMatch = msg; }
                    else { msg.BackgroundColor = null; }
                }
                if (firstMatch != null) listMessages.ScrollIntoView(firstMatch);
            }
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string keyword = txtSearch.Text.ToLower().Trim();
            Friends.Clear();
            foreach (var item in _allFriends)
            {
                if (string.IsNullOrEmpty(keyword) || item.Name.ToLower().Contains(keyword))
                    Friends.Add(item);
            }
        }

        private void btnUnfriend_Click(object sender, RoutedEventArgs e)
        {
            if (currentTarget == null || currentTarget.Type != "F") return;
            if (MessageBox.Show($"Bạn có chắc chắn muốn hủy kết bạn với {currentTarget.Name}?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                SendToServer($"UNFRIEND|{currentTarget.Id}");
                Friends.Remove(currentTarget);
                _allFriends.Remove(currentTarget);

                SetChatInputState(true);
                if (FindName("btnInfoUnfriend") is Button btnU) btnU.Visibility = Visibility.Collapsed;
                if (FindName("bdgE2EE") is Border bdgE) bdgE.Visibility = Visibility.Collapsed;
                if (FindName("pnlSelfDestructContainer") is Border pnlSelf) pnlSelf.Visibility = Visibility.Collapsed;
            }
        }

        private void btnLeaveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (currentTarget == null || currentTarget.Type != "G") return;
            if (MessageBox.Show($"Bạn có chắc chắn muốn rời khỏi nhóm {currentTarget.Name}?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                SendToServer($"LEAVE_GROUP|{currentTarget.Id}");
                Groups.Remove(currentTarget); _allGroups.Remove(currentTarget);
                colInfoPanel.Width = new GridLength(0); isInfoPanelVisible = false; currentTarget = null; Messages.Clear(); lblCurrentChat.Text = "Chọn một cuộc hội thoại...";
            }
        }

        private void ImgMyAvatar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ImageSource curAvt = (imgMyAvatar.Fill is ImageBrush b) ? b.ImageSource : null;
            UserProfileWindow p = new UserProfileWindow(this, DisplayName, UserName, curAvt, UserEmail) { Owner = this };
            p.ShowDialog();
        }

        public void SendToServer(string msg) { try { if (client.Connected) writer.WriteLine(msg); } catch { } }

        private void ReceiveMessages()
        {
            try
            {
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    string[] parts = line.Split('|');

                    Dispatcher.BeginInvoke(new Action(() => {
                        if (parts[0] == "LIST")
                        {
                            Friends.Clear(); Groups.Clear();
                            for (int i = 1; i < parts.Length; i++)
                            {
                                string[] itm = parts[i].Split(':');
                                if (itm[0] == "F") Friends.Add(new ChatItem { Type = "F", Id = int.Parse(itm[1]), Name = itm[2], IsOnline = (itm[3] == "1"), AvatarSource = ImageUtils.Base64ToImage(itm.Length > 4 ? itm[4] : "") });
                                else Groups.Add(new ChatItem { Type = "G", Id = int.Parse(itm[1]), Name = itm[2] });
                            }
                            _allFriends = Friends.ToList(); _allGroups = Groups.ToList();
                        }
                        else if (parts[0] == "MSG_PRIVATE")
                        {
                            if (!isMuted) { if (_soundPlayer != null) _soundPlayer.Play(); else System.Media.SystemSounds.Beep.Play(); }
                            if (currentTarget != null && currentTarget.Id == int.Parse(parts[1]) && currentTarget.Type == "F")
                            {
                                Messages.Add(new MessageModel { RawPayload = parts[3], Content = parts[3], IsMe = false, AvatarSource = currentTarget.AvatarSource });
                                UpdateMessageGroupings();
                                if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                                UpdateInfoPanelMedia();
                            }
                            else foreach (var f in Friends) if (f.Id == int.Parse(parts[1])) f.HasNewMessage = true;
                        }
                        else if (parts[0] == "HISTORY")
                        {
                            Messages.Clear();
                            for (int i = 1; i < parts.Length; i++)
                            {
                                string[] m = parts[i].Split(new[] { ':' }, 2);
                                if (m.Length == 2) Messages.Add(new MessageModel { RawPayload = m[1], Content = m[1], IsMe = (int.Parse(m[0]) != currentTarget.Id), AvatarSource = currentTarget.AvatarSource });
                            }
                            UpdateMessageGroupings();
                            if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                            UpdateInfoPanelMedia();
                        }
                        else if (parts[0] == "MSG_GROUP")
                        {
                            if (!isMuted) { if (_soundPlayer != null) _soundPlayer.Play(); else System.Media.SystemSounds.Beep.Play(); }
                            int gId = int.Parse(parts[1]); string senderName = parts[2]; string content = parts[3];
                            if (currentTarget != null && currentTarget.Id == gId && currentTarget.Type == "G")
                            {
                                Messages.Add(new MessageModel { RawPayload = $"{senderName}:\n{content}", Content = $"{senderName}:\n{content}", IsMe = false, AvatarSource = ImageUtils.Base64ToImage("") });
                                UpdateMessageGroupings();
                                if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                                UpdateInfoPanelMedia();
                            }
                            else foreach (var g in Groups) if (g.Id == gId) g.HasNewMessage = true;
                        }
                        else if (parts[0] == "GROUP_HISTORY")
                        {
                            Messages.Clear();
                            int myId = int.Parse(this.UserName);
                            for (int i = 1; i < parts.Length; i++)
                            {
                                string[] m = parts[i].Split(new[] { ':' }, 3);
                                if (m.Length == 3)
                                {
                                    bool isMe = (int.Parse(m[0]) == myId);
                                    Messages.Add(new MessageModel { RawPayload = (isMe ? m[2] : $"{m[1]}:\n{m[2]}"), Content = isMe ? m[2] : $"{m[1]}:\n{m[2]}", IsMe = isMe, AvatarSource = ImageUtils.Base64ToImage("") });
                                }
                            }
                            UpdateMessageGroupings();
                            if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                            UpdateInfoPanelMedia();
                        }
                        else if (parts[0] == "GROUP_MEMBERS")
                        {
                            int creatorId = int.Parse(parts[2]);
                            string inviteCode = parts[3];

                            if (FindName("txtInfoInviteCode") is TextBlock txtCodeInfo) txtCodeInfo.Text = inviteCode;

                            var members = new ObservableCollection<MemberItem>();
                            int myId = int.Parse(this.UserName);
                            int memberCount = 0;

                            for (int i = 4; i < parts.Length; i++)
                            {
                                string[] m = parts[i].Split(':'); int mid = int.Parse(m[0]);
                                bool isMe = (mid == myId);
                                bool isFriend = _allFriends.Any(f => f.Id == mid);

                                members.Add(new MemberItem
                                {
                                    Id = mid,
                                    Name = m[1] + (isMe ? " (Bạn)" : ""),
                                    Avatar = ImageUtils.Base64ToImage(m.Length > 2 ? m[2] : ""),
                                    KickVisibility = (creatorId == myId && !isMe) ? Visibility.Visible : Visibility.Collapsed,
                                    AddFriendVisibility = (!isMe && !isFriend) ? Visibility.Visible : Visibility.Collapsed
                                });
                                memberCount++;
                            }

                            if (FindName("lstInfoMembers") is ItemsControl lstMem) lstMem.ItemsSource = members;
                            if (FindName("expGroupMembers") is Expander expM) expM.Header = $"Thành viên ({memberCount})";

                            if (FindName("btnInfoDeleteGroup") is Button btnDel) btnDel.Visibility = (creatorId == myId) ? Visibility.Visible : Visibility.Collapsed;
                            if (FindName("btnInfoLeaveGroup") is Button btnLeave) btnLeave.Visibility = (creatorId != myId) ? Visibility.Visible : Visibility.Collapsed;
                        }
                        else if (parts[0] == "SEARCH_RES") { if (_addFriendWin != null) _addFriendWin.UpdateSearchResult(parts); }
                        else if (parts[0] == "REQ_LIST")
                        {
                            if (_addFriendWin != null) _addFriendWin.UpdateRequestList(parts);
                            int requestCount = parts.Length - 1;
                            if (FindName("bdgNewRequest") is Border bdg && FindName("txtRequestCount") is TextBlock txtCount)
                            {
                                if (requestCount > 0) { bdg.Visibility = Visibility.Visible; txtCount.Text = requestCount > 9 ? "9+" : requestCount.ToString(); }
                                else bdg.Visibility = Visibility.Collapsed;
                            }
                        }
                        else if (parts[0] == "NOTIF_LIST")
                        {
                            Notifications.Clear();
                            for (int i = 1; i < parts.Length; i += 2)
                            {
                                if (i + 1 < parts.Length)
                                {
                                    Notifications.Add(new NotificationItem { Message = parts[i], Time = parts[i + 1] });
                                }
                            }
                            if (Notifications.Count == 0 && FindName("txtEmptyNotif") is TextBlock txtEmpty) txtEmpty.Visibility = Visibility.Visible;
                            else if (FindName("txtEmptyNotif") is TextBlock txtEmpty2) txtEmpty2.Visibility = Visibility.Collapsed;
                        }
                        else if (parts[0] == "NEW_NOTIF")
                        {
                            string msg = parts[1];
                            string time = parts.Length > 2 ? parts[2] : DateTime.Now.ToString("HH:mm - dd/MM/yyyy");
                            Notifications.Insert(0, new NotificationItem { Message = msg, Time = time });
                            if (FindName("bdgUnreadNotif") is Border bdg) bdg.Visibility = Visibility.Visible;
                            if (FindName("txtEmptyNotif") is TextBlock txtEmpty) txtEmpty.Visibility = Visibility.Collapsed;
                        }
                        else if (parts[0] == "UNFRIENDED")
                        {
                            int unfrienderId = int.Parse(parts[1]);
                            var f = Friends.FirstOrDefault(x => x.Id == unfrienderId && x.Type == "F");
                            if (f != null) Friends.Remove(f);
                            var af = _allFriends.FirstOrDefault(x => x.Id == unfrienderId && x.Type == "F");
                            if (af != null) _allFriends.Remove(af);

                            if (currentTarget != null && currentTarget.Id == unfrienderId && currentTarget.Type == "F")
                            {
                                SetChatInputState(true);
                                if (FindName("btnInfoUnfriend") is Button btnU) btnU.Visibility = Visibility.Collapsed;
                                if (FindName("bdgE2EE") is Border bdgE) bdgE.Visibility = Visibility.Collapsed;
                                if (FindName("pnlSelfDestructContainer") is Border pnlSelf) pnlSelf.Visibility = Visibility.Collapsed;
                            }
                        }
                        else if (parts[0] == "PUBLICKEY_RES")
                        {
                            int targetId = int.Parse(parts[1]);
                            string pubKeyBase64 = parts[2];
                            if (!string.IsNullOrEmpty(pubKeyBase64))
                            {
                                string pubKeyXml = Encoding.UTF8.GetString(Convert.FromBase64String(pubKeyBase64));
                                FriendPublicKeys[targetId] = pubKeyXml;
                            }
                        }
                        else if (parts[0] == "BACKUP_STATUS")
                        {
                            CurrentBackupEncrypted = parts.Length > 1 ? parts[1] : "";
                        }
                        else if (parts[0] == "RECOVER_KEY_RES")
                        {
                            string encData = parts.Length > 1 ? parts[1] : "";
                            if (string.IsNullOrEmpty(encData))
                            {
                                MessageBox.Show("Bạn chưa có bản sao lưu Khóa nào trên Server!", "Lỗi");
                            }
                            else
                            {
                                bool isRecovered = false;
                                while (!isRecovered)
                                {
                                    string pin = PromptForPin("Nhập mã PIN", "Vui lòng nhập mã PIN 6 số để khôi phục Khóa bảo mật:");

                                    if (string.IsNullOrEmpty(pin)) break;

                                    string decryptedKey = AesEncryptionHelper.DecryptPrivateKey(encData, pin);
                                    if (!string.IsNullOrEmpty(decryptedKey) && decryptedKey.Contains("<RSAKeyValue>"))
                                    {
                                        isRecovered = true;

                                        EncryptionHelper.MyPrivateKey = decryptedKey;
                                        using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048))
                                        {
                                            rsa.FromXmlString(EncryptionHelper.MyPrivateKey);
                                            EncryptionHelper.MyPublicKey = rsa.ToXmlString(false);
                                        }

                                        string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatAppSecurity");
                                        if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);
                                        File.WriteAllText(Path.Combine(appDataPath, $"private_key_{this.UserName}.xml"), EncryptionHelper.MyPrivateKey);

                                        string base64PubKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(EncryptionHelper.MyPublicKey));
                                        SendToServer($"UPDATE_PUBLICKEY|{base64PubKey}");

                                        CurrentBackupEncrypted = encData;

                                        MessageBox.Show("Đã khôi phục Khóa bảo mật thành công! Giờ bạn đã có thể đọc tin nhắn cũ.", "Hoàn tất");

                                        if (currentTarget != null && currentTarget.Type == "F")
                                        {
                                            Messages.Clear(); SendToServer($"GET_HISTORY|{currentTarget.Id}");
                                        }
                                    }
                                    else
                                    {
                                        MessageBox.Show("Mã PIN không chính xác hoặc dữ liệu bị hỏng! Vui lòng thử lại.", "Lỗi");
                                    }
                                }
                            }
                        }
                        else if (parts[0] == "BACKUP_KEY_OK")
                        {
                            MessageBox.Show("Đã sao lưu Khóa bảo mật lên Server thành công!", "Hoàn tất");
                        }
                        else if (parts[0] == "MSG_SYS") MessageBox.Show(parts.Length > 1 ? parts[1] : "Thông báo", "Thông báo");
                        else if (parts[0] == "NEW_REQ") SendToServer("GET_REQ_LIST");
                        else if (parts[0] == "MY_EMAIL") UserEmail = parts.Length > 1 ? parts[1] : "Chưa cập nhật Email";

                        // --- CÁC HÀM CẬP NHẬT GIAO DIỆN THỜI GIAN THỰC ---
                        else if (parts[0] == "FRIEND_STATUS")
                        {
                            int friendId = int.Parse(parts[1]);
                            bool isOnline = (parts[2] == "1");

                            var friend = Friends.FirstOrDefault(f => f.Id == friendId && f.Type == "F");
                            if (friend != null) friend.IsOnline = isOnline;

                            var allFriend = _allFriends.FirstOrDefault(f => f.Id == friendId && f.Type == "F");
                            if (allFriend != null) allFriend.IsOnline = isOnline;

                            if (currentTarget != null && currentTarget.Id == friendId && currentTarget.Type == "F")
                            {
                                currentTarget.IsOnline = isOnline;
                            }
                        }
                        else if (parts[0] == "PROFILE_UPDATED_OK")
                        {
                            string newName = parts[1];
                            bool hasAvt = (parts[2] == "1");
                            string newAvt = parts.Length > 3 ? parts[3] : "";

                            DisplayName = newName;
                            lblWelcome.Text = newName;

                            if (hasAvt && !string.IsNullOrEmpty(newAvt))
                            {
                                var myImg = ImageUtils.Base64ToImage(newAvt);
                                if (myImg != null) imgMyAvatar.Fill = new ImageBrush(myImg) { Stretch = Stretch.UniformToFill };
                            }
                        }
                        else if (parts[0] == "AVATAR_UPDATE")
                        {
                            int friendId = int.Parse(parts[1]);
                            string newAvt = parts.Length > 2 ? parts[2] : "";
                            ImageSource newImg = ImageUtils.Base64ToImage(newAvt);

                            var friend = Friends.FirstOrDefault(f => f.Id == friendId && f.Type == "F");
                            if (friend != null) friend.AvatarSource = newImg;

                            var allFriend = _allFriends.FirstOrDefault(f => f.Id == friendId && f.Type == "F");
                            if (allFriend != null) allFriend.AvatarSource = newImg;

                            if (currentTarget != null && currentTarget.Id == friendId && currentTarget.Type == "F")
                            {
                                currentTarget.AvatarSource = newImg;
                                foreach (var msg in Messages) { if (!msg.IsMe) msg.AvatarSource = newImg; }
                            }
                        }
                        else if (parts[0] == "FRIEND_PROFILE_UPDATE")
                        {
                            int friendId = int.Parse(parts[1]);
                            string newName = parts[2];
                            bool hasAvt = (parts[3] == "1");
                            string newAvt = parts.Length > 4 ? parts[4] : "";

                            var friend = Friends.FirstOrDefault(f => f.Id == friendId && f.Type == "F");
                            if (friend != null)
                            {
                                friend.Name = newName;
                                if (hasAvt) friend.AvatarSource = ImageUtils.Base64ToImage(newAvt);
                            }

                            var allFriend = _allFriends.FirstOrDefault(f => f.Id == friendId && f.Type == "F");
                            if (allFriend != null)
                            {
                                allFriend.Name = newName;
                                if (hasAvt) allFriend.AvatarSource = ImageUtils.Base64ToImage(newAvt);
                            }

                            if (currentTarget != null && currentTarget.Id == friendId && currentTarget.Type == "F")
                            {
                                currentTarget.Name = newName;
                                lblCurrentChat.Text = $"Chat với: {newName}";
                                if (hasAvt)
                                {
                                    ImageSource newImg = ImageUtils.Base64ToImage(newAvt);
                                    currentTarget.AvatarSource = newImg;
                                    foreach (var msg in Messages) { if (!msg.IsMe) msg.AvatarSource = newImg; }
                                }
                            }
                        }

                        // --- XỬ LÝ ĐỒNG BỘ TIN NHẮN TỰ HỦY ---
                        else if (parts[0] == "MSG_BURNED")
                        {
                            string burnedPayload = string.Join("|", parts.Skip(1));
                            var msgToRemove = Messages.FirstOrDefault(m => m.RawPayload == burnedPayload);
                            if (msgToRemove != null) Messages.Remove(msgToRemove);
                            BurnCacheManager.RemoveBurn(burnedPayload);
                        }
                        else if (parts[0] == "MSG_BURN_STARTED")
                        {
                            string payload = string.Join("|", parts.Skip(1)).Trim();
                            var msgToStart = Messages.FirstOrDefault(m => m.RawPayload != null &&
                                            (m.RawPayload.Contains(payload) || payload.Contains(m.RawPayload)) && m.IsMe);

                            if (msgToStart != null)
                            {
                                msgToStart.IsBurnStarted = true;
                                if (msgToStart.RemainingSeconds >= 3600)
                                    msgToStart.TimerText = TimeSpan.FromSeconds(msgToStart.RemainingSeconds).ToString(@"h\:mm\:ss");
                                else
                                    msgToStart.TimerText = TimeSpan.FromSeconds(msgToStart.RemainingSeconds).ToString(@"mm\:ss");

                                BurnCacheManager.StartBurn(msgToStart.RawPayload, msgToStart.RemainingSeconds);
                            }
                        }
                        else if (parts[0] == "KEY_CHANGED")
                        {
                            int changedUserId = int.Parse(parts[1]);

                            // Xóa ngay khóa rác/khóa cũ trong RAM
                            if (FriendPublicKeys.ContainsKey(changedUserId))
                            {
                                FriendPublicKeys.Remove(changedUserId);

                                // Chủ động xin lại khóa mới luôn để lát nhắn tin không bị khựng
                                SendToServer($"GET_PUBLICKEY|{changedUserId}");
                            }
                        }
                        else if (parts[0] == "DELETE_ACCOUNT_OK")
                        {
                            MessageBox.Show("Tài khoản của bạn đã được xóa vĩnh viễn khỏi hệ thống.", "Đã xóa");
                            client.Close(); new LoginWindow().Show(); this.Close();
                        }
                    }));
                }
            }
            catch { }
        }

        private void btnSend_Click(object sender, RoutedEventArgs e)
        {
            if (currentTarget == null || string.IsNullOrWhiteSpace(txtMessage.Text)) return;

            string originalText = txtMessage.Text;

            if (SelfDestructSeconds > 0)
            {
                originalText = $"[BURN:{SelfDestructSeconds}]" + originalText;
            }

            string payload = originalText;

            if (currentTarget.Type == "F")
            {
                if (FriendPublicKeys.ContainsKey(currentTarget.Id))
                {
                    string aesKey, aesIV;
                    string encryptedMsg = AesEncryptionHelper.Encrypt(originalText, out aesKey, out aesIV);
                    string encKeyThem = EncryptionHelper.EncryptRSA(aesKey, FriendPublicKeys[currentTarget.Id]);
                    string encKeyMe = EncryptionHelper.EncryptRSA(aesKey, EncryptionHelper.MyPublicKey);

                    payload = $"[E2EE]{encKeyThem}*{encKeyMe}*{aesIV}*{encryptedMsg}";
                }
                else
                {
                    MessageBox.Show("Đang thiết lập kênh bảo mật với người này, vui lòng chờ 1-2 giây rồi gửi lại!");
                    SendToServer($"GET_PUBLICKEY|{currentTarget.Id}");
                    return;
                }

                Messages.Add(new MessageModel { RawPayload = payload, Content = originalText, IsMe = true });
                UpdateMessageGroupings();
                if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);

                SendToServer($"SEND_PRIVATE|{currentTarget.Id}|{payload}");
            }
            else if (currentTarget.Type == "G")
            {
                Messages.Add(new MessageModel { RawPayload = payload, Content = originalText, IsMe = true });
                UpdateMessageGroupings();
                if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                SendToServer($"SEND_GROUP|{currentTarget.Id}|{payload}");
            }

            txtMessage.Clear();
        }

        private void lstFriends_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstFriends.SelectedItem is ChatItem item)
            {
                lstGroups.SelectedItem = null; currentTarget = item; item.HasNewMessage = false; lblCurrentChat.Text = $"Chat với: {item.Name}";

                if (FindName("expGroupCode") is Expander expGC) expGC.Visibility = Visibility.Collapsed;
                if (FindName("expGroupMembers") is Expander expGM) expGM.Visibility = Visibility.Collapsed;
                if (FindName("btnInfoLeaveGroup") is Button btnL) btnL.Visibility = Visibility.Collapsed;
                if (FindName("btnInfoDeleteGroup") is Button btnD) btnD.Visibility = Visibility.Collapsed;

                if (FindName("btnInfoUnfriend") is Button btnU) btnU.Visibility = Visibility.Visible;

                if (FindName("bdgE2EE") is Border bdgE) bdgE.Visibility = Visibility.Visible;
                if (FindName("pnlSelfDestructContainer") is Border pnlSelf) pnlSelf.Visibility = Visibility.Visible;

                SetChatInputState(false);

                if (!FriendPublicKeys.ContainsKey(item.Id))
                {
                    SendToServer($"GET_PUBLICKEY|{item.Id}");
                }

                Messages.Clear(); SendToServer($"GET_HISTORY|{item.Id}");
                if (isInfoPanelVisible) pnlInfo.DataContext = item;
                UpdateInfoPanelMedia();
            }
        }

        private void UpdateMessageGroupings()
        {
            for (int i = 0; i < Messages.Count; i++)
            {
                var current = Messages[i];
                bool isMe = current.IsMe;

                bool sameAsPrev = (i > 0) && (Messages[i - 1].IsMe == isMe) && (Messages[i - 1].SenderName == current.SenderName);
                bool sameAsNext = (i < Messages.Count - 1) && (Messages[i + 1].IsMe == isMe) && (Messages[i + 1].SenderName == current.SenderName);

                current.AvatarVisibility = isMe ? Visibility.Collapsed : (sameAsNext ? Visibility.Hidden : Visibility.Visible);
                current.MessageMargin = !sameAsPrev ? new Thickness(0, 10, 0, 2) : new Thickness(0, 2, 0, 2);

                if (!isMe && !sameAsPrev && !string.IsNullOrEmpty(current.SenderName))
                {
                    current.SenderNameVisibility = Visibility.Visible;
                }
                else
                {
                    current.SenderNameVisibility = Visibility.Collapsed;
                }

                if (!sameAsPrev && !sameAsNext)
                    current.BubbleRadius = isMe ? new CornerRadius(15, 15, 0, 15) : new CornerRadius(15, 15, 15, 0);
                else if (!sameAsPrev && sameAsNext)
                    current.BubbleRadius = isMe ? new CornerRadius(15, 15, 4, 15) : new CornerRadius(15, 15, 15, 4);
                else if (sameAsPrev && sameAsNext)
                    current.BubbleRadius = isMe ? new CornerRadius(15, 4, 4, 15) : new CornerRadius(4, 15, 15, 4);
                else if (sameAsPrev && !sameAsNext)
                    current.BubbleRadius = isMe ? new CornerRadius(15, 4, 0, 15) : new CornerRadius(4, 15, 15, 0);
            }
        }

        private void lstGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstGroups.SelectedItem is ChatItem item)
            {
                lstFriends.SelectedItem = null; currentTarget = item; item.HasNewMessage = false; lblCurrentChat.Text = $"Chat nhóm: {item.Name}";

                if (FindName("expGroupCode") is Expander expGC) expGC.Visibility = Visibility.Visible;
                if (FindName("expGroupMembers") is Expander expGM) expGM.Visibility = Visibility.Visible;

                if (FindName("btnInfoUnfriend") is Button btnU) btnU.Visibility = Visibility.Collapsed;

                if (FindName("bdgE2EE") is Border bdgE) bdgE.Visibility = Visibility.Collapsed;
                if (FindName("pnlSelfDestructContainer") is Border pnlSelf) pnlSelf.Visibility = Visibility.Collapsed;

                SetChatInputState(false);

                Messages.Clear(); SendToServer($"GET_GROUP_HISTORY|{item.Id}"); SendToServer($"GET_GROUP_MEMBERS|{item.Id}");
                if (isInfoPanelVisible) pnlInfo.DataContext = item;
                UpdateInfoPanelMedia();
            }
        }

        private void btnAddFriend_Click(object sender, RoutedEventArgs e)
        {
            if (_addFriendWin != null)
            {
                if (_addFriendWin.WindowState == WindowState.Minimized)
                {
                    _addFriendWin.WindowState = WindowState.Normal;
                }
                _addFriendWin.Activate();
            }
            else
            {
                _addFriendWin = new AddFriendWindow(this);
                _addFriendWin.Closed += (s, args) => _addFriendWin = null;
                _addFriendWin.Show();
            }
        }

        private void btnAddMemberFriend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MemberItem mem)
            {
                SendToServer($"SEND_REQ|{mem.Id}");
                btn.Visibility = Visibility.Collapsed;
            }
        }

        private void btnCreateGroup_Click(object sender, RoutedEventArgs e) { CreateGroupWindow win = new CreateGroupWindow(this) { Owner = this }; win.ShowDialog(); }
        private void txtMessage_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) btnSend_Click(null, null); }
        private void btnLogout_Click(object sender, RoutedEventArgs e) { client.Close(); new LoginWindow().Show(); Close(); }

        private void btnDeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("CẢNH BÁO: Xóa vĩnh viễn tài khoản?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes) SendToServer("DELETE_ACCOUNT");
        }

        private void btnEmoji_Click(object sender, RoutedEventArgs e) { if (FindName("popEmoji") is Popup pop) pop.IsOpen = !pop.IsOpen; }
        private void Emoji_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null) { txtMessage.Text += btn.Tag.ToString(); txtMessage.CaretIndex = txtMessage.Text.Length; txtMessage.Focus(); }
        }

        private void btnSendImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentTarget == null) { MessageBox.Show("Vui lòng chọn người nhận trước!"); return; }
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog() { Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string base64 = ImageUtils.CompressImageToBase64(dlg.FileName, 800, 70);
                    if (base64.Length > 2000000) { MessageBox.Show("Ảnh quá nặng!"); return; }

                    string originalText = "[IMG]" + base64;
                    if (SelfDestructSeconds > 0)
                    {
                        originalText = $"[BURN:{SelfDestructSeconds}]" + originalText;
                    }
                    string payload = originalText;

                    if (currentTarget.Type == "F" && FriendPublicKeys.ContainsKey(currentTarget.Id))
                    {
                        string aesKey, aesIV;
                        string encryptedMsg = AesEncryptionHelper.Encrypt(originalText, out aesKey, out aesIV);
                        string encKeyThem = EncryptionHelper.EncryptRSA(aesKey, FriendPublicKeys[currentTarget.Id]);
                        string encKeyMe = EncryptionHelper.EncryptRSA(aesKey, EncryptionHelper.MyPublicKey);
                        payload = $"[E2EE]{encKeyThem}*{encKeyMe}*{aesIV}*{encryptedMsg}";
                    }

                    Messages.Add(new MessageModel { RawPayload = payload, Content = originalText, IsMe = true });
                    UpdateMessageGroupings(); if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                    SendToServer((currentTarget.Type == "F" ? "SEND_PRIVATE|" : "SEND_GROUP|") + currentTarget.Id + "|" + payload);
                    UpdateInfoPanelMedia();
                }
                catch { }
            }
        }

        private void ImageMessage_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Image img && img.Source != null)
            {
                vidFullscreen.Stop();
                gridFullscreenVideo.Visibility = Visibility.Collapsed;
                imgFullscreen.Source = img.Source;
                imgFullscreen.Visibility = Visibility.Visible;
                pnlImagePreview.Visibility = Visibility.Visible;
            }
        }

        private void btnClosePreview_Click(object sender, RoutedEventArgs e)
        {
            vidFullscreen.Stop(); vidFullscreen.Source = null;
            pnlImagePreview.Visibility = Visibility.Collapsed;
            gridFullscreenVideo.Visibility = Visibility.Collapsed;
            imgFullscreen.Visibility = Visibility.Collapsed;
        }

        private static string ReadLineManual(NetworkStream stream)
        {
            List<byte> bytes = new List<byte>(); int b;
            while ((b = stream.ReadByte()) != -1) { if (b == '\n') break; if (b != '\r') bytes.Add((byte)b); }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private async void btnSendFile_Click(object sender, RoutedEventArgs e)
        {
            if (currentTarget == null) { MessageBox.Show("Vui lòng chọn người nhận!"); return; }
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog() { Filter = "Tài liệu (*.pdf;*.docx;*.xlsx;*.txt)|*.pdf;*.docx;*.xlsx;*.txt|Tất cả (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
            {
                FileInfo fi = new FileInfo(dlg.FileName);
                if (fi.Length > 50 * 1024 * 1024) { MessageBox.Show("Giới hạn file 50MB."); return; }
                string serverIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

                try
                {
                    string uniqueName = await Task.Run(() => {
                        using (TcpClient fc = new TcpClient(serverIp, 8889))
                        using (NetworkStream stream = fc.GetStream())
                        {
                            byte[] header = Encoding.UTF8.GetBytes($"UPLOAD|{fi.Name}|{fi.Length}\n");
                            stream.Write(header, 0, header.Length);
                            byte[] buffer = new byte[81920];
                            using (FileStream fs = new FileStream(dlg.FileName, FileMode.Open, FileAccess.Read))
                            {
                                int read; while ((read = fs.Read(buffer, 0, buffer.Length)) > 0) stream.Write(buffer, 0, read);
                            }
                            string response = ReadLineManual(stream);
                            if (response.StartsWith("SUCCESS|")) return response.Split('|')[1];
                            return null;
                        }
                    });

                    if (uniqueName == null) return;

                    string originalText = $"[FILE]{uniqueName}*{fi.Name}";
                    if (SelfDestructSeconds > 0)
                    {
                        originalText = $"[BURN:{SelfDestructSeconds}]" + originalText;
                    }
                    string payload = originalText;

                    if (currentTarget.Type == "F" && FriendPublicKeys.ContainsKey(currentTarget.Id))
                    {
                        string aesKey, aesIV;
                        string encryptedMsg = AesEncryptionHelper.Encrypt(originalText, out aesKey, out aesIV);
                        string encKeyThem = EncryptionHelper.EncryptRSA(aesKey, FriendPublicKeys[currentTarget.Id]);
                        string encKeyMe = EncryptionHelper.EncryptRSA(aesKey, EncryptionHelper.MyPublicKey);
                        payload = $"[E2EE]{encKeyThem}*{encKeyMe}*{aesIV}*{encryptedMsg}";
                    }

                    Messages.Add(new MessageModel { RawPayload = payload, Content = originalText, IsMe = true });
                    UpdateMessageGroupings();
                    if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                    SendToServer((currentTarget.Type == "F" ? "SEND_PRIVATE|" : "SEND_GROUP|") + currentTarget.Id + "|" + payload);
                    UpdateInfoPanelMedia();
                }
                catch (Exception ex) { MessageBox.Show("Lỗi gửi file: " + ex.Message); }
            }
        }

        private async void btnSendVideo_Click(object sender, RoutedEventArgs e)
        {
            if (currentTarget == null) { MessageBox.Show("Vui lòng chọn người nhận!"); return; }
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog() { Filter = "Video Files (*.mp4;*.avi;*.mkv)|*.mp4;*.avi;*.mkv" };
            if (dlg.ShowDialog() == true)
            {
                FileInfo fi = new FileInfo(dlg.FileName);
                if (fi.Length > 50 * 1024 * 1024) { MessageBox.Show("Giới hạn video 50MB."); return; }
                string serverIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

                try
                {
                    string uniqueName = await Task.Run(() => {
                        using (TcpClient fc = new TcpClient(serverIp, 8889))
                        using (NetworkStream stream = fc.GetStream())
                        {
                            byte[] header = Encoding.UTF8.GetBytes($"UPLOAD|{fi.Name}|{fi.Length}\n");
                            stream.Write(header, 0, header.Length);
                            byte[] buffer = new byte[81920];
                            using (FileStream fs = new FileStream(dlg.FileName, FileMode.Open, FileAccess.Read))
                            {
                                int read; while ((read = fs.Read(buffer, 0, buffer.Length)) > 0) stream.Write(buffer, 0, read);
                            }
                            string response = ReadLineManual(stream);
                            if (response.StartsWith("SUCCESS|")) return response.Split('|')[1];
                            return null;
                        }
                    });

                    if (uniqueName == null) return;

                    string cacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatCache");
                    if (!Directory.Exists(cacheFolder)) Directory.CreateDirectory(cacheFolder);
                    File.Copy(dlg.FileName, Path.Combine(cacheFolder, uniqueName), true);

                    string originalText = $"[VID]{uniqueName}*{fi.Name}";
                    if (SelfDestructSeconds > 0)
                    {
                        originalText = $"[BURN:{SelfDestructSeconds}]" + originalText;
                    }
                    string payload = originalText;

                    if (currentTarget.Type == "F" && FriendPublicKeys.ContainsKey(currentTarget.Id))
                    {
                        string aesKey, aesIV;
                        string encryptedMsg = AesEncryptionHelper.Encrypt(originalText, out aesKey, out aesIV);
                        string encKeyThem = EncryptionHelper.EncryptRSA(aesKey, FriendPublicKeys[currentTarget.Id]);
                        string encKeyMe = EncryptionHelper.EncryptRSA(aesKey, EncryptionHelper.MyPublicKey);
                        payload = $"[E2EE]{encKeyThem}*{encKeyMe}*{aesIV}*{encryptedMsg}";
                    }

                    Messages.Add(new MessageModel { RawPayload = payload, Content = originalText, IsMe = true });
                    UpdateMessageGroupings();
                    if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                    SendToServer((currentTarget.Type == "F" ? "SEND_PRIVATE|" : "SEND_GROUP|") + currentTarget.Id + "|" + payload);
                    UpdateInfoPanelMedia();
                }
                catch (Exception ex) { MessageBox.Show("Lỗi gửi video: " + ex.Message); }
            }
        }

        private async void FileMessage_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is MessageModel msg)
            {
                if (string.IsNullOrEmpty(msg.AttachedFileBase64))
                {
                    MessageBox.Show("Tài liệu đã cũ hoặc bị lỗi dữ liệu, không thể tải về!");
                    return;
                }

                Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog() { FileName = msg.FileName };
                if (sfd.ShowDialog() == true)
                {
                    string serverIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    try
                    {
                        await Task.Run(() => {
                            using (TcpClient fc = new TcpClient(serverIp, 8889))
                            using (NetworkStream stream = fc.GetStream())
                            {
                                byte[] req = Encoding.UTF8.GetBytes($"DOWNLOAD|{msg.AttachedFileBase64}\n");
                                stream.Write(req, 0, req.Length);
                                string res = ReadLineManual(stream);
                                if (res.StartsWith("FILE_SIZE|"))
                                {
                                    long fileSize = long.Parse(res.Split('|')[1]);
                                    byte[] buffer = new byte[81920]; long totalRead = 0;
                                    using (FileStream fs = new FileStream(sfd.FileName, FileMode.Create, FileAccess.Write))
                                    {
                                        int read; while (totalRead < fileSize && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, fileSize - totalRead))) > 0)
                                        { fs.Write(buffer, 0, read); totalRead += read; }
                                    }
                                    Dispatcher.Invoke(() => MessageBox.Show("Tải tệp tin thành công!", "Hoàn tất"));
                                }
                            }
                        });
                    }
                    catch (Exception ex) { MessageBox.Show("Lỗi tải: " + ex.Message); }
                }
            }
        }

        private void btnInvite_Click(object sender, MouseButtonEventArgs e) { if (FindName("txtInfoInviteCode") is TextBlock txtCode) MessageBox.Show($"Mã mời: {txtCode.Text}"); }

        private void MemberAction_Kick(object sender, RoutedEventArgs e) { if (currentTarget != null && MessageBox.Show("Xóa người này khỏi nhóm?", "Xác nhận", MessageBoxButton.YesNo) == MessageBoxResult.Yes) SendToServer($"KICK_MEMBER|{currentTarget.Id}|{((MemberItem)((Button)sender).Tag).Id}"); }

        private void btnDeleteGroup_Click(object sender, RoutedEventArgs e) { if (currentTarget != null && MessageBox.Show("Xóa giải tán nhóm này?", "Xác nhận", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { SendToServer($"DELETE_GROUP|{currentTarget.Id}"); colInfoPanel.Width = new GridLength(0); isInfoPanelVisible = false; currentTarget = null; Messages.Clear(); } }
    }

    public static class EncryptionHelper
    {
        public static string MyPublicKey = "";

        private static string _myPrivateKey = "";
        public static string MyPrivateKey
        {
            get => _myPrivateKey;
            set
            {
                _myPrivateKey = value;
                if (_cachedRsa != null)
                {
                    _cachedRsa.Dispose();
                    _cachedRsa = null;
                }
                if (!string.IsNullOrEmpty(value))
                {
                    _cachedRsa = new RSACryptoServiceProvider();
                    _cachedRsa.FromXmlString(value);
                }
            }
        }

        private static RSACryptoServiceProvider _cachedRsa;

        public static void GenerateRSAKeys()
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048))
            {
                MyPublicKey = rsa.ToXmlString(false);
                MyPrivateKey = rsa.ToXmlString(true);
            }
        }

        public static string EncryptRSA(string plainText, string publicKeyXml)
        {
            try
            {
                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(publicKeyXml);
                    byte[] data = Encoding.UTF8.GetBytes(plainText);
                    byte[] encryptedData = rsa.Encrypt(data, false);
                    return Convert.ToBase64String(encryptedData);
                }
            }
            catch { return ""; }
        }

        public static string DecryptRSA(string cipherTextBase64, string privateKeyXml)
        {
            try
            {
                byte[] data = Convert.FromBase64String(cipherTextBase64);

                if (privateKeyXml == MyPrivateKey && _cachedRsa != null)
                {
                    byte[] decryptedData = _cachedRsa.Decrypt(data, false);
                    return Encoding.UTF8.GetString(decryptedData);
                }

                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(privateKeyXml);
                    byte[] decryptedData = rsa.Decrypt(data, false);
                    return Encoding.UTF8.GetString(decryptedData);
                }
            }
            catch { return ""; }
        }
    }

    public static class AesEncryptionHelper
    {
        public static string Encrypt(string plainText, out string keyBase64, out string ivBase64)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.KeySize = 256;
                aesAlg.GenerateKey();
                aesAlg.GenerateIV();
                keyBase64 = Convert.ToBase64String(aesAlg.Key);
                ivBase64 = Convert.ToBase64String(aesAlg.IV);

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                    {
                        swEncrypt.Write(plainText);
                    }
                    return Convert.ToBase64String(msEncrypt.ToArray());
                }
            }
        }

        public static string Decrypt(string cipherTextBase64, string keyBase64, string ivBase64)
        {
            byte[] cipherText = Convert.FromBase64String(cipherTextBase64);
            byte[] key = Convert.FromBase64String(keyBase64);
            byte[] iv = Convert.FromBase64String(ivBase64);

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key;
                aesAlg.IV = iv;
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                {
                    return srDecrypt.ReadToEnd();
                }
            }
        }

        public static string EncryptPrivateKey(string privateKeyXml, string pin)
        {
            using (Aes aesAlg = Aes.Create())
            {
                using (SHA256 sha256 = SHA256.Create())
                {
                    aesAlg.Key = sha256.ComputeHash(Encoding.UTF8.GetBytes(pin));
                }
                aesAlg.GenerateIV();
                string ivBase64 = Convert.ToBase64String(aesAlg.IV);

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                    {
                        swEncrypt.Write(privateKeyXml);
                    }
                    return ivBase64 + "." + Convert.ToBase64String(msEncrypt.ToArray());
                }
            }
        }

        public static string DecryptPrivateKey(string encryptedData, string pin)
        {
            try
            {
                string[] parts = encryptedData.Split('.');
                if (parts.Length != 2) return "";
                byte[] iv = Convert.FromBase64String(parts[0]);
                byte[] cipherText = Convert.FromBase64String(parts[1]);

                using (Aes aesAlg = Aes.Create())
                {
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        aesAlg.Key = sha256.ComputeHash(Encoding.UTF8.GetBytes(pin));
                    }
                    aesAlg.IV = iv;
                    ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
                    using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
            catch { return ""; }
        }
    }

    public static class ImageUtils
    {
        public static string ImageToBase64(string filePath) { try { return Convert.ToBase64String(File.ReadAllBytes(filePath)); } catch { return ""; } }
        public static ImageSource Base64ToImage(string base64String)
        {
            if (string.IsNullOrEmpty(base64String)) return LoadDefaultImage();
            try { var i = new System.Windows.Media.Imaging.BitmapImage(); using (var m = new MemoryStream(Convert.FromBase64String(base64String))) { i.BeginInit(); i.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; i.StreamSource = m; i.EndInit(); } i.Freeze(); return i; } catch { return LoadDefaultImage(); }
        }
        public static string CompressImageToBase64(string filePath, int maxWidth = 800, int quality = 75)
        {
            try { var b = new System.Windows.Media.Imaging.BitmapImage(); b.BeginInit(); b.UriSource = new Uri(filePath); b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; b.DecodePixelWidth = maxWidth; b.EndInit(); b.Freeze(); var e = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = quality }; e.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(b)); using (var ms = new MemoryStream()) { e.Save(ms); return Convert.ToBase64String(ms.ToArray()); } } catch { return ImageToBase64(filePath); }
        }
        public static ImageSource GetImageFromCacheOrBase64(string base64String)
        {
            if (string.IsNullOrEmpty(base64String)) return LoadDefaultImage();
            try { string c = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatCache"); if (!Directory.Exists(c)) Directory.CreateDirectory(c); string n = ""; using (MD5 md5 = MD5.Create()) { n = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(base64String))).Replace("-", "").ToLower() + ".jpg"; } string f = Path.Combine(c, n); if (File.Exists(f)) { var b = new System.Windows.Media.Imaging.BitmapImage(); b.BeginInit(); b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; b.UriSource = new Uri(f); b.EndInit(); b.Freeze(); return b; } else { byte[] ib = Convert.FromBase64String(base64String); File.WriteAllBytes(f, ib); var b = new System.Windows.Media.Imaging.BitmapImage(); using (var m = new MemoryStream(ib)) { b.BeginInit(); b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; b.StreamSource = m; b.EndInit(); } b.Freeze(); return b; } } catch { return Base64ToImage(base64String); }
        }
        private static ImageSource LoadDefaultImage() { try { string p = Path.GetFullPath("default_user.png"); if (File.Exists(p)) { var i = new System.Windows.Media.Imaging.BitmapImage(); i.BeginInit(); i.UriSource = new Uri(p); i.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; i.EndInit(); i.Freeze(); return i; } } catch { } return null; }
    }
    public static class ThemeHelper
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void ApplyTitleBarTheme(Window window, bool isDark)
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                int dark = isDark ? 1 : 0;
                int result = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                if (result != 0) DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
            }
            catch { }
        }
    }

    // --- BỘ QUẢN LÝ GHI NHỚ LỊCH SỬ CHÁY TIN NHẮN (CACHE) ---
    public static class BurnCacheManager
    {
        private static string CachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatCache", "burn_states.txt");
        private static Dictionary<string, DateTime> _burningDict = new Dictionary<string, DateTime>();

        public static void Load()
        {
            try
            {
                if (File.Exists(CachePath))
                {
                    var lines = File.ReadAllLines(CachePath);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new string[] { "|||" }, StringSplitOptions.None);
                        if (parts.Length == 2 && DateTime.TryParse(parts[1], out DateTime endTime))
                        {
                            _burningDict[parts[0]] = endTime;
                        }
                    }
                }
            }
            catch { }
        }

        public static void StartBurn(string rawPayload, int seconds)
        {
            if (string.IsNullOrEmpty(rawPayload)) return;
            if (!_burningDict.ContainsKey(rawPayload))
            {
                _burningDict[rawPayload] = DateTime.Now.AddSeconds(seconds);
                Save();
            }
        }

        public static void RemoveBurn(string rawPayload)
        {
            if (string.IsNullOrEmpty(rawPayload)) return;
            if (_burningDict.ContainsKey(rawPayload))
            {
                _burningDict.Remove(rawPayload);
                Save();
            }
        }

        public static bool IsBurning(string rawPayload)
        {
            return !string.IsNullOrEmpty(rawPayload) && _burningDict.ContainsKey(rawPayload);
        }

        public static int GetRemainingSeconds(string rawPayload)
        {
            if (!string.IsNullOrEmpty(rawPayload) && _burningDict.TryGetValue(rawPayload, out DateTime endTime))
            {
                int rem = (int)(endTime - DateTime.Now).TotalSeconds;
                return rem > 0 ? rem : 0;
            }
            return 0;
        }

        private static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(CachePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var lines = _burningDict.Select(kv => $"{kv.Key}|||{kv.Value.ToString("O")}").ToArray();
                File.WriteAllLines(CachePath, lines);
            }
            catch { }
        }
    }
}