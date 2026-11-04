//////////////////
//Author: M.Stohn/
//////////////////

#include "up3d.h"
#include "up3dcomm.h"

#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/time.h>
#include <time.h>
#include <math.h>

#include "compat.h"

static uint32_t _up3d_connected_fw_version;

bool UP3D_Open()
{
  return UP3DCOMM_Open();
}

void UP3D_Close()
{
  UP3DCOMM_Close();
}

bool UP3D_IsPrinterResponsive()
{
  static const uint8_t UP3D_CMD_1[] = { 0x01, 0x00 }; //GetFwVersion
  uint8_t resp[2048];
  if( (UP3DCOMM_Write( UP3D_CMD_1, sizeof(UP3D_CMD_1) ) != sizeof(UP3D_CMD_1)) ||
      (5 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[4]) )
    return false;

  _up3d_connected_fw_version = le32toh(*((uint32_t*)resp));

  return true;
}

bool UP3D_ClearProgramBuf()
{
  static const uint8_t UP3D_CMD_43[] = { 0x43 };
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_43, sizeof(UP3D_CMD_43) ) != sizeof(UP3D_CMD_43)) ||
      (1 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[0]) )
    return false;

  return true;
}

bool UP3D_UseSDProgramBuf( uint8_t progID, bool enableWrite )
{
  if( progID>9 )
    return false;

  uint8_t UP3D_CMD_6C_X_Y[3] = { 0x6C, progID, enableWrite?0x01:0x00 };
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_6C_X_Y, sizeof(UP3D_CMD_6C_X_Y) ) != sizeof(UP3D_CMD_6C_X_Y)) ||
      (1 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[0]) )
    return false;

  return true;
}

bool UP3D_SetPrintJobInfo( uint8_t progID, uint8_t x, uint32_t y )
{
  if( progID>9 )
    return false;

  uint8_t UP3D_CMD_63_X_Y[7] = { 0x63, progID, x, (y>>0)&0xFF, (y>>8)&0xFF, (y>>16)&0xFF, (y>>24)&0xFF };
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_63_X_Y, sizeof(UP3D_CMD_63_X_Y) ) != sizeof(UP3D_CMD_63_X_Y)) ||
      (1 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[0]) )
    return false;

  return true;
}

bool UP3D_InsertRomProgram( uint8_t prog )
{
  uint8_t UP3D_CMD_4C_X[] = { 0x4C, '0'+prog };
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_4C_X, sizeof(UP3D_CMD_4C_X) ) != sizeof(UP3D_CMD_4C_X)) ||
      (5 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[4]) ||
      (1 != resp[0])
    )
    return false;

  return true;
}

static int64_t _up3d_free_blocks(void)   /* -1 = the printer didn't answer */
{
  static const uint8_t UP3D_CMD_46[] = { 0x46 };
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_46, sizeof(UP3D_CMD_46) ) != sizeof(UP3D_CMD_46)) ||
      (5 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[4]) )
    return -1;

  return le32toh( *((uint32_t*)resp) );
}

uint32_t UP3D_GetFreeBlocks()
{
  int64_t f = _up3d_free_blocks();
  return f < 0 ? 0 : (uint32_t)f;
}

bool UP3D_WriteBlocks( const UP3D_BLK *data, uint8_t blocks )
{
  uint8_t send[2048];
  int noanswer = 0, nospace = 0;
  for( ;blocks>0; )
  {
    uint8_t b = blocks;

    if( _up3d_connected_fw_version<330 )
      if (b > 3) b = 3; //older firmware versions support max 64 bytes (3 blocks) per write only

    if( b>72 ) b = 72;                          //limit to max. 2048 bytes per logical packet

    /* Never wait forever: give up if the printer stops answering (switched off,
       unplugged, reset) for ~3 s, or has no room for 60 s. Otherwise this program
       would sit holding the USB connection and block every other tool. */
    int64_t fb = _up3d_free_blocks();
    if( fb < 0 )
    {
      if( ++noanswer >= 30 ) { fprintf(stderr, "ERROR: the printer stopped answering\n"); return false; }
      usleep( 100000 );
      continue;
    }
    noanswer = 0;
    uint32_t fblocks = (uint32_t)fb;
    if( fblocks == 0 && ++nospace >= 600 ) { fprintf(stderr, "ERROR: the printer has had no room for new data for 60 s\n"); return false; }
    if( fblocks > 0 ) nospace = 0;
    if( b>fblocks ) b = fblocks;

    if( b>0 )
    {
      uint8_t UP3D_CMD_2F_X[] = { 0x2F, b };
      memcpy( &send[0], UP3D_CMD_2F_X, 2 );
      memcpy( &send[2], data, 20*b );
      //data++;
      if( UP3DCOMM_Write( send, 2+20*b ) != 2+20*b )
        return false;

      blocks -= b;
      data += b;
    }
    else
      usleep( 100000 );
  }

  return true;
}

