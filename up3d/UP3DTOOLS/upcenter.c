//////////////////////////////////////////////////////////////
// up3dcenter: find where the bed centre is, in the coordinates
// the transcoder uses (G-code X/Y relative to the home position).
//
// 1. initialise/home the printer (ROM program 0)
// 2. run ROM program 3 ("move to centre, 10mm over bed")
// 3. read the axis positions and convert them to G-code X/Y
//
// Output (last line, machine readable):
//   CENTER X=<mm> Y=<mm> ZPOS=<mm>
//
// Added for UP Box+ USB Print (GPL v2, like the rest of UP3D).
//////////////////////////////////////////////////////////////

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdbool.h>
#include <stdint.h>
#include <inttypes.h>
#include <unistd.h>
#include <math.h>

#include "up3d.h"
#include "up3dconf.h"

#define MSTATE_RUNNING 2
#define MSTATE_IDLE    3

#define MAXS 4000
static int    ns = 0;
static double sx[MAXS], sy[MAXS], sz[MAXS];
static double g_steps[3];

static void sample(void)
{
  if( ns >= MAXS ) return;
  sx[ns] = (double)UP3D_GetAxisPosition(1) / g_steps[0];
  sy[ns] = (double)UP3D_GetAxisPosition(2) / g_steps[1];
  sz[ns] = (double)UP3D_GetAxisPosition(3) / g_steps[2];
  ns++;
}

static bool run_rom_and_wait(uint8_t prog, int timeout_s, bool record)
{
  if( !UP3D_ClearProgramBuf() || !UP3D_InsertRomProgram(prog) || !UP3D_StartResumeProgram() )
  {
    printf("ERROR: could not start printer routine %u\n", prog);
    return false;
  }
  bool seen_running = false;
  int per_s = record ? 10 : 4;
  for( int i = 0; i < timeout_s * per_s; i++ )
  {
    usleep( (1000/per_s) * 1000 );
    if( record ) sample();
    int32_t m = UP3D_GetMachineState();
    if( m == MSTATE_RUNNING ) seen_running = true;
    if( m == MSTATE_IDLE && (seen_running || i > 2*per_s) ) return true;
    if( m == 0 ) { printf("ERROR: printer reports a system error\n"); return false; }
  }
  printf("ERROR: printer routine %u did not finish within %d s\n", prog, timeout_s);
  return false;
}

/* Move the nozzle to slicer X/Y (same coordinate mapping as the converter),
   homing first the same way a converted print does. Bed stays at Z home (low). */
static int do_goto(double gx, double gy, bool home)
{
  UP3D_BLK blk, b2[2];
  if( !UP3D_ClearProgramBuf() ) { printf("ERROR: printer busy\n"); return 4; }
  UP3D_PROG_BLK_Power(&blk,true); UP3D_WriteBlock(&blk);
  if( home )
  {
    UP3D_PROG_BLK_Home(&blk, settings.y_axes, settings.y_dir, settings.y_hofs_hi, settings.y_hspeed_hi); UP3D_WriteBlock(&blk);
    UP3D_PROG_BLK_Home(&blk, settings.y_axes, settings.y_dir, settings.y_hofs_lo, settings.y_hspeed_lo); UP3D_WriteBlock(&blk);
    UP3D_PROG_BLK_Home(&blk, settings.x_axes, settings.x_dir, settings.x_hofs_hi, settings.x_hspeed_hi); UP3D_WriteBlock(&blk);
    UP3D_PROG_BLK_Home(&blk, settings.x_axes, settings.x_dir, settings.x_hofs_lo, settings.x_hspeed_lo); UP3D_WriteBlock(&blk);
    UP3D_PROG_BLK_Home(&blk, UP3DAXIS_Z, settings.z_dir, settings.z_hofs_hi, settings.z_hspeed_hi); UP3D_WriteBlock(&blk);
    UP3D_PROG_BLK_Home(&blk, UP3DAXIS_Z, settings.z_dir, settings.z_hofs_lo, settings.z_hspeed_lo); UP3D_WriteBlock(&blk);
  }
  double topos[2], feed = 3000.0; /* mm/min */
  topos[settings.x_axes] = gx * settings.x_dir;
  topos[settings.y_axes] = gy * settings.y_dir;
  UP3D_PROG_BLK_MoveF( b2, -feed, topos[0], -feed, topos[1], -600.0, 0, 0, 0 );
  UP3D_WriteBlocks( b2, 2 );
  UP3D_PROG_BLK_Stop(&blk); UP3D_WriteBlock(&blk);
  if( !UP3D_StartResumeProgram() ) { printf("ERROR: could not start the move\n"); return 5; }

  printf(home ? "Homing, then moving...\n" : "Moving...\n"); fflush(stdout);
  bool seen = false;
  for( int i = 0; i < 120*4; i++ )
  {
    usleep(250*1000);
    int32_t m = UP3D_GetMachineState();
    if( m == MSTATE_RUNNING ) seen = true;
    if( m == MSTATE_IDLE && (seen || i > 8) ) break;
    if( m == 0 ) { printf("ERROR: printer reports a system error\n"); return 6; }
  }
  double a1 = (double)UP3D_GetAxisPosition(1) / g_steps[0];
  double a2 = (double)UP3D_GetAxisPosition(2) / g_steps[1];
  printf("AT X=%.2f Y=%.2f (motors %.2f %.2f)\n", gx, gy, a1, a2);
  return 0;
}

