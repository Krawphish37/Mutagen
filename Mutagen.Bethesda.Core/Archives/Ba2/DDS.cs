using System.Runtime.InteropServices;

namespace Mutagen.Bethesda.Archives.Ba2;
/*
 * Copied from https://raw.githubusercontent.com/AlexxEG/BSA_Browser/master/Sharp.BSA.BA2/BA2Util/DDS.cs
 * which is also GPL3 code. Modified slightly for Wabbajack
 * 
 */

/* 
 * Copied from dds.h. Includes (almost) only stuff I need in this project.
 * 
 * Link: https://github.com/digitalutopia1/BA2Lib/blob/master/BA2Lib/dds.h
 * 
 */

class DDS
{
    public static uint HeaderSizeForFormat(DXGI_FORMAT fmt)
    {
        return UsesDxt10Header(fmt) ? DDS_HEADER_DXT10.Size + DDS_HEADER.Size : DDS_HEADER.Size;
    }

    /// <summary>
    /// The formats a legacy DDS header (a FourCC or bit masks) describes; every other format is written with the DX10
    /// extended header, which names the DXGI format itself
    /// </summary>
    public static bool UsesDxt10Header(DXGI_FORMAT fmt)
    {
        switch (fmt)
        {
            case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8X8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8_UNORM:
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// The size of the top mip of a texture of a DXGI format, for a DDS header's linear size: blocks of 4x4 pixels for
    /// the block-compressed formats, bits per pixel for the others (width x height for a format not listed)
    /// </summary>
    public static uint LinearSize(int format, uint width, uint height)
    {
        uint blockBytes = format switch
        {
            >= 70 and <= 72 or >= 79 and <= 81 => 8, // BC1, BC4
            >= 73 and <= 78 or >= 82 and <= 84 or >= 94 and <= 99 => 16, // BC2, BC3, BC5, BC6H, BC7
            _ => 0,
        };
        if (blockBytes != 0)
        {
            return Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockBytes;
        }
        uint bits = format switch
        {
            >= 1 and <= 4 => 128, // R32G32B32A32
            >= 5 and <= 8 => 96, // R32G32B32
            >= 9 and <= 22 => 64, // R16G16B16A16, R32G32, R32G8X24
            >= 23 and <= 47 => 32, // R10G10B10A2, R11G11B10, R8G8B8A8, R16G16, R32, R24G8
            >= 48 and <= 59 => 16, // R8G8, R16
            >= 60 and <= 65 => 8, // R8, A8
            >= 67 and <= 69 or >= 87 and <= 93 => 32, // R9G9B9E5, R8G8_B8G8, B8G8R8A8, B8G8R8X8, R10G10B10_XR
            85 or 86 or 115 => 16, // B5G6R5, B5G5R5A1, B4G4R4A4
            _ => 8,
        };
        return width * height * bits / 8;
    }

    public const int DDS_MAGIC = 0x20534444; // "DDS "

    public static uint MAKEFOURCC(char ch0, char ch1, char ch2, char ch3)
    {
        // This is alien to me...
        return ((uint)(byte)(ch0) | ((uint)(byte)(ch1) << 8) | ((uint)(byte)(ch2) << 16 | ((uint)(byte)(ch3) << 24)));
    }

    public const int DDS_FOURCC = 0x00000004; // DDPF_FOURCC
    public const int DDS_RGB = 0x00000040; // DDPF_RGB
    public const int DDS_RGBA = 0x00000041; // DDPF_RGB | DDPF_ALPHAPIXELS

    public const int DDS_HEADER_FLAGS_TEXTURE = 0x00001007; // DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT
    public const int DDS_HEADER_FLAGS_MIPMAP = 0x00020000; // DDSD_MIPMAPCOUNT
    public const int DDS_HEADER_FLAGS_LINEARSIZE = 0x00080000; // DDSD_LINEARSIZE

    public const int DDS_SURFACE_FLAGS_TEXTURE = 0x00001000; // DDSCAPS_TEXTURE
    public const int DDS_SURFACE_FLAGS_MIPMAP = 0x00400008; // DDSCAPS_COMPLEX | DDSCAPS_MIPMAP

    public const int DDS_ALPHA_MODE_UNKNOWN = 0x0;
}

#region dxgiformat.h

enum DXGI_FORMAT
{
    DXGI_FORMAT_R8G8B8A8_UNORM = 28,
    DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29,
    DXGI_FORMAT_R8_UNORM = 61,
    DXGI_FORMAT_BC1_UNORM = 71,
    DXGI_FORMAT_BC1_UNORM_SRGB = 72,
    DXGI_FORMAT_BC2_UNORM = 74,
    DXGI_FORMAT_BC3_UNORM = 77,
    DXGI_FORMAT_BC3_UNORM_SRGB = 78,
    DXGI_FORMAT_BC4_UNORM = 80,
    DXGI_FORMAT_BC5_UNORM = 83,
    DXGI_FORMAT_BC5_SNORM = 84,
    DXGI_FORMAT_B8G8R8A8_UNORM = 87,
    DXGI_FORMAT_B8G8R8X8_UNORM = 88,
    DXGI_FORMAT_BC6H_UF16 = 95,
    DXGI_FORMAT_BC7_UNORM = 98,
    DXGI_FORMAT_BC7_UNORM_SRGB = 99
}

#endregion

enum DXT10_RESOURCE_DIMENSION
{
    DIMENSION_TEXTURE1D = 2,
    DIMENSION_TEXTURE2D = 3,
    DIMENSION_TEXTURE3D = 4,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct DDS_HEADER
{
    public uint dwSize;
    public uint dwHeaderFlags;
    public uint dwHeight;
    public uint dwWidth;
    public uint dwPitchOrLinearSize;
    public uint dwDepth; // only if DDS_HEADER_FLAGS_VOLUME is set in dwHeaderFlags
    public uint dwMipMapCount;
    public uint dwReserved1; // [11]
    public DDS_PIXELFORMAT PixelFormat; // ddspf
    public uint dwSurfaceFlags;
    public uint dwCubemapFlags;
    public uint dwReserved2; // [3]

    public uint GetSize()
    {
        // 9 uint + DDS_PIXELFORMAT uints + 2 uint arrays with 14 uints total
        // each uint 4 bytes each
        return (9 * 4) + PixelFormat.GetSize() + (14 * 4);
    }

    public void Write(BinaryWriter bw)
    {
        bw.Write(dwSize);
        bw.Write(dwHeaderFlags);
        bw.Write(dwHeight);
        bw.Write(dwWidth);
        bw.Write(dwPitchOrLinearSize);
        bw.Write(dwDepth);
        bw.Write(dwMipMapCount);

        // Just write it multiple times, since it's never assigned a value anyway
        for (int i = 0; i < 11; i++)
            bw.Write(dwReserved1);

        // DDS_PIXELFORMAT
        bw.Write(PixelFormat.dwSize);
        bw.Write(PixelFormat.dwFlags);
        bw.Write(PixelFormat.dwFourCC);
        bw.Write(PixelFormat.dwRGBBitCount);
        bw.Write(PixelFormat.dwRBitMask);
        bw.Write(PixelFormat.dwGBitMask);
        bw.Write(PixelFormat.dwBBitMask);
        bw.Write(PixelFormat.dwABitMask);

        bw.Write(dwSurfaceFlags);
        bw.Write(dwCubemapFlags);

        // Just write it multiple times, since it's never assigned a value anyway
        for (int i = 0; i < 3; i++)
            bw.Write(dwReserved2);
    }

    public static uint Size
    {
        get
        {
            unsafe
            {
                return (uint)(sizeof(DDS_HEADER) + (sizeof(int) * 10) + (sizeof(int) * 2));
            };
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct DDS_HEADER_DXT10
{
    public uint dxgiFormat;
    public uint resourceDimension;
    public uint miscFlag;
    public uint arraySize;
    public uint miscFlags2;

    public void Write(BinaryWriter bw)
    {
        bw.Write(dxgiFormat);
        bw.Write(resourceDimension);
        bw.Write(miscFlag);
        bw.Write(arraySize);
        bw.Write(miscFlags2);
    }

    public static uint Size
    {
        get
        {
            unsafe
            {
                return (uint)sizeof(DDS_HEADER_DXT10);
            };
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
unsafe struct DDS_PIXELFORMAT
{
    public uint dwSize;
    public uint dwFlags;
    public uint dwFourCC;
    public uint dwRGBBitCount;
    public uint dwRBitMask;
    public uint dwGBitMask;
    public uint dwBBitMask;
    public uint dwABitMask;

    public DDS_PIXELFORMAT(uint size, uint flags, uint fourCC, uint rgbBitCount, uint rBitMask, uint gBitMask, uint bBitMask, uint aBitMask)
    {
        dwSize = size;
        dwFlags = flags;
        dwFourCC = fourCC;
        dwRGBBitCount = rgbBitCount;
        dwRBitMask = rBitMask;
        dwGBitMask = gBitMask;
        dwBBitMask = bBitMask;
        dwABitMask = aBitMask;
    }

    public uint GetSize()
    {
        // 8 uints, each 4 bytes each
        return 8 * 4;
    }
}