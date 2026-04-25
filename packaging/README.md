# Packaging

Everything needed to cut a release and ship it to Windows / macOS / Linux package managers.

```
packaging/
├── inno/        Windows installer (Inno Setup) — source of the .exe
├── winget/      winget-pkgs manifest templates
├── chocolatey/  Chocolatey .nuspec + install scripts
├── homebrew/    Homebrew formula template
├── linux/       postinst / prerm / sitepix-install-schedule — used by .deb/.rpm/install.sh
└── README.md    (this file)
```

Tokens — `__VERSION__`, `__SHA256__`, `__SHA_X64__`, `__SHA_ARM__`, `__RELEASE_DATE__` — are substituted by [`../.github/workflows/release.yml`](../.github/workflows/release.yml) at build time. Files here stay token-shaped on disk so they're easy to diff across releases.

---

## Cutting a release

1. **Bump the version.** Edit `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, and `<InformationalVersion>` in [`../SitePix/SitePix.csproj`](../SitePix/SitePix.csproj). Commit.
2. **Tag it.**
   ```bash
   git tag v1.1.0
   git push origin v1.1.0
   ```
3. **CI does the rest.** The `Release` workflow fans out across three runners:

   | Job | Runner | Produces |
   |---|---|---|
   | `build-windows` | `windows-latest` | Inno installer `.exe`, portable `.zip`, winget manifests, Chocolatey `.nupkg` |
   | `build-linux`   | `ubuntu-latest`  | `linux-x64`/`arm64` tarballs, AppImage, `.deb` × 2, `.rpm` × 2 |
   | `build-macos`   | `macos-latest`   | `osx-x64`/`arm64` tarballs + rendered Homebrew formula |
   | `release`       | `ubuntu-latest`  | Gathers every artifact, creates the GitHub Release, and pushes to the package-manager feeds whose secrets are configured. |

   A version mismatch between the git tag and the csproj `<Version>` fails the build — that's intentional.

---

## One-time setup per package manager

First release for each channel is manual; subsequent releases auto-update via CI secrets.

### winget (microsoft/winget-pkgs)

1. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
2. Download `SitePix-<version>-winget-manifests.zip` from the first Release. Extract and copy `manifests/a/AlexReich/SitePix/<version>/` into your fork, commit, open a PR against `microsoft/winget-pkgs:master`. Title: `New package: AlexReich.SitePix version <version>`.
3. For **subsequent releases**: create a [fine-grained PAT](https://github.com/settings/tokens?type=beta) with `Contents: Read & Write` on your `winget-pkgs` fork. Add it as repo secret `WINGET_TOKEN`. [Komac](https://github.com/russellbanks/Komac) runs in CI and opens the PR for you.

### Chocolatey community feed

1. Register at [community.chocolatey.org](https://community.chocolatey.org/) and grab your API key from [your profile](https://community.chocolatey.org/account).
2. Add it as repo secret `CHOCO_API_KEY`.
3. The first `.nupkg` goes through human moderation (days to weeks). Subsequent versions auto-publish via `dotnet nuget push` in CI.

### Homebrew tap

Homebrew has two options:
- **Homebrew-core** (`brew install sitepix` with no tap) — requires the package to meet their popularity / maintenance bar. Not realistic for a niche app today.
- **Your own tap** — `brew install yourname/tap/sitepix`. This is what we target.

Setup:

1. Create a **new public repo** called `homebrew-tap` under your GitHub account — the prefix is mandatory. Empty is fine; CI populates it. The final URL will look like `github.com/alexreich/homebrew-tap`.
2. In this repo's **Settings → Secrets and variables → Actions**:
   - Add repo variable `HOMEBREW_TAP_REPO` = `alexreich/homebrew-tap`.
   - Add repo secret `HOMEBREW_TAP_TOKEN` = a [fine-grained PAT](https://github.com/settings/tokens?type=beta) with `Contents: Read & Write` on the tap repo.
3. First release pushes `Formula/sitepix.rb` into the tap repo with SHAs for macOS arm64 and x64 pre-filled.
4. Users then: `brew install alexreich/tap/sitepix` (or `brew tap alexreich/tap && brew install sitepix`).

### Linux distro repositories (optional, not automated)

The CI release attaches `.deb` and `.rpm` to every GitHub Release. Distributions are on their own — we don't push to APT / DNF / Flatpak / Snap in this version because each has heavyweight submission requirements.

Users who want true `apt install sitepix` have two paths:

- **Manual install from GitHub**: `sudo dpkg -i sitepix_<v>_amd64.deb`. Good enough for most.
- **Self-hosted apt/dnf repo**: host the `.deb`/`.rpm` somewhere, sign the index, add a `sources.list.d` entry. Future work.

---

## Building locally

### Windows installer

```powershell
dotnet publish SitePix/SitePix.csproj `
  -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

iscc.exe /DAppVersion=1.0.0 `
  /DPublishDir="$((Resolve-Path 'SitePix/bin/Release/net10.0/win-x64/publish').Path)" `
  packaging\inno\SitePix.iss
# Output: packaging\inno\Output\SitePix-Setup-1.0.0.exe
```

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

