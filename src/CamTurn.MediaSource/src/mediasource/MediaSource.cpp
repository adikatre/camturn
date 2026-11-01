#include "MediaSource.h"

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::MakeAndInitialize;

namespace camturn
{

HRESULT MediaSource::RuntimeClassInitialize(IMFAttributes* activatorAttributes)
{
    // Keep construction cheap: Frame Server also instantiates sources just to publish sensor groups.
    CT_RETURN_IF_FAILED(MFCreateAttributes(&m_attributes, 8));
    if (activatorAttributes)
    {
        CT_RETURN_IF_FAILED(activatorAttributes->CopyAllItems(m_attributes.Get()));
    }

    // Camera profiles: a single legacy profile covering every offered format.
    ComPtr<IMFSensorProfileCollection> profiles;
    ComPtr<IMFSensorProfile> legacy;
    if (SUCCEEDED(MFCreateSensorProfileCollection(&profiles)) &&
        SUCCEEDED(MFCreateSensorProfile(KSCAMERAPROFILE_Legacy, 0, nullptr, &legacy)) &&
        SUCCEEDED(legacy->AddProfileFilter(0, L"((RES==;FRT<=30,1;SUT==))")) &&
        SUCCEEDED(profiles->AddProfile(legacy.Get())))
    {
        m_attributes->SetUnknown(MF_DEVICEMFT_SENSORPROFILE_COLLECTION, profiles.Get());
    }

    CT_RETURN_IF_FAILED(MakeAndInitialize<MediaStream>(&m_stream, static_cast<IMFMediaSource*>(this), 0));
    ComPtr<IMFStreamDescriptor> streamDescriptor;
    CT_RETURN_IF_FAILED(m_stream->GetStreamDescriptor(&streamDescriptor));
    IMFStreamDescriptor* descriptors[] = {streamDescriptor.Get()};
    CT_RETURN_IF_FAILED(MFCreatePresentationDescriptor(1, descriptors, &m_descriptor));
    CT_RETURN_IF_FAILED(MFCreateEventQueue(&m_queue));
    return S_OK;
}

STDMETHODIMP MediaSource::BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state)
{
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
    return m_queue->BeginGetEvent(callback, state);
}

STDMETHODIMP MediaSource::EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event)
{
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
    return m_queue->EndGetEvent(result, event);
}

STDMETHODIMP MediaSource::GetEvent(DWORD flags, IMFMediaEvent** event)
{
    ComPtr<IMFMediaEventQueue> queue;
    {
        auto lock = m_lock.LockShared();
        CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
        queue = m_queue;
    }
    return queue->GetEvent(flags, event);
}

STDMETHODIMP MediaSource::QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status,
                                     const PROPVARIANT* value)
{
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
    return m_queue->QueueEventParamVar(type, extendedType, status, value);
}

STDMETHODIMP MediaSource::CreatePresentationDescriptor(IMFPresentationDescriptor** descriptor)
{
    CT_RETURN_HR_IF(E_POINTER, !descriptor);
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
    return m_descriptor->Clone(descriptor);
}

STDMETHODIMP MediaSource::GetCharacteristics(DWORD* characteristics)
{
    CT_RETURN_HR_IF(E_POINTER, !characteristics);
    *characteristics = MFMEDIASOURCE_IS_LIVE;
    return S_OK;
}

STDMETHODIMP MediaSource::Pause()
{
    return MF_E_INVALID_STATE_TRANSITION;
}

