#!/usr/bin/env bash
# Build a self-contained .deb for joplin-note-node.
# Usage: ./packaging/build.sh [version]
#   version defaults to the one in DEBIAN/control.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOTNET_DIR="$REPO_ROOT/dotnet"
PKG_DIR="$SCRIPT_DIR"

VERSION="${1:-$(grep '^Version:' "$PKG_DIR/DEBIAN/control" | awk '{print $2}')}"
STAGING="$REPO_ROOT/dist/joplin-note-node_${VERSION}_amd64"

echo "==> Building v$VERSION"

# ── 1. Publish self-contained single-file binary ─────────────────────────────
echo "==> dotnet publish …"
dotnet publish "$DOTNET_DIR" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -o "$REPO_ROOT/dist/publish"

# ── 2. Assemble staging tree ──────────────────────────────────────────────────
echo "==> Assembling staging tree …"
rm -rf "$STAGING"
mkdir -p "$STAGING/DEBIAN"
mkdir -p "$STAGING/usr/bin"
mkdir -p "$STAGING/lib/systemd/system"
mkdir -p "$STAGING/etc/joplin-note-node"

# Update version in control file
sed "s/^Version:.*/Version: $VERSION/" "$PKG_DIR/DEBIAN/control" > "$STAGING/DEBIAN/control"

cp "$PKG_DIR/DEBIAN/conffiles" "$STAGING/DEBIAN/"
cp "$PKG_DIR/DEBIAN/postinst"  "$STAGING/DEBIAN/"
cp "$PKG_DIR/DEBIAN/prerm"     "$STAGING/DEBIAN/"
chmod 755 "$STAGING/DEBIAN/postinst" "$STAGING/DEBIAN/prerm"

cp "$REPO_ROOT/dist/publish/NPS.Demo.JoplinNoteNode" "$STAGING/usr/bin/joplin-note-node"
chmod 755 "$STAGING/usr/bin/joplin-note-node"

cp "$PKG_DIR/lib/systemd/system/joplin-note-node.service" \
   "$STAGING/lib/systemd/system/"

cp "$PKG_DIR/etc/joplin-note-node/env.example" \
   "$STAGING/etc/joplin-note-node/env.example"

# ── 3. Build .deb ─────────────────────────────────────────────────────────────
DEB_OUT="$REPO_ROOT/dist/joplin-note-node_${VERSION}_amd64.deb"
echo "==> dpkg-deb → $DEB_OUT"
dpkg-deb --build --root-owner-group "$STAGING" "$DEB_OUT"

echo ""
echo "Done: $DEB_OUT"
echo ""
echo "Install with:"
echo "  sudo dpkg -i $DEB_OUT"
echo "  sudo apt-get install -f   # fix deps if needed"
echo ""
echo "After install:"
echo "  sudo nano /etc/joplin-note-node/env"
echo "  sudo systemctl start joplin-note-node"
echo "  journalctl -u joplin-note-node -f"
