# SwiftRaise — 自动即刻复活插件 / Auto Swiftcast-Raise Plugin

FF14 国服卫月（Dalamud CN）插件：治疗职业**自动**「即刻咏唱 + 复活」鼠标悬停的倒地玩家，无需宏、无需按键。

A Dalamud plugin for FFXIV (CN client): when playing a healer, **automatically** casts Swiftcast + Raise on the dead player under your mouse cursor — no macros, no keybinds.

---

## 核心功能 / Core Features

### 1. 自动触发 / Automatic trigger

**中文**：插件每帧检测，当同时满足以下两个条件时自动施放复活流程：

- 当前职业拥有复活手段：治疗（白魔 / 学者 / 占星 / 贤者 / 幻术师）、**赤魔 / 召唤**，或在**新月岛**携带幻影职业**药剂师**且已习得**苏生**（此时任意职业均可触发）；
- 鼠标悬停在一名**已死亡**的玩家上（悬停**小队列表**或**场景中的人物模型**均可识别）。

**English**: The plugin checks every frame and automatically starts the raise sequence when both conditions are met:

- Your current job has a raise: healers (WHM / SCH / AST / SGE / CNJ), **RDM / SMN**, or — inside the **Occult Crescent** — any job with the **Phantom Chemist** job equipped and **Revive** learned;
- Your mouse is hovering over a **dead** player (both the **party list** and the **3D character model** are recognized).

### 2. 智能施放 / Smart casting

根据即刻咏唱的状态自动选择最优方式：

| 状态 | 行为 |
|---|---|
| 身上已有即刻 buff（赤魔的连续咏唱同理） | 直接秒读复活 |
| 即刻可用 | 先放即刻，buff 生效后自动释放复活 |
| 没有即刻，但新月岛药剂师的苏生可用 | 直接用**苏生**瞬发拉人 |
| 即刻冷却中且无苏生 | 直接硬读复活 |

复活技能按职业自动匹配：复活（白魔/幻术）、复苏（学者）、生辰（占星）、复苏（贤者）、复生（召唤）、赤复活（赤魔）、苏生（新月岛药剂师）。

Picks the best cast path available:

| State | Behavior |
|---|---|
| Swiftcast buff active (RDM Dualcast counts too) | Instant-cast Raise immediately |
| Swiftcast ready | Use Swiftcast first, then Raise once the buff is up |
| No Swiftcast, but Phantom Chemist Revive is up | Use instant **Revive** |
| Swiftcast on cooldown, no Revive | Hardcast Raise directly |

The raise action is matched to your job automatically: Raise (WHM/CNJ), Resurrection (SCH/SMN), Ascend (AST), Egeiro (SGE), Verraise (RDM), Revive (Phantom Chemist).

### 3. 原生信息栏开关 / Server info bar toggle

**中文**：开关集成在游戏**右上角原生信息栏**（显示「艾欧泽亚时间 / 本地时间 / 当前服务器」的那一条）中，显示为「复活:开」（绿色）/「复活:关」（红色），**点击即可切换**，悬停有提示；也可用命令 `/sres` 切换。开关状态自动保存，重启游戏后保持。条目的显示/排序可在 `/xlsettings` → 服务器信息栏 中调整。

**English**: The toggle lives in the game's **native server info bar** (the top-right bar showing Eorzea time / local time / current world), displayed as "复活:开" (green) / "复活:关" (red). **Click it to toggle**; hovering shows a tooltip, and the `/sres` command works too. The state persists across restarts. The entry's visibility/order can be adjusted in `/xlsettings` → Server Info Bar.

### 4. 防误触保护 / Safety guards

**中文**：

- 目标身上已有「复活」待确认状态（已被他人拉起）时不重复施放；
- 同一目标 8 秒内只尝试一次，避免鼠标停留期间反复触发；
- PVP 区域不触发（PVP 技能 ID 不同）；
- 自己倒地或正在读条时不触发；
- 等待即刻生效有 2 秒超时，被打断则放弃本次，等待下一轮检测。

**English**:

- Skips targets that already have the Raise (resurrection pending) status;
- At most one attempt per target every 8 seconds, so hovering doesn't re-trigger every frame;
- Disabled in PvP areas (PvP action IDs differ);
- Won't trigger while you are dead or casting;
- Waiting for the Swiftcast buff times out after 2 seconds (e.g. if interrupted), then waits for the next detection cycle.

---

## 编译 / Building

1. 安装 [.NET 10 SDK](https://dotnet.microsoft.com/download)；
2. 确保用卫月启动过一次游戏（`%AppData%\XIVLauncher\addon\Hooks\dev\` 下有 `Dalamud.dll` 等程序集）；
   - 启动器是 XIVLauncherCN 或装在其他位置时，编辑 `SwiftRaise/SwiftRaise.csproj`，取消 `DalamudLibPath` 注释并改成实际路径，或设置环境变量 `DALAMUD_HOME`；
3. 在 `SwiftRaise/SwiftRaise` 目录下执行：

   ```
   dotnet build -c Release
   ```

4. 产物在 `bin\Release\SwiftRaise\`（含 `latest.zip` 与散装 dll + json）。

## 安装 / Installation

### 方式一：自定义插件仓库（推荐，可远程安装+自动更新）

> [!IMPORTANT]
> **插件仓库地址 / Plugin repo URL：**
>
> ```
> https://raw.githubusercontent.com/sankerXD/SwiftRaise/main/repo.json
> ```

游戏内 `/xlsettings` → 试验性功能 → **自定义插件仓库**（Custom Plugin Repositories），粘贴上面的地址并保存；

### 方式二：本地开发版插件

1. 从 Actions 的 Artifact（或 Release）下载并解压 `SwiftRaise.dll` + json；
2. 游戏内 `/xlsettings` → 试验性功能 → 开发者插件位置（Dev Plugin Locations），添加 `SwiftRaise.dll` 的完整路径并保存；
3. `/xlplugins` → 开发插件列表中启用 SwiftRaise。

## 注意事项 / Notes

- 国服使用任何插件都有账号风险，使用本插件即代表你已知晓并接受，**后果自负**。请自行斟酌，尽量避免在他人可见的场合表现出明显的插件行为。
  Using any plugin on the CN server carries account risk — by using this plugin you accept that risk. **Use at your own risk**, and avoid visibly plugin-like behavior around other players.
