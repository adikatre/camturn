// Shared Windows includes and helpers for the media source and the native helper DLL.
#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <windows.h>
// Order matters: ks.h redefines GUID_NULL as a __uuidof() macro, so everything that pulls in cguid.h (COM and
// WRL headers) must come before the kernel-streaming headers.
#include <cguid.h>
#include <objbase.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>
#include <wrl/ftm.h>
#include <wrl/wrappers/corewrappers.h>
#include <initguid.h>
#include <propvarutil.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mferror.h>
#include <mfvirtualcamera.h>
#include <ks.h>
#include <ksproxy.h>
#include <ksmedia.h>

#define CT_RETURN_IF_FAILED(expr)                                                                                      \
    do                                                                                                                 \
    {                                                                                                                  \
        const HRESULT _hr = (expr);                                                                                    \
        if (FAILED(_hr))                                                                                               \
        {                                                                                                              \
            return _hr;                                                                                                \
        }                                                                                                              \
    } while (0)

#define CT_RETURN_HR_IF(hr, cond)                                                                                      \
    do                                                                                                                 \
    {                                                                                                                  \
        if (cond)                                                                                                      \
        {                                                                                                              \
            return (hr);                                                                                               \
        }                                                                                                              \
    } while (0)

namespace camturn
{
// {EF0883BB-0F68-4106-B1D3-3B3B786D600D}
constexpr GUID CLSID_CamTurnMediaSource = {
    0xef0883bb, 0x0f68, 0x4106, {0xb1, 0xd3, 0x3b, 0x3b, 0x78, 0x6d, 0x60, 0x0d}};
constexpr const wchar_t* kClsidString = L"{EF0883BB-0F68-4106-B1D3-3B3B786D600D}";
constexpr const wchar_t* kFriendlyName = L"CamTurn Camera";

// Debug output only; never writes files or registry (Frame Server runs us as LocalService).
void Trace(const wchar_t* format, ...);
} // namespace camturn

