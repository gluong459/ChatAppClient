using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ChatServer
{
    class Program
    {
        static List<TcpClient> clients = new List<TcpClient>();
        static string userFile = "users.txt";       // File lưu tài khoản
        static string historyFile = "history.txt";  // File lưu tin nhắn
        static object fileLock = new object();      // Khóa để chống lỗi khi ghi file cùng lúc

        static void Main(string[] args)
        {
            // Tạo các file cần thiết nếu chưa có
            if (!File.Exists(userFile)) File.Create(userFile).Close();
            if (!File.Exists(historyFile)) File.Create(historyFile).Close();

            IPAddress ip = IPAddress.Any;
            int port = 8888;
            TcpListener server = new TcpListener(ip, port);
            server.Start();
            Console.WriteLine($"Server dang chay tai {ip}:{port}");
            Console.WriteLine("Da bat chuc nang luu lich su tin nhan.");

            while (true)
            {
                TcpClient client = server.AcceptTcpClient();
                Thread t = new Thread(HandleClient);
                t.Start(client);
            }
        }

        static void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            StreamReader reader = new StreamReader(client.GetStream());
            StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

            try
            {
                string request = reader.ReadLine();
                if (request == null) return;

                string[] parts = request.Split('|');
                string command = parts[0];

                if (command == "REGISTER")
                {
                    HandleRegister(parts, writer);
                    client.Close();
                }
                else if (command == "LOGIN")
                {
                    string displayName = HandleLogin(parts, writer);
                    if (displayName != null)
                    {
                        EnterChatRoom(client, displayName, reader, writer);
                    }
                    else
                    {
                        client.Close();
                    }
                }
            }
            catch { client.Close(); }
        }

        static void HandleRegister(string[] parts, StreamWriter writer)
        {
            string username = parts[1];
            string password = parts[2];
            string displayName = parts[3];

            lock (fileLock) // Dùng lock khi đọc/ghi file
            {
                string[] lines = File.ReadAllLines(userFile);
                foreach (string line in lines)
                {
                    if (line.Split('|')[0] == username)
                    {
                        writer.WriteLine("FAIL|Tai khoan da ton tai");
                        return;
                    }
                }
                File.AppendAllText(userFile, $"{username}|{password}|{displayName}\n");
            }

            Console.WriteLine($">> Da tao user: {username}");
            writer.WriteLine("OK|Dang ky thanh cong");
        }

        static string HandleLogin(string[] parts, StreamWriter writer)
        {
            string username = parts[1];
            string password = parts[2];

            lock (fileLock)
            {
                string[] lines = File.ReadAllLines(userFile);
                foreach (string line in lines)
                {
                    string[] data = line.Split('|');
                    if (data.Length >= 3 && data[0] == username && data[1] == password)
                    {
                        writer.WriteLine("OK|" + data[2]);
                        Console.WriteLine($">> {data[2]} da dang nhap.");
                        return data[2];
                    }
                }
            }
            writer.WriteLine("FAIL|Sai thong tin");
            return null;
        }

        static void EnterChatRoom(TcpClient client, string displayName, StreamReader reader, StreamWriter writer)
        {
            // 1. Gửi lại lịch sử tin nhắn cũ cho người mới vào
            lock (fileLock)
            {
                if (File.Exists(historyFile))
                {
                    string[] historyLines = File.ReadAllLines(historyFile);
                    foreach (string line in historyLines)
                    {
                        writer.WriteLine(line);
                    }
                }
            }
            // Gửi một dòng kẻ để phân biệt đâu là tin nhắn cũ
            writer.WriteLine("---------------- Ban da vao phong chat ----------------");

            // 2. Thêm vào danh sách online
            clients.Add(client);
            BroadcastMessage("Server: " + displayName + " da tham gia.");

            try
            {
                while (true)
                {
                    string msg = reader.ReadLine();
                    if (msg == null) break;

                    BroadcastMessage(displayName + ": " + msg);
                    Console.WriteLine(displayName + ": " + msg);
                }
            }
            catch { }
            finally
            {
                clients.Remove(client);
                BroadcastMessage("Server: " + displayName + " da roi phong.");
                client.Close();
            }
        }

        // Hàm này vừa gửi tin cho mọi người, VỪA LƯU VÀO FILE
        static void BroadcastMessage(string msg)
        {
            // 1. Lưu vào file history.txt
            lock (fileLock)
            {
                try
                {
                    File.AppendAllText(historyFile, msg + "\n");
                }
                catch { }
            }

            // 2. Gửi cho tất cả client đang online
            foreach (TcpClient c in clients)
            {
                try
                {
                    StreamWriter w = new StreamWriter(c.GetStream()) { AutoFlush = true };
                    w.WriteLine(msg);
                }
                catch { }
            }
        }
    }
}