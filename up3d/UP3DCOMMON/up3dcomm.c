//////////////////
//Author: M.Stohn/
//////////////////

#include "up3dcomm.h"

#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>

#include <libusb-1.0/libusb.h>

//#define _DEBUG_IN_OUT_

#define VID (0x4745)
#define PID_MINI_A (0x0001)
#define PID_MINI_M (0x2777)
#define PID_PLUS   (0x277d)
#define PID_CETUS_S7 (0x277f)
#define EP_OUT 1
#define EP_IN  1

static libusb_context* _libusb_ctx              = NULL;
static libusb_device_handle* _libusb_dev_handle = NULL;

#ifdef _DEBUG_IN_OUT_
static void _print_buffer( const uint8_t* data, const size_t datalen )
{
  unsigned int pos;
  for( pos=0; pos<datalen; pos++ )
    printf("%02X ", data[pos]);
  printf("\n");
}
#endif

bool UP3DCOMM_Open()
{
  int r;

  r = libusb_init( &_libusb_ctx );
  if( r < 0 ) 
  {
    printf( "[ERROR] USB Init: %d\n", r );
    return false;
  }

  libusb_set_debug( _libusb_ctx, 0 ); //set verbosity level to 0

  _libusb_dev_handle = libusb_open_device_with_vid_pid( _libusb_ctx, VID, PID_MINI_A );
  if( !_libusb_dev_handle )
    _libusb_dev_handle = libusb_open_device_with_vid_pid( _libusb_ctx, VID, PID_MINI_M );
  
  if( !_libusb_dev_handle )
    _libusb_dev_handle = libusb_open_device_with_vid_pid( _libusb_ctx, VID, PID_PLUS );

  if( !_libusb_dev_handle )
    _libusb_dev_handle = libusb_open_device_with_vid_pid( _libusb_ctx, VID, PID_CETUS_S7 );

  
  /* UP Box+ and later models may use a PID not listed above:
     fall back to the first device with the Tiertime vendor ID. */
  if( !_libusb_dev_handle )
  {
    libusb_device **list = NULL;
    ssize_t n = libusb_get_device_list( _libusb_ctx, &list );
    int last_err = 0, found = 0;
    for( ssize_t i = 0; i < n && !_libusb_dev_handle; i++ )
    {
      struct libusb_device_descriptor d;
      if( libusb_get_device_descriptor( list[i], &d ) != 0 || d.idVendor != VID )
        continue;
      found++;
      last_err = libusb_open( list[i], &_libusb_dev_handle );
      if( last_err != 0 )
      {
        fprintf(stderr, "[ERROR] Found UP printer %04X:%04X but could not open it: %s\n",
                d.idVendor, d.idProduct, libusb_strerror( last_err ));
        if( last_err == LIBUSB_ERROR_NOT_SUPPORTED || last_err == LIBUSB_ERROR_NOT_FOUND )
          fprintf(stderr, "[HINT] Windows has no compatible USB driver for it. See README: Driver setup.\n");
        if( last_err == LIBUSB_ERROR_ACCESS || last_err == LIBUSB_ERROR_BUSY )
          fprintf(stderr, "[HINT] Another program (UP Studio?) is using the printer. Close it and retry.\n");
        _libusb_dev_handle = NULL;
      }
    }
    if( list ) libusb_free_device_list( list, 1 );
    if( !_libusb_dev_handle )
    {
      if( !found )
        fprintf(stderr, "[ERROR] No UP printer found on USB (vendor %04X). Is it plugged in and switched on?\n", VID );
      UP3DCOMM_Close();
      return false;
    }
  }

  if( 1 == libusb_kernel_driver_active( _libusb_dev_handle, 0 ) )
    libusb_detach_kernel_driver( _libusb_dev_handle, 0 );

  r = libusb_claim_interface( _libusb_dev_handle, 0 );
  if( r < 0 ) 
  {
    fprintf(stderr,"[ERROR] USB Claim Interface: %s\n", libusb_strerror( r ));
    fprintf(stderr,"[HINT] Close UP Studio (and any other UP software) and retry.\n");
    UP3DCOMM_Close();
    return false;
  }

  //clean all outstanding printer responses (left from previous sessions)
  for( ;; )
  {
    int read;
    uint8_t buf[2048];
    if( (0 != libusb_bulk_transfer( _libusb_dev_handle, (EP_IN | LIBUSB_ENDPOINT_IN), buf, sizeof(buf), &read, 10)) || (!read) )
      break;
  }

  return true;
}

void UP3DCOMM_Close()
{
  if( _libusb_dev_handle )
    libusb_close( _libusb_dev_handle );

  _libusb_dev_handle = NULL;

  if( _libusb_ctx )
    libusb_exit( _libusb_ctx );
  _libusb_ctx = NULL;
}

int UP3DCOMM_ReadTO( const uint8_t *data, const size_t maxdatalen, const int timeout )
{
  int read;
  if( 0 != libusb_bulk_transfer( _libusb_dev_handle, (EP_IN | LIBUSB_ENDPOINT_IN), (uint8_t*)data, maxdatalen, &read, timeout) )
    return -1;

#ifdef _DEBUG_IN_OUT_
  fprintf(stderr,"<"); _print_buffer( data, read );
#endif

  return read;
}

int UP3DCOMM_Read( const uint8_t *data, const size_t maxdatalen )
{
  return UP3DCOMM_ReadTO( data, maxdatalen, 500 );
}

int UP3DCOMM_Write( const uint8_t *data, const size_t datalen )
{
#ifdef _DEBUG_IN_OUT_
  fprintf(stderr,">"); _print_buffer( data, datalen );
#endif

  int written;
  if( 0 != libusb_bulk_transfer( _libusb_dev_handle, (EP_OUT | LIBUSB_ENDPOINT_OUT), (uint8_t*)data, datalen, &written, 500) )
    return -1;

  return written;
}

