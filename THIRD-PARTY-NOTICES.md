# Third-party notices

Sonora itself is released under the [MIT License](LICENSE). It builds on the following.

## Bundled with the source

| Component | Used for | License |
|---|---|---|
| [Barlow Condensed](https://github.com/jpt/barlow), [Figtree](https://github.com/erikdkennedy/figtree), [Syne](https://gitlab.com/bonjour-monde/fonderie/syne-typeface), [Fraunces](https://github.com/undercasetype/Fraunces) | Fonts of the Encre, Affiche and Soul themes (`Sonora/Fonts/`) | SIL Open Font License 1.1 — full text and copyright lines in [`Sonora/Fonts/OFL.txt`](Sonora/Fonts/OFL.txt) |

## NuGet packages

| Package | Used for | License |
|---|---|---|
| [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) | Hosts Spotify's Web Playback SDK in an invisible browser (Spotify playback) | Microsoft's WebView2 SDK license, shipped inside the package |
| [Velopack](https://velopack.io) | The installer, the uninstaller and in-app updates | MIT |
| [xUnit](https://xunit.net/), Microsoft.NET.Test.Sdk, coverlet | Tests only (`Sonora.Tests`) | Apache-2.0 / MIT |

The .NET runtime and WPF are MIT-licensed by Microsoft and are **not** redistributed here.

## Online services Sonora can talk to

Sonora ships none of their content; it asks them at run time, and only for the feature that needs it.

| Service | Used for | When |
|---|---|---|
| [Spotify Web API](https://developer.spotify.com/documentation/web-api) and [Web Playback SDK](https://developer.spotify.com/documentation/web-playback-sdk) | Your library, search, playback and Spotify Connect | Once you sign in with your own Spotify developer application |
| [LRCLIB](https://lrclib.net) | Synchronized lyrics | When the lyrics panel is on |
| [GitHub](https://github.com) (Releases) | Looking for, downloading and installing a new version of Sonora | When Sonora opens (at most once an hour) or when you click the check button in the settings; the automatic check can be switched off. Installing is always your click |
| [MusicBrainz](https://musicbrainz.org) and [ListenBrainz](https://listenbrainz.org) | Similar artists; optional scrobbling | Similar artists can be switched off; scrobbling only with a ListenBrainz token |

Please respect each service's terms and rate limits when you use or modify Sonora.

## Trademarks

Spotify is a trademark of Spotify AB. Sonora is an independent project and is not affiliated with, endorsed by or sponsored by Spotify.
