/**
 * IfcTester ArchiCAD Add-On
 * 
 * ArchiCAD API Server Header
 * HTTP server for communication between the web interface and ArchiCAD API.
 * This provides REST endpoints similar to the Revit implementation.
 */

#ifndef ARCHICAD_API_SERVER_HPP
#define ARCHICAD_API_SERVER_HPP

#include "IfcTesterArchiCAD.hpp"
#include <thread>
#include <atomic>
#include <mutex>

// Windows headers for HTTP server
#ifdef _WIN32
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")
#endif

namespace IfcTester {

/**
 * HTTP Response structure
 */
struct HttpResponse {
    int statusCode;
    GS::UniString contentType;
    GS::UniString body;
    std::vector<char> binaryBody;
    bool isBinary;
    
    HttpResponse() : statusCode(200), contentType("application/json"), isBinary(false) {}
};

/**
 * HTTP Request structure
 */
struct HttpRequest {
    GS::UniString method;
    GS::UniString path;
    GS::UniString body;
    std::map<GS::UniString, GS::UniString> headers;
};

/**
 * ArchiCADApiServer class
 * Implements an HTTP server for REST API communication between
 * the IfcTester web interface and ArchiCAD.
 * 
 * Endpoints:
 * - GET  /status              - Server status check
 * - GET  /select-by-guid/{id} - Select element by GUID
 * - GET  /ifc-configurations  - Get available IFC export configurations
 * - POST /export-ifc          - Export model to IFC format
 */
class ArchiCADApiServer {
public:
    /**
     * Constructor
     * @param port The port to listen on
     */
    explicit ArchiCADApiServer(int port = 48882);
    
    /**
     * Destructor
     */
    ~ArchiCADApiServer();
    
    /**
     * Start the server
     * @return true if started successfully
     */
    bool Start();
    
    /**
     * Stop the server
     */
    void Stop();
    
    /**
     * Check if server is running
     */
    bool IsRunning() const;
    
    /**
     * Get the server port
     */
    int GetPort() const { return port; }

private:
    /**
     * Main server loop (runs in separate thread)
     */
    void ServerLoop();
    
    /**
     * Handle incoming HTTP request
     */
    HttpResponse HandleRequest(const HttpRequest& request);
    
    /**
     * Handle status endpoint
     */
    HttpResponse HandleStatus();
    
    /**
     * Handle select-by-guid endpoint
     */
    HttpResponse HandleSelectByGuid(const GS::UniString& guid);
    
    /**
     * Handle ifc-configurations endpoint
     */
    HttpResponse HandleGetIfcConfigurations();
    
    /**
     * Handle export-ifc endpoint
     */
    HttpResponse HandleExportIfc(const GS::UniString& requestBody);
    
    /**
     * Parse HTTP request from raw data
     */
    HttpRequest ParseRequest(const std::string& rawRequest);
    
    /**
     * Format HTTP response for sending
     */
    std::string FormatResponse(const HttpResponse& response);
    
    /**
     * Add CORS headers to response
     */
    void AddCorsHeaders(HttpResponse& response);
    
    /**
     * URL decode string
     */
    static GS::UniString UrlDecode(const GS::UniString& encoded);
    
    /**
     * Create JSON error response
     */
    static HttpResponse CreateErrorResponse(int statusCode, const GS::UniString& message);
    
    /**
     * Create JSON success response
     */
    static HttpResponse CreateJsonResponse(const GS::UniString& json);

    // Server configuration
    int port;
    
    // Server state
    std::atomic<bool> running;
    std::thread serverThread;
    std::mutex requestMutex;
    
    // Socket handle
#ifdef _WIN32
    SOCKET serverSocket;
#else
    int serverSocket;
#endif
    
    // Cached configuration state
    bool configsLoaded;
    GS::Array<IFCConfiguration> cachedConfigs;
};

} // namespace IfcTester

#endif // ARCHICAD_API_SERVER_HPP
