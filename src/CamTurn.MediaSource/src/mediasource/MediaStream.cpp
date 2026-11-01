#include "MediaStream.h"

#include "camturn/frames.h"
#include "camturn/protocol.h"

using Microsoft::WRL::ComPtr;

namespace camturn
{
namespace
{
struct Format
{
    GUID subtype;
    UINT32 width;
    UINT32 height;
};

// Offered formats. NV12 first: it is what most apps (browsers, Teams, Zoom) negotiate.
const Format kFormats[] = {
    {MFVideoFormat_NV12, 1280, 720},
    {MFVideoFormat_NV12, 1920, 1080},
    {MFVideoFormat_YUY2, 1280, 720},
    {MFVideoFormat_YUY2, 1920, 1080},
};

HRESULT CreateVideoType(const Format& f, IMFMediaType** result)
{
    ComPtr<IMFMediaType> type;
    CT_RETURN_IF_FAILED(MFCreateMediaType(&type));
    CT_RETURN_IF_FAILED(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
    CT_RETURN_IF_FAILED(type->SetGUID(MF_MT_SUBTYPE, f.subtype));
    CT_RETURN_IF_FAILED(MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, f.width, f.height));
    CT_RETURN_IF_FAILED(MFSetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, 30, 1));
    CT_RETURN_IF_FAILED(MFSetAttributeRatio(type.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
    CT_RETURN_IF_FAILED(type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
    CT_RETURN_IF_FAILED(type->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE));
    const bool yuy2 = f.subtype == MFVideoFormat_YUY2;
    CT_RETURN_IF_FAILED(type->SetUINT32(MF_MT_DEFAULT_STRIDE, yuy2 ? f.width * 2 : f.width));
    const UINT32 frameBytes = yuy2 ? f.width * f.height * 2 : f.width * f.height * 3 / 2;
    CT_RETURN_IF_FAILED(type->SetUINT32(MF_MT_SAMPLE_SIZE, frameBytes));
    CT_RETURN_IF_FAILED(type->SetUINT32(MF_MT_AVG_BITRATE, frameBytes * 8 * 30));
    *result = type.Detach();
    return S_OK;
}
} // namespace

HRESULT MediaStream::RuntimeClassInitialize(IMFMediaSource* source, DWORD id)
{
    m_source = source;
    CT_RETURN_IF_FAILED(MFCreateAttributes(&m_attributes, 8));
    CT_RETURN_IF_FAILED(m_attributes->SetGUID(MF_DEVICESTREAM_STREAM_CATEGORY, PINNAME_VIDEO_CAPTURE));
    CT_RETURN_IF_FAILED(m_attributes->SetUINT32(MF_DEVICESTREAM_STREAM_ID, id));
    CT_RETURN_IF_FAILED(m_attributes->SetUINT32(MF_DEVICESTREAM_FRAMESERVER_SHARED, 1));
    CT_RETURN_IF_FAILED(m_attributes->SetUINT32(MF_DEVICESTREAM_ATTRIBUTE_FRAMESOURCE_TYPES, MFFrameSourceTypes_Color));
    CT_RETURN_IF_FAILED(MFCreateEventQueue(&m_queue));

    IMFMediaType* types[ARRAYSIZE(kFormats)] = {};
    HRESULT hr = S_OK;
    for (size_t i = 0; i < ARRAYSIZE(kFormats) && SUCCEEDED(hr); i++)
    {
        hr = CreateVideoType(kFormats[i], &types[i]);
    }
    if (SUCCEEDED(hr))
    {
        hr = MFCreateStreamDescriptor(id, ARRAYSIZE(kFormats), types, &m_descriptor);
    }
    if (SUCCEEDED(hr))
    {
        ComPtr<IMFMediaTypeHandler> handler;
        hr = m_descriptor->GetMediaTypeHandler(&handler);
        if (SUCCEEDED(hr))
        {
            hr = handler->SetCurrentMediaType(types[0]);
        }
    }
    for (auto* t : types)
    {
        if (t)
        {
            t->Release();
        }
    }
    return hr;
}

HRESULT MediaStream::Start(IMFMediaType* type)
{
    auto lock = m_lock.LockExclusive();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);

