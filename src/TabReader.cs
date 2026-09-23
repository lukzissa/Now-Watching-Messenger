// Janela anonima: o navegador esconde o titulo da midia. Lemos as abas pela acessibilidade do Windows
// (UI Automation), como um leitor de tela, e escolhemos a aba do YouTube que esta tocando audio
// (ativa ou em segundo plano, em qualquer janela do navegador).
// Roda so no processo auxiliar (--read-tabs), para o app principal nao carregar UI Automation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Forms;

namespace NowWatching
{
    static class TabReader
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

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        const string YouTubeMark = " - YouTube";
        // Resposta quando a leitura ainda e incerta (aba recem-aberta): o app tenta de novo em seguida
        public const string Retry = "?RETRY";
        // Resposta quando o navegador nao expoe as abas pela acessibilidade: o app usa o titulo da janela
        public const string NoTabs = "?NOTABS";
        // Chrome/Edge acrescentam o uso de memoria no fim do nome da aba (": 488 MB", " - 517 MB"), em qualquer idioma
        static readonly Regex MemoryTail = new Regex(@"\d[\d.,\u00a0 ]*\s?\S{1,3}$");
        static readonly Regex NotificationCount = new Regex(@"^\(\d+\+?\)\s*");

        class Tab
        {
            public string Name;
            public bool Selected;
            public List<string> Buttons = new List<string>();
        }

        class Candidate
        {
            public string Title;
            public bool Audio;
        }