### Linux tarball + .deb

```bash
dotnet publish SitePix/SitePix.csproj \
  -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64

# Tarball
tar -C publish/linux-x64 -czf SitePix-1.0.0-linux-x64.tar.gz .

# .deb (requires fpm: `sudo gem install fpm`)
ROOT=build/deb-root
rm -rf "$ROOT"
mkdir -p "$ROOT/opt/sitepix" "$ROOT/usr/bin"
cp -r publish/linux-x64/. "$ROOT/opt/sitepix/"
ln -sf /opt/sitepix/SitePix "$ROOT/usr/bin/sitepix"
cp packaging/linux/sitepix-install-schedule "$ROOT/usr/bin/"
fpm -s dir -t deb -a amd64 -n sitepix -v 1.0.0 \
    --after-install packaging/linux/postinst.sh \
    --before-remove packaging/linux/prerm.sh \
    -C "$ROOT" .
```

### macOS tarball

```bash
dotnet publish SitePix/SitePix.csproj \
  -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish/osx-arm64
tar -C publish/osx-arm64 -czf SitePix-1.0.0-osx-arm64.tar.gz .
```

For a full from-clean-machine bootstrap that installs .NET, Playwright Chromium, and the LaunchAgent, see [`../macos/install.sh`](../macos/install.sh).

---

## Repository secrets reference

| Name | Type | Purpose | Required? |
|---|---|---|---|
| `GITHUB_TOKEN` | provided | `gh release create/edit/upload` | automatic |
| `CHOCO_API_KEY` | secret | `dotnet nuget push` → Chocolatey feed | optional |
| `WINGET_TOKEN` | secret | Komac auto-PR to `microsoft/winget-pkgs` | optional |
| `HOMEBREW_TAP_TOKEN` | secret | Commit rendered formula to the tap repo | optional |
| `HOMEBREW_TAP_REPO` | variable | `owner/repo` of the tap (e.g. `alexreich/homebrew-tap`) | optional |

Missing any of `CHOCO_API_KEY` / `WINGET_TOKEN` / `HOMEBREW_TAP_TOKEN` is non-fatal — CI just skips that publish step. Artifacts are always attached to the GitHub Release so you can publish manually.

---

## Why these specific formats

- **Inno Setup** — industry-standard for free Windows installers. Real Add/Remove Programs entry, silent-install flags, and Chocolatey/winget can both chain to it.
- **winget** — Microsoft's first-party, shipped with Windows 11.
- **Chocolatey** — the older, larger, enterprise-leaning option.
- **Homebrew** — all macOS users have it or can install it in 30 seconds.
- **AppImage** — the only Linux format that genuinely runs on any distro with no install.
- **.deb / .rpm** — native package managers for the two largest Linux families. Still the preferred path for users who want `apt remove sitepix` to work.
- **Portable tarballs / zips** — restricted environments, containers, and CI.

Anything fancier (Flatpak, Snap, AUR, MSIX, Scoop, PKG) is explicitly out of scope — the set above already covers ~95% of users.