bool UP3D_WriteBlock( const UP3D_BLK *data ) 
{
  return UP3D_WriteBlocks(data,1);
}

bool UP3D_StartResumeProgram()
{
  static const uint8_t UP3D_CMD_58[] = { 0x58 };
  uint8_t resp[2048];

  /* a STOP (or pause) request left set would end the new program at once */
  if( UP3D_GetParameter(0x0D) ) UP3D_SetParameter(0x0D, 0);
  if( UP3D_GetParameter(0x0C) ) UP3D_SetParameter(0x0C, 0);

  if( (UP3DCOMM_Write( UP3D_CMD_58, sizeof(UP3D_CMD_58) ) != sizeof(UP3D_CMD_58)) ||
      (0 == UP3DCOMM_Read( resp, sizeof(resp) )) /*||
      (6 != resp[0])*/ )                                //returns "Exe Count: 0" 06
    return false;
  return true;
}

bool UP3D_SetParameter(uint8_t param, uint32_t value)
{
  uint8_t UP3D_CMD_56_X_Y[6] = { 0x56, param, 0x00,0x00,0x00,0x00 };
  memcpy( &UP3D_CMD_56_X_Y[2], &value, sizeof(value) );
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_56_X_Y, sizeof(UP3D_CMD_56_X_Y) ) != sizeof(UP3D_CMD_56_X_Y)) ||
      (1 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[0]) )
    return false;

  return true;
}

uint32_t UP3D_GetParameter(uint8_t param)
{
  uint8_t UP3D_CMD_76_X[] = { 0x76, param };
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_76_X, sizeof(UP3D_CMD_76_X) ) != sizeof(UP3D_CMD_76_X)) ||
      (5 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[4]) )
    return 0;

  return le32toh( *((uint32_t*)resp) );
}

bool UP3D_GetSystemVar(uint8_t param, int32_t *var)
{
  uint8_t UP3D_CMD_76_X[] = { 0x76, param };
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_76_X, sizeof(UP3D_CMD_76_X) ) != sizeof(UP3D_CMD_76_X)) ||
      (5 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[4]) )
    return false;

  *var = le32toh( *((uint32_t*)resp) );
  return true;
}

int32_t UP3D_GetAxisPosition(int axis)
{
  if( (axis<1) || (axis>4) )
    return -1;
  uint8_t UP3D_CMD_70_X[] = { 0x70, '0'+axis-1 };
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_70_X, sizeof(UP3D_CMD_70_X) ) != sizeof(UP3D_CMD_70_X)) ||
      (5 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[4]) )
    return 0;

  return le32toh( *((uint32_t*)resp) );
}

bool UP3D_SendRandom()
{
  uint8_t UP3D_CMD_2B_RRR[9] = { 0x2B, 0xCA,0xFE,0x00,0x00,0x00,0x00,0x12,0x34 };
  uint8_t resp[2048];

  if( (UP3DCOMM_Write( UP3D_CMD_2B_RRR, sizeof(UP3D_CMD_2B_RRR) ) != sizeof(UP3D_CMD_2B_RRR)) ||
      (1 != UP3DCOMM_Read( resp, sizeof(resp) )) ||
      (6 != resp[0]) )
    return false;

  return true;
}

/* Collects one complete answer: everything the printer sends until it goes quiet. */
static int _up3d_collect( uint8_t *buf, int max, int first_timeout )
{
  int total = 0, to = first_timeout;
  while( total < max )
  {
    int n = UP3DCOMM_ReadTO( buf + total, max - total, to );
    if( n <= 0 ) break;
    total += n;
    to = 60;
  }
  return total;
}

