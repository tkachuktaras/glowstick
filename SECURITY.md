# Security

Glowstick runs with administrator rights and installs drivers, so please report vulnerabilities privately through [GitHub Security Advisories](../../security/advisories/new), not in public issues.

In place:

- Releases are built by GitHub Actions from this repository, with `SHA256.txt` next to the exe.
- Driver installers are signature-checked before they run.
- Updates download only when you click **Update** and are checksum-verified before they replace the exe.
- Starting without UAC goes through a copy in `C:\Program Files\Glowstick`, which only administrators can change.
- Links and programs Glowstick opens run with your normal rights.
- The Pro local API listens on 127.0.0.1 only and requires a token.
