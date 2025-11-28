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
#endif
        return false;
    }
    
    // Set socket options
    int opt = 1;
    setsockopt(serverSocket, SOL_SOCKET, SO_REUSEADDR, (const char*)&opt, sizeof(opt));
    
    // Bind to port
    sockaddr_in serverAddr;
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_addr.s_addr = inet_addr("127.0.0.1");
    serverAddr.sin_port = htons(port);
    
    if (bind(serverSocket, (sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR) {
#ifdef _WIN32
        int error = WSAGetLastError();
        ACAPI_WriteReport("IfcTester API Server: Failed to bind to port %d (error %d). Port may be in use.", false, port, error);
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
                    // If select fails, break the loop
                    break;
                }
#endif
                continue;
            }
            
            if (selectResult > 0 && FD_ISSET(serverSocket, &readSet)) {
                sockaddr_in clientAddr;
                int clientAddrLen = sizeof(clientAddr);
                
                SOCKET clientSocket = accept(serverSocket, (sockaddr*)&clientAddr, &clientAddrLen);
                
                if (clientSocket != INVALID_SOCKET) {
                    // Set client socket to blocking mode (for reading)
                    // The server socket is non-blocking for accept(), but client sockets should be blocking for recv()
#ifdef _WIN32
                    u_long mode = 0; // 0 = blocking mode
                    ioctlsocket(clientSocket, FIONBIO, &mode);
#endif
                    
                    // Set socket timeout
                    DWORD timeout = 5000;
                    setsockopt(clientSocket, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));
                    setsockopt(clientSocket, SOL_SOCKET, SO_SNDTIMEO, (const char*)&timeout, sizeof(timeout));
                    
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
#endif
                        }
                    } else if (bytesRead == 0) {
                        // Client closed connection
                        ACAPI_WriteReport("IfcTester API Server: Client closed connection", false);
                    } else {
                        // Error reading (bytesRead < 0)
#ifdef _WIN32
                        int error = WSAGetLastError();
                        // WSAEWOULDBLOCK (10035) shouldn't happen with blocking sockets, but ignore it just in case
                        // WSAETIMEDOUT is expected when timeout occurs
                        if (error != WSAETIMEDOUT && error != WSAEWOULDBLOCK) {
                            ACAPI_WriteReport("IfcTester API Server: Error reading request (error %d)", false, error);
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
    
    if (response.isBinary) {
        stream << "Content-Length: " << response.binaryBody.size() << "\r\n";
    } else {
        stream << "Content-Length: " << response.body.GetLength() << "\r\n";
    }
    stream << "\r\n";
    
    // Body
    if (response.isBinary) {
        stream.write(response.binaryBody.data(), response.binaryBody.size());
    } else {
        stream << response.body.ToCStr().Get();
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
    // Static file serving for the webapp
    else if (request.method == "GET") {
        ACAPI_WriteReport("IfcTester API Server: Handling GET request for path: %s", false, path.ToCStr().Get());
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
    json << "\"version\":\"1.0.0\"";
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
    // Check if message window is set up
    if (messageWindow == nullptr) {
        ACAPI_WriteReport("IfcTester API Server: Message window not initialized, cannot queue selection", false);
        return false;
    }
    
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
    PostMessage(messageWindow, WM_IFCTESTER_PROCESS_QUEUE, 0, 0);
    
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
        json << "\"name\":\"" << config.name.ToCStr().Get() << "\",";
        json << "\"description\":\"" << config.description.ToCStr().Get() << "\",";
        json << "\"version\":\"" << config.version.ToCStr().Get() << "\"";
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
    
    // Export to IFC
    GS::UniString outputPath;
    bool success = ExportToIFC(configName, outputPath);
    
    if (!success || outputPath.IsEmpty()) {
        return CreateErrorResponse(500, GS::UniString("IFC export failed"));
    }
    
    // Read the exported file
    std::ifstream file(outputPath.ToCStr().Get(), std::ios::binary);
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
    
    // Delete temporary file (using standard C++ API)
    std::remove(outputPath.ToCStr().Get());
    
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
    
    // Log the request for debugging
    ACAPI_WriteReport("IfcTester API Server: Serving static file - path: %s, webAppPath: %s", false, 
        path.ToCStr().Get(), webAppPath.ToCStr().Get());
    
    // Determine the file path
    GS::UniString filePath = webAppPath;
    
    // Handle root path -> serve index.html
    if (path == "/" || path.IsEmpty()) {
        filePath += "\\index.html";
    } else {
        // Convert URL path to file path (replace / with \)
        GS::UniString relativePath = path;
        if (relativePath.BeginsWith("/")) {
            relativePath = relativePath.GetSubstring(1, relativePath.GetLength() - 1);
        }
        
        // Replace forward slashes with backslashes for Windows
        std::string pathStr = relativePath.ToCStr().Get();
        for (char& c : pathStr) {
            if (c == '/') c = '\\';
        }
        
        filePath += "\\";
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
        ACAPI_WriteReport("IfcTester API Server: File not found: %s", false, filePathStr.c_str());
        // Try with index.html for SPA routing (client-side routing support)
        GS::UniString indexPath = webAppPath + "\\index.html";
        file.open(indexPath.ToCStr().Get(), std::ios::binary);
        if (!file) {
            ACAPI_WriteReport("IfcTester API Server: index.html also not found at: %s", false, indexPath.ToCStr().Get());
            return CreateErrorResponse(404, GS::UniString("File not found"));
        }
        filePath = indexPath;
        ACAPI_WriteReport("IfcTester API Server: Serving index.html for SPA routing", false);
    } else {
        ACAPI_WriteReport("IfcTester API Server: Successfully found file: %s", false, filePathStr.c_str());
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
