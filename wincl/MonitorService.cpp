#include <windows.h>
#include <winhttp.h>
#include <iostream>
#include <thread>
#include <atomic>
#include <string>
#include <sstream>
#include <iomanip>
#include <algorithm> 
#include <vector>
#include <pdh.h>
#include <WtsApi32.h>
#include <tlhelp32.h>
#include <memory>
#include <chrono>

#pragma comment(lib, "winhttp.lib")
#pragma comment(lib, "pdh.lib")
#pragma comment(lib, "WtsApi32.lib")

SERVICE_STATUS g_ServiceStatus = {0};
SERVICE_STATUS_HANDLE g_StatusHandle = NULL;
std::atomic<bool> g_ServiceStopped(false);
std::unique_ptr<class ActiveWindowMonitor> g_Monitor = nullptr;

const wchar_t* const SERVICE_NAME = L"ActiveWindowMonitorService";
std::wstring g_DefaultApiUrl = L"http://localhost:3000/api/status";

VOID WINAPI ServiceMain(DWORD dwArgc, LPWSTR *lpszArgv);
VOID WINAPI ServiceCtrlHandler(DWORD dwControl);
VOID ReportServiceStatus(DWORD dwCurrentState, DWORD dwWin32ExitCode, DWORD dwWaitHint);
VOID LogEvent(WORD wType, const wchar_t* lpszMsg);
BOOL ParseCommandLine(DWORD dwArgc, LPWSTR *lpszArgv, std::wstring& apiUrl);
static std::string WideToUTF8(const std::wstring& wide);

class CPUMonitor {
private:
    PDH_HQUERY cpuQuery;
    PDH_HCOUNTER cpuTotalCounter;
    
public:
    CPUMonitor() : cpuQuery(NULL), cpuTotalCounter(NULL) {
        PdhOpenQuery(NULL, NULL, &cpuQuery);
        PdhAddCounterA(cpuQuery, "\\Processor(_Total)\\% Processor Time", 0, &cpuTotalCounter);
        PdhCollectQueryData(cpuQuery);
    }
    
    ~CPUMonitor() {
        if (cpuQuery) {
            PdhCloseQuery(cpuQuery);
        }
    }
    
    double GetCPUUsage() {
        PDH_FMT_COUNTERVALUE counterVal;
        PdhCollectQueryData(cpuQuery);
        
        if (PdhGetFormattedCounterValue(cpuTotalCounter, PDH_FMT_DOUBLE, NULL, &counterVal) == ERROR_SUCCESS) {
            return counterVal.doubleValue;
        }
        return 0.0;
    }
};

class ActiveWindowMonitor {
private:
    std::atomic<bool> running;
    std::thread monitorThread;
    std::wstring apiUrl;
    CPUMonitor cpuMonitor;
    std::string lastSoftwareName; 
    
public:
    ActiveWindowMonitor(const std::wstring& url) : running(false), apiUrl(url) {}
    
    void Start() {
        running = true;
        monitorThread = std::thread(&ActiveWindowMonitor::MonitorLoop, this);
        LogEvent(EVENTLOG_INFORMATION_TYPE, L"Monitor thread started.");
    }
    
    void Stop() {
        if (!running) return;
        running = false;
        if (monitorThread.joinable()) {
            monitorThread.join();
        }
        LogEvent(EVENTLOG_INFORMATION_TYPE, L"Monitor thread stopped.");
    }
    
    ~ActiveWindowMonitor() {
        Stop();
    }
    
private:
    void MonitorLoop() {
        const std::chrono::seconds sendInterval(10);
        auto lastSendTime = std::chrono::steady_clock::now() - sendInterval; 

        while (running) {
            std::string currentSoftware = GetActiveProcessName();
            auto currentTime = std::chrono::steady_clock::now();
            bool softwareChanged = (currentSoftware != lastSoftwareName);
            bool timeElapsed = (currentTime - lastSendTime) >= sendInterval;

            if (softwareChanged || timeElapsed) {
                std::string data = CreateJsonData(currentSoftware);

                if (SendToAPI(data)) {
                    lastSoftwareName = currentSoftware;
                    lastSendTime = currentTime;
                } else {
                    LogEvent(EVENTLOG_ERROR_TYPE, L"Failed to send data to API.");
                }
            }
            for (int i = 0; i < 10 && running; ++i) {
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            }
        }
    }
    
