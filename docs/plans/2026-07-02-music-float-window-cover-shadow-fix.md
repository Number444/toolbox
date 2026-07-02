# MusicFloatWindow 封面阴影修复实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 MusicFloatWindow 封面图片显示肉眼可见的阴影，且不依赖 AllowsTransparency 窗口上不稳定的 Effect 渲染路径。

**架构：** 放弃手动 Border 堆叠模拟阴影的方案（已失败两次），改用 DropShadowEffect 直接挂载在 CoverContainer 上。代码验证：同一窗口中 SongTitle 和 SongArtist 的 DropShadowEffect 正常工作（行89-94、106-111），说明 Effect 在本窗口中并非全部不可用。CoverContainer 的冗余 `Border.Clip` 与 Grid 尺寸不匹配是原始 Effect 失效的可疑原因，将一并清理。

**Tech Stack:** WPF / .NET 9 / XAML

**涉及文件：**
- Modify: `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml`（行21-67）
- No changes: `MusicFloatWindow.xaml.cs`（布局逻辑无需改动）

---

### Task 1: 清理 CoverGrid，恢复干净的基线布局

**Files:**
- Modify: `Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml:21-67`

**修改内容：**
1. 删除 4 层手动阴影 Border（当前行33-52）
2. CoverGrid 尺寸从 186×186 **回退到 180×180**（阴影将靠 Effect 扩展，不再需要 Grid 留边距）
3. CoverContainer 移除显式 Width/Height/Margin，改为默认 Stretch 填满 Grid
4. 删除冗余的 `Border.Clip`——`CornerRadius="10"` 已自带子元素裁切，显式 Clip 的 RectangleGeometry 反而可能与 Grid 缩放不匹配，干扰 Effect 渲染

- [ ] **Step 1: 替换 XAML 封面区块**

将以下内容（当前行21-67）：

```xml
            <!-- 封面 + 阴影（手动模拟，避免 DropShadowEffect 在分层窗口下的兼容问题） -->
            <Grid x:Name="CoverGrid" Width="186" Height="186"
                  HorizontalAlignment="Center"
                  Margin="12,12,12,0">
                <!--
                手动阴影方案（修复版）：Grid 从 180×180 → 186×186，每边留有 3px 余量；
                ...
                -->
                <!-- 第 1 层（最外最淡）：全尺寸覆盖，偏移 (3,3)，只露出右/下 3px -->
                <Border Width="186" Height="186" CornerRadius="13"
                        Background="#30000000"
                        HorizontalAlignment="Left" VerticalAlignment="Top"
                        Margin="3,3,0,0" />
                <!-- 第 2 层：偏移 (2,2)，与层 1 叠加形成 2px 过渡区 -->
                <Border Width="186" Height="186" CornerRadius="12"
                        Background="#40000000"
                        HorizontalAlignment="Left" VerticalAlignment="Top"
                        Margin="2,2,0,0" />
                <!-- 第 3 层：偏移 (1,1)，过渡到紧贴区 -->
                <Border Width="186" Height="186" CornerRadius="11"
                        Background="#55000000"
                        HorizontalAlignment="Left" VerticalAlignment="Top"
                        Margin="1,1,0,0" />
                <!-- 第 4 层（紧贴封面边缘，最深）：无偏移，与封面同区 -->
                <Border Width="186" Height="186" CornerRadius="10"
                        Background="#70000000"
                        HorizontalAlignment="Left" VerticalAlignment="Top"
                        Margin="0,0,0,0" />

                <!-- 封面（圆角裁切图片）：内缩至 (3,3)，180×180 居中铺在阴影之上 -->
                <Border x:Name="CoverContainer"
                        Width="180" Height="180"
                        CornerRadius="10"
                        HorizontalAlignment="Left" VerticalAlignment="Top"
                        Margin="3,3,0,0">
                    <Border.Clip>
                        <RectangleGeometry RadiusX="10" RadiusY="10"
                            Rect="0,0,180,180" />
                    </Border.Clip>
                    <Image x:Name="CoverImage"
                           Stretch="UniformToFill" />
                </Border>
            </Grid>
```

替换为：

