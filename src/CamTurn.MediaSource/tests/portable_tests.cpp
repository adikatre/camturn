// Portable tests for the media source's platform-independent code. Built and run on Linux (g++) and
// Windows (MSVC) by CI. No test framework: each CHECK failure prints and the process exits non-zero.
#include "camturn/frames.h"
#include "camturn/protocol.h"

#include <cstdio>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

static int g_failures = 0;
#define CHECK(cond)                                                                                                    \
    do                                                                                                                 \
    {                                                                                                                  \
        if (!(cond))                                                                                                   \
        {                                                                                                              \
            std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond);                                                \
            g_failures++;                                                                                              \
        }                                                                                                              \
    } while (0)

using namespace camturn;

static FrameHeader GoldenHeader()
{
    FrameHeader h;
    h.type = static_cast<uint32_t>(MessageType::Frame);
    h.width = 1280;
    h.height = 720;
    h.stride = 1280;
    h.payloadSize = static_cast<uint32_t>(Nv12Size(1280, 720));
    h.frameCounter = 0x0102030405060708ull;
    h.timestamp100ns = -1234567890123ll;
    h.flags = 3;
    h.param = 270;
    return h;
}

static void TestEncodeMatchesGoldenFile(const std::string& fixture)
{
    std::ifstream f(fixture, std::ios::binary);
    CHECK(f.good());
    std::vector<uint8_t> golden((std::istreambuf_iterator<char>(f)), std::istreambuf_iterator<char>());
    CHECK(golden.size() == kHeaderSize);
    if (golden.size() != kHeaderSize)
    {
        return;
    }

    uint8_t encoded[kHeaderSize];
    Encode(GoldenHeader(), encoded);
    CHECK(std::memcmp(encoded, golden.data(), kHeaderSize) == 0);

    FrameHeader decoded;
    CHECK(Decode(golden.data(), decoded) == DecodeResult::Ok);
    CHECK(decoded.width == 1280 && decoded.height == 720);
    CHECK(decoded.frameCounter == 0x0102030405060708ull);
    CHECK(decoded.timestamp100ns == -1234567890123ll);
    CHECK(decoded.param == 270 && decoded.flags == 3);
}

static void TestDecodeRejectsBadInput()
{
    uint8_t buf[kHeaderSize];
    FrameHeader h = GoldenHeader();
    FrameHeader out;

    Encode(h, buf);
    buf[0] ^= 0xFF;
    CHECK(Decode(buf, out) == DecodeResult::BadMagic);

    h = GoldenHeader();
    h.version = 2;
    Encode(h, buf);
    CHECK(Decode(buf, out) == DecodeResult::UnsupportedVersion);

    h = GoldenHeader();
    h.width = 1281;
    Encode(h, buf);
    CHECK(Decode(buf, out) == DecodeResult::BadDimensions);

    h = GoldenHeader();
    h.width = 8192;
    Encode(h, buf);
    CHECK(Decode(buf, out) == DecodeResult::BadDimensions);

    h = GoldenHeader();
    h.payloadSize += 1;
    Encode(h, buf);
    CHECK(Decode(buf, out) == DecodeResult::BadPayloadSize);

    FrameHeader hello;
    hello.type = static_cast<uint32_t>(MessageType::Hello);
    hello.width = 1920;
    hello.height = 1080;
    hello.param = 30;
    Encode(hello, buf);
    CHECK(Decode(buf, out) == DecodeResult::Ok);
    hello.payloadSize = 10;
    Encode(hello, buf);
    CHECK(Decode(buf, out) == DecodeResult::BadPayloadSize);
}

static void TestPlaceholderHasContentAndNeutralChroma()
{
    for (auto [w, hgt] : {std::pair<uint32_t, uint32_t>{1280, 720}, {1920, 1080}, {640, 360}})
    {
        auto frame = MakePlaceholderNv12(w, hgt);
        CHECK(frame.size() == Nv12Size(w, hgt));
        size_t bright = 0;
        for (size_t i = 0; i < static_cast<size_t>(w) * hgt; i++)
        {
            bright += frame[i] > 150 ? 1 : 0;
        }
        CHECK(bright > static_cast<size_t>(w) * hgt / 400); // icon and text drawn
        CHECK(bright < static_cast<size_t>(w) * hgt / 4);   // mostly background
        bool neutral = true;
        for (size_t i = static_cast<size_t>(w) * hgt; i < frame.size(); i++)
        {
            neutral = neutral && frame[i] == 128;
        }
        CHECK(neutral);
    }
}

static void TestCopyAndYuy2()
{
    const uint32_t w = 4, h = 2;
    std::vector<uint8_t> nv12 = {10, 11, 12, 13, 20, 21, 22, 23, /* UV */ 100, 200, 101, 201};
    std::vector<uint8_t> padded(8 * h * 3 / 2 + 8, 0);
    CopyNv12(nv12.data(), w, h, padded.data(), 8);
    CHECK(padded[0] == 10 && padded[3] == 13 && padded[8] == 20 && padded[16] == 100 && padded[19] == 201);

    std::vector<uint8_t> yuy2(w * 2 * h);
    Nv12ToYuy2(nv12.data(), w, h, yuy2.data(), w * 2);
    const std::vector<uint8_t> expected = {10, 100, 11, 200, 12, 101, 13, 201, 20, 100, 21, 200, 22, 101, 23, 201};
    CHECK(yuy2 == expected);
}

int main(int argc, char** argv)
{
    const std::string fixture = argc > 1 ? argv[1] : "tests/fixtures/frame-header-v1.bin";
    TestEncodeMatchesGoldenFile(fixture);
    TestDecodeRejectsBadInput();
    TestPlaceholderHasContentAndNeutralChroma();
    TestCopyAndYuy2();
    if (g_failures == 0)
    {
        std::printf("portable_tests: all passed\n");
    }
    return g_failures == 0 ? 0 : 1;
}
