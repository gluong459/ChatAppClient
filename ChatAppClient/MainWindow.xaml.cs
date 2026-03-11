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

namespace ChatAppClient
{
    public class MemberItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ImageSource Avatar { get; set; }
        public Visibility KickVisibility { get; set; }
        public Visibility AddFriendVisibility { get; set; } // THÊM BIẾN NÀY ĐỂ ẨN HIỆN NÚT KẾT BẠN
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
        public bool IsMe { get => _isMe; set { if (_isMe == value) return; _isMe = value; OnPropertyChanged("IsMe"); OnPropertyChanged("Alignment"); OnPropertyChanged("TextColor"); OnPropertyChanged("BackgroundColor"); } }
        public HorizontalAlignment Alignment => IsMe ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public string TextColor => IsMe ? "White" : "Black";
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
        private string _backgroundColor;
        public string BackgroundColor { get => _backgroundColor ?? (IsMe ? "#0084FF" : "#E4E6EB"); set { if (_backgroundColor != value) { _backgroundColor = value; OnPropertyChanged("BackgroundColor"); } } }
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
        private ChatItem currentTarget = null;
        private AddFriendWindow _addFriendWin;
        private List<ChatItem> _allFriends = new List<ChatItem>();
        private List<ChatItem> _allGroups = new List<ChatItem>();

        private bool isInfoPanelVisible = false;
        private bool isMuted = false;

        public string UserEmail = "";

        DispatcherTimer _videoTimer;
        MediaElement _activeVideoPlayer;
        Slider _activeVideoSlider;
        bool _isDraggingSlider = false;
        bool _isDraggingFS = false;

