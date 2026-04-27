#!/usr/bin/env bash
# Generates Unity .meta files for every asset Unity expects in this UPM package.
#
# Why: Unity needs .meta files for every script/folder/asmdef/text asset inside a
# package. UPM packages are installed read-only ("immutable folder"), so Unity
# cannot generate them on import — they must be committed to the source repo.
# Without them, Unity prints "no meta file ... will be ignored" and the package
# is unusable.
#
# This script:
#   - Walks the package's Unity-relevant assets.
#   - For each, writes a sibling .meta file with a deterministic GUID derived
#     from the asset's repo-relative path (md5 of the path -> 32 hex chars).
#   - Idempotent: re-running produces the same GUIDs.
#   - Skips assets that already have a .meta file (so any meta already
#     committed wins; in particular, real Unity-generated GUIDs are preserved).
#
# Usage: bash scripts/generate-unity-meta.sh

set -euo pipefail

cd "$(git rev-parse --show-toplevel 2>/dev/null || dirname "$0"/..)"

# Pick the available md5 implementation. Linux/CI has md5sum; macOS ships `md5`.
if command -v md5sum >/dev/null 2>&1; then
    _md5() { md5sum | awk '{print $1}'; }
elif command -v md5 >/dev/null 2>&1; then
    _md5() { md5 -q; }
else
    echo "ERROR: neither md5sum nor md5 found on PATH." >&2
    exit 1
fi

guid_for() {
    # 32-char hex GUID from the repo-relative asset path.
    printf '%s' "$1" | _md5
}

write_if_missing() {
    local meta_path="$1"
    local body="$2"
    if [[ -e "$meta_path" ]]; then
        return 0
    fi
    printf '%s' "$body" > "$meta_path"
    echo "  + $meta_path"
}

folder_meta() {
    local path="$1"
    local guid; guid=$(guid_for "$path")
    write_if_missing "${path}.meta" "fileFormatVersion: 2
guid: ${guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"
}

script_meta() {
    local path="$1"
    local guid; guid=$(guid_for "$path")
    write_if_missing "${path}.meta" "fileFormatVersion: 2
guid: ${guid}
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
"
}

asmdef_meta() {
    local path="$1"
    local guid; guid=$(guid_for "$path")
    write_if_missing "${path}.meta" "fileFormatVersion: 2
guid: ${guid}
AssemblyDefinitionImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"
}

text_meta() {
    local path="$1"
    local guid; guid=$(guid_for "$path")
    write_if_missing "${path}.meta" "fileFormatVersion: 2
guid: ${guid}
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"
}

# .asset (ScriptableObject / serialized native asset)
asset_meta() {
    local path="$1"
    local guid; guid=$(guid_for "$path")
    write_if_missing "${path}.meta" "fileFormatVersion: 2
guid: ${guid}
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
"
}

# .shader
shader_meta() {
    local path="$1"
    local guid; guid=$(guid_for "$path")
    write_if_missing "${path}.meta" "fileFormatVersion: 2
guid: ${guid}
ShaderImporter:
  externalObjects: {}
  defaultTextures: []
  nonModifiableTextures: []
  preprocessorOverride: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"
}

# .compute
compute_meta() {
    local path="$1"
    local guid; guid=$(guid_for "$path")
    write_if_missing "${path}.meta" "fileFormatVersion: 2
guid: ${guid}
ComputeShaderImporter:
  externalObjects: {}
  currentAPIMask: 0
  preprocessorOverride: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"
}

echo "Generating Unity .meta files (idempotent)…"

# ---------- Root-level assets Unity scans inside an installed package ----------
for f in package.json README.md CHANGELOG.md LICENSE.md; do
    [[ -e "$f" ]] && text_meta "$f"
done

# ---------- Walk Unity-shipped folders ----------
# Samples~/ is intentionally excluded — the trailing ~ tells Unity to ignore
# the folder until the user clicks Import in Package Manager, at which point
# Unity copies the contents into Assets/ and generates .meta files itself.
# Excluded subtrees: .NET build artifacts and editor cruft. Anything beneath
# these folders is not part of the shipped Unity package.
EXCLUDE=(
    -name 'bin' -o
    -name 'obj' -o
    -name '.vs' -o
    -name '.vscode' -o
    -name '.idea' -o
    -name 'TestResults' -o
    -name 'artifacts'
)

for root in Runtime Tests Editor; do
    [[ -d "$root" ]] || continue

    # Folder metas (skip the excluded subtrees entirely)
    while IFS= read -r -d '' dir; do
        folder_meta "$dir"
    done < <(find "$root" -type d \( "${EXCLUDE[@]}" \) -prune -o -type d -print0)

    # File metas
    while IFS= read -r -d '' file; do
        case "$file" in
            *.cs)      script_meta "$file" ;;
            *.asmdef)  asmdef_meta "$file" ;;
            *.json|*.md|*.txt|*.xml|*.uss|*.uxml) text_meta "$file" ;;
            *.asset)   asset_meta   "$file" ;;
            *.shader)  shader_meta  "$file" ;;
            *.compute) compute_meta "$file" ;;
            *.meta)    : ;;  # don't meta a meta
            *)         : ;;  # ignore everything else
        esac
    done < <(find "$root" -type d \( "${EXCLUDE[@]}" \) -prune -o -type f -print0)
done

echo "Done."
