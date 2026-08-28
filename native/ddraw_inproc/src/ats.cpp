// ats.cpp —— 自動狩獵標記(經典服 ATS),畫進遊戲自己的 backbuffer
//
// 為什麼在這裡畫,而不是外面疊一層視窗:
//   遊戲一進世界就不再呼叫任何可以外掛 detour 的繪圖函式,所以先前只能開一個 WPF layered
//   window 疊在遊戲上。那條路有兩個先天問題 —— 更新率跟遊戲不同步(疊層自己跑自己的),
//   以及它畫的是「螢幕」不是「遊戲畫面」,遊戲一縮放位置就要重算。
//   present.cpp 既然已經接管了最終 present,backbuffer 的 HDC 就在手上:在 StretchDIBits 之後、
//   Present 之前畫,等於跟遊戲同一幀、同一張圖、同一個座標系。
//
// 動畫曲線取自 ATSMsgHudLayout.csb 的 Playing_loop:
//   Ico_Rotate(ATS_RING) RotationSkew 0 度 @frame0 → 360 度 @frame120
//   Ico_Play  (ATS_LETTERS) Alpha 255 @frame0 → 127 @frame60 → 255 @frame120
//   Cocos timeline 一秒 60 frame,兩者都是 2 秒一圈。
//
// 全部繁體中文註解(專案規則)。
#include "inhook.h"
#include "ats_sprites.h"
#include <math.h>

// ───────────────────────── launcher → 遊戲的開關 ─────────────────────────
//  launcher 在自己的 process,狩獵開關也在那邊。用具名共享記憶體傳一個 byte 過來:
//  DLL 建、launcher 開,名字帶遊戲 pid 所以多開互不干擾。
//  launcher 開得到 = 遊戲內版本活著 → 它那邊就不再自己畫一份(見 launcher 端 AtsMark)。
struct AtsShared {
    DWORD magic;     // 'L38A'
    DWORD version;   // 1
    BYTE  hunting;   // 0 = 關,非 0 = 自動狩獵中
    BYTE  reserved[3];
};

static const DWORD ATS_MAGIC   = 0x4C333841;
static const DWORD ATS_VERSION = 1;

static HANDLE     g_map    = NULL;
static AtsShared* volatile g_shared = NULL;

void AtsPublish()
{
    if (g_shared) return;

    char name[64];
    wsprintfA(name, "Local\\l38ats_%lu", GetCurrentProcessId());

    g_map = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, sizeof(AtsShared), name);
    if (!g_map) {
        Log("ats: CreateFileMapping 失敗 err=%lu", GetLastError());
        return;
    }

    g_shared = (AtsShared*)MapViewOfFile(g_map, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(AtsShared));
    if (!g_shared) {
        Log("ats: MapViewOfFile 失敗 err=%lu", GetLastError());
        CloseHandle(g_map);
        g_map = NULL;
        return;
    }

    g_shared->magic   = ATS_MAGIC;
    g_shared->version = ATS_VERSION;
    g_shared->hunting = 0;
    Log("ats: 共享旗標就緒 %s", name);
}

// ───────────────────────── 版面 ─────────────────────────
//  經典服 HUD 以 1280x960 排版,標記站在下方橫條上方(高度的 71%),
//  水平置中。經典服自己的線是 78%,再抬高一點是玩家要的。
static const double ATS_DESIGNED_H = 960.0;   // 排版基準高度
static const double ATS_ZOOM       = 1.35;    // 比經典服原尺寸放大多少
static const double ATS_SMALLEST   = 0.7;     // 再小字就不是字了
static const double ATS_LARGEST    = 1.35;
static const int    ATS_FLOOR_NUM  = 71;      // 底線位置:高度 * 71 / 100
static const int    ATS_FLOOR_DEN  = 100;
static const double ATS_LOOP_MS    = 2000.0;  // 一圈兩秒
static const double ATS_DIMMEST    = 127.0 / 255.0;

// ───────────────────────── 畫布(32bpp 預乘 BGRA DIB section)─────────────────────────
//  自己旋轉 / 縮放 / 疊字,最後一次 AlphaBlend 上去。GDI 沒有帶 alpha 的旋轉,而這張圖
//  最大也才 103x103,軟體重取樣的成本遠低於為它另開一條 D3D 繪製路徑。
static HDC     g_canvasDC  = NULL;
static HBITMAP g_canvasBmp = NULL;
static HBITMAP g_canvasOld = NULL;
static BYTE*   g_canvas    = NULL;
static int     g_canvasSide = 0;

