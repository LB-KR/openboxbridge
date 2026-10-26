//////////////////////////////////////////////////////////////
// up3dsnap: READ-ONLY snapshot of the printer's settings memory.
//
//   up3dsnap <output.txt>
//
// Uses only read commands: 0x76 (read one parameter, 0x00-0xFF)
// and 0x52 (read the printer information block). Nothing is written.
// Two snapshots taken before/after changing something in UP Studio
// show where that value lives.
// Added for UP Box+ USB Print (GPL v2, like the rest of UP3D).
//////////////////////////////////////////////////////////////

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdbool.h>
#include <stdint.h>
#include <time.h>

#include "up3d.h"

static void hexdump(FILE *f, const char *title, const uint8_t *p, size_t n)
{
  fprintf(f, "[%s] %u bytes\n", title, (unsigned)n);
  for( size_t i = 0; i < n; i += 16 )
  {
    fprintf(f, "  %04X ", (unsigned)i);
    for( size_t j = i; j < i + 16 && j < n; j++ ) fprintf(f, " %02X", p[j]);
    fprintf(f, "\n");
  }
}

int main(int argc, char *argv[])
{
  if( argc != 2 ) { printf("Usage: up3dsnap output.txt\n"); return 2; }
  FILE *f = fopen(argv[1], "w");
  if( !f ) { printf("ERROR: cannot write %s\n", argv[1]); return 3; }

  if( !UP3D_Open() ) { fclose(f); return 4; }
  if( !UP3D_IsPrinterResponsive() ) { printf("ERROR: UP printer is not responding\n"); UP3D_Close(); fclose(f); return 5; }

  time_t now = time(NULL);
  fprintf(f, "UP3D read-only snapshot  %s", ctime(&now));
  fprintf(f, "[parameters] id = unsigned (hex) signed float\n");
  for( int p = 0; p < 256; p++ )
  {
    uint32_t v = UP3D_GetParameter((uint8_t)p);
    float fl; memcpy(&fl, &v, 4);
    fprintf(f, "P%02X = %u (0x%08X) %d %g\n", p, v, v, (int32_t)v, fl);
  }

  TT_tagPrinterInfoHeader pihdr; TT_tagPrinterInfoName piname; TT_tagPrinterInfoData pidata; TT_tagPrinterInfoSet pisets[8];
  memset(pisets, 0, sizeof(pisets));
  if( UP3D_GetPrinterInfo( &pihdr, &piname, &pidata, pisets ) )
  {
    hexdump(f, "info header", (uint8_t*)&pihdr, sizeof(pihdr));
    hexdump(f, "info name", (uint8_t*)&piname, sizeof(piname));
    hexdump(f, "info data", (uint8_t*)&pidata, sizeof(pidata));
    for( uint32_t s = 0; s < pidata.u32_NumSets && s < 8; s++ )
    {
      char t[32]; snprintf(t, sizeof(t), "info set %u", s);
      hexdump(f, t, (uint8_t*)&pisets[s], sizeof(pisets[s]));
    }
  }
  else fprintf(f, "[info] could not read\n");

  UP3D_Close();
  fclose(f);
  printf("Snapshot saved: %s\n", argv[1]);
  return 0;
}
