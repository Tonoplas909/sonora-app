# Security policy

## Supported versions

Sonora is a young project: security fixes go into the latest release and `main`. Please update before reporting.

## Reporting a vulnerability

**Please do not open a public issue for a security problem.** Use GitHub's private reporting instead:

1. Go to the repository's **Security** tab.
2. Choose **Report a vulnerability**.
3. Describe what you found, how to reproduce it and what an attacker could do with it.

If that option is not available, contact the maintainers privately through the details on their GitHub profiles. You will get an answer as soon as someone can look at it; this is a volunteer project, so please allow a few days. We will keep you informed, fix the problem, and credit you in the release notes if you wish.

Please never include a real Spotify token, Client ID or the contents of `state.json` in a report. Describe the problem with placeholders.

## What is worth reporting

Sonora runs on your PC with your user rights and holds the keys to a Spotify account, so the interesting areas are:

- **Credential handling.** The Spotify refresh token is stored in `%LOCALAPPDATA%\Sonora\spotify.bin`, encrypted with Windows DPAPI for the current user. A way to read it, leak it, log it or send it anywhere else is a vulnerability.
- **The sign-in listener.** During sign-in Sonora listens briefly on `127.0.0.1:5543` for Spotify's redirect and checks a random `state` value. Anything that lets another page or program hijack that flow counts.
- **The embedded browser.** A WebView2 hosts Spotify's player from a virtual host mapped to a local folder. Ways to load untrusted content, reach other local files from it, or escape it are in scope.
- **Files and parsing.** Themes, layouts, playlists (M3U) and lyrics are read from disk or the network. Crashes or code execution from a crafted file are in scope.
- **The installer and the updater.** Sonora is installed and updated by Velopack from GitHub release assets over HTTPS, and runs with your user rights. A way to make it install something other than the published release, or to skip the user's confirmation, is in scope. Releases can be code-signed; see [docs/RELEASING.md](docs/RELEASING.md).

## What is not

- Problems that need an attacker who already controls your Windows account.
- Rate limits or access restrictions imposed by Spotify on your developer application.
- Vulnerabilities in Windows, .NET, WebView2 or Spotify itself: please report those to their vendors.
