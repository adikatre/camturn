#include <cwchar>
#include <windows.h>

extern "C" __declspec(dllimport) HRESULT __stdcall CamTurn_RegisterVirtualCamera(BOOL allUsers);
extern "C" __declspec(dllimport) HRESULT __stdcall CamTurn_UnregisterVirtualCamera(BOOL allUsers);

int wmain(int argc, wchar_t** argv)
{
    if (argc == 2 && std::wcscmp(argv[1], L"--register") == 0)
    {
        return CamTurn_RegisterVirtualCamera(TRUE);
    }
    if (argc == 2 && std::wcscmp(argv[1], L"--unregister") == 0)
    {
        return CamTurn_UnregisterVirtualCamera(TRUE);
    }
    return E_INVALIDARG;
}
