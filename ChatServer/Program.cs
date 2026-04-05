using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Net.Mail;
using System.Linq;

namespace ChatServer
{
    class ClientInfo
    {
        public TcpClient Socket;
        public StreamWriter Writer;
        public int UserId;
        public string Username;
    }

    class Program
    {
        static string connectionString = "Data Source=localhost;Initial Catalog=ChatAppDB;Integrated Security=True;TrustServerCertificate=True";

        static Dictionary<int, ClientInfo> onlineClients = new Dictionary<int, ClientInfo>();
        static object lockObj = new object();

        static string uploadDir = "UploadedFiles";

        static string smtpEmail = "gluong27@gmail.com";
        static string smtpPass = "etceicalfrnchnnq";
        static Dictionary<string, string> resetCodes = new Dictionary<string, string>();

        static void Main(string[] args)
        {
            if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

            InitializeDatabase();

            Thread udpThread = new Thread(UdpDiscoveryServer);
            udpThread.IsBackground = true;
            udpThread.Start();

            Thread fileServerThread = new Thread(FileServerListener);
            fileServerThread.IsBackground = true;
            fileServerThread.Start();

            TcpListener server = new TcpListener(IPAddress.Any, 8888);
            try
            {
                server.Start();
                Console.WriteLine("=================================");
                Console.WriteLine("=== SERVER CHAT IS RUNNING ===");
                Console.WriteLine(">> Port 8888: Lang nghe Tin Nhan Text");
                Console.WriteLine(">> Port 8889: Lang nghe Truyen File TCP");
                Console.WriteLine("=================================");
            }
            catch (Exception ex) { Console.WriteLine("Loi khoi dong server: " + ex.Message); return; }

            while (true)
            {
                try
                {
                    TcpClient client = server.AcceptTcpClient();
                    Thread t = new Thread(HandleClient);
                    t.Start(client);
                }
                catch { }
            }
        }

        static void InitializeDatabase()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sqlNotif = @"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Notifications')
                        BEGIN
                            CREATE TABLE Notifications (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                UserId INT NOT NULL,
                                Message NVARCHAR(500) NOT NULL,
                                CreatedAt DATETIME DEFAULT GETDATE()
                            )
                        END";
                    using (SqlCommand cmd = new SqlCommand(sqlNotif, conn)) cmd.ExecuteNonQuery();

                    string sqlPubKey = @"
                        IF COL_LENGTH('Users', 'PublicKey') IS NULL
                        BEGIN
                            ALTER TABLE Users ADD PublicKey NVARCHAR(MAX) NULL
                        END";
                    using (SqlCommand cmd = new SqlCommand(sqlPubKey, conn)) cmd.ExecuteNonQuery();

                    string sqlEncKey = @"
                        IF COL_LENGTH('Users', 'EncryptedPrivateKey') IS NULL
                        BEGIN
                            ALTER TABLE Users ADD EncryptedPrivateKey NVARCHAR(MAX) NULL
                        END";
                    using (SqlCommand cmd = new SqlCommand(sqlEncKey, conn)) cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { Console.WriteLine("Lỗi khởi tạo Database: " + ex.Message); }
        }

