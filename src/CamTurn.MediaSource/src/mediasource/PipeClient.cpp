#include "PipeClient.h"

#include "camturn/protocol.h"

#include <algorithm>

namespace camturn
{

PipeClient::~PipeClient()
{
    Stop();
}

void PipeClient::Start(uint32_t width, uint32_t height, uint32_t fps)
{
    Stop();
    m_width = width;
    m_height = height;
    m_fps = fps ? fps : 30;
    m_stop = false;
    {
        std::lock_guard<std::mutex> guard(m_lock);
        m_hasFrame = false;
        m_latest.assign(Nv12Size(width, height), 0);
    }
    m_wake = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    m_thread = std::thread([this] { Run(); });
}

void PipeClient::Stop()
{
    if (!m_thread.joinable())
    {
        return;
    }
    m_stop = true;
    if (m_wake)
    {
        SetEvent(m_wake);
    }
    // Unblock a ReadFile/WriteFile in progress on the worker thread.
    CancelSynchronousIo(static_cast<HANDLE>(m_thread.native_handle()));
    m_thread.join();
    if (m_wake)
    {
        CloseHandle(m_wake);
        m_wake = nullptr;
    }
}

bool PipeClient::TryCopyLatest(std::vector<uint8_t>& out, uint32_t maxAgeMs)
{
    std::lock_guard<std::mutex> guard(m_lock);
    if (!m_hasFrame || GetTickCount64() - m_latestTick > maxAgeMs)
    {
        return false;
    }
    out = m_latest;
    return true;
}

void PipeClient::Run()
{
    while (!m_stop)
    {
        HANDLE pipe = CreateFileW(kPipeName, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (pipe == INVALID_HANDLE_VALUE)
        {
            const DWORD err = GetLastError();
            if (err == ERROR_PIPE_BUSY)
            {
                WaitNamedPipeW(kPipeName, 500);
            }
            else
            {
                // App not running (ERROR_FILE_NOT_FOUND) or access denied: retry quietly.
                WaitForSingleObject(m_wake, 1000);
            }
            continue;
        }

        DWORD mode = PIPE_READMODE_BYTE;
        SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr);
        Trace(L"CamTurn: connected to app pipe (%ux%u@%u)", m_width, m_height, m_fps);
        Session(pipe);
        CloseHandle(pipe);
        Trace(L"CamTurn: pipe session ended");
        if (!m_stop)
        {
            WaitForSingleObject(m_wake, 250);
        }
    }
}

bool PipeClient::Session(HANDLE pipe)
{
    FrameHeader hello;
    hello.type = static_cast<uint32_t>(MessageType::Hello);
    hello.width = m_width;
    hello.height = m_height;
    hello.stride = 0;
    hello.payloadSize = 0;
    hello.param = m_fps;
    uint8_t buffer[kHeaderSize];
    Encode(hello, buffer);
    DWORD written = 0;
    if (!WriteFile(pipe, buffer, kHeaderSize, &written, nullptr) || written != kHeaderSize)
    {
        return false;
    }

    std::vector<uint8_t> payload;
    while (!m_stop)
    {
        if (!ReadExact(pipe, buffer, kHeaderSize))
        {
            return false;
        }
        FrameHeader h;
        if (Decode(buffer, h) != DecodeResult::Ok || h.type != static_cast<uint32_t>(MessageType::Frame))
        {
            Trace(L"CamTurn: protocol error, dropping connection");
            return false;
        }
        payload.resize(h.payloadSize);
        if (!ReadExact(pipe, payload.data(), payload.size()))
        {
            return false;
        }
        if (h.width != m_width || h.height != m_height)
        {
            continue; // app has not switched to our size yet
        }
        std::lock_guard<std::mutex> guard(m_lock);
        m_latest.swap(payload);
        m_latestTick = GetTickCount64();
        m_hasFrame = true;
    }
    return true;
}

bool PipeClient::ReadExact(HANDLE pipe, uint8_t* data, size_t size)
{
    size_t done = 0;
    while (done < size)
    {
        DWORD got = 0;
        const DWORD chunk = static_cast<DWORD>(std::min<size_t>(size - done, 1 << 20));
        if (!ReadFile(pipe, data + done, chunk, &got, nullptr) || got == 0)
        {
            return false;
        }
        done += got;
    }
    return true;
}

} // namespace camturn
