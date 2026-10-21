# Packaging

Manifests for package managers. Update the version, URL and SHA-256 (from the release's `SHA256.txt`) on every release.

## winget

1. Validate locally: `winget validate --manifest packaging\winget`
2. Test install: `winget settings --enable LocalManifestFiles`, then `winget install --manifest packaging\winget`
3. Submit: fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs), copy the three files to
   `manifests/t/tkachuktaras/Glowstick/1.0.0/` and open a pull request.
   Or let [wingetcreate](https://github.com/microsoft/winget-create) do it:
   `wingetcreate submit --token <github-token> packaging\winget`
4. For later versions: `wingetcreate update tkachuktaras.Glowstick --version X.Y.Z --urls <exe url> --submit`

After it's merged: `winget install Glowstick`.

## Scoop

Scoop's main buckets want some popularity first, so start with your own bucket:

1. Create a repository `tkachuktaras/scoop-bucket`, put `glowstick.json` into `bucket/`.
2. Users install with:
   ```
   scoop bucket add glowstick https://github.com/tkachuktaras/scoop-bucket
   scoop install glowstick/glowstick
   ```
3. `checkver` + `autoupdate` let Scoop's update tooling bump the version from GitHub releases automatically.

Later, propose it to the [Extras](https://github.com/ScoopInstaller/Extras) bucket.