/* The info block is the same on every model: header (28 bytes), name, data (56 bytes),
   then the parameter sets (136 bytes each). The UP Box+ sends a leading 07 and a 63-byte
   name in separate pieces; the UP300 sends it all at once with the name padded to 64. */
static bool _up3d_parse_info( const uint8_t *b, int n, TT_tagPrinterInfoHeader *pihdr, TT_tagPrinterInfoName *piname,
                              TT_tagPrinterInfoData *pidata, TT_tagPrinterInfoSet pisets[8] )
{
  int off = (n > 0 && b[0] == 7) ? 1 : 0;
  if( n < off + 28 ) return false;
  memcpy( pihdr, b + off, 28 );
  if( pihdr->flt_unk1 < 0.5f || pihdr->flt_unk1 > 2.0f ) return false;
  for( int nl = 63; nl <= 64; nl++ )
  {
    int d = off + 28 + nl;
    if( n < d + 56 ) continue;
    TT_tagPrinterInfoData t;
    memcpy( &t, b + d, 56 );
    if( t.u32_NumSets > 16 || !(t.f_steps_mm_x > 1.0f && t.f_steps_mm_x < 100000.0f) ) continue;
    memset( piname, 0, sizeof(*piname) );
    memcpy( piname->printer_name, b + off + 28, 62 );
    *pidata = t;
    if( pidata->u32_NumSets > 8 ) pidata->u32_NumSets = 8;
    memset( pisets, 0, sizeof(TT_tagPrinterInfoSet) * 8 );
    for( uint32_t s = 0; s < pidata->u32_NumSets; s++ )
      if( n >= d + 56 + 136 * (int)(s + 1) ) memcpy( &pisets[s], b + d + 56 + 136 * s, 136 );
    return true;
  }
  return false;
}

bool UP3D_GetPrinterInfo(TT_tagPrinterInfoHeader *pihdr, TT_tagPrinterInfoName *piname, TT_tagPrinterInfoData *pidata, TT_tagPrinterInfoSet pisets[8])
{
  /* "52 00" is what UP3D always sent; "52 77 A6 5B" is what UP Studio sends (UP Box+ and UP300) */
  static const uint8_t req[2][4] = { { 0x52, 0x00 }, { 0x52, 0x77, 0xA6, 0x5B } };
  static const int     len[2]    = { 2, 4 };
  static uint8_t buf[8192];
  for( int a = 0; a < 2; a++ )
  {
    if( UP3DCOMM_Write( req[a], len[a] ) != len[a] )
      return false;
    int n = _up3d_collect( buf, sizeof(buf), 800 );
    if( _up3d_parse_info( buf, n, pihdr, piname, pidata, pisets ) )
      return true;
  }
  return false;
}

/* Model from the info header: 0x2783 = UP300, 0x2710 = UP Box+ (others as UP3D knew them). */
uint32_t UP3D_GetPrinterModelId(void)
{
  TT_tagPrinterInfoHeader h; TT_tagPrinterInfoName nm; TT_tagPrinterInfoData d; TT_tagPrinterInfoSet st[8];
  if( UP3D_GetPrinterInfo( &h, &nm, &d, st ) ) return h.u32_printerid;
  return 0;
}

///////////////////////////////////////////

int32_t UP3D_GetMachineState()
{
  int32_t state = UP3D_GetParameter(0x00);
  if( (state<0) || (state>4) )
    state = 5;
  return state;
}

int32_t UP3D_GetProgramState()
{
  int32_t state = UP3D_GetParameter(0x01);
  if( (state<0) || (state>3) )
    state = 4;
  return state;
}

int32_t UP3D_GetSystemState()
{
  int32_t state = UP3D_GetParameter(0x10);
  if( (state<0) || (state>22) )
    state = 23;
  return state;
}

int32_t UP3D_GetMotorState(int motor)
{
  if( (motor<1) || (motor>4) )
    return -1;
  int32_t state = UP3D_GetParameter(0x02 + motor - 1);
  if( (state<0) || (state>5) )
    state = 6;
  return state;
}

