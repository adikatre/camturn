// COM entry points for camturn_mediasource.dll. Registration (HKLM\Software\Classes\CLSID\{...}) is done by the
// installer, not by DllRegisterServer, so the DLL never writes the registry itself.
#include "Activator.h"

#include <cstdarg>
#include <cwchar>

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::MakeAndInitialize;

namespace camturn
{
void Trace(const wchar_t* format, ...)
{
#if defined(_DEBUG) || defined(CAMTURN_TRACE)
    wchar_t buffer[512];
    va_list args;
    va_start(args, format);
    _vsnwprintf_s(buffer, _TRUNCATE, format, args);
    va_end(args);
    OutputDebugStringW(buffer);
    OutputDebugStringW(L"\n");
#else
    (void)format;
#endif
}

namespace
{
class ClassFactory
    : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, IClassFactory>
{
  public:
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** object) override
    {
        CT_RETURN_HR_IF(E_POINTER, !object);
        *object = nullptr;
        CT_RETURN_HR_IF(CLASS_E_NOAGGREGATION, outer != nullptr);
        ComPtr<Activator> activator;
        CT_RETURN_IF_FAILED(MakeAndInitialize<Activator>(&activator));
        return activator.CopyTo(riid, object);
    }

    STDMETHODIMP LockServer(BOOL lock) override
    {
        auto& module = Microsoft::WRL::Module<Microsoft::WRL::InProc>::GetModule();
        lock ? module.IncrementObjectCount() : module.DecrementObjectCount();
        return S_OK;
    }
};
} // namespace
} // namespace camturn

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return Microsoft::WRL::Module<Microsoft::WRL::InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

_Check_return_ STDAPI DllGetClassObject(_In_ REFCLSID clsid, _In_ REFIID riid, _Outptr_ LPVOID* object)
{
    if (!object)
    {
        return E_POINTER;
    }
    *object = nullptr;
    if (clsid != camturn::CLSID_CamTurnMediaSource)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    auto factory = Microsoft::WRL::Make<camturn::ClassFactory>();
    return factory ? factory.CopyTo(riid, object) : E_OUTOFMEMORY;
}
