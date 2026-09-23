// Janelas de Preferencias (inclui a area Sobre/Doar) e de aviso de atualizacao.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NowWatching
{
    class BaseForm : Form
    {
        protected BaseForm()
        {
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Program.LoadIcon(SystemInformation.IconSize);
        }

        // Ajusta a janela ao conteudo + margem e centraliza o conteudo
        // (Padding do Form so afeta controles com Dock, entao o conteudo ficava colado a esquerda)
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (Controls.Count != 1) return;
            var content = Controls[0];
            float scale;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            int mx = (int)(22 * scale), my = (int)(18 * scale);
            var pref = content.PreferredSize;
            ClientSize = new Size(pref.Width + 2 * mx, pref.Height + 2 * my);
            content.Location = new Point((ClientSize.Width - pref.Width) / 2, (ClientSize.Height - pref.Height) / 2);
        }

        protected static FlowLayoutPanel Flow(FlowDirection dir)
        {
            return new FlowLayoutPanel { FlowDirection = dir, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        }

        protected static Button MakeButton(string text)
        {
            return new Button { Text = text, AutoSize = true, MinimumSize = new Size(88, 28), Margin = new Padding(6, 0, 0, 0) };
        }
    }

    class PreferencesForm : BaseForm
    {
        const string DonateUrl = "https://www.paypal.com/donate/?business=lukz.issa%40gmail.com&no_recurring=0&item_name=Now+Watching+Messenger";

        readonly TrayApp app;
        readonly ComboBox cbLanguage;
        readonly CheckBox chkChannel, chkStartup;

        public PreferencesForm(TrayApp app)
        {
            this.app = app;
            var t = Strings.Current;
            Text = Program.Name + " - " + t.PreferencesTitle;

            var root = Flow(FlowDirection.TopDown);

            // --- Configuracoes ---
            var langRow = Flow(FlowDirection.LeftToRight);
            langRow.Margin = new Padding(0, 0, 0, 10);
            langRow.Controls.Add(new Label { Text = t.Language, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
            cbLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            foreach (var code in Strings.Codes) cbLanguage.Items.Add(Strings.Get(code).LanguageName);
            cbLanguage.SelectedIndex = Math.Max(0, Array.IndexOf(Strings.Codes, Settings.Language));
            langRow.Controls.Add(cbLanguage);
            root.Controls.Add(langRow);

            chkChannel = new CheckBox { Text = t.ShowChannel, AutoSize = true, Checked = Settings.ShowChannel, Margin = new Padding(0, 0, 0, 6) };
            chkStartup = new CheckBox { Text = t.Startup, AutoSize = true, Checked = Settings.Startup, Margin = new Padding(0, 0, 0, 14) };
            root.Controls.Add(chkChannel);
            root.Controls.Add(chkStartup);

            root.Controls.Add(Divider());

            // --- Sobre / Doar ---
            int logo;
            using (var g = CreateGraphics()) logo = (int)(80 * g.DpiX / 96f);

            var about = Flow(FlowDirection.LeftToRight);
            about.Margin = new Padding(0, 14, 0, 14);
            var pic = new PictureBox { Image = Program.LoadAboutImage(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(logo, logo), Margin = new Padding(0, 0, 14, 0) };
            about.Controls.Add(pic);
            // PictureBox nao libera a imagem sozinho; libera imagem e icone ao fechar
            FormClosed += (s, e) =>
            {
                var img = pic.Image; pic.Image = null; if (img != null) img.Dispose();
                var ico = Icon; if (ico != null) ico.Dispose();
            };

            var text = Flow(FlowDirection.TopDown);
            text.Controls.Add(new Label { Text = Program.Name + " " + Program.Version, AutoSize = true, Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 84, 166), Margin = new Padding(0, 0, 0, 2) });
            text.Controls.Add(new Label { Text = t.Freeware, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) });
            var hint = new Label { Text = t.DonateHint, AutoSize = true, ForeColor = Color.FromArgb(90, 90, 90), Margin = new Padding(0, 0, 0, 10) };
            hint.MaximumSize = new Size(hint.Font.Height * 18, 0);
            text.Controls.Add(hint);

            var btnDonate = new Button
            {
                Text = t.DonateButton,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(255, 196, 57),
                ForeColor = Color.FromArgb(0, 48, 135),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Padding = new Padding(14, 4, 14, 4),
                Margin = new Padding(0),
            };
            btnDonate.FlatAppearance.BorderSize = 1;
            btnDonate.FlatAppearance.BorderColor = btnDonate.ForeColor;
            btnDonate.FlatAppearance.MouseOverBackColor = Color.FromArgb(242, 186, 54);
            btnDonate.Click += (s, e) => { try { Process.Start(DonateUrl); } catch { } };
            text.Controls.Add(btnDonate);

            about.Controls.Add(text);
            root.Controls.Add(about);

            root.Controls.Add(Divider());

            // --- Botoes ---
            var buttons = Flow(FlowDirection.RightToLeft);
            buttons.Anchor = AnchorStyles.Right;
            buttons.Margin = new Padding(24, 14, 0, 0);
            var btnCancel = MakeButton(t.Cancel);
            var btnOk = MakeButton(t.Ok);
            btnCancel.Click += (s, e) => Close();
            btnOk.Click += (s, e) => Apply();
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        // Linha horizontal que estica ate a largura da coluna (Anchor Left|Right no FlowLayoutPanel)
        static Control Divider()
        {
            return new Label { AutoSize = false, Height = 1, BackColor = Color.FromArgb(215, 215, 215), Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0) };
        }

        void Apply()
        {
            Settings.Language = Strings.Codes[cbLanguage.SelectedIndex];
            Settings.ShowChannel = chkChannel.Checked;
            if (chkStartup.Checked != Settings.Startup) Settings.Startup = chkStartup.Checked;
            app.Changed();
            Close();
        }
    }

    class UpdateForm : BaseForm
    {
        public UpdateForm(Updater.Release release, Action quit)
        {
            var t = Strings.Current;
            Text = Program.Name + " - " + t.UpdateTitle;
            TopMost = true;

            var root = Flow(FlowDirection.TopDown);
            var msg = new Label { Text = string.Format(t.UpdateText, release.Tag, Program.Version), AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
            msg.MaximumSize = new Size(msg.Font.Height * 22, 0);
            root.MinimumSize = new Size(msg.Font.Height * 22, 0); // largura para o titulo nao cortar
            root.Controls.Add(msg);

            var buttons = Flow(FlowDirection.RightToLeft);
            buttons.Anchor = AnchorStyles.Right;
            buttons.Margin = new Padding(24, 0, 0, 0);
            var btnCancel = MakeButton(t.Cancel);
            var btnUpdate = MakeButton(t.UpdateButton);
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnUpdate);
            root.Controls.Add(buttons);

            bool failed = false;
            btnCancel.Click += (s, e) => Close();
            btnUpdate.Click += (s, e) =>
            {
                if (failed)
                {
                    try { Process.Start(Updater.ReleasesPage); } catch { }
                    Close();
                    return;
                }
                btnUpdate.Enabled = btnCancel.Enabled = false;
                msg.Text = t.Downloading;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Updater.Apply(release);
                        BeginInvoke((Action)(() => { Close(); quit(); }));
                    }
                    catch
                    {
                        BeginInvoke((Action)(() =>
                        {
                            failed = true;
                            msg.Text = t.UpdateFailed;
                            btnUpdate.Text = t.OpenPage;
                            btnUpdate.Enabled = btnCancel.Enabled = true;
                        }));
                    }
                });
            };

            Controls.Add(root);
            AcceptButton = btnUpdate;
            CancelButton = btnCancel;
            FormClosed += (s, e) => { var ico = Icon; if (ico != null) ico.Dispose(); };
        }
    }
}