int main(int argc, char *argv[])
{
  char mtype = (argc > 1) ? argv[1][0] : 'b';
  switch( mtype )
  {
    case 'm': memcpy(&settings,&settings_mini,sizeof(settings)); break;
    case 'c': case 'p': memcpy(&settings,&settings_classic_plus,sizeof(settings)); break;
    case 'C': memcpy(&settings,&settings_cetus,sizeof(settings)); break;
    default:  memcpy(&settings,&settings_box,sizeof(settings)); break;
  }

  if( !UP3D_Open() )
    return 2;

  if( !UP3D_IsPrinterResponsive() )
  {
    printf("ERROR: UP printer is not responding\n");
    UP3D_Close();
    return 3;
  }

  TT_tagPrinterInfoHeader pihdr;
  TT_tagPrinterInfoName   piname;
  TT_tagPrinterInfoData   pidata;
  TT_tagPrinterInfoSet    pisets[8];
  /* axis positions are read in motor steps: use the printer's own steps/mm
     (the converter's 3rd value is the extruder, not Z; Z moves are sent in mm) */
  double *steps = g_steps;
  steps[0] = settings.steps_per_mm[0]; steps[1] = settings.steps_per_mm[1]; steps[2] = 854.0;
  if( UP3D_GetPrinterInfo( &pihdr, &piname, &pidata, pisets ) )
  {
    printf("Printer: %s\n", piname.printer_name);
    printf("Printer steps/mm: X %.1f  Y %.1f  Z %.1f   (converter uses %.1f / %.1f for X / Y)\n",
           pidata.f_steps_mm_x, pidata.f_steps_mm_y, pidata.f_steps_mm_z,
           settings.steps_per_mm[0], settings.steps_per_mm[1]);
    if( pidata.f_steps_mm_x > 1 ) steps[0] = pidata.f_steps_mm_x;
    if( pidata.f_steps_mm_y > 1 ) steps[1] = pidata.f_steps_mm_y;
    if( pidata.f_steps_mm_z > 1 ) steps[2] = pidata.f_steps_mm_z;
  }

  if( argc >= 5 && !strcmp(argv[2], "goto") )
  {
    double gx = atof(argv[3]), gy = atof(argv[4]);
    bool home = !(argc >= 6 && !strcmp(argv[5], "nohome"));
    int r = do_goto( gx, gy, home );
    UP3D_Close();
    return r;
  }

  /* probe: run the printer's own initialise routine; on the Box+ it ends
     with the head over the bed centre. Report that as a starting guess. */
  printf("Homing (printer's own start-up routine)...\n"); fflush(stdout);
  if( !run_rom_and_wait(0, 180, false) ) { UP3D_Close(); return 4; }
  {
    double a[3];
    for( int k = 0; k < 3; k++ ) a[k] = (double)UP3D_GetAxisPosition(k+1) / g_steps[k];
    double gx = a[settings.x_axes] / settings.x_dir, gy = a[settings.y_axes] / settings.y_dir;
    printf("Start-up routine ended at motors %.2f %.2f %.2f\n", a[0], a[1], a[2]);
    printf("GUESS X=%.2f Y=%.2f\n", gx, gy);
    UP3D_Close();
    return 0;
  }

  printf("Moving to bed centre...\n"); fflush(stdout);
  ns = 0; sample();                       /* position before the move (home) */
  if( !run_rom_and_wait(3, 90, true) ) { UP3D_Close(); return 5; }
  sample();

  /* The Box+ goes back home after visiting the centre, so look through the
     recorded path: the centre is where the bed was raised highest (Z furthest
     from home) and the head furthest from its home X/Y. */
  double hx = sx[0], hy = sy[0], hz = sz[0];
  double zmax = 0;
  for( int i = 0; i < ns; i++ ) if( fabs(sz[i]-hz) > zmax ) zmax = fabs(sz[i]-hz);
  int best = -1; double bestd = -1;
  for( int i = 0; i < ns; i++ )
  {
    if( fabs(sz[i]-hz) < zmax - 0.5 ) continue;
    double d = (sx[i]-hx)*(sx[i]-hx) + (sy[i]-hy)*(sy[i]-hy);
    if( d > bestd ) { bestd = d; best = i; }
  }

  /* compact path for the log: points where the head paused */
  printf("Path (A1 A2 A3, mm), %d samples:\n", ns);
  for( int i = 0; i < ns; i++ )
  {
    bool still = (i>0) && fabs(sx[i]-sx[i-1])<0.01 && fabs(sy[i]-sy[i-1])<0.01 && fabs(sz[i]-sz[i-1])<0.01;
    bool next_moves = (i+1<ns) && !(fabs(sx[i+1]-sx[i])<0.01 && fabs(sy[i+1]-sy[i])<0.01 && fabs(sz[i+1]-sz[i])<0.01);
    if( i==0 || i==ns-1 || (still && next_moves) )
      printf("  #%d  %.2f  %.2f  %.2f\n", i, sx[i], sy[i], sz[i]);
  }

  if( best < 0 || zmax < 1.0 )
  {
    printf("ERROR: the head never moved away from home - could not find the centre\n");
    UP3D_Close();
    return 6;
  }

  double mm[3] = { sx[best], sy[best], sz[best] };
  // transcoder: motor[x_axes] = X * x_dir, motor[y_axes] = Y * y_dir
  double gx = mm[settings.x_axes] / settings.x_dir;
  double gy = mm[settings.y_axes] / settings.y_dir;

  printf("Centre sample #%d: A1 %.2f  A2 %.2f  A3 %.2f (bed raised %.2f mm from home)\n",
         best, mm[0], mm[1], mm[2], zmax);
  printf("If the nozzle was 10 mm above the bed there, nozzle height is about %.2f mm\n", zmax + 10.0);
  printf("CENTER X=%.2f Y=%.2f ZPOS=%.2f ZRAISE=%.2f\n", gx, gy, mm[2], zmax);

  UP3D_Close();
  return 0;
}
