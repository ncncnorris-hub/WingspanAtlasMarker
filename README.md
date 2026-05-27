# Wingspan Atlas Marker

一个 [Wingspan](https://store.steampowered.com/app/1054490/Wingspan/)（《展翅翱翔》）的 BepInEx MOD：
在对局中检测你**手牌 / 鸟盘里从没打过的鸟**，并在 BepInEx 控制台高亮列出，
方便冲「打出每一种鸟」这类成就。
> 检测纯只读：只读取存档里的「已打过鸟」列表，**不修改游戏本体、不写存档、不碰成就**。卸载后不留痕迹。
<img width="795" height="348" alt="image" src="https://github.com/user-attachments/assets/49eaddf0-8afa-458d-9596-f3dc96572e83" />
使用非常简单：
> 安装好MOD后，正常进行游戏（建议开5个真人，自己操控）
> 在游戏过程中，终端窗口会高亮提示有哪只鸟没有打出来过（同时会提示分数、产蛋上限、翼展等信息方便核对）
> 看到提示，就抓哪个鸟，然后就正常打出就可以。

## 功能

- 对局中实时找出手牌（`HAND`）和鸟盘可抽区（`TRAY`）里你从未打出过的鸟。
- 每只鸟单独一行、轮换高亮底色，附带基础数据：
  ```
  UNPLAYED (2)
  Common Swift(252) [TRAY] {VP:5 Eggs:2 WS:55 Nest:None Hab:F/G/W}
  Common Buzzard(244) [HAND] {VP:4 Eggs:3 WS:127 Nest:Platform Hab:G}
  ```
  - `VP` 分数 · `Eggs` 产蛋上限 · `WS` 翼展(cm) · `Nest` 巢型 · `Hab` 栖息地(F=森林/G=草原/W=湿地)
- 控制台日志按等级过滤，只留重要信息保持整洁；并把游戏的 `[Operations]` 日志重新放行显示。

## 运行环境

- Steam 版 Wingspan（Unity **IL2CPP**，**x86 / 32 位**）
- 加载器：**BepInEx 6.0.0-be.755**（Unity.IL2CPP, win-x86）

## 安装（普通玩家）

本 MOD **不附带 BepInEx**（避免转发第三方 / Unity 版权文件），需要你先自行安装，再放入插件 DLL。

1. **安装 BepInEx 6.0.0-be.755**（必须是 IL2CPP / win-x86 变体）：到
   [BepInEx Releases](https://github.com/BepInEx/BepInEx/releases) 下载
   `BepInEx-Unity.IL2CPP-win-x86-*.zip`，解压到游戏根目录
   （`...\steamapps\common\Wingspan\`），从 Steam 启动一次让它初始化（**首次较慢**，在生成 interop），再退出。
2. **放入插件**：到 [Releases](../../releases) 下载 `WingspanAtlasMarker-vX.Y.Z-插件包.zip`，
   把 `WingspanAtlasMarker.dll` 放到
   `<游戏根目录>\BepInEx\plugins\WingspanAtlasMarker\WingspanAtlasMarker.dll`
   （也可直接用仓库里的 [`prebuilt/WingspanAtlasMarker.dll`](prebuilt/WingspanAtlasMarker.dll)）。
3. *(可选)* 让控制台只看重要信息：编辑 `BepInEx\config\BepInEx.cfg`，
   `[Logging.Console]` 段设 `LogLevels = Fatal, Error, Warning`。
4. 启动游戏，开局即可在 BepInEx 黑色控制台看到 `UNPLAYED` 提示。

详细步骤见 [`docs/运行MOD说明.txt`](docs/运行MOD说明.txt)。

## 卸载

完成成就后想恢复原版：完全退出游戏 → 游戏根目录双击 `卸载.bat`（运行包内自带）→ 按 `Y` 确认。
或手动删除游戏根目录的 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`dotnet\`、`BepInEx\`。

## 自行编译

需要 .NET SDK 6.0+，并先装好 BepInEx 跑过一次（生成 interop 引用）。
然后改 `WingspanAtlasMarker.csproj` 里的 `<GameDir>` 为你的 Wingspan 路径，执行：

```
dotnet build
```

产物会自动部署到 `<GameDir>\BepInEx\plugins\`。完整说明见 [`docs/开发MOD说明.txt`](docs/开发MOD说明.txt)。

## 重要提醒

- **同游戏版本最稳**：插件针对当前游戏版本编译，版本差异较大可能失效甚至报错。
- 本仓库**不含任何游戏/Unity 版权文件**（如 `GameAssembly.dll`、`global-metadata.dat`、interop 程序集）。
  编译所需的接口程序集由 BepInEx 在你本机首次运行时生成。

## 致谢

- [BepInEx](https://github.com/BepInEx/BepInEx) / [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop)
- [HarmonyX](https://github.com/BepInEx/HarmonyX)
- [Il2CppDumper](https://github.com/Perfare/Il2CppDumper)（查阅 API 用）

## 许可

[MIT](LICENSE)
