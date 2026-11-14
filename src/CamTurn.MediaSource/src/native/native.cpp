// camturn_native.dll: a small C ABI over MFCreateVirtualCamera for the .NET app and the installer,
// so C# does not have to redeclare IMFVirtualCamera (and its IMFAttributes vtable). See DECISIONS D-009.
#include "../common/win.h"

using Microsoft::WRL::ComPtr;

namespace camturn
{
void Trace(const wchar_t*, ...) {}

namespace
{
class MfScope
{
  public:
    MfScope()
    {
        m_com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        m_mf = MFStartup(MF_VERSION, MFSTARTUP_LITE);
    }
    ~MfScope()
    {
        if (SUCCEEDED(m_mf))
        {
            MFShutdown();
        }
        if (SUCCEEDED(m_com))
        {
            CoUninitialize();
        }
    }
    HRESULT Status() const { return FAILED(m_mf) ? m_mf : S_OK; }

  private:
    HRESULT m_com = E_FAIL;
    HRESULT m_mf = E_FAIL;
};

HRESULT CreateCamera(BOOL allUsers, IMFVirtualCamera** camera)
{
    return MFCreateVirtualCamera(MFVirtualCameraType_SoftwareCameraSource, MFVirtualCameraLifetime_System,
                                 allUsers ? MFVirtualCameraAccess_AllUsers : MFVirtualCameraAccess_CurrentUser,
                                 kFriendlyName, kClsidString, nullptr, 0, camera);
}
} // namespace
} // namespace camturn

extern "C"
{
    // *supported = TRUE if this Windows build supports software virtual cameras (Windows 11 22000+).
    HRESULT __stdcall CamTurn_IsVirtualCameraSupported(BOOL* supported)
    {
        if (!supported)
        {
            return E_POINTER;
        }
        *supported = FALSE;
        camturn::MfScope mf;
        CT_RETURN_IF_FAILED(mf.Status());
        return MFIsVirtualCameraTypeSupported(MFVirtualCameraType_SoftwareCameraSource, supported);
    }

    // Creates (or re-opens) the persistent "CamTurn Camera" and starts it. allUsers requires administrator rights
    // (the installer); the media source CLSID must already be registered under HKLM.
    HRESULT __stdcall CamTurn_RegisterVirtualCamera(BOOL allUsers)
    {
        camturn::MfScope mf;
        CT_RETURN_IF_FAILED(mf.Status());
        ComPtr<IMFVirtualCamera> camera;
        CT_RETURN_IF_FAILED(camturn::CreateCamera(allUsers, &camera));
        return camera->Start(nullptr);
    }

    // Removes the persistent camera created with the same allUsers value.
    HRESULT __stdcall CamTurn_UnregisterVirtualCamera(BOOL allUsers)
    {
        camturn::MfScope mf;
        CT_RETURN_IF_FAILED(mf.Status());
        ComPtr<IMFVirtualCamera> camera;
        CT_RETURN_IF_FAILED(camturn::CreateCamera(allUsers, &camera));
        // Remove() without Shutdown(): calling both shuts the media source down twice.
        return camera->Remove();
    }
}
