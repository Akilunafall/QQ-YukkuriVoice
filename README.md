# 油库里语音 / Yukkuri Voice

Windows 10 / 11 x64 原生桌面工具。选择 AquesTalk 音色、调整语速，输入文字后离线生成原声 WAV。QQ 发送使用内存注入服务提交语音消息。

## macOS / Linux

新增独立跨平台版 1.3.0，提供 Intel / Apple Silicon Mac 的 `.dmg`、`.app` ZIP，以及 Linux x64 / ARM64 的 `.deb` 和便携 `.tar.gz`。基础音色八种，离线合成、语速、试听、WAV 导出、记录和可编辑悬浮窗均包含。基础音色的输出已与 Windows 原生版逐字节核对。

QQ 跨平台连接为实验性，本次未进行 QQ 实机连接或发送测试。macOS 包仅有本地 ad-hoc 签名，未做 Apple Developer ID 签名或公证。详见 [跨平台使用与验证说明](docs/跨平台版本.md)。

源码在 `src/Yukkuri.CrossPlatform`，GitHub Actions 的 `Build macOS and Linux packages` 可在四种目标系统构建安装包并检查启动。本机输出目录为 `dist/YukkuriVoice-1.3.0-CrossPlatform`。这组新增包不会替代 Windows 1.2.1 原生版。

## 启动

