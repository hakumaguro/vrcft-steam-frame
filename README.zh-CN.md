[English](README.md) | [ไทย](README.th.md) | [日本語](README.ja.md) | **简体中文**

# vrcft-steam-frame

通过 [VRCFaceTracking](https://docs.vrcft.io/)(VRCFT)在 **VRChat** 中为 **Steam Frame** 提供眼动追踪:
双眼分别的视线方向、真实的眨眼和单眼眨眼(wink)。无法实现嘴部追踪:Frame 没有下半脸摄像头。

> 非官方项目。它使用 SteamVR 通过 OSC 共享的眼动数据,以及(可选)头显上一种私有、未公开的数据格式。
> SteamOS 或 SteamVR 的更新都可能改变其中任何一项。使用风险自负。

## 你需要什么

| | 版本 |
|---|---|
| Steam Frame | **SteamOS 0.4.3 或更新** |
| PC 上的 SteamVR | **2.18.2 或更新** |
| VRCFaceTracking | 通过 Steam 安装(已用 5.4.5 测试) |
| 虚拟形象(Avatar) | 支持 VRCFT 眼动追踪(`FT/v2` 参数) |

从这些版本开始,SteamVR 会自行把每只眼睛的眼睑数据发送到 PC,因此**头显上不需要安装任何东西**,也不需要 SSH。
如果版本较旧,请使用 [v0.2.2](https://github.com/hakumaguro/vrcft-steam-frame/releases/tag/v0.2.2)。

## 0.3 新功能

- **无需校准。** 模块会在你佩戴头显期间自动学习你睁眼的程度(戴上后约 20 秒开始,几分钟内稳定),并记住以便下次使用。
  校准(菜单 **3**)现在是可选项,仅用于微调单眼眨眼。
- **眨眼和单眼眨眼直接来自 SteamVR**,按每只眼睛分别处理,并使用符合你今天眼睛状态的睁眼基准。
  右眼单眼眨眼比使用固定基准时成功率更高,眨眼时双眼会同步闭合。
- **更清晰的健康检查:**当眼部摄像头帧率过低或数据持续中断时,状态检查(菜单 **2**)会给出警告。
- **更简单的菜单:**头显部分移到了 **A**(高级),并新增了关闭它的选项。现在已经不再需要它了。
- 半闭(困倦)的眼睑可以调节(`steamVrLid.sleepyLevel`),校准时头显中会显示实时眼睑仪表。

## 快速开始

1. 从[最新发布版](https://github.com/hakumaguro/vrcft-steam-frame/releases/latest)下载 **`vrcft-steam-frame-<version>.zip`**,
   并解压到任意位置。(压缩包内含已编译好的模块,无需安装其他东西。直接下载源代码也可以:此时安装程序会自行编译模块,
   并提示为你的用户安装 .NET SDK。)
2. **关闭 SteamVR**,然后双击 **`Start Here.cmd`** 并选择 **1**。
   它会在 PC 上完成所有设置,重启 VRCFaceTracking,并**语音播报**结果。可以随时再次运行:它只会修改尚未正确的部分。
3. 启动 SteamVR,戴上头显开始游玩。如果 SteamVR 询问是否与 PC 应用共享眼动追踪数据,请选择**允许**。
   请让头显里的 **"Track Dominant Eye Only"(仅追踪主导眼)保持关闭**(VR Settings > General,需显示高级设置):
   开启后两只眼睑都会跟随同一只眼睛,单眼眨眼将无法工作。

有什么不正常?选择 **2**:它会检查每个部分,告诉你哪里有问题以及如何修复。

戴了几分钟后,眨眼或单眼眨眼还是不太对?选择 **3**(需要 [Python 3](https://www.python.org/downloads/)):
语音会引导你完成约 70 秒的睁眼 / 闭眼 / 单眼眨眼步骤,并自动应用结果。

**请始终通过 Steam 启动 VRCFaceTracking**(菜单 **S** 会帮你做到)。直接启动它的 `.exe` 会跳过各个模块(VRCFT 的特性)。

**从 0.2.x 更新:**再次运行菜单 **1**(你的设置会保留)。如果你安装过头显部分(旧版菜单 2),现在已不再需要,
而且旧版无法读取 SteamOS 0.4.3+ 的眼动数据(它会循环重启,但无害)。
用 **A**,然后 **2** 把它关闭,或者在头显上运行 `systemctl --user disable --now frameeyeosc`。
只要它在发送数据,模块就会优先使用它的数据而不是 SteamVR 的数据。

## 头显部分(高级,可选)

菜单 **A** > **1** 通过 SSH 在头显上安装 [frameeyeosc](https://github.com/konsti219/frameeyeosc)。在 SteamOS
0.4.3+ 上它只多提供一样东西:每只眼睛各自的视线方向,而不是双眼共用一个。当它在发送数据时,模块也会用它的数据来处理眼睑,
并使用它自己的校准,因此安装或移除它之后请进行校准(菜单 **3**)。

1. 在头显上启用 SSH(开发者模式),然后选择 **A**、**1**,并输入头显的登录信息(例如
   `steamos@192.168.1.50`)。你只需输入一次头显密码;之后它会安装所有内容,并设置为每次开机自动启动。
   如果有疑虑,请先阅读[为什么需要 SSH?](#为什么需要-ssh仅头显部分)。

   **密码提示是什么样的。**在 "authorising the key" 这一行之后,窗口会停在:

   ```
   [info ] authorising the key on the headset: enter the headset password when asked (one time only)
   steamos@192.168.1.50's password:
   ```

   输入头显用户账户的密码,然后按 **Enter**。**输入时屏幕上不会显示任何内容**(没有圆点,也没有星号);
   这是 SSH 的正常行为。随后它会打印 `[ok   ] SSH login works` 并继续。如果你看到
   `Permission denied, please try again.`,说明密码错误(共三次机会;再次运行 **A** > **1** 可重新开始)。
   如果你从未在头显上设置过密码,请先在头显的终端中用 `passwd` 设置一个。
   如果你已经用自己的 SSH 密钥登录头显(或者没有设置密码),安装程序会使用该登录来授权它自己的密钥,不会询问密码。
   登录失败时,它会打印 ssh 返回的信息以及需要检查的事项。
2. **A** > **2** 可再次将其关闭(停止服务并取消自动启动;文件会保留)。

## 在多台 PC 上使用同一台头显

不使用头显部分时无需做任何事:每台 PC 都从自己的 SteamVR 获取眼动数据,并且每台 PC 各自学习睁眼基准。
在每台 PC 上运行一次菜单 **1** 即可。

使用头显部分时,头显只会把数据发送到**一个** PC 地址。**每次切换 PC 时,请在你要使用的那台 PC 上运行 A > 1**
(头显上必须开启 SSH;第一次之后只需几秒钟)。在此之前,这台 PC 上的模块会使用 SteamVR 的数据,这同样可用,
只是双眼共用一个视线方向。每台 PC 的校准都保存在各自的 `steamframe-config.json` 中(菜单 **5** 会打开该文件夹);
你可以在 PC 之间复制这个文件。

## 为什么需要 SSH?(仅头显部分)

**简短版:**使用 SteamOS 0.4.3+ 和 SteamVR 2.18.2+ 时,你完全不需要 SSH。头显部分只是多提供每只眼睛各自的视线;
它是一个必须在*头显上*运行的小程序,而 SSH 只是 PC 帮你安装它的方式。眼动数据本身不经过 SSH。

**详细版:**

- Frame 用自己的摄像头追踪你的眼睛,结果(视线方向以及每只眼睑的睁开程度)存放在头显的内存中。Steam Link 会把其中一部分转发给 PC:
  双眼共用的一个视线方向,以及(从 SteamOS 0.4.3 / SteamVR 2.18.2 起)每只眼睛的眼睑数据。旧版本发送的眼睑值始终为 0,
  所以过去才需要头显部分。
- [frameeyeosc](https://github.com/konsti219/frameeyeosc) 是一个小程序,在头显上读取这些数据,并通过网络(OSC,UDP 端口 9020)发送给你的 PC。
- 要在头显上安装程序,必须有东西在头显上输入命令。**A** > **1** 通过 SSH 从 PC 完成这一步:
  它会复制三个小脚本,编译 frameeyeosc,并设置为随头显一起启动。

**SSH 用来做什么,不用来做什么:**

| | 是否需要 SSH? |
|---|---|
| 安装或更新头显部分(高级 > 1) | 需要,仅一次 |
| 游玩时发送眼动数据 | **不需要**:frameeyeosc 直接发送,SSH 可以关闭 |
| PC 上的所有操作(菜单 1-6、S) | 不需要 |

**头显上运行的是什么:**frameeyeosc,以你的普通用户身份运行(不是 root)。它只读取眼动追踪数据,并且只发送到你的 PC 地址。
它不会在头显上打开任何端口。它由源代码(约 300 行)在 `~/frameeyeosc` 中编译而成,Rust 安装在 `~/.cargo`。
你的主目录之外的任何东西都不会被改动。

**如何收紧权限:**

1. 为 Advanced > 1 使用一次 SSH,然后再把 **SSH 关掉**(用你当初开启它的同样方式,或在头显上运行 `sudo systemctl disable --now sshd`)。
   眼动追踪仍会继续工作。只有想更新时再重新开启。
2. Advanced > 1 使用它自己创建的密钥登录(`%USERPROFILE%\.ssh\id_ed25519_frame`);你的密码只输入一次,不会被保存。
   若要撤销 PC 的访问权限,请在头显上从 `~/.ssh/authorized_keys` 中删除该密钥所在的那一行。

**完全不使用 SSH**,你有两个选择:

- **跳过头显部分。**只用菜单 1,就能通过 SteamVR 获得视线、眨眼和单眼眨眼(SteamOS 0.4.3+、SteamVR 2.18.2+)。
- **在头显自己的桌面上安装。**在 Frame 的桌面模式中打开终端,运行 Advanced > 1 本来会运行的同一个脚本,并填入你 PC 的地址
  (在 PC 上用 `ipconfig` 查看;请使用与头显处于同一网络的那个地址):
  ```
  git clone https://github.com/hakumaguro/vrcft-steam-frame ~/vrcft-steam-frame
  bash ~/vrcft-steam-frame/scripts/headset-install.sh <PC-IP>
  ```

**完全移除头显部分:**
`systemctl --user disable --now frameeyeosc; loginctl disable-linger $USER; rm -rf ~/frameeyeosc ~/steamframe ~/.config/systemd/user/frameeyeosc.service`,
如需卸载 Rust,再运行 `~/.cargo/bin/rustup self uninstall`。

## 故障排除

| 你注意到的现象 | 怎么做 |
|---|---|
| 单眼眨眼时虚拟形象的**两只**眼睛都闭上 | 虚拟形象把双眼眼睑联动了:打开虚拟形象菜单,关闭 **Eye Sync** / "Link eyes"。同时检查头显中的 "Track Dominant Eye Only" 是否已关闭。 |
| 状态显示 "no eye data at all right now"(目前完全没有眼动数据) | 戴上头显并连接 Steam Link。如果 SteamVR 询问是否与 PC 应用共享眼动追踪数据,请允许。再次运行菜单 **1**(它会检查 Steam Link 是否在端口 9015 上共享眼动数据)。 |
| 状态显示 "only the Steam Link gaze is arriving"(只收到 Steam Link 的视线数据) | 视线正常,但眼睑是猜测出来的:SteamVR 没有发送眼睑数值。请更新 SteamVR(2.18.2+)和头显(SteamOS 0.4.3+)。 |
| 状态显示 "Track Dominant Eye Only is on"(仅追踪主导眼已开启) | 两只眼睑会跟随同一只眼睛,因此单眼眨眼无法工作。在头显中关闭它:VR Settings > General(需显示高级设置)。 |
| 状态显示 "eye data is slow"(眼动数据过慢) | SteamVR 以较低的帧率运行眼部摄像头,所以可能漏掉眨眼。请检查头显上 SteamVR 的眼动追踪设置(`eyeTrackingRateMin`)。 |
| 最初几分钟眼睛看起来是半闭的 | 模块仍在学习你的睁眼基准(戴上头显后约 20 秒才开始)。如果一直这样,请校准(菜单 **3**)。 |
| 眨眼太浅,单眼眨眼太弱 | 菜单 **3**(校准)。有些人单眼眨眼时另一只眼会眯起;校准会考虑到这一点。 |
| 状态显示 "your settings file has an error"(你的设置文件有错误) | 模块忽略了损坏的 `steamframe-config.json`,并沿用之前的设置。请修复它,或恢复 `steamframe-config.json.bak`,或重新校准(菜单 **3**)。 |
| 状态显示 "started without its modules"(未加载模块就启动了) | 关闭 VRCFaceTracking,再从 Steam 启动它(菜单 **S**)。 |
| 状态提到旧的 SteamLink 模块 / 端口 9015 | 菜单 **1** 会把自带的 SteamLink 模块移到一边(菜单 **6**,卸载,会把它放回去)。 |
| 打开 **Module Registry** 时 VRCFaceTracking 自己关闭了 | 已在 0.2.1 中修复:运行一次菜单 **1**(它会修复模块的 `module.json`)。 |
| 使用头显部分时:切换网络或使用过另一台 PC 后眼睑不动了 | 在这台 PC 上再次运行 **A** > **1**:它会让头显指向这里,并保留其余设置。 |

日志(PC):`%TEMP%\steamframe-module.log`(只记录变化,上限 1 MB)。

## 菜单的功能

| | |
|---|---|
| 1 Set up / update(设置 / 更新) | `scripts\setup.ps1`:在任意 Steam 库中找到 VRCFaceTracking,安装或更新模块(你的校准会保留),把自带的 SteamLink 模块移到一边,将 VRCFT 的输出设为 VRChat 的端口 9000,将 Steam Link 设为在端口 9015 上共享眼动数据(仅在 SteamVR 关闭时进行,并带备份),检查防火墙规则(会请求管理员权限),重启 VRCFaceTracking。`-DryRun` 会显示将要发生的更改。 |
| 2 Status(状态) | `scripts\doctor.ps1`:检查每个部分并语音播报摘要。 |
| 3 Calibrate(校准,可选) | `tools\tune.py calibrate`:语音提示(睁眼、闭眼、单眼眨眼、眨眼),然后测量你的眼睛并应用结果。录制期间,视野中央会显示一个小面板,展示左右眼睑的原始数值(`--no-meter` 可关闭;`tune.py meter` 只显示该面板)。加上 `--gaze` 时,还会在头显中显示一个供你注视的圆点,并报告你的视线偏差多少度(仅为报告;目前还不会做任何修正)。 |
| 4 Live view(实时视图) | 原始追踪数据与输出眼睑的实时对比。 |
| 5 Settings folder(设置文件夹) | 打开包含 `steamframe-config.json` 的文件夹。 |
| 6 Uninstall(卸载) | 移除模块(校准会被保存,重新安装时恢复),并恢复自带的 SteamLink 模块。 |
| S Start VRCFaceTracking(启动 VRCFaceTracking) | 通过 Steam 启动它(直接启动 exe 会跳过各个模块)。如果它没有运行,菜单会提示。 |
| A Advanced (headset part)(高级,头显部分) | 使用 SteamOS 0.4.3+ 和 SteamVR 2.18+ 时不需要。1:`setup.ps1 -Headset user@host`:SSH 密钥(密码只需一次),在头显上编译 [frameeyeosc](https://github.com/konsti219/frameeyeosc)(固定的版本加上一个适配 SteamOS 0.4.3 眼动数据格式的补丁;Rust 在 `~/.cargo`,主目录之外不会有任何改动),并安装一个自动启动服务,发送到头显所见的 PC 地址。2:`-HeadsetOff` 再次将其关闭(停止服务并取消自动启动;文件会保留)。 |

## 工作原理

| | Steam Link OSC,旧版本 | Steam Link OSC,SteamOS 0.4.3+ 配合 SteamVR 2.18.2+ | + 头显上的 frameeyeosc |
|---|---|---|---|
| 视线 | 有(双眼镜像) | 有(双眼镜像) | 有,每只眼睛各自 |
| 眨眼 | 根据眼球运动猜测 | 真实的眼睑睁开程度 | 真实的眼睑睁开程度 |
| 单眼眨眼 | 无 | 有(需校准) | 有(需校准) |

在 SteamOS 0.4.3 / SteamVR 2.18.2 之前,Steam Link 驱动在 Frame 上提供的眼睑值始终为 0,真正的每眼眼睑睁开程度只存在于头显的共享内存
(`/dev/shm/eye-server.mmap`)中,由 frameeyeosc 读取并通过 OSC 发送。从这些版本开始,SteamVR 会自行发送每只眼睛的闭合程度
(`/sl/xrfb/facew/EyesClosedL` 和 `EyesClosedR`),只要 frameeyeosc 没有在发送,模块就会使用这些数据。上游的 frameeyeosc 读取的是共享内存
版本 4;SteamOS 0.4.3 使用的是版本 5(眼睛数据记录移动了 5 个字节),所以 **A** > **1** 会带着一个能同时读取两种版本的小补丁来编译它。

```
Steam Frame (SteamOS)                                PC (Windows)
  eye tracking service -> /dev/shm/eye-server.mmap
        |                                              VRCFaceTracking
  frameeyeosc  ---- OSC/UDP :9020 (network) ------->   Steam Frame module  --> VRChat (OSC :9000)
                                                            ^
  Steam Link driver ---- OSC/UDP :9015 (this PC) -----------+   (used when frameeyeosc is not sending)
```

已在 Windows 11 和 VRCFaceTracking 5.4.5(Steam 版)上测试。SteamVR 自身的眼睑数值:SteamOS 0.4.3 beta、0.4.5 和
0.5.8 beta,配合 SteamVR 2.18.2 beta、2.19.2 beta 以及稳定版通道(build 25998080,2026-11-10)。frameeyeosc:SteamOS build 20260922(眼动共享内存版本 4)
和 0.4.5(版本 5,已打补丁)。

## 手动调节

模块会在一秒内重新加载 `steamframe-config.json`(菜单 **5** 会打开其所在文件夹),无需重启。
校准(菜单 **3**,可选)会为你写入每只眼睛的基准值以及眨眼/单眼眨眼设置;以下是可以调节的参数:

| 键 | 含义 |
|---|---|
| `lid.leftClosed/leftOpen/rightClosed/rightOpen` | 以追踪器原始单位表示的每眼固定校准值(null = 自动适应) |
| `steamVrLid.leftClosed/leftOpen/rightClosed/rightOpen` | 针对 SteamVR 自身发送的眼睑值的同类设置(单位为 1 - closed;null = closed 取 0,睁眼基准自动适应) |
| `steamVrLid.learnOpen` | 默认开启(null = 对 SteamVR 的眼睑值开启):模块会在你佩戴头显期间学习每只眼睛的睁眼基准(睁眼样本的第 40 百分位数,主要取最近约 10 分钟的佩戴数据,经过 20 秒预热后开始;每秒最多变化 0.02,并保存在 `steamframe-learned.json` 中供下次启动使用),因此你无需校准。此时 `leftOpen/rightOpen` 只作为起点(新的校准会让学习从它们重新开始)。`false` = 原样使用已校准的基准值 |
| `steamVrLid.learnMinutes` | 学习到的基准值能记住多久,以佩戴分钟数计(默认 10) |
| `steamVrLid.sleepyLevel` | 半闭的眼睑:该值(闭眼到睁眼范围的 0..1,双眼通用)会显示为 `targetSleepy`(0.55)。如果半闭的眼睛在虚拟形象上看起来太睁开,就调高;太闭,就调低。不由校准设置(SteamVR 对半闭眼睛的读数在不同次会话之间差异太大);null = 直线 |
| `steamVrLid.leftSquint/leftSleepy/rightSquint/rightSleepy` | 眯眼和困倦(半闭)状态下的原始值;它们会弯曲输出曲线,使这些姿态显示为 `targetSquint`(0.30)和 `targetSleepy`(0.55),中间为线性。null = 直线。仅适用于 SteamVR 的眼睑值(校准会写入它们) |
| `steamVrLid.filter`(+ `filterMinCutoff / filterBeta`) | One Euro 滤波器:在不拖慢眨眼的前提下稳定保持住的半闭眼睑。默认关闭;校准会将其开启 |
| `steamVrLid.sharpenBelow / asymHysteresis` | 仅当一只眼睛低于此值时才锐化单眼眨眼,并防止在 `blink.asymClosed` 附近徘徊的单眼眯眼反复触发双眼闭合。由校准设置 |
| `lid.deadband` | 范围两端被视为完全闭合 / 完全睁开的比例 |
| `wink.threshold / range / strength` | 放大左右不对称(strength 为 0 = 关闭) |
| `wink.assist`(+ `assistOpen / assistClosed / assistMin / assistPersistMs / assistReleaseMs`) | 闭上一只眼会牵动另一只眼,所以追踪器在单眼眨眼时会把另一只眼报告为部分闭合。当一只眼睛处于它的下限,而另一只眼睛在 `assistPersistMs` 时间内一直高于它自己的下限时,就把另一只眼显示为睁开。默认关闭 |
| `blink.holdMs / releasePerSec` | 短暂保持最低的眼睑值,使快速眨眼也能达到完全闭合 |
| `blink.saturatedRaw / glitchMinMs` | 追踪器经常把一次眨眼报告成一只眼闭合、另一只眼被钉在上限(原始值约 1.0);在此状态持续期间,两只眼睛都会闭合(0 = 关闭;仅适用于 frameeyeosc 数据) |
| `blink.coupleMs / asymClosed / asymOpen` | 任何短于 `coupleMs` 的其他单边闭合都视为眨眼并使双眼闭合;更长则视为单眼眨眼 |
| `gaze.scale / invertX / invertY`、`swapEyes` | 视线缩放和方向修正 |
| `trace` | 持续把 30 毫秒采样写入 `%TEMP%\steamframe-trace.csv`;请保持关闭(校准工具只在需要时自行开启跟踪,无需此设置) |

命令行:`python tools/tune.py calibrate | record | analyze [--apply] | watch | show | dot`。`calibrate --gaze` 会增加视线检查
(头显中在 5 个位置显示圆点,多约 25 秒,SteamVR 必须在运行);`dot` 只在每个位置显示圆点,用于检查它在头显中是否可见。

## 已知限制

- 追踪器会让双眼互相牵连:闭上一只眼会把另一只眼拉向半闭。校准和单眼眨眼辅助(wink assist)会对此进行补偿。
- VRCFT 发送 `EyeLid = openness x 0.75`(0.75 为正常睁开;高于此值表示睁大)。这是 VRCFT 的约定。
- 视线缩放(frameeyeosc 用 +-1 表示 +-45 度)尚未与参考值核对。
- 模块在所有网络上监听 UDP 9020 以接收 frameeyeosc;同一网络中的任何人都可以向它发送眼动数据。

## 从源代码构建

`scripts\deploy-module.ps1`(构建 + 替换已安装的 DLL + 重启 VRCFT)用于开发,
`scripts\package.ps1` 用于生成带预编译 DLL 的发布压缩包(用户无需 .NET SDK)。需要 .NET 10 SDK 和已安装的
VRCFaceTracking(会引用其 DLL)。

## 致谢

- [frameeyeosc](https://github.com/konsti219/frameeyeosc)(MIT),作者 konsti219,负责读取 Frame 的眼动数据。它由安装程序在头显上克隆并编译,
  不包含在本仓库中。
- [LinkFT](https://github.com/ykeara/LinkFT)(MIT),作者 ykeara,本模块参照的 Steam Link VRCFT 模块。
- [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking),作者 benaclejames。

## 许可证

MIT,见 [LICENSE](LICENSE)。