    std::string GetCurrentTimestamp() {
        SYSTEMTIME st;
        GetSystemTime(&st);
        
        char buffer[64];
        snprintf(buffer, sizeof(buffer), "%04d-%02d-%02dT%02d:%02d:%02d.%03dZ",
                st.wYear, st.wMonth, st.wDay,
                st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
        
        return std::string(buffer);
    }
    
    std::string CreateJsonData(const std::string& software) {
        double cpuUsage = cpuMonitor.GetCPUUsage();
        std::string timestamp = GetCurrentTimestamp();
        std::wstring logMsg = L"Detected software: " + std::wstring(software.begin(), software.end());
        LogEvent(EVENTLOG_INFORMATION_TYPE, logMsg.c_str());
        
        std::ostringstream json;
        json << "{";
        json << "\"globalConnectionStatus\":\"online\",";
        json << "\"receivedAt\":\"" << timestamp << "\",";
        json << "\"devices\": {";
        json << "\"pc\": {";
        json << "\"status\":\"on\",";
        json << "\"software\":\"" << EscapeJsonString(software) << "\",";
        json << "\"cpuUsage\":" << std::fixed << std::setprecision(2) << cpuUsage << ",";
        json << "\"connectionStatus\":\"online\"";
        json << "}";
        json << "}";
        json << "}";
        
        return json.str();
    }
    
    std::string EscapeJsonString(const std::string& input) {
        std::ostringstream escaped;
        for (char c : input) {
            switch (c) {
                case '"': escaped << "\\\""; break;
                case '\\': escaped << "\\\\"; break;
                case '\b': escaped << "\\b"; break;
                case '\f': escaped << "\\f"; break;
                case '\n': escaped << "\\n"; break;
                case '\r': escaped << "\\r"; break;
                case '\t': escaped << "\\t"; break;
                default:
                    if (c >= 0 && c <= 0x1F) {
                        escaped << "\\u" << std::hex << std::setw(4) << std::setfill('0') << (int)c;
                    } else {
                        escaped << c;
                    }
                    break;
            }
        }
        return escaped.str();
    }
    std::string GetActiveProcessName() {
        return GetForegroundProcessName();
    }
    
    std::string GetForegroundProcessName() {
        HWND foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == NULL) {
            return "NoActiveWindow";
        }
        wchar_t className[256];
        if (GetClassNameW(foregroundWindow, className, 256) > 0) {
            if (wcscmp(className, L"WorkerW") == 0 || 
                wcscmp(className, L"Progman") == 0 || 
                wcscmp(className, L"Shell_TrayWnd") == 0) {
                return "Desktop"; 
            }
        }

        DWORD processId;
        GetWindowThreadProcessId(foregroundWindow, &processId);
        if (processId == 0) {
            return "System";
        }

        HANDLE hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (hSnapshot == INVALID_HANDLE_VALUE) {
            return "Unknown";
        }

        PROCESSENTRY32W pe32;
        pe32.dwSize = sizeof(PROCESSENTRY32W);

        std::string processName = "Unknown";

        if (Process32FirstW(hSnapshot, &pe32)) {
            do {
                if (pe32.th32ProcessID == processId) {
                    processName = WideToUTF8(pe32.szExeFile);
                    break;
                }
            } while (Process32NextW(hSnapshot, &pe32));
        }

        CloseHandle(hSnapshot);
        return processName;
    }
    
