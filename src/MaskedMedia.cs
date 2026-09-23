// Janela anonima/InPrivate: o navegador troca o titulo da midia por um texto generico
// ("Um site reproduzindo midia"). Aqui detectamos esse texto e buscamos o titulo pela janela.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace NowWatching
{
    static class MaskedMedia
    {
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

        // Textos genericos do Edge em todos os idiomas (extraidos dos arquivos de idioma do navegador)
        static HashSet<string> placeholders;

        public static bool IsPlaceholder(string title)
        {
            if (placeholders == null)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var s = typeof(MaskedMedia).Assembly.GetManifestResourceStream("NowWatching.media-placeholders.txt"))
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                        if (line.Trim().Length > 0) set.Add(line.Trim());
                }
                placeholders = set;
            }
            return placeholders.Contains(title.Trim());
        }

        static readonly Regex NotificationCount = new Regex(@"^\(\d+\+?\)\s*");

        // Procura uma janela de navegador cuja aba ativa seja um video do YouTube ("Titulo - YouTube ...")
        public static string YouTubeTitleFromWindows()
        {
            string found = null;
            var text = new StringBuilder(1024);
            var cls = new StringBuilder(64);
            EnumProc callback = (h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                cls.Length = 0;
                GetClassName(h, cls, cls.Capacity);
                string c = cls.ToString();
                if (c != "Chrome_WidgetWin_1" && c != "MozillaWindowClass") return true;

                text.Length = 0;
                GetWindowText(h, text, text.Capacity);
                string t = text.ToString();
                int i = t.IndexOf(" - YouTube", StringComparison.Ordinal);
                if (i <= 0) return true;

                found = NotificationCount.Replace(t.Substring(0, i), "").Trim();
                return found.Length == 0; // continua procurando se o titulo ficou vazio
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return string.IsNullOrEmpty(found) ? null : found;
        }
    }
}
