// Ferramenta de diagnostico: envia o mesmo titulo em formatos diferentes para o WLM,
// para descobrir qual deles o lado do contato exibe sem o "-" sobrando.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class TesteFormatos
{
    [StructLayout(LayoutKind.Sequential)]
    struct CDS { public IntPtr d; public int c; public IntPtr p; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr a, IntPtr b, string c, string t);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, ref CDS l);

    static void Send(string s)
    {
        IntPtr b = Marshal.StringToHGlobalUni(s);
        var c = new CDS { d = (IntPtr)0x547, c = (s.Length + 1) * 2, p = b };
        IntPtr h = IntPtr.Zero;
        while ((h = FindWindowEx(IntPtr.Zero, h, "MsnMsgrUIManager", null)) != IntPtr.Zero)
            SendMessage(h, 0x4A, IntPtr.Zero, ref c);
        Marshal.FreeHGlobal(b);
    }

    [STAThread]
    static void Main()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("NowWatching")) p.Kill();

        string[][] tests = {
            new[] { "1 - Atual (Musica, so titulo)",        "\\0Music\\01\\0{0}\\0Minha Live de Hoje T1\\0\\0\\0\\0" },
            new[] { "2 - Musica, sem campos extras",         "\\0Music\\01\\0{0}\\0Minha Live de Hoje T2\\0" },
            new[] { "3 - Musica, titulo no campo artista",   "\\0Music\\01\\0{1}\\0\\0Minha Live de Hoje T3\\0\\0\\0" },
            new[] { "4 - Musica, texto no proprio formato",  "\\0Music\\01\\0Minha Live de Hoje T4\\0\\0\\0\\0\\0" },
            new[] { "5 - Musica, com WMContentID",           "\\0Music\\01\\0{0}\\0Minha Live de Hoje T5\\0\\0\\0WMContentID\\0" },
            new[] { "6 - Categoria Jogos (icone muda)",      "\\0Games\\01\\0{0}\\0Minha Live de Hoje T6\\0\\0\\0\\0" },
            new[] { "7 - Categoria Office (icone muda)",     "\\0Office\\01\\0{0}\\0Minha Live de Hoje T7\\0\\0\\0\\0" },
        };

        Application.EnableVisualStyles();
        var f = new Form { Text = "Teste de formatos - Now Watching", Font = new Font("Segoe UI", 9f), AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
            StartPosition = FormStartPosition.CenterScreen, Padding = new Padding(12), TopMost = true };
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        flow.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(0, 0, 0, 10),
            Text = "Clique em um teste, espere alguns segundos e veja na outra conta como aparece. Anote quais aparecem SEM o \"-\" no final." });
        foreach (var t in tests)
        {
            var payload = t[1];
            var b = new Button { Text = t[0], Width = 360, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
            b.Click += (s, e) => { Send(payload); f.Text = "Enviado: " + ((Button)s).Text.Substring(0, 1); };
            flow.Controls.Add(b);
        }
        var clear = new Button { Text = "Limpar status", Width = 360, Height = 30, Margin = new Padding(3, 10, 3, 3) };
        clear.Click += (s, e) => Send("\\0Music\\00\\0\\0\\0\\0\\0\\0");
        flow.Controls.Add(clear);
        f.Controls.Add(flow);
        f.FormClosed += (s, e) =>
        {
            Send("\\0Music\\00\\0\\0\\0\\0\\0\\0");
            var exe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NowWatching.exe");
            if (System.IO.File.Exists(exe)) System.Diagnostics.Process.Start(exe);
        };
        Application.Run(f);
    }
}