```xml
            <!-- 封面 + DropShadowEffect（SongTitle/SongArtist 的 Effect 在此窗口正常工作，方法已验证） -->
            <Grid x:Name="CoverGrid" Width="180" Height="180"
                  HorizontalAlignment="Center"
                  Margin="12,12,12,0">
                <!-- 封面（圆角裁切，CornerRadius 自带子元素裁切，无需显式 Clip） -->
                <Border x:Name="CoverContainer"
                        CornerRadius="10">
                    <Border.Effect>
                        <DropShadowEffect BlurRadius="15"
                                          ShadowDepth="4"
                                          Opacity="0.5"
                                          Color="Black" />
                    </Border.Effect>
                    <Image x:Name="CoverImage"
                           Stretch="UniformToFill" />
                </Border>
            </Grid>
```

使用 `SearchReplace` 工具直接替换。注意确认行范围精确匹配。

- [ ] **Step 2: 验证 XAML 语法正确**

运行：

```powershell
# 验证没有引入拼写/结构错误
Select-String -Path "d:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml" -Pattern "Effect|CoverGrid|CoverContainer"
```

Expected: 输出应包含 `DropShadowEffect`、`BlurRadius` 等字段，无拼写错误。

- [ ] **Step 3: 验证 .cs 文件不受影响**

确认 code-behind 中 `CoverGrid.HorizontalAlignment`（行182）仍然编译通过——CoverGrid 仍然是 Grid，x:Name 未变。

```powershell
Select-String -Path "d:\Agent Space\Toolbox\Toolbox.Plugins\Tools\Views\MusicFloatWindow.xaml.cs" -Pattern "CoverGrid"
```

Expected: 匹配到 `CoverGrid.HorizontalAlignment = halign;`（行182）。

- [ ] **Step 4: 编译并确认无错误**

```powershell
# 从项目根目录编译
cd "d:\Agent Space\Toolbox"
dotnet build -c Debug --no-restore 2>&1 | Select-String -Pattern "error|Error|CS"
```

Expected: 无任何编译错误输出。如果有错误，检查 XAML 标签是否闭合、命名空间是否正确。

- [ ] **Step 5: Commit**

```bash
git add "Toolbox.Plugins/Tools/Views/MusicFloatWindow.xaml"
git commit -m "fix: replace manual border shadow with DropShadowEffect on cover
- 删除 4 层手动偏移 Border 阴影（方案两次验证均不可见）
- CoverGrid 从 186×186 回退到 180×180
- CoverContainer 移除显式 Width/Height/Margin，默认 Stretch 填满 Grid
- 删除冗余 Border.Clip（CornerRadius 已自带子元素裁切）
- 添加 DropShadowEffect BlurRadius=15 ShadowDepth=4 Opacity=0.5
- 基于已验证事实：SongTitle/SongArtist 的 DropShadowEffect 在同一窗口正常工作"
```

---

### 关于 Effect 可靠性的设计说明

之前 DropShadowEffect 在 CoverContainer 上失效的可疑原因已被清理：

| 疑点 | 判断 |
|---|---|
| **分层窗口 Effect 不可用** | 证伪——SongTitle（行89-94）和 SongArtist（行106-111）的 Effect 正常渲染 |
| **CoverContainer 的显式 Clip 干扰** | 行55-57：`Clip` 的 `Rect="0,0,180,180"` 与 Grid 缩放可能存在不一致，已删除（CornerRadius 自动裁切子元素） |
| **CoverContainer 被夹在 Border 堆叠中** | 手动阴影层（原行34-52）是纯 Border，但其 Z 顺序和 Margin 偏移导致 Effect 视觉混乱，现已全部移除 |
| **Grid 尺寸 186×186 与 CoverContainer 180×180 不匹配** | 已统一回退到 180×180，CoverContainer 默认 Stretch 填满 |

### 参数微调建议

初次编译运行后，如果阴影效果偏弱或偏强，可调整 DropShadowEffect 参数：

- `BlurRadius="15"` → 增大（>20）使阴影更弥散柔和；减小（<10）使阴影更锐利聚焦
- `ShadowDepth="4"` → 增大（>6）让阴影向右下偏移更明显；减小（<2）让阴影更贴近封面
- `Opacity="0.5"` → 增大（>0.6）加深阴影；减小（<0.4）减淡阴影

这些调整仅在 XAML 行内改数值即可，无需重新编译？不——需重新编译。但无需改动 .cs 文件。