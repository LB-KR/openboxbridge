/*
  umcwriter.cpp for UP3DTranscoder
  M. Stohn 2016

  This is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This software is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  If not, see <http://www.gnu.org/licenses/>.
*/

#include "umcwriter.h"
#include "up3ddata.h"
#include "up3dconf.h"
#include "hostplanner.h"
#include "hoststepper.h"

#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>
#include <math.h>
#include <stdlib.h>

static FILE*   umcwriter_file;
static double  umcwriter_Z;
static double  umcwriter_Z_height;
static double  umcwriter_print_time;
static char    umcwriter_machine_type;
static int32_t umcwriter_bed_temp;

static int _umcwriter_write_file(UP3D_BLK* pblks, uint32_t blks )
{
  if( umcwriter_file )
    return (int)fwrite( pblks, blks, sizeof(UP3D_BLK), umcwriter_file );
  else
    return 0;
}

#define UMC_PRINT_MAX 25.0   /* mm/s: UP Studio never drives the head faster while printing */
static bool umc_classic = false;   /* UP3D_CLASSIC_SEGMENTS=1 in the environment: old behaviour */

bool umcwriter_init(const char* filename, const double heightZ, const char machine_type)
{
  { const char *e = getenv("UP3D_CLASSIC_SEGMENTS"); umc_classic = (e && *e == '1'); }
  umcwriter_Z = 0;
  umcwriter_Z_height = heightZ;
  umcwriter_print_time = 0;
  umcwriter_machine_type = machine_type;
  umcwriter_bed_temp = 0;

  st_reset();
  plan_reset();

  umcwriter_file = fopen(filename,"wb+");
  if( !umcwriter_file )
    return false;

  umcwriter_set_report_data( 0, 0 );

  UP3D_BLK blk;
  UP3D_PROG_BLK_Power(&blk,true);
  _umcwriter_write_file(&blk, 1);

  UP3D_PROG_BLK_Beeper(&blk,false);
  _umcwriter_write_file(&blk, 1);
  
  umcwriter_pause(4000); //wait for power on complete and temperature measurement to stabilize

  UP3D_PROG_BLK_SetParameter(&blk,0x41,0);              //TEMP FOR NOZZLE1 TEMP REACHED
  _umcwriter_write_file(&blk, 1);
  UP3D_PROG_BLK_SetParameter(&blk,0x42,0);              //TEMP FOR NOZZLE2 TEMP REACHED
  _umcwriter_write_file(&blk, 1);
  UP3D_PROG_BLK_SetParameter(&blk,0x43,0);              //TEMP FOR BED TEMP REACHED
  _umcwriter_write_file(&blk, 1);

// NEEDED?
  UP3D_PROG_BLK_SetParameter(&blk,0x46,13);               //?
  _umcwriter_write_file(&blk, 1);

  if( 'm' == umcwriter_machine_type )
  {
    UP3D_PROG_BLK_SetParameter(&blk,0x49,80);           //? for up mini only!
    _umcwriter_write_file(&blk, 1);
  }

  //home all axis (needed for correct print status
  umcwriter_home(0);
  umcwriter_home(1);
  umcwriter_home(2);

  UP3D_PROG_BLK_SetParameter(&blk,PARA_PRINT_STATUS,1); //initialized
  _umcwriter_write_file(&blk, 1);
  UP3D_PROG_BLK_SetParameter(&blk,0x11,0);              //no support
  _umcwriter_write_file(&blk, 1);

// NEEDED ?
  UP3D_PROG_BLK_SetParameter(&blk,0x35,0);              //needed for heat bed!
  _umcwriter_write_file(&blk, 1);
//  UP3D_PROG_BLK_SetParameter(&blk,0x36,103);            //feedback length ?
//  _umcwriter_write_file(&blk, 1);

//  UP3D_PROG_BLK_SetParameter(&blk,0x2A,5000);           //change nozzle time
//  _umcwriter_write_file(&blk, 1);
//  UP3D_PROG_BLK_SetParameter(&blk,0x2B,0);              //jump time
//  _umcwriter_write_file(&blk, 1);

  UP3D_PROG_BLK_SetParameter(&blk,0x17,0);              //nozzle #1 not open
  _umcwriter_write_file(&blk, 1);

// PROBLEM...
//  UP3D_PROG_BLK_SetParameter(&blk,PARA_PRINT_STATUS,2); //running ==> PAUSED ???
//  _umcwriter_write_file(&blk, 1);
//

// NEEDED?
  UP3D_PROG_BLK_SetParameter(&blk,0x82,255);            //?
  _umcwriter_write_file(&blk, 1);
  UP3D_PROG_BLK_SetParameter(&blk,0x83,255);            //?
  _umcwriter_write_file(&blk, 1);
  //DEL UP3D_PROG_BLK_SetParameter(&blk,0x1D,100000);         //feed error length
  //DEL _umcwriter_write_file(&blk, 1);


/* PROBLEM...
  UP3D_PROG_BLK_SetParameter(&blk,0x1C,1);              //printing...
  _umcwriter_write_file(&blk, 1);
*/
  return true;
}

