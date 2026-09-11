using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

internal static class Program
{
    const int Port = 7689;
    const int WM_INPUT = 0x00FF;
    const int RID_INPUT = 0x10000003;
    const int RIM_TYPEMOUSE = 0;
    const int RIM_TYPEKEYBOARD = 1;
    const uint RIDEV_INPUTSINK = 0x00000100;
    const ushort RI_MOUSE_LEFT_DOWN = 0x0001;
    const ushort RI_MOUSE_LEFT_UP = 0x0002;
    const ushort RI_MOUSE_RIGHT_DOWN = 0x0004;
    const ushort RI_MOUSE_RIGHT_UP = 0x0008;
    const ushort RI_MOUSE_WHEEL = 0x0400;
    const ushort RI_KEY_BREAK = 1;
    const ushort RI_KEY_E0 = 2;
    static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    static readonly object Gate = new object();
    static readonly List<WebSocketClient> Clients = new List<WebSocketClient>();
    static int MouseMask;
    static string BaseDir;
    static string HtmlPath;
    static string PanelPath;
    static string SettingsPath;
    static string DefaultSettingsPath;
    static WndProc KeepAlive;

    const string DefaultSettingsJson =
        "{\"accent\":\"#e56b8a\",\"design\":\"classic\",\"mouse\":\"ribbon\",\"layout\":\"azerty\",\"sgJump\":\"space\",\"sgCrouch\":\"kC\",\"sgFps\":138}";

    static void Main()
    {
        BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        HtmlPath = Path.Combine(BaseDir, "overlay.html");
        PanelPath = Path.Combine(BaseDir, "panel.html");
        SettingsPath = Path.Combine(BaseDir, "settings.json");
        DefaultSettingsPath = Path.Combine(BaseDir, "settings.default.json");
        try { Directory.CreateDirectory(Path.Combine(BaseDir, "assets")); } catch { }
        if (!File.Exists(HtmlPath))
        {
            Console.WriteLine("overlay.html introuvable a cote de bridge.exe");
            return;
        }

        EnsureSettingsFile();

        var http = new Thread(HttpLoop) { IsBackground = true };
        http.Start();
        var keep = new Thread(KeepWsAlive) { IsBackground = true };
        keep.Start();

        Console.WriteLine("Overlay HTML pret (V2 + panneau).");
        Console.WriteLine("Dans OBS : source Navigateur (PAS fichier local)");
        Console.WriteLine("URL overlay : http://127.0.0.1:" + Port + "/");
        Console.WriteLine("URL panneau : http://127.0.0.1:" + Port + "/panel");
        Console.WriteLine("Taille conseillee : 700 x 340");
        Console.WriteLine("Laisse cette fenetre ouverte pendant le stream.");
        Console.WriteLine();

        RunMessageWindow();
    }

    static void EnsureSettingsFile()
    {
        if (File.Exists(SettingsPath)) return;
        try
        {
            if (File.Exists(DefaultSettingsPath))
                File.Copy(DefaultSettingsPath, SettingsPath);
            else
                File.WriteAllText(SettingsPath, DefaultSettingsJson, Encoding.UTF8);
        }
        catch
        {
            try { File.WriteAllText(SettingsPath, DefaultSettingsJson, Encoding.UTF8); } catch { }
        }
    }

    static string LoadSettingsJson()
    {
        try
        {
            EnsureSettingsFile();
            return File.ReadAllText(SettingsPath, Encoding.UTF8);
        }
        catch
        {
            return DefaultSettingsJson;
        }
    }