uint32_t UP3D_GetPositiveLimitState(int motor)
{
  if( (motor<1) || (motor>4) )
    return -1;
  return UP3D_GetParameter(0x22 + motor - 1);
}

uint32_t UP3D_GetNegativeLimitState(int motor)
{
  if( (motor<1) || (motor>4) )
    return -1;
  return UP3D_GetParameter(0x26 + motor - 1);
}

int32_t UP3D_GetLayer()
{
  return UP3D_GetParameter(0x0A);
}

float UP3D_GetHeight()
{
  uint32_t f = UP3D_GetParameter(0x0B);
  return *((float*)&f);
}

int32_t UP3D_GetPercent()
{
  return UP3D_GetParameter(0x4C);
}

int32_t UP3D_GetTimeRemaining()
{
  return UP3D_GetParameter(0x4D);
}

int32_t UP3D_GetPrintState()
{
  return UP3D_GetParameter(0x1C);
}

int32_t UP3D_GetHeaterTempReached(int heater)
{
  if( (heater<1) || (heater>2) )
    return -1;
  return UP3D_GetParameter(0x12 + heater - 1);
}

int32_t UP3D_GetHeaterState(int heater)
{
  if( (heater<1) || (heater>3) )
    return -1;
  return UP3D_GetParameter(0x14 + heater - 1);
}

int32_t UP3D_GetHeaterSetTemp(int heater)
{
  if( (heater<1) || (heater>4) )
    return -1;
  return UP3D_GetParameter(0x39 + heater - 1);
}

float UP3D_GetHeaterTemp(int heater)
{
  if( (heater<1) || (heater>4) )
    return -1;

  uint32_t f = UP3D_GetParameter(0x06 + heater -1);
  return *((float*)&f);
}

int32_t UP3D_GetHeaterTargetTemp(int heater)
{
  if( (heater<1) || (heater>4) )
    return -1;
  return UP3D_GetParameter(0x2D + heater -1);
}

int32_t UP3D_GetHF(int heater)
{
  if( (heater<1) || (heater>4) )
    return -1;
  return UP3D_GetParameter(0x18 + heater - 1);
}

int32_t UP3D_GetSupportState()
{
  return UP3D_GetParameter(0x11);
}

int32_t UP3D_GetFeedErrorLength()
{
  return UP3D_GetParameter(0x1D); //QQQ /1000 ?
}

float UP3D_GetFeedBackLength()
{
  return ((float)UP3D_GetParameter(0x36))/1000;
}

int32_t UP3D_GetAxisState(int axis)
{
  if( (axis<1) || (axis>4) )
    return -1;
  int32_t state = UP3D_GetParameter(0x1E + axis - 1);
  if( state>2 )
    state = 3;
  return state;
}

float UP3D_GetChangeNozzleTime()
{
  return ((float)UP3D_GetParameter(0x2A))/1000;
}

float UP3D_GetJumpTime()
{
  return ((float)UP3D_GetParameter(0x2B))/1000;
}

int32_t UP3D_GetUsedNozzle()
{
  return UP3D_GetParameter(0x2C);
}

int32_t UP3D_GetCheckDoor()
{
  return UP3D_GetParameter(0x31);
}

int32_t UP3D_GetCheck24V()
{
  return UP3D_GetParameter(0x32);
}

int32_t UP3D_GetCheckPowerKey()
{
  return UP3D_GetParameter(0x33);
}

int32_t UP3D_GetCheckLightKey()
{
  return UP3D_GetParameter(0x34);
}

int32_t UP3D_GetCheckWorkRoomFan()
{
  return UP3D_GetParameter(0x35);
}

int32_t UP3D_GetReadFromSD()
{
  return UP3D_GetParameter(0x36);
}

int32_t UP3D_GetWriteToSD()
{
  return UP3D_GetParameter(0x37);
}

