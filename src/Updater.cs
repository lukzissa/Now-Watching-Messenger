// Verificacao de nova versao pelo GitHub Releases e atualizacao automatica do exe.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NowWatching
{
    static class Updater
    {
        const string ApiUrl = "https://api.github.com/repos/lukzissa/Now-Watching-Messenger/releases/latest";
        public const string ReleasesPage = "https://github.com/lukzissa/Now-Watching-Messenger/releases/latest";
        const string AssetName = "NowWatchingMessenger.exe";

        public class Release
        {
            public Version Version;
            public string Tag, DownloadUrl, Sha256;
        }

        static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }

        public static Version Current { get { return Normalize(new Version(Program.Version)); } }

        static WebClient NewClient()
        {
            // GitHub exige TLS 1.2 e um User-Agent
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "NowWatchingMessenger/" + Program.Version;
            return wc;
        }

        // Consulta o ultimo release; devolve null se nao houver versao mais nova (ou sem internet)
        public static Release CheckLatest()
        {
            try
            {
                string json;
                using (var wc = NewClient())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = wc.DownloadString(ApiUrl);
                }

                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("tag_name")) return null;

                string tag = root["tag_name"] as string;
                Version v;
                if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out v)) return null;
                v = Normalize(v);
                if (v <= Current) return null;

                var assets = root.ContainsKey("assets") ? root["assets"] as object[] : null;
                if (assets == null) return null;
                foreach (var o in assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null || !string.Equals(a["name"] as string, AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    string digest = a.ContainsKey("digest") ? a["digest"] as string : null;
                    return new Release
                    {
                        Version = v,
                        Tag = tag,
                        DownloadUrl = a["browser_download_url"] as string,
                        Sha256 = digest != null && digest.StartsWith("sha256:") ? digest.Substring(7) : null,
                    };
                }
            }
            catch { }
            return null;
        }

        // Roda no processo auxiliar (--check-update): escreve "tag|url|sha256" se houver versao nova
        public static void WriteCheckResult()
        {
            var r = CheckLatest();
            if (r != null) Console.Out.Write(r.Tag + "|" + r.DownloadUrl + "|" + (r.Sha256 ?? ""));
            Console.Out.Flush();
        }

        // Roda no app principal: inicia o proprio exe com --check-update e le a resposta
        public static Release CheckInChildProcess()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--check-update")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                string output;
                using (var p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return null; }
                }

                var parts = output.Trim().Split('|');
                Version v;
                if (parts.Length != 3 || !Version.TryParse(parts[0].TrimStart('v', 'V'), out v)) return null;
                v = Normalize(v);
                if (v <= Current) return null;
                return new Release { Version = v, Tag = parts[0], DownloadUrl = parts[1], Sha256 = parts[2].Length > 0 ? parts[2] : null };
            }
            catch { return null; }
        }

        // Baixa o novo exe, confere integridade, troca pelo atual e inicia a nova versao.
        // O exe em uso pode ser renomeado no Windows; o antigo (.old) e apagado pela nova versao.
        public static void Apply(Release r)
        {
            string exe = Application.ExecutablePath;
            string tmp = exe + ".new";
            string old = exe + ".old";

            if (File.Exists(tmp)) File.Delete(tmp);
            using (var wc = NewClient()) wc.DownloadFile(r.DownloadUrl, tmp);

            byte[] head = new byte[2];
            using (var fs = File.OpenRead(tmp)) fs.Read(head, 0, 2);
            if (head[0] != 'M' || head[1] != 'Z') { File.Delete(tmp); throw new InvalidDataException("not an exe"); }

            if (r.Sha256 != null)
            {
                string hash;
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(tmp))
                    hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                if (!string.Equals(hash, r.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(tmp); throw new InvalidDataException("sha256 mismatch"); }
            }

            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(tmp, exe); }
            catch { File.Move(old, exe); throw; }

            Process.Start(exe, "--updated");
        }

        // Apaga sobras de uma atualizacao anterior (o processo antigo pode demorar a fechar)
        public static void CleanupAsync()
        {
            string exe = Application.ExecutablePath;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (var f in new[] { exe + ".old", exe + ".new" })
                    for (int i = 0; i < 10 && File.Exists(f); i++)
                    {
                        try { File.Delete(f); } catch { Thread.Sleep(1000); }
                    }
            });
        }
    }
}
