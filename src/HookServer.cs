using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace NoodlePet
{
    // 只聽本機 127.0.0.1 的極簡 HTTP 端點
    // POST /prompt、/stop：Claude Code 的 http hook；POST /hello：第二次打開 exe 時叫桌寵打招呼
    public class HookServer
    {
        const int MaxBody = 2 * 1024 * 1024;
        static readonly Regex SessionId = new Regex("\"session_id\"\\s*:\\s*\"([^\"]{1,200})\"");
        TcpListener listener;
        Action<string, string> handler;
        int port;

        public bool Start(int port, Action<string, string> onEvent)
        {
            this.port = port;
            handler = onEvent;
            try { listener = new TcpListener(IPAddress.Loopback, port); listener.Start(); }
            catch { listener = null; return false; }
            var l = listener;
            new Thread(() => Accept(l)) { IsBackground = true, Name = "hook-server" }.Start();
            return true;
        }

        public void Stop()
        {
            try { if (listener != null) listener.Stop(); } catch { }
            listener = null;
        }

        void Accept(TcpListener l)
        {
            while (true)
            {
                TcpClient client;
                try { client = l.AcceptTcpClient(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;
                    var stream = client.GetStream();
                    byte[] rest;
                    var head = ReadHead(stream, out rest);
                    if (head == null) return;
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var first = lines[0].Split(' ');
                    string method = first[0], path = first.Length > 1 ? first[1] : "", host = "";
                    int length = 0;
                    bool json = false, chunked = false;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon <= 0) continue;
                        string name = lines[i].Substring(0, colon).Trim().ToLowerInvariant();
                        string value = lines[i].Substring(colon + 1).Trim().ToLowerInvariant();
                        if (name == "content-length") int.TryParse(value, out length);
                        else if (name == "content-type") json = value.StartsWith("application/json");
                        else if (name == "transfer-encoding") chunked = value.Contains("chunked");
                        else if (name == "host") host = value;
                    }
                    // 只收 JSON（網頁不經預檢送不出 application/json）＋ Host 必須是本機（擋 DNS rebinding）
                    bool local = host == "127.0.0.1:" + port || host == "localhost:" + port;
                    bool ok = method == "POST" && json && local && (path == "/prompt" || path == "/stop" || path == "/hello");
                    string body = ok ? ReadBody(stream, rest, chunked ? -1 : Math.Min(Math.Max(0, length), MaxBody)) : "";
                    var reply = Encoding.ASCII.GetBytes((ok ? "HTTP/1.1 204 No Content" : "HTTP/1.1 404 Not Found") +
                        "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    stream.Write(reply, 0, reply.Length);
                    if (!ok) return;
                    var m = SessionId.Match(body);
                    handler(path.Substring(1), m.Success ? m.Groups[1].Value : "");
                }
                catch { }
            }
        }

        static string ReadHead(Stream s, out byte[] rest)
        {
            rest = null;
            var buf = new byte[16384];
            int len = 0;
            while (len < buf.Length)
            {
                int n = s.Read(buf, len, buf.Length - len);
                if (n <= 0) return null;
                len += n;
                for (int i = Math.Max(0, len - n - 3); i + 3 < len; i++)
                    if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10)
                    {
                        rest = new byte[len - i - 4];
                        Array.Copy(buf, i + 4, rest, 0, rest.Length);
                        return Encoding.ASCII.GetString(buf, 0, i);
                    }
            }
            return null;
        }

        // length = -1 代表 chunked：讀到結尾標記為止（只拿來找 session_id，不需要精確解碼）
        static string ReadBody(Stream s, byte[] already, int length)
        {
            var ms = new MemoryStream();
            ms.Write(already, 0, length < 0 ? already.Length : Math.Min(already.Length, length));
            var buf = new byte[8192];
            while (length < 0 ? (!ChunkEnd(ms) && ms.Length < MaxBody) : (ms.Length < length))
            {
                int n = s.Read(buf, 0, length < 0 ? buf.Length : (int)Math.Min(buf.Length, length - ms.Length));
                if (n <= 0) break;
                ms.Write(buf, 0, n);
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        static bool ChunkEnd(MemoryStream ms)
        {
            if (ms.Length < 5) return false;
            var b = ms.GetBuffer();
            int n = (int)ms.Length;
            return b[n - 5] == (byte)'0' && b[n - 4] == 13 && b[n - 3] == 10 && b[n - 2] == 13 && b[n - 1] == 10;
        }
    }
}
