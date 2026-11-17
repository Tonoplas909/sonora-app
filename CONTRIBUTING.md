# Contributing to Sonora

Thanks for wanting to help! Bug reports, ideas, translations, docs and code are all welcome. This page gets you from zero to a pull request.

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). To report a security problem, please use [SECURITY.md](SECURITY.md) rather than a public issue.

## Ways to help

- **Report a bug.** Open an [issue](https://github.com/Tonoplas909/sonora-app/issues/new/choose) with what you did, what you expected and what happened. The app version and your Windows version help a lot. Never paste a Spotify Client ID, token or the contents of `state.json`.
- **Suggest a feature.** Say what you are trying to do rather than only the solution you have in mind. Sonora leans towards *customizable* over *opinionated*, so "make it a setting" is often the right shape.
- **Translate.** The interface is French today and its text lives inline in the XAML and C#. A pull request that moves it into resource files and adds a language would be a big contribution: open an issue first so we agree on the approach.
- **Improve the docs, themes or screenshots.**

## Set up

You need Windows 10 (2004) or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/Tonoplas909/sonora-app.git
cd sonora-app
dotnet build Sonora.slnx
dotnet test Sonora.Tests
dotnet run --project Sonora
```

Visual Studio, JetBrains Rider and VS Code all work as long as they use the .NET 10 SDK; `.editorconfig` carries the basics.

### Try it without touching your own profile

Sonora keeps everything in `%LOCALAPPDATA%\Sonora`. Point it somewhere else with an environment variable and it starts clean:

```powershell
.\tools\New-DemoProfile.ps1                       # a fictional library, in %TEMP%\sonora-demo
$env:SONORA_DATA_DIR = "$env:TEMP\sonora-demo\data"
dotnet run --project Sonora
```

### Working with Spotify

You need your own Spotify developer application and a Premium account, as described in the [README](README.md#connect-spotify). A few habits that save you pain:

- **Do not hammer the API.** Development-mode apps get rate-limited, sometimes for hours, and restarting the app over and over is the quickest way to trigger it. Test against the demo profile when your change is not about Spotify.
- **Keep secrets out of git.** The Client ID and token live in your profile folder, never in the repo. Do not add them to code, tests, issues or screenshots.
- Spotify's [Developer Terms](https://developer.spotify.com/terms) apply to anything you build on its API.

## How the code is organized

| Project | Rule of thumb |
|---|---|
| `Sonora.Appearance` | **No UI, no Windows APIs.** Themes, dock layout, settings, shortcuts, LRC, M3U, sleep timer, search ranking. Anything that can be plain logic belongs here **with tests**. |
| `Sonora` | The WPF app. Views stay thin; logic goes in view models and services, or down into `Sonora.Appearance` when it can. |
| `Sonora.Tests` | xUnit. A bug fix should come with a test that fails without it, when the code is testable. |

Releases are made by pushing a tag; [docs/RELEASING.md](docs/RELEASING.md) explains how, and how the in-app updates work.

Themes are data: the built-in ones are JSON files in `Sonora.Appearance/Theming/BuiltIn/` registered in `ThemeCatalog.cs`. Adding a colour palette to an existing style is just a new JSON file plus its id in that list and in the two tests that enumerate the built-in themes. The design notes in [`docs/design/`](docs/design) explain why things are the way they are (in French).

### Seeing what you changed

Sonora can render a view to an image and exit, which is how the README pictures are made and a good way to check a UI change on several themes:

```powershell
$env:SONORA_DATA_DIR = "$env:TEMP\sonora-demo\data"
.\Sonora\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Sonora.exe --snapshot out.png album --theme soul
```

Views: `home`, `albums`, `artists`, `album`, `all`, `play`, `search:<text>`, `discover`, `tops`, `stats`, `settings`, `appearance`, `fullscreen`, `miniplayer`, `toast`. Include before/after images in your pull request when it changes how something looks.

## Code style

- C# with nullable reference types on, 4-space indentation, file-scoped namespaces as in the existing code.
- Comments explain **why**, in English. They are for the next reader, so say what is surprising or what would break if it changed.
- User-facing text is French for now; keep it consistent with the surrounding wording.
- Keep the app **light**. Sonora is proud of a small footprint: avoid per-frame work, large decoded images and always-on timers. If you add something heavy, say what it costs.
- No new dependency without a good reason; mention its license in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Commits and pull requests

1. Fork, then branch from `main`.
2. Keep a pull request focused on one thing. Small ones get reviewed faster.
3. Write commit subjects in the imperative, in English ("Fix the taskbar on a maximized start"), and explain the *why* in the body when it is not obvious.
4. Make sure `dotnet build Sonora.slnx` shows no warnings and `dotnet test Sonora.Tests` passes. CI runs the same.
5. Fill in the pull request template. If the change is visible, add screenshots.

Not sure whether an idea fits? Open an issue and ask before you spend a weekend on it.

## License

By contributing you agree that your work is released under the project's [MIT License](LICENSE).