bool UP3D_GetPrinterStatus(TT_tagPrinterStatus *status)
{
  int32_t sysvar, sysvar2 = 0;
  if( UP3D_GetSystemVar( 0, &sysvar) )     //PARA_SYSTEM_STATUS
    status->SystemStatus = (uint8_t)sysvar;

  if( !UP3D_GetSystemVar( 0x10, &sysvar) ) //PARA_PRINT_STATUS
    return false;
  status->PrintStatus = (uint8_t)sysvar;

  if( UP3D_GetSystemVar( 0x0A, &sysvar) )  //PARA_REPORT_LAYER
    status->ReportLayer = htole16((uint16_t)sysvar);

  if( UP3D_GetSystemVar( 0x0B, &sysvar) )  //PARA_REPORT_HEIGHT
    status->ReportHeight = htole16((int16_t)floor( (*((float*)&sysvar))*10.0 ) );

  if( UP3D_GetSystemVar( 0x4D, &sysvar) && UP3D_GetSystemVar( 0x4C, &sysvar2) )  //PARA_REPORT_TIME_REMAIN PARA_REPORT_PERCENT 
    status->ReportTimeAndPercent = htole32(sysvar2 + (sysvar<<8));

  if( UP3D_GetSystemVar( 6, &sysvar) )     //NOZZLE1 TEMP
    status->Nozzle1Temp = htole16((int16_t)floor( (*((float*)&sysvar))*50.0 ));
  if( UP3D_GetSystemVar( 7, &sysvar) )     //NOZZLE2 TEMP
    status->Nozzle2Temp = htole16((int16_t)floor( (*((float*)&sysvar))*50.0 ));
  if( UP3D_GetSystemVar( 8, &sysvar) )     //BED TEMP
    status->BedTemp = htole16((int16_t)floor( (*((float*)&sysvar))*50.0 ));
  if( UP3D_GetSystemVar( 9, &sysvar) )     //ROOM TEMP
    status->RoomTemp = htole16((int16_t)floor( (*((float*)&sysvar))*50.0 ));

  status->HeaterStatus = 0;
  if( UP3D_GetSystemVar( 0x14, &sysvar) )     //HEATER NOZZLE1 STATUS
    status->HeaterStatus |= sysvar?1:0;
  if( UP3D_GetSystemVar( 0x15, &sysvar) )     //HEATER NOZZLE2 STATUS
    status->HeaterStatus |= sysvar?2:0;
  if( UP3D_GetSystemVar( 0x16, &sysvar) )     //HEATER BED STATUS
    status->HeaterStatus |= sysvar?4:0;

  UP3D_SetParameter( 0x94, 999 );             //SET_Z_PRECISION

  uint32_t v;
  for( v=0; v<4; v++ )
  {
    status->AxisStatus[v] = 0;

    if( UP3D_GetSystemVar( v+2, &sysvar) )    //MOTOR STATUS
      status->AxisStatus[v] |= sysvar & 7;

    if( UP3D_GetSystemVar( v+0x18, &sysvar) ) //HF?
      status->AxisStatus[v] |= sysvar?8:0;

    if( UP3D_GetSystemVar( v+0x26, &sysvar) ) //NL?
      status->AxisStatus[v] |= sysvar?16:0;

    if( UP3D_GetSystemVar( v+0x22, &sysvar) ) //ENDSTOP
      status->AxisStatus[v] |= sysvar?32:0;

    if( UP3D_GetSystemVar( v+0x1E, &sysvar) ) //ERROR
      status->AxisStatus[v] |= sysvar<<6;

    sysvar = UP3D_GetAxisPosition(v);
    float f = ((float)sysvar) / 854.0;        //FIXED VALUE FOR NOW
      status->AxisPosition[v] = htole32( *((uint32_t*)&f) );
  }

  UP3D_SetParameter( 0x94, 0 );             //SET_Z_PRECISION

  if( UP3D_GetSystemVar( 0x5E, &sysvar) )     //?
    status->Para_x5E = htole32(sysvar);
  if( UP3D_GetSystemVar( 0x5F, &sysvar) )     //?
    status->Para_x5F = htole32(sysvar);
  if( UP3D_GetSystemVar( 0x56, &sysvar) )     //?
    status->Para_x56 = htole32(sysvar);
  if( UP3D_GetSystemVar( 0x57, &sysvar) )     //?
    status->Para_x57 = htole32(sysvar);

  status->UnkMinusOne = htole64((int64_t)-1);
  return true;
}

