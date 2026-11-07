# v2rayN（mac 分支）

一个适用于 Windows、Linux 和 macOS 的 GUI 客户端，支持 [Xray](https://github.com/XTLS/Xray-core)、[sing-box](https://github.com/SagerNet/sing-box) 和[其他核心](https://github.com/2dust/v2rayN/wiki/List-of-supported-cores)。

## 基本说明

- 本分支已包含 `v2rayN.Cli`（终端控制层），可直接操控 GUI 使用的同一套配置/数据库。
- 共享数据目录使用 `LocalApplicationData/v2ray-mac`（首次运行会自动迁移旧 `v2rayN` 目录数据）。
- 支持 GUI 与 CLI 同时运行并同步状态。

## GUI 编译与运行

```bash
cd v2rayN

# 编译 GUI
dotnet build v2rayN.Desktop/v2rayN.Desktop.csproj -c Debug

# 运行 GUI（推荐与 CLI 共用用户数据目录）
V2RAYN_LOCAL_APPLICATION_DATA_V2=1 dotnet run --project v2rayN.Desktop/v2rayN.Desktop.csproj -c Debug
```

## CLI 编译与运行

```bash
cd v2rayN

# 编译 CLI
dotnet build v2rayN.Cli/v2rayN.Cli.csproj -c Debug

# 可选：启用短命令包装器
chmod +x ./v2n

# 帮助
./v2n help
```

## 常用 CLI 命令

```bash
# 订阅
./v2n sub list
./v2n sub update
./v2n sub update 1

# 节点列表与切换
./v2n node list
./v2n node select 3

# 延迟/速度测试
./v2n delay all --mode realping
./v2n delay 1 2 8 --mode tcping
./v2n speed 3

# 地理位置检测（国家/城市）
./v2n geo all
./v2n geo 1 2 8

# 代理 / TUN
./v2n proxy on
./v2n proxy off
./v2n proxy none
./v2n proxy pac
./v2n tun on
./v2n tun off
```

## 自动化命令

```bash
# 一次性流程：更新订阅 -> 全量延迟 -> 前 N 节点测速 -> 自动选最快 -> 可选开代理
./v2n auto run --proxy --top 5

# 一次性流程 + 国家预筛（先做 geo，再按国家筛选）
./v2n auto run --proxy --top 5 --countries "Hong Kong,Japan,Singapore"

# 循环执行（例：每 6 小时）
./v2n auto loop --interval-minutes 360 --proxy --top 5

# 循环执行 + 国家预筛
./v2n auto loop --interval-minutes 360 --proxy --top 5 --countries "Hong Kong,Japan,Singapore"
```

## GUI/CLI 同步

- 菜单 `MainWindow -> Sync Refresh` 可手动立即同步。
- 自动轮询默认每 10 秒同步一次。
- 可通过环境变量调整：`V2RAYN_GUI_SYNC_INTERVAL_SECONDS`。

## 备注

- `node list --json` 必须写成 `./v2n node list --json`。单独写 `node ...` 会调用 Node.js。
- `--json` 可用于 `sub list`、`node list`、`delay`、`speed`、`geo` 便于脚本解析。
- `auto run/auto loop` 支持 `--countries`（或 `--country`）按国家预筛，流程会先检测所有节点 IP 国家再筛选。
- 为避免筛选过严导致无可用节点，自动流程会在以下情况自动取消国家限制并回退全量节点：无国家匹配、测速结果全部 `<0.1`、或延迟结果全部 `>600ms / <=0 / -1`。
- 非 Windows 下若 TUN 需要 sudo，可传 `--sudo-password <pwd>` 或设置 `V2RAYN_SUDO_PASSWORD`。

## macOS 打包（含 CLI）

先发布 GUI 和 CLI：

```bash
cd v2rayN
dotnet publish v2rayN.Desktop/v2rayN.Desktop.csproj -c Release -r osx-x64 -p:SelfContained=true -o Release/macos-64
dotnet publish v2rayN.Cli/v2rayN.Cli.csproj -c Release -r osx-x64 -p:SelfContained=true -o Release/cli-macos-64
```

然后在仓库根目录执行打包脚本（会生成 `v2rayN.app` 并内置 `v2rayN.Cli` 与 `v2n`）：

```bash
./package-osx-cli.sh ./v2rayN/Release/macos-64 ./v2rayN/Release/cli-macos-64 7.20.1 ./v2rayN/Release/v2rayN-macos-64-with-cli.zip
```

用户解压并拖到 `Applications` 后，可执行：

```bash
sudo ln -sf /Applications/v2rayN.app/Contents/MacOS/v2n /usr/local/bin/v2n
v2n status
```
