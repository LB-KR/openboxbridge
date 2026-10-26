///////////////////////////////////////////////////////
// Author: K.Scheffer
//
// Date: 27.Sep.2016
//
// tool to control and print out current printer status
//
//////////////////////////////////////////////////////

#include <stdlib.h>
//#ifdef _WIN32
//#include <ncurses/curses.h>
//#else
//#include <curses.h>
//#endif
//#include <signal.h>
#include <string.h>
//#include <sys/time.h>

#include <stdbool.h>
#include <stdint.h>

#include <stdio.h>
//#include <math.h>
#include <unistd.h>

#include "compat.h"
#include "up3d.h"
#include "up3dconf.h"


void print_usage_and_exit(char *name)
{
  printf("Usage: %s [stop | pos | on | off | init | heat <C> | bed <C> [minutes] | cool]\n\n", name);
  printf("          stop:  stops the current job (also from SD card), heaters off\n");
  printf("          on:    switch printer on and reports status\n");
  printf("          off:   switch printer off and reports status\n");
  printf("          no parameter reports current printer status:\n");
  printf("          <machine>;<program>;<system>;<temp>;<layer>;<height>;<percent>;<time>\n");
  printf("          <machine>: machine state \n");
  printf("          <program>: program state \n");
  printf("          <system>:  numeric value machine state \n");
  printf("          <temp>:    current nozzle temperture in C\n");
  printf("          <layer>:   reported printing layer\n");
  printf("          <height>:  reported printing height in mm\n");
  printf("          <percent>: reported completion in percent\n");
  printf("          <time>:    reported time remaining in seconds\n");
  printf("\n");
  exit(0);
}


/* Heaters off straight away (direct parameter writes, not program blocks:
   while a job runs from the SD card the program buffer is not read). */
static void heaters_off_now(void)
{
  UP3D_SetParameter(PARA_HEATER_NOZZLE1_ON, 0);
  UP3D_SetParameter(PARA_NOZZLE1_TEMP, 0);
  UP3D_SetParameter(PARA_HEATER_BED_ON, 0);
  UP3D_SetParameter(PARA_BED_TEMP, 0);
}

/* program state (param 0x01): 0 stopped, 1 running, 2 paused, 3 error */
static bool wait_program_not_running(int ms)
{
  for( int t = 0; t <= ms; t += 250 )
  {
    int32_t ps = UP3D_GetParameter(0x01);
    int32_t ms_ = UP3D_GetParameter(0x00);
    if( ps != 1 && ms_ != 2 ) return true;
    usleep(250*1000);
  }
  return false;
}

/* STOP: use the printer's own "stop program" request (param 0x0D), which
   works for jobs running from the SD card. Heaters off as well. If the
   printer ignores it, pause the program (0x0C) so the motion halts.
   Never calls Start/Resume (that could restart the job). */
static int do_stop(void)
{
  int32_t m0 = UP3D_GetParameter(0x00), p0 = UP3D_GetParameter(0x01);
  printf("Before stop: machine state %d, program state %d\n", m0, p0);
  fflush(stdout);

  bool sent = UP3D_SetParameter(0x0D, 1);
  heaters_off_now();
  UP3D_SetParameter(0x1C, 0);                 /* "not printing" */
  if( !sent ) { printf("ERROR: the printer did not accept the stop request\n"); return 10; }

  if( wait_program_not_running(5000) )
  {
    UP3D_ClearProgramBuf();
    usleep(300*1000);
    UP3D_SetParameter(0x0D, 0);               /* ready for the next job */
    printf("STOPPED: machine state %d, program state %d. Heaters off.\n",
           UP3D_GetParameter(0x00), UP3D_GetParameter(0x01));
    return 0;
  }

  printf("Stop request not obeyed after 5 s - pausing the program instead\n"); fflush(stdout);
  UP3D_SetParameter(0x0C, 1);
  heaters_off_now();
  if( wait_program_not_running(5000) )
  {
    printf("PAUSED: motion halted and heaters off, but the job is only paused.\n"
           "Press the button on the printer to end it.\n");
    return 11;
  }
  printf("ERROR: the printer is still running the job (machine state %d, program state %d).\n"
         "Use the button on the printer.\n", UP3D_GetParameter(0x00), UP3D_GetParameter(0x01));
  return 12;
}

/* pos: wait until the printer has finished moving, then report where the
   head and bed are (Z = bed position in mm from Z home, bed up = negative,
   the same frame the converter uses). Used by nozzle height calibration. */
