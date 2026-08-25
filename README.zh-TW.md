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

## 功能

### 登入器

- 讀伺服器主加密過的 `list.txt`,只列出真的有在用的欄位,並逐一探測連線,伺服器掛了會在玩家按下
  開始之前就顯示為離線。
- 玩家自己的顯示偏好存在 `launcher.ini`,跟伺服器主的 `config.ini` 分開,所以發新的設定檔
  不會蓋掉別人的設定。
- 公告頁與伺服器主的連結;可選的伺服器清單自動更新與登入器自我更新,網址由伺服器主指定。
- 可同時開多開,上限由伺服器主決定(0 = 不限)。
- 可選的封包加密:伺服器送來的 RSA challenge 折成一個位元組,客戶端把送出去的每個位元組跟它
  XOR;伺服器主想讓移動封包保持明文的話可以單獨排除。
- 在登入畫面攔下輸入的帳號密碼,用伺服器模擬器認得的格式送出登入封包。
- 把客戶端所有對外連線導向選定的伺服器,原版客戶端因此連得到私服。

### 在記憶體裡對客戶端做的修補

每一項都是 `src/Login38.Patching/Patches/` 底下獨立的 `IGamePatch`,依客戶端解包與啟動的
階段套用。

| 修補 | 做什麼 |
| --- | --- |
| `HitPointExpansionPatch` | 把 HP/MP 從 16 位元欄位擴成 32 位元,客戶端碰到的每一處都改 |
| `ArmourResistanceExpansionPatch` | 把 AC 與魔防從一個位元組擴成完整整數 |
| `InventoryLimitPatch` | 修正背包視窗顯示的物品數量上限 |
| `EquipmentSlotsPatch` | 裝備視窗從 19 格擴到 25 格 |
| `ImageLimitPatch` | 提高客戶端能載入的 `img` 圖素資源數量上限 |
| `PngLimitPatch` | 擴大客戶端固定大小的 PNG surface 池 |
| `ItemDescriptionLengthPatch` | 讓過長的物品說明不再讓客戶端當掉 |
| `ItemDescriptionColourPatch` | 讓物品說明裡的顏色碼生效 |
| `LongItemStatusPatch` | 教客戶端一種能帶超過 255 位元組物品說明的封包 |
| `ChatWidthPatch` | 讓聊天行用滿整個聊天框寬度 |
| `InputBoxBackgroundPatch` | 讓聊天輸入框從畫面上取背景,視窗模式下不再畫成一塊黑 |
| `PresentHookPatch` | 接管客戶端把畫面送上螢幕的流程,讓輔助自己畫的文字不會被蓋掉 |
| `SurfacePixelFormatPatch` | 固定 surface 的色彩排列,不再跟著 blitter 執行期的選擇跑 |
| `SmoothRunPatch` | 加速狀態下的角色是用跑的,不是抖著走 |
| `SimplifiedChineseTextPatch` | 讓客戶端能顯示簡體中文,給出簡體版的伺服器主用 |
| `DynamicDialogPatch` | 讓伺服器直接送 NPC 對話內容,而不是只送一個對話編號 |
| `DynamicIconPatch` | 用自訂 PNG pak 讓指定的物品圖示會動 |
| `MorphTablePatch` | 從記憶體餵變身表給客戶端,不走磁碟 |
| `ConnectRedirectPatch` | 把所有對外 TCP 連線導到選定的伺服器 |
| `LoginHookPatch` | 攔帳號密碼,送出模擬器格式的登入封包 |
| `MovePacketEncryptionPatch` | 讓客戶端不要混淆與加密移動封包 |
| `AntiCheatBypassPatch` | 讓客戶端不要因為自己的記憶體完整性檢查而自殺 |
| `TimeProtectionBypassPatch` | 在殼解完之後解除客戶端的自我保護檢查 |
| `CrtWatsonPatch` | 讓 VC++ 2008 執行階段不要因為一個無效參數就終止客戶端 |
| `WindowTitlePatch` | 每次啟動給遊戲視窗一個不同的標題 |

### 遊戲內輔助

**沒按 HOME 就不會啟動** —— 開客戶端不等於要開輔助,有人只是登進去看個東西,或是在玩一隻
不想讓它自己喝水的角色。之後用 **INSERT** 開關。一個客戶端一份,開兩個客戶端就有兩份。

- **喝水** —— 血量低就喝,物品清單由伺服器主放在 `linhelperZ.ini` 裡。
- **補 buff** —— 把角色的 buff 維持著。
- **快捷鍵** —— F1 到 F4 各綁一個指令。
- **計時器** —— 每隔一段時間執行一個指令。
- **喊話** —— 把玩家設定的訊息循環喊出去。
- **丟棄/銷毀** —— 處理玩家標記的物品。
- **角色設定檔** —— 角色進入世界時載入它的設定,離開時存回去。
- **通知** —— 左下角的拾取提示,以及每次擊殺浮出的經驗值與金錢文字。
- **背包監看** —— 輔助視窗開著的時候持續更新背包內容。
- **封包側錄** —— 把客戶端送出的每個封包寫進 log。

輔助視窗裡玩家可以自己開關的項目:

| 開關 | 做什麼 |
| --- | --- |
| 永晝 | 世界維持在正午的亮度 |
| 傷害顯示 | 顯示每次打中的傷害,在目標上方或下方 |
| 怪物顏色 | 依怪物比玩家高多少來上色牠的名字 |
| 時鐘 | 把遊戲內時鐘留在畫面上 |
| 低 CPU | 讓客戶端閒置,不要空轉一顆核心 |
| 水中 | 玩家在水下時把水的效果從畫面上拿掉 |

### 編碼器

- 編輯伺服器清單 —— 名稱、位址、連接埠,以及每台伺服器各自的功能開關 —— 寫回成加密的
  `list.txt`,原版客戶端也讀得懂的格式。
- 把 `[aux]` 與 `[launcher]` 開關寫進 `config.ini`,RSA 金鑰對寫進給伺服器端用的
  `pack.properties`。
- 把變身表從 `.txt` 打包成 `.pak`,並加上標記,讓登入器拒絕第三方工具產生的 `.pak`。

### 尚未實作

有兩個開關會被解析並原樣寫回,好讓伺服器主的設定檔 round-trip 不掉東西,但目前沒有任何程式碼
會讀它們:`AntiCheatAdvanced` 與 `InternalBotEnabled`。

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