    ComPtr<IMFMediaType> current = type;
    if (!current)
    {
        ComPtr<IMFMediaTypeHandler> handler;
        CT_RETURN_IF_FAILED(m_descriptor->GetMediaTypeHandler(&handler));
        CT_RETURN_IF_FAILED(handler->GetCurrentMediaType(&current));
    }
    CT_RETURN_IF_FAILED(current->GetGUID(MF_MT_SUBTYPE, &m_subtype));
    CT_RETURN_IF_FAILED(MFGetAttributeSize(current.Get(), MF_MT_FRAME_SIZE, &m_width, &m_height));
    UINT32 num = 30, den = 1;
    if (SUCCEEDED(MFGetAttributeRatio(current.Get(), MF_MT_FRAME_RATE, &num, &den)) && den != 0)
    {
        m_fps = num / den;
    }

    m_placeholder = MakePlaceholderNv12(m_width, m_height);
    m_frame.assign(m_placeholder.size(), 0);
    m_pipe.Start(m_width, m_height, m_fps);
    m_state = MF_STREAM_STATE_RUNNING;
    Trace(L"CamTurn: stream started %ux%u@%u", m_width, m_height, m_fps);
    return m_queue->QueueEventParamVar(MEStreamStarted, GUID_NULL, S_OK, nullptr);
}

HRESULT MediaStream::Stop()
{
    auto lock = m_lock.LockExclusive();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown);
    m_pipe.Stop(); // disconnecting tells the app the consumer has gone
    m_state = MF_STREAM_STATE_STOPPED;
    Trace(L"CamTurn: stream stopped");
    return m_queue->QueueEventParamVar(MEStreamStopped, GUID_NULL, S_OK, nullptr);
}

void MediaStream::Shutdown()
{
    auto lock = m_lock.LockExclusive();
    if (m_shutdown)
    {
        return;
    }
    m_shutdown = true;
    m_pipe.Stop();
    if (m_queue)
    {
        m_queue->Shutdown();
    }
    m_queue.Reset();
    m_descriptor.Reset();
    m_source.Reset();
}

STDMETHODIMP MediaStream::BeginGetEvent(IMFAsyncCallback* callback, IUnknown* state)
{
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, !m_queue);
    return m_queue->BeginGetEvent(callback, state);
}

STDMETHODIMP MediaStream::EndGetEvent(IMFAsyncResult* result, IMFMediaEvent** event)
{
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, !m_queue);
    return m_queue->EndGetEvent(result, event);
}

STDMETHODIMP MediaStream::GetEvent(DWORD flags, IMFMediaEvent** event)
{
    // Must not hold the lock: GetEvent can block until an event arrives.
    ComPtr<IMFMediaEventQueue> queue;
    {
        auto lock = m_lock.LockShared();
        CT_RETURN_HR_IF(MF_E_SHUTDOWN, !m_queue);
        queue = m_queue;
    }
    return queue->GetEvent(flags, event);
}

STDMETHODIMP MediaStream::QueueEvent(MediaEventType type, REFGUID extendedType, HRESULT status, const PROPVARIANT* data)
{
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, !m_queue);
    return m_queue->QueueEventParamVar(type, extendedType, status, data);
}

STDMETHODIMP MediaStream::GetMediaSource(IMFMediaSource** source)
{
    CT_RETURN_HR_IF(E_POINTER, !source);
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, !m_source);
    return m_source.CopyTo(source);
}

STDMETHODIMP MediaStream::GetStreamDescriptor(IMFStreamDescriptor** descriptor)
{
    CT_RETURN_HR_IF(E_POINTER, !descriptor);
    auto lock = m_lock.LockShared();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, !m_descriptor);
    return m_descriptor.CopyTo(descriptor);
}

