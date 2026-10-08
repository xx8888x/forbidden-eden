# 超禁忌學園 ～EDEN～　Unity 客戶端 v0.1（原型）

獨立 Unity 6（6000.3.23f1）客戶端。版面依 `Doc\超禁忌學園_UI線框圖評估.md` 與 EDEN UI 線框圖製作，連線 EDEN ai-agent-server（`/api/*` + SSE）。

## 怎麼看

| 方式 | 做法 |
| --- | --- |
| 離線預覽（建議） | 雙擊 `啟動_離線預覽.bat`：啟動模擬伺服器 `Tools\eden_mock_server.py`，再開 `Build\ForbiddenEden.exe`。不連正式伺服器，不消耗 EP。需要 Python 3。 |
| 連正式伺服器 | 開 `Build\ForbiddenEden.exe` → 標題 › 設定 › 帳號與連線，填伺服器位址後按「套用並測試」。**每回合會消耗 1 EP。** |
| 只看截圖 | `_screens_hs2b\` 是最新一次自動導覽拍下的每個畫面（含 HS2 教室與角色、串流中、突發事件、排名戰、被擋輸入）。 |

模擬伺服器的測試關鍵字：輸入含「事件」觸發突發事件、「排名」或「對決」進入排名戰（三回合）、「演出」進入過場、「故事」產生長文分頁、「越獄」觸發被擋輸入。

## 已完成的畫面

| 線框 | 畫面 | 伺服器端點 |
| --- | --- | --- |
| 01 | 客戶端啟動器（語言、解析度、畫質、伺服器連線檢查） | `/api/version` |
| 02 | 標題畫面（核心人物輪替、繼續） | `/api/player/auto_login`、`/api/npc/roster` |
| 03 | 成年確認 → 開場詩卡 → 登入方式 | `/api/onboarding/config` |
| 04 | 登錄問卷 | `/api/onboarding/config`、`/api/onboarding/score` |
| 05 | 結果卡・取名・性別 | `/api/solo/random_name`、`/api/solo/blacklist_names` |
| 06 | 起點選擇（選卡時 3D 預覽場景與初遇人物） | `/api/solo/professions`、`/api/init` |
| 07 | 外觀自訂（五類、滑桿有成年下限、外觀卡 PNG） | — |
| 08 | 主畫面：串流打字、分頁、選項／自己說二擇一、等待／錯誤狀態、402／403／409、過場、突發事件、排名戰列 | `/api/chat`、`/api/events`、`/api/state`、`/api/events/choose` |
| 09 | 移動・校園地圖 | `/api/scene/roster`、`/api/scene/movable`、`/api/scene/move` |
| 10 | 人物手帳（只有文字，無數值；v2 欄位標示未開放） | `/api/npc/roster` |
| 11 | 任務與章節（接／棄／完成） | `/api/quest/player/*` |
| 16 | 拍照模式（機位 1–10、鏡頭語言預設、畫面效果） | `/api/camera/presets` |
| 17 | 設定（無分級切換） | `/api/lang` |
| 18 | 讀取紀錄（自動快照，無手動槽） | `/api/solo/saves`、`/api/solo/load` |
| 補 | 對話回顧、行囊、遊戲選單 | `/api/history`、`/api/inventory/*` |

v2（等後端）：12 個人終端、13 社團招募、15 學園紀錄與許可。

## HS2 素材（已匯出成本專案自己的資產）

HS2 的教室與女性角色已經匯出到專案裡，遊戲執行時**不需要安裝 HS2**。

| 素材 | 位置 | 內容 |
| --- | --- | --- |
| 女性角色 | `Assets\Resources\HS2\Female01.prefab`、`Assets\HS2Export\Female01\` | 身體、頭部、短髮、水手服、百褶裙、高筒襪、室內鞋；網格／貼圖／材質皆為專案資產 |
| 男性角色 | `Assets\Resources\HS2\Male01.prefab`、`Assets\HS2Export\Male01\` | 身體、頭部、短髮、白襯衫、深色西裝褲、室內鞋 |
| 待機動畫 | `Assets\HS2Export\Anim\`（女）、`Assets\HS2Export\AnimMale\`（男） | 女：ADV 待機 6 種＋角色編輯器待機；男：角色編輯器待機；皆重新烘焙成一般動畫片段 |
| 教室 | `Assets\Resources\HS2\Classroom.prefab`、`Assets\HS2Export\Classroom\` | 105 個物件、49 網格、34 材質、50 貼圖，含燈光與碰撞 |
| 職員室 | `Assets\Resources\HS2\Staffroom.prefab`、`Assets\HS2Export\Staffroom\` | HS2 地圖 34；靜態合併網格已從 GPU 讀回還原 |
| 夜間泳池 | `Assets\Resources\HS2\NightPool.prefab`、`Assets\HS2Export\NightPool\` | HS2 地圖 50；約 50 盞彩色點光源 |

### HS2 零件目錄與換裝系統

| 系統 | 位置 | 說明 |
| --- | --- | --- |
| 零件目錄 | `Assets\Resources\HS2\Catalog.json`、`Parts\<類別>\`、`Thumbs\<類別>\` | 834 件（上衣 110、下身 58、襪 29、鞋 59、手套 31、褲襪 23、髮型 100、飾品 11 部位），每件有 HS2 縮圖與中文名稱（對照表 `Assets\Editor\Hs2Names.zh-TW.tsv`）。不適合一般向的項目在 `Hs2CharacterExporter.Parts.cs` 的 `Excluded` 排除。 |
| 基礎身體 | `Resources\HS2\FemaleBase.prefab`、`MaleBase.prefab` | 只有身體與頭；`Hs2Avatar` 依 `Hs2Look` 穿上零件。 |
| 換色 | `Shaders\EdenHs2Cloth.shader`（半透明：`EdenHs2ClothAlpha`） | 與 HS2 相同的 4 色遮罩（黑→色 1、R→2、G→3、B→4），執行時用 MaterialPropertyBlock 換色。 |
| 擺動物理 | `Scripts\Hs2Compat\DynamicBone.cs` | 讀 HS2 原本的參數（阻尼、彈性、硬度…），模擬為本專案自寫。裙子、頭髮會擺動。 |
| HS2 元件替身 | `Scripts\Hs2Compat\Cmp*.cs` | 讓匯出工具讀到 HS2 的預設配色、可換色區域；必須留在 Assembly-CSharp。 |
| 臉型滑桿 | `Scripts\World\Hs2FaceShape.cs`、`Resources\HS2\Shape\` | HS2 的 59 個臉型滑桿；眼睛寬高上限 0.7（成年比例）。 |
| 表情與視線 | `Hs2Rig.SetExpression`、`LookAt` | 伺服器回合的 emotion（Plutchik 八種）→ HS2 眉／眼／嘴變形；頭會轉向鏡頭。 |
| 對話動作 | `Assets\HS2Export\AnimAdv\` | HS2 ADV 的情緒動作（喜、怒、悲、害羞）、說話手勢、13 種站姿、6 種坐姿。沐浴、廁所、睡姿等不匯出。 |
| NPC 造型 | `Scripts\World\Hs2Looks.cs` | 依 NPC ID 固定髮型髮色；學校穿制服、競技塔穿運動服、泳池男生穿泳褲。 |

重新匯出：`ExportBases` → `ExportParts`（可加 `-slots top,bot -limit 5`）→ `Hs2Pipeline.AfterParts`（名稱／排除、臉型資料、對話動作）。地圖：`Tools\export_maps.ps1`。

遊戲中的使用方式：

- 場景為「教室」時改用 HS2 教室（轉 180°，角色站在講桌旁的黑板前）。
- 場景名稱含「職員室／教師室／staff」時用 HS2 職員室（燈光 0.55 倍）；含「泳池／游泳／pool」時用 HS2 夜間泳池（燈光 0.2 倍、夜空背景）。
- 站位用 `Hs2Preview.StaffroomCandidates`／`PoolCandidates` 拍的候選圖挑選；`PoolLighting` 比較減光倍率。
- 女性與男性 NPC 都改用 HS2 角色，依角色 ID 換髮色、瞳色與待機動作，會眨眼、說話時嘴巴會動。
- 玩家自己的外觀自訂仍是暫代模型。
- 一般向固定：身體包裡的成人部位網格在匯出時已排除。

重新匯出（需要 HS2 安裝在 `D:\Honey Select\HS2`，且**不加** `-nographics`）：

```
Unity.exe -batchmode -quit -projectPath ForbiddenEden -executeMethod ForbiddenEden.EditorTools.Hs2CharacterExporter.ExportFemale01
Unity.exe -batchmode -quit -projectPath ForbiddenEden -executeMethod ForbiddenEden.EditorTools.Hs2CharacterExporter.ExportMale01
Unity.exe -batchmode -projectPath ForbiddenEden -executeMethod ForbiddenEden.EditorTools.Hs2SceneExporter.ExportClassroom
Unity.exe -batchmode -projectPath ForbiddenEden -executeMethod ForbiddenEden.EditorTools.Hs2SceneExporter.ExportStaffroom
Unity.exe -batchmode -projectPath ForbiddenEden -executeMethod ForbiddenEden.EditorTools.Hs2SceneExporter.ExportPool
```

教室匯出會讓 Unity 進入執行模式載入 HS2 場景包，完成後自動結束（所以不加 `-quit`）。預覽圖：`-executeMethod ForbiddenEden.EditorTools.Hs2Preview.RenderFemale01`／`RenderClassroom`。

## 重新建置

```
"D:\Unity\Editor\6000.3.23f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath ForbiddenEden -executeMethod ForbiddenEden.EditorTools.BuildTools.SetupAndBuild -logFile _build.log
```

自動導覽截圖：`Build\ForbiddenEden.exe -server http://127.0.0.1:3905 -tour <資料夾> -quitAfterTour`

## 資料位置

設定、外觀卡、照片、紀錄：`%USERPROFILE%\AppData\LocalLow\AISG\超禁忌學園 EDEN\UserData\`（遊戲內「開啟紀錄資料夾」）。
