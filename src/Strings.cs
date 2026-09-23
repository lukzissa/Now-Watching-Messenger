// Textos da interface nos idiomas suportados.

using System.Collections.Generic;
using System.Globalization;

namespace NowWatching
{
    class Strings
    {
        public string LanguageName, NothingPlaying, ShowInMessenger, Sources, YouTube, Spotify,
            PreferencesMenu, Exit, PreferencesTitle, Language, ShowChannel, Startup,
            Ok, Cancel, Freeware, DonateHint, DonateButton,
            UpdateTitle, UpdateText, UpdateButton, Downloading, UpdateFailed, OpenPage;

        public static readonly string[] Codes = { "pt-BR", "es", "en", "ru" };

        static readonly Dictionary<string, Strings> All = new Dictionary<string, Strings>
        {
            { "pt-BR", new Strings {
                LanguageName = "Português (Brasil)",
                NothingPlaying = "Nada tocando",
                ShowInMessenger = "Mostrar no Messenger",
                Sources = "Fontes",
                YouTube = "YouTube (navegador)",
                Spotify = "Spotify",
                PreferencesMenu = "Preferências...",
                Exit = "Sair",
                PreferencesTitle = "Preferências",
                Language = "Idioma:",
                ShowChannel = "Mostrar nome do canal (YouTube)",
                Startup = "Iniciar com o Windows",
                Ok = "OK",
                Cancel = "Cancelar",
                Freeware = "Freeware desenvolvido por Lucas Issa.",
                DonateHint = "Se o app foi útil para você, considere apoiar com uma doação.",
                DonateButton = "Doar com PayPal",
                UpdateTitle = "Atualização disponível",
                UpdateText = "Uma nova versão está disponível: {0}\nVersão atual: {1}\n\nDeseja atualizar agora?",
                UpdateButton = "Atualizar",
                Downloading = "Baixando atualização...",
                UpdateFailed = "Não foi possível atualizar automaticamente.\nBaixe a nova versão pela página do GitHub.",
                OpenPage = "Abrir página" } },
            { "es", new Strings {
                LanguageName = "Español",
                NothingPlaying = "Nada en reproducción",
                ShowInMessenger = "Mostrar en Messenger",
                Sources = "Fuentes",
                YouTube = "YouTube (navegador)",
                Spotify = "Spotify",
                PreferencesMenu = "Preferencias...",
                Exit = "Salir",
                PreferencesTitle = "Preferencias",
                Language = "Idioma:",
                ShowChannel = "Mostrar nombre del canal (YouTube)",
                Startup = "Iniciar con Windows",
                Ok = "Aceptar",
                Cancel = "Cancelar",
                Freeware = "Freeware desarrollado por Lucas Issa.",
                DonateHint = "Si la aplicación te resultó útil, considera apoyarla con una donación.",
                DonateButton = "Donar con PayPal",
                UpdateTitle = "Actualización disponible",
                UpdateText = "Hay una nueva versión disponible: {0}\nVersión actual: {1}\n\n¿Deseas actualizar ahora?",
                UpdateButton = "Actualizar",
                Downloading = "Descargando actualización...",
                UpdateFailed = "No se pudo actualizar automáticamente.\nDescarga la nueva versión desde la página de GitHub.",
                OpenPage = "Abrir página" } },
            { "en", new Strings {
                LanguageName = "English",
                NothingPlaying = "Nothing playing",
                ShowInMessenger = "Show in Messenger",
                Sources = "Sources",
                YouTube = "YouTube (browser)",
                Spotify = "Spotify",
                PreferencesMenu = "Preferences...",
                Exit = "Exit",
                PreferencesTitle = "Preferences",
                Language = "Language:",
                ShowChannel = "Show channel name (YouTube)",
                Startup = "Start with Windows",
                Ok = "OK",
                Cancel = "Cancel",
                Freeware = "Freeware developed by Lucas Issa.",
                DonateHint = "If this app is useful to you, please consider supporting it with a donation.",
                DonateButton = "Donate with PayPal",
                UpdateTitle = "Update available",
                UpdateText = "A new version is available: {0}\nCurrent version: {1}\n\nDo you want to update now?",
                UpdateButton = "Update",
                Downloading = "Downloading update...",
                UpdateFailed = "Could not update automatically.\nPlease download the new version from the GitHub page.",
                OpenPage = "Open page" } },
            { "ru", new Strings {
                LanguageName = "Русский",
                NothingPlaying = "Ничего не воспроизводится",
                ShowInMessenger = "Показывать в Messenger",
                Sources = "Источники",
                YouTube = "YouTube (браузер)",
                Spotify = "Spotify",
                PreferencesMenu = "Настройки...",
                Exit = "Выход",
                PreferencesTitle = "Настройки",
                Language = "Язык:",
                ShowChannel = "Показывать название канала (YouTube)",
                Startup = "Запускать вместе с Windows",
                Ok = "ОК",
                Cancel = "Отмена",
                Freeware = "Бесплатная программа, разработанная Lucas Issa.",
                DonateHint = "Если программа оказалась полезной, поддержите её пожертвованием.",
                DonateButton = "Пожертвовать через PayPal",
                UpdateTitle = "Доступно обновление",
                UpdateText = "Доступна новая версия: {0}\nТекущая версия: {1}\n\nОбновить сейчас?",
                UpdateButton = "Обновить",
                Downloading = "Загрузка обновления...",
                UpdateFailed = "Не удалось обновить автоматически.\nСкачайте новую версию со страницы GitHub.",
                OpenPage = "Открыть страницу" } },
        };

        public static Strings Get(string code)
        {
            Strings s;
            return All.TryGetValue(code ?? "", out s) ? s : All["en"];
        }

        public static Strings Current { get { return Get(Settings.Language); } }

        public static string DefaultLanguage()
        {
            switch (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            {
                case "pt": return "pt-BR";
                case "es": return "es";
                case "ru": return "ru";
                default: return "en";
            }
        }
    }
}
