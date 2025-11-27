// *****************************************************************************
// General settings for IfcTester Archicad Add-On
// Based on ARCHICAD API DevKit boilerplate
// *****************************************************************************

#ifndef IFC_TESTER_APIENVIR_H
#define IFC_TESTER_APIENVIR_H

#if defined (_MSC_VER)
#ifndef WINDOWS
#define WINDOWS
#endif
#endif

#if defined (WINDOWS)
#include "Win32Interface.hpp"
#endif

#if defined (macintosh)
#include <CoreServices/CoreServices.h>
#endif

#ifndef ACExtension
#define ACExtension
#endif

#endif // IFC_TESTER_APIENVIR_H
