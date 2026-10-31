// A minimal Lua C entry point: no Lua symbols or game memory are accessed here.
// The module and helper live together in Binaries/Win64/RoweRC.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wchar.h>

static HMODULE module_handle;

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        module_handle = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

extern "C" __declspec(dllexport) int RoweRCStart(void*) {
    wchar_t executable[4096] = {};
    if (!GetModuleFileNameW(module_handle, executable, 4096)) return 0;
    wchar_t* last_separator = wcsrchr(executable, L'\\');
    if (!last_separator) return 0;
    last_separator[1] = 0;
    wchar_t working_directory[4096] = {};
    wcscpy_s(working_directory, executable);
    if (wcscat_s(executable, L"RoweRC.exe")) return 0;
    wchar_t arguments[8192] = {};
    if (swprintf_s(arguments, L"\"%s\" --background-only", executable) < 0) return 0;
    STARTUPINFOW startup = {};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;
    PROCESS_INFORMATION process = {};
    if (CreateProcessW(executable, arguments, nullptr, nullptr, FALSE,
        CREATE_NO_WINDOW, nullptr, working_directory, &startup, &process)) {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
    }
    return 0; // Lua C functions return the number of values pushed, here zero.
}