///////////////////////////////////////////
// Storing a program on the printer and starting it (a print job, or a long control
// program). Two ways of doing it:
//   method 0 - UP3D's: switch the SD program slot to write mode (6C 05 01) and stream
//              the blocks into it, then check the printer's block counter (param C6).
//   method 1 - UP Studio's (recorded on an UP Box+): put the blocks in the program
//              buffer and commit them to slot 9 fourteen at a time (63 09 0E offset).
// Returns 0 when the program is running, otherwise an error code:
//   4 clear failed, 5 slot select failed, 6 write failed, 7 storing not confirmed,
//   8 slot select for running failed, 9 start failed
///////////////////////////////////////////

static uint32_t _ms_now(void)
{
  struct timeval tv; gettimeofday(&tv, NULL);
  return (uint32_t)(tv.tv_sec * 1000 + tv.tv_usec / 1000);
}

static void _up3d_job_header( const char *name )
{
  /* 22 6D 3C + 60 bytes: as UP Studio sends it (job date/time, optional UTF-16 name) */
  uint8_t h[63]; uint8_t r[64];
  memset( h, 0, sizeof(h) );
  h[0] = 0x22; h[1] = 0x6D; h[2] = 0x3C;
  time_t t = time(NULL); struct tm *lt = localtime(&t);
  if( lt ) { h[10] = (uint8_t)(lt->tm_year % 100); h[11] = (uint8_t)(lt->tm_mon + 1); h[12] = (uint8_t)lt->tm_mday;
             h[13] = (uint8_t)lt->tm_hour; h[14] = (uint8_t)lt->tm_min; h[15] = (uint8_t)lt->tm_sec; }
  if( name )
    for( int i = 0; name[i] && 16 + 2 * i + 1 < 63 - 8; i++ ) h[16 + 2 * i] = (uint8_t)name[i];
  if( UP3DCOMM_Write( h, sizeof(h) ) == sizeof(h) )
    UP3DCOMM_Read( r, sizeof(r) );
}

static void _up3d_cmd44( const uint8_t *c, int n )
{
  uint8_t r[64];
  if( UP3DCOMM_Write( c, n ) == n ) UP3DCOMM_Read( r, sizeof(r) );
}

