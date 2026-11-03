#!/bin/bash
# Build the printer tools (up3dtranscode, up3dload, up3dstatus, ...) for 64-bit Windows.
#
# Needs:
#   llvm-mingw  https://github.com/mstorsjo/llvm-mingw   (any recent release)
#   libusb 1.0.27, static MinGW build (include/libusb-1.0/libusb.h and lib/libusb-1.0.a)
#
#   MINGW_BIN=/path/to/llvm-mingw/bin LIBUSB=/path/to/libusb ./build/build-tools.sh
#
# Output: out/bin/*.exe
set -e
HERE=$(cd "$(dirname "$0")/.." && pwd)
SRC=$HERE/up3d
OUT=${OUT:-$HERE/out/bin}
: "${MINGW_BIN:?set MINGW_BIN to the llvm-mingw bin folder}"
: "${LIBUSB:?set LIBUSB to the libusb folder (with include/ and lib/)}"
export PATH=$MINGW_BIN:$PATH
CC=x86_64-w64-mingw32-gcc
mkdir -p "$OUT"
INC="-I $SRC/UP3DCOMMON -I $LIBUSB/include"
USB=$LIBUSB/lib/libusb-1.0.a
COMMON="$SRC/UP3DCOMMON/up3dcomm.c $SRC/UP3DCOMMON/up3d.c $SRC/UP3DCOMMON/up3ddata.c $SRC/UP3DCOMMON/up3dconf.c"
TRL="$SRC/UP3DTRANSCODE/hoststepper.c $SRC/UP3DTRANSCODE/hostplanner.c $SRC/UP3DTRANSCODE/umcwriter.c"

for t in upload:up3dload upinfo:up3dinfo upstatus:up3dstatus upcenter:up3dcenter upsnap:up3dsnap; do
  $CC -Os -std=gnu99 -static $INC -o "$OUT/${t##*:}.exe" $COMMON "$SRC/UP3DTOOLS/${t%%:*}.c" "$USB" -w
done
$CC -Os -std=gnu99 -static $INC -I "$SRC/UP3DTRANSCODE" -o "$OUT/up3dctl.exe" $COMMON $TRL "$SRC/UP3DTOOLS/upctl.c" "$USB" -lm -w
$CC -O2 -std=gnu99 -static -I "$SRC/UP3DCOMMON" -o "$OUT/up3dtranscode.exe" \
  "$SRC/UP3DCOMMON/up3dconf.c" "$SRC/UP3DCOMMON/up3ddata.c" $TRL "$SRC/UP3DTRANSCODE/gcodeparser.c" "$SRC/UP3DTRANSCODE/up3dtranscode.c" -lm -w
x86_64-w64-mingw32-strip "$OUT"/*.exe
ls -la "$OUT"
