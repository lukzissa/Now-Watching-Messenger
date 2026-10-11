// Now Watching Messenger (reescrita para Windows 10/11)
// Mostra o video do YouTube e/ou a musica do Spotify no status "o que estou ouvindo"
// do Windows Live Messenger 2009, usando os controles de midia do sistema (GSMTC).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Media.Control;

[assembly: System.Reflection.AssemblyTitle("Now Watching Messenger")]
[assembly: System.Reflection.AssemblyProduct("Now Watching Messenger")]
[assembly: System.Reflection.AssemblyCompany("Lucas Issa")]
[assembly: System.Reflection.AssemblyCopyright("Freeware - Lucas Issa")]
[assembly: System.Reflection.AssemblyVersion("1.5.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.5.0.0")]

namespace NowWatching
{
    static class Program
    {
        public const string Name = "Now Watching Messenger";
        public const string Version = "1.5";

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // Modo auxiliar: consulta o GitHub, escreve o resultado e sai (processo de vida curta)
            if (Array.IndexOf(args, "--check-update") >= 0)
            {
                Updater.WriteCheckResult();
                return;
            }

            // Modo auxiliar: le as abas do navegador pela acessibilidade do Windows e sai
            int readTabs = Array.IndexOf(args, "--read-tabs");
            if (readTabs >= 0)
            {
                TabReader.Run(readTabs + 1 < args.Length ? args[readTabs + 1] : null, Array.IndexOf(args, "--list") >= 0);
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "NowWatching_WLM_SingleInstance", out created))
            {
                // Recem-atualizado: espera a versao antiga terminar de fechar
                if (!created && Array.IndexOf(args, "--updated") >= 0)
                {
                    try { created = mutex.WaitOne(15000); }
                    catch (AbandonedMutexException) { created = true; }
                }
                if (!created) return;
                Updater.CleanupAsync();
                // Se o exe foi renomeado/movido, atualiza o caminho do "Iniciar com o Windows"
                try { if (Settings.Startup) Settings.Startup = true; } catch { }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }

        public static Icon LoadIcon(Size size)
        {
            using (var s = typeof(Program).Assembly.GetManifestResourceStream("NowWatching.icon.ico"))
                return new Icon(s, size);
        }

