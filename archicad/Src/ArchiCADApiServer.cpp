/**
 * IfcTester ArchiCAD Add-On
 * 
 * ArchiCAD API Server Implementation
 * HTTP server for communication between the web interface and ArchiCAD API.
 */

#include "ArchiCADApiServer.hpp"
#include "ACAPinc.h"  // For ACAPI_WriteReport
#include <sstream>
#include <fstream>
#include <ctime>
#include <algorithm>
#include <cstdio>
#include <chrono>
#include <cctype>
#include <cstring>  // for memset

#ifndef _WIN32
// macOS: Grand Central Dispatch for main thread callbacks
#include <dispatch/dispatch.h>
#endif

namespace IfcTester {

// Global instance pointer for message window callback
static ArchiCADApiServer* gApiServerInstance = nullptr;

ArchiCADApiServer* GetApiServerInstance()
{
    return gApiServerInstance;
}

// ============================================================================
// Constructor / Destructor
// ============================================================================

ArchiCADApiServer::ArchiCADApiServer(int port)
    : port(port)
    , running(false)
    , serverSocket(INVALID_SOCKET)
    , configsLoaded(false)
    , messageWindow(nullptr)
{
    gApiServerInstance = this;
#ifdef _WIN32
    // Initialize Winsock (only if not already initialized)
    static bool winsockInitialized = false;
    if (!winsockInitialized) {
        WSADATA wsaData;
        int result = WSAStartup(MAKEWORD(2, 2), &wsaData);
        if (result != 0) {
            ACAPI_WriteReport("IfcTester API Server: WSAStartup failed with error %d", false, result);
        } else {
            winsockInitialized = true;
            ACAPI_WriteReport("IfcTester API Server: Winsock initialized", false);
        }
    }
#else
    // POSIX sockets don't need initialization
    ACAPI_WriteReport("IfcTester API Server: POSIX sockets ready", false);
#endif
}

ArchiCADApiServer::~ArchiCADApiServer()
{
    Stop();
    
    // Clear any pending requests in the queue
    {
        std::lock_guard<std::mutex> lock(queueMutex);
        while (!selectionQueue.empty()) {
            SelectionRequest* req = selectionQueue.front();
            selectionQueue.pop();
            if (req->cv && req->mtx) {
                std::lock_guard<std::mutex> reqLock(*req->mtx);
                req->processed = true;
                req->success = false;
                req->cv->notify_one();
            }
        }
    }
    
    gApiServerInstance = nullptr;
    
#ifdef _WIN32
    WSACleanup();
#endif
}

// ============================================================================
// Server Control
// ============================================================================

bool ArchiCADApiServer::Start()
{
    if (running) {
        return true;
    }
    
    // Create socket
    serverSocket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (serverSocket == INVALID_SOCKET) {
#ifdef _WIN32
        int error = WSAGetLastError();
        ACAPI_WriteReport("IfcTester API Server: Failed to create socket (error %d)", false, error);
#else
        ACAPI_WriteReport("IfcTester API Server: Failed to create socket (error %d)", false, errno);
#endif
        return false;
    }
    
    // Set socket options
    int opt = 1;
    setsockopt(serverSocket, SOL_SOCKET, SO_REUSEADDR, (const char*)&opt, sizeof(opt));
    
    // Bind to port
    sockaddr_in serverAddr;
    memset(&serverAddr, 0, sizeof(serverAddr));
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_addr.s_addr = inet_addr("127.0.0.1");
    serverAddr.sin_port = htons(port);
    
    if (bind(serverSocket, (sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR) {
#ifdef _WIN32
        int error = WSAGetLastError();
        ACAPI_WriteReport("IfcTester API Server: Failed to bind to port %d (error %d). Port may be in use.", false, port, error);
#else
        ACAPI_WriteReport("IfcTester API Server: Failed to bind to port %d (error %d). Port may be in use.", false, port, errno);
#endif
        closesocket(serverSocket);
        serverSocket = INVALID_SOCKET;
        return false;
    }
    
    // Listen
    if (listen(serverSocket, SOMAXCONN) == SOCKET_ERROR) {
#ifdef _WIN32
        int error = WSAGetLastError();
        ACAPI_WriteReport("IfcTester API Server: Failed to listen on port %d (error %d)", false, port, error);
#else
        ACAPI_WriteReport("IfcTester API Server: Failed to listen on port %d (error %d)", false, port, errno);
#endif
        closesocket(serverSocket);
        serverSocket = INVALID_SOCKET;
        return false;
    }
    
    // Set non-blocking mode
#ifdef _WIN32
    u_long mode = 1;
    if (ioctlsocket(serverSocket, FIONBIO, &mode) == SOCKET_ERROR) {
        int error = WSAGetLastError();
        ACAPI_WriteReport("IfcTester API Server: Failed to set non-blocking mode (error %d)", false, error);
        closesocket(serverSocket);
        serverSocket = INVALID_SOCKET;
        return false;
    }
#else
    // macOS/POSIX: Use fcntl for non-blocking mode
    int flags = fcntl(serverSocket, F_GETFL, 0);
    if (flags == -1 || fcntl(serverSocket, F_SETFL, flags | O_NONBLOCK) == -1) {
        ACAPI_WriteReport("IfcTester API Server: Failed to set non-blocking mode (error %d)", false, errno);
        closesocket(serverSocket);
        serverSocket = INVALID_SOCKET;
        return false;
    }
#endif
    
    running = true;
    
    // Start server thread
    try {
        serverThread = std::thread(&ArchiCADApiServer::ServerLoop, this);
        ACAPI_WriteReport("IfcTester API Server: Successfully started on http://127.0.0.1:%d", false, port);
    } catch (const std::exception& e) {
        ACAPI_WriteReport("IfcTester API Server: Failed to start thread: %s", false, e.what());
        running = false;
        closesocket(serverSocket);
        serverSocket = INVALID_SOCKET;
        return false;
    }
    
    return true;
}

void ArchiCADApiServer::Stop()
{
    if (!running) {
        return;
    }
    
    running = false;
    
    if (serverSocket != INVALID_SOCKET) {
        closesocket(serverSocket);
        serverSocket = INVALID_SOCKET;
    }
    
    if (serverThread.joinable()) {
        serverThread.join();
    }
}

bool ArchiCADApiServer::IsRunning() const
{
    return running;
}

// ============================================================================
// Server Loop
// ============================================================================

void ArchiCADApiServer::ServerLoop()
{
    ACAPI_WriteReport("IfcTester API Server: ServerLoop thread started", false);
    
    try {
        while (running) {
            // Check if server socket is still valid
            if (serverSocket == INVALID_SOCKET) {
                ACAPI_WriteReport("IfcTester API Server: Server socket is invalid, stopping loop", false);
                break;
            }
            
            fd_set readSet;
            FD_ZERO(&readSet);
            FD_SET(serverSocket, &readSet);
            
            timeval timeout;
            timeout.tv_sec = 0;
            timeout.tv_usec = 100000; // 100ms
            
            int selectResult = select((int)serverSocket + 1, &readSet, nullptr, nullptr, &timeout);
            
            if (selectResult < 0) {
#ifdef _WIN32
                int error = WSAGetLastError();
                if (error != WSAEINTR) {
                    ACAPI_WriteReport("IfcTester API Server: select() error %d", false, error);
                    break;
                }
#else
                if (errno != EINTR) {
                    ACAPI_WriteReport("IfcTester API Server: select() error %d", false, errno);
                    break;
                }
#endif
                continue;
            }
            
            if (selectResult > 0 && FD_ISSET(serverSocket, &readSet)) {
                sockaddr_in clientAddr;
                socklen_t clientAddrLen = sizeof(clientAddr);
                
                SOCKET clientSocket = accept(serverSocket, (sockaddr*)&clientAddr, &clientAddrLen);
                
                if (clientSocket != INVALID_SOCKET) {
                    // Set client socket to blocking mode (for reading)
                    // The server socket is non-blocking for accept(), but client sockets should be blocking for recv()
#ifdef _WIN32
                    u_long mode = 0; // 0 = blocking mode
                    ioctlsocket(clientSocket, FIONBIO, &mode);
                    
                    // Set socket timeout (Windows uses DWORD in milliseconds)
                    DWORD timeout = 5000;
                    setsockopt(clientSocket, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));
                    setsockopt(clientSocket, SOL_SOCKET, SO_SNDTIMEO, (const char*)&timeout, sizeof(timeout));
#else
                    // macOS/POSIX: Ensure blocking mode
                    int flags = fcntl(clientSocket, F_GETFL, 0);
                    if (flags != -1) {
                        fcntl(clientSocket, F_SETFL, flags & ~O_NONBLOCK);
                    }
                    
                    // Set socket timeout (POSIX uses struct timeval)
                    struct timeval tv;
                    tv.tv_sec = 5;
                    tv.tv_usec = 0;
                    setsockopt(clientSocket, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
                    setsockopt(clientSocket, SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof(tv));
#endif
                    
                    try {
                        // Read request
                        char buffer[8192];
                        int bytesRead = recv(clientSocket, buffer, sizeof(buffer) - 1, 0);
                    
                    if (bytesRead > 0) {
                        buffer[bytesRead] = '\0';
                        std::string rawRequest(buffer);
                        
                        // Parse and handle request
                        HttpRequest request = ParseRequest(rawRequest);
                        HttpResponse response;
                        
                        // Handle OPTIONS preflight
                        if (request.method == "OPTIONS") {
                            response.statusCode = 200;
                            response.contentType = "text/plain";
                            response.body = "";
                        } else {
                            std::lock_guard<std::mutex> lock(requestMutex);
                            response = HandleRequest(request);
                        }
                        
                        // Add CORS headers
                        AddCorsHeaders(response);
                        
                        // Send response
                        std::string responseStr = FormatResponse(response);
                        int bytesSent = send(clientSocket, responseStr.c_str(), (int)responseStr.length(), 0);
                        
                        if (bytesSent == SOCKET_ERROR) {
#ifdef _WIN32
                            int error = WSAGetLastError();
                            ACAPI_WriteReport("IfcTester API Server: Failed to send response (error %d)", false, error);
#else
                            ACAPI_WriteReport("IfcTester API Server: Failed to send response (error %d)", false, errno);
#endif
                        }
                    } else if (bytesRead == 0) {
                        // Client closed connection
                        // This is normal, don't log it
                    } else {
                        // Error reading (bytesRead < 0)
#ifdef _WIN32
                        int error = WSAGetLastError();
                        // WSAEWOULDBLOCK (10035) shouldn't happen with blocking sockets, but ignore it just in case
                        // WSAETIMEDOUT is expected when timeout occurs
                        if (error != WSAETIMEDOUT && error != WSAEWOULDBLOCK) {
                            ACAPI_WriteReport("IfcTester API Server: Error reading request (error %d)", false, error);
                        }
#else
                        // EAGAIN/EWOULDBLOCK shouldn't happen with blocking sockets
                        // ETIMEDOUT is expected when timeout occurs
                        if (errno != ETIMEDOUT && errno != EAGAIN && errno != EWOULDBLOCK) {
                            ACAPI_WriteReport("IfcTester API Server: Error reading request (error %d)", false, errno);
                        }
#endif
                    }
                } catch (...) {
                    ACAPI_WriteReport("IfcTester API Server: Exception handling request", false);
                }
                
                // Close connection
                closesocket(clientSocket);
            }
            }
        }
    } catch (const std::exception& e) {
        ACAPI_WriteReport("IfcTester API Server: Exception in ServerLoop: %s", false, e.what());
        running = false;
    } catch (...) {
        ACAPI_WriteReport("IfcTester API Server: Unknown exception in ServerLoop", false);
        running = false;
    }
    
    ACAPI_WriteReport("IfcTester API Server: ServerLoop thread exiting", false);
}

// ============================================================================
// Request Handling
// ============================================================================

HttpRequest ArchiCADApiServer::ParseRequest(const std::string& rawRequest)
{
    HttpRequest request;
    std::istringstream stream(rawRequest);
    std::string line;
    
    // Parse request line
    if (std::getline(stream, line)) {
        std::istringstream lineStream(line);
        std::string method, path, version;
        lineStream >> method >> path >> version;
        request.method = GS::UniString(method.c_str());
        request.path = GS::UniString(path.c_str());
    }
    
    // Parse headers
    while (std::getline(stream, line) && line != "\r" && !line.empty()) {
        size_t colonPos = line.find(':');
        if (colonPos != std::string::npos) {
            std::string key = line.substr(0, colonPos);
            std::string value = line.substr(colonPos + 1);
            // Trim whitespace
            value.erase(0, value.find_first_not_of(" \t"));
            value.erase(value.find_last_not_of(" \t\r\n") + 1);
            request.headers[GS::UniString(key.c_str())] = GS::UniString(value.c_str());
        }
    }
    
    // Parse body (rest of the request)
    std::string body;
    while (std::getline(stream, line)) {
        body += line + "\n";
    }
    if (!body.empty() && body.back() == '\n') {
        body.pop_back();
    }
    request.body = GS::UniString(body.c_str());
    
    return request;
}

std::string ArchiCADApiServer::FormatResponse(const HttpResponse& response)
{
    std::ostringstream stream;
    
    // Status line
    stream << "HTTP/1.1 " << response.statusCode << " ";
    switch (response.statusCode) {
        case 200: stream << "OK"; break;
        case 400: stream << "Bad Request"; break;
        case 404: stream << "Not Found"; break;
        case 500: stream << "Internal Server Error"; break;
        default: stream << "Unknown"; break;
    }
    stream << "\r\n";
    
    // Headers
    stream << "Content-Type: " << response.contentType.ToCStr().Get() << "\r\n";
    stream << "Access-Control-Allow-Origin: *\r\n";
    stream << "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n";
    stream << "Access-Control-Allow-Headers: Content-Type\r\n";
    
    // Calculate correct body length in bytes
    std::string bodyStr;
    if (!response.isBinary) {
        bodyStr = response.body.ToCStr().Get();
    }
    
    if (response.isBinary) {
        stream << "Content-Length: " << response.binaryBody.size() << "\r\n";
    } else {
        stream << "Content-Length: " << bodyStr.length() << "\r\n";
    }
    stream << "\r\n";
    
    // Body
    if (response.isBinary) {
        stream.write(response.binaryBody.data(), response.binaryBody.size());
    } else {
        stream << bodyStr;
    }
    
    return stream.str();
}

void ArchiCADApiServer::AddCorsHeaders(HttpResponse& /*response*/)
{
    // CORS headers are added in FormatResponse
}

HttpResponse ArchiCADApiServer::HandleRequest(const HttpRequest& request)
{
    GS::UniString path = request.path;
    
    // Remove query string for path matching
    Int32 queryPos = path.FindFirst('?');
    if (queryPos >= 0) {
        path = path.GetSubstring(0, queryPos);
    }
    
    // Route API requests
    if (path == "/api/status" && request.method == "GET") {
        return HandleStatus();
    }
    else if (path.BeginsWith("/api/select-by-guid/") && request.method == "GET") {
        GS::UniString guid = path.GetSubstring(20, path.GetLength() - 20);
        guid = UrlDecode(guid);
        return HandleSelectByGuid(guid);
    }
    else if (path == "/api/ifc-configurations" && request.method == "GET") {
        return HandleGetIfcConfigurations();
    }
    else if (path == "/api/export-ifc" && request.method == "POST") {
        return HandleExportIfc(request.body);
    }
    else if (path.BeginsWith("/api/export-status/") && request.method == "GET") {
        GS::UniString jobIdStr = path.GetSubstring(19, path.GetLength() - 19);
        std::string jobId = jobIdStr.ToCStr().Get();
        return HandleExportStatus(jobId);
    }
    else if (path.BeginsWith("/api/export-file/") && request.method == "GET") {
        GS::UniString jobIdStr = path.GetSubstring(17, path.GetLength() - 17);
        std::string jobId = jobIdStr.ToCStr().Get();
        return HandleExportFile(jobId);
    }
    // Legacy API routes (without /api prefix) for backwards compatibility
    else if (path == "/status" && request.method == "GET") {
        return HandleStatus();
    }
    else if (path.BeginsWith("/select-by-guid/") && request.method == "GET") {
        GS::UniString guid = path.GetSubstring(16, path.GetLength() - 16);
        guid = UrlDecode(guid);
        return HandleSelectByGuid(guid);
    }
    else if (path == "/ifc-configurations" && request.method == "GET") {
        return HandleGetIfcConfigurations();
    }
    else if (path == "/export-ifc" && request.method == "POST") {
        return HandleExportIfc(request.body);
    }
    else if (path.BeginsWith("/export-status/") && request.method == "GET") {
        GS::UniString jobIdStr = path.GetSubstring(15, path.GetLength() - 15);
        std::string jobId = jobIdStr.ToCStr().Get();
        return HandleExportStatus(jobId);
    }
    else if (path.BeginsWith("/export-file/") && request.method == "GET") {
        GS::UniString jobIdStr = path.GetSubstring(13, path.GetLength() - 13);
        std::string jobId = jobIdStr.ToCStr().Get();
        return HandleExportFile(jobId);
    }
    // Static file serving for the webapp
    else if (request.method == "GET") {
        return HandleStaticFile(path);
    }
    
    return CreateErrorResponse(404, GS::UniString("Not Found"));
}

// ============================================================================
// Endpoint Handlers
// ============================================================================

HttpResponse ArchiCADApiServer::HandleStatus()
{
    // Load configurations if not already done
    if (!configsLoaded) {
        cachedConfigs = GetIFCExportConfigurations();
        configsLoaded = cachedConfigs.GetSize() > 0;
    }
    
    std::ostringstream json;
    json << "{";
    json << "\"status\":\"" << (configsLoaded ? "ok" : "initializing") << "\",";
    json << "\"connected\":true,";
    json << "\"configsReady\":" << (configsLoaded ? "true" : "false") << ",";
    json << "\"version\":\"1.1.0\"";
    json << "}";
    
    return CreateJsonResponse(GS::UniString(json.str().c_str()));
}

HttpResponse ArchiCADApiServer::HandleSelectByGuid(const GS::UniString& guid)
{
    // This is called from the HTTP server background thread.
    // ArchiCAD API calls must be made from the main thread.
    // We use a message queue to post the request to the main thread and wait for the result.
    
    bool success = QueueSelectionRequest(guid);
    
    std::ostringstream json;
    json << "{";
    json << "\"success\":" << (success ? "true" : "false") << ",";
    json << "\"message\":\"" << (success ? "Element selected" : "Element not found or selection failed") << "\"";
    json << "}";
    
    return CreateJsonResponse(GS::UniString(json.str().c_str()));
}

// ============================================================================
// Thread-Safe Queue for Main Thread Processing
// ============================================================================

bool ArchiCADApiServer::QueueSelectionRequest(const GS::UniString& guid)
{
#ifdef _WIN32
    // Check if message window is set up (Windows only)
    if (messageWindow == nullptr) {
        ACAPI_WriteReport("IfcTester API Server: Message window not initialized, cannot queue selection", false);
        return false;
    }
#else
    // macOS: GCD is always available, no setup needed
    ACAPI_WriteReport("IfcTester API Server: Queuing selection request for macOS (GCD)", false);
#endif
    
    // Create request with synchronization primitives
    std::mutex reqMutex;
    std::condition_variable reqCV;
    
    SelectionRequest request;
    request.guid = guid;
    request.processed = false;
    request.success = false;
    request.cv = &reqCV;
    request.mtx = &reqMutex;
    
    // Add to queue
    {
        std::lock_guard<std::mutex> lock(queueMutex);
        selectionQueue.push(&request);
    }
    
    // Post message to main thread to process the queue
#ifdef _WIN32
    PostMessage(messageWindow, WM_IFCTESTER_PROCESS_QUEUE, 0, 0);
#else
    // macOS: Use Grand Central Dispatch to process on main thread
    // We need to capture the server instance pointer for the dispatch block
    ArchiCADApiServer* server = this;
    dispatch_async(dispatch_get_main_queue(), ^{
        if (server != nullptr) {
            ACAPI_WriteReport("IfcTester: Processing selection queue via GCD on main thread", false);
            server->ProcessSelectionQueue();
        }
    });
#endif
    
    // Wait for the request to be processed (with timeout)
    {
        std::unique_lock<std::mutex> lock(reqMutex);
        bool waitResult = reqCV.wait_for(lock, std::chrono::seconds(10), [&request] {
            return request.processed;
        });
        
        if (!waitResult) {
            ACAPI_WriteReport("IfcTester API Server: Selection request timed out", false);
            return false;
        }
    }
    
    return request.success;
}

void ArchiCADApiServer::ProcessSelectionQueue()
{
    // This is called from the main thread (message window callback)
    // Process all pending requests
    
    while (true) {
        SelectionRequest* request = nullptr;
        
        // Get next request from queue
        {
            std::lock_guard<std::mutex> lock(queueMutex);
            if (selectionQueue.empty()) {
                break;
            }
            request = selectionQueue.front();
            selectionQueue.pop();
        }
        
        if (request == nullptr) {
            break;
        }
        
        // Process the selection on the main thread
        bool success = false;
        try {
            ACAPI_WriteReport("IfcTester API Server: Processing selection request for GUID: %s", false, 
                request->guid.ToCStr().Get());
            success = SelectElementByGUID(request->guid);
        } catch (...) {
            ACAPI_WriteReport("IfcTester API Server: Exception while processing selection", false);
            success = false;
        }
        
        // Signal completion
        if (request->cv && request->mtx) {
            std::lock_guard<std::mutex> reqLock(*request->mtx);
            request->success = success;
            request->processed = true;
            request->cv->notify_one();
        }
    }
}

void ArchiCADApiServer::ProcessExportQueue()
{
    while (true) {
        ExportRequest request;
        
        {
            std::lock_guard<std::mutex> lock(exportQueueMutex);
            if (exportQueue.empty()) {
                break;
            }
            request = exportQueue.front();
            exportQueue.pop();
        }
        
        if (request.jobId.empty()) {
            break;
        }
        
        // Process the export on the main thread
        bool success = false;
        GS::UniString outputPath;
        GS::UniString errorMessage;
        
        try {
            ACAPI_WriteReport("IfcTester API Server: Processing export job %s on MAIN THREAD for config: %s", false, 
                request.jobId.c_str(), request.configName.ToCStr().Get());
            success = ExportToIFC(request.configName, outputPath, &errorMessage);
            ACAPI_WriteReport("IfcTester API Server: Export job %s completed on main thread, success=%d", false, 
                request.jobId.c_str(), success ? 1 : 0);
        } catch (const std::exception& e) {
            ACAPI_WriteReport("IfcTester API Server: Exception while processing export job %s: %s", false, 
                request.jobId.c_str(), e.what());
            errorMessage = GS::UniString::Printf("Exception: %s", e.what());
            success = false;
        } catch (...) {
            ACAPI_WriteReport("IfcTester API Server: Unknown exception while processing export job %s", false, 
                request.jobId.c_str());
            errorMessage = "Unknown exception occurred";
            success = false;
        }
        
        // Update job status in the jobs map
        {
            std::lock_guard<std::mutex> lock(exportJobsMutex);
            auto it = exportJobs.find(request.jobId);
            if (it != exportJobs.end()) {
                if (success) {
                    it->second.status = ExportJobStatus::Complete;
                    it->second.outputPath = outputPath;
                } else {
                    it->second.status = ExportJobStatus::Failed;
                    it->second.errorMessage = errorMessage;
                }
            }
        }
    }
}

std::string ArchiCADApiServer::GenerateJobId()
{
    // Generate a unique job ID using timestamp + random suffix
    auto now = std::chrono::system_clock::now();
    auto timestamp = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch()).count();
    
    // Add a random component
    static int counter = 0;
    counter++;
    
    std::ostringstream oss;
    oss << "job_" << timestamp << "_" << counter;
    return oss.str();
}

std::string ArchiCADApiServer::QueueExportRequest(const GS::UniString& configName)
{
#ifdef _WIN32
    if (messageWindow == nullptr) {
        ACAPI_WriteReport("IfcTester API Server: Message window not initialized, cannot queue export", false);
        return "";
    }
#else
    // macOS: GCD is always available, no setup needed
    ACAPI_WriteReport("IfcTester API Server: Queuing export request for macOS (GCD)", false);
#endif
    
    // Generate unique job ID
    std::string jobId = GenerateJobId();
    
    // Create job entry in the tracking map
    {
        std::lock_guard<std::mutex> lock(exportJobsMutex);
        exportJobs[jobId] = ExportJob(jobId, configName);
    }
    
    // Create request and add to queue
    ExportRequest request(jobId, configName);
    {
        std::lock_guard<std::mutex> lock(exportQueueMutex);
        exportQueue.push(request);
    }
    
    // Post message to main thread to process queue (non-blocking)
#ifdef _WIN32
    PostMessage(messageWindow, WM_IFCTESTER_PROCESS_EXPORT, 0, 0);
#else
    // macOS: Use Grand Central Dispatch to process on main thread
    ArchiCADApiServer* server = this;
    dispatch_async(dispatch_get_main_queue(), ^{
        if (server != nullptr) {
            ACAPI_WriteReport("IfcTester: Processing export queue via GCD on main thread", false);
            server->ProcessExportQueue();
        }
    });
#endif
    
    ACAPI_WriteReport("IfcTester API Server: Queued export job %s for config: %s", false, 
        jobId.c_str(), configName.ToCStr().Get());
    
    return jobId;
}

HttpResponse ArchiCADApiServer::HandleGetIfcConfigurations()
{
    GS::Array<IFCConfiguration> configs = GetIFCExportConfigurations();
    
    std::ostringstream json;
    json << "{\"configurations\":[";
    
    bool first = true;
    for (const IFCConfiguration& config : configs) {
        if (!first) json << ",";
        first = false;
        
        json << "{";
        json << "\"name\":\"" << EscapeJsonString(config.name).ToCStr().Get() << "\",";
        json << "\"description\":\"" << EscapeJsonString(config.description).ToCStr().Get() << "\",";
        json << "\"version\":\"" << EscapeJsonString(config.version).ToCStr().Get() << "\"";
        json << "}";
    }
    
    json << "]}";
    
    // Cache configs
    cachedConfigs = configs;
    configsLoaded = true;
    
    return CreateJsonResponse(GS::UniString(json.str().c_str()));
}

HttpResponse ArchiCADApiServer::HandleExportIfc(const GS::UniString& requestBody)
{
    // Parse configuration name from request body
    // Simple JSON parsing for {"configuration":"name"}
    GS::UniString configName;
    
    Int32 configStart = requestBody.FindFirst("\"configuration\"");
    if (configStart >= 0) {
        Int32 valueStart = requestBody.FindFirst(':', configStart);
        if (valueStart >= 0) {
            Int32 quoteStart = requestBody.FindFirst('"', valueStart);
            if (quoteStart >= 0) {
                Int32 quoteEnd = requestBody.FindFirst('"', quoteStart + 1);
                if (quoteEnd > quoteStart) {
                    configName = requestBody.GetSubstring(quoteStart + 1, quoteEnd - quoteStart - 1);
                }
            }
        }
    }
    
    if (configName.IsEmpty()) {
        return CreateErrorResponse(400, GS::UniString("Missing configuration parameter"));
    }
    
    // Queue export and return immediately with job ID (async pattern)
    std::string jobId = QueueExportRequest(configName);
    
    if (jobId.empty()) {
        return CreateErrorResponse(500, GS::UniString("Failed to queue export request"));
    }
    
    // Return job ID immediately - client will poll for status
    std::ostringstream json;
    json << "{\"jobId\":\"" << jobId << "\",\"status\":\"running\"}";
    
    return CreateJsonResponse(GS::UniString(json.str().c_str()));
}

HttpResponse ArchiCADApiServer::HandleExportStatus(const std::string& jobId)
{
    std::lock_guard<std::mutex> lock(exportJobsMutex);
    
    auto it = exportJobs.find(jobId);
    if (it == exportJobs.end()) {
        return CreateErrorResponse(404, GS::UniString("Job not found"));
    }
    
    const ExportJob& job = it->second;
    
    std::ostringstream json;
    json << "{\"jobId\":\"" << jobId << "\",";
    
    switch (job.status) {
        case ExportJobStatus::Running:
            json << "\"status\":\"running\"";
            break;
        case ExportJobStatus::Complete:
            json << "\"status\":\"complete\"";
            break;
        case ExportJobStatus::Failed:
            json << "\"status\":\"failed\",";
            json << "\"error\":\"" << EscapeJsonString(job.errorMessage).ToCStr().Get() << "\"";
            break;
    }
    
    json << "}";
    
    return CreateJsonResponse(GS::UniString(json.str().c_str()));
}

HttpResponse ArchiCADApiServer::HandleExportFile(const std::string& jobId)
{
    ExportJob job;
    
    // Find and validate job
    {
        std::lock_guard<std::mutex> lock(exportJobsMutex);
        
        auto it = exportJobs.find(jobId);
        if (it == exportJobs.end()) {
            return CreateErrorResponse(404, GS::UniString("Job not found"));
        }
        
        job = it->second;
        
        if (job.status != ExportJobStatus::Complete) {
            return CreateErrorResponse(400, GS::UniString("Export not yet complete"));
        }
    }
    
    // Read the exported file
    std::ifstream file(job.outputPath.ToCStr().Get(), std::ios::binary);
    if (!file) {
        return CreateErrorResponse(500, GS::UniString("Failed to read exported IFC file"));
    }
    
    // Get file size
    file.seekg(0, std::ios::end);
    size_t fileSize = file.tellg();
    file.seekg(0, std::ios::beg);
    
    // Read file content
    HttpResponse response;
    response.statusCode = 200;
    response.contentType = "application/octet-stream";
    response.isBinary = true;
    response.binaryBody.resize(fileSize);
    file.read(response.binaryBody.data(), fileSize);
    file.close();
    
    // Delete temporary file
    std::remove(job.outputPath.ToCStr().Get());
    
    // Remove job from tracking map (cleanup)
    {
        std::lock_guard<std::mutex> lock(exportJobsMutex);
        exportJobs.erase(jobId);
    }
    
    ACAPI_WriteReport("IfcTester API Server: Delivered export file for job %s, cleaned up", false, jobId.c_str());
    
    return response;
}

// ============================================================================
// Utility Functions
// ============================================================================

GS::UniString ArchiCADApiServer::UrlDecode(const GS::UniString& encoded)
{
    std::string result;
    std::string input = encoded.ToCStr().Get();
    
    for (size_t i = 0; i < input.length(); ++i) {
        if (input[i] == '%' && i + 2 < input.length()) {
            std::string hex = input.substr(i + 1, 2);
            char decoded = (char)strtol(hex.c_str(), nullptr, 16);
            result += decoded;
            i += 2;
        } else if (input[i] == '+') {
            result += ' ';
        } else {
            result += input[i];
        }
    }
    
    return GS::UniString(result.c_str());
}

HttpResponse ArchiCADApiServer::CreateErrorResponse(int statusCode, const GS::UniString& message)
{
    HttpResponse response;
    response.statusCode = statusCode;
    response.contentType = "application/json";
    
    std::ostringstream json;
    json << "{\"error\":\"" << message.ToCStr().Get() << "\"}";
    response.body = GS::UniString(json.str().c_str());
    
    return response;
}

HttpResponse ArchiCADApiServer::CreateJsonResponse(const GS::UniString& json)
{
    HttpResponse response;
    response.statusCode = 200;
    response.contentType = "application/json";
    response.body = json;
    return response;
}

GS::UniString ArchiCADApiServer::EscapeJsonString(const GS::UniString& str)
{
    std::string input = str.ToCStr().Get();
    std::string output;
    output.reserve(input.length() + 10); // Reserve a bit more

    for (char c : input) {
        switch (c) {
            case '"': output += "\\\""; break;
            case '\\': output += "\\\\"; break;
            case '\b': output += "\\b"; break;
            case '\f': output += "\\f"; break;
            case '\n': output += "\\n"; break;
            case '\r': output += "\\r"; break;
            case '\t': output += "\\t"; break;
            default:
                if (static_cast<unsigned char>(c) < 0x20) {
                    char buf[7];
                    snprintf(buf, sizeof(buf), "\\u%04x", c);
                    output += buf;
                } else {
                    output += c;
                }
                break;
        }
    }

    return GS::UniString(output.c_str());
}

// ============================================================================
// Static File Serving
// ============================================================================

HttpResponse ArchiCADApiServer::HandleStaticFile(const GS::UniString& path)
{
    // Check if webAppPath is set
    if (webAppPath.IsEmpty()) {
        ACAPI_WriteReport("IfcTester API Server: WebApp path not set", false);
        return CreateErrorResponse(500, GS::UniString("WebApp path not configured"));
    }
    
    // Platform-specific path separator
#ifdef _WIN32
    const char pathSep = '\\';
#else
    const char pathSep = '/';
#endif
    
    // Determine the file path
    GS::UniString filePath = webAppPath;
    
    // Handle root path -> serve index.html
    if (path == "/" || path.IsEmpty()) {
        filePath += pathSep;
        filePath += "index.html";
    } else {
        // Convert URL path to file path
        GS::UniString relativePath = path;
        if (relativePath.BeginsWith("/")) {
            relativePath = relativePath.GetSubstring(1, relativePath.GetLength() - 1);
        }
        
        // Convert slashes to platform-specific separator
        std::string pathStr = relativePath.ToCStr().Get();
#ifdef _WIN32
        for (char& c : pathStr) {
            if (c == '/') c = '\\';
        }
#endif
        
        filePath += pathSep;
        filePath += GS::UniString(pathStr.c_str());
    }
    
    // Security check: prevent directory traversal
    std::string filePathStr = filePath.ToCStr().Get();
    if (filePathStr.find("..") != std::string::npos) {
        return CreateErrorResponse(403, GS::UniString("Forbidden"));
    }
    
    // Read the file
    std::ifstream file(filePathStr, std::ios::binary);
    if (!file) {
        // Try with index.html for SPA routing (client-side routing support)
        GS::UniString indexPath = webAppPath;
        indexPath += pathSep;
        indexPath += "index.html";
        file.open(indexPath.ToCStr().Get(), std::ios::binary);
        if (!file) {
            return CreateErrorResponse(404, GS::UniString("File not found"));
        }
        filePath = indexPath;
    }
    
    // Get file size
    file.seekg(0, std::ios::end);
    size_t fileSize = file.tellg();
    file.seekg(0, std::ios::beg);
    
    // Read file content
    HttpResponse response;
    response.statusCode = 200;
    response.contentType = GetMimeType(filePath);
    response.isBinary = true;
    response.binaryBody.resize(fileSize);
    file.read(response.binaryBody.data(), fileSize);
    file.close();
    
    return response;
}

GS::UniString ArchiCADApiServer::GetMimeType(const GS::UniString& path)
{
    // Get file extension
    Int32 dotPos = path.FindLast('.');
    if (dotPos < 0) {
        return "application/octet-stream";
    }
    
    GS::UniString ext = path.GetSubstring(dotPos + 1, path.GetLength() - dotPos - 1);
    // Convert to lowercase for comparison
    std::string extStr = ext.ToCStr().Get();
    std::transform(extStr.begin(), extStr.end(), extStr.begin(), ::tolower);
    ext = GS::UniString(extStr.c_str());
    
    // Map extensions to MIME types
    if (ext == "html" || ext == "htm") return "text/html; charset=utf-8";
    if (ext == "css") return "text/css; charset=utf-8";
    if (ext == "js" || ext == "mjs") return "application/javascript; charset=utf-8";
    if (ext == "json") return "application/json; charset=utf-8";
    if (ext == "png") return "image/png";
    if (ext == "jpg" || ext == "jpeg") return "image/jpeg";
    if (ext == "gif") return "image/gif";
    if (ext == "svg") return "image/svg+xml";
    if (ext == "ico") return "image/x-icon";
    if (ext == "woff") return "font/woff";
    if (ext == "woff2") return "font/woff2";
    if (ext == "ttf") return "font/ttf";
    if (ext == "otf") return "font/otf";
    if (ext == "eot") return "application/vnd.ms-fontobject";
    if (ext == "wasm") return "application/wasm";
    if (ext == "whl") return "application/zip";
    if (ext == "zip") return "application/zip";
    if (ext == "py") return "text/x-python; charset=utf-8";
    if (ext == "map") return "application/json";
    if (ext == "ifc") return "application/x-step";
    
    return "application/octet-stream";
}

} // namespace IfcTester