普通用户请在本仓库 [Releases](https://github.com/Akilunafall/QQ-YukkuriVoice/releases) 下载已发布的 `YukkuriVoice-1.2.1-Windows-x64.zip`，完整解压后运行文件夹内的 `YukkuriVoice.exe`。如果 Releases 中尚无附件，需等待便携包发布或按下方说明从源码构建。GitHub 自动生成的 Source code ZIP 是源码，不包含已编译程序。

便携版自带 .NET 运行时；整个文件夹应一起保留，`bridge-x86` 用于 32 位 AquesTalk 引擎。本地构建后的入口为 `dist/YukkuriVoice-1.2.1/YukkuriVoice.exe`。

详细操作见 `docs/使用说明.html`，发行包中也有同名文件。

## 声线与语言

- 内置旧许可 AquesTalk1 的 F1、F2、M1、M2、DVD、IMD1、JGR、R1 八种音色，随附原始授权文本。
- 本机已经安装的官方评估 SDK 提供 AT2 的 14 种 phont、AT10 的 F1/F2/F3/M1/M2/R1/R2 七种预设，以及 AT1-F3。总计 30 个选项。
- AT2/10 和新增 F3 的 SDK 保持完整，存于 `%LOCALAPPDATA%\YukkuriVoice\Engines`，没有打入可再分发 ZIP。官方评估版将「ナ行、マ行」读作「ヌ」；完整 SDK/开发密钥需从 AQUEST 正式获取。程序没有解除此限制。
- 通过“导入授权声库”选择 DLL，支持 x86/x64。AT2 会自动读取 SDK 同级 `phont` 文件夹；AT10 会自动添加七个预设。
- 工作台只有音色下拉框和语速滑块。音频直接保存引擎返回的 WAV，移除了旧版量化、机械调制、移调和增益处理。旧设置不会恢复这些效果。
- 中文先在本机转成假名；日语汉字目前也按中文拼音处理，需要正确日语读法时输入假名。未做与 Yukumo 网站的听感一致性结论。

## QQ 工作流

1. 打开 QQ 聊天输入框并输入文字。软件开启“读取 QQ 当前聊天输入框”和“自动生成”时，在停止输入约 1.2 秒后自动生成。
2. QQ 某些版本未公开可访问的输入框时，将光标放到聊天输入区，按 `Ctrl+Alt+Y` 手动取词。该兼容方式会选中并复制草稿，尽量恢复原剪贴板，最后将光标放到当前行末；自动监测不使用剪贴板。
3. 打开软件后自动连接本机已登录的 QQ。不需要 PMHQ、LuckyLillia、网站注册、手动令牌、独立 Node 或虚拟声卡。
4. 新版内置 x64 注入模块，在正在运行的 QQ 主进程内建立本机命名管道；不修改 QQ 安装文件。退出 QQ 后注入消失，重新打开 QQ 时软件会再次连接。
5. 在 QQ 打开目标好友或群，检查软件显示的“当前 QQ 会话”，然后点击“一键发送 QQ 语音”。不按最近联系人或窗口标题猜测目标。独立聊天窗按最近获得焦点的 QQ 聊天窗定位；无法唯一定位时取消。
6. 发送用的音频副本转为 24 kHz PCM，并在 QQ 进程内编码为 SILK，通过 QQ 原生 PTT 元素提交。原始合成 WAV 不变，不播放麦克风录制。提交前再次比对窗口、聊天类型和真实 UID；切换聊天后取消本次发送。
7. 只有收到与本条消息编号、会话、音频校验值对应的 QQ 成功状态，才显示成功；超时或中断显示“结果未知”，不会自动重发。请在 QQ 查看结果后决定下一步。
8. 悬浮窗现在支持多行文字编辑、240 字符限制，与主窗口双向同步。开启自动生成时停止输入后重新合成；也可按 `Ctrl+Enter` 立即生成，或点击试听/发送时生成最新文字。

**实际发送验证（2026-11-15）**：已在本机 QQ 9.9.29-47354 的用户授权群聊中实际发送三条语音，分别验证本地调用、1.2.1 主窗口一键发送，以及悬浮窗修改文字后发送。三条均取得与目标、消息编号、音频校验值匹配的 QQ 成功状态，并在 QQ 消息列表显示原生语音气泡；点击首条新语音后进入正常播放状态。悬浮窗验证使用相同控件和发送处理器的开发测试入口，仅将窗口边框改为桌面检查工具可识别的普通边框。未核验其他成员设备上的播放效果，未向其他会话发送消息。

1.2.1 修复了 QQ 原生参数转换失败：保留 `msgAttributeInfos` 的 `Map` 类型及可选参数的 `undefined` 值，补全 PTT 的 `voiceType`、`autoConvertText`、`storeID` 和 `otherBusinessInfo.aiVoiceType`。原生拒绝错误现在保留具体原因。临时 IPC 抓包入口已删除。

**兼容性风险**：这是非腾讯官方的进程内集成。QQ 更新可能使接口失效，注入也可能触发客户端安全检查或账号限制；不承诺无风险。只支持 Windows x64、单个 QQ 登录进程，建议先在文件传输助手等本人可控会话测试。不要禁用系统安全软件或添加未知网站信任。

## 数据与兼容性

- 设置、最近最多 60 条记录、WAV 在 `%LOCALAPPDATA%\YukkuriVoice`。
- 语音合成、草稿读取、SILK 编码和软件与 QQ 的通信都在本机完成；消息送达使用 QQ 自身网络连接，没有第三方在线授权。命名管道自动校验本机随机密钥及服务端进程；本软件不开放 HTTP 端口，也不提供任意代码执行接口。
- 每条最多 240 字符；QQ 语音最多 58 秒，较长内容可分段或导出。
- 自动读取要求 QQ 为前台，焦点在输入框，输入框公开可写 ValuePattern 且位于窗口下半部。新版 QQ 的可写 Group 输入区域已做适配。
- QQ 版本/布局/可访问性会影响读取。剪贴板回退必须由用户在正确输入框显式触发，不能自动识别所有 QQ 布局。
- 热键占用时界面会提示，窗口按钮仍可使用。请让 QQ 与本软件都以普通用户权限运行。
- 默认热键被占用时会尝试 `Ctrl+Alt+F9/F10`，再尝试加入 Shift 的组合；实际热键见“QQ 与引擎”。
- 不需要虚拟声卡。导出保留引擎原始采样率；试听时按播放设备要求重采样。
- 本程序不是腾讯 QQ、上海爱丽丝幻乐团、AQUEST 的官方产品。

## 开发与检查

源码构建要求：

- Windows 10 / 11 x64、.NET 10 SDK。
- Node.js，用于本地 QQ JavaScript 传输检查；成品软件运行不需要 Node.js。
- Visual Studio 2022 或更新版本的 C++ Build Tools，包含 MSVC x64 工具链和 Windows SDK，用于从 `src/Yukkuri.QQInjector/Injector.cpp` 编译本地连接器。

在仓库根目录使用 PowerShell 构建：

```powershell
.\scripts\Build.ps1
```

脚本会下载并校验固定版本的 MinHook 源码、编译 QQ 连接器、构建 x86 合成桥接、运行检查，并输出自带运行时的便携文件夹和 ZIP 到 `dist`。仓库包含八套旧许可 AT1 声库及必要运行资源；官网下载的评估 SDK 不随源码分发，未安装时只验证可用基础音色。生成的连接器 DLL 不纳入版本管理，首次从源码运行前请先完成上述构建。

完成首次构建后，也可单独运行：

```powershell
dotnet run --project tests/Yukkuri.Smoke -c Release -- .test-data
dotnet run --project tests/Yukkuri.Smoke -c Release -- .test-data --require-sdks
dotnet run --project src/Yukkuri.Desktop -c Release
```

检查覆盖各音色实际合成、原始 WAV 字节一致性、三代引擎语速、x86 桥接、选择持久化、取消、历史、QQ 草稿分类、悬浮窗编辑同步及发送副本转换。`node scripts/Test-LocalQQ.cjs` 的 25 项检查还覆盖真实接口所需的对象类型与 PTT 字段、完整编码/提交链、消息确认、切换/歧义取消、重复请求抑制和超时不重试。常规检查不向联系人发送消息；本次真实发送验证另见 `docs/验证记录.md`。`--require-sdks` 要求本机安装上述官方 AT2/10 SDK。

`tools/QQProbe` 提供开发诊断。真实发送相关的 `send-test` / `mini-ui` 命令要求在本机设置 `YUKKURI_TEST_GROUP_ID` 和 `YUKKURI_TEST_GROUP_NAME`，只对照当前群进行检查；未配置或目标不符时拒绝进入发送测试。请只配置你获得明确授权的测试群，不要将群号、连接密钥或聊天数据提交到仓库。

`dist`、`bin`、`obj`、下载缓存、测试音频、截图和本机连接配置由 `.gitignore` 排除。第三方组件的许可证保留在原目录及 `THIRD-PARTY-NOTICES.md` 中，公开源码不会改变其授权条件。

## 资料

2026-11-15 查阅：

- 东方百科关于油库里/馒馒来：https://thwiki.cc/油库里
- AQUEST 产品说明：https://www.a-quest.com/products/aquestalk.html
- AquesTalk1 产品/平台说明：https://www.a-quest.com/products/aquestalk_1.html
- 官方下载：https://www.a-quest.com/download.html
- 官方个人使用条件：https://www.a-quest.com/licence_free.html
- MinHook：https://github.com/TsudaKageyu/minhook
- silk-wasm：https://github.com/idranme/silk-wasm

依赖及许可证见 `THIRD-PARTY-NOTICES.md`。