static int do_pos(void)
{
  int i, still = 0;
  usleep(600*1000);                            /* let a just-sent move start */
  for( i = 0; i < 240; i++ )                   /* up to 60 s */
  {
    if( UP3D_GetParameter(0x00) != 2 && UP3D_GetParameter(0x01) != 1 ) { if( ++still >= 2 ) break; }
    else still = 0;
    usleep(250*1000);
  }
  if( i == 240 ) { printf("ERROR: the printer is still moving\n"); return 3; }
  usleep(200*1000);

  double steps[3] = { 644.0, 644.0, 3220.0 };
  TT_tagPrinterInfoHeader pihdr; TT_tagPrinterInfoName piname; TT_tagPrinterInfoData pidata; TT_tagPrinterInfoSet pisets[8];
  if( UP3D_GetPrinterInfo( &pihdr, &piname, &pidata, pisets ) )
  {
    if( pidata.f_steps_mm_x > 1 ) steps[0] = pidata.f_steps_mm_x;
    if( pidata.f_steps_mm_y > 1 ) steps[1] = pidata.f_steps_mm_y;
    if( pidata.f_steps_mm_z > 1 ) steps[2] = pidata.f_steps_mm_z;
  }
  UP3D_SetParameter(0x94, 999);                /* full precision position reports */
  usleep(100*1000);
  int32_t raw[3];
  for( int k = 0; k < 3; k++ ) raw[k] = UP3D_GetAxisPosition(k+1);
  UP3D_SetParameter(0x94, 0);
  double gx = (raw[settings_box.x_axes] / steps[settings_box.x_axes]) / settings_box.x_dir;
  double gy = (raw[settings_box.y_axes] / steps[settings_box.y_axes]) / settings_box.y_dir;
  double z  = raw[2] / steps[2];
  printf("Z steps %d at %.1f steps/mm\n", raw[2], steps[2]);
  printf("POS %.3f %.3f %.3f\n", gx, gy, z);
  return 0;
}

