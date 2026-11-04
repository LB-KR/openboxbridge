//////////////////
//Author: M.Stohn/
//////////////////
// Changed for OpenBoxBridge: the storing itself is UP3D_SendProgramSD (up3d.c), with a
// choice of method and an automatic second attempt; reports the printer model.

#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <stdbool.h>
#include <stdint.h>
#include <inttypes.h>
#include <unistd.h>
#include <sys/time.h>

#include "up3d.h"
#include "up3dcomm.h"
#include "compat.h"

#define upl_error(s) { printf("ERROR: %s\n",s); }

static struct timeval tval_start;

static void progress(uint32_t sent, uint32_t total)
{
  struct timeval now, diff;
  gettimeofday(&now, NULL);
  timersub(&now, &tval_start, &diff);
  int64_t usecs = (int64_t)diff.tv_sec*1000000+(int64_t)diff.tv_usec;
  if( usecs < 1 ) usecs = 1;
  double kb = sent * 20.0 / 1024.0;
  printf("UPload: %.2f kB sent (%u%%) [%.2f kB/sec]\r", kb, total ? (unsigned)((uint64_t)sent*100/total) : 100, kb*1000000.0/usecs);
  fflush(stdout);
}

int main(int argc, char const *argv[])
{
  const char *file = NULL;
  int method = -1;   /* -1 = automatic: UP Box+ = UP3D's way, then UP Studio's if the printer doesn't
                        confirm; UP300 = stream (it reads stored jobs from its SD card and fails) */
  for( int i = 1; i < argc; i++ )
  {
    if( !strncmp(argv[i], "--method=", 9) ) method = atoi(argv[i] + 9);
    else file = argv[i];
  }
  if( !file )
  {
    printf("Usage: %s program.umc [--method=0|1|2]\n\n", argv[0]);
    printf("  --method=0  UP3D's way of storing the job (SD slot 5, block counter check)\n");
    printf("  --method=1  UP Studio's way (program buffer committed to slot 9)\n");
    printf("  --method=2  stream it while it prints (USB must stay connected until the end)\n");
    printf("  default     UP Box+: method 0, then method 1 if the printer doesn't confirm the job\n");
    printf("              UP300:   method 2\n");
    return 0;
  }

  FILE* fdat = fopen( file, "rb" );
  if( !fdat )
    return 1;
  fseek(fdat, 0, SEEK_END);
  long flen = ftell(fdat);
  fseek(fdat, 0, SEEK_SET);
  uint32_t n = (uint32_t)(flen / (long)sizeof(UP3D_BLK));
  UP3D_BLK *blks = (UP3D_BLK*)malloc( sizeof(UP3D_BLK) * (n ? n : 1) );
  if( !blks || fread( blks, sizeof(UP3D_BLK), n, fdat ) != n ) { fclose(fdat); upl_error("cannot read the job file"); return 1; }
  fclose( fdat );

  if( !UP3D_Open() )
    return 2;

  if( !UP3D_IsPrinterResponsive() )
  {
    upl_error( "UP printer is not responding" );
    UP3D_Close();
    return 3;
  }

  uint32_t model = UP3D_GetPrinterModelId();
  printf("Printer model id %04X (USB product %04X)%s\n", model, UP3DCOMM_GetPID(), model == 0x2783 ? " - UP300 (experimental)" : "");
  printf("Job: %u blocks\n", n);
  fflush(stdout);

  if( method < 0 && (model == 0x2783 || UP3DCOMM_GetPID() == 0x2790) )
  {
    printf("UP300: streaming the job (stored jobs give 'SD Card Reading Error' on this printer)\n");
    fflush(stdout);
    method = 2;
  }

  gettimeofday(&tval_start, NULL);
  int r = (method == 2) ? UP3D_StreamProgram( blks, n, progress )
                        : UP3D_SendProgramSD( blks, n, method < 0 ? 0 : method, progress );
  printf("\n");
  if( r == 7 && method < 0 )
  {
    printf("The printer did not confirm the job. Trying UP Studio's way of storing it...\n");
    fflush(stdout);
    gettimeofday(&tval_start, NULL);
    r = UP3D_SendProgramSD( blks, n, 1, progress );
    printf("\n");
    if( r == 0 ) printf("Stored with method 1 (UP Studio's way).\n");
  }
  free( blks );

  switch( r )
  {
    case 0: break;
    case 4: upl_error("ClearProgramBuf failed"); break;
    case 5: upl_error("UseSDProgramBuf failed"); break;
    case 6: upl_error("Write to SD failed"); break;
    case 7: upl_error("Write to SD not finished"); break;
    case 8: upl_error("Selecting the stored job failed"); break;
    case 9: upl_error("StartResumeProgram failed"); break;
    default: upl_error("sending failed"); break;
  }
  UP3D_Close();
  return r;
}
