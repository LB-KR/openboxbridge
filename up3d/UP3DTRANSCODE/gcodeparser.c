/*
  gcodeparser.cpp for UP3DTranscoder
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

#include "gcodeparser.h"
#include "umcwriter.h"

#include <stdint.h>
#include <stdbool.h>
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <math.h>

static unsigned int gcp_line_number;
static unsigned int gcp_file_line_number;

static bool gcp_use_absoulte;
static bool gcp_use_extruder_absoulte;
static double gcp_X,gcp_Y,gcp_Z,gcp_E,gcp_F;

static double gcp_Z_max_used;
static double gcp_ofs_X = 0, gcp_ofs_Y = 0;   /* slicer origin -> machine origin */
static bool   gcp_rot180 = false;             /* turn the part 180 deg about the bed centre */
#define OUT_X(x) ((gcp_rot180 ? -(x) : (x)) + gcp_ofs_X)
#define OUT_Y(y) ((gcp_rot180 ? -(y) : (y)) + gcp_ofs_Y)

/* commands we skip instead of aborting (counted for a summary) */
#define GCP_MAX_IGNORED 32
static char         gcp_ignored_name[GCP_MAX_IGNORED][12];
static unsigned int gcp_ignored_count[GCP_MAX_IGNORED];
static unsigned int gcp_ignored_n = 0;

static void gcp_note_ignored(const char* line)
{
  char name[12]; int i = 0;
  while( line[i] && line[i]!=' ' && line[i]!='\r' && line[i]!='\n' && line[i]!=';' && i<11 ) { name[i]=line[i]; i++; }
  name[i]=0;
  for( unsigned int k=0; k<gcp_ignored_n; k++ )
    if( !strcmp(gcp_ignored_name[k],name) ) { gcp_ignored_count[k]++; return; }
  if( gcp_ignored_n < GCP_MAX_IGNORED )
  {
    strcpy( gcp_ignored_name[gcp_ignored_n], name );
    gcp_ignored_count[gcp_ignored_n++] = 1;
  }
}

void gcp_set_offset(double ox, double oy) { gcp_ofs_X = ox; gcp_ofs_Y = oy; }
void gcp_set_rot180(bool r) { gcp_rot180 = r; }

void gcp_print_ignored()
{
  if( !gcp_ignored_n ) return;
  printf("Skipped commands the UP does not use:");
  for( unsigned int k=0; k<gcp_ignored_n; k++ )
    printf(" %s(x%u)", gcp_ignored_name[k], gcp_ignored_count[k]);
  printf("\n");
}
static unsigned int gcp_layer;

#define gcp_error(e,s) { printf("GCP ERROR at line %d: %s\n>>%s\n",gcp_file_line_number,e,s); }

void gcp_reset()
{
  gcp_line_number = 0;
  gcp_file_line_number = 0;

  gcp_use_absoulte = true;
  gcp_use_extruder_absoulte = true;
  
  gcp_X = gcp_Y = gcp_Z = gcp_E = 0;
  gcp_F = 100;

  gcp_Z_max_used = 0;
  gcp_layer = 0;
  gcp_ignored_n = 0;
}