/* --- raw program writing for printer controls (no homing, no print setup) --- */
bool umcwriter_open_raw(const char* filename, const char machine_type)
{
  umcwriter_Z = 0;
  umcwriter_Z_height = 0;
  umcwriter_print_time = 0;
  umcwriter_machine_type = machine_type;
  umcwriter_bed_temp = 0;
  st_reset();
  plan_reset();
  umcwriter_file = fopen(filename,"wb+");
  return umcwriter_file != NULL;
}

void umcwriter_write_raw(UP3D_BLK* pblks, uint32_t blks)
{
  umcwriter_planner_sync();
  _umcwriter_write_file(pblks, blks);
}

void umcwriter_close_raw()
{
  umcwriter_planner_sync();
  UP3D_BLK blk;
  UP3D_PROG_BLK_Stop(&blk);
  _umcwriter_write_file(&blk, 1);
  fclose( umcwriter_file );
  umcwriter_file = NULL;
}

void umcwriter_finish(bool power_off)
{
  umcwriter_planner_sync();

  umcwriter_print_time += 1.5; //1.5 seconds for end of job

  umcwriter_beep(200);umcwriter_pause(200);
  umcwriter_beep(200);umcwriter_pause(200);
  umcwriter_beep(200);umcwriter_pause(500);

  UP3D_BLK blk;

  //post process time and perecent values
  rewind( umcwriter_file );
  double time = 0;
  for(;;)
  {
    if( fread( &blk, sizeof(UP3D_BLK), 1, umcwriter_file ) <= 0 )
      break;

    if( UP3DPCMD_SetParameter == blk.pcmd )
    {
      bool change = false;
      if( PARA_REPORT_TIME_REMAIN == blk.pdat1.l )
      {
        time = blk.pdat2.l;
        blk.pdat2.l = (int32_t)(umcwriter_print_time - time);
        change=true;
      }
      if( PARA_REPORT_PERCENT == blk.pdat1.l )
      {
        blk.pdat2.l = (time*100)/umcwriter_print_time;
        change=true;
      }

      if( change )
      {
        fseek( umcwriter_file, -sizeof(UP3D_BLK), SEEK_CUR );
        fwrite( &blk, sizeof(UP3D_BLK), 1, umcwriter_file );
        fseek( umcwriter_file, 0, SEEK_CUR ); //workaround for windows bug
      }
    }
  }

  UP3D_PROG_BLK_SetParameter(&blk,PARA_REPORT_PERCENT,100);
  _umcwriter_write_file(&blk, 1);
  UP3D_PROG_BLK_SetParameter(&blk,PARA_REPORT_TIME_REMAIN,0);
  _umcwriter_write_file(&blk, 1);

  if ( power_off )
  {
    UP3D_PROG_BLK_Power(&blk,false);
    _umcwriter_write_file(&blk, 1);
  }

  UP3D_PROG_BLK_SetParameter(&blk,0x1C,0); //not printing...
  _umcwriter_write_file(&blk, 1);

  UP3D_PROG_BLK_SetParameter(&blk,PARA_PRINT_STATUS,0); //not ready
  _umcwriter_write_file(&blk, 1);

  UP3D_PROG_BLK_Stop(&blk);
  _umcwriter_write_file(&blk, 1);

  fclose( umcwriter_file );
  umcwriter_file = NULL;
}

int32_t umcwriter_get_print_time()
{
  return (int32_t)umcwriter_print_time;
}

