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
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]

namespace NowWatching
{
    static class Program
    {
        public const string Name = "Now Watching Messenger";
        public const string Version = "1.1";

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
        readonly ToolStripMenuItem miNow, miEnabled, miSources, miYouTube, miSpotify, miPrefs, miExit;
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
                miSources, miYouTube, miSpotify, new ToolStripSeparator(),
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
        DateTime mediaFetched = DateTime.MinValue;
        static readonly TimeSpan MediaRefresh = TimeSpan.FromSeconds(30);

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
                            if (src == Source.YouTube && !Settings.YouTube) continue;
                            if (src == Source.Spotify && !Settings.Spotify) continue;

                            var info = session.GetPlaybackInfo();
                            bool isPlaying = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                            Release(info);
                            if (!isPlaying) continue;

                            playing = session; found = src; appId = id; keep = true;
                            break;
                        }
                        catch { }
                        finally { if (!keep) Release(session); }
                    }
                }
                finally { Release(sessions); }

                string title = null, artist = null;
                if (playing != null)
                {
                    try
                    {
                        // Detecta troca de faixa/video pela duracao ou por posicao voltando para tras
                        long end = 0; double pos = 0;
                        var tl = playing.GetTimelineProperties();
                        if (tl != null) { end = tl.EndTime.Ticks; pos = tl.Position.TotalSeconds; }
                        Release(tl);

                        bool changed = appId != mediaKey || end != mediaEnd || pos + 3 < mediaPos
                            || DateTime.UtcNow - mediaFetched > MediaRefresh || mediaTitle == null;
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
                        }
                    }
                    finally { Release(playing); }

                    if (mediaTitle != null)
                    {
                        title = mediaTitle;
                        artist = (found == Source.Spotify || Settings.ShowChannel) ? mediaArtist : null;
                        // Spotify: "Artista - Musica"; YouTube mantem "Titulo - Canal"
                        if (found == Source.Spotify && !string.IsNullOrWhiteSpace(artist))
                        {
                            string t = title; title = artist; artist = t;
                        }
                    }
                }
                else
                {
                    mediaKey = mediaTitle = mediaArtist = null;
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
                    : (found == Source.Spotify ? "Spotify: " : "YouTube: ")
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
