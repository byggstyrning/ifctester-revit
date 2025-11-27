#pragma once

#include "APIEnvir.h"
#include "ACAPinc.h"

#include "FileSystem.hpp"
#include "Location.hpp"

namespace IfcTester::Archicad {

struct WebAppConfig {
    GS::UniString resolvedUrl;
    GS::UniString installRoot;
    bool usesDevServer = false;

    static WebAppConfig Create();
};

GS::UniString EnsureUrlHasHostParam(const GS::UniString& url);

} // namespace IfcTester::Archicad