// Implements every IMFAttributes method by forwarding to a member ComPtr<IMFAttributes>.
#define CAMTURN_FORWARD_IMFATTRIBUTES(store)                                                                           \
    STDMETHODIMP GetItem(REFGUID k, PROPVARIANT* v) override                                                           \
    {                                                                                                                  \
        return store->GetItem(k, v);                                                                                   \
    }                                                                                                                  \
    STDMETHODIMP GetItemType(REFGUID k, MF_ATTRIBUTE_TYPE* t) override                                                 \
    {                                                                                                                  \
        return store->GetItemType(k, t);                                                                               \
    }                                                                                                                  \
    STDMETHODIMP CompareItem(REFGUID k, REFPROPVARIANT v, BOOL* r) override                                            \
    {                                                                                                                  \
        return store->CompareItem(k, v, r);                                                                            \
    }                                                                                                                  \
    STDMETHODIMP Compare(IMFAttributes* o, MF_ATTRIBUTES_MATCH_TYPE m, BOOL* r) override                               \
    {                                                                                                                  \
        return store->Compare(o, m, r);                                                                                \
    }                                                                                                                  \
    STDMETHODIMP GetUINT32(REFGUID k, UINT32* v) override                                                              \
    {                                                                                                                  \
        return store->GetUINT32(k, v);                                                                                 \
    }                                                                                                                  \
    STDMETHODIMP GetUINT64(REFGUID k, UINT64* v) override                                                              \
    {                                                                                                                  \
        return store->GetUINT64(k, v);                                                                                 \
    }                                                                                                                  \
    STDMETHODIMP GetDouble(REFGUID k, double* v) override                                                              \
    {                                                                                                                  \
        return store->GetDouble(k, v);                                                                                 \
    }                                                                                                                  \
    STDMETHODIMP GetGUID(REFGUID k, GUID* v) override                                                                  \
    {                                                                                                                  \
        return store->GetGUID(k, v);                                                                                   \
    }                                                                                                                  \
    STDMETHODIMP GetStringLength(REFGUID k, UINT32* l) override                                                        \
    {                                                                                                                  \
        return store->GetStringLength(k, l);                                                                           \
    }                                                                                                                  \
    STDMETHODIMP GetString(REFGUID k, LPWSTR s, UINT32 n, UINT32* l) override                                          \
    {                                                                                                                  \
        return store->GetString(k, s, n, l);                                                                           \
    }                                                                                                                  \
    STDMETHODIMP GetAllocatedString(REFGUID k, LPWSTR* s, UINT32* l) override                                          \
    {                                                                                                                  \
        return store->GetAllocatedString(k, s, l);                                                                     \
    }                                                                                                                  \
    STDMETHODIMP GetBlobSize(REFGUID k, UINT32* n) override                                                            \
    {                                                                                                                  \
        return store->GetBlobSize(k, n);                                                                               \
    }                                                                                                                  \
    STDMETHODIMP GetBlob(REFGUID k, UINT8* b, UINT32 n, UINT32* s) override                                            \
    {                                                                                                                  \
        return store->GetBlob(k, b, n, s);                                                                             \
    }                                                                                                                  \
    STDMETHODIMP GetAllocatedBlob(REFGUID k, UINT8** b, UINT32* n) override                                            \
    {                                                                                                                  \
        return store->GetAllocatedBlob(k, b, n);                                                                       \
    }                                                                                                                  \
    STDMETHODIMP GetUnknown(REFGUID k, REFIID r, LPVOID* p) override                                                   \
    {                                                                                                                  \
        return store->GetUnknown(k, r, p);                                                                             \
    }                                                                                                                  \
    STDMETHODIMP SetItem(REFGUID k, REFPROPVARIANT v) override                                                         \
    {                                                                                                                  \
        return store->SetItem(k, v);                                                                                   \
    }                                                                                                                  \
    STDMETHODIMP DeleteItem(REFGUID k) override                                                                        \
    {                                                                                                                  \
        return store->DeleteItem(k);                                                                                   \
    }                                                                                                                  \
    STDMETHODIMP DeleteAllItems() override                                                                             \
    {                                                                                                                  \
        return store->DeleteAllItems();                                                                                \
    }                                                                                                                  \
    STDMETHODIMP SetUINT32(REFGUID k, UINT32 v) override                                                               \
    {                                                                                                                  \
        return store->SetUINT32(k, v);                                                                                 \
    }                                                                                                                  \
    STDMETHODIMP SetUINT64(REFGUID k, UINT64 v) override                                                               \
    {                                                                                                                  \
        return store->SetUINT64(k, v);                                                                                 \
    }                                                                                                                  \
    STDMETHODIMP SetDouble(REFGUID k, double v) override                                                               \
    {                                                                                                                  \
        return store->SetDouble(k, v);                                                                                 \
    }                                                                                                                  \
    STDMETHODIMP SetGUID(REFGUID k, REFGUID v) override                                                                \
    {                                                                                                                  \
        return store->SetGUID(k, v);                                                                                   \
    }                                                                                                                  \
    STDMETHODIMP SetString(REFGUID k, LPCWSTR v) override                                                              \
    {                                                                                                                  \
        return store->SetString(k, v);                                                                                 \
    }                                                                                                                  \
    STDMETHODIMP SetBlob(REFGUID k, const UINT8* b, UINT32 n) override                                                 \
    {                                                                                                                  \
        return store->SetBlob(k, b, n);                                                                                \
    }                                                                                                                  \
    STDMETHODIMP SetUnknown(REFGUID k, IUnknown* u) override                                                           \
    {                                                                                                                  \
        return store->SetUnknown(k, u);                                                                                \
    }                                                                                                                  \
    STDMETHODIMP LockStore() override                                                                                  \
    {                                                                                                                  \
        return store->LockStore();                                                                                     \
    }                                                                                                                  \
    STDMETHODIMP UnlockStore() override                                                                                \
    {                                                                                                                  \
        return store->UnlockStore();                                                                                   \
    }                                                                                                                  \
    STDMETHODIMP GetCount(UINT32* n) override                                                                          \
    {                                                                                                                  \
        return store->GetCount(n);                                                                                     \
    }                                                                                                                  \
    STDMETHODIMP GetItemByIndex(UINT32 i, GUID* k, PROPVARIANT* v) override                                            \
    {                                                                                                                  \
        return store->GetItemByIndex(i, k, v);                                                                         \
    }                                                                                                                  \
    STDMETHODIMP CopyAllItems(IMFAttributes* d) override                                                               \
    {                                                                                                                  \
        return store->CopyAllItems(d);                                                                                 \
    }