        // App principal: roda o proprio exe com --read-tabs e le o titulo encontrado (ou null)
        public static string ReadInChildProcess(string processName)
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--read-tabs " + processName)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                string output;
                using (var p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return null; }
                }
                output = output.Trim();
                return output.Length == 0 ? null : output;
            }
            catch { return null; }
        }

        // Processo auxiliar: escreve o titulo do video do YouTube que esta tocando em aba de fundo
        public static void Run(string processName)
        {
            string title = null;
            try { title = Find(processName); } catch { }
            var stdout = Console.OpenStandardOutput();
            var bytes = Encoding.UTF8.GetBytes(title ?? "");
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }

        static string Find(string processName)
        {
            var windows = new List<KeyValuePair<IntPtr, string>>();
            var text = new StringBuilder(1024);
            var cls = new StringBuilder(64);
            EnumProc callback = (h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                cls.Length = 0;
                GetClassName(h, cls, cls.Capacity);
                string c = cls.ToString();
                if (c != "Chrome_WidgetWin_1" && c != "MozillaWindowClass") return true;
                if (!BelongsTo(h, processName)) return true;
                text.Length = 0;
                GetWindowText(h, text, text.Capacity);
                if (text.Length > 0) windows.Add(new KeyValuePair<IntPtr, string>(h, text.ToString()));
                return true;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);

            var candidates = new List<Candidate>();
            bool anyTabs = false;
            foreach (var w in windows)
            {
                bool hasTabs;
                var found = FromWindow(w.Key, w.Value, out hasTabs);
                if (found == null) return Retry;
                anyTabs |= hasTabs;
                candidates.AddRange(found);
            }
            if (!anyTabs) return NoTabs;

            // Prefere a aba que esta tocando audio (tem o botao de silenciar); ambiguidade = nao mostra nada
            var audio = candidates.FindAll(c => c.Audio);
            if (audio.Count == 1) return audio[0].Title;
            if (audio.Count == 0 && candidates.Count == 1) return candidates[0].Title;
            return null;
        }

        static bool BelongsTo(IntPtr hWnd, string processName)
        {
            if (string.IsNullOrEmpty(processName)) return true;
            try
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                using (var p = Process.GetProcessById((int)pid))
                    return string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static List<Candidate> FromWindow(IntPtr hWnd, string windowTitle, out bool hasTabs)
        {
            var result = new List<Candidate>();
            var root = AutomationElement.FromHandle(hWnd);
            var items = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            hasTabs = items.Count > 0;
            if (items.Count == 0) return result;

            var tabs = new List<Tab>();
            var buttonCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            foreach (AutomationElement item in items)
            {
                var tab = new Tab { Name = item.Current.Name ?? "" };
                object pattern;
                if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                    tab.Selected = ((SelectionItemPattern)pattern).Current.IsSelected;
                foreach (AutomationElement b in item.FindAll(TreeScope.Descendants, buttonCond))
                    tab.Buttons.Add(b.Current.Name ?? "");
                tabs.Add(tab);
            }

            string prefix = LearnPrefix(tabs, windowTitle);
            if (prefix == null)
            {
                // Incerto: so importa se houver aba do YouTube nesta janela
                if (tabs.Exists(t => t.Name.IndexOf(YouTubeMark, StringComparison.Ordinal) > 0)) return null;
                prefix = "";
            }
            string closeName = CloseButtonName(tabs);

            foreach (var tab in tabs)
            {
                int end = tab.Name.IndexOf(YouTubeMark, StringComparison.Ordinal);
                if (end <= 0) continue;
                int start = prefix.Length > 0 && tab.Name.StartsWith(prefix, StringComparison.Ordinal) ? prefix.Length : 0;
                if (end <= start) continue;
                string title = NotificationCount.Replace(tab.Name.Substring(start, end - start), "").Trim();
                if (title.Length == 0) continue;
                // Aba tocando audio: tem um botao alem do "fechar" (o de silenciar a aba), em qualquer idioma
                bool audio = tab.Buttons.Exists(n => n != closeName);
                result.Add(new Candidate { Title = title, Audio = audio });
            }
            return result;
        }

        // Chrome/Brave poem um texto antes do titulo no nome da aba ("Uso da memoria em ..."), que muda por idioma.
        // Descobre esse texto comparando a aba ativa com o titulo da janela (que comeca com o titulo da aba ativa).
        // Devolve null quando ainda nao da para saber (aba recem-aberta, sem o texto de memoria).
        static string LearnPrefix(List<Tab> tabs, string windowTitle)
        {
            var selected = tabs.Find(t => t.Selected);
            bool anyMemory = tabs.Exists(t => MemoryTail.IsMatch(t.Name));
            if (selected != null && (!anyMemory || MemoryTail.IsMatch(selected.Name)))
            {
                for (int len = windowTitle.Length; len >= 4; len--)
                {
                    int p = selected.Name.IndexOf(windowTitle.Substring(0, len), StringComparison.Ordinal);
                    if (p >= 0) return selected.Name.Substring(0, p);
                }
            }
            // Alternativa: parte inicial comum as abas com texto de memoria, cortada no ultimo espaco
            var decorated = anyMemory ? tabs.FindAll(t => MemoryTail.IsMatch(t.Name)) : tabs;
            if (decorated.Count < 2) return anyMemory ? null : "";
            string common = decorated[0].Name;
            foreach (var t in decorated)
            {
                int i = 0;
                while (i < common.Length && i < t.Name.Length && common[i] == t.Name[i]) i++;
                common = common.Substring(0, i);
            }
            int space = common.LastIndexOf(' ');
            return space > 0 ? common.Substring(0, space + 1) : "";
        }

        // O botao "fechar" e o que aparece na aba ativa (ou o nome de botao mais comum entre as abas)
        static string CloseButtonName(List<Tab> tabs)
        {
            var selected = tabs.Find(t => t.Selected);
            if (selected != null && selected.Buttons.Count == 1) return selected.Buttons[0];
            var counts = new Dictionary<string, int>();
            string best = null;
            foreach (var t in tabs)
                foreach (var b in t.Buttons)
                {
                    int n;
                    counts.TryGetValue(b, out n);
                    counts[b] = ++n;
                    if (best == null || n > counts[best]) best = b;
                }
            return best;
        }
    }
}
