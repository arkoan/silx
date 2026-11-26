#!/usr/bin/env bash
# Construit l'application installable macOS : Silx.app autonome (runtime .NET inclus) et une image disque .dmg.
#
#   app/packaging/paquet-macos.sh                      # Apple Silicon, ngspice pris sur le système (Homebrew)
#   NGSPICE_DIR=/chemin/ngspice app/packaging/paquet-macos.sh   # embarque ce ngspice (bin/ngspice, lib/ngspice/*.cm)
#   RID=osx-x64 VERSION=0.2.0 app/packaging/paquet-macos.sh     # Mac Intel, version donnée
#
# Résultat : app/build/Silx.app et app/build/Silx-<version>-<rid>.dmg
set -euo pipefail
ICI="$(cd "$(dirname "$0")" && pwd)"; APPDIR="$(cd "$ICI/.." && pwd)"
RID="${RID:-osx-arm64}"
VERSION="${VERSION:-$(git -C "$APPDIR" describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || true)}"; VERSION="${VERSION:-0.1.0}"
BUILD="${BUILD:-$(git -C "$APPDIR" rev-list --count HEAD 2>/dev/null || echo 1)}"
SORTIE="$APPDIR/build"; PUB="$SORTIE/publish-$RID"; APP="$SORTIE/Silx.app"

echo "▶ Publication .NET autonome ($RID, version $VERSION)"
rm -rf "$PUB" "$APP"
dotnet publish "$APPDIR/src/Silx.App" -c Release -r "$RID" --self-contained true -p:Version="$VERSION" -o "$PUB" -v quiet
echo "▶ Serveur MCP (silx-mcp, à côté de l'application : même runtime, même ngspice embarqué)"
PUBMCP="$SORTIE/publish-mcp-$RID"; rm -rf "$PUBMCP"
dotnet publish "$APPDIR/../mcp/src/Silx.Mcp" -c Release -r "$RID" --self-contained true -p:Version="$VERSION" -o "$PUBMCP" -v quiet

echo "▶ Paquet Silx.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB/." "$APP/Contents/MacOS/"
# Fichiers communs (runtime .NET, cœur Silx) : identiques, on garde ceux de l'application ; une différence est une erreur.
for f in "$PUBMCP"/*; do
  n="$(basename "$f")"; d="$APP/Contents/MacOS/$n"
  if [[ -e "$d" ]]; then cmp -s "$f" "$d" || { echo "✗ $n diffère entre Silx et silx-mcp : versions de dépendances à aligner" >&2; exit 1; }
  else cp -R "$f" "$d"; fi
done
sed -e "s/@VERSION@/$VERSION/" -e "s/@BUILD@/$BUILD/" "$ICI/macos/Info.plist" > "$APP/Contents/Info.plist"
cp "$ICI/macos/Silx.icns" "$APP/Contents/Resources/Silx.icns"
if [[ -n "${NGSPICE_DIR:-}" ]]; then
  echo "▶ ngspice embarqué depuis $NGSPICE_DIR"
  mkdir -p "$APP/Contents/Resources/ngspice"
  cp -R "$NGSPICE_DIR/." "$APP/Contents/Resources/ngspice/"
fi

echo "▶ Signature ad hoc (exigée pour lancer un exécutable sur Apple Silicon)"
codesign --force --deep --sign - "$APP"

echo "▶ Image disque"
DMG="$SORTIE/Silx-$VERSION-$RID.dmg"; TMP="$SORTIE/dmg"
rm -rf "$TMP" "$DMG"; mkdir -p "$TMP"; cp -R "$APP" "$TMP/"; ln -s /Applications "$TMP/Applications"
hdiutil create -volname "Silx $VERSION" -srcfolder "$TMP" -ov -format UDZO "$DMG" > /dev/null
rm -rf "$TMP"
echo "✓ $APP"
echo "✓ $DMG"
