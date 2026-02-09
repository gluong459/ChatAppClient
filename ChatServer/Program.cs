using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

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
        // LƯU Ý: Thay đổi chuỗi kết nối cho đúng máy bạn
        static string connectionString = "Data Source=localhost;Initial Catalog=ChatAppDB;Integrated Security=True;TrustServerCertificate=True";

        static Dictionary<int, ClientInfo> onlineClients = new Dictionary<int, ClientInfo>();
        static object lockObj = new object();

        static void Main(string[] args)
        {
            Thread udpThread = new Thread(UdpDiscoveryServer);
            udpThread.IsBackground = true;
            udpThread.Start();

            TcpListener server = new TcpListener(IPAddress.Any, 8888);
            try
            {
                server.Start();
                Console.WriteLine("=== SERVER CHAT (FRIEND REQUEST UPDATE) ===");
            }
            catch (Exception ex) { Console.WriteLine("Loi: " + ex.Message); return; }

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

                    if (command == "REGISTER") { HandleRegister(parts, writer); break; }
                    else if (command == "LOGIN")
                    {
                        currentClient = HandleLogin(parts, client, writer);
                        if (currentClient == null) break;
                    }
                    else if (currentClient != null)
                    {
                        // --- CÁC LỆNH MỚI ---
                        if (command == "GET_LIST") HandleGetList(currentClient);
                        else if (command == "SEARCH_USER") HandleSearchUser(parts, currentClient);
                        else if (command == "SEND_REQ") HandleSendRequest(parts, currentClient);
                        else if (command == "GET_REQ_LIST") HandleGetRequestList(currentClient); // Lấy ds lời mời
                        else if (command == "RESP_REQ") HandleResponseRequest(parts, currentClient); // Đồng ý/Từ chối
                        else if (command == "CREATE_GROUP") HandleCreateGroup(parts, currentClient);
                        else if (command == "SEND_PRIVATE") HandleSendPrivate(parts, currentClient);
                        else if (command == "SEND_GROUP") HandleSendGroup(parts, currentClient);
                        else if (command == "GET_HISTORY") HandleGetHistory(parts, currentClient);
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
                client.Close();
            }
        }
        static void HandleRegister(string[] parts, StreamWriter writer)
        {
            // (Copy lại code cũ của bạn hoặc dùng logic INSERT đơn giản)
            string user = parts[1]; string pass = parts[2]; string display = parts[3];
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                SqlCommand check = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Username=@u", conn);
                check.Parameters.AddWithValue("@u", user);
                if ((int)check.ExecuteScalar() > 0) writer.WriteLine("FAIL|User ton tai");
                else
                {
                    SqlCommand cmd = new SqlCommand("INSERT INTO Users(Username,Password,DisplayName) VALUES(@u,@p,@d)", conn);
                    cmd.Parameters.AddWithValue("@u", user); cmd.Parameters.AddWithValue("@p", pass); cmd.Parameters.AddWithValue("@d", display);
                    cmd.ExecuteNonQuery(); writer.WriteLine("OK|Dang ky xong");
                }
            }
        }

        static ClientInfo HandleLogin(string[] parts, TcpClient client, StreamWriter writer)
        {
            string user = parts[1]; string pass = parts[2];
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                SqlCommand cmd = new SqlCommand("SELECT Id, DisplayName FROM Users WHERE Username=@u AND Password=@p", conn);
                cmd.Parameters.AddWithValue("@u", user); cmd.Parameters.AddWithValue("@p", pass);
                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        int id = r.GetInt32(0); string name = r.GetString(1);
                        ClientInfo info = new ClientInfo { Socket = client, Writer = writer, UserId = id, Username = name };
                        lock (lockObj)
                        {
                            if (onlineClients.ContainsKey(id)) onlineClients.Remove(id);
                            onlineClients.Add(id, info);
                        }
                        writer.WriteLine($"OK|{id}|{name}");
                        NotifyFriendStatus(id, true);

                        return info;
                    }
                }
            }
            writer.WriteLine("FAIL|Sai pass"); return null;
        }

        static void HandleGetList(ClientInfo client)
        {
            StringBuilder sb = new StringBuilder("LIST");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = @"SELECT u.Id, u.DisplayName FROM Friends f 
                       JOIN Users u ON f.FriendId = u.Id WHERE f.UserId = @uid AND f.Status = 1
                       UNION
                       SELECT u.Id, u.DisplayName FROM Friends f 
                       JOIN Users u ON f.UserId = u.Id WHERE f.FriendId = @uid AND f.Status = 1";
                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@uid", client.UserId);
                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int fId = r.GetInt32(0);
                        string fName = r.GetString(1);
                        bool isOnline = onlineClients.ContainsKey(fId);
                        int status = isOnline ? 1 : 0;
                        sb.Append($"|F:{fId}:{fName}:{status}");
                    }
                }
                SqlCommand cmdG = new SqlCommand("SELECT g.Id, g.GroupName FROM Groups g JOIN GroupMembers gm ON g.Id=gm.GroupId WHERE gm.UserId=@uid", conn);
                cmdG.Parameters.AddWithValue("@uid", client.UserId);
                using (SqlDataReader r2 = cmdG.ExecuteReader()) { while (r2.Read()) sb.Append($"|G:{r2.GetInt32(0)}:{r2.GetString(1)}"); }
            }
            client.Writer.WriteLine(sb.ToString());
        }
        //1. TÌM KIẾM NGƯỜI DÙNG VÀ KIỂM TRA TRẠNG THÁI
        static void HandleSearchUser(string[] parts, ClientInfo client)
        {
            string keyword = parts[1]; // Tên người muốn tìm
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                // Tìm User ID theo tên
                SqlCommand findCmd = new SqlCommand("SELECT Id, DisplayName FROM Users WHERE Username=@u", conn);
                findCmd.Parameters.AddWithValue("@u", keyword);

                using (SqlDataReader r = findCmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        int targetId = r.GetInt32(0);
                        string targetName = r.GetString(1);
                        r.Close();

                        if (targetId == client.UserId)
                        {
                            client.Writer.WriteLine($"SEARCH_RES|{targetId}|{targetName}|ME"); // Tìm thấy chính mình
                            return;
                        }

                        // Kiểm tra quan hệ trong bảng Friends
                        // 0: Chưa gì cả, 1: Đã gửi lời mời (mình gửi), 2: Đã là bạn
                        string status = "NONE";

                        // Check xem đã là bạn chưa (2 chiều)
                        SqlCommand checkFriend = new SqlCommand("SELECT Status FROM Friends WHERE (UserId=@me AND FriendId=@target) OR (UserId=@target AND FriendId=@me)", conn);
                        checkFriend.Parameters.AddWithValue("@me", client.UserId);
                        checkFriend.Parameters.AddWithValue("@target", targetId);

                        object res = checkFriend.ExecuteScalar();
                        if (res != null)
                        {
                            int s = (int)res;
                            if (s == 1) status = "FRIEND";
                            else if (s == 0) status = "PENDING"; // Đang chờ duyệt (có thể mình gửi hoặc họ gửi)
                        }

                        client.Writer.WriteLine($"SEARCH_RES|{targetId}|{targetName}|{status}");
                    }
                    else
                    {
                        client.Writer.WriteLine("SEARCH_RES|NOT_FOUND");
                    }
                }
            }
        }

        // 2. GỬI LỜI MỜI KẾT BẠN
        static void HandleSendRequest(string[] parts, ClientInfo client)
        {
            int targetId = int.Parse(parts[1]);
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                // Insert status = 0 (Chờ duyệt)
                try
                {
                    SqlCommand cmd = new SqlCommand("INSERT INTO Friends(UserId, FriendId, Status) VALUES(@me, @target, 0)", conn);
                    cmd.Parameters.AddWithValue("@me", client.UserId);
                    cmd.Parameters.AddWithValue("@target", targetId);
                    cmd.ExecuteNonQuery();

                    client.Writer.WriteLine("MSG_SYS|Đã gửi lời mời kết bạn.");

                    // Thông báo cho người kia nếu họ online
                    lock (lockObj)
                    {
                        if (onlineClients.ContainsKey(targetId))
                        {
                            onlineClients[targetId].Writer.WriteLine("NEW_REQ|Có lời mời kết bạn mới!");
                        }
                    }
                }
                catch { client.Writer.WriteLine("MSG_SYS|Lỗi: Đã có lời mời trước đó."); }
            }
        }
        // 3. LẤY DANH SÁCH LỜI MỜI ĐANG CHỜ
        static void HandleGetRequestList(ClientInfo client)
        {
            // Tìm những dòng mà FriendId = Mình và Status = 0 (Người khác gửi cho mình)
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

        //4. ĐỒNG Ý / TỪ CHỐI
        static void HandleResponseRequest(string[] parts, ClientInfo client)
        {
            // RESP_REQ|ACCEPT/DECLINE|RequesterId
            string action = parts[1];
            int requesterId = int.Parse(parts[2]);

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                if (action == "ACCEPT")
                {
                    // Cập nhật Status = 1 cho dòng (Requester, Me)
                    SqlCommand updateCmd = new SqlCommand("UPDATE Friends SET Status=1 WHERE UserId=@req AND FriendId=@me", conn);
                    updateCmd.Parameters.AddWithValue("@req", requesterId);
                    updateCmd.Parameters.AddWithValue("@me", client.UserId);
                    updateCmd.ExecuteNonQuery();

                    // Insert thêm dòng (Me, Requester) Status=1 để thành bạn 2 chiều (tùy logic DB, làm cách này dễ query list nhất)
                    /* LƯU Ý: Nếu database bạn set Primary Key (UserId, FriendId) thì insert ngược lại sẽ OK */
                    try
                    {
                        SqlCommand insertBack = new SqlCommand("INSERT INTO Friends(UserId, FriendId, Status) VALUES(@me, @req, 1)", conn);
                        insertBack.Parameters.AddWithValue("@req", requesterId);
                        insertBack.Parameters.AddWithValue("@me", client.UserId);
                        insertBack.ExecuteNonQuery();
                    }
                    catch { } // Nếu đã có rồi thì thôi

                    client.Writer.WriteLine("MSG_SYS|Đã chấp nhận kết bạn!");
                    HandleGetList(client); // Refresh list bạn bè của mình

                    // Refresh list bạn bè của người kia
                    lock (lockObj)
                    {
                        if (onlineClients.ContainsKey(requesterId))
                        {
                            onlineClients[requesterId].Writer.WriteLine("MSG_SYS|Lời mời kết bạn đã được chấp nhận!");
                            HandleGetList(onlineClients[requesterId]);
                        }
                    }
                }
                else // DECLINE
                {
                    // Xóa dòng yêu cầu
                    SqlCommand delCmd = new SqlCommand("DELETE FROM Friends WHERE UserId=@req AND FriendId=@me AND Status=0", conn);
                    delCmd.Parameters.AddWithValue("@req", requesterId);
                    delCmd.Parameters.AddWithValue("@me", client.UserId);
                    delCmd.ExecuteNonQuery();
                    client.Writer.WriteLine("MSG_SYS|Đã từ chối lời mời.");
                }
            }
            // Gửi lại danh sách lời mời (đã update)
            HandleGetRequestList(client);
        }

        // (Các hàm HandleCreateGroup, HandleSendPrivate... giữ nguyên từ code cũ)
        static void HandleCreateGroup(string[] parts, ClientInfo client) { /* Code cũ... */ }
        static void HandleSendPrivate(string[] parts, ClientInfo client)
        {
            try
            {
                int toId = int.Parse(parts[1]);
                string content = parts[2];

                // 1. Lưu vào Database
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = "INSERT INTO Messages(SenderId, ReceiverId, Content) VALUES(@s, @r, @c)";
                    SqlCommand cmd = new SqlCommand(sql, conn);

                    // SỬA LỖI Ở ĐÂY: Đổi 'sender' thành 'client'
                    cmd.Parameters.AddWithValue("@s", client.UserId);
                    cmd.Parameters.AddWithValue("@r", toId);
                    cmd.Parameters.AddWithValue("@c", content);
                    cmd.ExecuteNonQuery();
                }

                // 2. Gửi ngay cho người nhận
                lock (lockObj)
                {
                    if (onlineClients.ContainsKey(toId))
                    {
                        // SỬA LỖI Ở ĐÂY: Đổi 'sender' thành 'client'
                        onlineClients[toId].Writer.WriteLine($"MSG_PRIVATE|{client.UserId}|{client.Username}|{content}");
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Loi gui tin: " + ex.Message); }
        }
        static void HandleSendGroup(string[] parts, ClientInfo client) { /* Code cũ... */ }
        static void UdpDiscoveryServer() { }
        static void HandleGetHistory(string[] parts, ClientInfo client)
        {
            // GET_HISTORY|FriendID
            int friendId = int.Parse(parts[1]);
            StringBuilder sb = new StringBuilder("HISTORY");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                // Lấy tin nhắn 2 chiều: Tôi gửi Bạn HOẶC Bạn gửi Tôi
                string sql = @"SELECT SenderId, Content FROM Messages 
                       WHERE (SenderId=@me AND ReceiverId=@friend) 
                          OR (SenderId=@friend AND ReceiverId=@me)
                       ORDER BY SentTime ASC";

                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@me", client.UserId);
                cmd.Parameters.AddWithValue("@friend", friendId);

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int senderId = r.GetInt32(0);
                        string content = r.GetString(1);
                        // Gửi về format: SenderID:Content (để client biết ai gửi)
                        sb.Append($"|{senderId}:{content}");
                    }
                }
            }
            // Gửi trả về Client
            client.Writer.WriteLine(sb.ToString());
        }
        static void NotifyFriendStatus(int userId, bool isOnline)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                // Lấy danh sách những người ĐANG LÀ BẠN BÈ của user này
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
                        // Nếu bạn mình đang Online thì gửi thông báo cho họ
                        lock (lockObj)
                        {
                            if (onlineClients.ContainsKey(friendId))
                            {
                                // FRIEND_STATUS|ID_Cua_Minh|1_hoac_0
                                string status = isOnline ? "1" : "0";
                                onlineClients[friendId].Writer.WriteLine($"FRIEND_STATUS|{userId}|{status}");
                            }
                        }
                    }
                }
            }
        }
    }
}