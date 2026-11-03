#!/bin/bash
# Native build of the converter for testing on Linux/macOS:
#   ./build/build-transcode-linux.sh && ./out/up3dtranscode box in.gcode out.umc 232.1 20 100 0.05 125 75 rot180
set -e
HERE=$(cd "$(dirname "$0")/.." && pwd); SRC=$HERE/up3d; mkdir -p "$HERE/out"
cc -O2 -std=gnu99 -I "$SRC/UP3DCOMMON" -o "$HERE/out/up3dtranscode" \
  "$SRC/UP3DCOMMON/up3dconf.c" "$SRC/UP3DCOMMON/up3ddata.c" "$SRC/UP3DTRANSCODE/hoststepper.c" "$SRC/UP3DTRANSCODE/hostplanner.c" \
  "$SRC/UP3DTRANSCODE/umcwriter.c" "$SRC/UP3DTRANSCODE/gcodeparser.c" "$SRC/UP3DTRANSCODE/up3dtranscode.c" -lm -w
echo "built out/up3dtranscode"