static bool EnsureCanvas(int side)
{
    if (g_canvas && g_canvasSide == side) return true;

    if (g_canvasDC) {
        SelectObject(g_canvasDC, g_canvasOld);
        DeleteDC(g_canvasDC);
        g_canvasDC = NULL;
    }
    if (g_canvasBmp) { DeleteObject(g_canvasBmp); g_canvasBmp = NULL; }
    g_canvas = NULL;
    g_canvasSide = 0;

    BITMAPINFO bmi;
    ZeroMemory(&bmi, sizeof(bmi));
    bmi.bmiHeader.biSize        = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth       = side;
    bmi.bmiHeader.biHeight      = -side;         // top-down
    bmi.bmiHeader.biPlanes      = 1;
    bmi.bmiHeader.biBitCount    = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    HDC screen = GetDC(NULL);
    g_canvasBmp = CreateDIBSection(screen, &bmi, DIB_RGB_COLORS, (void**)&g_canvas, NULL, 0);
    if (screen) ReleaseDC(NULL, screen);
    if (!g_canvasBmp || !g_canvas) {
        Log("ats: CreateDIBSection 失敗 side=%d", side);
        g_canvasBmp = NULL;
        g_canvas = NULL;
        return false;
    }

    g_canvasDC = CreateCompatibleDC(NULL);
    if (!g_canvasDC) {
        DeleteObject(g_canvasBmp);
        g_canvasBmp = NULL;
        g_canvas = NULL;
        return false;
    }

    g_canvasOld = (HBITMAP)SelectObject(g_canvasDC, g_canvasBmp);
    g_canvasSide = side;
    return true;
}

// 來源雙線性取樣(預乘資料可以直接線性內插,這正是預乘的用處)。
//  sx/sy 以 pixel 為單位,超出邊界回全透明。
static void SampleBilinear(
    const unsigned char* src, int sw, int sh, double sx, double sy, double out[4])
{
    out[0] = out[1] = out[2] = out[3] = 0.0;
    if (sx < -1.0 || sy < -1.0 || sx > sw || sy > sh) return;

    int x0 = (int)floor(sx), y0 = (int)floor(sy);
    double fx = sx - x0, fy = sy - y0;

    for (int dy = 0; dy < 2; ++dy) {
        for (int dx = 0; dx < 2; ++dx) {
            int x = x0 + dx, y = y0 + dy;
            if (x < 0 || y < 0 || x >= sw || y >= sh) continue;
            double w = (dx ? fx : 1.0 - fx) * (dy ? fy : 1.0 - fy);
            const unsigned char* p = src + ((size_t)y * sw + x) * 4;
            out[0] += p[0] * w;
            out[1] += p[1] * w;
            out[2] += p[2] * w;
            out[3] += p[3] * w;
        }
    }
}

static unsigned char Clamp255(double v)
{
    if (v <= 0.0) return 0;
    if (v >= 255.0) return 255;
    return (unsigned char)(v + 0.5);
}

// 旋轉後的環寫進畫布(覆蓋,畫布已清空)。turns = 0..1 的圈內進度。
static void DrawRing(int side, double scale, double turns)
{
    double angle = turns * 6.283185307179586;   // 2*pi
    double cosA = cos(angle), sinA = sin(angle);
    double half = side / 2.0;

    for (int y = 0; y < side; ++y) {
        unsigned char* row = g_canvas + (size_t)y * side * 4;
        double dy = (y + 0.5) - half;
        for (int x = 0; x < side; ++x) {
            double dx = (x + 0.5) - half;

            // 目的 → 來源:先反旋轉再反縮放。畫面 y 軸向下,所以這組式子轉出來是順時針,
            // 跟 csb 的 RotationSkew 同向。
            double ux = (dx * cosA + dy * sinA) / scale + ATS_RING_W / 2.0;
            double uy = (-dx * sinA + dy * cosA) / scale + ATS_RING_H / 2.0;

            double sample[4];
            SampleBilinear(ATS_RING, ATS_RING_W, ATS_RING_H, ux - 0.5, uy - 0.5, sample);

            unsigned char* p = row + (size_t)x * 4;
            p[0] = Clamp255(sample[0]);
            p[1] = Clamp255(sample[1]);
            p[2] = Clamp255(sample[2]);
            p[3] = Clamp255(sample[3]);
        }
    }
}

