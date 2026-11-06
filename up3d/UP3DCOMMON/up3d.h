//////////////////
//Author: M.Stohn/
//////////////////

#ifndef _UP3D_H_
#define _UP3D_H_

#include "up3ddata.h"

#include <stdint.h>
#include <stdbool.h>

bool     UP3D_Open();
void     UP3D_Close();

bool     UP3D_IsPrinterResponsive();

uint32_t UP3D_GetParameter(uint8_t param);
bool     UP3D_SetParameter(uint8_t param, uint32_t value);

bool     UP3D_GetSystemVar(uint8_t param, int32_t *var);
bool     UP3D_GetPrinterStatus(TT_tagPrinterStatus *status);
uint32_t UP3D_GetPrinterModelId(void);   /* 0x2783 = UP300, 0x2710 = UP Box+ */
typedef void (*up3d_progress_fn)(uint32_t sent, uint32_t total);
int      UP3D_SendProgramSD( const UP3D_BLK *blks, uint32_t n, int method, up3d_progress_fn progress );
int      UP3D_StreamProgram( const UP3D_BLK *blks, uint32_t n, up3d_progress_fn progress );   /* 20 = stopped on request (UP3D_STOPFILE) */
int      UP3D_StopProgram(void);
void     UP3D_PrintStatusLine(const char *prefix);   /* the up3dstatus line, with a prefix */
int      UP3D_PreheatBed(int target, int nozzle_standby, int timeout_min);   /* 0 ready, 20 stop requested, 22 timed out, 23 no answer */   /* 0 stopped, 10 not accepted, 11 paused only, 12 still running */
bool     UP3D_GetPrinterInfo(TT_tagPrinterInfoHeader *pihdr, TT_tagPrinterInfoName *piname, TT_tagPrinterInfoData *pidata, TT_tagPrinterInfoSet pisets[8]);

bool     UP3D_ClearProgramBuf();
bool     UP3D_UseSDProgramBuf( uint8_t progID, bool enableWrite );
bool     UP3D_SetPrintJobInfo( uint8_t progID, uint8_t x, uint32_t y );
bool     UP3D_InsertRomProgram( uint8_t prog );
uint32_t UP3D_GetFreeBlocks();
bool     UP3D_WriteBlocks( const UP3D_BLK *data, uint8_t blocks );
bool     UP3D_WriteBlock( const UP3D_BLK *data );
bool     UP3D_StartResumeProgram();

int32_t  UP3D_GetMachineState();
int32_t  UP3D_GetProgramState();
int32_t  UP3D_GetSystemState();
int32_t  UP3D_GetMotorState(int motor);
uint32_t UP3D_GetPositiveLimitState(int motor);
uint32_t UP3D_GetNegativeLimitState(int motor);
int32_t  UP3D_GetLayer();
float    UP3D_GetHeight();
int32_t  UP3D_GetPercent();
int32_t  UP3D_GetTimeRemaining();
int32_t  UP3D_GetPrintState();
int32_t  UP3D_GetHeaterTempReached(int heater);
int32_t  UP3D_GetHeaterState(int heater);
int32_t  UP3D_GetHeaterSetTemp(int heater);
float    UP3D_GetHeaterTemp(int heater);
int32_t  UP3D_GetHeaterTargetTemp(int heater);
int32_t  UP3D_GetHF(int heater);
int32_t  UP3D_GetSupportState();
int32_t  UP3D_GetFeedErrorLength();
float    UP3D_GetFeedBackLength();
int32_t  UP3D_GetAxisState(int axis);
float    UP3D_GetChangeNozzleTime();
float    UP3D_GetJumpTime();
int32_t  UP3D_GetUsedNozzle();
int32_t  UP3D_GetCheckDoor();
int32_t  UP3D_GetCheck24V();
int32_t  UP3D_GetCheckPowerKey();
int32_t  UP3D_GetCheckLightKey();
int32_t  UP3D_GetCheckWorkRoomFan();
int32_t  UP3D_GetReadFromSD();
int32_t  UP3D_GetWriteToSD();
int32_t  UP3D_GetAxisPosition(int axis);
bool     UP3D_SendRandom(); //not used from MAC, no change in printer status...

#endif //_UP3D_H_
