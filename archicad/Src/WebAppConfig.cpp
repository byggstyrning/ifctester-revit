#include "WebAppConfig.hpp"

#include <cstdlib>

#include "Name.hpp"

namespace IfcTester::Archicad {
namespace {
GS::UniString NormalizeFileUrl(const IO::Name& absolutePath)
{
    GS::UniString path = absolutePath.ToUniString();
    path.Replace('\', '/');
    if (!path.StartsWith("/")) {
        path.Insert(0, "/");
    }
    return GS::UniString("file://") + path;
}
}

GS::UniString EnsureUrlHasHostParam(const GS::UniString& url)
{
    if (url.IsEmpty()) {
        return url;
    }

    if (url.FindSubstring("host=archicad") >= 0) {
        return url;
    }

    GS::UniString withParam = url;
    if (withParam.FindLast('?') >= 0) {
        if (!withParam.EndsWith("&") && !withParam.EndsWith("?")) {
            withParam += "&";
        }
        withParam += "host=archicad";
        return withParam;
    }

    if (!withParam.EndsWith("/")) {
        withParam += "/";
    }
    withParam += "?host=archicad";
    return withParam;
}

WebAppConfig WebAppConfig::Create()
{
    WebAppConfig config;

    if (const char* envUrl = std::getenv("IFCTESTER_ARCHICAD_URL")) {
        if (*envUrl != '\0') {
            config.resolvedUrl = EnsureUrlHasHostParam(GS::UniString::Printf("%s", envUrl));
            config.usesDevServer = true;
            return config;
        }
    }

    IO::Location addOnLocation;
    if (ACAPI_GetOwnLocation(&addOnLocation) == NoError) {
        IO::Location rootFolder = addOnLocation;
        rootFolder.DeleteLastLocalName();
        rootFolder.AppendToLocal("WebApp");

        IO::Location indexLocation = rootFolder;
        indexLocation.AppendToLocal("index.html");

        IO::Name pathName;
        if (indexLocation.ToPath(&pathName) == NoError) {
            config.resolvedUrl = EnsureUrlHasHostParam(NormalizeFileUrl(pathName));
            IO::Name rootName;
            if (rootFolder.ToPath(&rootName) == NoError) {
                config.installRoot = rootName.ToUniString();
            }
            config.usesDevServer = false;
            return config;
        }
    }

#if defined (_DEBUG)
    config.resolvedUrl = EnsureUrlHasHostParam("http://localhost:5173/");
    config.usesDevServer = true;
#else
    config.resolvedUrl = EnsureUrlHasHostParam("https://ifctester.app/");
    config.usesDevServer = true;
#endif
    return config;
}

} // namespace IfcTester::Archicad
