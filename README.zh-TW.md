# Login38 — 天堂 3.8 登入器

*[English](README.md) · 繁體中文*

天堂 3.8 私服專用登入器:選伺服器、在記憶體裡修補 32 位元客戶端、注入輔助 DLL、啟動遊戲。
以 C# 寫在 .NET 10 上,介面是 WPF Fluent。

包含兩支程式:

- **`launcher.exe`** — 玩家執行的登入器。伺服器清單、視窗模式、修補、注入。
- **`encoder.exe`** — 伺服器主執行的工具。產生登入器要讀的加密 `list.txt` 與 `config.ini`,
  也負責把自訂變身表打包成 `.pak`。

## 兩種取得方式

**我只想直接用。** 到 [Releases](../../releases/latest) 下載 zip,解壓到客戶端目錄,
照 `使用說明.txt` 做。需要安裝 **x86** 版的
[.NET 10 桌面執行階段](https://dotnet.microsoft.com/download/dotnet/10.0) —— 是 x86 不是 x64,
登入器本身是 32 位元行程。

**我想自己編譯。** [**BUILD.md**](BUILD.md) 從頭到尾寫完了:環境需求、clone、建置、測試、
產出跟 release 一模一樣的執行檔。簡短版:

```powershell
git clone https://github.com/r0ptik/Login38.git
cd Login38
dotnet build Login38.slnx -c Debug
pwsh -File build/publish.ps1 -Zip
```

## x86 是正確性要求,不是偏好

注入器會在自己這個行程裡解析 `LoadLibraryW`,再把那個位址交給 32 位元遊戲行程的
`CreateRemoteThread`。在 WOW64 底下兩邊的 `kernel32` 基底位址不同,所以 64 位元的建置會注入
一個錯的指標。所有修補位址同樣都是 32 位元 VA。`Platforms=x86` 寫在
`Directory.Build.props` 對整個方案生效,不要改掉。

## 目錄結構

```
Login38.slnx                 方案檔(.slnx,.NET 10 的新格式)
Directory.Build.props        共用的 TFM / x86 / nullable / 警告視為錯誤
Directory.Packages.props     套件版本集中管理

src/
  Login38.Core/              純邏輯,完全不碰 Win32,可完整單元測試
  Login38.Interop/           Win32 介面:SafeHandle、遠端行程與記憶體、注入器
  Login38.Patching/          遠端記憶體修補
  Login38.Aux/               遊戲內輔助功能
  Login38.Ui/                共用的 WPF 控制項與佈景
  Login38.App/               launcher.exe —— WPF Fluent 介面(WPF-UI + MVVM)
  Login38.Encoder/           encoder.exe —— 伺服器主用的工具
tests/                       xUnit 測試專案,每個來源專案一個
build/                       應用程式資訊清單、發佈腳本、給玩家的說明檔
assets/                      圖示與 logo,編譯進執行檔裡
native/ddraw_inproc/         C++ DirectDraw present hook,以資源形式內嵌
```

## 設定檔

這個 repo 裡刻意沒有任何一份可用的伺服器設定。

| 檔案 | 由誰產生 | 放到哪 | 有進 git 嗎 |
| --- | --- | --- | --- |
| `list.txt` | `encoder.exe` | 發給玩家,跟 `launcher.exe` 放一起 | 否 |
| `config.ini` | `encoder.exe` | 發給玩家,跟 `launcher.exe` 放一起 | 否 |
| `pack.properties` | `encoder.exe` | 伺服器的 `./config/` —— **裡面是 RSA 私鑰指數** | 否 |
| `launcher.ini` | `launcher.exe` | 第一次執行時自動產生,每個玩家自己的 | 否 |
| `*.pak` | `encoder.exe` | 發給玩家,伺服器有用自訂變身表才需要 | 否 |

伺服器主自己用 `encoder.exe` 產生一整組。`pack.properties` 是私鑰,只進伺服器,不去別的地方。

## 驗證

```bash
.claude/check.sh
```

以「警告視為錯誤」建置並跑完整測試。exit 0 表示可以出貨。
