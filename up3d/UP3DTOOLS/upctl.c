//////////////////////////////////////////////////////////////
// up3dctl: printer controls for UP Box+ USB Print
//
//   up3dctl box home
//   up3dctl box jog <dx> <dy> <dz>          slicer-frame mm; +dz moves the bed down
//   up3dctl box extrude <temp> <mm> [mm/min]   heat, wait until hot, push filament
//   up3dctl box withdraw <temp> <mm>          heat, wait until hot, pull filament, heater off
//   add --dry=<file> to only write the program (testing)
//
// Programs are built with the converter's own writer (same moves/heating as a
// print) and sent to the printer: small ones straight into the program buffer,
// larger ones through the SD card like a print.
// Added for UP Box+ USB Print (GPL v2, like the rest of UP3D).
//////////////////////////////////////////////////////////////

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdbool.h>
#include <stdint.h>
#include <inttypes.h>
#include <unistd.h>

#include "up3d.h"
#include "up3dconf.h"
#include "umcwriter.h"

static void usage(void)
{
  printf("Usage: up3dctl box home | jog dx dy dz | extrude temp mm [mm/min] | withdraw temp mm  [--dry=file]\n");
  exit(2);
}

static bool send_direct(UP3D_BLK *blks, long n)
{
  if( !UP3D_ClearProgramBuf() ) return false;
  for( long i = 0; i < n; i += 64 )
  {
    long c = (n - i) > 64 ? 64 : (n - i);
    if( !UP3D_WriteBlocks( blks + i, (uint8_t)c ) ) return false;
  }
  return UP3D_StartResumeProgram();
}

static bool send_sd(UP3D_BLK *blks, long n)
{
  UP3D_SetParameter(PARA_RED_BLUE_BLINK,10);
  UP3D_SetParameter(PARA_PRINT_STATUS,0x13); //write to SD
  if( !UP3D_ClearProgramBuf() ) return false;
  uint8_t program_id = 5;
  if( !UP3D_UseSDProgramBuf( program_id, true ) ) return false;
  for( long i = 0; i < n; i += 72 )
  {
    long c = (n - i) > 72 ? 72 : (n - i);
    if( !UP3D_WriteBlocks( blks + i, (uint8_t)c ) ) return false;
  }
  for( int w = 0; w < 100 && UP3D_GetParameter(0xC6) != (uint32_t)n; w++ )
    usleep(50*1000);
  if( UP3D_GetParameter(0xC6) != (uint32_t)n ) return false;
  UP3D_SetParameter(PARA_RED_BLUE_BLINK,0);
  UP3D_SetParameter(PARA_PRINT_STATUS,0x1);
  if( !UP3D_ClearProgramBuf() ) return false;
  if( !UP3D_UseSDProgramBuf( program_id, false ) ) return false;
  return UP3D_StartResumeProgram();
}

