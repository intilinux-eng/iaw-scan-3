#!/bin/bash
# Builds the IAW Scan 3 Coupé Debian package for the Arduino UNO Q (arm64 Debian).
#
#   IES_3.Coupe/packaging/build-deb.sh <version> [runtime]
#
# <version>  e.g. 0.1.0-experimental (becomes 0.1.0~experimental, so pre-releases sort before 0.1.0)
# [runtime]  .NET runtime identifier, default linux-arm64
#
# Needs the .NET 10 SDK and dpkg-deb (any Debian/Ubuntu, WSL included). Output: packaging/out/.
# With PUBLISH_DIR set, packages that existing `dotnet publish` output instead of publishing (e.g.
# published on Windows, packaged in WSL).
set -euo pipefail

VERSION=${1:?usage: build-deb.sh <version> [runtime]}
RID=${2:-linux-arm64}
case $RID in
    linux-arm64) ARCH=arm64 ;;
    linux-x64) ARCH=amd64 ;;
    *) echo "unsupported runtime: $RID" >&2; exit 1 ;;
esac

HERE=$(cd "$(dirname "$0")" && pwd)
PROJECT=$HERE/../IES_3.Coupe.csproj
DEB_VERSION=${VERSION/-/\~}
PACKAGE=iaw-scan3-coupe

# Staged on a Linux filesystem: from a Windows checkout under WSL (/mnt/c) every file would show
# up as mode 777, which dpkg-deb refuses.
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
PKGROOT=$WORK/pkgroot
mkdir -p "$PKGROOT/DEBIAN" "$PKGROOT/opt/$PACKAGE"

PUBLISH_DIR=${PUBLISH_DIR:-}
if [ -z "$PUBLISH_DIR" ]; then
    # Self-contained: the board needs no .NET installation. Single file, with the runtime's own
    # native code linked in, so nothing gets extracted to disk at start-up.
    PUBLISH_DIR=$WORK/publish
    dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
        -p:Version="$VERSION" -p:PublishSingleFile=true -p:DebugType=None \
        -o "$PUBLISH_DIR"
fi

cp -r "$PUBLISH_DIR/." "$PKGROOT/opt/$PACKAGE/"
chmod -R u=rwX,go=rX "$PKGROOT/opt/$PACKAGE"
find "$PKGROOT/opt/$PACKAGE" -type f -exec chmod 644 {} +
chmod 755 "$PKGROOT/opt/$PACKAGE/IES_3.Coupe"

install -D -m 644 "$HERE/files/iaw-scan3-coupe.service" "$PKGROOT/usr/lib/systemd/system/$PACKAGE.service"
install -D -m 644 "$HERE/files/iaw-scan3-coupe.default" "$PKGROOT/etc/default/$PACKAGE"
install -D -m 644 "$HERE/files/60-iaw-scan3-coupe.rules" "$PKGROOT/usr/lib/udev/rules.d/60-$PACKAGE.rules"
install -D -m 755 "$HERE/files/iaw-coupe-setup" "$PKGROOT/usr/sbin/iaw-coupe-setup"
install -D -m 644 "$HERE/../../license.txt" "$PKGROOT/usr/share/doc/$PACKAGE/copyright"

cat >"$PKGROOT/DEBIAN/control" <<EOF
Package: $PACKAGE
Version: $DEB_VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libicu76 | libicu74 | libicu72
Recommends: network-manager, dnsmasq-base, qrencode
Maintainer: IAW Scan 3 project <https://github.com/intilinux-eng/iaw-scan-3>
Homepage: https://github.com/intilinux-eng/iaw-scan-3
Description: IAW Scan 3 Coupé (EXPERIMENTAL) - Fiat Coupé diagnostics and data logger
 Diagnostics for the Fiat Coupé's Weber-Marelli IAW-04K.P8 engine ECU and
 FIAT CODE immobiliser over a USB K-line cable, used from a phone through
 the device's own Wi-Fi. Logs every power-on (ECU parameters, fault codes,
 events) for later analysis. Built for the Arduino UNO Q powered from the
 car's ignition. Experimental: not yet verified on real hardware.
EOF

echo "/etc/default/$PACKAGE" >"$PKGROOT/DEBIAN/conffiles"

cat >"$PKGROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    if ! getent passwd iawscan >/dev/null; then
        useradd --system --user-group --home-dir /var/lib/iaw-scan3-coupe --no-create-home \
            --shell /usr/sbin/nologin --comment "IAW Scan 3 Coupé" iawscan
    fi
    udevadm control --reload-rules >/dev/null 2>&1 || true
    udevadm trigger --subsystem-match=usb-serial --action=add >/dev/null 2>&1 || true
    systemctl daemon-reload >/dev/null 2>&1 || true
    systemctl enable iaw-scan3-coupe.service >/dev/null 2>&1 || true
    systemctl restart iaw-scan3-coupe.service || true
fi
EOF

cat >"$PKGROOT/DEBIAN/prerm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ]; then
    systemctl disable --now iaw-scan3-coupe.service >/dev/null 2>&1 || true
fi
EOF

# The logbook in /var/lib/iaw-scan3-coupe is kept even on purge: it is the user's data.
cat >"$PKGROOT/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    systemctl daemon-reload >/dev/null 2>&1 || true
    udevadm control --reload-rules >/dev/null 2>&1 || true
fi
EOF
chmod 755 "$PKGROOT/DEBIAN/postinst" "$PKGROOT/DEBIAN/prerm" "$PKGROOT/DEBIAN/postrm"

mkdir -p "$HERE/out"
DEB=$HERE/out/${PACKAGE}_${DEB_VERSION}_${ARCH}.deb
dpkg-deb --build --root-owner-group "$PKGROOT" "$WORK/package.deb"
cp "$WORK/package.deb" "$DEB"
echo "$DEB"
