# Deceiver Fusion Slots 20

把《开拓者：正义之怒》（Pathfinder: Wrath of the Righteous）里**魔法诈欺师（Magic Deceiver）**的
「魔法融合」槽位从 **10 个扩展到 20 个**。

原版的 10 个融合槽很快就不够用了 —— 想把不同元素的组合都存下来时会反复覆盖。
这个 mod 在法术书界面加出第二排（两排 × 10 列），新增的 11~20 号槽位与原版槽位**完全等价**。

---

## 功能

- 融合槽位 10 → 20，界面排成两排，编号 1~20 连续
- 每个槽位都能独立选择「默认法术 + 接触法术」组合，独立抄录、独立施法，**互不覆盖**
- 新增槽位所需的法术蓝图在首次打开法术书时自动生成，走的是游戏官方的蓝图序列化路径
- 蓝图 GUID 由槽位名确定性生成（MD5），**跨启动、跨存档保持稳定**，不会串槽
- 纯内存改动，不修改任何游戏文件；移除 mod 后回到原版 10 槽

---

## 依赖

| 依赖 | 是否必需 | 说明 |
|---|---|---|
| **Unity Mod Manager (UMM)** | ✅ 必需 | 本 mod 是 UMM 插件，Harmony 由 UMM 提供，没有 UMM 无法加载 |
| **Pathfinder: Wrath of the Righteous 2.1.x** | ✅ 必需 | 仅针对该版本编译，其他版本需重新编译 |
| 其他 mod（ToyBox / TabletopTweaks 等） | ❌ 不需要 | 本 mod 独立运行，不依赖也不冲突 |
| 「魔法诈欺师」原型 | ⚠️ 使用前提 | 只有拥该原型（奥术师变体）的角色才会出现魔法融合界面 |

> 已验证环境：游戏 2.1.1 + UMM（正常加载，无报错）。

---

## 安装

**方式一：用 UMM 安装 zip（推荐）**

1. 下载 `DeceiverFusionSlots20-UMM.zip`
2. 打开 UMM → `Mods` → `Install` → 选择该 zip
3. 重启游戏

**方式二：手动安装**

把 `Info.json` 和 `DeceiverFusionSlots20.dll` 两个文件放进：

```
<游戏根目录>\Mods\DeceiverFusionSlots20\
```

然后重启游戏。

**卸载**：在 UMM 里禁用或删除即可；如果手动装的，直接删掉那个目录。

---

## 使用

进入角色法术书 → 切换到「魔法融合」标签页，会看到两排共 20 个槽位。
点任意槽位、选择组合、抄录，用法与原版 10 个槽完全一致。

---

## 从源码编译

仓库里已经带了编译好的 `DeceiverFusionSlots20.dll`，一般不需要自己编译。
要改代码的话：

1. 编辑 `Main.cs`
2. 双击 `BUILD.bat`（它会调用 `build.ps1`，自动找编译器、编译、打包 zip，并可选直接安装到游戏目录）

编译脚本会依次尝试：本地 Roslyn 缓存 → 系统 .NET Framework 编译器 → 联网下载 Roslyn。
需要引用游戏安装目录下的 `Wrath_Data\Managed\*.dll`，脚本自动处理。

需要带调试日志的版本：

```powershell
.\build.ps1 -Debug
```

> 微调界面位置只改 `SlotsUI` 顶部的两个公共常量（当前 `ShiftX = 0`、`ShiftY = 29`）:
>
> ```csharp
> public const float ShiftX = 0f;    // 横向整体平移；一格 ≈ 53，正数向右
> public const float ShiftY = 29f;   // 纵向整体平移；一格 ≈ 58，正数向上（29 = 下移半格）
> ```
>
> 列间距不写死：打开面板时先强制跑一次原版布局并记下第一排基准坐标，
> 之后由同步协程**把两排持续钉在「基准 + 平移」的位置上**（游戏自己再改也会被拉回来），
> 所以改这两个数一定会立即生效。

---

## 实现原理

原版限制来自两层，都不在 UI 层：

1. **VM 层硬编码钳制**
   `SpellbookMagicHackMixerVM.TrySelectSlot(int)` 里写着 `Mathf.Clamp(slotIndex, 0, 9)`，
   第 11~20 号槽位传进去会被全部压成 9；而真正加载槽位数据的是它随后调用的
   `ResetCurrentInputToSlot()`。所以只在外部改 `SelectedSlotIndex` 是无效的 —— 那会绕过数据加载。

2. **数据层只有 10 组蓝图**
   `MagicHackSpellbookComponent.m_Abilities` / `m_TouchAbilities` 都是长度 10 的数组，
   `GetDefaultBlueprint(index >= 10)` 直接返回 `null`，即使放开钳制也拿不到数据。

本 mod 用四处 Harmony patch 解决：

| Patch 目标 | 作用 |
|---|---|
| `TrySelectSlot` | Prefix 接管，钳制范围改为 0~19，并调用 `ResetCurrentInputToSlot()` 真正切换槽位 |
| `GetDefaultBlueprint` / `GetTouchBlueprint` | index ≥ 10 时返回自建蓝图（首次调用时惰性构建） |
| `BindViewImplementation` | Postfix 只负责**启动一个协程**：等面板可见、布局算完后克隆 `Slot_1` 生成 11~20，注册进 toggle group，并持续同步两排位置 |

> 为什么不用 `OnSlotChange`：读档时游戏会自动恢复上次选中的槽位并触发该事件，
> 此时 UI 仍在绑定过程中，在事件回调里销毁布局组件 / 重排按钮会打断初始化
> （表现为第一次读档法术书消失）。所有 UI 改动一律放到独立协程里做。

新蓝图通过游戏官方的 `BlueprintJsonWrapper` + `Json.Serializer` 反序列化路径深拷贝生成，
再交给 `ResourcesLibrary.BlueprintsCache.AddCachedBlueprint` 注册 —— 与官方 mod 加载蓝图的流程一致。

---

## 调试

正常编译的版本**不写任何日志、不产生任何文件**。

需要排查时用 `build.ps1 -Debug` 编译，日志会写到：

```
<游戏根目录>\DeceiverFusionSlots20.log
```

正常启动应能看到 20 条 `Cloned (json) ...`（新蓝图构建成功）和
`UI extended: created 10 slots, nativeRow=yes`（界面扩展成功；若为 `fallback`
说明布局尚未生效，会退回固定步进，位置可能略有偏差）。

---

## 已知限制

- 槽位编号文本（11~20）通过反射改写 TextMeshPro 文本实现：如果游戏未来调整 UI 结构，
  编号可能回退显示为 1，**但槽位功能不受影响**（选择和抄录仍按 1~20 正确工作）。
- 界面栅格：打开面板时先强制跑一次原版布局，读取第一排 10 个槽位的真实列坐标，
  第二排逐列复制（只对整块做固定像素的整体平移 `shiftX` / `shiftY`）。
  只有在布局尚未生效（读到的坐标全等）时才回退到固定步进 53×58。
- 仅在 2.1.x 验证过；游戏大版本更新后需重新编译（引用的是游戏程序集）。

---

## 许可证

MIT，详见 [LICENSE](LICENSE)。
