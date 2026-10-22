# Security

Glowstick runs with administrator rights and installs drivers (ViGEmBus, HidHide), so vulnerabilities are taken seriously.

If you found one, **don't open a public issue** — report it privately through this repository's [GitHub Security Advisories](../../security/advisories/new) (“Report a vulnerability”).

What's already in place:

- The driver installers are embedded in the exe; their SHA-256 is checked at build time and the Nefarius Software Solutions digital signature is verified before they run.
- Releases are built by GitHub Actions from this repository, with `SHA256.txt` published next to the exe. The build downloads its compiler (Roslyn, from NuGet) pinned by version and SHA-256.
- Updates: once a day Glowstick asks the GitHub API whether a newer release exists and only tells you. Nothing is downloaded until you click **Update**; then the new exe is downloaded next to the current one, checked against the release's `SHA256.txt`, and only then swapped in (the old one is kept as `.old` until the new one starts).
- Starting without UAC goes through a scheduled task that runs a copy kept in `C:\Program Files\Glowstick` (writable only by administrators) — never the exe you downloaded, which other programs could replace.
- What Glowstick opens for you — links, programs from a “launch” action — starts with your normal rights, not administrator ones.
- The Pro local API listens on 127.0.0.1 only, requires a token, and limits request size.