        public MainWindow(string userName, string displayName, TcpClient existingClient, string userAvatar = "")
        {
            InitializeComponent();
            this.DataContext = this;
            this.UserName = userName;
            this.DisplayName = displayName;
            this.client = existingClient;
            lblWelcome.Text = DisplayName;

            var myImg = ImageUtils.Base64ToImage(userAvatar);
            if (myImg != null) imgMyAvatar.Fill = new ImageBrush(myImg) { Stretch = Stretch.UniformToFill };
            imgMyAvatar.MouseLeftButtonDown += ImgMyAvatar_MouseLeftButtonDown;

            Friends = new ObservableCollection<ChatItem>(); Groups = new ObservableCollection<ChatItem>(); Messages = new ObservableCollection<MessageModel>();
            lstFriends.ItemsSource = Friends; lstGroups.ItemsSource = Groups; listMessages.ItemsSource = Messages;

            var stream = client.GetStream();
            reader = new StreamReader(stream); writer = new StreamWriter(stream) { AutoFlush = true };
            SendToServer("GET_LIST");
            SendToServer("GET_REQ_LIST");
            SendToServer("GET_MY_EMAIL");

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

        // ================= XỬ LÝ LƯU ẢNH VÀ VIDEO =================
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


        // ================= XỬ LÝ SỰ KIỆN TRÌNH PHÁT VIDEO NHỎ =================
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

        // ================= XỬ LÝ SỰ KIỆN FULLSCREEN VIDEO =================
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

        // ================= CÁC CHỨC NĂNG CÒN LẠI =================

        // Cập nhật hàm lấy dữ liệu Phương tiện
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

        // Mở Video trong tab Phương tiện
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

        // Copy mã mời
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
            if (FindName("txtMuteText") is TextBlock txtText) { txtText.Text = isMuted ? "Đã tắt" : "Thông báo"; txtText.Foreground = isMuted ? Brushes.Red : Brushes.Black; }

            // Cập nhật nút ở Info Panel
            if (FindName("txtMuteIcon2") is TextBlock txtIcon2) txtIcon2.Text = isMuted ? "🔕" : "🔔";
            if (FindName("txtMuteText2") is TextBlock txtText2) { txtText2.Text = isMuted ? "Đã tắt" : "Tắt thông báo"; txtText2.Foreground = isMuted ? (SolidColorBrush)new BrushConverter().ConvertFrom("#E53935") : (SolidColorBrush)new BrushConverter().ConvertFrom("#050505"); }
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
                SendToServer($"UNFRIEND|{currentTarget.Id}"); Friends.Remove(currentTarget); _allFriends.Remove(currentTarget);
                colInfoPanel.Width = new GridLength(0); isInfoPanelVisible = false; currentTarget = null; Messages.Clear(); lblCurrentChat.Text = "Chọn một cuộc hội thoại...";
            }
        }

        // Rời nhóm
        private void btnLeaveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (currentTarget == null || currentTarget.Type != "G") return;
            if (MessageBox.Show($"Bạn có chắc chắn muốn rời khỏi nhóm {currentTarget.Name}?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                // Gửi lệnh KICK chính mình (vì server bạn cũ chỉ hỗ trợ KICK)
                // Hoặc giả lập gửi lệnh LEAVE_GROUP nếu server có hỗ trợ. Ở đây dùng LEAVE_GROUP chuẩn
                SendToServer($"LEAVE_GROUP|{currentTarget.Id}");

                // Xóa khỏi giao diện
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
                                Messages.Add(new MessageModel { Content = parts[3], IsMe = false, AvatarSource = currentTarget.AvatarSource });
                                UpdateMessageGroupings();
                                if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                                UpdateInfoPanelMedia();
                            }
                            else foreach (var f in Friends) if (f.Id == int.Parse(parts[1])) f.HasNewMessage = true;
                        }
                        else if (parts[0] == "HISTORY")
                        {
                            listMessages.ItemsSource = null; Messages.Clear();
                            for (int i = 1; i < parts.Length; i++)
                            {
                                string[] m = parts[i].Split(new[] { ':' }, 2);
                                if (m.Length == 2) Messages.Add(new MessageModel { Content = m[1], IsMe = (int.Parse(m[0]) != currentTarget.Id), AvatarSource = currentTarget.AvatarSource });
                            }
                            UpdateMessageGroupings(); listMessages.ItemsSource = Messages;
                            if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                            UpdateInfoPanelMedia();
                        }
                        else if (parts[0] == "MSG_GROUP")
                        {
                            if (!isMuted) { if (_soundPlayer != null) _soundPlayer.Play(); else System.Media.SystemSounds.Beep.Play(); }
                            int gId = int.Parse(parts[1]); string senderName = parts[2]; string content = parts[3];
                            if (currentTarget != null && currentTarget.Id == gId && currentTarget.Type == "G")
                            {
                                Messages.Add(new MessageModel { Content = $"{senderName}:\n{content}", IsMe = false, AvatarSource = ImageUtils.Base64ToImage("") });
                                UpdateMessageGroupings();
                                if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
                                UpdateInfoPanelMedia();
                            }
                            else foreach (var g in Groups) if (g.Id == gId) g.HasNewMessage = true;
                        }
                        else if (parts[0] == "GROUP_HISTORY")
                        {
                            listMessages.ItemsSource = null; Messages.Clear();
                            int myId = int.Parse(this.UserName);
                            for (int i = 1; i < parts.Length; i++)
                            {
                                string[] m = parts[i].Split(new[] { ':' }, 3);
                                if (m.Length == 3)
                                {
                                    bool isMe = (int.Parse(m[0]) == myId);
                                    Messages.Add(new MessageModel { Content = isMe ? m[2] : $"{m[1]}:\n{m[2]}", IsMe = isMe, AvatarSource = ImageUtils.Base64ToImage("") });
                                }
                            }
                            UpdateMessageGroupings(); listMessages.ItemsSource = Messages;
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

                            // Cập nhật hiển thị nút Rời nhóm / Xóa nhóm
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
                        else if (parts[0] == "MSG_SYS") MessageBox.Show(parts.Length > 1 ? parts[1] : "Thông báo hệ thống", "Thông báo");
                        else if (parts[0] == "NEW_REQ") SendToServer("GET_REQ_LIST");
                        else if (parts[0] == "MY_EMAIL") UserEmail = parts.Length > 1 ? parts[1] : "Chưa cập nhật Email";
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
            Messages.Add(new MessageModel { Content = txtMessage.Text, IsMe = true });
            UpdateMessageGroupings();
            if (listMessages.Items.Count > 0) listMessages.ScrollIntoView(listMessages.Items[listMessages.Items.Count - 1]);
            if (currentTarget.Type == "F") SendToServer($"SEND_PRIVATE|{currentTarget.Id}|{txtMessage.Text}");
            else if (currentTarget.Type == "G") SendToServer($"SEND_GROUP|{currentTarget.Id}|{txtMessage.Text}");
            txtMessage.Clear();
        }

        private void lstFriends_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstFriends.SelectedItem is ChatItem item)
            {
                lstGroups.SelectedItem = null; currentTarget = item; item.HasNewMessage = false; lblCurrentChat.Text = $"Chat với: {item.Name}";

                // Ẩn UI của Group
                if (FindName("expGroupCode") is Expander expGC) expGC.Visibility = Visibility.Collapsed;
                if (FindName("expGroupMembers") is Expander expGM) expGM.Visibility = Visibility.Collapsed;
                if (FindName("btnInfoLeaveGroup") is Button btnL) btnL.Visibility = Visibility.Collapsed;
                if (FindName("btnInfoDeleteGroup") is Button btnD) btnD.Visibility = Visibility.Collapsed;

                // Hiện UI của Friend
                if (FindName("btnInfoUnfriend") is Button btnU) btnU.Visibility = Visibility.Visible;

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

                // Hiện UI của Group
                if (FindName("expGroupCode") is Expander expGC) expGC.Visibility = Visibility.Visible;
                if (FindName("expGroupMembers") is Expander expGM) expGM.Visibility = Visibility.Visible;

                // Ẩn UI của Friend
                if (FindName("btnInfoUnfriend") is Button btnU) btnU.Visibility = Visibility.Collapsed;

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
                MessageBox.Show($"Đã gửi yêu cầu kết bạn tới {mem.Name}!");
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
                    string payload = "[IMG]" + base64;
                    Messages.Add(new MessageModel { Content = payload, IsMe = true });
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

                    string payload = $"[FILE]{uniqueName}*{fi.Name}";
                    Messages.Add(new MessageModel { Content = payload, IsMe = true }); UpdateMessageGroupings();
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

                    string payload = $"[VID]{uniqueName}*{fi.Name}";
                    Messages.Add(new MessageModel { Content = payload, IsMe = true }); UpdateMessageGroupings();
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
                                    Dispatcher.Invoke(() => MessageBox.Show("Tải thành công!"));
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
}