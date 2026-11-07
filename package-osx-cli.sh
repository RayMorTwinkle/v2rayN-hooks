#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 ]]; then
  echo "Usage: $0 <gui_publish_dir> <cli_publish_dir> <version> <output_zip> [bin_source_dir] [readme_file] [core_zip_file]"
  exit 1
fi

GUI_DIR="$1"
CLI_DIR="$2"
VERSION="$3"
OUTPUT_ZIP="$4"
BIN_SOURCE_DIR="${5:-}"
README_FILE="${6:-}"
CORE_ZIP_FILE="${7:-}"

[[ -d "$GUI_DIR" ]] || { echo "GUI publish dir not found: $GUI_DIR"; exit 1; }
[[ -d "$CLI_DIR" ]] || { echo "CLI publish dir not found: $CLI_DIR"; exit 1; }
[[ -f "$GUI_DIR/v2rayN" ]] || { echo "Missing GUI binary: $GUI_DIR/v2rayN"; exit 1; }
[[ -f "$CLI_DIR/v2rayN.Cli" ]] || { echo "Missing CLI binary: $CLI_DIR/v2rayN.Cli"; exit 1; }

ensure_core_bin() {
  normalize_core_bin_permissions() {
    local target_bin="$1"
    local f
    for f in \
      "$target_bin/xray/xray" \
      "$target_bin/sing_box/sing-box" \
      "$target_bin/mihomo/mihomo"
    do
      if [[ -f "$f" ]]; then
        chmod +x "$f" || true
      fi
    done
  }

  has_valid_core_bin() {
    local target_bin="$1"
    [[ -d "$target_bin" ]] || return 1
    [[ -f "$target_bin/xray/xray" || -f "$target_bin/sing_box/sing-box" || -f "$target_bin/mihomo/mihomo" ]]
  }

  normalize_core_bin_permissions "$GUI_DIR/bin"
  if has_valid_core_bin "$GUI_DIR/bin"; then
    return 0
  fi
  if [[ -d "$GUI_DIR/bin" ]]; then
    echo "Found existing bin directory but it does not contain executable core files. Rebuilding bin..."
    rm -rf "$GUI_DIR/bin"
  fi

  if [[ -n "$BIN_SOURCE_DIR" ]]; then
    if [[ -d "$BIN_SOURCE_DIR" ]]; then
      echo "Using local bin source: $BIN_SOURCE_DIR"
      cp -Rf "$BIN_SOURCE_DIR" "$GUI_DIR/bin"
      normalize_core_bin_permissions "$GUI_DIR/bin"
      if ! has_valid_core_bin "$GUI_DIR/bin"; then
        echo "Local bin source is invalid: no executable core files found."
        rm -rf "$GUI_DIR/bin"
        exit 1
      fi
      return 0
    else
      echo "Specified bin source dir not found: $BIN_SOURCE_DIR"
      exit 1
    fi
  fi

  if [[ -n "$CORE_ZIP_FILE" ]]; then
    if [[ ! -f "$CORE_ZIP_FILE" ]]; then
      echo "Specified core zip file not found: $CORE_ZIP_FILE"
      exit 1
    fi

    local arch_from_zip tmpzip
    arch_from_zip="$(basename "$CORE_ZIP_FILE")"
    arch_from_zip="${arch_from_zip#v2rayN-}"
    arch_from_zip="${arch_from_zip%.zip}"
    tmpzip="$(mktemp -d)"

    unzip -q "$CORE_ZIP_FILE" -d "$tmpzip"
    if [[ -d "$tmpzip/v2rayN-${arch_from_zip}/bin" ]]; then
      cp -Rf "$tmpzip/v2rayN-${arch_from_zip}/bin" "$GUI_DIR/bin"
    elif [[ -d "$tmpzip/bin" ]]; then
      cp -Rf "$tmpzip/bin" "$GUI_DIR/bin"
    else
      echo "Specified core zip is invalid: cannot locate bin directory inside archive."
      rm -rf "$tmpzip"
      exit 1
    fi

    normalize_core_bin_permissions "$GUI_DIR/bin"
    if ! has_valid_core_bin "$GUI_DIR/bin"; then
      echo "Specified core zip is invalid: no executable core files found."
      rm -rf "$tmpzip"
      exit 1
    fi
    rm -rf "$tmpzip"
    return 0
  fi

  local arch="macos-64"
  local file_info
  file_info="$(file "$GUI_DIR/v2rayN" 2>/dev/null || true)"
  if echo "$file_info" | grep -Eiq "arm64|aarch64"; then
    arch="macos-arm64"
  fi

  local core_zip="v2rayN-${arch}.zip"
  local core_url="https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/${core_zip}"
  local tmpdir
  tmpdir="$(mktemp -d)"

  echo "bin directory not found in GUI publish output. Downloading core bundle: ${core_url}"
  if command -v curl >/dev/null 2>&1; then
    curl -fL \
      --retry 3 \
      --retry-delay 2 \
      --connect-timeout 20 \
      --max-time 600 \
      "$core_url" -o "$tmpdir/$core_zip"
  elif command -v wget >/dev/null 2>&1; then
    wget -nv \
      --tries=3 \
      --timeout=20 \
      -O "$tmpdir/$core_zip" "$core_url"
  else
    echo "Neither curl nor wget is available to download core bundle."
    rm -rf "$tmpdir"
    exit 1
  fi

  unzip -q "$tmpdir/$core_zip" -d "$tmpdir"
  if [[ ! -d "$tmpdir/v2rayN-${arch}/bin" ]]; then
    echo "Downloaded core bundle is invalid: bin directory not found."
    rm -rf "$tmpdir"
    exit 1
  fi

  cp -Rf "$tmpdir/v2rayN-${arch}/bin" "$GUI_DIR/bin"
  normalize_core_bin_permissions "$GUI_DIR/bin"
  if ! has_valid_core_bin "$GUI_DIR/bin"; then
    echo "Downloaded core bundle is invalid: no executable core files found."
    rm -rf "$tmpdir"
    exit 1
  fi
  rm -rf "$tmpdir"
}

