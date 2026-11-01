// CamTurn frame protocol, version 1. Portable (no Windows headers) so it can be unit-tested anywhere.
// Must stay byte-for-byte identical to src/CamTurn.Core/Video/FrameProtocol.cs.
//
// Transport: the tray app hosts the named pipe \\.\pipe\CamTurn.Video.v1 (byte mode). The media source,
// running inside Windows Frame Server, connects as a client when an app starts the virtual camera and
// disconnects when it stops; that connection is the "consumer started/stopped" signal.
//
//   client -> server : Hello  (header only; width/height/param = requested size and frame rate)
//   server -> client : Frame* (header + payload of payloadSize bytes, NV12)
//
// All fields are little-endian.
#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace camturn
{

constexpr uint32_t kMagic = 0x4E525443; // "CTRN" as bytes 'C','T','R','N'
constexpr uint16_t kProtocolVersion = 1;
constexpr uint16_t kHeaderSize = 64;
constexpr uint32_t kFourccNv12 = 0x3231564E; // 'N','V','1','2'
constexpr const wchar_t* kPipeName = L"\\\\.\\pipe\\CamTurn.Video.v1";
constexpr uint32_t kMaxWidth = 3840;
constexpr uint32_t kMaxHeight = 2160;

enum class MessageType : uint32_t
{
    Hello = 1,
    Frame = 2,
};

enum FrameFlags : uint32_t
{
    FrameFlagNone = 0,
    FrameFlagMirrored = 1u << 0,
    FrameFlagHolding = 1u << 1, // orientation held because the device is flat
};

struct FrameHeader
{
    uint32_t magic = kMagic;
    uint16_t version = kProtocolVersion;
    uint16_t headerSize = kHeaderSize;
    uint32_t type = static_cast<uint32_t>(MessageType::Frame);
    uint32_t width = 0;
    uint32_t height = 0;
    uint32_t stride = 0;
    uint32_t pixelFormat = kFourccNv12;
    uint32_t payloadSize = 0;
    uint64_t frameCounter = 0;
    int64_t timestamp100ns = 0;
    uint32_t flags = 0;
    uint32_t param = 0; // Hello: frames per second. Frame: applied rotation in degrees.
    uint64_t reserved = 0;
};

static_assert(sizeof(FrameHeader) == kHeaderSize, "FrameHeader must be 64 bytes");
static_assert(offsetof(FrameHeader, frameCounter) == 32, "layout");
static_assert(offsetof(FrameHeader, timestamp100ns) == 40, "layout");
static_assert(offsetof(FrameHeader, param) == 52, "layout");

inline size_t Nv12Size(uint32_t width, uint32_t height)
{
    return static_cast<size_t>(width) * height * 3 / 2;
}

namespace detail
{
template <typename T> inline void Put(uint8_t* p, T v)
{
    for (size_t i = 0; i < sizeof(T); i++)
    {
        p[i] = static_cast<uint8_t>(static_cast<uint64_t>(v) >> (8 * i));
    }
}

template <typename T> inline T Get(const uint8_t* p)
{
    uint64_t v = 0;
    for (size_t i = 0; i < sizeof(T); i++)
    {
        v |= static_cast<uint64_t>(p[i]) << (8 * i);
    }
    return static_cast<T>(v);
}
} // namespace detail

// Explicit little-endian encoding so the wire format never depends on struct packing or host byte order.
inline void Encode(const FrameHeader& h, uint8_t out[kHeaderSize])
{
    using detail::Put;
    std::memset(out, 0, kHeaderSize);
    Put(out + 0, h.magic);
    Put(out + 4, h.version);
    Put(out + 6, h.headerSize);
    Put(out + 8, h.type);
    Put(out + 12, h.width);
    Put(out + 16, h.height);
    Put(out + 20, h.stride);
    Put(out + 24, h.pixelFormat);
    Put(out + 28, h.payloadSize);
    Put(out + 32, h.frameCounter);
    Put(out + 40, h.timestamp100ns);
    Put(out + 48, h.flags);
    Put(out + 52, h.param);
    Put(out + 56, h.reserved);
}

enum class DecodeResult
{
    Ok,
    BadMagic,
    UnsupportedVersion,
    BadHeaderSize,
    BadDimensions,
    BadPayloadSize,
};

// Validates everything a reader must check before trusting the payload size.
inline DecodeResult Decode(const uint8_t in[kHeaderSize], FrameHeader& h)
{
    using detail::Get;
    h.magic = Get<uint32_t>(in + 0);
    h.version = Get<uint16_t>(in + 4);
    h.headerSize = Get<uint16_t>(in + 6);
    h.type = Get<uint32_t>(in + 8);
    h.width = Get<uint32_t>(in + 12);
    h.height = Get<uint32_t>(in + 16);
    h.stride = Get<uint32_t>(in + 20);
    h.pixelFormat = Get<uint32_t>(in + 24);
    h.payloadSize = Get<uint32_t>(in + 28);
    h.frameCounter = Get<uint64_t>(in + 32);
    h.timestamp100ns = Get<int64_t>(in + 40);
    h.flags = Get<uint32_t>(in + 48);
    h.param = Get<uint32_t>(in + 52);
    h.reserved = Get<uint64_t>(in + 56);

    if (h.magic != kMagic)
    {
        return DecodeResult::BadMagic;
    }
    if (h.version != kProtocolVersion)
    {
        return DecodeResult::UnsupportedVersion;
    }
    if (h.headerSize != kHeaderSize)
    {
        return DecodeResult::BadHeaderSize;
    }
    if (h.width > kMaxWidth || h.height > kMaxHeight || (h.width % 2) != 0 || (h.height % 2) != 0)
    {
        return DecodeResult::BadDimensions;
    }
    if (h.type == static_cast<uint32_t>(MessageType::Frame))
    {
        if (h.pixelFormat != kFourccNv12 || h.stride != h.width || h.payloadSize != Nv12Size(h.width, h.height))
        {
            return DecodeResult::BadPayloadSize;
        }
    }
    else if (h.payloadSize != 0)
    {
        return DecodeResult::BadPayloadSize;
    }
    return DecodeResult::Ok;
}

} // namespace camturn
