# Releasing Sonora

A release is made by pushing a tag. The [Release workflow](../.github/workflows/release.yml) does the rest.

```powershell
# 1. Make sure main is green and contains what you want to ship.
# 2. Tag it with the version (semantic versioning, prefixed with v) and push the tag.
git tag v1.0.0
git push origin v1.0.0
```

The workflow tests, publishes, packs with [Velopack](https://velopack.io) and creates a GitHub release with:

| Asset | For |
|---|---|
| `SonoraApp-win-Setup.exe` | The installer. Installs for the current user (no administrator rights), adds the Start menu entry and the uninstaller, and installs the .NET 10 Desktop Runtime and WebView2 if they are missing. |
| `SonoraApp-win-Portable.zip` | No installation: unzip anywhere and run `Sonora.exe`. Needs the .NET 10 Desktop Runtime and WebView2 to be installed already. |
| `SonoraApp-<version>-full.nupkg`, `…-delta.nupkg`, `releases.win.json`, `RELEASES` | What the in-app updater reads. **Do not delete them** from a release. |

The release notes are the commit subjects since the previous tag, so write them for a reader.

## How updates work

An installed Sonora asks GitHub for the latest release when it opens (at most once an hour, and it can be switched off in *Settings › Updates*) and has a button for a manual check. When there is a newer version it says so; installing is always the user's click. Velopack then downloads the package (a small delta when possible), Sonora closes, the files are replaced and the new version starts.

A copy that was not installed by the installer (a build from source, `dotnet run`) never updates itself and sends no request on opening: its settings page links to the releases page instead.

## Naming: why `SonoraApp`

The package id is `SonoraApp` and not `Sonora` on purpose. Velopack owns its install folder, `%LOCALAPPDATA%\SonoraApp`, and removes it on uninstall. Sonora's own data (settings, library cache, Spotify session) lives in `%LOCALAPPDATA%\Sonora`, which must not be touched by an update or an uninstall.

## Code signing

An unsigned installer works but Windows SmartScreen warns about an unknown publisher. Signing is optional and the workflow does it when it finds the right secrets (*Settings › Secrets and variables › Actions*):

| Option | Secrets |
|---|---|
| **Azure Trusted Signing** (called Artifact Signing in some places). A Microsoft service, around US$10 a month, with a one-off identity check of the publisher. | `AZURE_SIGNING_METADATA` (the contents of the `metadata.json` file with the endpoint, account and certificate profile names), `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` (a service principal allowed to sign) |
| **Any other signing tool** (a certificate in a cloud HSM, `signtool` parameters…) | `VPK_SIGN_PARAMS`, passed to Velopack's `--signParams` |

If neither is set the release is built unsigned. Even a signed app can show a SmartScreen warning for a while: reputation builds with downloads (an EV certificate gets it at once, at a higher price).
[SignPath Foundation](https://signpath.org/) offers free signing to open source projects; it works by submitting the finished files to their service, which needs a different workflow from this one.

## Trying it without publishing

Build the packages locally to check that packing works (this does not install anything):

```powershell
dotnet tool install -g vpk --version 1.2.161
dotnet publish Sonora/Sonora.csproj -c Release -r win-x64 --self-contained false -p:Version=0.0.1 -o publish
vpk pack --packId SonoraApp --packTitle Sonora --packVersion 0.0.1 --packDir publish --mainExe Sonora.exe `
  --framework net10.0-x64-desktop,webview2 --runtime win-x64 --shortcuts StartMenuRoot
```

The first real release is also the first end-to-end test of the updater: tag `v1.0.0`, install it, then tag `v1.0.1` and check that the installed app offers the update.