    bool SendToAPI(const std::string& data) {
        HINTERNET hSession = NULL;
        HINTERNET hConnect = NULL;
        HINTERNET hRequest = NULL;
        BOOL bResults = FALSE;

        URL_COMPONENTS urlComp;
        ZeroMemory(&urlComp, sizeof(urlComp));
        urlComp.dwStructSize = sizeof(urlComp);
        
        wchar_t hostName[256] = {0};
        wchar_t urlPath[1024] = {0}; 
        
        urlComp.lpszHostName = hostName;
        urlComp.dwHostNameLength = sizeof(hostName)/sizeof(hostName[0]);
        urlComp.lpszUrlPath = urlPath;
        urlComp.dwUrlPathLength = sizeof(urlPath)/sizeof(urlPath[0]);
        
        if (!WinHttpCrackUrl(apiUrl.c_str(), static_cast<DWORD>(apiUrl.length()), 0, &urlComp)) {
            return false;
        }

        hSession = WinHttpOpen(L"ActiveWindowMonitor/1.0", 
                               WINHTTP_ACCESS_TYPE_DEFAULT_PROXY,
                               WINHTTP_NO_PROXY_NAME, 
                               WINHTTP_NO_PROXY_BYPASS, 0);
        
        if (!hSession) {
            return false;
        }
        
        WinHttpSetTimeouts(hSession, 10000, 10000, 10000, 10000);
        
        hConnect = WinHttpConnect(hSession, hostName, 
                                 urlComp.nPort, 0);
        if (!hConnect) {
            WinHttpCloseHandle(hSession);
            return false;
        }
        
        DWORD flags = (urlComp.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
        hRequest = WinHttpOpenRequest(hConnect, L"POST", urlPath,
                                      NULL, WINHTTP_NO_REFERER, 
                                      WINHTTP_DEFAULT_ACCEPT_TYPES, 
                                      flags);
        
        if (!hRequest) {
            if (hConnect) WinHttpCloseHandle(hConnect);
            if (hSession) WinHttpCloseHandle(hSession);
            return false;
        }

        std::wstring headers = L"Content-Type: application/json\r\n";
        
        bResults = WinHttpSendRequest(hRequest,
                                      headers.c_str(), 
                                      static_cast<DWORD>(headers.length()),
                                      (LPVOID)data.c_str(), 
                                      static_cast<DWORD>(data.length()),
                                      static_cast<DWORD>(data.length()), 
                                      0);
        
        if (bResults) {
            bResults = WinHttpReceiveResponse(hRequest, NULL);
        }
        
        if (bResults) {
            DWORD statusCode = 0;
            DWORD statusCodeSize = sizeof(statusCode);
            WinHttpQueryHeaders(hRequest, 
                                 WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                                 NULL, &statusCode, &statusCodeSize, NULL);
            
            if (statusCode != 200) {
                bResults = FALSE;
            }
        }
        
        if (hRequest) WinHttpCloseHandle(hRequest);
        if (hConnect) WinHttpCloseHandle(hConnect);
        if (hSession) WinHttpCloseHandle(hSession);
        
        return bResults != FALSE;
    }
};

static std::string WideToUTF8(const std::wstring& wide) {
    if (wide.empty()) return "";
    int size = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (size <= 0) return "";
    std::string result(size - 1, 0);
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, &result[0], size, nullptr, nullptr);
    return result;
}


BOOL ParseCommandLine(DWORD dwArgc, LPWSTR *lpszArgv, std::wstring& apiUrl) {
    if (dwArgc > 1 && lpszArgv[1] != nullptr && wcslen(lpszArgv[1]) > 0) {
        apiUrl = lpszArgv[1];
        std::wstring msg = L"Using API URL from command line: " + apiUrl;
        LogEvent(EVENTLOG_INFORMATION_TYPE, msg.c_str());
        return TRUE;
    } else {
        apiUrl = g_DefaultApiUrl;
        std::wstring msg = L"No API URL provided, using default: " + apiUrl;
        LogEvent(EVENTLOG_INFORMATION_TYPE, msg.c_str());
        return TRUE;
    }
}

int wmain(int argc, wchar_t *argv[]) {
    if (argc > 1 && wcscmp(argv[1], L"debug") == 0) {
        std::wstring apiUrl = (argc > 2) ? argv[2] : g_DefaultApiUrl;
        std::wcout << L"Running in debug mode with API: " << apiUrl << std::endl;
        
        ActiveWindowMonitor monitor(apiUrl);
        monitor.Start();
        
        std::wcout << L"Monitor started. Press Enter to stop..." << std::endl;
        std::cin.get();
        
        monitor.Stop();
        std::wcout << L"Monitor stopped." << std::endl;
        return 0;
    }

    SERVICE_TABLE_ENTRYW ServiceTable[] = {
        {(LPWSTR)SERVICE_NAME, (LPSERVICE_MAIN_FUNCTIONW)ServiceMain},
        {NULL, NULL}
    };

    if (StartServiceCtrlDispatcherW(ServiceTable) == FALSE) {
        DWORD error = GetLastError();
        if (error != ERROR_FAILED_SERVICE_CONTROLLER_CONNECT) {
            std::wcerr << L"StartServiceCtrlDispatcher failed with error: " << error << std::endl;
            return error;
        }
        return 0; 
    }
    return 0;
}