    static bool SaveSettingsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            File.WriteAllText(SettingsPath, json.Trim(), Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string RequestPath(string req)
    {
        var first = req.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
        var parts = first.Split(' ');
        if (parts.Length < 2) return "/";
        var path = parts[1];
        var q = path.IndexOf('?');
        if (q >= 0) path = path.Substring(0, q);
        return path;
    }

    static string RequestBody(string req)
    {
        var idx = req.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (idx < 0) return "";
        return req.Substring(idx + 4);
    }

    static void WriteBytes(NetworkStream stream, string status, string contentType, byte[] body)
    {
        var head =
            status + "\r\n" +
            "Content-Type: " + contentType + "\r\n" +
            "Cache-Control: no-store\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
            "Access-Control-Allow-Headers: Content-Type\r\n" +
            "Content-Length: " + body.Length + "\r\n" +
            "Connection: close\r\n\r\n";
        var hb = Encoding.ASCII.GetBytes(head);
        stream.Write(hb, 0, hb.Length);
        if (body.Length > 0) stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    static void WriteText(NetworkStream stream, string status, string contentType, string text)
    {
        WriteBytes(stream, status, contentType, Encoding.UTF8.GetBytes(text ?? ""));
    }

    static void KeepWsAlive()
    {
        while (true)
        {
            Thread.Sleep(20000);
            try { Broadcast("{\"event_type\":\"hello\"}"); }
            catch { }
        }
    }

    static void HttpLoop()
    {
        var listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        while (true)
        {
            TcpClient tcp;
            try { tcp = listener.AcceptTcpClient(); }
            catch { break; }
            ThreadPool.QueueUserWorkItem(_ => HandleHttp(tcp));
        }
    }

    static void HandleHttp(TcpClient tcp)
    {
        try
        {
            tcp.NoDelay = true;
            tcp.ReceiveTimeout = 8000;
            var stream = tcp.GetStream();
            stream.ReadTimeout = 8000;
            var req = ReadRequest(stream);
            if (req == null) { tcp.Close(); return; }

            if (req.Contains("Upgrade: websocket") && req.Contains("GET /ws"))
            {
                var key = Header(req, "Sec-WebSocket-Key");
                if (string.IsNullOrEmpty(key)) { tcp.Close(); return; }
                AcceptWebSocket(stream, tcp, key);
                return;
            }

            var path = RequestPath(req);
            var isPost = req.StartsWith("POST ", StringComparison.OrdinalIgnoreCase);
            var isOptions = req.StartsWith("OPTIONS ", StringComparison.OrdinalIgnoreCase);

            if (isOptions)
            {
                WriteText(stream, "HTTP/1.1 204 No Content", "text/plain; charset=utf-8", "");
                tcp.Close();
                return;
            }

            if (path == "/settings" || path == "/settings.json")
            {
                if (isPost)
                {
                    var body = RequestBody(req);
                    if (!SaveSettingsJson(body))
                    {
                        WriteText(stream, "HTTP/1.1 500 Internal Server Error", "application/json; charset=utf-8",
                            "{\"ok\":false}");
                        tcp.Close();
                        return;
                    }
                    var saved = LoadSettingsJson();
                    Broadcast("{\"event_type\":\"settings\",\"settings\":" + saved + "}");
                    WriteText(stream, "HTTP/1.1 200 OK", "application/json; charset=utf-8",
                        "{\"ok\":true,\"settings\":" + saved + "}");
                    tcp.Close();
                    return;
                }

                WriteText(stream, "HTTP/1.1 200 OK", "application/json; charset=utf-8", LoadSettingsJson());
                tcp.Close();
                return;
            }

            if (path == "/media/upload" && isPost)
            {
                var body = RequestBody(req);
                string savedName;
                string err;
                if (!TrySaveMediaUpload(body, out savedName, out err))
                {
                    WriteText(stream, "HTTP/1.1 400 Bad Request", "application/json; charset=utf-8",
                        "{\"ok\":false,\"error\":\"" + JsonEscape(err) + "\"}");
                    tcp.Close();
                    return;
                }
                WriteText(stream, "HTTP/1.1 200 OK", "application/json; charset=utf-8",
                    "{\"ok\":true,\"file\":\"" + JsonEscape(savedName) + "\",\"url\":\"/assets/" + JsonEscape(savedName) + "\"}");
                tcp.Close();
                return;
            }

            if (path == "/media/delete" && isPost)
            {
                var body = RequestBody(req);
                var file = JsonGetString(body, "file");
                if (!TryDeleteMedia(file))
                {
                    WriteText(stream, "HTTP/1.1 400 Bad Request", "application/json; charset=utf-8",
                        "{\"ok\":false}");
                    tcp.Close();
                    return;
                }
                WriteText(stream, "HTTP/1.1 200 OK", "application/json; charset=utf-8", "{\"ok\":true}");
                tcp.Close();
                return;
            }

            string filePath = null;
            string contentType = "text/html; charset=utf-8";
            if (path == "/" || path == "/overlay" || path == "/overlay.html")
                filePath = HtmlPath;
            else if (path == "/panel" || path == "/panel.html")
                filePath = PanelPath;
            else if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith("."))
                {
                    WriteText(stream, "HTTP/1.1 400 Bad Request", "text/plain; charset=utf-8", "Bad request");
                    tcp.Close();
                    return;
                }
                filePath = Path.Combine(BaseDir, "assets", name);
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg") contentType = "image/jpeg";
                else if (ext == ".png") contentType = "image/png";
                else if (ext == ".webp") contentType = "image/webp";
                else if (ext == ".gif") contentType = "image/gif";
                else contentType = "application/octet-stream";
            }

            if (filePath == null || !File.Exists(filePath))
            {
                WriteText(stream, "HTTP/1.1 404 Not Found", "text/plain; charset=utf-8", "Not found");
                tcp.Close();
                return;
            }

            WriteBytes(stream, "HTTP/1.1 200 OK", contentType, File.ReadAllBytes(filePath));
            tcp.Close();
        }
        catch
        {
            try { tcp.Close(); } catch { }
        }
    }

