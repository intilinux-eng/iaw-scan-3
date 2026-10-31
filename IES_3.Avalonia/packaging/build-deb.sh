#!/bin/bash
# Builds the IAW Scan 3 desktop application's Debian package.
#
#   IES_3.Avalonia/packaging/build-deb.sh <version> [runtime]
#
# <version>  e.g. 0.2.0-beta (becomes 0.2.0~beta, so pre-releases sort before 0.2.0)
# [runtime]  .NET runtime identifier, default linux-x64 (linux-arm64 for a Raspberry Pi and the like)
#
# Needs the .NET 10 SDK and dpkg-deb (any Debian/Ubuntu, WSL included). Output: packaging/out/.
# With PUBLISH_DIR set, packages that existing `dotnet publish` output instead of publishing.
set -euo pipefail

VERSION=${1:?usage: build-deb.sh <version> [runtime]}
RID=${2:-linux-x64}
case $RID in
    linux-x64) ARCH=amd64 ;;
    linux-arm64) ARCH=arm64 ;;
    *) echo "unsupported runtime: $RID" >&2; exit 1 ;;
esac

HERE=$(cd "$(dirname "$0")" && pwd)
PROJECT=$HERE/../IES_3.Avalonia.csproj
DEB_VERSION=${VERSION/-/\~}
PACKAGE=iaw-scan3

# Staged on a Linux filesystem: from a Windows checkout under WSL (/mnt/c) every file would show
# up as mode 777, which dpkg-deb refuses.
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
PKGROOT=$WORK/pkgroot
mkdir -p "$PKGROOT/DEBIAN" "$PKGROOT/opt/$PACKAGE"

PUBLISH_DIR=${PUBLISH_DIR:-}
if [ -z "$PUBLISH_DIR" ]; then
    # Everything else comes from the project's published-build settings. Native libraries stay
    # next to the program: installed under /opt, there's no point in extracting them per user.
    PUBLISH_DIR=$WORK/publish
    dotnet publish "$PROJECT" -c Release -r "$RID" -p:Version="$VERSION" \
        -p:IncludeNativeLibrariesForSelfExtract=false -o "$PUBLISH_DIR"
fi

cp -r "$PUBLISH_DIR/." "$PKGROOT/opt/$PACKAGE/"
chmod -R u=rwX,go=rX "$PKGROOT/opt/$PACKAGE"
find "$PKGROOT/opt/$PACKAGE" -type f -exec chmod 644 {} +
chmod 755 "$PKGROOT/opt/$PACKAGE/IES_3.Avalonia"
mkdir -p "$PKGROOT/usr/bin"
ln -s "/opt/$PACKAGE/IES_3.Avalonia" "$PKGROOT/usr/bin/$PACKAGE"

install -D -m 644 "$HERE/../Assets/app.png" "$PKGROOT/usr/share/icons/hicolor/256x256/apps/$PACKAGE.png"
install -D -m 644 "$HERE/iaw-scan3.desktop" "$PKGROOT/usr/share/applications/$PACKAGE.desktop"
install -D -m 644 "$HERE/60-iaw-scan3.rules" "$PKGROOT/usr/lib/udev/rules.d/60-$PACKAGE.rules"
install -D -m 644 "$HERE/../../license.txt" "$PKGROOT/usr/share/doc/$PACKAGE/copyright"

cat >"$PKGROOT/DEBIAN/control" <<EOF
Package: $PACKAGE
Version: $DEB_VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libfontconfig1, libfreetype6, libx11-6, libice6, libsm6
Maintainer: IAW Scan 3 project <https://github.com/intilinux-eng/iaw-scan-3>
Homepage: https://github.com/intilinux-eng/iaw-scan-3
Description: IAW Scan 3 (BETA) - diagnostics for Fiat/Lancia/Alfa OBD-I engines
 Diagnostic software for Marelli IAW engine control units and the FIAT CODE
 immobiliser over a USB K-line cable: live parameters and gauges, fault codes,
 actuator tests, adjustments, graphs and a log of every session. To use the
 cable without root, add your user to the dialout group.
EOF

cat >"$PKGROOT/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    udevadm control --reload-rules >/dev/null 2>&1 || true
    udevadm trigger --subsystem-match=usb-serial --action=add >/dev/null 2>&1 || true
fi
POSTINST
chmod 755 "$PKGROOT/DEBIAN/postinst"

mkdir -p "$HERE/out"
DEB=$HERE/out/${PACKAGE}_${DEB_VERSION}_${ARCH}.deb
dpkg-deb --build --root-owner-group "$PKGROOT" "$WORK/package.deb"
cp "$WORK/package.deb" "$DEB"
echo "$DEB"
