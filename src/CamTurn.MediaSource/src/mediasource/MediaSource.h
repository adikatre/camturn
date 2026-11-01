#pragma once

#include "../common/win.h"
#include "MediaStream.h"

namespace camturn
{

// The custom media source that Windows Frame Server activates for "CamTurn Camera".
// It does as little as possible: one stream, frames come from the CamTurn app over a pipe.
class MediaSource : public Microsoft::WRL::RuntimeClass<
                        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
                        Microsoft::WRL::ChainInterfaces<IMFMediaSourceEx, IMFMediaSource, IMFMediaEventGenerator>,
                        IMFAttributes, IMFGetService, IKsControl, Microsoft::WRL::FtmBase>
{
  public:
    HRESULT RuntimeClassInitialize(IMFAttributes* activatorAttributes);

    // IMFMediaEventGenerator
    STDMETHODIMP BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state) override;
    STDMETHODIMP EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event) override;
    STDMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** event) override;
    STDMETHODIMP QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status,
                            const PROPVARIANT* value) override;

    // IMFMediaSource
    STDMETHODIMP CreatePresentationDescriptor(IMFPresentationDescriptor** descriptor) override;
    STDMETHODIMP GetCharacteristics(DWORD* characteristics) override;
    STDMETHODIMP Pause() override;
    STDMETHODIMP Shutdown() override;
    STDMETHODIMP Start(IMFPresentationDescriptor* descriptor, const GUID* timeFormat,
                       const PROPVARIANT* startPosition) override;
    STDMETHODIMP Stop() override;

    // IMFMediaSourceEx
    STDMETHODIMP GetSourceAttributes(IMFAttributes** attributes) override;
    STDMETHODIMP GetStreamAttributes(DWORD streamId, IMFAttributes** attributes) override;
    STDMETHODIMP SetD3DManager(IUnknown* manager) override;

    // IMFGetService
    STDMETHODIMP GetService(REFGUID service, REFIID riid, LPVOID* object) override;

    // IKsControl
    STDMETHODIMP KsProperty(PKSPROPERTY, ULONG, LPVOID, ULONG, ULONG*) override;
    STDMETHODIMP KsMethod(PKSMETHOD, ULONG, LPVOID, ULONG, ULONG*) override;
    STDMETHODIMP KsEvent(PKSEVENT, ULONG, LPVOID, ULONG, ULONG*) override;

    CAMTURN_FORWARD_IMFATTRIBUTES(m_attributes)

  private:
    Microsoft::WRL::Wrappers::SRWLock m_lock;
    Microsoft::WRL::ComPtr<IMFAttributes> m_attributes;
    Microsoft::WRL::ComPtr<IMFMediaEventQueue> m_queue;
    Microsoft::WRL::ComPtr<IMFPresentationDescriptor> m_descriptor;
    Microsoft::WRL::ComPtr<MediaStream> m_stream;
    bool m_streamWasSelected = false;
    bool m_shutdown = false;
};

} // namespace camturn
