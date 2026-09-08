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
    static string HtmlPath;
    static WndProc KeepAlive;

    static void Main()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        HtmlPath = Path.Combine(dir, "overlay.html");
        if (!File.Exists(HtmlPath))
        {
            Console.WriteLine("overlay.html introuvable a cote de bridge.exe");
            return;
        }

        var http = new Thread(HttpLoop) { IsBackground = true };
        http.Start();

        Console.WriteLine("Overlay HTML pret.");
        Console.WriteLine("Dans OBS : source Navigateur (PAS fichier local)");
        Console.WriteLine("URL : http://127.0.0.1:" + Port + "/");
        Console.WriteLine("Taille conseillee : 520 x 280");
        Console.WriteLine("Laisse cette fenetre ouverte pendant le stream.");
        Console.WriteLine();

        RunMessageWindow();
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
            var stream = tcp.GetStream();
            stream.ReadTimeout = 15000;
            var req = ReadRequest(stream);
            if (req == null) { tcp.Close(); return; }

            if (req.Contains("Upgrade: websocket") && req.Contains("GET /ws"))
            {
                var key = Header(req, "Sec-WebSocket-Key");
                if (string.IsNullOrEmpty(key)) { tcp.Close(); return; }
                AcceptWebSocket(stream, tcp, key);
                return;
            }

            var html = File.ReadAllBytes(HtmlPath);
            var head =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                "Cache-Control: no-store\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Content-Length: " + html.Length + "\r\n" +
                "Connection: close\r\n\r\n";
            var hb = Encoding.ASCII.GetBytes(head);
            stream.Write(hb, 0, hb.Length);
            stream.Write(html, 0, html.Length);
            stream.Flush();
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
            if (sb.ToString().Contains("\r\n\r\n")) return sb.ToString();
            if (sb.Length > 16000) return null;
        }
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

        var client = new WebSocketClient { Stream = stream, Tcp = tcp };
        lock (Gate) Clients.Add(client);
        Broadcast("{\"event_type\":\"hello\"}");
        try
        {
            var buf = new byte[4096];
            while (tcp.Connected)
            {
                var n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) break;
            }
        }
        catch { }
        lock (Gate) Clients.Remove(client);
        try { tcp.Close(); } catch { }
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