VOID WINAPI ServiceMain(DWORD dwArgc, LPWSTR *lpszArgv) {
    DWORD dwError = 0;
    
    g_StatusHandle = RegisterServiceCtrlHandlerW(SERVICE_NAME, ServiceCtrlHandler);

    if (g_StatusHandle == NULL) {
        return;
    }

    g_ServiceStatus.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
    g_ServiceStatus.dwServiceSpecificExitCode = 0;
    
    ReportServiceStatus(SERVICE_START_PENDING, NO_ERROR, 3000);
    LogEvent(EVENTLOG_INFORMATION_TYPE, L"Service starting...");

    std::wstring apiUrl;
    if (!ParseCommandLine(dwArgc, lpszArgv, apiUrl)) {
        LogEvent(EVENTLOG_ERROR_TYPE, L"Failed to parse command line parameters.");
        ReportServiceStatus(SERVICE_STOPPED, ERROR_INVALID_PARAMETER, 0);
        return;
    }

    try {
        g_Monitor = std::make_unique<ActiveWindowMonitor>(apiUrl);
        g_Monitor->Start();

        ReportServiceStatus(SERVICE_RUNNING, NO_ERROR, 0);
        LogEvent(EVENTLOG_INFORMATION_TYPE, L"Service started successfully and running the monitor loop.");
        
        while (!g_ServiceStopped) {
            Sleep(1000);
        }

    } catch (const std::exception& e) {
        std::wstring wMsg = L"Service runtime exception: ";
        std::string narrowMsg = e.what();
        wMsg += std::wstring(narrowMsg.begin(), narrowMsg.end());
        LogEvent(EVENTLOG_ERROR_TYPE, wMsg.c_str());
        dwError = ERROR_SERVICE_SPECIFIC_ERROR;
    } catch (...) {
        LogEvent(EVENTLOG_ERROR_TYPE, L"Service runtime unknown exception.");
        dwError = ERROR_SERVICE_SPECIFIC_ERROR;
    }

    if (g_Monitor) {
        g_Monitor->Stop();
        g_Monitor.reset();
    }

    ReportServiceStatus(SERVICE_STOPPED, dwError, 0);
    LogEvent(EVENTLOG_INFORMATION_TYPE, L"Service stopped.");
}

VOID WINAPI ServiceCtrlHandler(DWORD dwControl) {
    switch (dwControl) {
        case SERVICE_CONTROL_STOP:
        case SERVICE_CONTROL_SHUTDOWN:
            LogEvent(EVENTLOG_INFORMATION_TYPE, L"Service received STOP control.");
            ReportServiceStatus(SERVICE_STOP_PENDING, NO_ERROR, 5000);
            g_ServiceStopped = true;
            if (g_Monitor) {
                g_Monitor->Stop();
            }
            break;
            
        case SERVICE_CONTROL_INTERROGATE:
            break;
            
        default:
            break;
    }
    ReportServiceStatus(g_ServiceStatus.dwCurrentState, NO_ERROR, 0);
}

VOID ReportServiceStatus(DWORD dwCurrentState, DWORD dwWin32ExitCode, DWORD dwWaitHint) {
    static DWORD dwCheckPoint = 1;

    g_ServiceStatus.dwCurrentState = dwCurrentState;
    g_ServiceStatus.dwWin32ExitCode = dwWin32ExitCode;
    g_ServiceStatus.dwWaitHint = dwWaitHint;

    if (dwCurrentState == SERVICE_RUNNING || dwCurrentState == SERVICE_STOPPED) {
        g_ServiceStatus.dwCheckPoint = 0;
    } else {
        g_ServiceStatus.dwCheckPoint = dwCheckPoint++;
    }
    
    if (!SetServiceStatus(g_StatusHandle, &g_ServiceStatus)) {
        LogEvent(EVENTLOG_ERROR_TYPE, L"SetServiceStatus failed.");
    }
}

VOID LogEvent(WORD wType, const wchar_t* lpszMsg) {
    HANDLE hEventSource = NULL;
    const wchar_t* lpszStrings[1];

    hEventSource = RegisterEventSourceW(NULL, SERVICE_NAME);

    if (hEventSource != NULL) {
        lpszStrings[0] = lpszMsg;

        ReportEventW(hEventSource, 
                     wType, 
                     0, 
                     0, 
                     NULL, 
                     1, 
                     0,
                     lpszStrings, 
                     NULL);

        DeregisterEventSource(hEventSource);
    }
}