    static string ReadRequest(NetworkStream stream)
    {
        var buf = new byte[4096];
        var sb = new StringBuilder();
        while (true)
        {
            var n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) return null;
            sb.Append(Encoding.ASCII.GetString(buf, 0, n));
            if (sb.ToString().Contains("\r\n\r\n")) break;
            if (sb.Length > 16000) return null;
        }

        var req = sb.ToString();
        var headerEnd = req.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0) return req;
        var headers = req.Substring(0, headerEnd);
        var body = req.Substring(headerEnd + 4);
        var cl = Header(headers + "\r\n", "Content-Length");
        int need;
        if (!int.TryParse(cl, out need) || need <= 0) return req;
        // Uploads médias base64 : jusqu'à ~8 Mo ; settings restent petits
        if (need > 9000000) return null;
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        while (bodyBytes.Length < need)
        {
            var n = stream.Read(buf, 0, Math.Min(buf.Length, need - bodyBytes.Length));
            if (n <= 0) break;
            var more = new byte[bodyBytes.Length + n];
            Buffer.BlockCopy(bodyBytes, 0, more, 0, bodyBytes.Length);
            Buffer.BlockCopy(buf, 0, more, bodyBytes.Length, n);
            bodyBytes = more;
        }
        if (bodyBytes.Length > need)
        {
            var trimmed = new byte[need];
            Buffer.BlockCopy(bodyBytes, 0, trimmed, 0, need);
            bodyBytes = trimmed;
        }
        return headers + "\r\n\r\n" + Encoding.UTF8.GetString(bodyBytes);
    }

    static string JsonEscape(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    static string JsonGetString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
        var needle = "\"" + key + "\"";
        var i = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        i = json.IndexOf(':', i + needle.Length);
        if (i < 0) return null;
        i++;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        if (i >= json.Length || json[i] != '"') return null;
        i++;
        var sb = new StringBuilder();
        while (i < json.Length)
        {
            var c = json[i++];
            if (c == '\\' && i < json.Length)
            {
                var n = json[i++];
                if (n == 'n') sb.Append('\n');
                else if (n == 'r') sb.Append('\r');
                else if (n == 't') sb.Append('\t');
                else sb.Append(n);
                continue;
            }
            if (c == '"') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    static bool TrySaveMediaUpload(string json, out string savedName, out string err)
    {
        savedName = null;
        err = "invalid";
        var name = JsonGetString(json, "name");
        var data = JsonGetString(json, "data");
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(data))
        {
            err = "name/data manquants";
            return false;
        }
        name = Path.GetFileName(name);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".gif" && ext != ".webp")
        {
            err = "format non supporté";
            return false;
        }
        // data:image/...;base64,XXXX  ou base64 brut
        var comma = data.IndexOf(',');
        if (data.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
            data = data.Substring(comma + 1);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); }
        catch
        {
            err = "base64 invalide";
            return false;
        }
        if (bytes.Length < 8 || bytes.Length > 6500000)
        {
            err = "fichier trop petit/grand";
            return false;
        }
        var safe = "user-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" +
                   Guid.NewGuid().ToString("N").Substring(0, 8) + ext;
        var dir = Path.Combine(BaseDir, "assets");
        try { Directory.CreateDirectory(dir); } catch { }
        var path = Path.Combine(dir, safe);
        try
        {
            File.WriteAllBytes(path, bytes);
        }
        catch
        {
            err = "écriture impossible";
            return false;
        }
        savedName = safe;
        err = null;
        return true;
    }

    static bool TryDeleteMedia(string file)
    {
        if (string.IsNullOrEmpty(file)) return false;
        file = Path.GetFileName(file);
        if (!file.StartsWith("user-", StringComparison.OrdinalIgnoreCase)) return false;
        var path = Path.Combine(BaseDir, "assets", file);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    static string Header(string req, string name)
    {
        foreach (var line in req.Split(new[] { "\r\n" }, StringSplitOptions.None))
        {
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                return line.Substring(name.Length + 1).Trim();
        }
        return null;
    }

    static void AcceptWebSocket(NetworkStream stream, TcpClient tcp, string key)
    {
        var accept = Convert.ToBase64String(
            SHA1.Create().ComputeHash(
                Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var resp =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
        var rb = Encoding.ASCII.GetBytes(resp);
        stream.Write(rb, 0, rb.Length);
        stream.Flush();

        tcp.ReceiveTimeout = 0;
        stream.ReadTimeout = Timeout.Infinite;
        stream.WriteTimeout = Timeout.Infinite;
        try
        {
            tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }
        catch { }

        var client = new WebSocketClient { Stream = stream, Tcp = tcp };
        lock (Gate) Clients.Add(client);
        Broadcast("{\"event_type\":\"hello\"}");
        try
        {
            BroadcastTo(client, "{\"event_type\":\"settings\",\"settings\":" + LoadSettingsJson() + "}");
        }
        catch { }
        try { PumpWebSocket(client); }
        catch { }
        lock (Gate) Clients.Remove(client);
        try { tcp.Close(); } catch { }
    }

    static void PumpWebSocket(WebSocketClient client)
    {
        var pending = new List<byte>();
        var buf = new byte[4096];
        var stream = client.Stream;
        var tcp = client.Tcp;
        while (tcp.Connected)
        {
            int n;
            try { n = stream.Read(buf, 0, buf.Length); }
            catch { break; }
            if (n <= 0) break;
            for (var i = 0; i < n; i++) pending.Add(buf[i]);
            while (TryHandleWsFrame(client, pending)) { }
        }
    }

    static bool TryHandleWsFrame(WebSocketClient client, List<byte> pending)
    {
        if (pending.Count < 2) return false;
        var b0 = pending[0];
        var b1 = pending[1];
        var opcode = b0 & 0x0F;
        var masked = (b1 & 0x80) != 0;
        long len = b1 & 0x7F;
        var idx = 2;
        if (len == 126)
        {
            if (pending.Count < 4) return false;
            len = (pending[2] << 8) | pending[3];
            idx = 4;
        }
        else if (len == 127)
        {
            if (pending.Count < 10) return false;
            len = 0;
            for (var i = 0; i < 8; i++) len = (len << 8) | pending[2 + i];
            idx = 10;
        }
        var maskLen = masked ? 4 : 0;
        if (pending.Count < idx + maskLen + len) return false;

        byte[] mask = null;
        if (masked)
        {
            mask = new byte[4];
            for (var i = 0; i < 4; i++) mask[i] = pending[idx + i];
        }
        var dataStart = idx + maskLen;
        var payload = new byte[len];
        for (var i = 0; i < len; i++)
        {
            var b = pending[dataStart + i];
            payload[i] = mask != null ? (byte)(b ^ mask[i % 4]) : b;
        }
        pending.RemoveRange(0, dataStart + (int)len);

        if (opcode == 0x8)
            throw new IOException("ws close");
        if (opcode == 0x9)
        {
            var pong = EncodeControl(0xA, payload);
            lock (Gate)
            {
                try { client.Stream.Write(pong, 0, pong.Length); }
                catch { }
            }
        }
        return true;
    }

    static byte[] EncodeControl(int opcode, byte[] payload)
    {
        if (payload == null) payload = new byte[0];
        if (payload.Length > 125) payload = new byte[0];
        var frame = new byte[2 + payload.Length];
        frame[0] = (byte)(0x80 | opcode);
        frame[1] = (byte)payload.Length;
        Buffer.BlockCopy(payload, 0, frame, 2, payload.Length);
        return frame;
    }

    static void Broadcast(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = EncodeFrame(payload);
        lock (Gate)
        {
            for (var i = Clients.Count - 1; i >= 0; i--)
            {
                try { Clients[i].Stream.Write(frame, 0, frame.Length); }
                catch
                {
                    try { Clients[i].Tcp.Close(); } catch { }
                    Clients.RemoveAt(i);
                }
            }
        }
    }

    static void BroadcastTo(WebSocketClient client, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = EncodeFrame(payload);
        try { client.Stream.Write(frame, 0, frame.Length); }
        catch { }
    }

    static byte[] EncodeFrame(byte[] payload)
    {
        byte[] frame;
        if (payload.Length < 126)
        {
            frame = new byte[2 + payload.Length];
            frame[0] = 0x81;
            frame[1] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, 2, payload.Length);
        }
        else
        {
            frame = new byte[4 + payload.Length];
            frame[0] = 0x81;
            frame[1] = 126;
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)(payload.Length);
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
        }
        return frame;
    }

    static void RunMessageWindow()
    {
        KeepAlive = WindowProc;
        var wc = new WNDCLASS
        {
            lpfnWndProc = KeepAlive,
            hInstance = GetModuleHandle(null),
            lpszClassName = "RoseOverlayRawInput"
        };
        RegisterClass(ref wc);
        var hwnd = CreateWindowEx(0, wc.lpszClassName, "rose-overlay", 0,
            0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        var devices = new RAWINPUTDEVICE[2];
        devices[0].usUsagePage = 1;
        devices[0].usUsage = 2;
        devices[0].dwFlags = RIDEV_INPUTSINK;
        devices[0].hwndTarget = hwnd;
        devices[1].usUsagePage = 1;
        devices[1].usUsage = 6;
        devices[1].dwFlags = RIDEV_INPUTSINK;
        devices[1].hwndTarget = hwnd;
        if (!RegisterRawInputDevices(devices, 2, Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
            Console.WriteLine("Echec RegisterRawInputDevices");

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT) HandleRawInput(lParam);
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    static void HandleRawInput(IntPtr lParam)
    {
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, Marshal.SizeOf(typeof(RAWINPUTHEADER)));
        if (size == 0) return;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buf, ref size, Marshal.SizeOf(typeof(RAWINPUTHEADER))) != size)
                return;
            var header = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
            if (header.dwType == RIM_TYPEMOUSE)
            {
                var mouse = (RAWMOUSE)Marshal.PtrToStructure(
                    new IntPtr(buf.ToInt64() + Marshal.SizeOf(typeof(RAWINPUTHEADER))), typeof(RAWMOUSE));
                OnMouse(mouse);
            }
            else if (header.dwType == RIM_TYPEKEYBOARD)
            {
                var kb = (RAWKEYBOARD)Marshal.PtrToStructure(
                    new IntPtr(buf.ToInt64() + Marshal.SizeOf(typeof(RAWINPUTHEADER))), typeof(RAWKEYBOARD));
                OnKey(kb);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static void OnMouse(RAWMOUSE m)
    {
        if (m.lLastX != 0 || m.lLastY != 0)
            Broadcast("{\"event_type\":\"mouse_moved\",\"delta_x\":" + m.lLastX + ",\"delta_y\":" + m.lLastY + "}");

        var flags = m.usButtonFlags;
        if ((flags & RI_MOUSE_WHEEL) != 0)
        {
            var delta = (short)m.usButtonData;
            var rot = delta > 0 ? 1 : -1;
            Broadcast("{\"event_type\":\"mouse_wheel\",\"rotation\":" + rot + "}");
        }
        if ((flags & RI_MOUSE_LEFT_DOWN) != 0 || (flags & RI_MOUSE_LEFT_UP) != 0 ||
            (flags & RI_MOUSE_RIGHT_DOWN) != 0 || (flags & RI_MOUSE_RIGHT_UP) != 0)
        {
            if ((flags & RI_MOUSE_LEFT_DOWN) != 0) MouseMask |= 1 << 8;
            if ((flags & RI_MOUSE_LEFT_UP) != 0) MouseMask &= ~(1 << 8);
            if ((flags & RI_MOUSE_RIGHT_DOWN) != 0) MouseMask |= 1 << 9;
            if ((flags & RI_MOUSE_RIGHT_UP) != 0) MouseMask &= ~(1 << 9);
            var down = (flags & (RI_MOUSE_LEFT_DOWN | RI_MOUSE_RIGHT_DOWN)) != 0;
            Broadcast("{\"event_type\":\"" + (down ? "mouse_pressed" : "mouse_released") +
                      "\",\"mask\":" + MouseMask + ",\"button\":" +
                      (((flags & (RI_MOUSE_LEFT_DOWN | RI_MOUSE_LEFT_UP)) != 0) ? 1 : 2) + "}");
        }
    }

    static void OnKey(RAWKEYBOARD k)
    {
        if (k.VKey == 255) return;
        var code = (int)k.MakeCode;
        if ((k.Flags & RI_KEY_E0) != 0) code |= 0x0E00;
        var down = (k.Flags & RI_KEY_BREAK) == 0;
        Broadcast("{\"event_type\":\"" + (down ? "key_pressed" : "key_released") + "\",\"keycode\":" + code + "}");
    }

    class WebSocketClient
    {
        public NetworkStream Stream;
        public TcpClient Tcp;
    }

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASS
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE
    {
        public ushort usUsagePage, usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER
    {
        public uint dwType, dwSize;
        public IntPtr hDevice, wParam;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort usFlags;
        [FieldOffset(4)] public uint ulButtons;
        [FieldOffset(4)] public ushort usButtonFlags;
        [FieldOffset(6)] public ushort usButtonData;
        [FieldOffset(8)] public uint ulRawButtons;
        [FieldOffset(12)] public int lLastX;
        [FieldOffset(16)] public int lLastY;
        [FieldOffset(20)] public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWKEYBOARD
    {
        public ushort MakeCode, Flags, Reserved, VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll")] static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, int cbSize);
    [DllImport("user32.dll")]
    static extern uint GetRawInputData(IntPtr hRawInput, int uiCommand, IntPtr pData, ref uint pcbSize, int cbSizeHeader);
}
