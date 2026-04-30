# Security policy

## Supported versions

SitePix is distributed as standalone packages from each tagged release.
Only the latest minor release receives security fixes:

| Version | Supported |
|---|---|
| 1.0.x   | ✅ |
| < 1.0   | ❌ |

## Reporting a vulnerability

**Do not file public issues for security bugs.**

Use GitHub's private vulnerability reporting:
<https://github.com/alexreich/SitePix/security/advisories/new>

Or email **alex@alexreich.com** with `[sitepix-security]` in the
subject. Include:
- A clear description of the issue
- Steps to reproduce, or a proof-of-concept
- The version you tested against (`sitepix --version` or the package
  filename you installed)
- Anything you'd like credited in the advisory

Acknowledgement target is **3 business days**. A fix or mitigation
plan target is **14 days** for confirmed issues. We'll publish a
GitHub Security Advisory and credit reporters who'd like that.

## Scope

In scope:
- The SitePix binary itself (.NET code in `SitePix/`).
- The packaging scripts in `packaging/` (Inno installer, Chocolatey
  scripts, Linux postinst/prerm, Homebrew formula).
- The release workflow in `.github/workflows/release.yml`.
- Bundled sample profiles in `samples/`.

Out of scope:
- Vulnerabilities in third-party dependencies (Playwright, SkiaSharp,
  HtmlAgilityPack, etc.) — please report those upstream. We'll bump
  the dependency once the upstream fix lands.
- Vulnerabilities in the source sites SitePix scrapes. SitePix only
  reads what those sites publish; it does not log into them or submit
  data.
- Issues that require physical or local-admin access to the user's
  machine.
