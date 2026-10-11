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

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

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
            public bool AudioIcon; // botao/icone de audio da aba reconhecido pela classe (Chromium, Vivaldi)
            public List<string> Buttons = new List<string>();
            public List<string> ButtonClasses = new List<string>();
        }

        // Nomes das abas do Spotify Web ("Musica • Artista") e do YouTube Music ("Musica | YouTube Music"), so no modo lista
        static List<string> bulletTabs = new List<string>();
        // Abas de outros sites tocando audio (para nunca confundir com a aba "YouTube Music" sem musica no nome)
        static int otherAudioTabs;

        class Candidate
        {
            public string Title;
            public bool Audio;
            public bool Music; // aba do YouTube Music
            public bool Selected; // aba ativa da janela
            public bool Home; // aba do YouTube Music sem a musica no nome (o site nao atualizou o titulo, ex.: depois de um anuncio)
            public IntPtr Window;
        }

        // Prefixo da resposta quando a aba tocando e do YouTube Music (o app trata separado do YouTube)
        public const string MusicPrefix = "M\t";
        // Linha da lista (--list) quando a unica aba tocando e a do YouTube Music sem a musica no nome
        public const string MusicHome = "!M";

        // App principal: roda o proprio exe com --read-tabs e le a resposta (ou null).
        // list = false: titulo do video do YouTube que esta tocando audio (janela anonima)
        // list = true: titulos de todas as abas do YouTube abertas, um por linha (confirmar que a midia e do YouTube)
        public static string ReadInChildProcess(string processName, bool list = false)
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--read-tabs " + processName + (list ? " --list" : ""))
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

        // Processo auxiliar: escreve a resposta (ver ReadInChildProcess)
        public static void Run(string processName, bool list)
        {
            string title = null;
            try { title = Find(processName, list); } catch { }
            var stdout = Console.OpenStandardOutput();
            var bytes = Encoding.UTF8.GetBytes(title ?? "");
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }

        static string Find(string processName, bool list)
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
            bulletTabs = new List<string>();
            otherAudioTabs = 0;
            bool anyTabs = false;
            foreach (var w in windows)
            {
                bool hasTabs;
                var found = FromWindow(w.Key, w.Value, processName, out hasTabs);
                if (found == null) return Retry;
                anyTabs |= hasTabs;
                candidates.AddRange(found);
            }
            if (!anyTabs) return NoTabs;

            // Unica aba tocando e a do YouTube Music sem a musica no nome: a midia e do YouTube Music
            var playing = candidates.FindAll(c => c.Audio);
            bool homeOnly = otherAudioTabs == 0 && playing.Count == 1 && playing[0].Home;

            if (list)
            {
                var titles = new StringBuilder();
                if (homeOnly)
                {
                    // Aba ativa e janela visivel: manda junto a musica da barra do player, para o app conferir (anuncio nao bate)
                    string bar = null;
                    if (playing[0].Selected && !IsIconic(playing[0].Window)) { bar = ""; YouTubeMusicPage(playing[0].Window, ref bar, new StringBuilder()); }
                    titles.Append(MusicHome);
                    // Visivel mas sem musica na barra do player (ex.: anuncio tocando): "?" = nao confirmado
                    if (bar != null) titles.Append('\t').Append(bar.Length > 0 ? bar : "?");
                    titles.Append('\n');
                }
                foreach (var c in candidates) if (!c.Home) titles.Append(c.Title).Append('\n');
                foreach (var n in bulletTabs) titles.Append('~').Append(n).Append('\n');
                return titles.ToString();
            }

            // So a aba do YouTube que esta tocando audio (tem o botao de silenciar, ativa ou nao).
            // Ambiguidade ou nenhuma tocando = nao mostra nada (privacidade: nunca mostra um video pausado
            // enquanto outro site toca)
            var audio = playing;
            if (audio.Count != 1 || (audio[0].Home && !homeOnly)) return null;
            if (!audio[0].Music) return audio[0].Title;
            // YouTube Music: o artista so aparece na pagina (barra do player), e so da para ler com a aba ativa e a janela visivel
            // Junto vem a fila "A seguir" (uma linha "musica<tab>artista" por item), para o app lembrar dos artistas
            // das proximas musicas quando a aba estiver em segundo plano
            string artist = null;
            var queue = new StringBuilder();
            string song = audio[0].Title;
            if (audio[0].Selected && !IsIconic(audio[0].Window)) artist = YouTubeMusicPage(audio[0].Window, ref song, queue);
            if (string.IsNullOrEmpty(song)) return null; // aba sem a musica no nome e pagina ilegivel (aba de fundo ou janela minimizada)
            return MusicPrefix + song + (string.IsNullOrEmpty(artist) ? "" : "\t" + artist) + queue;
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

        static List<Candidate> FromWindow(IntPtr hWnd, string windowTitle, string processName, out bool hasTabs)
        {
            var result = new List<Candidate>();
            var root = AutomationElement.FromHandle(hWnd);
            var tabCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
            var buttonCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            var tabs = new List<Tab>();
            // Janela minimizada: o Chrome/Edge marca as abas como "nao controle"; elas so aparecem na visao bruta
            // (as abas de dentro da pagina continuam visiveis, por isso a decisao e depois de filtra-las).
            // Na visao bruta os botoes das abas nao servem (todos aparecem), e a aba tocando e reconhecida pelo nome ("... - Audio playing")
            bool raw = false;
            bool vivaldi = string.Equals(processName, "vivaldi", StringComparison.OrdinalIgnoreCase);
            for (int pass = 0; pass < 2 && tabs.Count == 0; pass++)
            {
                raw = pass == 1;
                AutomationElementCollection items;
                if (!raw) items = root.FindAll(TreeScope.Descendants, tabCond);
                else
                {
                    var request = new CacheRequest { TreeFilter = Automation.RawViewCondition };
                    request.Add(AutomationElement.NameProperty);
                    using (request.Activate()) items = root.FindAll(TreeScope.Descendants, tabCond);
                }
                foreach (AutomationElement item in items)
                {
                    // Ignora abas que estao dentro da pagina (ex.: "A SEGUIR", "LETRA" do YouTube Music); so a barra do navegador
                    // (no Vivaldi a propria barra de abas e uma pagina: as abas do navegador ficam dentro de um documento)
                    if (InsidePage(item, root, raw, vivaldi ? 1 : 0)) continue;
                    var tab = new Tab { Name = item.Current.Name ?? "" };
                    object pattern;
                    if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                        tab.Selected = ((SelectionItemPattern)pattern).Current.IsSelected;
                    if (!raw)
                        foreach (AutomationElement b in item.FindAll(TreeScope.Descendants, buttonCond))
                        {
                            // Chrome/Edge/Brave/Opera: o botao de audio da aba tem esta classe em qualquer idioma
                            // (o Opera mostra so ele na aba ativa, sem o "fechar")
                            if ((b.Current.ClassName ?? "").IndexOf("AlertIndicator", StringComparison.Ordinal) >= 0) { tab.AudioIcon = true; continue; }
                            tab.Buttons.Add(b.Current.Name ?? "");
                            tab.ButtonClasses.Add(b.Current.ClassName ?? "");
                        }
                    if (vivaldi && !raw)
                        foreach (AutomationElement e in item.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                            if ((e.Current.ClassName ?? "").IndexOf("tab-audio", StringComparison.Ordinal) >= 0) { tab.AudioIcon = true; break; }
                    tabs.Add(tab);
                }
            }
            hasTabs = tabs.Count > 0;
            if (tabs.Count == 0) return result;
            string prefix = LearnPrefix(tabs, windowTitle);
            if (prefix != null && !ValidPrefix(prefix)) prefix = null; // titulo da janela atrasado em relacao a aba
            if (!string.IsNullOrEmpty(prefix)) SavePrefix(processName, prefix);
            else
            {
                // Incerto agora (aba recem-aberta): usa o texto aprendido antes para este navegador, se o nome da aba comecar com ele
                string saved = LoadPrefix(processName);
                if (!string.IsNullOrEmpty(saved) && ValidPrefix(saved) && tabs.Exists(t => t.Name.StartsWith(saved, StringComparison.Ordinal))) prefix = saved;
            }
            if (prefix == null)
            {
                // Incerto: so importa se houver aba do YouTube nesta janela
                if (tabs.Exists(t => t.Name.IndexOf(YouTubeMark, StringComparison.Ordinal) > 0 || t.Name.IndexOf(MaskedMedia.YouTubeMusicMark, StringComparison.Ordinal) > 0)) return null;
                prefix = "";
            }
            string closeName = CloseButtonName(tabs);
            foreach (var tab in tabs)
                if (tab.Name.IndexOf('\u2022') >= 0 || tab.Name.IndexOf(MaskedMedia.YouTubeMusicMark, StringComparison.Ordinal) >= 0)
                    bulletTabs.Add(tab.Name.Replace('\n', ' '));

            foreach (var tab in tabs)
            {
                int end = tab.Name.IndexOf(YouTubeMark, StringComparison.Ordinal);
                bool music = false;
                if (end <= 0) { end = tab.Name.IndexOf(MaskedMedia.YouTubeMusicMark, StringComparison.Ordinal); music = true; } // YouTube Music
                if (end <= 0)
                {
                    // Outra aba tocando audio: "YouTube Music" sem a musica no nome, ou outro site
                    int from = prefix.Length > 0 && tab.Name.StartsWith(prefix, StringComparison.Ordinal) ? prefix.Length : 0;
                    string rest = tab.Name.Substring(from);
                    if (!((!raw && (tab.AudioIcon || HasAudioButton(tab, closeName))) || AudioInName(rest))) continue;
                    if (IsYouTubeMusicHome(rest)) result.Add(new Candidate { Title = "", Audio = true, Music = true, Home = true, Selected = tab.Selected, Window = hWnd });
                    else otherAudioTabs++;
                    continue;
                }
                int start = prefix.Length > 0 && tab.Name.StartsWith(prefix, StringComparison.Ordinal) ? prefix.Length : 0;
                if (end <= start) continue;
                string title = NotificationCount.Replace(tab.Name.Substring(start, end - start), "").Trim();
                if (title.Length == 0) continue;
                // Aba tocando audio: tem um botao alem do "fechar" (o de silenciar a aba), em qualquer idioma,
                // ou o navegador escreve "Audio playing" no nome da aba
                bool audio = (!raw && (tab.AudioIcon || HasAudioButton(tab, closeName))) || AudioInName(tab.Name.Substring(end));
                result.Add(new Candidate { Title = title, Audio = audio, Music = music, Selected = tab.Selected, Window = hWnd });
            }
            return result;
        }

        // Nome da aba e "YouTube Music" (seguido so do texto de audio/memoria do navegador)
        static bool IsYouTubeMusicHome(string name)
        {
            const string Home = "YouTube Music";
            if (!name.StartsWith(Home, StringComparison.Ordinal)) return false;
            string after = name.Substring(Home.Length);
            return after.Length == 0 || after.StartsWith(" - ", StringComparison.Ordinal) || after.StartsWith(" \u2013 ", StringComparison.Ordinal)
                || after.StartsWith(": ", StringComparison.Ordinal) || after.StartsWith(" \u2014 ", StringComparison.Ordinal);
        }

        static bool HasClass(string cls, string a, string b)
        {
            return cls.IndexOf(a, StringComparison.Ordinal) >= 0 && cls.IndexOf(b, StringComparison.Ordinal) >= 0;
        }

        // Le a pagina do YouTube Music (aba ativa): devolve o artista da barra do player ("byline": "Artista • ...")
        // conferindo que a musica e a mesma, e escreve em queue a fila "A seguir" ("\nmusica\tartista" por item).
        // Os nomes de classe da pagina nao mudam com o idioma. Na primeira leitura o navegador pode ainda nao
        // expor o conteudo da pagina; por isso tenta duas vezes.
        static string YouTubeMusicPage(IntPtr hWnd, ref string title, StringBuilder queue)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    // Tudo de uma vez (tipo, classe e nome guardados na mesma consulta)
                    var request = new CacheRequest();
                    request.Add(AutomationElement.ControlTypeProperty);
                    request.Add(AutomationElement.ClassNameProperty);
                    request.Add(AutomationElement.NameProperty);
                    AutomationElementCollection all;
                    using (request.Activate()) all = AutomationElement.FromHandle(hWnd).FindAll(TreeScope.Descendants, Condition.TrueCondition);
                    int n = all.Count;
                    var types = new ControlType[n]; var classes = new string[n]; var names = new string[n];
                    for (int i = 0; i < n; i++)
                    {
                        types[i] = all[i].Cached.ControlType;
                        classes[i] = all[i].Cached.ClassName ?? "";
                        names[i] = (all[i].Cached.Name ?? "").Replace('\t', ' ').Replace('\n', ' ').Trim();
                    }

                    // Anuncio tocando (o player do YouTube ganha a classe "ad-showing"): a barra do player mostra o anuncio, nao a musica
                    for (int i = 0; i < n; i++)
                        if (classes[i].IndexOf("ad-showing", StringComparison.Ordinal) >= 0 || classes[i].IndexOf("ad-interrupting", StringComparison.Ordinal) >= 0)
                            return null;

                    // Fila: botao da capa, depois o texto da musica e o do artista (ou um grupo "byline" com varios artistas)
                    for (int i = 0; i + 2 < n; i++)
                    {
                        if (types[i] != ControlType.Button || classes[i].IndexOf("ytmusic-item-thumbnail-overlay-renderer", StringComparison.Ordinal) < 0) continue;
                        if (types[i + 1] != ControlType.Text || names[i + 1].Length == 0) continue;
                        string who = null;
                        if (types[i + 2] == ControlType.Text) who = names[i + 2];
                        else if (HasClass(classes[i + 2], "byline", "ytmusic-player-queue-item")) who = names[i + 2];
                        // Confirma que e um item da fila (o grupo da duracao vem logo depois)
                        bool inQueue = false;
                        for (int j = i + 2; j < n && j < i + 12 && !inQueue; j++) inQueue = HasClass(classes[j], "duration", "ytmusic-player-queue-item");
                        if (inQueue && !string.IsNullOrEmpty(who)) queue.Append('\n').Append(names[i + 1]).Append('\t').Append(who);
                    }

                    for (int i = 0; i < n; i++)
                    {
                        if (types[i] != ControlType.Group || !HasClass(classes[i], "byline", "ytmusic-player-bar")) continue;
                        // Confere o titulo da barra do player (classe "title ... ytmusic-player-bar"; no Firefox fica mais longe do byline)
                        for (int j = 0; j < n; j++)
                            if (classes[j].StartsWith("title ", StringComparison.Ordinal) && classes[j].IndexOf("ytmusic-player-bar", StringComparison.Ordinal) >= 0)
                            {
                                // Sem a musica no nome da aba: usa a da barra do player
                                if (string.IsNullOrEmpty(title)) title = names[j];
                                else if (!string.Equals(names[j], title.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
                                break;
                            }
                        string artist = names[i];
                        int dot = artist.IndexOf(" • ", StringComparison.Ordinal);
                        if (dot > 0) artist = artist.Substring(0, dot);
                        else
                        {
                            // Sem o "•": primeiro link dentro do byline
                            for (int j = i + 1; j < n && j < i + 6; j++)
                                if (types[j] == ControlType.Hyperlink && names[j].Length > 0) { artist = names[j]; break; }
                        }
                        artist = artist.Trim();
                        return artist.Length > 0 ? artist : null;
                    }
                    queue.Length = 0;
                }
                catch { queue.Length = 0; }
                System.Threading.Thread.Sleep(400);
            }
            return null;
        }

        // Texto que o navegador poe antes do titulo da aba, aprendido com seguranca e guardado por navegador
        const string PrefixKey = @"Software\NowWatching\TabPrefix";

        static string LoadPrefix(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return null;
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PrefixKey)) return k == null ? null : k.GetValue(processName) as string; }
            catch { return null; }
        }

        // O prefixo e um texto fixo do navegador ("Uso da memoria em "); nunca parte do titulo de uma pagina
        static bool ValidPrefix(string prefix)
        {
            return prefix.Length <= 60 && prefix.IndexOf("YouTube", StringComparison.OrdinalIgnoreCase) < 0
                && prefix.IndexOf(" | ", StringComparison.Ordinal) < 0
                && prefix.IndexOf('\u2022') < 0;
        }

        static void SavePrefix(string processName, string prefix)
        {
            if (string.IsNullOrEmpty(processName) || LoadPrefix(processName) == prefix) return;
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PrefixKey)) k.SetValue(processName, prefix); }
            catch { }
        }

        // Textos "<aba> - Audio playing" do Chrome/Edge/Brave em todos os idiomas (extraidos dos arquivos de idioma
        // dos navegadores); guardamos so o trecho depois do titulo da aba
        static List<string> audioMarks;

        static bool AudioInName(string nameAfterTitle)
        {
            if (audioMarks == null)
            {
                var marks = new List<string>();
                using (var s = typeof(TabReader).Assembly.GetManifestResourceStream("NowWatching.tab-audio.txt"))
                using (var r = new System.IO.StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        int p = line.IndexOf("$1", StringComparison.Ordinal);
                        if (p < 0) continue;
                        string mark = line.Substring(p + 2).TrimEnd();
                        if (mark.Trim().Length >= 4 && !marks.Contains(mark)) marks.Add(mark);
                    }
                }
                audioMarks = marks;
            }
            return audioMarks.Exists(m => nameAfterTitle.IndexOf(m, StringComparison.Ordinal) >= 0);
        }

        // A aba esta dentro do conteudo da pagina (algum ancestral e um documento web)?
        // allowedDocuments: documentos que fazem parte do proprio navegador (Vivaldi: 1)
        static bool InsidePage(AutomationElement item, AutomationElement root, bool raw, int allowedDocuments)
        {
            var walker = raw ? TreeWalker.RawViewWalker : TreeWalker.ControlViewWalker;
            var e = walker.GetParent(item);
            int documents = 0;
            for (int depth = 0; e != null && depth < 40; depth++)
            {
                if (e.Current.ControlType == ControlType.Document && ++documents > allowedDocuments) return true;
                if (Automation.Compare(e, root)) return false;
                e = walker.GetParent(e);
            }
            return false;
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
                    if (p < 0) continue;
                    string found = selected.Name.Substring(0, p);
                    // O prefixo e o mesmo em todas as abas com texto de memoria; se outra nao comecar com ele,
                    // o titulo da janela estava atrasado (pagina carregando): incerto
                    if (tabs.Exists(t => t != selected && MemoryTail.IsMatch(t.Name) && !t.Name.StartsWith(found, StringComparison.Ordinal))) return null;
                    return found;
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

        // Botao alem do "fechar": pela classe quando o navegador informa ("...Close..." = fechar), senao pelo nome
        static bool HasAudioButton(Tab tab, string closeName)
        {
            for (int i = 0; i < tab.Buttons.Count; i++)
            {
                string cls = tab.ButtonClasses[i];
                if (cls.Length > 0 ? cls.IndexOf("close", StringComparison.OrdinalIgnoreCase) < 0 : tab.Buttons[i] != closeName) return true;
            }
            return false;
        }

        // O botao "fechar" e o que aparece na aba ativa (ou o nome de botao mais comum entre as abas)
        static string CloseButtonName(List<Tab> tabs)
        {
            // Pela classe, quando o navegador informa (Firefox: "tab-close-button")
            foreach (var t in tabs)
                for (int i = 0; i < t.Buttons.Count; i++)
                    if (t.ButtonClasses[i].IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0) return t.Buttons[i];
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
