// ats_preview.cpp -- draw one frame of the in-game ATS mark to a BMP, outside the game.
//
// The mark is composed by hand into a premultiplied DIB and then AlphaBlended, and every way
// of getting that wrong (GDI text leaving alpha at zero, a premultiply applied twice, a
// caption measured against a font that was never selected) shows up as "nothing there" or
// "a grey box" rather than as an error. Cheaper to look at it here than to start a client.
//
//   cl /nologo /utf-8 /MT /O2 /EHsc /DWIN32 tools\ats_preview.cpp ^
//      native\ddraw_inproc\src\ats.cpp native\ddraw_inproc\src\log.cpp ^
//      /Fetools\ats_preview.exe /link user32.lib gdi32.lib msimg32.lib
#define WIN32_LEAN_AND_MEAN
#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>

void AtsPublish();
void AtsDraw(HDC dst, int cw, int ch);

int main(int argc, char** argv)
{
    int cw = argc > 1 ? atoi(argv[1]) : 1280;
    int ch = argc > 2 ? atoi(argv[2]) : 960;
    const char* out = argc > 3 ? argv[3] : "tools\\ats_preview.bmp";

    AtsPublish();

    // The DLL publishes the block switched off; the launcher is what turns it on.
    char name[64];
    wsprintfA(name, "Local\\l38ats_%lu", GetCurrentProcessId());
    HANDLE map = OpenFileMappingA(FILE_MAP_ALL_ACCESS, FALSE, name);
    if (!map) { printf("no shared block\n"); return 1; }
    BYTE* shared = (BYTE*)MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, 12);
    if (!shared) { printf("no view\n"); return 1; }
    shared[8] = 1;

    BITMAPINFO bmi;
    ZeroMemory(&bmi, sizeof(bmi));
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = cw;
    bmi.bmiHeader.biHeight = -ch;
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    BYTE* bits = NULL;
    HBITMAP bmp = CreateDIBSection(NULL, &bmi, DIB_RGB_COLORS, (void**)&bits, NULL, 0);
    HDC dc = CreateCompatibleDC(NULL);
    HBITMAP old = (HBITMAP)SelectObject(dc, bmp);

    // Mid grey, so both a light mark and its dark shadow are visible against it.
    HBRUSH back = CreateSolidBrush(RGB(72, 84, 96));
    RECT all = { 0, 0, cw, ch };
    FillRect(dc, &all, back);
    DeleteObject(back);

    AtsDraw(dc, cw, ch);
    GdiFlush();

    BITMAPFILEHEADER file;
    ZeroMemory(&file, sizeof(file));
    file.bfType = 0x4D42;
    file.bfOffBits = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER);
    file.bfSize = file.bfOffBits + (DWORD)cw * ch * 4;

    FILE* f = fopen(out, "wb");
    if (!f) { printf("cannot write %s\n", out); return 1; }
    fwrite(&file, sizeof(file), 1, f);
    fwrite(&bmi.bmiHeader, sizeof(BITMAPINFOHEADER), 1, f);
    fwrite(bits, 1, (size_t)cw * ch * 4, f);
    fclose(f);

    SelectObject(dc, old);
    DeleteDC(dc);
    DeleteObject(bmp);
    printf("wrote %s (%dx%d)\n", out, cw, ch);
    return 0;
}