HRESULT MediaStream::FillSample(IMFSample* sample)
{
    const bool yuy2 = m_subtype == MFVideoFormat_YUY2;
    if (!m_pipe.TryCopyLatest(m_frame))
    {
        m_frame = m_placeholder;
    }

    ComPtr<IMFMediaBuffer> buffer;
    CT_RETURN_IF_FAILED(MFCreate2DMediaBuffer(
        m_width, m_height, yuy2 ? MFVideoFormat_YUY2.Data1 : MFVideoFormat_NV12.Data1, FALSE, &buffer));
    ComPtr<IMF2DBuffer2> buffer2d;
    CT_RETURN_IF_FAILED(buffer.As(&buffer2d));
    BYTE* scanline0 = nullptr;
    BYTE* start = nullptr;
    LONG pitch = 0;
    DWORD length = 0;
    CT_RETURN_IF_FAILED(buffer2d->Lock2DSize(MF2DBuffer_LockFlags_Write, &scanline0, &pitch, &start, &length));
    if (pitch > 0)
    {
        if (yuy2)
        {
            Nv12ToYuy2(m_frame.data(), m_width, m_height, scanline0, pitch);
        }
        else
        {
            CopyNv12(m_frame.data(), m_width, m_height, scanline0, pitch);
        }
    }
    buffer2d->Unlock2D();
    CT_RETURN_IF_FAILED(buffer->SetCurrentLength(length));
    return sample->AddBuffer(buffer.Get());
}

STDMETHODIMP MediaStream::RequestSample(IUnknown* token)
{
    auto lock = m_lock.LockExclusive();
    CT_RETURN_HR_IF(MF_E_SHUTDOWN, m_shutdown || !m_queue);
    CT_RETURN_HR_IF(MF_E_INVALIDREQUEST, m_state != MF_STREAM_STATE_RUNNING);

    ComPtr<IMFSample> sample;
    CT_RETURN_IF_FAILED(MFCreateSample(&sample));
    CT_RETURN_IF_FAILED(FillSample(sample.Get()));
    CT_RETURN_IF_FAILED(sample->SetSampleTime(MFGetSystemTime()));
    CT_RETURN_IF_FAILED(sample->SetSampleDuration(10'000'000LL / (m_fps ? m_fps : 30)));
    if (token)
    {
        CT_RETURN_IF_FAILED(sample->SetUnknown(MFSampleExtension_Token, token));
    }
    return m_queue->QueueEventParamUnk(MEMediaSample, GUID_NULL, S_OK, sample.Get());
}

STDMETHODIMP MediaStream::SetStreamState(MF_STREAM_STATE state)
{
    MF_STREAM_STATE current;
    {
        auto lock = m_lock.LockShared();
        current = m_state;
    }
    if (state == current)
    {
        return S_OK;
    }
    switch (state)
    {
    case MF_STREAM_STATE_RUNNING:
        return Start(nullptr);
    case MF_STREAM_STATE_STOPPED:
        return Stop();
    case MF_STREAM_STATE_PAUSED: {
        auto lock = m_lock.LockExclusive();
        CT_RETURN_HR_IF(MF_E_INVALID_STATE_TRANSITION, m_state != MF_STREAM_STATE_RUNNING);
        m_state = state;
        return S_OK;
    }
    default:
        return MF_E_INVALID_STATE_TRANSITION;
    }
}

STDMETHODIMP MediaStream::GetStreamState(MF_STREAM_STATE* state)
{
    CT_RETURN_HR_IF(E_POINTER, !state);
    auto lock = m_lock.LockShared();
    *state = m_state;
    return S_OK;
}

STDMETHODIMP MediaStream::KsProperty(PKSPROPERTY, ULONG, LPVOID, ULONG, ULONG*)
{
    return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
}

STDMETHODIMP MediaStream::KsMethod(PKSMETHOD, ULONG, LPVOID, ULONG, ULONG*)
{
    return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
}

STDMETHODIMP MediaStream::KsEvent(PKSEVENT, ULONG, LPVOID, ULONG, ULONG*)
{
    return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);
}

} // namespace camturn