void umcwriter_home(int32_t axes)
{
  umcwriter_planner_sync();

  double pos[3];
  plan_get_position(pos);

  umcwriter_print_time += 3; //apx. 3 seconds for homing of one axis
  
  UP3D_BLK blk;
  
  switch( axes )
  {
    case 0:
      UP3D_PROG_BLK_Home( &blk, settings.y_axes, settings.y_dir, settings.y_hofs_hi, settings.y_hspeed_hi );
      _umcwriter_write_file(&blk, 1);
      UP3D_PROG_BLK_Home( &blk, settings.y_axes, settings.y_dir, settings.y_hofs_lo, settings.y_hspeed_lo );
      _umcwriter_write_file(&blk, 1);
      pos[settings.y_axes] = 0;
      break;
      
    case 1:
      UP3D_PROG_BLK_Home( &blk, settings.x_axes, settings.x_dir, settings.x_hofs_hi, settings.x_hspeed_hi );
      _umcwriter_write_file(&blk, 1);
      UP3D_PROG_BLK_Home( &blk, settings.x_axes, settings.x_dir, settings.x_hofs_lo, settings.x_hspeed_lo );
      _umcwriter_write_file(&blk, 1);
      pos[settings.x_axes] = 0;
      break;
      
    case 2:
      UP3D_PROG_BLK_Home( &blk, UP3DAXIS_Z, settings.z_dir, settings.z_hofs_hi, settings.z_hspeed_hi );
      _umcwriter_write_file(&blk, 1);
      UP3D_PROG_BLK_Home( &blk, UP3DAXIS_Z, settings.z_dir, settings.z_hofs_lo, settings.z_hspeed_lo );
      _umcwriter_write_file(&blk, 1);
      umcwriter_Z = 0;
      break;
  }

  umcwriter_planner_set_position( pos[settings.x_axes], pos[settings.y_axes], umcwriter_Z );
}

void umcwriter_virtual_home(double speedX, double speedY, double speedZ)
{
  umcwriter_planner_sync();

  umcwriter_print_time += 5; //apx. 5 seconds for virtual homeing

  double speed[2];
  speed[settings.x_axes] = min(speedX,settings.max_rate[settings.x_axes]*60.0 );
  speed[settings.y_axes] = min(speedY,settings.max_rate[settings.y_axes]*60.0 );
  speedZ = min( speedZ, 50.0*60.0 );

  UP3D_BLK blks[2];
  UP3D_PROG_BLK_MoveF( blks, -speed[0],0, -speed[1],0, -speedZ,0, 0,0);
  _umcwriter_write_file( blks, 2);
  umcwriter_planner_set_position(0,0,0);
}

#define UMC_Z_MAX 15.0
#define UMC_TRAVEL_MAX 100.0  /* mm/s, travel moves done by the printer's jog routine */

void umcwriter_move_direct(double X, double Y, double Z, double A, double F)
{
  umcwriter_planner_sync();

  /* direct moves bypass the planner: keep them gentle. X/Y no faster than the
     converter's speed cap, the bed (Z) no faster than UMC_Z_MAX mm/s */
  double feedX = min(F, UMC_TRAVEL_MAX*60.0);
  double feedY = min(F, UMC_TRAVEL_MAX*60.0);
  double feedZ = min(F, UMC_Z_MAX*60.0);
  double feedA = min(F, settings.max_rate[2]*60.0);

  double pos[3];
  plan_get_position(pos);
  double relA = A-pos[A_AXIS];

  double tX = fabs(X+pos[1])/feedX;
  double tY = fabs(Y-pos[0])/feedY;
  double tZ = fabs(Z-umcwriter_Z)/feedZ;
  //double tA = fabs(relA)/feedA;

  double t=0;
  if(tX>t) t=tX; if(tY>t) t=tY; if(tZ>t) t=tZ;
  umcwriter_print_time += t*60.0 + 0.2;   /* feeds are mm/min; plus a little for the stop/start */

  double topos[2];
  topos[settings.x_axes] = X * settings.x_dir;
  topos[settings.y_axes] = Y * settings.y_dir;

  double feed[2];
  feed[settings.x_axes] = feedX;
  feed[settings.y_axes] = feedY;

  UP3D_BLK blks[2];
  UP3D_PROG_BLK_MoveF( blks,-feed[0],topos[0],-feed[1],topos[1],-feedZ,-(umcwriter_Z_height-Z),feedA,relA );
  _umcwriter_write_file(blks, 2);

  umcwriter_planner_set_position(X,Y,A);
  umcwriter_Z = Z;
}