// 字疊在環上面(source-over,兩邊都是預乘)。alpha = 這一刻整體透明度 0..1。
static void DrawLetters(int side, double scale, double alpha)
{
    double half = side / 2.0;
    double w = ATS_LETTERS_W * scale, h = ATS_LETTERS_H * scale;
    int x0 = (int)floor(half - w / 2.0), x1 = (int)ceil(half + w / 2.0);
    int y0 = (int)floor(half - h / 2.0), y1 = (int)ceil(half + h / 2.0);

    if (x0 < 0) x0 = 0;
    if (y0 < 0) y0 = 0;
    if (x1 > side) x1 = side;
    if (y1 > side) y1 = side;

    for (int y = y0; y < y1; ++y) {
        unsigned char* row = g_canvas + (size_t)y * side * 4;
        double uy = ((y + 0.5) - half) / scale + ATS_LETTERS_H / 2.0;
        for (int x = x0; x < x1; ++x) {
            double ux = ((x + 0.5) - half) / scale + ATS_LETTERS_W / 2.0;

            double sample[4];
            SampleBilinear(ATS_LETTERS, ATS_LETTERS_W, ATS_LETTERS_H, ux - 0.5, uy - 0.5, sample);
            if (sample[3] <= 0.5) continue;

            double sa = sample[3] * alpha;
            double keep = 1.0 - sa / 255.0;

            unsigned char* p = row + (size_t)x * 4;
            p[0] = Clamp255(sample[0] * alpha + p[0] * keep);
            p[1] = Clamp255(sample[1] * alpha + p[1] * keep);
            p[2] = Clamp255(sample[2] * alpha + p[2] * keep);
            p[3] = Clamp255(sa + p[3] * keep);
        }
    }
}

// csb 的三個 alpha key 之間是直線,沒有指定 easing 就是直線。
static double Breath(double turns)
{
    return turns < 0.5
        ? 1.0 - (1.0 - ATS_DIMMEST) * (turns * 2.0)
        : ATS_DIMMEST + (1.0 - ATS_DIMMEST) * ((turns - 0.5) * 2.0);
}

// 圈內進度。用 QPC 而不是幀數,動畫速度才跟遊戲幀率脫鉤。
static double Turns()
{
    static LARGE_INTEGER freq = {0};
    if (freq.QuadPart == 0 && !QueryPerformanceFrequency(&freq)) return 0.0;

    LARGE_INTEGER now;
    if (!QueryPerformanceCounter(&now)) return 0.0;

    double ms = (double)now.QuadPart * 1000.0 / (double)freq.QuadPart;
    double through = fmod(ms, ATS_LOOP_MS) / ATS_LOOP_MS;
    return through < 0.0 ? through + 1.0 : through;
}

void AtsDraw(HDC dst, int cw, int ch)
{
    if (!dst || cw <= 0 || ch <= 0) return;
    if (!g_shared || g_shared->hunting == 0) return;

    double scale = ch / ATS_DESIGNED_H * ATS_ZOOM;
    if (scale > ATS_LARGEST) scale = ATS_LARGEST;
    if (scale < ATS_SMALLEST) scale = ATS_SMALLEST;

    // 畫布邊長取對角線,轉到任何角度都不會被切到。
    int side = (int)ceil(sqrt((double)ATS_RING_W * ATS_RING_W +
                              (double)ATS_RING_H * ATS_RING_H) * scale);
    if (side < 4) return;
    if (!EnsureCanvas(side)) return;

    ZeroMemory(g_canvas, (size_t)side * side * 4);

    double turns = Turns();
    DrawRing(side, scale, turns);
    DrawLetters(side, scale, Breath(turns));

    // 環的底邊坐在底線上,水平置中。
    int ringH = (int)(ATS_RING_H * scale);
    int floorY = ch * ATS_FLOOR_NUM / ATS_FLOOR_DEN;

    BLENDFUNCTION blend;
    blend.BlendOp             = AC_SRC_OVER;
    blend.BlendFlags          = 0;
    blend.SourceConstantAlpha = 255;
    blend.AlphaFormat         = AC_SRC_ALPHA;

    // 畫布比環大一圈(留旋轉的餘裕),所以是以環的中心對齊,不是以畫布左上角。
    int left = cw / 2 - side / 2;
    int top  = floorY - ringH / 2 - side / 2;
    AlphaBlend(dst, left, top, side, side, g_canvasDC, 0, 0, side, side, blend);
}
