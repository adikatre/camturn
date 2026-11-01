// Portable frame helpers used by the media source: the placeholder frame and NV12 -> YUY2 conversion.
#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace camturn
{

// Fills a tightly packed NV12 buffer (stride == width) with the "waiting" placeholder: a dark background,
// a simple camera outline and two centred text lines (upper-case ASCII; unknown characters render blank).
void RenderPlaceholderNv12(uint8_t* nv12, uint32_t width, uint32_t height, const char* line1, const char* line2);

// Convenience wrapper that allocates.
std::vector<uint8_t> MakePlaceholderNv12(uint32_t width, uint32_t height);

// Copies a packed NV12 image into a destination with arbitrary pitches (as returned by IMF2DBuffer2::Lock2DSize).
// The destination's UV plane starts at dst + dstPitch * height.
void CopyNv12(const uint8_t* src, uint32_t width, uint32_t height, uint8_t* dst, ptrdiff_t dstPitch);

// Converts packed NV12 to YUY2 (Y0 U Y1 V) with the given destination pitch in bytes.
void Nv12ToYuy2(const uint8_t* src, uint32_t width, uint32_t height, uint8_t* dst, ptrdiff_t dstPitch);

} // namespace camturn
