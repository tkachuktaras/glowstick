# Security

Glowstick runs with administrator rights and installs drivers (ViGEmBus, HidHide), so vulnerabilities are taken seriously.

If you found one, **don't open a public issue** — report it privately through this repository's [GitHub Security Advisories](../../security/advisories/new) (“Report a vulnerability”).

What's already in place:

- The driver installers are embedded in the exe; their SHA-256 is checked at build time and the Nefarius Software Solutions digital signature is verified before they run.
- Releases are built by GitHub Actions from this repository, with `SHA256.txt` published next to the exe.
- The update check only reads the latest release from the GitHub API and shows a link — it never downloads or runs anything.
