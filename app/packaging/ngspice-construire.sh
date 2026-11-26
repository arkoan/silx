#!/usr/bin/env bash
# Compile ngspice pour l'embarquer dans Silx.app : sans X11 ni readline ni OpenMP (aucune bibliothèque hors système),
# avec XSPICE (composants logiques) et CIDER. Utilisé par la CI ; sortie : <dossier>/bin/ngspice, lib/ngspice/*.cm.
#
#   app/packaging/ngspice-construire.sh <dossier de sortie> [version]
set -euo pipefail
SORTIE="$(mkdir -p "$1" && cd "$1" && pwd)"; VERSION="${2:-47}"
TRAVAIL="$(mktemp -d)"
curl -fsSL -o "$TRAVAIL/ngspice.tar.gz" "https://downloads.sourceforge.net/project/ngspice/ng-spice-rework/$VERSION/ngspice-$VERSION.tar.gz"
tar -xzf "$TRAVAIL/ngspice.tar.gz" -C "$TRAVAIL"
cd "$TRAVAIL/ngspice-$VERSION"
./configure --prefix="$SORTIE" --without-x --with-readline=no --disable-openmp --with-fftw3=no \
  --enable-xspice --enable-cider --disable-debug CFLAGS="-O2" > configure.log
make -j"$(sysctl -n hw.ncpu 2>/dev/null || nproc)" > make.log
make install > install.log
echo "Dépendances de ngspice :"; otool -L "$SORTIE/bin/ngspice" 2>/dev/null || ldd "$SORTIE/bin/ngspice" || true
ls "$SORTIE/lib/ngspice"
rm -rf "$TRAVAIL"
