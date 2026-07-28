# 悬浮窗「贴边自动缩入」功能方案

> 基于 `music-float-window-structure.md` 的结构（WPF + MusicFloatWindowManager 单例调度 + AudioflowSettings 持久化 + DwmHelper 毛玻璃）设计。

## 总体思路

新增一个 **`EdgeDockService`**（放在 `Tools/Services/` 下），以**状态机**形式挂在窗口上，不改变现有「面板 → 管理器 → 窗口」的调用链。两种窗口（Acrylic / Transparent）共用同一套逻辑，差异仅在触发条样式。

```
拖动结束 → 边缘距离检测 → 吸附缩入（动画）→ 触发条待命
触发条悬停 → 展开（动画）→ 移出 → 缩回
触发条/窗口被主动拖动 → 脱离贴边，回到自由状态
```

## 分模块方案

### 1. 贴边检测

- 监听窗口 `LocationChanged` + 鼠标左键释放（`DragMove` 结束后）两个时机。
- 取 `Screen.FromHandle` 得到所在屏 `WorkingArea`，计算窗口左/右边距。
- 距离 ≤ 阈值（建议 20px，可配置）→ 判定贴边，记录贴边方向（Left/Right）；否则不处理。
- 拖动过程中实时给一个「吸附预览」（可选，二期）。

### 2. 状态管理（核心）

状态机挂在 Manager 层，状态流转：

```
Free → Docking(动画中) → Docked → Expanding → Expanded → Docking ...
```

拖动触发条进入 `Dragging`，松手重新走检测。

- `Docked`：窗口主体移出屏幕外，仅留 ~14px 触发条可见。
- 所有状态迁移由 `EdgeDockService` 统一发出，窗口只做动画执行，避免两个窗口各自维护状态。

### 3. 缩入 / 展开动画

- WPF 的 `Window.Left/Top` 不是依赖属性，不能直接 `DoubleAnimation`，用 `DispatcherTimer` / `CompositionTarget.Rendering` 手写缓动（EaseOutCubic，200~250ms）逐帧设 `Left`。
- 缩入：移向屏幕外，保留触发条宽度；展开：反向移回完整可见位置。
- 动画期间锁定状态机，忽略重复触发。

### 4. 触发条渲染与事件

- 在 `MusicContentControl` 外层包一层 `EdgeDockChrome`（自定义控件放 `Controls/`），用 `Path` / `Geometry` 画**梯形圆角**外形，`Clip` 裁切窗口内容边缘。
- 毛玻璃直接复用 `DwmHelper` 的 Acrylic，触发条上加一个「展开方向」小箭头按钮（贴左边 → 箭头朝右）。
- 事件绑定：
  - `MouseEnter` → 展开
  - 窗口整体 `MouseLeave` → 延迟 ~300ms（防抖动）缩回
  - 触发条 `MouseLeftButtonDown` 拖拽 → 转 `Dragging`，拖动即脱离贴边状态

### 5. 全局开关

- `AudioflowSettings` 增加 `EdgeDockEnabled`（默认开）字段，随现有设置持久化。
- `NeteaseMusicTool` 设置卡片加一个胶囊开关，绑定该字段；关闭时 `EdgeDockService` 立即脱离贴边并恢复自由状态，不响应任何检测。

## 落地顺序

1. `AudioflowSettings` 加开关字段 + 面板 UI 开关（半小时可见效果）
2. `EdgeDockService`：边缘检测 + 状态机骨架（先瞬时跳转，无动画）
3. 手写窗口位移动画接入状态机
4. `EdgeDockChrome` 触发条：梯形圆角 + Acrylic + 方向按钮 + 悬停/拖拽事件
5. 联调两种窗口（Acrylic / Transparent）、多屏 DPI、延迟缩回防抖
