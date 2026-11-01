#pragma once

#include "../common/win.h"

#include <atomic>
#include <cstdint>
#include <mutex>
#include <thread>
#include <vector>

namespace camturn
{

// Connects to the CamTurn app's frame pipe on a background thread and keeps the most recent frame.
// Never blocks the caller: if the app is not running, the pipe is missing, or frames stop arriving,
// TryCopyLatest() simply returns false and the stream serves the placeholder instead.
class PipeClient
{
  public:
    PipeClient() = default;
    ~PipeClient();
    PipeClient(const PipeClient&) = delete;
    PipeClient& operator=(const PipeClient&) = delete;

    void Start(uint32_t width, uint32_t height, uint32_t fps);
    void Stop();

    // Copies the latest frame (packed NV12, width x height) if one arrived within maxAgeMs.
    bool TryCopyLatest(std::vector<uint8_t>& out, uint32_t maxAgeMs = 1000);

  private:
    void Run();
    bool Session(HANDLE pipe);
    bool ReadExact(HANDLE pipe, uint8_t* buffer, size_t size);

    std::thread m_thread;
    std::atomic<bool> m_stop{false};
    HANDLE m_wake = nullptr;
    uint32_t m_width = 0;
    uint32_t m_height = 0;
    uint32_t m_fps = 30;

    std::mutex m_lock;
    std::vector<uint8_t> m_latest;
    ULONGLONG m_latestTick = 0;
    bool m_hasFrame = false;
};

} // namespace camturn