void umcwriter_planner_set_position(double X, double Y, double A)
{
  umcwriter_planner_sync();
  double pos[3];
  pos[settings.x_axes] = X * settings.x_dir;
  pos[settings.y_axes] = Y * settings.y_dir;
  pos[2] = A;
  plan_set_position(pos);
  st_reset();
}

void umcwriter_planner_set_a_position(double A)
{
  plan_set_e_position(A);
}


/* -----------------------------------------------------------------------------------------
   Write one motion segment the way UP Studio does.

   UP Studio never sends a motion segment longer than ~500 ticks, uses short ticks
   (2-5 kHz) and never gives an axis a speed of exactly 0 (an axis that should stay
   still gets speed 1, which can't add up to a step within 500 ticks). The original UP3D
   segments are long (up to 65535 ticks of ~1.3 ms) with many steps per tick and 0 for
   idle axes; on the UP Box this shows up as a slow drift every layer
   (github.com/MaikStohn/UP3D/issues/31).

   Here each segment is re-cut into chunks of <= UMC_MAX_TICKS short ticks. The step count
   of every chunk is fixed exactly (same floor() maths as the printer), so each axis still
   moves exactly the number of steps the converter planned for the whole segment.
   ----------------------------------------------------------------------------------------- */
#define UMC_TICK_TARGET 12500   /* cycles per tick at 50 MHz: 4 kHz */
#define UMC_MAX_TICKS   500     /* < 512, so a speed of 1 never makes a step */


static int64_t umc_ceil_div(int64_t a, int64_t b)   /* b > 0 */
{
  int64_t q = a / b;
  if( (a % b) != 0 && a > 0 ) q++;
  return q;
}

static int64_t umc_floor_div(int64_t a, int64_t b)  /* b > 0 */
{
  int64_t q = a / b;
  if( (a % b) != 0 && a < 0 ) q--;
  return q;
}

/* steps the converter planned for this axis (same expression as hoststepper.c) */
static int64_t umc_model_steps(int32_t p1, int32_t v, int32_t a)
{
  return (int64_t)floor( (float)(v*p1+a*(p1-1)*p1/2) / 512.0 );
}

static void umc_emit(uint16_t p1, uint16_t p2, const int32_t v[3], const int32_t a[3])
{
  UP3D_BLK blk;
  UP3D_PROG_BLK_MoveL(&blk, p1, p2, (int16_t)v[0], (int16_t)v[1], (int16_t)v[2], (int16_t)a[0], (int16_t)a[1], (int16_t)a[2]);
  _umcwriter_write_file(&blk, 1);
}

