#!/usr/bin/env bash
# Writes <web root>/repo/pluginmaster.json listing every plugin in <web root>/*/pluginmaster.json, so one custom
# repository URL (https://moonbright.cc/repo/pluginmaster.json) offers all of them. Each plugin's own folder keeps its
# zips and its own pluginmaster.json, so per-plugin repository URLs keep working.
#
#   tools/combine-repos.sh [web root]    (default /var/www/moonbright.cc)
#
# Run after publishing any plugin into the web root. Create the output folder once with:
#   sudo install -d -o "$USER" -g www-data -m 755 /var/www/moonbright.cc/repo
set -euo pipefail

WEB_ROOT="${1:-/var/www/moonbright.cc}"
WEB_ROOT="${WEB_ROOT%/}"

python3 -I - "$WEB_ROOT" <<'PY'
import json, sys
from pathlib import Path

root = Path(sys.argv[1])
out_dir = root / "repo"
entries = {}
for path in sorted(root.glob("*/pluginmaster.json")):
    if path.parent == out_dir:
        continue
    for entry in json.loads(path.read_text(encoding="utf-8-sig")):
        entries[entry["InternalName"]] = entry

tmp = out_dir / "pluginmaster.json.tmp"
tmp.write_text(json.dumps(list(entries.values()), indent=2) + "\n", encoding="utf-8")
tmp.chmod(0o644)
tmp.replace(out_dir / "pluginmaster.json")
print(f"Combined repository lists: {', '.join(entries)}")
PY
