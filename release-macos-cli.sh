#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/v2rayN"

ARCH="x64"                  # x64 | arm64
CONFIGURATION="Release"     # Release | Debug
VERSION=""                  # e.g. 7.20.1
BIN_SOURCE_DIR=""           # optional local bin override
README_FILE=""              # optional file to include in zip
CORE_ZIP_FILE=""            # optional local core zip override
SKIP_PUBLISH=0

usage() {
  cat <<'EOF'
Usage:
  ./release-macos-cli.sh --version <ver> [options]

Options:
  --version <ver>          Package version label, e.g. 7.20.1 (required)
  --arch <x64|arm64>       Target architecture (default: x64)
  --configuration <cfg>    Build configuration (default: Release)
  --bin-source <dir>       Optional local bin directory to use instead of downloading official core bundle
  --core-zip <file>        Optional local core zip file (e.g. v2rayN-macos-64.zip)
  --readme <file>          Optional README file to include in output zip
  --skip-publish           Skip dotnet publish and use existing Release outputs
  -h, --help               Show help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --arch) ARCH="${2:-}"; shift 2 ;;
    --configuration) CONFIGURATION="${2:-}"; shift 2 ;;
    --bin-source) BIN_SOURCE_DIR="${2:-}"; shift 2 ;;
    --core-zip) CORE_ZIP_FILE="${2:-}"; shift 2 ;;
    --readme) README_FILE="${2:-}"; shift 2 ;;
    --skip-publish) SKIP_PUBLISH=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *)
      echo "Unknown option: $1"
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$VERSION" ]]; then
  echo "--version is required."
  usage
  exit 1
fi

case "$ARCH" in
  x64)
    RID="osx-x64"
    OUT_TAG="macos-64"
    ;;
  arm64)
    RID="osx-arm64"
    OUT_TAG="macos-arm64"
    ;;
  *)
    echo "Unsupported --arch '$ARCH'. Use x64 or arm64."
    exit 1
    ;;
esac

GUI_OUT="$PROJECT_DIR/Release/$OUT_TAG"
CLI_OUT="$PROJECT_DIR/Release/cli-$OUT_TAG"
ZIP_OUT="$PROJECT_DIR/Release/v2rayN-$OUT_TAG-with-cli.zip"

if [[ -z "$README_FILE" ]]; then
  if [[ -f "$SCRIPT_DIR/README_CN.md" ]]; then
    README_FILE="$SCRIPT_DIR/README_CN.md"
  elif [[ -f "$SCRIPT_DIR/README.md" ]]; then
    README_FILE="$SCRIPT_DIR/README.md"
  fi
fi

echo "[1/3] Target: arch=$ARCH rid=$RID configuration=$CONFIGURATION version=$VERSION"

if [[ "$SKIP_PUBLISH" -ne 1 ]]; then
  echo "[2/3] Publishing GUI and CLI..."
  dotnet publish "$PROJECT_DIR/v2rayN.Desktop/v2rayN.Desktop.csproj" \
    -c "$CONFIGURATION" -r "$RID" -p:SelfContained=true -o "$GUI_OUT"

  dotnet publish "$PROJECT_DIR/v2rayN.Cli/v2rayN.Cli.csproj" \
    -c "$CONFIGURATION" -r "$RID" -p:SelfContained=true -o "$CLI_OUT"
else
  echo "[2/3] Skip publish enabled. Using existing outputs."
fi

echo "[3/3] Packaging app + CLI..."
"$SCRIPT_DIR/package-osx-cli.sh" "$GUI_OUT" "$CLI_OUT" "$VERSION" "$ZIP_OUT" "$BIN_SOURCE_DIR" "$README_FILE" "$CORE_ZIP_FILE"

echo
echo "Done."
echo "Output zip: $ZIP_OUT"
echo "Contains: v2rayN.app + bundled v2rayN.Cli + v2n + README"