        public static Image LoadAboutImage()
        {
            using (var s = typeof(Program).Assembly.GetManifestResourceStream("NowWatching.about.png"))
                return new Bitmap(Image.FromStream(s));
        }
    }

    static class Settings
    {
        const string AppKey = @"Software\NowWatching";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static volatile bool Enabled = GetBool("Enabled", true);
        public static volatile bool YouTube = GetBool("YouTube", true);
        public static volatile bool YouTubeMusic = GetBool("YouTubeMusic", true);
        public static volatile bool Spotify = GetBool("Spotify", true);
        public static volatile bool ShowChannel = GetBool("ShowChannel", true);
        public static string Language = GetString("Language", Strings.DefaultLanguage());

        static bool GetBool(string name, bool def)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(AppKey))
            {
                object v = k == null ? null : k.GetValue(name);
                return v == null ? def : Convert.ToInt32(v) != 0;
            }
        }

        static string GetString(string name, string def)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(AppKey))
            {
                string v = k == null ? null : k.GetValue(name) as string;
                return string.IsNullOrEmpty(v) ? def : v;
            }
        }

        public static void Save()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(AppKey))
            {
                k.SetValue("Enabled", Enabled ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("YouTube", YouTube ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("YouTubeMusic", YouTubeMusic ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("Spotify", Spotify ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("ShowChannel", ShowChannel ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("Language", Language);
            }
        }

        public static bool Startup
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("NowWatching") != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("NowWatching", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("NowWatching", false);
                }
            }
        }
    }

    static class Messenger
    {
        const int WM_COPYDATA = 0x004A;
        const uint SMTO_ABORTIFHUNG = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam,
            uint flags, uint timeout, out IntPtr result);

        public static List<IntPtr> Windows()
        {
            var list = new List<IntPtr>();
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, "MsnMsgrUIManager", null)) != IntPtr.Zero)
                list.Add(h);
            return list;
        }

        static string Clean(string s)
        {
            // "\0" e o separador do protocolo; evita que barras no titulo quebrem o campo
            return (s ?? "").Replace("\\", "/").Trim();
        }

        // Formato do protocolo: \0Categoria\0Ativo\0Formato\0Campo0\0Campo1\0Campo2\0WMContentID\0
        public static void SetStatus(string title, string artist)
        {
            // Alguns clientes (lado do contato / Messenger Plus!) ignoram o formato "{0}" e sempre exibem
            // "Campo0 - Campo1", deixando um "-" sobrando quando nao ha artista. Se o titulo ja tiver " - ",
            // divide em dois campos: o texto exibido fica identico ao titulo original em qualquer cliente.
            if (!string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
            {
                int sep = title.IndexOf(" - ", StringComparison.Ordinal);
                if (sep > 0 && sep + 3 < title.Length)
                {
                    artist = title.Substring(sep + 3);
                    title = title.Substring(0, sep);
                }
            }

            string payload;
            if (string.IsNullOrEmpty(title))
                payload = "\\0Music\\00\\0\\0\\0\\0\\0\\0";
            else if (string.IsNullOrEmpty(artist))
                // Titulo no Campo1 com formato "{1}": unico jeito testado que o lado do contato exibe sem "-" no final
                payload = "\\0Music\\01\\0{1}\\0\\0" + Clean(title) + "\\0\\0\\0";
            else
                payload = "\\0Music\\01\\0{0} - {1}\\0" + Clean(title) + "\\0" + Clean(artist) + "\\0\\0\\0";

            IntPtr buf = Marshal.StringToHGlobalUni(payload);
            try
            {
                var cds = new COPYDATASTRUCT { dwData = (IntPtr)0x547, cbData = (payload.Length + 1) * 2, lpData = buf };
                foreach (var h in Windows())
                {
                    IntPtr res;
                    SendMessageTimeout(h, WM_COPYDATA, IntPtr.Zero, ref cds, SMTO_ABORTIFHUNG, 1000, out res);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    class TrayApp : ApplicationContext
    {
        enum Source { None, YouTube, Spotify }

        static readonly string[] Browsers = { "msedge", "chrome", "firefox", "308046b0af4a39cb", "opera", "brave", "vivaldi", "yandex" };

        readonly NotifyIcon tray;
        readonly ToolStripMenuItem miNow, miEnabled, miSources, miYouTube, miYouTubeMusic, miSpotify, miPrefs, miExit;
        readonly System.Threading.Timer timer;
        readonly SynchronizationContext ui;
        GlobalSystemMediaTransportControlsSessionManager manager;
        PreferencesForm prefsForm;

        volatile bool dirty = true;
        string lastTitle, lastArtist, lastWindows = "", label = null;
        int busy;

        public TrayApp()
        {
            ui = new WindowsFormsSynchronizationContext();

            miNow = new ToolStripMenuItem { Enabled = false };
            miEnabled = new ToolStripMenuItem("", null, (s, e) => { Settings.Enabled = !Settings.Enabled; Changed(); });
            miSources = new ToolStripMenuItem { Enabled = false };
            miYouTube = new ToolStripMenuItem("", null, (s, e) => { Settings.YouTube = !Settings.YouTube; Changed(); });
            miYouTubeMusic = new ToolStripMenuItem("", null, (s, e) => { Settings.YouTubeMusic = !Settings.YouTubeMusic; Changed(); });
            miSpotify = new ToolStripMenuItem("", null, (s, e) => { Settings.Spotify = !Settings.Spotify; Changed(); });
            miPrefs = new ToolStripMenuItem("", null, (s, e) => ShowPreferences());
            miExit = new ToolStripMenuItem("", null, (s, e) => Quit());
            using (var small = Program.LoadIcon(new Size(16, 16)))
                miPrefs.Image = small.ToBitmap();
            miSources.Font = new Font(miSources.Font, FontStyle.Bold);

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[] {
                miNow, new ToolStripSeparator(),
                miEnabled, new ToolStripSeparator(),
                miSources, miYouTube, miYouTubeMusic, miSpotify, new ToolStripSeparator(),
                miPrefs, new ToolStripSeparator(),
                miExit });

            tray = new NotifyIcon { Icon = Program.LoadIcon(SystemInformation.SmallIconSize), ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowPreferences(); };

            ApplyTexts();
            timer = new System.Threading.Timer(_ => Poll(), null, 500, 2000);

            // Uma unica verificacao de nova versao, 5 s apos abrir, em um processo auxiliar
            // (assim o app principal nao carrega as bibliotecas de internet na memoria)
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(5000);
                var release = Updater.CheckInChildProcess();
                if (release != null) ui.Post(__ => new UpdateForm(release, Quit).Show(), null);
            });
        }

        // Chamado quando qualquer configuracao muda (menu ou preferencias)
        public void Changed()
        {
            Settings.Save();
            ApplyTexts();
            dirty = true;
            ThreadPool.QueueUserWorkItem(_ => Poll());
        }

        void ApplyTexts()
        {
            var t = Strings.Current;
            miEnabled.Text = t.ShowInMessenger;
            miEnabled.Checked = Settings.Enabled;
            miSources.Text = t.Sources;
            miYouTube.Text = "    " + t.YouTube;
            miYouTube.Checked = Settings.YouTube;
            miYouTubeMusic.Text = "    " + t.YouTubeMusic;
            miYouTubeMusic.Checked = Settings.YouTubeMusic;
            miSpotify.Text = "    " + t.Spotify;
            miSpotify.Checked = Settings.Spotify;
            miPrefs.Text = t.PreferencesMenu;
            miExit.Text = t.Exit;
            UpdateLabel(label);
        }

        static Source Classify(string appId)
        {
            appId = (appId ?? "").ToLowerInvariant();
            if (appId.Contains("spotify")) return Source.Spotify;
            foreach (var b in Browsers) if (appId.Contains(b)) return Source.YouTube;
            return Source.None;
        }

        // Ultima midia lida (titulo/artista so sao buscados de novo quando algo muda)
        string mediaKey, mediaTitle, mediaArtist;
        long mediaEnd;
        double mediaPos;
        DateTime mediaFetched = DateTime.MinValue, mediaNewAt = DateTime.MinValue;
        static readonly TimeSpan MediaRefresh = TimeSpan.FromSeconds(30);

        // Titulo lido das abas (janela anonima com a aba do YouTube em segundo plano)
        string tabKey, tabTitle, tabIdent;

        // Artista de cada musica do YouTube Music vista na janela anonima (lido da pagina quando a aba estava ativa)
        readonly Dictionary<string, string> musicArtists = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Reproducao ja confirmada como YouTube/YouTube Music/Spotify Web neste navegador: com a janela minimizada o
        // navegador nao mostra as abas, entao as proximas midias (ex.: proximo video do Mix) continuam valendo
        // enquanto a reproducao nao parar
        string contApp;
        Site contSite;
        DateTime tabFetched = DateTime.MinValue, tabStarted = DateTime.MinValue;
        bool tabRetry;

        // Privacidade: no navegador so o YouTube e o Spotify Web contam. Resultado da ultima confirmacao (navegador + titulo)
        // Pending: reproducao ja confirmada trocou de faixa, mas o nome da aba ainda nao mudou (confere de novo no proximo ciclo)
        enum Site { Other, YouTube, YouTubeMusic, SpotifyWeb, Pending }
        string siteKey, pendingKey;
        DateTime pendingSince = DateTime.MinValue;
        Site siteResult;
        DateTime siteChecked = DateTime.MinValue;
        // Sessao de outro site (navegador + duracao) que o ciclo deve pular, para mostrar outra midia (ex.: Spotify)
        string skipSession;
        DateTime skipUntil = DateTime.MinValue;

        // Descobre de que site e a midia do navegador (o Windows nao informa o site):
        // - YouTube: aba "<mesmo titulo> - YouTube"; YouTube Music: aba "<musica> | YouTube Music";
        // - Spotify Web: aba "<musica> • <artista>" tocando
        // Primeiro pelo titulo das janelas (rapido); se a aba estiver em segundo plano, pelas abas (processo auxiliar).
        // Resultado guardado; outros sites sao conferidos de novo a cada 30 s.
        Site CheckSite(string appId, string title)
        {
            string key = appId + "|" + title;
            if (key == siteKey && (siteResult != Site.Other || DateTime.UtcNow - siteChecked < OtherRecheck())) return siteResult;
            string proc = BrowserProcess(appId);
            Site site = MaskedMedia.YouTubeMusicWindowHas(proc, title) ? Site.YouTubeMusic
                : MaskedMedia.YouTubeWindowHas(proc, title) ? Site.YouTube
                : MaskedMedia.SpotifyWebWindowHas(proc, title) ? Site.SpotifyWeb : Site.Other;
            if (site == Site.Other)
            {
                string list = TabReader.ReadInChildProcess(proc, true);
                if (list == TabReader.Retry) return Site.Other; // aba recem-aberta: confere de novo no proximo ciclo
                // Janela minimizada (o navegador nao mostra as abas): mantem o site ja confirmado nesta reproducao
                if (list == TabReader.NoTabs && contApp == appId && contSite != Site.Other)
                {
                    siteKey = key; siteResult = contSite; siteChecked = DateTime.UtcNow;
                    return contSite;
                }
                if (list != null && list != TabReader.NoTabs)
                {
                    var lines = list.Split('\n');
                    // YouTube Music e Spotify Web primeiro (o titulo da musica tambem aparece na lista do YouTube)
                    foreach (var line in lines)
                        if (line.StartsWith("~"))
                        {
                            if (MaskedMedia.IsYouTubeMusicName(line, title)) { site = Site.YouTubeMusic; break; }
                            if (MaskedMedia.IsSpotifyWebName(line, title)) { site = Site.SpotifyWeb; break; }
                        }
                    if (site == Site.Other)
                        foreach (var line in lines)
                            if (!line.StartsWith("~") && !line.StartsWith(TabReader.MusicHome) && MaskedMedia.SameTitle(line, title)) { site = Site.YouTube; break; }
                    // A unica aba tocando e a do YouTube Music, mas o site ainda nao pos a musica no nome da aba (ex.: depois de um anuncio)
                    if (site == Site.Other)
                        foreach (var line in lines)
                            if (line == TabReader.MusicHome || line.StartsWith(TabReader.MusicHome + "\t"))
                            {
                                // Com a musica da barra do player (aba visivel), ela tem que ser a mesma (um anuncio nao bate)
                                string bar = line.Length > TabReader.MusicHome.Length ? line.Substring(TabReader.MusicHome.Length + 1) : null;
                                // Sem como conferir (aba de fundo/janela minimizada): so se esta reproducao ja foi confirmada
                                // como YouTube Music antes (senao poderia ser um anuncio)
                                if (bar == null ? (contApp == appId && contSite == Site.YouTubeMusic) : (bar != "?" && MaskedMedia.SameTitle(bar, title)))
                                    site = Site.YouTubeMusic;
                                break;
                            }
                }
            }
            if (site == Site.Other && contApp == appId && contSite != Site.Other)
            {
                // Mesma reproducao (sem parar) com outra faixa: o navegador as vezes atualiza o nome da aba
                // depois da midia; espera ate 10 s antes de tratar como outro site
                if (pendingKey != key) { pendingKey = key; pendingSince = DateTime.UtcNow; }
                if (DateTime.UtcNow - pendingSince < TimeSpan.FromSeconds(10)) return Site.Pending;
            }
            siteKey = key; siteResult = site; siteChecked = DateTime.UtcNow;
            if (site != Site.Other) { contApp = appId; contSite = site; }
            return site;
        }

        // Videos no YouTube Music vem como "Artista - Musica (Official Video)": tira o artista repetido do titulo
        static string WithoutArtist(string song, string artist)
        {
            string lead = artist.Trim() + " - ";
            return song.StartsWith(lead, StringComparison.OrdinalIgnoreCase) && song.Length > lead.Length ? song.Substring(lead.Length) : song;
        }

        // "Outro site" e conferido de novo a cada 30 s; nos primeiros 40 s de uma midia nova, a cada 3 s
        // (o navegador as vezes informa a midia antes de atualizar o nome da aba)
        TimeSpan OtherRecheck()
        {
            return DateTime.UtcNow - mediaNewAt < TimeSpan.FromSeconds(40) ? TimeSpan.FromSeconds(3) : MediaRefresh;
        }

        // Nome do processo do navegador a partir do id da sessao de midia
        static string BrowserProcess(string appId)
        {
            appId = (appId ?? "").ToLowerInvariant();
            if (appId.Contains("msedge")) return "msedge";
            if (appId.Contains("firefox") || appId.Contains("308046b0af4a39cb")) return "firefox";
            if (appId.Contains("brave")) return "brave";
            if (appId.Contains("opera")) return "opera";
            if (appId.Contains("vivaldi")) return "vivaldi";
            if (appId.Contains("yandex")) return "browser";
            if (appId.Contains("chrome")) return "chrome";
            return null;
        }

        // Aba anonima/privativa: o navegador esconde o titulo ("Um site reproduzindo midia") e nunca informa artista.
        // Video normal do YouTube sempre traz o canal como artista, entao "sem artista" indica midia escondida.
        // Le as abas pela acessibilidade (processo auxiliar) e usa a aba do YouTube que toca audio, ativa ou em
        // segundo plano; se nao achar, nao mostra nada (nenhum texto generico chega ao Messenger). So roda quando
        // a midia muda (ou a cada 30 s), para nao pesar.
        string MaskedTitle(string appId, bool changed)
        {
            string proc = BrowserProcess(appId);
            // Midia nova (navegador + duracao): recomeca a janela de tentativas rapidas
            string ident = appId + "|" + mediaEnd;
            if (ident != tabIdent) { tabIdent = ident; tabStarted = DateTime.UtcNow; tabRetry = true; }
            if (changed || tabKey != appId || tabRetry || DateTime.UtcNow - tabFetched > MediaRefresh)
            {
                string read = TabReader.ReadInChildProcess(proc);
                // YouTube Music: as linhas seguintes sao a fila "A seguir" (musica<tab>artista); guarda os artistas para
                // quando essas musicas tocarem com a aba em segundo plano
                if (read != null && read.StartsWith(TabReader.MusicPrefix) && read.IndexOf('\n') > 0)
                {
                    var lines = read.Split('\n');
                    read = lines[0];
                    if (musicArtists.Count > 300) musicArtists.Clear();
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int tab = lines[i].IndexOf('\t');
                        if (tab > 0 && tab < lines[i].Length - 1) musicArtists[lines[i].Substring(0, tab)] = lines[i].Substring(tab + 1);
                    }
                }
                // Aba recem-aberta (leitura incerta, indicador de audio ainda nao apareceu, ou artista do YouTube Music
                // ainda nao lido nem lembrado): tenta de novo no proximo ciclo (2 s) durante os primeiros 40 s da midia (o Opera demora mais); depois so a cada 30 s
                bool musicNoArtist = read != null && read.StartsWith(TabReader.MusicPrefix) && read.IndexOf('\t', TabReader.MusicPrefix.Length) < 0
                    && !musicArtists.ContainsKey(read.Substring(TabReader.MusicPrefix.Length));
                tabRetry = (read == TabReader.Retry || read == null || musicNoArtist) && DateTime.UtcNow - tabStarted < TimeSpan.FromSeconds(40);
                if (read == TabReader.NoTabs)
                    tabTitle = MaskedMedia.YouTubeTitleFromWindows(proc); // navegador sem abas acessiveis
                else if (read != TabReader.Retry)
                    tabTitle = read;
                tabKey = appId;
                tabFetched = DateTime.UtcNow;
            }
            return tabTitle;
        }

        void Poll()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                if (manager == null)
                    manager = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

                // Passo leve (a cada 2 s): acha a sessao tocando so pelo estado de reproducao
                GlobalSystemMediaTransportControlsSession playing = null;
                Source found = Source.None;
                string appId = null;
                var sessions = manager.GetSessions();
                try
                {
                    int count = sessions.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var session = sessions[i];
                        bool keep = false;
                        try
                        {
                            string id = session.SourceAppUserModelId;
                            var src = Classify(id);
                            if (src == Source.None) continue;
                            if (src == Source.YouTube && !Settings.YouTube && !Settings.YouTubeMusic && !Settings.Spotify) continue; // navegador: YouTube, YouTube Music ou Spotify Web
                            if (src == Source.Spotify && !Settings.Spotify) continue;

                            var info = session.GetPlaybackInfo();
                            bool isPlaying = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                            Release(info);
                            if (!isPlaying) continue;

                            // Pula a midia de outro site (nao YouTube) que ja foi conferida, para achar outra midia
                            if (src == Source.YouTube && skipSession != null && DateTime.UtcNow < skipUntil && skipSession.StartsWith(id + "|", StringComparison.Ordinal))
                            {
                                var stl = session.GetTimelineProperties();
                                long sEnd = stl == null ? 0 : stl.EndTime.Ticks;
                                Release(stl);
                                if (skipSession == id + "|" + sEnd) continue;
                            }

                            playing = session; found = src; appId = id; keep = true;
                            break;
                        }
                        catch { }
                        finally { if (!keep) Release(session); }
                    }
                }
                finally { Release(sessions); }

                string title = null, artist = null;
                bool ytMusic = false; // para o rotulo do menu ("YouTube Music: ...")
                bool changed = false;
                if (playing != null)
                {
                    try
                    {
                        // Detecta troca de faixa/video pela duracao ou por posicao voltando para tras
                        long end = 0; double pos = 0;
                        var tl = playing.GetTimelineProperties();
                        if (tl != null) { end = tl.EndTime.Ticks; pos = tl.Position.TotalSeconds; }
                        Release(tl);

                        bool newMedia = appId != mediaKey || end != mediaEnd || pos + 3 < mediaPos;
                        if (newMedia) mediaNewAt = DateTime.UtcNow;
                        changed = newMedia || DateTime.UtcNow - mediaFetched > MediaRefresh || mediaTitle == null;
                        mediaPos = pos;

                        if (changed)
                        {
                            // Passo pesado: so quando algo mudou (ou a cada 30 s como garantia)
                            var op = playing.TryGetMediaPropertiesAsync();
                            var props = Wait(op);
                            Release(op);
                            mediaTitle = props == null || string.IsNullOrWhiteSpace(props.Title) ? null : props.Title;
                            mediaArtist = props == null ? null : props.Artist;
                            Release(props);
                            mediaKey = appId; mediaEnd = end; mediaFetched = DateTime.UtcNow;
                            // Logo apos trocar de faixa o navegador as vezes informa a musica antes do artista:
                            // le de novo em 2 s (so nos primeiros 15 s da faixa)
                            if (mediaTitle != null && string.IsNullOrWhiteSpace(mediaArtist) && !MaskedMedia.IsPlaceholder(mediaTitle)
                                && DateTime.UtcNow - mediaNewAt < TimeSpan.FromSeconds(15))
                                mediaFetched = DateTime.UtcNow - MediaRefresh + TimeSpan.FromSeconds(2);
                        }
                    }
                    finally { Release(playing); }

                    bool placeholder = mediaTitle != null && MaskedMedia.IsPlaceholder(mediaTitle);
                    Site site = Site.Other;
                    if (mediaTitle != null && found == Source.YouTube && (placeholder || string.IsNullOrWhiteSpace(mediaArtist)))
                    {
                        // Cada site respeita a sua opcao no menu (YouTube ou YouTube Music)
                        string masked = (Settings.YouTube || Settings.YouTubeMusic) ? MaskedTitle(appId, changed) : null;
                        if (masked != null && masked.StartsWith(TabReader.MusicPrefix))
                        {
                            if (Settings.YouTubeMusic)
                            {
                                // "M<tab>musica[<tab>artista]": artista lido da pagina, ou lembrado de antes para a mesma musica
                                var parts = masked.Substring(TabReader.MusicPrefix.Length).Split('\t');
                                string song = parts[0], who = parts.Length > 1 ? parts[1] : null;
                                if (!string.IsNullOrEmpty(who))
                                {
                                    if (musicArtists.Count > 300) musicArtists.Clear();
                                    musicArtists[song] = who;
                                }
                                else musicArtists.TryGetValue(song, out who);
                                if (string.IsNullOrEmpty(who)) title = song;
                                else { title = who; artist = WithoutArtist(song, who); } // "Artista - Musica"
                                ytMusic = true;
                            }
                        }
                        else if (masked != null && Settings.YouTube) title = masked;
                    }
                    else if (mediaTitle != null && found == Source.YouTube && (site = CheckSite(appId, mediaTitle)) == Site.Other)
                    {
                        // Outro site tocando no navegador: ignora (privacidade) e pula essa midia nos proximos ciclos
                        skipSession = appId + "|" + mediaEnd;
                        skipUntil = DateTime.UtcNow + OtherRecheck();
                        if (lastTitle != null) dirty = true; // tira o que estava aparecendo (sem reenviar "vazio" a cada ciclo)
                    }
                    else if (site == Site.Pending)
                    {
                        // Faixa nova ainda nao confirmada pelo nome da aba: mantem o que estava mostrando
                        title = lastTitle; artist = lastArtist;
                        ytMusic = contSite == Site.YouTubeMusic;
                    }
                    else if (mediaTitle != null && ((site == Site.YouTube && !Settings.YouTube) || (site == Site.YouTubeMusic && !Settings.YouTubeMusic) || (site == Site.SpotifyWeb && !Settings.Spotify)))
                    {
                        // Fonte desligada no menu (YouTube, YouTube Music ou Spotify): nao mostra
                    }
                    else if (mediaTitle != null)
                    {
                        if (site == Site.SpotifyWeb) found = Source.Spotify; // Spotify Web: mesmo formato do Spotify desktop
                        bool music = found == Source.Spotify || site == Site.YouTubeMusic;
                        ytMusic = site == Site.YouTubeMusic;
                        title = mediaTitle;
                        artist = (music || Settings.ShowChannel) ? mediaArtist : null;
                        // Spotify e YouTube Music: "Artista - Musica"; YouTube mantem "Titulo - Canal"
                        if (music && !string.IsNullOrWhiteSpace(artist))
                        {
                            string t = title; title = artist; artist = WithoutArtist(t, title);
                        }
                    }
                }
                else
                {
                    mediaKey = mediaTitle = mediaArtist = null;
                    tabKey = tabTitle = tabIdent = null; // midia parou: a proxima comeca do zero (tentativas rapidas de novo)
                    contApp = null; // reproducao parou: o proximo site precisa ser confirmado de novo
                }

                string shownTitle = Settings.Enabled ? title : null;
                string shownArtist = Settings.Enabled ? artist : null;

                // Reenvia se mudou a midia, as configuracoes, ou se o Messenger foi (re)aberto
                string wins = string.Join(",", Messenger.Windows().ConvertAll(h => h.ToString()).ToArray());
                if (dirty || shownTitle != lastTitle || shownArtist != lastArtist || wins != lastWindows)
                {
                    dirty = false;
                    Messenger.SetStatus(shownTitle, shownArtist);
                    lastTitle = shownTitle; lastArtist = shownArtist; lastWindows = wins;
                }

                string text = title == null ? null
                    : (ytMusic ? "YouTube Music: " : found == Source.Spotify ? "Spotify: " : "YouTube: ")
                      + (string.IsNullOrEmpty(artist) ? title : title + " - " + artist);
                if (text != label) ui.Post(_ => UpdateLabel(text), null);
            }
            catch
            {
                Release(manager);
                manager = null; // tenta de novo no proximo ciclo
                mediaKey = mediaTitle = mediaArtist = null;
            }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        // Libera o objeto do Windows na hora, em vez de esperar o coletor de lixo do .NET
        static void Release(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); } catch { }
        }

        // Espera uma operacao WinRT sem depender de System.Runtime.WindowsRuntime (AsTask)
        static T Wait<T>(IAsyncOperation<T> op)
        {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (op.Status == AsyncStatus.Started)
            {
                if (DateTime.UtcNow > until) { op.Cancel(); throw new TimeoutException(); }
                Thread.Sleep(10);
            }
            if (op.Status != AsyncStatus.Completed) throw new InvalidOperationException("WinRT: " + op.Status);
            return op.GetResults();
        }
        void UpdateLabel(string text)
        {
            label = text;
            string shown = text ?? Strings.Current.NothingPlaying;
            miNow.Text = shown.Length > 80 ? shown.Substring(0, 77) + "..." : shown;
            string tip = Program.Name + "\n" + shown;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 60) + "..." : tip;
        }

        void ShowPreferences()
        {
            if (prefsForm == null || prefsForm.IsDisposed)
            {
                prefsForm = new PreferencesForm(this);
                prefsForm.FormClosed += (s, e) => prefsForm = null;
                prefsForm.Show();
            }
            prefsForm.Activate();
        }
        void Quit()
        {
            timer.Dispose();
            Messenger.SetStatus(null, null);
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }
    }
}