static void umcwriter_write_movel(const segment_up3d_t *ps)
{
  int32_t V[3] = { ps->p3, ps->p4, ps->p5 };
  int32_t A[3] = { ps->p6, ps->p7, ps->p8 };

  if( umc_classic )
  {
    umc_emit(ps->p1, ps->p2, V, A);
    return;
  }

  int64_t m = (ps->p2 + UMC_TICK_TARGET - 1) / UMC_TICK_TARGET;   /* split each tick into m */
  if( m < 1 ) m = 1;
  int64_t p2n = (ps->p2 + m/2) / m;
  if( p2n < 801 ) { m = 1; p2n = ps->p2; }
  int64_t N = (int64_t)ps->p1 * m;                                  /* ticks after the split */
  int64_t chunks = (N + UMC_MAX_TICKS - 1) / UMC_MAX_TICKS;
  if( chunks < 1 ) chunks = 1;

  int64_t total[3], done[3] = {0,0,0};
  int32_t an[3];
  for( int k = 0; k < 3; k++ )
  {
    total[k] = umc_model_steps(ps->p1, V[k], A[k]);
    double ad = (double)A[k] / (double)(m*m);
    an[k] = (int32_t)(ad >= 0 ? ad + 0.5 : ad - 0.5);
  }

  int64_t t0 = 0;
  for( int64_t c = 0; c < chunks; c++ )
  {
    int64_t t1 = (c == chunks-1) ? N : (N * (c+1)) / chunks;
    int64_t n = t1 - t0;
    int32_t vv[3], aa[3];
    for( int k = 0; k < 3; k++ )
    {
      /* where this axis should be at the end of the chunk (in steps) */
      int64_t target;
      if( c == chunks-1 ) target = total[k];
      else
      {
        double tau = (double)t1 / (double)m;               /* time in original ticks */
        double s = V[k]*tau + A[k]*tau*(tau-1.0)/2.0;     /* 1/512 steps */
        target = (int64_t)floor(s / 512.0);
        /* never overshoot the segment's total in either direction */
        if( total[k] >= 0 ) { if( target > total[k] ) target = total[k]; if( target < 0 ) target = 0; }
        else                { if( target < total[k] ) target = total[k]; if( target > 0 ) target = 0; }
      }
      int64_t st = target - done[k];
      int64_t acc = (int64_t)an[k] * n * (n-1) / 2;
      int64_t v = umc_ceil_div(st*512 - acc, n);
      /* the printer counts floor((v*n + a*n*(n-1)/2)/512) steps: make it exactly st */
      while( umc_floor_div(v*n + acc, 512) > st ) v--;
      while( umc_floor_div(v*n + acc, 512) < st ) v++;
      if( st == 0 && an[k] == 0 && v == 0 ) v = 1;       /* idle axis: 1, like UP Studio */
      if( v > 32767 || v < -32767 || an[k] > 32767 || an[k] < -32767 ||
          llabs(v*n + acc) >= (1LL<<24) )
      {
        /* can't express this chunk safely: send the segment unchanged */
        umc_emit(ps->p1, ps->p2, V, A);
        return;
      }
      vv[k] = (int32_t)v; aa[k] = an[k];
      done[k] += st;
    }
    umc_emit((uint16_t)n, (uint16_t)p2n, vv, aa);
    t0 = t1;
  }
}

void umcwriter_planner_add(double X, double Y, double A, double F)
{
  while( plan_check_full_buffer() )
  {
    segment_up3d_t *pseg;
    if( !st_get_next_segment_up3d(&pseg) )
      break;
    umcwriter_print_time += ((double)pseg->p2*(double)pseg->p1)/F_CPU;
    umcwriter_write_movel(pseg);
  }

  double pos[3];
  pos[settings.x_axes] = X * settings.x_dir;
  pos[settings.y_axes] = Y * settings.y_dir;
  pos[2] = A;
  double feed = F/60;
  if( !umc_classic && feed > UMC_PRINT_MAX ) feed = UMC_PRINT_MAX;   /* UP Studio never prints faster */
  plan_buffer_line( pos, feed, false);
}

void umcwriter_planner_sync()
{
  segment_up3d_t *pseg;
  for(;;)
  {
    while( st_get_next_segment_up3d(&pseg) )
    {
      umcwriter_print_time += ((double)pseg->p2*(double)pseg->p1)/F_CPU;

      umcwriter_write_movel(pseg);
    }
    
    if( 0 == plan_get_block_buffer_count() )
      break;
  }
}

void umcwriter_set_extruder_temp(double temp, bool wait)
{
  umcwriter_planner_sync();

  UP3D_BLK blk;

  if( temp )
  {
    //TODO: always ???
    UP3D_PROG_BLK_SetParameter(&blk,0x17,1);              //nozzle #1 open
    _umcwriter_write_file(&blk, 1);
  }

  if( temp )
  {
    UP3D_PROG_BLK_SetParameter(&blk,0x41,temp);          //TEMP FOR NOZZLE1_TEMP_REACHED
    _umcwriter_write_file(&blk, 1);
  }

  UP3D_PROG_BLK_SetParameter(&blk,PARA_NOZZLE1_TEMP,temp);
  _umcwriter_write_file(&blk, 1);
  UP3D_PROG_BLK_SetParameter(&blk,PARA_HEATER_NOZZLE1_ON,(temp)?1:0);
  _umcwriter_write_file(&blk, 1);

  if(wait)
  {
    umcwriter_print_time += 120; //apx. 2 minutes
  
    UP3D_PROG_BLK_SetParameter(&blk,PARA_RED_BLUE_BLINK,100);
    _umcwriter_write_file(&blk, 1);

    UP3D_PROG_BLK_WaitIfNot( &blk, PARA_TEMP_REACHED_N1, 1, '=' );
    _umcwriter_write_file(&blk, 1);

    UP3D_PROG_BLK_SetParameter(&blk,PARA_RED_BLUE_BLINK,200);
    _umcwriter_write_file(&blk, 1);

    umcwriter_set_report_data(-1,-1);
  }
}