int main(int argc, char *argv[])
{
  if( argc < 3 ) usage();
  memcpy( &settings, &settings_box, sizeof(settings) );

  const char *dry = NULL;
  for( int i = 1; i < argc; i++ )
    if( !strncmp(argv[i], "--dry=", 6) ) { dry = argv[i] + 6; argc = i; break; }

  char path[1024];
  if( dry ) snprintf(path, sizeof(path), "%s", dry);
  else
  {
    const char *tmp = getenv("TEMP"); if( !tmp ) tmp = getenv("TMPDIR"); if( !tmp ) tmp = ".";
    snprintf(path, sizeof(path), "%s/up3dctl.umc", tmp);
  }
  if( !umcwriter_open_raw( path, 'b' ) ) { printf("ERROR: cannot write %s\n", path); return 3; }

  const char *cmd = argv[2];
  UP3D_BLK blk, b2[2];
  UP3D_PROG_BLK_Power(&blk, true); umcwriter_write_raw(&blk, 1);

  if( !strcmp(cmd, "home") )
  {
    umcwriter_home(0); umcwriter_home(1); umcwriter_home(2);
    printf("Homing all axes\n");
  }
  else if( !strcmp(cmd, "jog") && argc >= 6 )
  {
    double dx = atof(argv[3]), dy = atof(argv[4]), dz = atof(argv[5]);
    if( dx != 0 || dy != 0 )
      umcwriter_planner_add( dx, dy, 0, 3000 );          /* 50 mm/s, with the print planner's acceleration */
    if( dz != 0 )
    {
      /* relative move (positive speeds = relative, as in the printer's own routines) */
      UP3D_PROG_BLK_MoveF( b2, 1000, 0, 1000, 0, 8, dz, 1000, 0 );
      umcwriter_write_raw( b2, 2 );
    }
    printf("Jog X%+.2f Y%+.2f Z%+.2f\n", dx, dy, dz);
  }
  else if( !strcmp(cmd, "extrude") && argc >= 5 )
  {
    double t = atof(argv[3]), mm = atof(argv[4]), f = (argc >= 6) ? atof(argv[5]) : 120;
    if( t < 170 || t > 300 ) { printf("ERROR: temperature %.0f outside 170-300\n", t); return 4; }
    if( mm <= 0 || mm > 200 ) { printf("ERROR: length %.0f outside 1-200 mm\n", mm); return 4; }
    if( f < 30 || f > 600 ) f = 120;
    umcwriter_set_extruder_temp( t, true );              /* heats, then waits for the printer's "hot enough" flag */
    umcwriter_beep(300);
    umcwriter_planner_add( 0, 0, mm, f );
    umcwriter_planner_sync();
    umcwriter_beep(300);
    printf("Extrude %.0f mm at %.0f C (%.0f mm/min); heater stays on\n", mm, t, f);
  }
  else if( !strcmp(cmd, "withdraw") && argc >= 5 )
  {
    double t = atof(argv[3]), mm = atof(argv[4]);
    if( t < 170 || t > 300 ) { printf("ERROR: temperature %.0f outside 170-300\n", t); return 4; }
    if( mm <= 0 || mm > 300 ) { printf("ERROR: length %.0f outside 1-300 mm\n", mm); return 4; }
    umcwriter_set_extruder_temp( t, true );
    umcwriter_beep(300);
    double slow = mm < 10 ? mm : 10;
    umcwriter_planner_add( 0, 0, -slow, 180 );           /* ease the tip out of the melt zone */
    if( mm > slow ) umcwriter_planner_add( 0, 0, -mm, 900 );
    umcwriter_planner_sync();
    umcwriter_set_extruder_temp( 0, false );            /* heater off */
    umcwriter_beep(300);
    printf("Withdraw %.0f mm at %.0f C; heater off afterwards\n", mm, t);
  }
  else
  {
    usage();
  }
  umcwriter_close_raw();

  FILE *f = fopen(path, "rb");
  if( !f ) { printf("ERROR: cannot read %s\n", path); return 3; }
  fseek(f, 0, SEEK_END); long len = ftell(f); fseek(f, 0, SEEK_SET);
  long n = len / (long)sizeof(UP3D_BLK);
  UP3D_BLK *blks = (UP3D_BLK*)malloc(len > 0 ? len : 1);
  if( fread(blks, sizeof(UP3D_BLK), n, f) != (size_t)n ) { printf("ERROR: read\n"); return 3; }
  fclose(f);
  printf("Program: %ld blocks\n", n);
  if( dry ) return 0;

  if( !UP3D_Open() ) return 5;
  if( !UP3D_IsPrinterResponsive() ) { printf("ERROR: UP printer is not responding\n"); UP3D_Close(); return 6; }
  bool ok = (n <= 60) ? send_direct(blks, n) : send_sd(blks, n);
  UP3D_Close();
  free(blks);
  if( !ok ) { printf("ERROR: could not send the command to the printer\n"); return 7; }
  printf("Sent\n");
  return 0;
}