        static void SaveNotification(int userId, string message)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = "INSERT INTO Notifications (UserId, Message) VALUES (@uid, @msg)";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@uid", userId);
                        cmd.Parameters.AddWithValue("@msg", message);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { }
        }

        static void HandleGetNotifications(ClientInfo client)
        {
            try
            {
                StringBuilder sb = new StringBuilder("NOTIF_LIST");
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = "SELECT TOP 30 Message, CreatedAt FROM Notifications WHERE UserId=@uid ORDER BY CreatedAt DESC";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@uid", client.UserId);
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string msg = r.GetString(0);
                            string time = r.GetDateTime(1).ToString("HH:mm - dd/MM/yyyy");
                            sb.Append($"|{msg}|{time}");
                        }
                    }
                }
                client.Writer.WriteLine(sb.ToString());
            }
            catch { }
        }

        #region TCP FILE SERVER (PORT 8889)
        static void FileServerListener()
        {
            TcpListener fileListener = new TcpListener(IPAddress.Any, 8889);
            try
            {
                fileListener.Start();
                while (true)
                {
                    TcpClient fileClient = fileListener.AcceptTcpClient();
                    Thread t = new Thread(HandleFileClient);
                    t.Start(fileClient);
                }
            }
            catch (Exception ex) { Console.WriteLine("Lỗi File Server: " + ex.Message); }
        }

        static void HandleFileClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            try
            {
                string header = ReadLineManual(stream);
                if (string.IsNullOrEmpty(header)) return;

                string[] parts = header.Split('|');

                if (parts[0] == "UPLOAD")
                {
                    string originalName = parts[1];
                    long fileSize = long.Parse(parts[2]);

                    string ext = Path.GetExtension(originalName);
                    string uniqueName = Guid.NewGuid().ToString("N") + ext;
                    string savePath = Path.Combine(uploadDir, uniqueName);

                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                    {
                        int read;
                        while (totalRead < fileSize && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, fileSize - totalRead))) > 0)
                        {
                            fs.Write(buffer, 0, read);
                            totalRead += read;
                        }
                    }

                    byte[] response = Encoding.UTF8.GetBytes($"SUCCESS|{uniqueName}\n");
                    stream.Write(response, 0, response.Length);
                    Console.WriteLine($">> [FILE SERVER] Nhan thanh cong file: {originalName} -> {uniqueName}");
                }
                else if (parts[0] == "DOWNLOAD")
                {
                    string uniqueName = parts[1];
                    string filePath = Path.Combine(uploadDir, uniqueName);

                    if (File.Exists(filePath))
                    {
                        FileInfo fi = new FileInfo(filePath);
                        byte[] response = Encoding.UTF8.GetBytes($"FILE_SIZE|{fi.Length}\n");
                        stream.Write(response, 0, response.Length);

                        byte[] buffer = new byte[81920];
                        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                        {
                            int read;
                            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                stream.Write(buffer, 0, read);
                            }
                        }
                        Console.WriteLine($">> [FILE SERVER] Gui thanh cong file: {uniqueName}");
                    }
                    else
                    {
                        byte[] response = Encoding.UTF8.GetBytes("NOT_FOUND|\n");
                        stream.Write(response, 0, response.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(">> [FILE SERVER] Lỗi kết nối: " + ex.Message);
            }
            finally
            {
                client.Close();
            }
        }

        static string ReadLineManual(NetworkStream stream)
        {
            List<byte> bytes = new List<byte>();
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                if (b == '\n') break;
                if (b != '\r') bytes.Add((byte)b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        #endregion

        #region CHAT SERVER (PORT 8888)
        static bool SendEmail(string toEmail, string code)
        {
            try
            {
                MailMessage mail = new MailMessage();
                mail.From = new MailAddress(smtpEmail, "ChatApp Security");
                mail.To.Add(toEmail);
                mail.Subject = "Mã xác nhận khôi phục mật khẩu ChatApp";
                mail.Body = $"Xin chào,\n\nMã xác nhận khôi phục mật khẩu của bạn là: {code}\n\nVui lòng không chia sẻ mã này cho bất kỳ ai.";

                SmtpClient smtp = new SmtpClient("smtp.gmail.com");
                smtp.Port = 587;
                smtp.EnableSsl = true;
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new NetworkCredential(smtpEmail, smtpPass);

                smtp.Send(mail);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi gửi email: " + ex.Message);
                return false;
            }
        }

        static void HandleForgotPassword(string[] parts, StreamWriter writer)
        {
            string email = parts[1].Trim();
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                SqlCommand cmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Email=@e", conn);
                cmd.Parameters.AddWithValue("@e", email);
                if ((int)cmd.ExecuteScalar() == 0)
                {
                    writer.WriteLine("FORGOT_FAIL|Email không tồn tại trong hệ thống!");
                    return;
                }
            }

            string code = new Random().Next(100000, 999999).ToString();
            resetCodes[email] = code;

            if (SendEmail(email, code)) writer.WriteLine("FORGOT_OK|Mã xác nhận đã được gửi đến email của bạn.");
            else writer.WriteLine("FORGOT_FAIL|Lỗi hệ thống khi gửi email.");
        }

        static void HandleResetPassword(string[] parts, StreamWriter writer)
        {
            string email = parts[1].Trim();
            string code = parts[2].Trim();
            string newPass = parts[3];

            if (resetCodes.ContainsKey(email) && resetCodes[email] == code)
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SqlCommand cmd = new SqlCommand("UPDATE Users SET Password=@p WHERE Email=@e", conn);
                    cmd.Parameters.AddWithValue("@p", newPass);
                    cmd.Parameters.AddWithValue("@e", email);
                    cmd.ExecuteNonQuery();
                }
                resetCodes.Remove(email);
                writer.WriteLine("RESET_OK|Đổi mật khẩu thành công!");
            }
            else
            {
                writer.WriteLine("RESET_FAIL|Mã xác nhận không chính xác!");
            }
        }

        static void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            StreamReader reader = new StreamReader(client.GetStream());
            StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
            ClientInfo currentClient = null;

            try
            {
                while (true)
                {
                    string request = reader.ReadLine();
                    if (request == null) break;
                    string[] parts = request.Split('|');
                    string command = parts[0];

                    Console.WriteLine($">> Nhan lenh tu Client: {command}");

                    if (command == "REGISTER") { HandleRegister(parts, writer); break; }
                    else if (command == "FORGOT_PASS") { HandleForgotPassword(parts, writer); break; }
                    else if (command == "RESET_PASS") { HandleResetPassword(parts, writer); break; }
                    else if (command == "LOGIN")
                    {
                        currentClient = HandleLogin(parts, client, writer);
                        if (currentClient == null) break;
                    }
                    else if (currentClient != null)
                    {
                        if (command == "GET_LIST") HandleGetList(currentClient);
                        else if (command == "SEARCH_USER") HandleSearchUser(parts, currentClient);
                        else if (command == "SEND_REQ") HandleSendRequest(parts, currentClient);
                        else if (command == "GET_REQ_LIST") HandleGetRequestList(currentClient);
                        else if (command == "RESP_REQ") HandleResponseRequest(parts, currentClient);
                        else if (command == "CREATE_GROUP") HandleCreateGroup(parts, currentClient);
                        else if (command == "JOIN_GROUP") HandleJoinGroup(parts, currentClient);
                        else if (command == "SEND_PRIVATE") HandleSendPrivate(parts, currentClient);
                        else if (command == "SEND_GROUP") HandleSendGroup(parts, currentClient);
                        else if (command == "GET_GROUP_HISTORY") HandleGetGroupHistory(parts, currentClient);
                        else if (command == "UPDATE_AVATAR") HandleUpdateAvatar(parts, currentClient);
                        else if (command == "UPDATE_PROFILE") HandleUpdateProfile(parts, currentClient);
                        else if (command == "UNFRIEND") HandleUnfriend(parts, currentClient);
                        else if (command == "LEAVE_GROUP") HandleLeaveGroup(parts, currentClient);
                        else if (command == "GET_GROUP_MEMBERS") HandleGetGroupMembers(parts, currentClient);
                        else if (command == "KICK_MEMBER") HandleKickMember(parts, currentClient);
                        else if (command == "DELETE_GROUP") HandleDeleteGroup(parts, currentClient);
                        else if (command == "GET_HISTORY") HandleGetHistory(parts, currentClient);
                        else if (command == "GET_NOTIFS") HandleGetNotifications(currentClient);
                        else if (command == "GET_MY_EMAIL")
                        {
                            using (SqlConnection conn = new SqlConnection(connectionString))
                            {
                                conn.Open();
                                SqlCommand cmd = new SqlCommand("SELECT Email FROM Users WHERE Id=@id", conn);
                                cmd.Parameters.AddWithValue("@id", currentClient.UserId);
                                string email = cmd.ExecuteScalar()?.ToString() ?? "Chưa cập nhật Email";
                                currentClient.Writer.WriteLine($"MY_EMAIL|{email}");
                            }
                        }
                        else if (command == "UPDATE_PUBLICKEY")
                        {
                            using (SqlConnection conn = new SqlConnection(connectionString))
                            {
                                conn.Open();
                                // 1. Cập nhật khóa mới vào CSDL
                                SqlCommand cmd = new SqlCommand("UPDATE Users SET PublicKey=@pk WHERE Id=@id", conn);
                                cmd.Parameters.AddWithValue("@pk", parts[1]);
                                cmd.Parameters.AddWithValue("@id", currentClient.UserId);
                                cmd.ExecuteNonQuery();

                                // 2. Báo cho các bạn bè đang online biết để họ tải lại khóa
                                SqlCommand getFriends = new SqlCommand("SELECT FriendId FROM Friends WHERE UserId=@uid", conn);
                                getFriends.Parameters.AddWithValue("@uid", currentClient.UserId);
                                using (SqlDataReader r = getFriends.ExecuteReader())
                                {
                                    while (r.Read())
                                    {
                                        int fid = r.GetInt32(0);
                                        lock (lockObj)
                                        {
                                            if (onlineClients.ContainsKey(fid))
                                            {
                                                // Gửi lệnh cảnh báo đổi khóa để Client bên kia xóa khóa cũ
                                                onlineClients[fid].Writer.WriteLine($"KEY_CHANGED|{currentClient.UserId}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else if (command == "GET_PUBLICKEY")
                        {
                            int targetId = int.Parse(parts[1]);
                            using (SqlConnection conn = new SqlConnection(connectionString))
                            {
                                conn.Open();
                                SqlCommand cmd = new SqlCommand("SELECT PublicKey FROM Users WHERE Id=@id", conn);
                                cmd.Parameters.AddWithValue("@id", targetId);
                                string pk = cmd.ExecuteScalar()?.ToString() ?? "";
                                currentClient.Writer.WriteLine($"PUBLICKEY_RES|{targetId}|{pk}");
                            }
                        }

                        else if (command == "GET_BACKUP_STATUS")
                        {
                            using (SqlConnection conn = new SqlConnection(connectionString))
                            {
                                conn.Open();
                                SqlCommand cmd = new SqlCommand("SELECT EncryptedPrivateKey FROM Users WHERE Id=@id", conn);
                                cmd.Parameters.AddWithValue("@id", currentClient.UserId);
                                string ek = cmd.ExecuteScalar()?.ToString() ?? "";
                                currentClient.Writer.WriteLine($"BACKUP_STATUS|{ek}");
                            }
                        }
                        else if (command == "BACKUP_KEY")
                        {
                            using (SqlConnection conn = new SqlConnection(connectionString))
                            {
                                conn.Open();
                                SqlCommand cmd = new SqlCommand("UPDATE Users SET EncryptedPrivateKey=@ek WHERE Id=@id", conn);
                                cmd.Parameters.AddWithValue("@ek", parts[1]);
                                cmd.Parameters.AddWithValue("@id", currentClient.UserId);
                                cmd.ExecuteNonQuery();
                            }
                            currentClient.Writer.WriteLine("BACKUP_KEY_OK");
                        }
                        else if (command == "RECOVER_KEY")
                        {
                            using (SqlConnection conn = new SqlConnection(connectionString))
                            {
                                conn.Open();
                                SqlCommand cmd = new SqlCommand("SELECT EncryptedPrivateKey FROM Users WHERE Id=@id", conn);
                                cmd.Parameters.AddWithValue("@id", currentClient.UserId);
                                string ek = cmd.ExecuteScalar()?.ToString() ?? "";
                                currentClient.Writer.WriteLine($"RECOVER_KEY_RES|{ek}");
                            }
                        }

                        // LỆNH TIÊU HỦY TIN NHẮN KHỎI DATABASE
                        else if (command == "BURN_MSG")
                        {
                            HandleBurnMessage(parts, currentClient);
                        }
                        // LỆNH BẮT ĐẦU ĐẾM NGƯỢC
                        else if (command == "START_BURN")
                        {
                            HandleStartBurn(parts, currentClient);
                        }

                        else if (command == "DELETE_ACCOUNT") { HandleDeleteAccount(parts, currentClient); break; }
                    }
                }
            }
            catch { }
            finally
            {
                if (currentClient != null)
                {
                    lock (lockObj) { onlineClients.Remove(currentClient.UserId); }
                    NotifyFriendStatus(currentClient.UserId, false);
                    Console.WriteLine($">> {currentClient.Username} da thoat.");
                }
                try { client.Close(); } catch { }
            }
        }

        static void HandleBurnMessage(string[] parts, ClientInfo client)
        {
            try
            {
                string type = parts[1];
                int targetId = int.Parse(parts[2]);
                string contentToBurn = parts.Length > 3 ? string.Join("|", parts.Skip(3)) : "";

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    if (type == "F")
                    {
                        string sql = "DELETE FROM Messages WHERE ((SenderId=@me AND ReceiverId=@target) OR (SenderId=@target AND ReceiverId=@me)) AND Content=@c";
                        SqlCommand cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@me", client.UserId);
                        cmd.Parameters.AddWithValue("@target", targetId);
                        cmd.Parameters.AddWithValue("@c", contentToBurn);
                        cmd.ExecuteNonQuery();

                        // THÊM MỚI: Báo cho người kia xóa tin nhắn khỏi màn hình ngay lập tức
                        lock (lockObj)
                        {
                            if (onlineClients.ContainsKey(targetId))
                            {
                                onlineClients[targetId].Writer.WriteLine($"MSG_BURNED|{contentToBurn}");
                            }
                        }
                    }
                    else if (type == "G")
                    {
                        string sql = "DELETE FROM GroupMessages WHERE GroupId=@gid AND Content=@c";
                        SqlCommand cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@gid", targetId);
                        cmd.Parameters.AddWithValue("@c", contentToBurn);
                        cmd.ExecuteNonQuery();

                        // Lấy danh sách thành viên nhóm
                        List<int> memberIds = new List<int>();
                        SqlCommand cmdGetMem = new SqlCommand("SELECT UserId FROM GroupMembers WHERE GroupId=@gid", conn);
                        cmdGetMem.Parameters.AddWithValue("@gid", targetId);
                        using (SqlDataReader r = cmdGetMem.ExecuteReader())
                        {
                            while (r.Read()) memberIds.Add(r.GetInt32(0));
                        }
                        lock (lockObj)
                        {
                            foreach (int uid in memberIds)
                            {
                                if (uid != client.UserId && onlineClients.ContainsKey(uid))
                                {
                                    onlineClients[uid].Writer.WriteLine($"MSG_BURNED|{contentToBurn}");
                                }
                            }
                        }
                    }
                }
                Console.WriteLine($">> [BẢO MẬT] Đã đồng bộ tiêu hủy tin nhắn của {client.Username} thành công!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi tiêu hủy tin nhắn: " + ex.Message);
            }
        }

        // HÀM XỬ LÝ BẮT ĐẦU ĐẾM NGƯỢC ĐỒNG BỘ
        static void HandleStartBurn(string[] parts, ClientInfo client)
        {
            try
            {
                string type = parts[1];
                int targetId = int.Parse(parts[2]);
                string payload = parts.Length > 3 ? string.Join("|", parts.Skip(3)) : "";

                if (type == "F")
                {
                    lock (lockObj)
                    {
                        if (onlineClients.ContainsKey(targetId))
                        {
                            // Báo cho đối phương biết là mình đã bấm đọc
                            onlineClients[targetId].Writer.WriteLine($"MSG_BURN_STARTED|{payload}");
                        }
                    }
                }
                else if (type == "G")
                {
                    List<int> memberIds = new List<int>();
                    using (SqlConnection conn = new SqlConnection(connectionString))
                    {
                        conn.Open();
                        SqlCommand cmdGetMem = new SqlCommand("SELECT UserId FROM GroupMembers WHERE GroupId=@gid", conn);
                        cmdGetMem.Parameters.AddWithValue("@gid", targetId);
                        using (SqlDataReader r = cmdGetMem.ExecuteReader())
                        {
                            while (r.Read()) memberIds.Add(r.GetInt32(0));
                        }
                    }

                    lock (lockObj)
                    {
                        foreach (int uid in memberIds)
                        {
                            if (uid != client.UserId && onlineClients.ContainsKey(uid))
                            {
                                onlineClients[uid].Writer.WriteLine($"MSG_BURN_STARTED|{payload}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi Start Burn: " + ex.Message);
            }
        }

        static void HandleUnfriend(string[] parts, ClientInfo client)
        {
            try
            {
                int friendId = int.Parse(parts[1]);

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = "DELETE FROM Friends WHERE (UserId=@me AND FriendId=@f) OR (UserId=@f AND FriendId=@me)";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@me", client.UserId);
                    cmd.Parameters.AddWithValue("@f", friendId);
                    cmd.ExecuteNonQuery();
                }

                lock (lockObj)
                {
                    if (onlineClients.ContainsKey(friendId))
                    {
                        onlineClients[friendId].Writer.WriteLine("GET_LIST");
                        onlineClients[friendId].Writer.WriteLine($"UNFRIENDED|{client.UserId}");
                    }
                }

                Console.WriteLine($">> {client.Username} đã hủy kết bạn với ID: {friendId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi hủy kết bạn: " + ex.Message);
            }
        }

        static void HandleRegister(string[] parts, StreamWriter writer)
        {
            try
            {
                string user = parts[1]; string pass = parts[2]; string display = parts[3]; string email = parts[4];
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SqlCommand check = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Username=@u OR Email=@e", conn);
                    check.Parameters.AddWithValue("@u", user);
                    check.Parameters.AddWithValue("@e", email);
                    if ((int)check.ExecuteScalar() > 0) writer.WriteLine("FAIL|Tài khoản hoặc Email đã tồn tại!");
                    else
                    {
                        SqlCommand cmd = new SqlCommand("INSERT INTO Users(Username, Password, DisplayName, Email) VALUES(@u, @p, @d, @e)", conn);
                        cmd.Parameters.AddWithValue("@u", user); cmd.Parameters.AddWithValue("@p", pass);
                        cmd.Parameters.AddWithValue("@d", display); cmd.Parameters.AddWithValue("@e", email);
                        cmd.ExecuteNonQuery(); writer.WriteLine("OK|Đăng ký thành công!");
                    }
                }
            }
            catch (Exception ex) { writer.WriteLine("FAIL|" + ex.Message); }
        }

        static ClientInfo HandleLogin(string[] parts, TcpClient client, StreamWriter writer)
        {
            string user = parts[1]; string pass = parts[2];
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                SqlCommand cmd = new SqlCommand("SELECT Id, DisplayName, Avatar FROM Users WHERE Username COLLATE SQL_Latin1_General_CP1_CS_AS = @u AND Password COLLATE SQL_Latin1_General_CP1_CS_AS = @p", conn);
                cmd.Parameters.AddWithValue("@u", user); cmd.Parameters.AddWithValue("@p", pass);
                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        int id = r.GetInt32(0);
                        string name = r.GetString(1);
                        string avatar = r.IsDBNull(2) ? "" : r.GetString(2);

                        ClientInfo info = new ClientInfo { Socket = client, Writer = writer, UserId = id, Username = name };
                        lock (lockObj)
                        {
                            if (onlineClients.ContainsKey(id)) onlineClients.Remove(id);
                            onlineClients.Add(id, info);
                        }

                        writer.WriteLine($"OK|{id}|{name}|{avatar}");

                        NotifyFriendStatus(id, true);
                        Console.WriteLine($">> {name} (ID: {id}) da dang nhap.");
                        return info;
                    }
                }
            }
            writer.WriteLine("FAIL|Sai tên đăng nhập hoặc mật khẩu"); return null;
        }

        static void HandleGetList(ClientInfo client)
        {
            StringBuilder sb = new StringBuilder("LIST");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = @"SELECT u.Id, u.DisplayName, u.Avatar FROM Friends f 
               JOIN Users u ON f.FriendId = u.Id WHERE f.UserId = @uid AND f.Status = 1
               UNION
               SELECT u.Id, u.DisplayName, u.Avatar FROM Friends f 
               JOIN Users u ON f.UserId = u.Id WHERE f.FriendId = @uid AND f.Status = 1";
                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@uid", client.UserId);
                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int fId = r.GetInt32(0);
                        string fName = r.GetString(1);
                        string fAvatar = r.IsDBNull(2) ? "" : r.GetString(2);
                        bool isOnline = onlineClients.ContainsKey(fId);
                        int status = isOnline ? 1 : 0;
                        sb.Append($"|F:{fId}:{fName}:{status}:{fAvatar}");
                    }
                }
                SqlCommand cmdG = new SqlCommand("SELECT g.Id, g.GroupName FROM Groups g JOIN GroupMembers gm ON g.Id=gm.GroupId WHERE gm.UserId=@uid", conn);
                cmdG.Parameters.AddWithValue("@uid", client.UserId);
                using (SqlDataReader r2 = cmdG.ExecuteReader()) { while (r2.Read()) sb.Append($"|G:{r2.GetInt32(0)}:{r2.GetString(1)}"); }
            }
            client.Writer.WriteLine(sb.ToString());
        }

        static void HandleUpdateAvatar(string[] parts, ClientInfo client)
        {
            try
            {
                string base64 = parts[1];
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SqlCommand cmd = new SqlCommand("UPDATE Users SET Avatar=@a WHERE Id=@id", conn);
                    cmd.Parameters.AddWithValue("@a", base64);
                    cmd.Parameters.AddWithValue("@id", client.UserId);
                    cmd.ExecuteNonQuery();
                }
                NotifyAvatarChange(client.UserId, base64);
            }
            catch (Exception ex) { Console.WriteLine("Loi update avatar: " + ex.Message); }
        }

        static void NotifyAvatarChange(int userId, string newAvatar)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = @"SELECT FriendId FROM Friends WHERE UserId = @uid AND Status = 1
                       UNION
                       SELECT UserId FROM Friends WHERE FriendId = @uid AND Status = 1";
                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int friendId = r.GetInt32(0);
                        lock (lockObj)
                        {
                            if (onlineClients.ContainsKey(friendId))
                            {
                                onlineClients[friendId].Writer.WriteLine($"AVATAR_UPDATE|{userId}|{newAvatar}");
                            }
                        }
                    }
                }
            }
        }

        static void HandleSearchUser(string[] parts, ClientInfo client)
        {
            try
            {
                string keyword = parts[1];
                StringBuilder sb = new StringBuilder("SEARCH_RES");
                bool found = false;
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"
                SELECT u.Id, u.DisplayName, u.Email, ISNULL(f.Status, -1) AS FriendStatus
                FROM Users u
                LEFT JOIN Friends f ON ((u.Id = f.UserId AND f.FriendId = @me) OR (u.Id = f.FriendId AND f.UserId = @me))
                WHERE u.Username LIKE @kw OR u.Email LIKE @kw OR u.DisplayName LIKE @kw";

                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@me", client.UserId);
                    cmd.Parameters.AddWithValue("@kw", "%" + keyword + "%");
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            found = true;
                            int tId = r.GetInt32(0);
                            string tName = r.GetString(1);
                            string tEmail = r.IsDBNull(2) ? "Chưa cập nhật email" : r.GetString(2);
                            int fStatus = r.GetInt32(3);
                            string statusStr = "NONE";
                            if (tId == client.UserId) statusStr = "ME";
                            else if (fStatus == 1) statusStr = "FRIEND";
                            else if (fStatus == 0) statusStr = "PENDING";
                            sb.Append($"|{tId}:{tName}:{tEmail}:{statusStr}");
                        }
                    }
                }

                if (!found) client.Writer.WriteLine("SEARCH_RES|NOT_FOUND");
                else client.Writer.WriteLine(sb.ToString());
            }
            catch (Exception ex) { Console.WriteLine("Lỗi tìm kiếm: " + ex.Message); }
        }

        static void HandleSendRequest(string[] parts, ClientInfo client)
        {
            try
            {
                int targetId = int.Parse(parts[1]);
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SqlCommand cmd = new SqlCommand("INSERT INTO Friends(UserId, FriendId, Status) VALUES(@me, @target, 0)", conn);
                    cmd.Parameters.AddWithValue("@me", client.UserId);
                    cmd.Parameters.AddWithValue("@target", targetId);
                    cmd.ExecuteNonQuery();

                    string notifMsg = $"{client.Username} đã gửi cho bạn một lời mời kết bạn!";
                    SaveNotification(targetId, notifMsg);

                    lock (lockObj)
                    {
                        if (onlineClients.ContainsKey(targetId))
                        {
                            onlineClients[targetId].Writer.WriteLine("NEW_REQ|Có lời mời kết bạn mới!");
                            string timeStr = DateTime.Now.ToString("HH:mm - dd/MM/yyyy");
                            onlineClients[targetId].Writer.WriteLine($"NEW_NOTIF|{notifMsg}|{timeStr}");
                        }
                    }
                }
            }
            catch { }
        }

        static void HandleGetRequestList(ClientInfo client)
        {
            StringBuilder sb = new StringBuilder("REQ_LIST");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = @"SELECT u.Id, u.DisplayName FROM Friends f 
                               JOIN Users u ON f.UserId = u.Id 
                               WHERE f.FriendId = @me AND f.Status = 0";
                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@me", client.UserId);
                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read()) sb.Append($"|{r.GetInt32(0)}:{r.GetString(1)}");
                }
            }
            client.Writer.WriteLine(sb.ToString());
        }

        static void HandleResponseRequest(string[] parts, ClientInfo client)
        {
            try
            {
                string action = parts[1];
                int requesterId = int.Parse(parts[2]);

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    if (action == "ACCEPT")
                    {
                        SqlCommand updateCmd = new SqlCommand("UPDATE Friends SET Status=1 WHERE UserId=@req AND FriendId=@me", conn);
                        updateCmd.Parameters.AddWithValue("@req", requesterId);
                        updateCmd.Parameters.AddWithValue("@me", client.UserId);
                        updateCmd.ExecuteNonQuery();

                        try
                        {
                            SqlCommand insertBack = new SqlCommand("INSERT INTO Friends(UserId, FriendId, Status) VALUES(@me, @req, 1)", conn);
                            insertBack.Parameters.AddWithValue("@req", requesterId);
                            insertBack.Parameters.AddWithValue("@me", client.UserId);
                            insertBack.ExecuteNonQuery();
                        }
                        catch { }

                        HandleGetList(client);

                        string notifMsg = $"{client.Username} đã chấp nhận lời mời kết bạn của bạn!";
                        SaveNotification(requesterId, notifMsg);

                        lock (lockObj)
                        {
                            if (onlineClients.ContainsKey(requesterId))
                            {
                                string timeStr = DateTime.Now.ToString("HH:mm - dd/MM/yyyy");
                                onlineClients[requesterId].Writer.WriteLine($"NEW_NOTIF|{notifMsg}|{timeStr}");
                                HandleGetList(onlineClients[requesterId]);
                            }
                        }
                    }
                    else
                    {
                        SqlCommand delCmd = new SqlCommand("DELETE FROM Friends WHERE UserId=@req AND FriendId=@me AND Status=0", conn);
                        delCmd.Parameters.AddWithValue("@req", requesterId);
                        delCmd.Parameters.AddWithValue("@me", client.UserId);
                        delCmd.ExecuteNonQuery();
                    }
                }
                HandleGetRequestList(client);
            }
            catch { }
        }

        static void HandleCreateGroup(string[] parts, ClientInfo client)
        {
            try
            {
                string groupName = parts[1];
                string memberIdsStr = parts.Length > 2 ? parts[2] : "";

                List<int> memberIds = new List<int> { client.UserId };
                if (!string.IsNullOrEmpty(memberIdsStr))
                {
                    memberIds.AddRange(memberIdsStr.Split(',').Select(int.Parse));
                }

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    if (string.IsNullOrWhiteSpace(groupName))
                    {
                        List<string> names = new List<string> { client.Username };
                        foreach (int id in memberIds.Skip(1).Take(2))
                        {
                            SqlCommand cmdName = new SqlCommand("SELECT DisplayName FROM Users WHERE Id=@id", conn);
                            cmdName.Parameters.AddWithValue("@id", id);
                            names.Add((string)cmdName.ExecuteScalar());
                        }
                        groupName = "Nhóm - " + string.Join(", ", names);
                    }

                    string inviteCode = GenerateInviteCode();

                    string sql = "INSERT INTO Groups(GroupName, InviteCode, CreatorId) OUTPUT INSERTED.Id VALUES(@name, @code, @creator)";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@name", groupName);
                    cmd.Parameters.AddWithValue("@code", inviteCode);
                    cmd.Parameters.AddWithValue("@creator", client.UserId);
                    int groupId = (int)cmd.ExecuteScalar();

                    foreach (int uId in memberIds.Distinct())
                    {
                        string sqlMember = "INSERT INTO GroupMembers(GroupId, UserId) VALUES(@gid, @uid)";
                        SqlCommand cmdMem = new SqlCommand(sqlMember, conn);
                        cmdMem.Parameters.AddWithValue("@gid", groupId);
                        cmdMem.Parameters.AddWithValue("@uid", uId);
                        cmdMem.ExecuteNonQuery();

                        if (uId != client.UserId)
                        {
                            string notifMsg = $"Bạn đã được thêm vào nhóm '{groupName}'";
                            SaveNotification(uId, notifMsg);

                            lock (lockObj)
                            {
                                if (onlineClients.ContainsKey(uId))
                                {
                                    string timeStr = DateTime.Now.ToString("HH:mm - dd/MM/yyyy");
                                    onlineClients[uId].Writer.WriteLine($"NEW_NOTIF|{notifMsg}|{timeStr}");
                                    HandleGetList(onlineClients[uId]);
                                }
                            }
                        }
                    }
                    HandleGetList(client);
                }
            }
            catch (Exception ex) { client.Writer.WriteLine("MSG_SYS|Lỗi tạo nhóm: " + ex.Message); }
        }

        static void HandleJoinGroup(string[] parts, ClientInfo client)
        {
            try
            {
                string code = parts[1].Trim().ToUpper();
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SqlCommand cmdFind = new SqlCommand("SELECT Id, GroupName FROM Groups WHERE InviteCode=@code", conn);
                    cmdFind.Parameters.AddWithValue("@code", code);
                    using (SqlDataReader r = cmdFind.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            int groupId = r.GetInt32(0);
                            string groupName = r.GetString(1);
                            r.Close();

                            SqlCommand cmdCheck = new SqlCommand("SELECT COUNT(*) FROM GroupMembers WHERE GroupId=@gid AND UserId=@uid", conn);
                            cmdCheck.Parameters.AddWithValue("@gid", groupId);
                            cmdCheck.Parameters.AddWithValue("@uid", client.UserId);
                            if ((int)cmdCheck.ExecuteScalar() > 0)
                            {
                                client.Writer.WriteLine("MSG_SYS|Bạn đã ở trong nhóm này rồi!");
                                return;
                            }

                            SqlCommand cmdJoin = new SqlCommand("INSERT INTO GroupMembers(GroupId, UserId) VALUES(@gid, @uid)", conn);
                            cmdJoin.Parameters.AddWithValue("@gid", groupId);
                            cmdJoin.Parameters.AddWithValue("@uid", client.UserId);
                            cmdJoin.ExecuteNonQuery();

                            string notifMsg = $"Tham gia nhóm '{groupName}' thành công!";
                            SaveNotification(client.UserId, notifMsg);
                            string timeStr = DateTime.Now.ToString("HH:mm - dd/MM/yyyy");
                            client.Writer.WriteLine($"NEW_NOTIF|{notifMsg}|{timeStr}");

                            HandleGetList(client);
                        }
                        else
                        {
                            client.Writer.WriteLine("MSG_SYS|Mã nhóm không tồn tại!");
                        }
                    }
                }
            }
            catch (Exception ex) { client.Writer.WriteLine("MSG_SYS|Lỗi: " + ex.Message); }
        }

        static void HandleSendPrivate(string[] parts, ClientInfo client)
        {
            try
            {
                int toId = int.Parse(parts[1]);
                string content = parts[2];

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = "INSERT INTO Messages(SenderId, ReceiverId, Content) VALUES(@s, @r, @c)";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@s", client.UserId);
                    cmd.Parameters.AddWithValue("@r", toId);
                    cmd.Parameters.AddWithValue("@c", content);
                    cmd.ExecuteNonQuery();
                }

                lock (lockObj)
                {
                    if (onlineClients.ContainsKey(toId))
                    {
                        onlineClients[toId].Writer.WriteLine($"MSG_PRIVATE|{client.UserId}|{client.Username}|{content}");
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Loi gui tin rieng: " + ex.Message); }
        }

        static void HandleSendGroup(string[] parts, ClientInfo client)
        {
            try
            {
                int groupId = int.Parse(parts[1]);
                string content = parts[2];

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sqlInsert = "INSERT INTO GroupMessages(GroupId, SenderId, Content) VALUES(@gid, @sid, @c)";
                    SqlCommand cmdInsert = new SqlCommand(sqlInsert, conn);
                    cmdInsert.Parameters.AddWithValue("@gid", groupId);
                    cmdInsert.Parameters.AddWithValue("@sid", client.UserId);
                    cmdInsert.Parameters.AddWithValue("@c", content);
                    cmdInsert.ExecuteNonQuery();

                    List<int> memberIds = new List<int>();
                    SqlCommand cmd = new SqlCommand("SELECT UserId FROM GroupMembers WHERE GroupId=@gid", conn);
                    cmd.Parameters.AddWithValue("@gid", groupId);
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read()) memberIds.Add(r.GetInt32(0));
                    }

                    lock (lockObj)
                    {
                        foreach (int uid in memberIds)
                        {
                            if (uid != client.UserId && onlineClients.ContainsKey(uid))
                            {
                                onlineClients[uid].Writer.WriteLine($"MSG_GROUP|{groupId}|{client.Username}|{content}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Loi gui tin nhom: " + ex.Message); }
        }

        static void HandleGetGroupHistory(string[] parts, ClientInfo client)
        {
            try
            {
                int groupId = int.Parse(parts[1]);
                StringBuilder sb = new StringBuilder("GROUP_HISTORY");

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"
                        SELECT SenderId, DisplayName, Content FROM (
                            SELECT TOP 50 gm.Id, gm.SenderId, u.DisplayName, gm.Content 
                            FROM GroupMessages gm
                            JOIN Users u ON gm.SenderId = u.Id
                            WHERE gm.GroupId=@gid 
                            ORDER BY gm.SentTime DESC
                        ) AS T
                        ORDER BY T.Id ASC";

                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@gid", groupId);

                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int senderId = r.GetInt32(0);
                            string senderName = r.GetString(1);
                            string content = r.GetString(2);
                            sb.Append($"|{senderId}:{senderName}:{content}");
                        }
                    }
                }
                client.Writer.WriteLine(sb.ToString());
            }
            catch { }
        }

        static void HandleGetHistory(string[] parts, ClientInfo client)
        {
            try
            {
                int friendId = int.Parse(parts[1]);
                StringBuilder sb = new StringBuilder("HISTORY");

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"
                        SELECT SenderId, Content FROM (
                            SELECT TOP 50 Id, SenderId, Content 
                            FROM Messages 
                            WHERE (SenderId=@me AND ReceiverId=@friend) 
                               OR (SenderId=@friend AND ReceiverId=@me)
                            ORDER BY Id DESC
                        ) AS T
                        ORDER BY T.Id ASC";

                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@me", client.UserId);
                    cmd.Parameters.AddWithValue("@friend", friendId);

                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int senderId = r.GetInt32(0);
                            string content = r.GetString(1);
                            sb.Append($"|{senderId}:{content}");
                        }
                    }
                }
                client.Writer.WriteLine(sb.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi tải lịch sử 1-1: " + ex.Message);
            }
        }

        static void NotifyFriendStatus(int userId, bool isOnline)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"SELECT FriendId FROM Friends WHERE UserId = @uid AND Status = 1
                           UNION
                           SELECT UserId FROM Friends WHERE FriendId = @uid AND Status = 1";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@uid", userId);
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int friendId = r.GetInt32(0);
                            lock (lockObj)
                            {
                                if (onlineClients.ContainsKey(friendId))
                                {
                                    string status = isOnline ? "1" : "0";
                                    onlineClients[friendId].Writer.WriteLine($"FRIEND_STATUS|{userId}|{status}");
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        static void UdpDiscoveryServer()
        {
            try
            {
                UdpClient udp = new UdpClient(8888);
                while (true)
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref remote);
                    string msg = Encoding.UTF8.GetString(data);
                    if (msg == "DISCOVER_CHAT_SERVER")
                    {
                        byte[] response = Encoding.UTF8.GetBytes("HERE_I_AM");
                        udp.Send(response, response.Length, remote);
                    }
                }
            }
            catch { }
        }

        static void HandleUpdateProfile(string[] parts, ClientInfo client)
        {
            try
            {
                string newName = parts[1];
                bool hasAvatarChanged = (parts[2] == "1");
                string newAvatarBase64 = (parts.Length > 3) ? parts[3] : "";

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql;
                    SqlCommand cmd;

                    if (hasAvatarChanged)
                    {
                        sql = "UPDATE Users SET DisplayName=@name, Avatar=@avt WHERE Id=@id";
                        cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@name", newName);
                        cmd.Parameters.AddWithValue("@avt", newAvatarBase64);
                    }
                    else
                    {
                        sql = "UPDATE Users SET DisplayName=@name WHERE Id=@id";
                        cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@name", newName);
                    }
                    cmd.Parameters.AddWithValue("@id", client.UserId);
                    cmd.ExecuteNonQuery();
                }

                client.Username = newName;
                Console.WriteLine($">> User {client.UserId} cập nhật profile thành công.");

                client.Writer.WriteLine($"PROFILE_UPDATED_OK|{newName}|{parts[2]}|{newAvatarBase64}");
                NotifyProfileChangeToFriends(client.UserId, newName, hasAvatarChanged, newAvatarBase64);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi update profile: " + ex.Message);
                client.Writer.WriteLine("MSG_SYS|Lỗi cập nhật thông tin.");
            }
        }

        static void NotifyProfileChangeToFriends(int userId, string newName, bool hasAvatarChanged, string newAvatar)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = @"SELECT FriendId FROM Friends WHERE UserId = @uid AND Status = 1
                       UNION
                       SELECT UserId FROM Friends WHERE FriendId = @uid AND Status = 1";
                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int friendId = r.GetInt32(0);
                        lock (lockObj)
                        {
                            if (onlineClients.ContainsKey(friendId))
                            {
                                string flag = hasAvatarChanged ? "1" : "0";
                                onlineClients[friendId].Writer.WriteLine($"FRIEND_PROFILE_UPDATE|{userId}|{newName}|{flag}|{newAvatar}");
                            }
                        }
                    }
                }
            }
        }

        static string GenerateInviteCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            Random random = new Random();
            return new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
        }

        static void HandleGetGroupMembers(string[] parts, ClientInfo client)
        {
            try
            {
                int groupId = int.Parse(parts[1]);
                StringBuilder sb = new StringBuilder($"GROUP_MEMBERS|{groupId}");

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SqlCommand cmdCreator = new SqlCommand("SELECT CreatorId, InviteCode FROM Groups WHERE Id=@gid", conn);
                    cmdCreator.Parameters.AddWithValue("@gid", groupId);
                    int creatorId = 0; string inviteCode = "";
                    using (SqlDataReader r0 = cmdCreator.ExecuteReader())
                    {
                        if (r0.Read()) { creatorId = r0.GetInt32(0); inviteCode = r0.GetString(1); }
                    }
                    sb.Append($"|{creatorId}|{inviteCode}");

                    string sql = @"SELECT u.Id, u.DisplayName, u.Avatar FROM GroupMembers gm 
                           JOIN Users u ON gm.UserId = u.Id WHERE gm.GroupId = @gid";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@gid", groupId);
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int uId = r.GetInt32(0);
                            string uName = r.GetString(1);
                            string uAvt = r.IsDBNull(2) ? "" : r.GetString(2);
                            sb.Append($"|{uId}:{uName}:{uAvt}");
                        }
                    }
                }
                client.Writer.WriteLine(sb.ToString());
            }
            catch { }
        }

        static void HandleLeaveGroup(string[] parts, ClientInfo client)
        {
            try
            {
                int groupId = int.Parse(parts[1]);
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SqlCommand cmdDel = new SqlCommand("DELETE FROM GroupMembers WHERE GroupId=@gid AND UserId=@uid", conn);
                    cmdDel.Parameters.AddWithValue("@gid", groupId);
                    cmdDel.Parameters.AddWithValue("@uid", client.UserId);
                    cmdDel.ExecuteNonQuery();
                }
                client.Writer.WriteLine("GET_LIST");

                HandleGetGroupMembers(new string[] { "", groupId.ToString() }, client);
            }
            catch { }
        }

        static void HandleKickMember(string[] parts, ClientInfo client)
        {
            try
            {
                int groupId = int.Parse(parts[1]);
                int targetId = int.Parse(parts[2]);

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SqlCommand cmdCheck = new SqlCommand("SELECT CreatorId FROM Groups WHERE Id=@gid", conn);
                    cmdCheck.Parameters.AddWithValue("@gid", groupId);
                    if ((int)cmdCheck.ExecuteScalar() != client.UserId) return;

                    SqlCommand cmdDel = new SqlCommand("DELETE FROM GroupMembers WHERE GroupId=@gid AND UserId=@uid", conn);
                    cmdDel.Parameters.AddWithValue("@gid", groupId);
                    cmdDel.Parameters.AddWithValue("@uid", targetId);
                    cmdDel.ExecuteNonQuery();

                    lock (lockObj) { if (onlineClients.ContainsKey(targetId)) onlineClients[targetId].Writer.WriteLine("GET_LIST"); }
                }
                HandleGetGroupMembers(new string[] { "", groupId.ToString() }, client);
            }
            catch { }
        }

        static void HandleDeleteGroup(string[] parts, ClientInfo client)
        {
            try
            {
                int groupId = int.Parse(parts[1]);
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    SqlCommand cmdCheck = new SqlCommand("SELECT CreatorId FROM Groups WHERE Id=@gid", conn);
                    cmdCheck.Parameters.AddWithValue("@gid", groupId);
                    if ((int)cmdCheck.ExecuteScalar() != client.UserId) return;

                    new SqlCommand($"DELETE FROM GroupMessages WHERE GroupId={groupId}", conn).ExecuteNonQuery();

                    List<int> memberIds = new List<int>();
                    SqlCommand cmdGetMem = new SqlCommand("SELECT UserId FROM GroupMembers WHERE GroupId=@gid", conn);
                    cmdGetMem.Parameters.AddWithValue("@gid", groupId);
                    using (SqlDataReader r = cmdGetMem.ExecuteReader())
                    {
                        while (r.Read()) memberIds.Add(r.GetInt32(0));
                    }

                    new SqlCommand($"DELETE FROM GroupMembers WHERE GroupId={groupId}", conn).ExecuteNonQuery();
                    new SqlCommand($"DELETE FROM Groups WHERE Id={groupId}", conn).ExecuteNonQuery();

                    lock (lockObj)
                    {
                        foreach (int uid in memberIds)
                        {
                            if (onlineClients.ContainsKey(uid))
                                onlineClients[uid].Writer.WriteLine("GET_LIST");
                        }
                    }
                }
            }
            catch { }
        }

        static void HandleDeleteAccount(string[] parts, ClientInfo client)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    int uid = client.UserId;

                    SqlCommand getGroups = new SqlCommand("SELECT Id FROM Groups WHERE CreatorId=@uid", conn);
                    getGroups.Parameters.AddWithValue("@uid", uid);
                    List<int> groupIds = new List<int>();
                    using (SqlDataReader r = getGroups.ExecuteReader())
                    {
                        while (r.Read()) groupIds.Add(r.GetInt32(0));
                    }

                    foreach (int gid in groupIds)
                    {
                        new SqlCommand($"DELETE FROM GroupMessages WHERE GroupId={gid}", conn).ExecuteNonQuery();
                        new SqlCommand($"DELETE FROM GroupMembers WHERE GroupId={gid}", conn).ExecuteNonQuery();
                        new SqlCommand($"DELETE FROM Groups WHERE Id={gid}", conn).ExecuteNonQuery();
                    }

                    string[] queries = {
                        "DELETE FROM GroupMessages WHERE SenderId=@uid",
                        "DELETE FROM GroupMembers WHERE UserId=@uid",
                        "DELETE FROM Messages WHERE SenderId=@uid OR ReceiverId=@uid",
                        "DELETE FROM Friends WHERE UserId=@uid OR FriendId=@uid",
                        "DELETE FROM Users WHERE Id=@uid"
                    };
                    foreach (string q in queries)
                    {
                        SqlCommand cmd = new SqlCommand(q, conn);
                        cmd.Parameters.AddWithValue("@uid", uid);
                        cmd.ExecuteNonQuery();
                    }
                }
                Console.WriteLine($">> Tai khoan {client.Username} (ID: {client.UserId}) da bi xoa khoi he thong.");
                client.Writer.WriteLine("DELETE_ACCOUNT_OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi xóa tài khoản: " + ex.Message);
                client.Writer.WriteLine("MSG_SYS|Lỗi hệ thống khi xóa tài khoản!");
            }
        }
        #endregion
    }
}