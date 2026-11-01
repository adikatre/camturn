#pragma once

#include "../common/win.h"
#include "MediaSource.h"

namespace camturn
{

// What CoCreateInstance(CLSID_CamTurnMediaSource) returns. Frame Server fills its attribute store
// (symbolic link and configuration) and then calls ActivateObject to get the media source.
class Activator : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
                                                      Microsoft::WRL::ChainInterfaces<IMFActivate, IMFAttributes>,
                                                      Microsoft::WRL::FtmBase>
{
  public:
    HRESULT RuntimeClassInitialize();

    STDMETHODIMP ActivateObject(REFIID riid, void** object) override;
    STDMETHODIMP ShutdownObject() override;
    STDMETHODIMP DetachObject() override;

    CAMTURN_FORWARD_IMFATTRIBUTES(m_attributes)

  private:
    Microsoft::WRL::Wrappers::SRWLock m_lock;
    Microsoft::WRL::ComPtr<IMFAttributes> m_attributes;
    Microsoft::WRL::ComPtr<MediaSource> m_source;
};

} // namespace camturn