STDMETHODIMP MediaSource::Start(IMFPresentationDescriptor* descriptor, const GUID* timeFormat,
                                const PROPVARIANT* startPosition)
{
    CT_RETURN_HR_IF(E_INVALIDARG, !descriptor || !startPosition);
    CT_RETURN_HR_IF(MF_E_UNSUPPORTED_TIME_FORMAT, timeFormat && *timeFormat != GUID_NULL);

    ComPtr<IMFStreamDescriptor> requested;
    BOOL selected = FALSE;
    {
        auto lock = m_lock.LockExclusive();
        CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
        DWORD count = 0;
        CT_RETURN_IF_FAILED(descriptor->GetStreamDescriptorCount(&count));
        CT_RETURN_HR_IF(E_INVALIDARG, count != 1);
        CT_RETURN_IF_FAILED(descriptor->GetStreamDescriptorByIndex(0, &selected, &requested));
        DWORD id = 0;
        CT_RETURN_IF_FAILED(requested->GetStreamIdentifier(&id));
        CT_RETURN_HR_IF(MF_E_INVALIDSTREAMNUMBER, id != 0);
    }

    PROPVARIANT time;
    CT_RETURN_IF_FAILED(InitPropVariantFromInt64(MFGetSystemTime(), &time));

    if (selected)
    {
        ComPtr<IMFMediaTypeHandler> handler;
        ComPtr<IMFMediaType> type;
        CT_RETURN_IF_FAILED(requested->GetMediaTypeHandler(&handler));
        CT_RETURN_IF_FAILED(handler->GetCurrentMediaType(&type));

        bool wasSelected;
        {
            auto lock = m_lock.LockExclusive();
            CT_RETURN_IF_FAILED(m_descriptor->SelectStream(0));
            wasSelected = m_streamWasSelected;
            m_streamWasSelected = true;
        }
        ComPtr<IUnknown> unknown;
        CT_RETURN_IF_FAILED(m_stream.As(&unknown));
        CT_RETURN_IF_FAILED(
            m_queue->QueueEventParamUnk(wasSelected ? MEUpdatedStream : MENewStream, GUID_NULL, S_OK, unknown.Get()));
        CT_RETURN_IF_FAILED(m_stream->Start(type.Get())); // posts MEStreamStarted
    }
    else
    {
        auto lock = m_lock.LockExclusive();
        m_descriptor->DeselectStream(0);
    }

    return m_queue->QueueEventParamVar(MESourceStarted, GUID_NULL, S_OK, &time);
}

STDMETHODIMP MediaSource::Stop()
{
    {
        auto lock = m_lock.LockShared();
        CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
    }
    PROPVARIANT time;
    CT_RETURN_IF_FAILED(InitPropVariantFromInt64(MFGetSystemTime(), &time));
    MF_STREAM_STATE state = MF_STREAM_STATE_STOPPED;
    if (SUCCEEDED(m_stream->GetStreamState(&state)) && state != MF_STREAM_STATE_STOPPED)
    {
        CT_RETURN_IF_FAILED(m_stream->Stop());
    }
    {
        auto lock = m_lock.LockExclusive();
        m_descriptor->DeselectStream(0);
    }
    return m_queue->QueueEventParamVar(MESourceStopped, GUID_NULL, S_OK, &time);
}

STDMETHODIMP MediaSource::Shutdown()
{
    ComPtr<MediaStream> stream;
    {
        auto lock = m_lock.LockExclusive();
        CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
        m_shutdown = true;
        stream = m_stream;
        m_queue->Shutdown();
    }
    if (stream)
    {
        stream->Shutdown();
    }
    Trace(L"CamTurn: source shut down");
    return S_OK;
}

STDMETHODIMP MediaSource::GetSourceAttributes(IMFAttributes** attributes)
{
    CT_RETURN_HR_IF(E_POINTER, !attributes);
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
    return m_attributes.CopyTo(attributes);
}

STDMETHODIMP MediaSource::GetStreamAttributes(DWORD streamId, IMFAttributes** attributes)
{
    CT_RETURN_HR_IF(E_POINTER, !attributes);
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
    CT_RETURN_HR_IF(MF_E_INVALIDSTREAMNUMBER, streamId != 0);
    return m_stream.CopyTo(attributes);
}

STDMETHODIMP MediaSource::SetD3DManager(IUnknown*)
{
    // Samples are always system memory (MFCreate2DMediaBuffer); the pipeline uploads them if it needs GPU surfaces.
    return S_OK;
}

STDMETHODIMP MediaSource::GetService(REFGUID, REFIID, LPVOID* object)
{
    if (object)
    {
        *object = nullptr;
    }
    return MF_E_UNSUPPORTED_SERVICE;
}

STDMETHODIMP MediaSource::KsProperty(PKSPROPERTY, ULONG, LPVOID, ULONG, ULONG*)
{
    return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
}

STDMETHODIMP MediaSource::KsMethod(PKSMETHOD, ULONG, LPVOID, ULONG, ULONG*)
{
    return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
}

STDMETHODIMP MediaSource::KsEvent(PKSEVENT, ULONG, LPVOID, ULONG, ULONG*)
{
    return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
}

} // namespace camturn
