#include "Activator.h"

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::MakeAndInitialize;

namespace camturn
{

HRESULT Activator::RuntimeClassInitialize()
{
    CT_RETURN_IF_FAILED(MFCreateAttributes(&m_attributes, 4));
    return m_attributes->SetGUID(MFT_TRANSFORM_CLSID_Attribute, CLSID_CamTurnMediaSource);
}

STDMETHODIMP Activator::ActivateObject(REFIID riid, void** object)
{
    CT_RETURN_HR_IF(E_POINTER, !object);
    *object = nullptr;
    auto lock = m_lock.LockExclusive();
    if (!m_source)
    {
        CT_RETURN_IF_FAILED(MakeAndInitialize<MediaSource>(&m_source, m_attributes.Get()));
    }
    return m_source.CopyTo(riid, object);
}

STDMETHODIMP Activator::ShutdownObject()
{
    ComPtr<MediaSource> source;
    {
        auto lock = m_lock.LockExclusive();
        source.Swap(m_source);
    }
    if (source)
    {
        source->Shutdown();
    }
    return S_OK;
}

STDMETHODIMP Activator::DetachObject()
{
    auto lock = m_lock.LockExclusive();
    m_source.Reset();
    return S_OK;
}

} // namespace camturn
