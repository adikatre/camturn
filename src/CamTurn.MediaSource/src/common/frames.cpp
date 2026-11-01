#include "camturn/frames.h"
#include "camturn/protocol.h"

#include <algorithm>
#include <cstring>

namespace camturn
{
namespace
{
#include "glyphs.inc"

const uint8_t* FindGlyph(char c)
{
    if (c >= 'a' && c <= 'z')
    {
        c = static_cast<char>(c - 'a' + 'A');
    }
    for (const auto& g : kGlyphs)
    {
        if (g.ch == c)
        {
            return g.rows;
        }
    }
    return nullptr;
}

void FillY(uint8_t* y, uint32_t width, uint32_t height, int x0, int y0, int w, int h, uint8_t value)
{
    const int x1 = std::min<int>(static_cast<int>(width), x0 + w);
    const int y1 = std::min<int>(static_cast<int>(height), y0 + h);
    for (int row = std::max(0, y0); row < y1; row++)
    {
        for (int col = std::max(0, x0); col < x1; col++)
        {
            y[static_cast<size_t>(row) * width + col] = value;
        }
    }
}

void DrawText(uint8_t* y, uint32_t width, uint32_t height, const char* text, int centreY, int scale, uint8_t value)
{
    const int len = static_cast<int>(std::strlen(text));
    if (len == 0)
    {
        return;
    }
    // Shrink to fit 90% of the width.
    scale = std::max(1, std::min(scale, static_cast<int>(width) * 9 / 10 / (len * 6)));
    const int textWidth = len * 6 * scale - scale;
    int x = (static_cast<int>(width) - textWidth) / 2;
    const int top = centreY - (7 * scale) / 2;
    for (int i = 0; i < len; i++, x += 6 * scale)
    {
        const uint8_t* rows = FindGlyph(text[i]);
        if (!rows)
        {
            continue;
        }
        for (int r = 0; r < 7; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                if (rows[r] & (0x10 >> c))
                {
                    FillY(y, width, height, x + c * scale, top + r * scale, scale, scale, value);
                }
            }
        }
    }
}
} // namespace

void RenderPlaceholderNv12(uint8_t* nv12, uint32_t width, uint32_t height, const char* line1, const char* line2)
{
    uint8_t* y = nv12;
    uint8_t* uv = nv12 + static_cast<size_t>(width) * height;
    std::memset(y, 40, static_cast<size_t>(width) * height);
    std::memset(uv, 128, static_cast<size_t>(width) * height / 2);

    // Camera outline: a body rectangle with a lens, drawn as thick strokes.
    const int unit = std::max<int>(1, static_cast<int>(std::min(width, height)) / 60);
    const int bodyW = unit * 16, bodyH = unit * 10;
    const int bx = static_cast<int>(width) / 2 - bodyW / 2;
    const int by = static_cast<int>(height) / 2 - bodyH - unit * 4;
    const uint8_t fg = 200;
    FillY(y, width, height, bx, by, bodyW, unit, fg);
    FillY(y, width, height, bx, by + bodyH - unit, bodyW, unit, fg);
    FillY(y, width, height, bx, by, unit, bodyH, fg);
    FillY(y, width, height, bx + bodyW - unit, by, unit, bodyH, fg);
    FillY(y, width, height, bx + bodyW / 2 - unit * 2, by + bodyH / 2 - unit * 2, unit * 4, unit * 4, fg);
    FillY(y, width, height, bx + unit * 3, by - unit, unit * 4, unit, fg);

    const int scale = std::max(1, unit / 2 + 1);
    DrawText(y, width, height, line1, static_cast<int>(height) / 2 + unit * 3, scale + 1, 235);
    DrawText(y, width, height, line2, static_cast<int>(height) / 2 + unit * 3 + scale * 14, scale, 160);
}

std::vector<uint8_t> MakePlaceholderNv12(uint32_t width, uint32_t height)
{
    std::vector<uint8_t> buf(Nv12Size(width, height));
    RenderPlaceholderNv12(buf.data(), width, height, "CamTurn Camera", "Waiting for CamTurn to start the camera");
    return buf;
}

void CopyNv12(const uint8_t* src, uint32_t width, uint32_t height, uint8_t* dst, ptrdiff_t dstPitch)
{
    for (uint32_t row = 0; row < height; row++)
    {
        std::memcpy(dst + row * dstPitch, src + static_cast<size_t>(row) * width, width);
    }
    const uint8_t* srcUv = src + static_cast<size_t>(width) * height;
    uint8_t* dstUv = dst + dstPitch * height;
    for (uint32_t row = 0; row < height / 2; row++)
    {
        std::memcpy(dstUv + row * dstPitch, srcUv + static_cast<size_t>(row) * width, width);
    }
}

void Nv12ToYuy2(const uint8_t* src, uint32_t width, uint32_t height, uint8_t* dst, ptrdiff_t dstPitch)
{
    const uint8_t* srcUv = src + static_cast<size_t>(width) * height;
    for (uint32_t row = 0; row < height; row++)
    {
        const uint8_t* yRow = src + static_cast<size_t>(row) * width;
        const uint8_t* uvRow = srcUv + static_cast<size_t>(row / 2) * width;
        uint8_t* out = dst + row * dstPitch;
        for (uint32_t x = 0; x < width; x += 2)
        {
            out[x * 2 + 0] = yRow[x];
            out[x * 2 + 1] = uvRow[x];
            out[x * 2 + 2] = yRow[x + 1];
            out[x * 2 + 3] = uvRow[x + 1];
        }
    }
}

} // namespace camturn
