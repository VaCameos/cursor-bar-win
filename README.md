# Cursor Bar for Windows

Windows 托盘应用，用来随时看到 Cursor 套餐用量。形态接近 macOS 版 cursor-bar：没有主窗口，托盘一条双色用量条，点开就是明细。

普通用户不用装 .NET，也不用编译。下载 zip，解压，双击 `CursorBar.exe` 即可。

## 下载

push 到 `main` 后，GitHub Actions 会在 Windows 上自动打包，并挂到 Release：

- 普通电脑：[CursorBar-0.1.0-win-x64.zip](https://github.com/VaCameos/cursor-bar-win/releases/latest/download/CursorBar-0.1.0-win-x64.zip)
- ARM 电脑：[CursorBar-0.1.0-win-arm64.zip](https://github.com/VaCameos/cursor-bar-win/releases/latest/download/CursorBar-0.1.0-win-arm64.zip)
- GitHub：[github.com/VaCameos/cursor-bar-win](https://github.com/VaCameos/cursor-bar-win)
- 工蜂：[git.woa.com/rayyzhang/CursorBar-win](https://git.woa.com/rayyzhang/CursorBar-win)

如果配置了仓库 Secrets 里的 `WOA_TOKEN`，同一份 zip 也会提交回工蜂的 `releases/`，可用：

- [工蜂 raw x64](https://git.woa.com/rayyzhang/CursorBar-win/raw/main/releases/CursorBar-0.1.0-win-x64.zip)

解压后双击 **CursorBar.exe**。想装进开始菜单的话，右键 `install.ps1` → 使用 PowerShell 运行。

### 第一次打开被拦截是正常的

现在的包没有买 Windows 代码签名证书。SmartScreen 可能提示「Windows 已保护你的电脑」。**用户自己放行即可**：

1. 点 **更多信息**
2. 再点 **仍要运行**

或者：右键 `CursorBar.exe` → **属性** → 勾选 **解除锁定** → 确定，再双击。做过一次之后就可以正常启动。

图标可能藏在任务栏右下角的 **^** 溢出区。展开后把 Cursor Bar 拖到任务栏上，就能一直看见用量条。

## 能看到什么

- 托盘双条用量：上面是套餐用量，下面是其他模型 / 按需用量
- 桌面悬浮球：同样的双色条，可拖到任意位置，点击打开明细
- 百分比颜色：绿 → 黄 → 橙 → 红
- 套餐、Cursor 模型、其他模型、按需花费
- 账单周期重置倒计时
- 当前登录邮箱和套餐名

## 登录从哪来

默认不用单独登录。应用只读本机已有的 Cursor 会话：

1. Cursor 应用本地状态 `%APPDATA%\Cursor\User\globalStorage\state.vscdb`
2. `cursor-agent` 的 `%USERPROFILE%\.cursor\auth.json`
3. 设置里可选粘贴 `cursor.com` 的 Cookie

拿到会话后请求 Cursor 自己的用量接口：`/api/usage-summary` 和 `/api/auth/me`。Token 不会写进日志。

## 要求

- Windows 10 / 11
- 本机已安装并登录 Cursor

## 使用

- 左键托盘图标：打开用量面板
- 右键：刷新 / 打开 Cursor 用量页 / 退出
- 悬浮球：默认关闭。设置里打开后，桌面会有可拖动的用量球，点击打开面板
- 面板里可改刷新间隔、托盘提示是否显示百分比 / 金额、登录时启动、是否显示悬浮球

命令行只打印一行用量（不含凭证）：

```powershell
.\CursorBar.exe --once
```

## 给别人发包

平时不用本地打。推 `main` 到 GitHub 后，Actions 会出 zip 并更新 Release。

工蜂本身没有 Windows runner。要让工蜂 `releases/` 也自动更新，到 GitHub 仓库 **Settings → Secrets and variables → Actions** 加一个 `WOA_TOKEN`（工蜂个人设置里的私人令牌，勾选 `api` / 写仓库权限）。

本地仍可打：

```powershell
powershell -File scripts/package.ps1
```

### 第一次打不开是正常的

未签名的 exe 会被 SmartScreen 拦一次。**不需要**买代码签名证书。用户按「更多信息 → 仍要运行」即可。

如果以后想双击完全不弹提示，才需要 EV / 代码签名证书给 exe 签名。