int UP3D_SendProgramSD( const UP3D_BLK *blks, uint32_t n, int method, up3d_progress_fn progress )
{
  if( method == 1 )
  {
    /* pad to whole groups of 14 with Stop blocks (they are never reached) */
    uint32_t np = (n + 13) / 14 * 14;
    UP3D_BLK *b = (UP3D_BLK*)malloc( sizeof(UP3D_BLK) * (np ? np : 1) );
    if( !b ) return 6;
    memcpy( b, blks, sizeof(UP3D_BLK) * n );
    for( uint32_t i = n; i < np; i++ ) UP3D_PROG_BLK_Stop( &b[i] );

    _up3d_job_header( NULL );
    if( !UP3D_ClearProgramBuf() ) { free(b); return 4; }
    uint32_t last_keep = _ms_now();
    for( uint32_t off = 0; off < np; off += 70 )
    {
      uint32_t c = (np - off) > 70 ? 70 : (np - off);
      if( !UP3D_WriteBlocks( b + off, (uint8_t)c ) ) { free(b); return 6; }
      for( uint32_t g = 0; g < c; g += 14 )
      {
        uint32_t o = off + g;
        uint8_t cmd[7] = { 0x63, 0x09, 0x0E, (uint8_t)o, (uint8_t)(o >> 8), (uint8_t)(o >> 16), (uint8_t)(o >> 24) };
        if( UP3DCOMM_Write( cmd, 7 ) != 7 ) { free(b); return 6; }
        usleep( 20000 );                       /* UP Studio leaves ~20 ms per group */
      }
      if( _ms_now() - last_keep > 1000 ) { UP3D_SetParameter( 0xC1, 0 ); last_keep = _ms_now(); }
      if( progress ) progress( off + c > n ? n : off + c, n );
    }
    free(b);
    _up3d_job_header( "OpenBoxBridge" );
    { const uint8_t c1[2] = { 0x44, 0x04 }; _up3d_cmd44( c1, 2 ); }
    { const uint8_t c2[6] = { 0x44, 0x05, 0, 0, 0, 0 }; _up3d_cmd44( c2, 6 ); }
    if( !UP3D_ClearProgramBuf() ) return 8;
    if( !UP3D_UseSDProgramBuf( 9, false ) ) return 8;
    if( !UP3D_StartResumeProgram() ) return 9;
    return 0;
  }

  /* method 0 */
  UP3D_SetParameter( PARA_RED_BLUE_BLINK, 10 );
  UP3D_SetParameter( PARA_PRINT_STATUS, 0x13 );   /* write to SD */
  if( !UP3D_ClearProgramBuf() ) return 4;
  const uint8_t program_id = 5;
  if( !UP3D_UseSDProgramBuf( program_id, true ) ) return 5;
  for( uint32_t i = 0; i < n; i += 72 )
  {
    uint32_t c = (n - i) > 72 ? 72 : (n - i);
    if( !UP3D_WriteBlocks( blks + i, (uint8_t)c ) ) return 6;
    if( progress ) progress( i + c, n );
  }
  /* the printer counts the blocks it has stored (param C6): wait up to 60 s for all of them */
  uint32_t stored = 0, t0 = _ms_now(), lastmsg = t0;
  for( ;; )
  {
    stored = UP3D_GetParameter( 0xC6 );
    if( stored == n ) break;
    if( _ms_now() - t0 > 60000 ) break;
    if( _ms_now() - lastmsg > 5000 ) { printf( "Waiting for the printer to store the job: %u of %u blocks\n", stored, n ); fflush(stdout); lastmsg = _ms_now(); }
    usleep( 100000 );
  }
  if( stored != n )
  {
    printf( "ERROR: the printer confirmed %u of %u blocks stored (param C6)\n", stored, n );
    UP3D_SetParameter( PARA_RED_BLUE_BLINK, 0 );
    UP3D_SetParameter( PARA_PRINT_STATUS, 0x1 );
    return 7;
  }
  UP3D_SetParameter( PARA_RED_BLUE_BLINK, 0 );
  UP3D_SetParameter( PARA_PRINT_STATUS, 0x1 );    /* system ready */
  if( !UP3D_ClearProgramBuf() ) return 8;
  if( !UP3D_UseSDProgramBuf( program_id, false ) ) return 8;
  if( !UP3D_StartResumeProgram() ) return 9;
  return 0;
}

/* Streams a program into the printer's program buffer and runs it while it is still
   arriving (how UP Studio 3 sends moves to the UP300). The USB cable has to stay
   connected until everything is sent. Returns 0, 4 (clear), 6 (write/no answer) or 9 (start). */
int UP3D_StreamProgram( const UP3D_BLK *blks, uint32_t n, up3d_progress_fn progress )
{
  if( !UP3D_ClearProgramBuf() ) return 4;
  bool started = false;
  uint32_t i = 0, t_last = _ms_now(), t_beat = t_last;
  while( i < n )
  {
    int64_t fb = _up3d_free_blocks();
    if( fb < 0 )
    {
      if( _ms_now() - t_last > 5000 ) { printf("ERROR: the printer stopped answering\n"); return 6; }
      usleep( 100000 );
      continue;
    }
    if( fb == 0 )
    {
      if( !started ) { if( !UP3D_StartResumeProgram() ) return 9; started = true; }
      /* long enough for heating the bed from cold before the first layer */
      if( _ms_now() - t_last > 45 * 60 * 1000 ) { printf("ERROR: the printer took no new data for 45 minutes\n"); return 6; }
      if( _ms_now() - t_beat > 10000 ) { printf("Streaming: waiting for the printer (%u of %u blocks sent)\n", i, n); fflush(stdout); t_beat = _ms_now(); }
      usleep( 200000 );
      continue;
    }
    uint32_t c = (uint32_t)fb;
    if( c > 72 ) c = 72;
    if( c > n - i ) c = n - i;
    if( !UP3D_WriteBlocks( blks + i, (uint8_t)c ) ) return 6;
    i += c;
    t_last = _ms_now();
    if( progress ) progress( i, n );
  }
  if( !started && !UP3D_StartResumeProgram() ) return 9;
  return 0;
}
