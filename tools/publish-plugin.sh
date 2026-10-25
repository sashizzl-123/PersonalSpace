#!/usr/bin/env bash
# Builds the plugin in Release and publishes it to the custom Dalamud repository:
# repo/PersonalSpace-<version>.zip plus repo/pluginmaster.json, copied to WEB_DIR so nginx serves them at
# $PUBLIC_URL/pluginmaster.json. Then rebuilds the combined repository at $COMBINED_DIR/repo/pluginmaster.json, which lists
# every plugin published under it (HealthBar too). Modeled on HealthBar/tools/publish-plugin.sh.
#
#   tools/publish-plugin.sh
#
# Settings (environment):
#   PUBLIC_URL    where WEB_DIR is served (default https://moonbright.cc/personalspace)
#   WEB_DIR       web root folder to publish into (default /var/www/moonbright.cc/personalspace);
#                 create it once with: sudo install -d -o "$USER" -g www-data -m 755 /var/www/moonbright.cc/personalspace
#   COMBINED_DIR  web root holding one folder per plugin (default: the parent of WEB_DIR)
#   PLUGIN_VERSION  version to build as, overriding <Version> in the .csproj (the server's deploy-plugin sets it)
#   DALAMUD_HOME  Dalamud's dev folder to build against. If unset on Linux and ~/.xlcore has none,
#                 the latest Dalamud release is downloaded to ~/.cache/personalspace/dalamud.
#
# Run by hand, bump <Version> in PersonalSpace/PersonalSpace.csproj first, or players won't be offered the update.
set -euo pipefail
cd "$(dirname "$0")/.."

PUBLIC_URL="${PUBLIC_URL:-https://moonbright.cc/personalspace}"
PUBLIC_URL="${PUBLIC_URL%/}"
WEB_DIR="${WEB_DIR:-/var/www/moonbright.cc/personalspace}"
WEB_DIR="${WEB_DIR%/}"
COMBINED_DIR="${COMBINED_DIR:-$(dirname "$WEB_DIR")}"
REPO_DIR="repo"

build_args=(-c Release -p:Platform=x64)
if [[ -n "${PLUGIN_VERSION:-}" ]]; then
    build_args+=(-p:Version="$PLUGIN_VERSION")
fi
if [[ "$(uname -s)" != MINGW* && "$(uname -s)" != MSYS* ]]; then
    # Building a Windows plugin on Linux: fetch Dalamud's reference assemblies if needed.
    build_args+=(-p:EnableWindowsTargeting=true)
    if [[ -z "${DALAMUD_HOME:-}" && ! -f "$HOME/.xlcore/dalamud/Hooks/dev/Dalamud.dll" ]]; then
        DALAMUD_HOME="$HOME/.cache/personalspace/dalamud/"
        if [[ ! -f "$DALAMUD_HOME/Dalamud.dll" ]]; then
            echo "Downloading Dalamud reference assemblies to $DALAMUD_HOME"
            mkdir -p "$DALAMUD_HOME"
            curl -fsSL -o "$DALAMUD_HOME/latest.zip" https://goatcorp.github.io/dalamud-distrib/latest.zip
            python3 -I -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$DALAMUD_HOME/latest.zip" "$DALAMUD_HOME"
        fi
        export DALAMUD_HOME
    fi
fi

if [[ ! -d "$WEB_DIR" || ! -w "$WEB_DIR" ]]; then
    echo "$WEB_DIR is missing or not writable. Create it once with:" >&2
    echo "  sudo install -d -o \"$USER\" -g www-data -m 755 \"$WEB_DIR\"" >&2
    exit 1
fi

dotnet build PersonalSpace/PersonalSpace.csproj "${build_args[@]}"

# DalamudPackager writes bin/x64/Release/PersonalSpace/ (or bin/Release/PersonalSpace/ without a platform).
package_dir="$(dirname "$(ls -t PersonalSpace/bin/*/PersonalSpace/latest.zip PersonalSpace/bin/*/*/PersonalSpace/latest.zip 2>/dev/null | head -1)")"
if [[ ! -f "$package_dir/latest.zip" ]]; then
    echo "Couldn't find the packaged latest.zip under PersonalSpace/bin" >&2
    exit 1
fi

mkdir -p "$REPO_DIR"
python3 -I - "$package_dir/PersonalSpace.json" "$package_dir/latest.zip" "$REPO_DIR" "$PUBLIC_URL" <<'PY'
import json, shutil, sys, time
from pathlib import Path

manifest_path, zip_path, repo_dir, public_url = sys.argv[1:5]
manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8-sig"))
version = manifest["AssemblyVersion"]

# A new file name per version, so no cache anywhere can hand out an old build.
zip_name = f"PersonalSpace-{version}.zip"
shutil.copyfile(zip_path, Path(repo_dir) / zip_name)
link = f"{public_url}/{zip_name}"

entry = dict(manifest)
entry.update({
    "DownloadLinkInstall": link,
    "DownloadLinkUpdate": link,
    "DownloadLinkTesting": link,
    "IsHide": False,
    "IsTestingExclusive": False,
    "LastUpdate": str(int(time.time())),
})
(Path(repo_dir) / "pluginmaster.json").write_text(json.dumps([entry], indent=2) + "\n", encoding="utf-8")

# Keep the three newest zips; players only ever download the newest.
zips = sorted(Path(repo_dir).glob("PersonalSpace-*.zip"), key=lambda p: p.stat().st_mtime, reverse=True)
for old in zips[3:]:
    old.unlink()

print(f"Packaged Personal Space {version}: {link}")
PY

# Mirror repo/ into the web root: zip first, then the manifest that points at it, then prune.
cp -p "$REPO_DIR"/PersonalSpace-*.zip "$WEB_DIR"/
cp -p "$REPO_DIR"/pluginmaster.json "$WEB_DIR"/pluginmaster.json.tmp
mv -f "$WEB_DIR"/pluginmaster.json.tmp "$WEB_DIR"/pluginmaster.json
for published in "$WEB_DIR"/PersonalSpace-*.zip; do
    [[ -f "$REPO_DIR/$(basename "$published")" ]] || rm -f "$published"
done
chmod 644 "$WEB_DIR"/pluginmaster.json "$WEB_DIR"/PersonalSpace-*.zip

tools/combine-repos.sh "$COMBINED_DIR"
echo "Custom repository URL: $PUBLIC_URL/pluginmaster.json"
