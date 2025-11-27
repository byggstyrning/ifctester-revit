/**
 * IfcTester ArchiCAD Add-On
 * 
 * ArchiCAD API Server Implementation
 * HTTP server for communication between the web interface and ArchiCAD API.
 */

#include "ArchiCADApiServer.hpp"
#include <sstream>
#include <fstream>
#include <ctime>
#include <algorithm>

namespace IfcTester {

// ============================================================================
// Constructor / Destructor
// ============================================================================

ArchiCADApiServer::ArchiCADApiServer(int port)
    : port(port)
    , running(false)
    , serverSocket(INVALID_SOCKET)
    , configsLoaded(false)
{
#ifdef _WIN32
    // Initialize Winsock
    WSADATA wsaData;
    WSAStartup(MAKEWORD(2, 2), &wsaData);
#endif
}

ArchiCADApiServer::~ArchiCADApiServer()
{
    Stop();
    
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
        closesocket(serverSocket);
        return false;
    }
    
    // Listen
    if (listen(serverSocket, SOMAXCONN) == SOCKET_ERROR) {
        closesocket(serverSocket);
        return false;
    }
    
    // Set non-blocking mode
#ifdef _WIN32
    u_long mode = 1;
    ioctlsocket(serverSocket, FIONBIO, &mode);
#endif
    
    running = true;
    serverThread = std::thread(&ArchiCADApiServer::ServerLoop, this);
    
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
    while (running) {
        fd_set readSet;
        FD_ZERO(&readSet);
        FD_SET(serverSocket, &readSet);
        
        timeval timeout;
        timeout.tv_sec = 0;
        timeout.tv_usec = 100000; // 100ms
        
        int selectResult = select((int)serverSocket + 1, &readSet, nullptr, nullptr, &timeout);
        
        if (selectResult > 0 && FD_ISSET(serverSocket, &readSet)) {
            sockaddr_in clientAddr;
            int clientAddrLen = sizeof(clientAddr);
            
            SOCKET clientSocket = accept(serverSocket, (sockaddr*)&clientAddr, &clientAddrLen);
            
            if (clientSocket != INVALID_SOCKET) {
                // Set socket timeout
                DWORD timeout = 5000;
                setsockopt(clientSocket, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));
                
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
                        response.body = "";
                    } else {
                        std::lock_guard<std::mutex> lock(requestMutex);
                        response = HandleRequest(request);
                    }
                    
                    // Add CORS headers
                    AddCorsHeaders(response);
                    
                    // Send response
                    std::string responseStr = FormatResponse(response);
                    send(clientSocket, responseStr.c_str(), (int)responseStr.length(), 0);
                }
                
                closesocket(clientSocket);
            }
        }
    }
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
    
    // Route requests
    if (path == "/status" && request.method == "GET") {
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
    
    return CreateErrorResponse(404, "Not Found");
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
    bool success = SelectElementByGUID(guid);
    
    std::ostringstream json;
    json << "{";
    json << "\"success\":" << (success ? "true" : "false") << ",";
    json << "\"message\":\"" << (success ? "Element selected" : "Element not found") << "\"";
    json << "}";
    
    return CreateJsonResponse(GS::UniString(json.str().c_str()));
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
        return CreateErrorResponse(400, "Missing configuration parameter");
    }
    
    // Export to IFC
    GS::UniString outputPath;
    bool success = ExportToIFC(configName, outputPath);
    
    if (!success || outputPath.IsEmpty()) {
        return CreateErrorResponse(500, "IFC export failed");
    }
    
    // Read the exported file
    std::ifstream file(outputPath.ToCStr().Get(), std::ios::binary);
    if (!file) {
        return CreateErrorResponse(500, "Failed to read exported IFC file");
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
    IO::Location fileLoc;
    fileLoc.Set(outputPath);
    IO::fileSystem.Delete(fileLoc);
    
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

} // namespace IfcTester