void umcwriter_set_bed_temp(int32_t temp, bool wait)
{
  umcwriter_planner_sync();

  UP3D_BLK blk;
 
  if( temp )
  {
    UP3D_PROG_BLK_SetParameter(&blk,0x43,temp); //TEMP FOR BED_TEMP_REACHED
   _umcwriter_write_file(&blk, 1);
  }

  UP3D_PROG_BLK_SetParameter(&blk,PARA_BED_TEMP,temp);
  _umcwriter_write_file(&blk, 1);
  /* on the Box+ this is a countdown in 2 s units, not an on/off flag:
     keep the bed on for up to 24 h; the end G-code (M140 S0) switches it off */
  UP3D_PROG_BLK_SetParameter(&blk,PARA_HEATER_BED_ON,(temp)?43200:0);
  _umcwriter_write_file(&blk, 1);

  if(wait && (temp>umcwriter_bed_temp) ) //only wait if new temperature is higher
  {
    UP3D_PROG_BLK_SetParameter(&blk,PARA_RED_BLUE_BLINK,400);
    _umcwriter_write_file(&blk, 1);

    uint32_t waitsec = ((temp-umcwriter_bed_temp)/settings.heatbed_wait_factor)*60;
    umcwriter_pause(waitsec*1000);

    UP3D_PROG_BLK_SetParameter(&blk,PARA_RED_BLUE_BLINK,200);
    _umcwriter_write_file(&blk, 1);

    umcwriter_set_report_data(-1,-1);

    umcwriter_bed_temp = temp;
  }
}

void umcwriter_set_report_data(int32_t layer, double height)
{
  umcwriter_planner_sync();

  UP3D_BLK blk;

  if( layer>=0 )
  {
    UP3D_PROG_BLK_SetParameter(&blk,PARA_REPORT_LAYER,layer);
    _umcwriter_write_file(&blk, 1);
  }

  if( height>=0 )
  {
    float h = (float)height;
    UP3D_PROG_BLK_SetParameter(&blk,PARA_REPORT_HEIGHT,*((int32_t*)&h));
    _umcwriter_write_file(&blk, 1);
  }

  UP3D_PROG_BLK_SetParameter(&blk,PARA_REPORT_TIME_REMAIN,(int32_t)umcwriter_print_time);
  _umcwriter_write_file(&blk, 1);

  UP3D_PROG_BLK_SetParameter(&blk,PARA_REPORT_PERCENT,0);
  _umcwriter_write_file(&blk, 1);
}

void umcwriter_pause(uint32_t msec)
{
  umcwriter_planner_sync();

  umcwriter_print_time += msec/1000.0;

  UP3D_BLK blk;
  UP3D_PROG_BLK_Pause(&blk,msec);
  _umcwriter_write_file(&blk, 1);
}

void umcwriter_beep(uint32_t msec)
{
  umcwriter_planner_sync();

  UP3D_BLK blk;
  UP3D_PROG_BLK_Beeper(&blk,true);
  _umcwriter_write_file(&blk, 1);

  umcwriter_pause(msec);

  UP3D_PROG_BLK_Beeper(&blk,false);
  _umcwriter_write_file(&blk, 1);
}

void umcwriter_user_pause()
{
  umcwriter_planner_sync();

  umcwriter_print_time += 2; //2 seconds for processing

  UP3D_BLK blks[2];
  UP3D_PROG_BLK_MoveF( blks,150,0,150,0,10000,30,10000,0 );
  _umcwriter_write_file(blks, 2);

  umcwriter_beep(500);

  UP3D_BLK blk;
  
  UP3D_PROG_BLK_SetParameter(&blk,PARA_PAUSE_PROGRAM,1);
  _umcwriter_write_file(&blk, 1);

  UP3D_PROG_BLK_SetParameter(&blk,PARA_PRINT_STATUS,3);
  _umcwriter_write_file(&blk, 1);

  umcwriter_beep(500);

  UP3D_PROG_BLK_MoveF( blks,150,0,150,0,10000,-30,10000,0 );
  _umcwriter_write_file(blks, 2);
}
