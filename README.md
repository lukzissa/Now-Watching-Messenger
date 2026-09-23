# Now Watching Messenger

Show what you're watching on **YouTube** or listening to on **Spotify** in your **Windows Live Messenger 2009** "Now Playing" status — on Windows 10 and 11.

A modern rewrite of the classic *Now Watching* tray tool, which no longer runs on current Windows.

[Português](#português) · [Donate](#donate)

## Features

- Shows the YouTube video title (optionally with the channel name) or the Spotify track as *Artist - Song*
- Works with Edge, Chrome, Firefox, Opera, Brave and Vivaldi, even when the YouTube tab is in the background
- Enable or disable YouTube and Spotify independently
- Clears the status automatically when playback is paused or stopped
- Re-sends the status if Messenger is restarted
- Start with Windows option
- Checks for new versions on startup and updates itself with one click (downloads from GitHub Releases and verifies the SHA-256)
- Interface in English, Portuguese (Brazil), Spanish and Russian
- Single small executable, no installer, no extra runtime (uses the .NET Framework 4 already included in Windows)

## Requirements

- Windows 10 or 11
- Windows Live Messenger 2009 (works with revival servers)
- In Messenger, enable **"Show what I'm listening to"** in the personal message menu

## Usage

1. Download `NowWatchingMessenger.exe` from [Releases](../../releases) and run it
2. Play a YouTube video or a Spotify song
3. Right-click the tray icon to choose sources, open Preferences or exit

## How it works

- Reads the current media from the Windows **Global System Media Transport Controls** (the same media info shown in the Windows volume flyout)
- Sends it to Messenger through the original "Now Playing" protocol (`WM_COPYDATA` to the `MsnMsgrUIManager` window)
- Settings are stored in `HKEY_CURRENT_USER\Software\NowWatching`

## Building

No Visual Studio needed. The C# compiler from the .NET Framework ships with Windows:

```powershell
powershell -ExecutionPolicy Bypass -File src\build.ps1
```

The executable is created as `NowWatchingMessenger.exe` in the repository root.

## Known limitations

- Spotify Web Player (in the browser) is detected as a browser source, since Windows only reports which browser is playing, not which site
- Only media that the browser or app exposes to Windows media controls is detected

---

## Português

Mostra o que você está assistindo no **YouTube** ou ouvindo no **Spotify** no status "O que estou ouvindo" do **Windows Live Messenger 2009**, no Windows 10 e 11.

**Recursos:** título do vídeo (com ou sem o nome do canal), Spotify no formato *Artista - Música*, funciona no Edge, Chrome, Firefox e outros navegadores (até em abas de fundo), YouTube e Spotify ativados separadamente, iniciar com o Windows, atualização automática com um clique e interface em português, inglês, espanhol e russo.

**Como usar:** baixe o `NowWatchingMessenger.exe` em [Releases](../../releases), execute e, no Messenger, ative **"Mostrar o que estou ouvindo"** no menu da mensagem pessoal. Clique com o botão direito no ícone da bandeja para escolher as fontes, abrir as Preferências ou sair.

**Compilar:** `powershell -ExecutionPolicy Bypass -File src\build.ps1`

---

## Donate

Freeware developed by Lucas Issa. If this app is useful to you, consider supporting it:

[![Donate with PayPal](https://img.shields.io/badge/Donate-PayPal-ffc439?logo=paypal&logoColor=003087)](https://www.paypal.com/donate/?business=lukz.issa%40gmail.com&no_recurring=0&item_name=Now+Watching+Messenger)

## License

[MIT](LICENSE) © 2026 Lucas Issa

*Windows Live Messenger, YouTube and Spotify are trademarks of their respective owners. This project is not affiliated with or endorsed by them.*
