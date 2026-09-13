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
    const int WM_TIMER = 0x0113;
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
    const int TIMER_MOUSE_FLUSH = 1;
    // Coalesce high-Hz Raw Input moves (~125 Hz max) to avoid WS/CPU floods while gaming
    const uint MouseFlushMs = 8;
    static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    static readonly object Gate = new object();
    static readonly List<WebSocketClient> Clients = new List<WebSocketClient>();
    static int MouseMask;
    static int PendingDx;
    static int PendingDy;
    static bool HasPendingMouse;
    static IntPtr RawBuf = IntPtr.Zero;
    static uint RawBufCap;
    static string BaseDir;
    static string HtmlPath;
    static string PanelPath;
    static string SettingsPath;
    static string DefaultSettingsPath;
    static string ThemePersoDir;
    static string UserAssetsDir;
    static string DesignsDir;
    static string ExportsDir;
    static string BuiltinAssetsDir;
    static WndProc KeepAlive;

    const string DefaultSettingsJson =
        "{\"accent\":\"#e56b8a\",\"design\":\"classic\",\"mouse\":\"ribbon\",\"layout\":\"azerty\",\"sgJump\":\"space\",\"sgCrouch\":\"kC\",\"sgFps\":138}";

    static void Main()
    {
        BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        HtmlPath = Path.Combine(BaseDir, "overlay.html");
        PanelPath = Path.Combine(BaseDir, "panel.html");
        DefaultSettingsPath = Path.Combine(BaseDir, "settings.default.json");
        BuiltinAssetsDir = Path.Combine(BaseDir, "assets");
        ThemePersoDir = Path.Combine(BaseDir, "themeperso");
        UserAssetsDir = Path.Combine(ThemePersoDir, "assets");
        DesignsDir = Path.Combine(ThemePersoDir, "designs");
        ExportsDir = Path.Combine(ThemePersoDir, "exports");
        SettingsPath = Path.Combine(ThemePersoDir, "settings.json");
        EnsureThemePerso();
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
        Console.WriteLine("Donnees perso : themeperso\\ (conserve aux mises a jour)");
        Console.WriteLine("Dans OBS : source Navigateur (PAS fichier local)");
        Console.WriteLine("URL overlay : http://127.0.0.1:" + Port + "/");
        Console.WriteLine("URL panneau : http://127.0.0.1:" + Port + "/panel");
        Console.WriteLine("Taille conseillee : 700 x 340");
        Console.WriteLine("Laisse cette fenetre ouverte pendant le stream.");
        Console.WriteLine();

        RunMessageWindow();
    }

    static void EnsureThemePerso()
    {
        try { Directory.CreateDirectory(ThemePersoDir); } catch { }
        try { Directory.CreateDirectory(UserAssetsDir); } catch { }
        try { Directory.CreateDirectory(DesignsDir); } catch { }
        try { Directory.CreateDirectory(ExportsDir); } catch { }
        try { Directory.CreateDirectory(BuiltinAssetsDir); } catch { }

        // README local pour que l'utilisateur ne supprime pas le dossier
        var readme = Path.Combine(ThemePersoDir, "NE-PAS-SUPPRIMER.txt");
        if (!File.Exists(readme))
        {
            try
            {
                File.WriteAllText(readme,
                    "Dossier themeperso\r\n" +
                    "=================\r\n" +
                    "Tes themes / reglages / images perso sont ici.\r\n" +
                    "- designs\\  = themes sauvegardes (plusieurs possibles)\r\n" +
                    "- exports\\  = dossiers a partager\r\n" +
                    "- assets\\   = images / GIF uploades\r\n" +
                    "Ce dossier n'est PAS ecrase lors des mises a jour GitHub.\r\n" +
                    "Ne le supprime pas si tu veux garder ton overlay.\r\n",
                    Encoding.UTF8);
            }
            catch { }
        }

        // Migration : ancien settings.json a la racine -> themeperso
        var legacySettings = Path.Combine(BaseDir, "settings.json");
        if (!File.Exists(SettingsPath) && File.Exists(legacySettings))
        {
            try { File.Copy(legacySettings, SettingsPath, false); } catch { }
        }

        // Migration : anciennes images user-* dans assets/ -> themeperso/assets/
        try
        {
            if (Directory.Exists(BuiltinAssetsDir))
            {
                foreach (var file in Directory.GetFiles(BuiltinAssetsDir, "user-*"))
                {
                    var name = Path.GetFileName(file);
                    var dest = Path.Combine(UserAssetsDir, name);
                    if (!File.Exists(dest))
                    {
                        try { File.Copy(file, dest, false); } catch { }
                    }
                }
            }
        }
        catch { }
    }

    static string ResolveAssetPath(string name)
    {
        if (name.StartsWith("user-", StringComparison.OrdinalIgnoreCase))
        {
            var perso = Path.Combine(UserAssetsDir, name);
            if (File.Exists(perso)) return perso;
            // fallback legacy
            return Path.Combine(BuiltinAssetsDir, name);
        }
        return Path.Combine(BuiltinAssetsDir, name);
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

    static string SanitizeDesignId(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        id = id.Trim();
        if (id.Length > 64) id = id.Substring(0, 64);
        var sb = new StringBuilder(id.Length);
        foreach (var c in id)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')
                sb.Append(c);
        }
        var s = sb.ToString();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "theme";
        name = name.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c == ' ' || c == '\t') sb.Append('-');
            else if (Array.IndexOf(invalid, c) >= 0 || c < 32) sb.Append('-');
            else sb.Append(c);
        }
        var s = sb.ToString().Trim().Trim('.');
        while (s.IndexOf("--", StringComparison.Ordinal) >= 0) s = s.Replace("--", "-");
        if (string.IsNullOrEmpty(s)) s = "theme";
        if (s.Length > 48) s = s.Substring(0, 48);
        return s;
    }

    static string ListDesignsJson()
    {
        try { Directory.CreateDirectory(DesignsDir); } catch { }
        var sb = new StringBuilder();
        sb.Append('[');
        var first = true;
        try
        {
            foreach (var dir in Directory.GetDirectories(DesignsDir))
            {
                var id = Path.GetFileName(dir);
                if (SanitizeDesignId(id) != id) continue;
                var themePath = Path.Combine(dir, "theme.json");
                if (!File.Exists(themePath)) continue;
                string json;
                try { json = File.ReadAllText(themePath, Encoding.UTF8); }
                catch { continue; }
                var name = JsonGetTopLevelString(json, "name") ?? id;
                var accent = JsonGetTopLevelString(json, "accent") ?? "#e56b8a";
                var updated = JsonGetTopLevelString(json, "updatedAt") ?? "";
                var thumb = ExtractFirstUserAsset(json) ?? "";
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":\"").Append(JsonEscape(id)).Append("\",");
                sb.Append("\"name\":\"").Append(JsonEscape(name)).Append("\",");
                sb.Append("\"accent\":\"").Append(JsonEscape(accent)).Append("\",");
                sb.Append("\"updatedAt\":\"").Append(JsonEscape(updated)).Append("\",");
                sb.Append("\"thumb\":\"").Append(JsonEscape(thumb)).Append("\"}");
            }
        }
        catch { }
        sb.Append(']');
        return sb.ToString();
    }

    static string ExtractFirstUserAsset(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var i = 0;
        while (true)
        {
            var idx = json.IndexOf("\"file\"", i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            var q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            var q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            var file = json.Substring(q1 + 1, q2 - q1 - 1);
            if (file.StartsWith("user-", StringComparison.OrdinalIgnoreCase)
                && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                return file;
            i = q2 + 1;
        }
    }

    static void CollectUserAssets(string json, List<string> into)
    {
        if (string.IsNullOrEmpty(json) || into == null) return;
        var i = 0;
        while (true)
        {
            var idx = json.IndexOf("\"file\"", i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) break;
            var q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) break;
            var q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) break;
            var file = json.Substring(q1 + 1, q2 - q1 - 1);
            if (file.StartsWith("user-", StringComparison.OrdinalIgnoreCase)
                && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && !into.Contains(file))
                into.Add(file);
            i = q2 + 1;
        }
    }

    static bool TrySaveDesign(string body, out string savedId, out string err)
    {
        savedId = null;
        err = "json invalide";
        if (string.IsNullOrWhiteSpace(body)) return false;
        var json = body.Trim();
        if (!json.StartsWith("{")) return false;
        var id = SanitizeDesignId(JsonGetTopLevelString(json, "id"));
        if (string.IsNullOrEmpty(id))
        {
            id = "d_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            json = "{\n  \"id\":\"" + JsonEscape(id) + "\"," + json.Substring(1);
        }
        try
        {
            var dir = Path.Combine(DesignsDir, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "theme.json"), json, Encoding.UTF8);
            savedId = id;
            return true;
        }
        catch
        {
            err = "ecriture impossible";
            return false;
        }
    }

    static bool TryDeleteDesign(string id)
    {
        id = SanitizeDesignId(id);
        if (string.IsNullOrEmpty(id)) return false;
        var dir = Path.Combine(DesignsDir, id);
        if (!Directory.Exists(dir)) return false;
        try
        {
            Directory.Delete(dir, true);
            return true;
        }
        catch { return false; }
    }

    static bool TryExportDesign(string id, out string exportPath, out string err)
    {
        exportPath = null;
        err = "introuvable";
        id = SanitizeDesignId(id);
        if (string.IsNullOrEmpty(id)) return false;
        var themePath = Path.Combine(DesignsDir, id, "theme.json");
        if (!File.Exists(themePath)) return false;
        string json;
        try { json = File.ReadAllText(themePath, Encoding.UTF8); }
        catch { err = "lecture impossible"; return false; }

        var name = SanitizeFolderName(JsonGetTopLevelString(json, "name") ?? id);
        try { Directory.CreateDirectory(ExportsDir); } catch { }
        var dest = Path.Combine(ExportsDir, name);
        var n = 2;
        while (Directory.Exists(dest))
        {
            dest = Path.Combine(ExportsDir, name + "-" + n);
            n++;
            if (n > 99) { err = "trop d'exports"; return false; }
        }
        try
        {
            Directory.CreateDirectory(dest);
            var assetsDest = Path.Combine(dest, "assets");
            Directory.CreateDirectory(assetsDest);
            File.WriteAllText(Path.Combine(dest, "theme.json"), json, Encoding.UTF8);
            var files = new List<string>();
            CollectUserAssets(json, files);
            foreach (var file in files)
            {
                var src = ResolveAssetPath(file);
                if (File.Exists(src))
                {
                    try { File.Copy(src, Path.Combine(assetsDest, file), true); } catch { }
                }
            }
            File.WriteAllText(Path.Combine(dest, "LISEZMOI.txt"),
                "Theme overlay a partager\r\n" +
                "========================\r\n" +
                "1. Copie TOUT le contenu de assets\\ vers themeperso\\assets\\\r\n" +
                "2. Copie ce dossier (ou son theme.json) dans themeperso\\designs\\\r\n" +
                "   sous un nom SANS espaces (ex: mon-theme) :\r\n" +
                "   themeperso\\designs\\mon-theme\\theme.json\r\n" +
                "3. Relance start-overlay.bat / le panneau.\r\n",
                Encoding.UTF8);
            exportPath = dest;
            return true;
        }
        catch
        {
            err = "export impossible";
            return false;
        }
    }

    static void WriteBytes(NetworkStream stream, string status, string contentType, byte[] body)
    {
        var head =
            status + "\r\n" +
            "Content-Type: " + contentType + "\r\n" +
            "Cache-Control: no-store\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
            "Access-Control-Allow-Headers: Content-Type, X-Filename\r\n" +
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

    // Lit headers (ASCII) + body brut (binaire-safe) — nécessaire pour upload GIF
    static bool TryReadHttp(NetworkStream stream, out string headerText, out byte[] body)
    {
        headerText = null;
        body = new byte[0];
        var buf = new byte[8192];
        var headerBuf = new List<byte>(4096);
        while (true)
        {
            var n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) return false;
            for (var i = 0; i < n; i++) headerBuf.Add(buf[i]);
            // Cherche fin des headers
            var arr = headerBuf.ToArray();
            var end = IndexOfHeaderEnd(arr);
            if (end >= 0)
            {
                headerText = Encoding.ASCII.GetString(arr, 0, end);
                var already = arr.Length - (end + 4);
                int need = 0;
                var cl = Header(headerText + "\r\n", "Content-Length");
                int.TryParse(cl, out need);
                if (need < 0) need = 0;
                // Uploads médias : jusqu'à ~14 Mo
                if (need > 14000000) return false;
                body = new byte[need];
                var got = 0;
                if (already > 0)
                {
                    var copy = Math.Min(already, need);
                    Buffer.BlockCopy(arr, end + 4, body, 0, copy);
                    got = copy;
                }
                while (got < need)
                {
                    var r = stream.Read(buf, 0, Math.Min(buf.Length, need - got));
                    if (r <= 0) break;
                    Buffer.BlockCopy(buf, 0, body, got, r);
                    got += r;
                }
                if (got < need) return false;
                return true;
            }
            if (headerBuf.Count > 32000) return false;
        }
    }

    static int IndexOfHeaderEnd(byte[] data)
    {
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
                return i;
        }
        return -1;
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
            tcp.ReceiveTimeout = 120000;
            var stream = tcp.GetStream();
            stream.ReadTimeout = 120000;
            string req;
            byte[] bodyBytes;
            if (!TryReadHttp(stream, out req, out bodyBytes)) { tcp.Close(); return; }

            if (req.IndexOf("Upgrade: websocket", StringComparison.OrdinalIgnoreCase) >= 0
                && req.IndexOf("GET /ws", StringComparison.OrdinalIgnoreCase) >= 0)
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
                    var body = Encoding.UTF8.GetString(bodyBytes);
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

            if (path == "/designs")
            {
                if (isPost)
                {
                    string savedId;
                    string err;
                    if (!TrySaveDesign(Encoding.UTF8.GetString(bodyBytes), out savedId, out err))
                    {
                        WriteText(stream, "HTTP/1.1 400 Bad Request", "application/json; charset=utf-8",
                            "{\"ok\":false,\"error\":\"" + JsonEscape(err) + "\"}");
                        tcp.Close();
                        return;
                    }
                    WriteText(stream, "HTTP/1.1 200 OK", "application/json; charset=utf-8",
                        "{\"ok\":true,\"id\":\"" + JsonEscape(savedId) + "\"}");
                    tcp.Close();
                    return;
                }

                WriteText(stream, "HTTP/1.1 200 OK", "application/json; charset=utf-8", ListDesignsJson());
                tcp.Close();
                return;
            }

            if (path.StartsWith("/designs/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = path.Substring("/designs/".Length);
                var slash = rest.IndexOf('/');
                var designId = slash >= 0 ? rest.Substring(0, slash) : rest;
                var action = slash >= 0 ? rest.Substring(slash + 1) : "";
                designId = SanitizeDesignId(designId);

                if (string.IsNullOrEmpty(designId))
                {
                    WriteText(stream, "HTTP/1.1 400 Bad Request", "application/json; charset=utf-8",
                        "{\"ok\":false,\"error\":\"id invalide\"}");
                    tcp.Close();
                    return;
                }

                if (action == "export" && isPost)
                {
                    string exportPath;
                    string err;
                    if (!TryExportDesign(designId, out exportPath, out err))
                    {
                        WriteText(stream, "HTTP/1.1 400 Bad Request", "application/json; charset=utf-8",
                            "{\"ok\":false,\"error\":\"" + JsonEscape(err) + "\"}");
                        tcp.Close();
                        return;
                    }
                    WriteText(stream, "HTTP/1.1 200 OK", "application/json; charset=utf-8",
                        "{\"ok\":true,\"path\":\"" + JsonEscape(exportPath) + "\"}");
                    tcp.Close();
                    return;
                }

                if (string.IsNullOrEmpty(action) && isPost)
                {
                    // DELETE via POST body {"delete":true} to avoid needing DELETE method
                    var body = Encoding.UTF8.GetString(bodyBytes);
                    if (body.IndexOf("\"delete\"", StringComparison.OrdinalIgnoreCase) >= 0
                        && body.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!TryDeleteDesign(designId))
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
                }

                if (string.IsNullOrEmpty(action) && !isPost)
                {
                    var themePath = Path.Combine(DesignsDir, designId, "theme.json");
                    if (!File.Exists(themePath))
                    {
                        WriteText(stream, "HTTP/1.1 404 Not Found", "application/json; charset=utf-8",
                            "{\"ok\":false}");
                        tcp.Close();
                        return;
                    }
                    WriteText(stream, "HTTP/1.1 200 OK", "application/json; charset=utf-8",
                        File.ReadAllText(themePath, Encoding.UTF8));
                    tcp.Close();
                    return;
                }

                WriteText(stream, "HTTP/1.1 404 Not Found", "application/json; charset=utf-8", "{\"ok\":false}");
                tcp.Close();
                return;
            }

            if (path == "/media/upload" && isPost)
            {
                string savedName;
                string err;
                var ct = Header(req, "Content-Type") ?? "";
                var ok = false;
                if (ct.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
                    ok = TrySaveMediaUpload(Encoding.UTF8.GetString(bodyBytes), out savedName, out err);
                else
                {
                    var fname = Header(req, "X-Filename");
                    if (string.IsNullOrEmpty(fname))
                    {
                        // fallback query ?name=
                        var first = req.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                        var qi = first.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
                        if (qi >= 0)
                        {
                            var rest = first.Substring(qi + 5);
                            var sp = rest.IndexOf(' ');
                            if (sp >= 0) rest = rest.Substring(0, sp);
                            var amp = rest.IndexOf('&');
                            if (amp >= 0) rest = rest.Substring(0, amp);
                            try { fname = Uri.UnescapeDataString(rest); } catch { fname = rest; }
                        }
                    }
                    ok = TrySaveMediaBinary(bodyBytes, fname, out savedName, out err);
                }
                if (!ok)
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
                var body = Encoding.UTF8.GetString(bodyBytes);
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
                filePath = ResolveAssetPath(name);
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

    // Lit une clé string au niveau racine uniquement (évite keyLayout[].id)
    static string JsonGetTopLevelString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
        var needle = "\"" + key + "\"";
        var depth = 0;
        var inStr = false;
        var esc = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inStr)
            {
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') inStr = false;
                continue;
            }
            if (c == '"')
            {
                if (depth == 1 && i + needle.Length <= json.Length
                    && string.Compare(json, i, needle, 0, needle.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    var after = i + needle.Length;
                    while (after < json.Length && char.IsWhiteSpace(json[after])) after++;
                    if (after >= json.Length || json[after] != ':') { inStr = true; continue; }
                    after++;
                    while (after < json.Length && char.IsWhiteSpace(json[after])) after++;
                    if (after >= json.Length || json[after] != '"') return null;
                    after++;
                    var sb = new StringBuilder();
                    while (after < json.Length)
                    {
                        var ch = json[after++];
                        if (ch == '\\' && after < json.Length)
                        {
                            var n = json[after++];
                            if (n == 'n') sb.Append('\n');
                            else if (n == 'r') sb.Append('\r');
                            else if (n == 't') sb.Append('\t');
                            else sb.Append(n);
                            continue;
                        }
                        if (ch == '"') break;
                        sb.Append(ch);
                    }
                    return sb.ToString();
                }
                inStr = true;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}') depth--;
            else if (c == '[') depth++;
            else if (c == ']') depth--;
        }
        return null;
    }

    static string DetectImageExt(byte[] bytes, string fallbackExt)
    {
        if (bytes != null && bytes.Length >= 12)
        {
            // GIF87a / GIF89a
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
                return ".gif";
            // PNG
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return ".png";
            // JPEG
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return ".jpg";
            // WEBP (RIFF....WEBP)
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return ".webp";
        }
        var ext = (fallbackExt ?? "").ToLowerInvariant();
        if (ext == ".jpeg") ext = ".jpg";
        if (ext == ".png" || ext == ".jpg" || ext == ".gif" || ext == ".webp") return ext;
        return null;
    }

    static bool WriteUserMedia(byte[] bytes, string preferredName, out string savedName, out string err)
    {
        savedName = null;
        err = "invalid";
        if (bytes == null || bytes.Length < 8)
        {
            err = "fichier trop petit";
            return false;
        }
        if (bytes.Length > 12000000)
        {
            err = "fichier trop grand (max 12 Mo)";
            return false;
        }
        var name = Path.GetFileName(string.IsNullOrEmpty(preferredName) ? "image.gif" : preferredName);
        var ext = DetectImageExt(bytes, Path.GetExtension(name));
        if (ext == null)
        {
            err = "format non supporté (PNG, JPG, GIF, WEBP)";
            return false;
        }
        var safe = "user-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" +
                   Guid.NewGuid().ToString("N").Substring(0, 8) + ext;
        try { Directory.CreateDirectory(UserAssetsDir); } catch { }
        var path = Path.Combine(UserAssetsDir, safe);
        try { File.WriteAllBytes(path, bytes); }
        catch
        {
            err = "écriture impossible";
            return false;
        }
        savedName = safe;
        err = null;
        return true;
    }

    static bool TrySaveMediaBinary(byte[] bytes, string filename, out string savedName, out string err)
    {
        return WriteUserMedia(bytes, filename, out savedName, out err);
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
        return WriteUserMedia(bytes, name, out savedName, out err);
    }

    static bool TryDeleteMedia(string file)
    {
        if (string.IsNullOrEmpty(file)) return false;
        file = Path.GetFileName(file);
        if (!file.StartsWith("user-", StringComparison.OrdinalIgnoreCase)) return false;
        var path = Path.Combine(UserAssetsDir, file);
        var legacy = Path.Combine(BuiltinAssetsDir, file);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(legacy)) File.Delete(legacy);
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

        // Flush coalesced mouse deltas off the hot Raw Input path
        SetTimer(hwnd, new UIntPtr(TIMER_MOUSE_FLUSH), MouseFlushMs, IntPtr.Zero);

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
        else if (msg == WM_TIMER && wParam.ToInt32() == TIMER_MOUSE_FLUSH) FlushPendingMouse();
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    static void HandleRawInput(IntPtr lParam)
    {
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, Marshal.SizeOf(typeof(RAWINPUTHEADER)));
        if (size == 0) return;
        if (size > RawBufCap)
        {
            if (RawBuf != IntPtr.Zero) Marshal.FreeHGlobal(RawBuf);
            RawBuf = Marshal.AllocHGlobal((int)size);
            RawBufCap = size;
        }
        var buf = RawBuf;
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

    static void FlushPendingMouse()
    {
        if (!HasPendingMouse) return;
        var dx = PendingDx;
        var dy = PendingDy;
        PendingDx = 0;
        PendingDy = 0;
        HasPendingMouse = false;
        if (dx == 0 && dy == 0) return;
        Broadcast("{\"event_type\":\"mouse_moved\",\"delta_x\":" + dx + ",\"delta_y\":" + dy + "}");
    }

    static void OnMouse(RAWMOUSE m)
    {
        // Accumulate moves; timer (or button/wheel) flushes one coalesced WS message
        if (m.lLastX != 0 || m.lLastY != 0)
        {
            PendingDx += m.lLastX;
            PendingDy += m.lLastY;
            HasPendingMouse = true;
        }

        var flags = m.usButtonFlags;
        if ((flags & RI_MOUSE_WHEEL) != 0)
        {
            FlushPendingMouse();
            var delta = (short)m.usButtonData;
            var rot = delta > 0 ? 1 : -1;
            Broadcast("{\"event_type\":\"mouse_wheel\",\"rotation\":" + rot + "}");
        }
        if ((flags & RI_MOUSE_LEFT_DOWN) != 0 || (flags & RI_MOUSE_LEFT_UP) != 0 ||
            (flags & RI_MOUSE_RIGHT_DOWN) != 0 || (flags & RI_MOUSE_RIGHT_UP) != 0)
        {
            FlushPendingMouse();
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
    [DllImport("user32.dll")]
    static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
}