bool gcp_process_line(const char* gcodeline)
{
  if( !gcodeline )
    return false;

  char _line[1024];
  strncpy( _line, gcodeline, sizeof(_line)-1 );
  _line[sizeof(_line)-1] = 0;
  char *line = _line;
  while( ' '==*line || '\t'==*line ) line++;
  char *tok;

  gcp_file_line_number++;
  
  if( (0==*line) || ('\n'==*line) || ('\r'==*line) )
    return true; //empty line

  if( 'N'==*line )
  {
    double lineno = strtod( line+1, NULL);
    if( gcp_line_number+1 != lineno )
    {
      gcp_error("Invalid line number sequence",gcodeline);
      return false;
    }
    gcp_line_number = lineno;

    tok = strchr(line,' '); //jump over line number
    if( !tok )
      return true; //empty line
    line = tok+1;
  }

  if( (tok = strchr(line,'*')) )
  {
    double checksum = strtod( tok+1, NULL);
    uint8_t csx=0;
    for( tok--; tok!=line; tok-- )
      csx^=*tok;
    if( csx != checksum )
    {
      gcp_error("Invalid checksum",gcodeline);
      return false;
    }
    *tok=0; //cut line before checksum
  }

  if( (tok = strchr(line,';')) )
  {
    if( tok == line )
      return true; //complete line is a comment

    *tok=0; //cut line before first comment
  }

  bool  xp=false,yp=false,zp=false,ep=false,fp=false,tp=false,sp=false,pp=false,rp=false,ib=false,jb=false;
  double iv=0,jv=0;
  double xv=0,yv=0,zv=0,ev=0,fv=0,tv=0,sv=0,pv=0,rv=0;
  bool  xb=false,yb=false,zb=false,eb=false,fb=false,tb=false,sb=false,pb=false,rb=false;

  for( tok=line; tok; tok=strchr(tok,' ') ) //white spaces ???
  {
    if( !tok || !*(++tok) )
      break;

    char* ref=NULL;
    if('X'==*tok){xp=true;xv=strtod(tok+1,&ref);xb=(ref!=NULL);}
    if('Y'==*tok){yp=true;yv=strtod(tok+1,&ref);yb=(ref!=NULL);}
    if('Z'==*tok){zp=true;zv=strtod(tok+1,&ref);zb=(ref!=NULL);}
    if('E'==*tok){ep=true;ev=strtod(tok+1,&ref);eb=(ref!=NULL);}
    if('F'==*tok){fp=true;fv=strtod(tok+1,&ref);fb=(ref!=NULL);}
    if('T'==*tok){tp=true;tv=strtod(tok+1,&ref);tb=(ref!=NULL);}
    if('S'==*tok){sp=true;sv=strtod(tok+1,&ref);sb=(ref!=NULL);}
    if('P'==*tok){pp=true;pv=strtod(tok+1,&ref);pb=(ref!=NULL);}
    if('R'==*tok){rp=true;rv=strtod(tok+1,&ref);rb=(ref!=NULL);}
    if('I'==*tok){iv=strtod(tok+1,&ref);ib=(ref!=NULL);}
    if('J'==*tok){jv=strtod(tok+1,&ref);jb=(ref!=NULL);}
  }
  
  if( 'G'==*line )
  {
    char* ref = NULL;
    double code = strtod( line+1, &ref ); if(!ref) code=-1;
    switch( (int)code )
    {
      case 0:
      case 1: //move
       {
        if(fb) gcp_F=fv;
        double prevE = gcp_E, prevX = gcp_X, prevY = gcp_Y, prevZ = gcp_Z;
        if(eb) gcp_E=(gcp_use_extruder_absoulte)?ev:gcp_E+ev;
        if(xb) gcp_X=(gcp_use_absoulte)?xv:gcp_X+xv;
        if(yb) gcp_Y=(gcp_use_absoulte)?yv:gcp_Y+yv;
        if(zb) gcp_Z=(gcp_use_absoulte)?zv:gcp_Z+zv;
        /* Z moves go to the printer as a direct "jog" move, which skips this converter's
           acceleration control and speed cap. So only the Z change itself goes that way:
           - a line that names the Z it is already at (Orca writes z-hop travels as
             "G1 X.. Y.. Z.5") is an ordinary XY move;
           - a line that changes Z and XY together is split: going up, lift first and
             then travel; going down, travel first and then lower. */
        bool zchange = zb && fabs(gcp_Z - prevZ) > 0.00001;
        bool xychange = (fabs(gcp_X - prevX) > 0.00001) || (fabs(gcp_Y - prevY) > 0.00001);
        if(zchange && !xychange)
        {
          umcwriter_move_direct(OUT_X(gcp_X),OUT_Y(gcp_Y),gcp_Z,gcp_E,gcp_F);
        }
        else if(zchange)
        {
          if( gcp_Z > prevZ )
          {
            umcwriter_move_direct(OUT_X(prevX),OUT_Y(prevY),gcp_Z,prevE,gcp_F);
            umcwriter_planner_add(OUT_X(gcp_X),OUT_Y(gcp_Y),gcp_E,gcp_F);
          }
          else
          {
            umcwriter_planner_add(OUT_X(gcp_X),OUT_Y(gcp_Y),gcp_E,gcp_F);
            umcwriter_move_direct(OUT_X(gcp_X),OUT_Y(gcp_Y),gcp_Z,gcp_E,gcp_F);
          }
        }
        else
        {
          /* a new layer starts at the first extruding move at a new height
             (travel lifts and z-hops don't count) */
          if( gcp_E > prevE && (xb||yb) && gcp_Z > gcp_Z_max_used + 0.0001 )
          {
            gcp_Z_max_used = gcp_Z;
            gcp_layer++;
            umcwriter_set_report_data( gcp_layer, gcp_Z );
          }
          /* travel (no extrusion, not a wipe): like UP Studio, let the printer's own jog
             routine do it - it keeps count of its steps at speed. Printing moves go
             through the planner at UP Studio-like speeds (see umcwriter.c). */
          double dxy = sqrt((gcp_X-prevX)*(gcp_X-prevX)+(gcp_Y-prevY)*(gcp_Y-prevY));
          if( gcp_E == prevE && dxy >= 1.0 )
            umcwriter_move_direct(OUT_X(gcp_X),OUT_Y(gcp_Y),gcp_Z,gcp_E,gcp_F);
          else
            umcwriter_planner_add(OUT_X(gcp_X),OUT_Y(gcp_Y),gcp_E,gcp_F);
        }
       }
       break;

      case 2:
      case 3: //arc: split into short straight segments
       {
        if(fb) gcp_F=fv;
        double x0=gcp_X, y0=gcp_Y, z0=gcp_Z, e0=gcp_E;
        double x1 = xb ? (gcp_use_absoulte?xv:x0+xv) : x0;
        double y1 = yb ? (gcp_use_absoulte?yv:y0+yv) : y0;
        double z1 = zb ? (gcp_use_absoulte?zv:z0+zv) : z0;
        double e1 = eb ? (gcp_use_extruder_absoulte?ev:e0+ev) : e0;
        double cx, cy;
        bool cw = ((int)code == 2);
        if( ib || jb ) { cx = x0+iv; cy = y0+jv; }
        else if( rb )
        {
          double dx=x1-x0, dy=y1-y0, d=sqrt(dx*dx+dy*dy);
          if( d<1e-9 || fabs(rv)<d/2 ) { gcp_error("Invalid arc radius",gcodeline); return false; }
          double h = sqrt(rv*rv-(d/2)*(d/2));
          if( (cw && rv>0) || (!cw && rv<0) ) h = -h;
          cx = x0+dx/2 - h*dy/d; cy = y0+dy/2 + h*dx/d;
        }
        else { gcp_error("Arc without I/J or R",gcodeline); return false; }
        double r  = sqrt((x0-cx)*(x0-cx)+(y0-cy)*(y0-cy));
        double a0 = atan2(y0-cy,x0-cx), a1 = atan2(y1-cy,x1-cx);
        double sweep = a1-a0;
        if( cw  && sweep >= -1e-9 ) sweep -= 2*M_PI;
        if( !cw && sweep <=  1e-9 ) sweep += 2*M_PI;
        int segs = (int)ceil(fabs(sweep)*r/0.5); if(segs<1) segs=1; if(segs>2000) segs=2000;
        for( int k=1; k<=segs; k++ )
        {
          double t = (double)k/segs, a = a0+sweep*t;
          gcp_X = (k==segs)?x1:cx+r*cos(a);
          gcp_Y = (k==segs)?y1:cy+r*sin(a);
          gcp_E = e0+(e1-e0)*t;
          if( z1 != z0 ) { gcp_Z = z0+(z1-z0)*t; umcwriter_move_direct(OUT_X(gcp_X),OUT_Y(gcp_Y),gcp_Z,gcp_E,gcp_F); }
          else umcwriter_planner_add(OUT_X(gcp_X),OUT_Y(gcp_Y),gcp_E,gcp_F);
        }
        gcp_Z = z1;
       }
       break;

      case 4: //pause
        {
          uint32_t msec = 0;
          if(sb) msec=sv*1000;
          if(pb) msec=pv;
          if(msec)
            umcwriter_pause(msec);
        }
        break;

      case 21: //metric valus (default)
        break;

      case 28: //home
        {
          if(!xp && !yp && !zp) {xp=true;yp=true;zp=true;} //if no parameter given home all axis
          /* G28: the head goes to machine 0, i.e. where OUT_X/OUT_Y give 0 */
          if(xp){ gcp_X = gcp_rot180 ? gcp_ofs_X : -gcp_ofs_X; }
          if(yp){ gcp_Y = gcp_rot180 ? gcp_ofs_Y : -gcp_ofs_Y; }
          if(zp){ gcp_Z=0; }
          umcwriter_virtual_home(xp?100*60:0,yp?100*60:0,zp?50*60:0);
        }
        break;

      case 90: //set absolute positioning
        gcp_use_absoulte = true;
        gcp_use_extruder_absoulte = true;
        break;

      case 91: //set relative positioning
        gcp_use_absoulte = false;
        gcp_use_extruder_absoulte = false;
        break;

      case 92: //set position
        {
          if(xb) gcp_X=xv;
          if(yb) gcp_Y=yv;
          if(zb) gcp_Z=zv;
          if(eb) gcp_E=ev;
          if( xb || yb )
            umcwriter_planner_set_position(OUT_X(gcp_X),OUT_Y(gcp_Y),gcp_E);
          else if(eb)
            umcwriter_planner_set_a_position(gcp_E);
            
        }
        break;
 
      default:
        gcp_note_ignored(line);
        break;
    }
  }
  else
  if( 'M'==*line )
  {
    char* ref=NULL;
    double code = strtod( line+1, &ref ); if(!ref) code=-1;
    switch( (int)code )
    {
      case 82: //set extruder absolute mode - default
        gcp_use_extruder_absoulte = true;
        break;
      case 83: //set extruder relative mode
        gcp_use_extruder_absoulte = false;
        break;

      case 84: //disable motors
        break;

      case 104://set extruder target temp
        if( sb ) umcwriter_set_extruder_temp(sv,false);
        break;

      case 106: //fan
      case 107: //fan off
        //ignore
        break;

      case 117: //set message
        //ignore
        break;

      case 109: //set extrduder target temp and wait
        if( sb ) umcwriter_set_extruder_temp(sv,true);
        if( rb ) umcwriter_set_extruder_temp(rv,true);
        break;

      case 140: //set bed target temp
        if( sb ) umcwriter_set_bed_temp(sv,false);
        break;

      case 190: //set bed target temp and wait
        if( sb ) umcwriter_set_bed_temp(sv,true);
        if( rb ) umcwriter_set_bed_temp(rv,true);
        break;

      case 300: //play beep sound
        if( pb ) umcwriter_beep(pv);
        break;

      default:
        gcp_note_ignored(line);
        break;
    }
  }
  else
  if( 'T'==*line )
  {
    //double code = strtod( line+1, NULL);
    umcwriter_planner_sync();
    //ignore any T (tool change) commands
  }
  else
  {
    gcp_note_ignored(line);
  }

  return true;
}

int gcp_get_layer()
{
  return gcp_layer;
}

double gcp_get_height()
{
  return gcp_Z_max_used;
}