ensure_core_bin

WORK_DIR="$(mktemp -d)"
APP_ROOT="$WORK_DIR/v2rayN.app"
MACOS_DIR="$APP_ROOT/Contents/MacOS"
RES_DIR="$APP_ROOT/Contents/Resources"

mkdir -p "$MACOS_DIR" "$RES_DIR"

cp -Rf "$GUI_DIR/." "$MACOS_DIR/"
cp -f "$CLI_DIR/v2rayN.Cli" "$MACOS_DIR/v2rayN.Cli"
chmod +x "$MACOS_DIR/v2rayN" "$MACOS_DIR/v2rayN.Cli"

if [[ -f "$MACOS_DIR/v2rayN.icns" ]]; then
  cp -f "$MACOS_DIR/v2rayN.icns" "$RES_DIR/AppIcon.icns"
fi

cat > "$MACOS_DIR/v2n" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
export V2RAYN_LOCAL_APPLICATION_DATA_V2="${V2RAYN_LOCAL_APPLICATION_DATA_V2:-1}"
exec "$SCRIPT_DIR/v2rayN.Cli" "$@"
EOF
chmod +x "$MACOS_DIR/v2n"

echo "When this file exists, app will not store configs under this folder" > "$MACOS_DIR/NotStoreConfigHere.txt"

cat > "$APP_ROOT/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>English</string>
  <key>CFBundleDisplayName</key>
  <string>v2rayN</string>
  <key>CFBundleExecutable</key>
  <string>v2rayN</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleIconName</key>
  <string>AppIcon</string>
  <key>CFBundleIdentifier</key>
  <string>2dust.v2rayN</string>
  <key>CFBundleName</key>
  <string>v2rayN</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${VERSION}</string>
  <key>CSResourcesFileMapped</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>LSMinimumSystemVersion</key>
  <string>12.7</string>
</dict>
</plist>
EOF

if [[ -n "$README_FILE" ]]; then
  if [[ -f "$README_FILE" ]]; then
    cp -f "$README_FILE" "$WORK_DIR/$(basename "$README_FILE")"
  else
    echo "README file not found: $README_FILE"
    exit 1
  fi
fi

mkdir -p "$(dirname "$OUTPUT_ZIP")"
PACKAGE_ROOT="$WORK_DIR/package"
mkdir -p "$PACKAGE_ROOT"
cp -Rf "$APP_ROOT" "$PACKAGE_ROOT/v2rayN.app"
if [[ -n "$README_FILE" ]]; then
  cp -f "$WORK_DIR/$(basename "$README_FILE")" "$PACKAGE_ROOT/$(basename "$README_FILE")"
fi
(
  cd "$PACKAGE_ROOT"
  ditto -c -k --sequesterRsrc "." "$OUTPUT_ZIP"
)

echo "Package created: $OUTPUT_ZIP"
rm -rf "$WORK_DIR"
