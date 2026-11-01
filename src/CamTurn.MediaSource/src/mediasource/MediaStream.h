#pragma once

#include "../common/win.h"
#include "PipeClient.h"

#include <vector>

namespace camturn
{

// The single video stream of the CamTurn virtual camera. Serves the latest frame from the CamTurn app,
// or a placeholder frame whenever none is available, so the host app never stalls.
class MediaStream : public Microsoft::WRL::RuntimeClass<
                        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
                        Microsoft::WRL::ChainInterfaces<IMFMediaStream2, IMFMediaStream, IMFMediaEventGenerator>,
                        IMFAttributes, IKsControl, Microsoft::WRL::FtmBase>
{
  public:
    HRESULT RuntimeClassInitialize(IMFMediaSource* source, DWORD id);

    // IMFMediaEventGenerator
    STDMETHODIMP BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state) override;
    STDMETHODIMP EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event) override;
    STDMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** event) override;
    STDMETHODIMP QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status,
                            const PROPVARIANT* value) override;

    // IMFMediaStream
    STDMETHODIMP GetMediaSource(IMFMediaSource** source) override;
    STDMETHODIMP GetStreamDescriptor(IMFStreamDescriptor** descriptor) override;
    STDMETHODIMP RequestSample(IUnknown* token) override;

    // IMFMediaStream2
    STDMETHODIMP SetStreamState(MF_STREAM_STATE state) override;
    STDMETHODIMP GetStreamState(MF_STREAM_STATE* state) override;

    // IKsControl: no camera controls are exposed.
    STDMETHODIMP KsProperty(PKSPROPERTY, ULONG, LPVOID, ULONG, ULONG*) override;
    STDMETHODIMP KsMethod(PKSMETHOD, ULONG, LPVOID, ULONG, ULONG*) override;
    STDMETHODIMP KsEvent(PKSEVENT, ULONG, LPVOID, ULONG, ULONG*) override;

    CAMTURN_FORWARD_IMFATTRIBUTES(m_attributes)

    HRESULT Start(IMFMediaType* type);
    HRESULT Stop();
    void Shutdown();

  private:
    HRESULT FillSample(IMFSample* sample);

    Microsoft::WRL::Wrappers::SRWLock m_lock;
    Microsoft::WRL::ComPtr<IMFAttributes> m_attributes;
    Microsoft::WRL::ComPtr<IMFMediaEventQueue> m_queue;
    Microsoft::WRL::ComPtr<IMFStreamDescriptor> m_descriptor;
    Microsoft::WRL::ComPtr<IMFMediaSource> m_source; // weak in spirit; released on Shutdown to break the cycle
    MF_STREAM_STATE m_state = MF_STREAM_STATE_STOPPED;
    GUID m_subtype = GUID_NULL;
    UINT32 m_width = 0;
    UINT32 m_height = 0;
    UINT32 m_fps = 30;
    PipeClient m_pipe;
    std::vector<uint8_t> m_frame;
    std::vector<uint8_t> m_placeholder;
    bool m_shutdown = false;
};

} // namespace camturn