int main(int argc, char *argv[])
{
  if (argc > 4)
    print_usage_and_exit(argv[0]);
  
  if( !UP3D_Open() )
    return -1;
  
  if (argc >= 2)
  {
    if ( !strcmp(argv[1], "stop") )
    {
      int r = do_stop();
      UP3D_Close();
      return r;
    }
    else if ( !strcmp(argv[1], "pos") )
    {
      int r = do_pos();
      UP3D_Close();
      return r;
    }
    else if ( !strcmp(argv[1], "on") )
    {
      UP3D_BLK blk;
      UP3D_ClearProgramBuf();
      UP3D_PROG_BLK_Power(&blk,true);UP3D_WriteBlock(&blk);
      UP3D_PROG_BLK_Stop(&blk);UP3D_WriteBlock(&blk);
      UP3D_StartResumeProgram();
    }
    else if ( !strcmp(argv[1], "off") )
    {
      UP3D_BLK blk;
      UP3D_ClearProgramBuf();
      UP3D_PROG_BLK_Power(&blk,false);UP3D_WriteBlock(&blk);
      UP3D_PROG_BLK_Stop(&blk);UP3D_WriteBlock(&blk);
      UP3D_StartResumeProgram();
    }
    else if ( !strcmp(argv[1], "init") )
    {
      /* the printer's own initialise routine (homes and parks) */
      UP3D_ClearProgramBuf();
      UP3D_InsertRomProgram(0);
      UP3D_StartResumeProgram();
    }
    else if ( (!strcmp(argv[1], "heat") || !strcmp(argv[1], "bed")) && (argc == 3 || argc == 4) )
    {
      /* same parameter blocks the converter uses for M104 / M140 */
      int t = atoi(argv[2]);
      bool nozzle = !strcmp(argv[1], "heat");
      if( t < 0 || t > (nozzle ? 300 : 110) ) { UP3D_Close(); printf("ERROR: temperature out of range\n"); return 2; }
      UP3D_BLK blk;
      UP3D_ClearProgramBuf();
      if( t ) { UP3D_PROG_BLK_Power(&blk,true); UP3D_WriteBlock(&blk); }
      if( nozzle )
      {
        if( t ) { UP3D_PROG_BLK_SetParameter(&blk,0x17,1); UP3D_WriteBlock(&blk);
                  UP3D_PROG_BLK_SetParameter(&blk,0x41,t); UP3D_WriteBlock(&blk); }
        UP3D_PROG_BLK_SetParameter(&blk,PARA_NOZZLE1_TEMP,t); UP3D_WriteBlock(&blk);
        UP3D_PROG_BLK_SetParameter(&blk,PARA_HEATER_NOZZLE1_ON,t?1:0); UP3D_WriteBlock(&blk);
      }
      else
      {
        if( t ) { UP3D_PROG_BLK_SetParameter(&blk,0x35,0); UP3D_WriteBlock(&blk);
                  UP3D_PROG_BLK_SetParameter(&blk,0x43,t); UP3D_WriteBlock(&blk); }
        UP3D_PROG_BLK_SetParameter(&blk,PARA_BED_TEMP,t); UP3D_WriteBlock(&blk);
        /* bed heater value is a countdown in 2 s units (default 30 min) */
        int minutes = (argc == 4) ? atoi(argv[3]) : 30;
        if( minutes < 1 ) minutes = 1;
        if( minutes > 240 ) minutes = 240;
        UP3D_PROG_BLK_SetParameter(&blk,PARA_HEATER_BED_ON,t?(minutes*30):0); UP3D_WriteBlock(&blk);
      }
      UP3D_PROG_BLK_Stop(&blk); UP3D_WriteBlock(&blk);
      UP3D_StartResumeProgram();
    }
    else if ( !strcmp(argv[1], "cool") )
    {
      UP3D_BLK blk;
      UP3D_ClearProgramBuf();
      UP3D_PROG_BLK_SetParameter(&blk,PARA_NOZZLE1_TEMP,0); UP3D_WriteBlock(&blk);
      UP3D_PROG_BLK_SetParameter(&blk,PARA_HEATER_NOZZLE1_ON,0); UP3D_WriteBlock(&blk);
      UP3D_PROG_BLK_SetParameter(&blk,PARA_BED_TEMP,0); UP3D_WriteBlock(&blk);
      UP3D_PROG_BLK_SetParameter(&blk,PARA_HEATER_BED_ON,0); UP3D_WriteBlock(&blk);
      UP3D_PROG_BLK_Stop(&blk); UP3D_WriteBlock(&blk);
      UP3D_StartResumeProgram();
    }
    else
    {
      UP3D_Close();
      print_usage_and_exit(argv[0]);
    }
  }
  
  int32_t mstat = UP3D_GetMachineState();
  int32_t pstat = UP3D_GetProgramState();
  int32_t sstat = UP3D_GetSystemState();
  float   temp  = UP3D_GetHeaterTemp(1);
  int32_t layer = UP3D_GetLayer();
  float   height = UP3D_GetHeight();
  int32_t percent = UP3D_GetPercent();
  int32_t tr = UP3D_GetTimeRemaining();

  /* extra fields for the app: targets, heater states and head position */
  int32_t nset = UP3D_GetHeaterSetTemp(1), non = UP3D_GetHeaterState(1), nreached = UP3D_GetHeaterTempReached(1);
  int32_t bset = UP3D_GetHeaterSetTemp(3), bon = UP3D_GetHeaterState(3);
  double steps[3] = { 644.0, 644.0, 3220.0 };
  uint32_t serial = 0;
  {
    TT_tagPrinterInfoHeader pihdr; TT_tagPrinterInfoName piname; TT_tagPrinterInfoData pidata; TT_tagPrinterInfoSet pisets[8];
    if( UP3D_GetPrinterInfo( &pihdr, &piname, &pidata, pisets ) )
    {
      serial = pihdr.u32_printerserial;
      if( pidata.f_steps_mm_x > 1 ) steps[0] = pidata.f_steps_mm_x;
      if( pidata.f_steps_mm_y > 1 ) steps[1] = pidata.f_steps_mm_y;
      if( pidata.f_steps_mm_z > 1 ) steps[2] = pidata.f_steps_mm_z;
    }
  }
  double a[3];
  for( int k = 0; k < 3; k++ ) a[k] = (double)UP3D_GetAxisPosition(k+1) / steps[k];
  /* slicer-frame X/Y before the bed-centre offset (same mapping as the converter) */
  double gx = a[settings_box.x_axes] / settings_box.x_dir, gy = a[settings_box.y_axes] / settings_box.y_dir;

  
  float btemp = UP3D_GetHeaterTemp(3);   /* platform temperature (3rd heater channel) */
  printf("%s;%s;%s;%0.2f;%d;%0.2f;%d;%d;%d;%d;%d;%d;%d;%0.2f;%0.2f;%0.2f;%0.2f;%u\n",
         UP3D_STR_MACHINE_STATE[mstat],
         UP3D_STR_PROGRAM_STATE[pstat],
         UP3D_STR_SYSTEM_STATE[sstat],
         temp,
         layer,
         height,
         percent,
         tr,
         nset, non, nreached, bset, bon, gx, gy, a[2], btemp, serial );
  
  UP3D_Close();
  return 0;
}

