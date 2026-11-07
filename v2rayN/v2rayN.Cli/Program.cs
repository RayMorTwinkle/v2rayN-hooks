using System.Text.Json;
using System.Net.Sockets;
using ServiceLib.Enums;
using ServiceLib.Handler;
using ServiceLib.Handler.Builder;
using ServiceLib.Handler.SysProxy;
using ServiceLib.Manager;
using ServiceLib.Models;
using ServiceLib.Services;

namespace v2rayN.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new CliApplication();
        return await app.RunAsync(args);
    }
}

internal sealed class CliApplication
{
    private Config? _config;
    private bool _initialized;

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var parsed = ParsedArgs.Parse(args.Skip(1).ToArray());

            return command switch
            {
                "sub" or "subscription" => await HandleSubscriptionCommandAsync(parsed),
                "node" or "nodes" => await HandleNodeCommandAsync(parsed),
                "delay" => await HandleDelayCommandAsync(parsed),
                "speed" => await HandleSpeedCommandAsync(parsed),
                "geo" => await HandleGeoCommandAsync(parsed),
                "proxy" => await HandleProxyCommandAsync(parsed),
                "tun" => await HandleTunCommandAsync(parsed),
                "auto" => await HandleAutoCommandAsync(parsed),
                "status" => await HandleStatusCommandAsync(parsed),
                "help" or "--help" or "-h" => HandleHelpCommand(parsed),
                _ => UnknownCommand(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static bool IsHelp(string arg)
    {
        return arg.Equals("help", StringComparison.OrdinalIgnoreCase)
               || arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
               || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)
               || arg.Equals("/?", StringComparison.OrdinalIgnoreCase);
    }

    private int HandleHelpCommand(ParsedArgs parsed)
    {
        _ = parsed;
        PrintHelp();
        return 0;
    }

    private int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        // CLI defaults to shared user data dir so it controls the same config/db as GUI installs.
        // Set V2RAYN_CLI_PORTABLE=1 to keep data in the current executable directory.
        var portableMode = string.Equals(Environment.GetEnvironmentVariable("V2RAYN_CLI_PORTABLE"), "1", StringComparison.Ordinal);
        if (!portableMode
            && !string.Equals(Environment.GetEnvironmentVariable(ServiceLib.Global.LocalAppData), "1", StringComparison.Ordinal))
        {
            Environment.SetEnvironmentVariable(ServiceLib.Global.LocalAppData, "1", EnvironmentVariableTarget.Process);
        }

        if (!AppManager.Instance.InitApp())
        {
            throw new InvalidOperationException("Failed to initialize app config.");
        }

        _config = AppManager.Instance.Config;

        var sudoPassword = Environment.GetEnvironmentVariable("V2RAYN_SUDO_PASSWORD");
        if (!string.IsNullOrWhiteSpace(sudoPassword))
        {
            AppManager.Instance.LinuxSudoPwd = sudoPassword;
        }

        await ProfileExManager.Instance.Init();
        await CoreManager.Instance.Init(_config, UpdateLogAsync);

        _initialized = true;
    }

    private static async Task UpdateLogAsync(bool notify, string msg)
    {
        if (!string.IsNullOrWhiteSpace(msg))
        {
            if (notify)
            {
                Console.Error.WriteLine(msg.Trim());
            }
            else
            {
                Console.WriteLine(msg.Trim());
            }
        }
        await Task.CompletedTask;
    }

    private async Task<int> HandleSubscriptionCommandAsync(ParsedArgs parsed)
    {
        await EnsureInitializedAsync();
        var action = parsed.PositionalAt(0)?.ToLowerInvariant() ?? "list";

        if (action == "list")
        {
            var subscriptions = await AppManager.Instance.SubItems() ?? new List<SubItem>();
            if (parsed.HasFlag("--json"))
            {
                PrintJson(subscriptions.Select((s, i) => new
                {
                    no = i + 1,
                    id = s.Id,
                    remarks = s.Remarks,
                    enabled = s.Enabled,
                    autoUpdateMinutes = s.AutoUpdateInterval,
                    updateTimeUnix = s.UpdateTime,
                    url = s.Url
                }));
            }
            else
            {
                Console.WriteLine("No  Enabled  AutoUpdate(min)  Id                                   Remarks");
                for (var i = 0; i < subscriptions.Count; i++)
                {
                    var s = subscriptions[i];
                    Console.WriteLine($"{i + 1,2}  {(s.Enabled ? "Y" : "N"),7}  {s.AutoUpdateInterval,15}  {s.Id,-35}  {s.Remarks}");
                }
            }
            return 0;
        }

        if (action == "update")
        {
            var target = parsed.PositionalAt(1);
            var useProxy = parsed.HasFlag("--proxy");
            var reload = !parsed.HasFlag("--no-reload");
            var subId = await ResolveSubscriptionIdAsync(target);

            await SubscriptionHandler.UpdateProcess(_config!, subId, useProxy, async (_, msg) =>
            {
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    Console.WriteLine(msg);
                }
                await Task.CompletedTask;
            });

            if (reload)
            {
                await ReloadCoreAsync();
            }
            return 0;
        }

        Console.Error.WriteLine("Usage: sub list | sub update [sub-id|no|remarks] [--proxy] [--no-reload]");
        return 1;
    }

    private async Task<int> HandleNodeCommandAsync(ParsedArgs parsed)
    {
        await EnsureInitializedAsync();
        var action = parsed.PositionalAt(0)?.ToLowerInvariant() ?? "list";

        if (action == "list")
        {
            var sub = parsed.GetOption("--sub");
            var rows = await GetNodeRowsAsync(sub);
            if (parsed.HasFlag("--json"))
            {
                PrintJson(rows.Select(r => new
                {
                    no = r.No,
                    indexId = r.Profile.IndexId,
                    remarks = r.Profile.Remarks,
                    configType = r.Profile.ConfigType.ToString(),
                    subId = r.Profile.Subid,
                    ip = r.GeoIP,
                    location = r.GeoLocation,
                    delay = r.Delay,
                    speed = r.Speed,
                    active = r.Profile.IndexId == _config!.IndexId
                }));
            }
            else
            {
                Console.WriteLine("No  Act  Delay  Speed      Location               IndexId                              Remarks");
                foreach (var row in rows)
                {
                    var active = row.Profile.IndexId == _config!.IndexId ? "*" : " ";
                    Console.WriteLine($"{row.No,2}  {active,3}  {row.Delay,5}  {row.Speed,8:0.##}  {row.GeoLocation,-20}  {row.Profile.IndexId,-35}  {row.Profile.Remarks}");
                }
            }
            return 0;
        }

        if (action == "select" || action == "use")
        {
            var target = parsed.PositionalAt(1);
            if (string.IsNullOrWhiteSpace(target))
            {
                Console.Error.WriteLine("Usage: node select <no|indexId|keyword> [--sub <subId>] [--no-reload]");
                return 1;
            }

            var rows = await GetNodeRowsAsync(parsed.GetOption("--sub"));
            var selected = ResolveNode(rows, target);
            if (selected == null)
            {
                return 1;
            }

            await ConfigHandler.SetDefaultServerIndex(_config!, selected.Profile.IndexId);
            Console.WriteLine($"Selected node: {selected.Profile.Remarks} ({selected.Profile.IndexId})");

            if (!parsed.HasFlag("--no-reload"))
            {
                await ReloadCoreAsync();
            }
            return 0;
        }

        Console.Error.WriteLine("Usage: node list [--sub <subId>] [--json] | node select <no|indexId|keyword> [--sub <subId>] [--no-reload]");
        return 1;
    }

    private async Task<int> HandleDelayCommandAsync(ParsedArgs parsed)
    {
        await EnsureInitializedAsync();
        var sub = parsed.GetOption("--sub");
        var mode = parsed.GetOption("--mode")?.ToLowerInvariant() ?? "realping";
        var actionType = mode == "tcping" ? ESpeedActionType.Tcping : ESpeedActionType.Realping;
        var rows = await GetNodeRowsAsync(sub);

        List<NodeRow> selected;
        var target = parsed.PositionalAt(0);
        if (string.IsNullOrWhiteSpace(target) || target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            selected = rows;
        }
        else
        {
            selected = ResolveNodes(rows, parsed.Positionals);
            if (selected.Count == 0)
            {
                return 1;
            }
        }

        var timeoutSeconds = parsed.GetIntOption("--timeout", Math.Max(120, selected.Count * 20));
        await RunSpeedActionAsync(actionType, selected.Select(t => t.Profile).ToList(), TimeSpan.FromSeconds(timeoutSeconds));
        await ConfigHandler.SaveConfig(_config!);

        var refreshed = await GetNodeRowsAsync(sub);
        var result = refreshed.Where(r => selected.Any(s => s.Profile.IndexId == r.Profile.IndexId)).ToList();
        result.Sort((a, b) => a.Delay.CompareTo(b.Delay));

        if (parsed.HasFlag("--json"))
        {
            PrintJson(result.Select(r => new
            {
                no = r.No,
                indexId = r.Profile.IndexId,
                remarks = r.Profile.Remarks,
                delay = r.Delay
            }));
        }
        else
        {
            Console.WriteLine("No  Delay  IndexId                              Remarks");
            foreach (var row in result)
            {
                Console.WriteLine($"{row.No,2}  {row.Delay,5}  {row.Profile.IndexId,-35}  {row.Profile.Remarks}");
            }
        }
        return 0;
    }

    private async Task<int> HandleGeoCommandAsync(ParsedArgs parsed)
    {
        await EnsureInitializedAsync();
        var sub = parsed.GetOption("--sub");
        var rows = await GetNodeRowsAsync(sub);

        List<NodeRow> selected;
        var target = parsed.PositionalAt(0);
        if (string.IsNullOrWhiteSpace(target) || target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            selected = rows;
        }
        else
        {
            selected = ResolveNodes(rows, parsed.Positionals);
            if (selected.Count == 0)
            {
                Console.Error.WriteLine("Usage: geo all | geo <no|indexId|keyword> [...] [--sub <subId>] [--json]");
                return 1;
            }
        }

        foreach (var row in selected)
        {
            var geo = await GeoIpService.QueryAsync(row.Profile.Address);
            ProfileExManager.Instance.SetGeoInfo(row.Profile.IndexId, geo);
            await Task.Delay(120);
        }
        await ProfileExManager.Instance.SaveTo();

        var refreshed = await GetNodeRowsAsync(sub);
        var result = refreshed.Where(r => selected.Any(s => s.Profile.IndexId == r.Profile.IndexId)).ToList();

        if (parsed.HasFlag("--json"))
        {
            PrintJson(result.Select(r => new
            {
                no = r.No,
                indexId = r.Profile.IndexId,
                remarks = r.Profile.Remarks,
                ip = r.GeoIP,
                location = r.GeoLocation
            }));
        }
        else
        {
            Console.WriteLine("No  IP               Location               IndexId                              Remarks");
            foreach (var row in result)
            {
                Console.WriteLine($"{row.No,2}  {row.GeoIP,-15}  {row.GeoLocation,-20}  {row.Profile.IndexId,-35}  {row.Profile.Remarks}");
            }
        }

        return 0;
    }

    private async Task<int> HandleSpeedCommandAsync(ParsedArgs parsed)
    {
        await EnsureInitializedAsync();
        var sub = parsed.GetOption("--sub");
        var rows = await GetNodeRowsAsync(sub);
        var target = parsed.PositionalAt(0);
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("Usage: speed <no|indexId|keyword> [--sub <subId>] [--timeout <sec>] [--json]");
            return 1;
        }

        var node = ResolveNode(rows, target);
        if (node == null)
        {
            return 1;
        }

        var timeoutSeconds = parsed.GetIntOption("--timeout", 180);
        await RunSpeedActionAsync(ESpeedActionType.Speedtest, new List<ProfileItem> { node.Profile }, TimeSpan.FromSeconds(timeoutSeconds));
        await ConfigHandler.SaveConfig(_config!);

        var refreshed = (await GetNodeRowsAsync(sub)).FirstOrDefault(r => r.Profile.IndexId == node.Profile.IndexId);
        if (refreshed == null)
        {
            Console.Error.WriteLine("Speed test finished, but no refreshed profile data found.");
            return 1;
        }

        if (parsed.HasFlag("--json"))
        {
            PrintJson(new
            {
                no = refreshed.No,
                indexId = refreshed.Profile.IndexId,
                remarks = refreshed.Profile.Remarks,
                delay = refreshed.Delay,
                speed = refreshed.Speed
            });
        }
        else
        {
            Console.WriteLine($"Node: {refreshed.Profile.Remarks} ({refreshed.Profile.IndexId})");
            Console.WriteLine($"Delay: {refreshed.Delay} ms");
            Console.WriteLine($"Speed: {refreshed.Speed:0.##}");
        }
        return 0;
    }

    private async Task<int> HandleProxyCommandAsync(ParsedArgs parsed)
    {
        await EnsureInitializedAsync();
        var action = parsed.PositionalAt(0)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action) || action == "status")
        {
            Console.WriteLine($"System proxy mode: {_config!.SystemProxyItem.SysProxyType}");
            return 0;
        }

        var type = action switch
        {
            "on" or "enable" => ESysProxyType.ForcedChange,
            "off" or "disable" => ESysProxyType.ForcedClear,
            "none" => ESysProxyType.Unchanged,
            "pac" => ESysProxyType.Pac,
            _ => (ESysProxyType?)null
        };
        if (type == null)
        {
            Console.Error.WriteLine("Usage: proxy status | proxy <on|off|none|pac>");
            return 1;
        }

        _config!.SystemProxyItem.SysProxyType = type.Value;
        await ConfigHandler.SaveConfig(_config);
        await SysProxyHandler.UpdateSysProxy(_config, false);
        Console.WriteLine($"System proxy mode set to: {_config.SystemProxyItem.SysProxyType}");
        if (type == ESysProxyType.ForcedChange)
        {
            var socksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (!await WaitForLocalPortAsync(socksPort, 1500))
            {
                Console.Error.WriteLine($"[warning] Local socks port {socksPort} is not ready. Proxy may fail until core is running.");
            }
        }
        return 0;
    }

    private async Task<int> HandleTunCommandAsync(ParsedArgs parsed)
    {
        await EnsureInitializedAsync();
        var action = parsed.PositionalAt(0)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action) || action == "status")
        {
            Console.WriteLine($"Tun enabled: {_config!.TunModeItem.EnableTun}");
            return 0;
        }

        bool? enabled = action switch
        {
            "on" or "enable" => true,
            "off" or "disable" => false,
            _ => null
        };
        if (enabled == null)
        {
            Console.Error.WriteLine("Usage: tun status | tun <on|off> [--sudo-password <pwd>] [--no-reload]");
            return 1;
        }

        var sudoPassword = parsed.GetOption("--sudo-password")
                           ?? Environment.GetEnvironmentVariable("V2RAYN_SUDO_PASSWORD");
        if (!string.IsNullOrWhiteSpace(sudoPassword))
        {
            AppManager.Instance.LinuxSudoPwd = sudoPassword;
        }

        _config!.TunModeItem.EnableTun = enabled.Value;
        await ConfigHandler.SaveConfig(_config);
        Console.WriteLine($"Tun mode set to: {_config.TunModeItem.EnableTun}");

        if (!parsed.HasFlag("--no-reload"))
        {
            await ReloadCoreAsync();
        }
        return 0;
    }

    private async Task<int> HandleAutoCommandAsync(ParsedArgs parsed)
    {
        await EnsureInitializedAsync();
        var action = parsed.PositionalAt(0)?.ToLowerInvariant() ?? "run";
        return action switch
        {
            "run" => await RunAutoPipelineAsync(parsed),
            "loop" => await RunAutoLoopAsync(parsed),
            _ => HandleAutoUsage(),
        };
    }

    private int HandleAutoUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  auto run [--sub <subId>] [--countries <country1,country2,...>] [--delay-mode <realping|tcping>] [--top <n>] [--proxy] [--enable-tun]");
        Console.Error.WriteLine("  auto loop --interval-minutes <n> [--sub <subId>] [--countries <country1,country2,...>] [--delay-mode <realping|tcping>] [--top <n>] [--proxy] [--enable-tun] [--iterations <n>]");
        return 1;
    }

    private async Task<int> RunAutoPipelineAsync(ParsedArgs parsed)
    {
        var subTarget = parsed.GetOption("--sub");
        var top = Math.Max(1, parsed.GetIntOption("--top", 5));
        var delayMode = parsed.GetOption("--delay-mode")?.ToLowerInvariant() ?? "realping";
        var useProxyForSubUpdate = parsed.HasFlag("--proxy");
        var enableTun = parsed.HasFlag("--enable-tun");
        var timeoutSeconds = parsed.GetIntOption("--timeout", 240);
        var countryFilters = ParseCountryFilters(parsed);

        var subId = await ResolveSubscriptionIdAsync(subTarget);
        await SubscriptionHandler.UpdateProcess(_config!, subId, useProxyForSubUpdate, async (_, msg) =>
        {
            if (!string.IsNullOrWhiteSpace(msg))
            {
                Console.WriteLine(msg);
            }
            await Task.CompletedTask;
        });

        var rows = await GetNodeRowsAsync(null);
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("No nodes found.");
            return 1;
        }

        await RunGeoDetectAsync(rows);
        var allRows = await GetNodeRowsAsync(null);
        if (allRows.Count == 0)
        {
            Console.Error.WriteLine("No nodes found after geo detect.");
            return 1;
        }

        var candidateRows = allRows;
        var countryFilterApplied = false;
        if (countryFilters.Count > 0)
        {
            var filteredRows = allRows
                .Where(r => IsCountryMatched(r, countryFilters))
                .ToList();
            if (filteredRows.Count > 0)
            {
                candidateRows = filteredRows;
                countryFilterApplied = true;
                Console.WriteLine($"Country filter applied: {string.Join(", ", countryFilters)} ({filteredRows.Count}/{allRows.Count} nodes)");
            }
            else
            {
                Console.WriteLine("Country filter matched no nodes. Fallback to all nodes.");
            }
        }

        var delayAction = delayMode == "tcping" ? ESpeedActionType.Tcping : ESpeedActionType.Realping;
        var selection = await SelectBestNodeAsync(candidateRows, top, delayAction, timeoutSeconds);

        if (selection.Selected == null)
        {
            if (countryFilterApplied)
            {
                Console.WriteLine("Country filtered candidates produced no selectable node. Fallback to all nodes.");
                selection = await SelectBestNodeAsync(allRows, top, delayAction, timeoutSeconds);
            }
        }

        if (countryFilterApplied && ShouldFallbackCountryRestriction(selection))
        {
            Console.WriteLine("Country filtered candidates are poor (delay/speed threshold). Fallback to all nodes.");
            selection = await SelectBestNodeAsync(allRows, top, delayAction, timeoutSeconds);
        }

        var selected = selection.Selected;

        if (selected == null)
        {
            Console.Error.WriteLine("No candidate node found after speed test.");
            return 1;
        }

        await ConfigHandler.SetDefaultServerIndex(_config!, selected.Profile.IndexId);
        _config!.SystemProxyItem.SysProxyType = ESysProxyType.ForcedChange;

        if (enableTun)
        {
            _config.TunModeItem.EnableTun = true;
        }

        await ConfigHandler.SaveConfig(_config);
        await SysProxyHandler.UpdateSysProxy(_config, false);
        await ReloadCoreAsync();

        Console.WriteLine("Auto pipeline completed.");
        Console.WriteLine($"Selected node: {selected.Profile.Remarks} ({selected.Profile.IndexId})");
        Console.WriteLine($"Delay: {selected.Delay} ms");
        Console.WriteLine($"Speed: {selected.Speed:0.##}");
        return 0;
    }

    private async Task<int> RunAutoLoopAsync(ParsedArgs parsed)
    {
        var intervalMinutes = parsed.GetIntOption("--interval-minutes", 1440);
        if (intervalMinutes <= 0)
        {
            Console.Error.WriteLine("--interval-minutes must be > 0");
            return 1;
        }

        var iterations = parsed.GetIntOption("--iterations", 0);
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var count = 0;
        while (!cts.IsCancellationRequested)
        {
            count++;
            Console.WriteLine($"[auto-loop] run #{count} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            var runParsed = parsed.WithoutFirstPositional();
            var result = await RunAutoPipelineAsync(runParsed);
            if (result != 0)
            {
                Console.Error.WriteLine("[auto-loop] run failed.");
            }

            if (iterations > 0 && count >= iterations)
            {
                break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Console.WriteLine("[auto-loop] stopped.");
        return 0;
    }

    private async Task<int> HandleStatusCommandAsync(ParsedArgs parsed)
    {
        await EnsureInitializedAsync();
        var _ = parsed;
        var running = await ConfigHandler.GetDefaultServer(_config!);
        Console.WriteLine($"Current node: {running?.Remarks ?? "(none)"} ({running?.IndexId ?? "-"})");
        Console.WriteLine($"System proxy: {_config!.SystemProxyItem.SysProxyType}");
        Console.WriteLine($"Tun enabled : {_config.TunModeItem.EnableTun}");
        Console.WriteLine($"Config path : {ServiceLib.Common.Utils.GetConfigPath()}");
        return 0;
    }

    private async Task ReloadCoreAsync()
    {
        var profileItem = await ConfigHandler.GetDefaultServer(_config!);
        if (profileItem == null)
        {
            throw new InvalidOperationException("No valid default node found.");
        }

        var allResult = await CoreConfigContextBuilder.BuildAll(_config!, profileItem);
        foreach (var warning in allResult.CombinedValidatorResult.Warnings)
        {
            Console.Error.WriteLine($"[warning] {warning}");
        }

        if (!allResult.Success)
        {
            var errors = string.Join("; ", allResult.CombinedValidatorResult.Errors);
            throw new InvalidOperationException($"Core config validation failed: {errors}");
        }

        await CoreManager.Instance.LoadCore(allResult.ResolvedMainContext, allResult.PreSocksResult?.Context);
        await SysProxyHandler.UpdateSysProxy(_config!, false);

        var socksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        if (!await WaitForLocalPortAsync(socksPort, 4000))
        {
            throw new InvalidOperationException($"Core reload did not expose socks port {socksPort}. Selected node may be unavailable.");
        }
    }

    private async Task RunSpeedActionAsync(ESpeedActionType actionType, List<ProfileItem> profiles, TimeSpan timeout)
    {
        if (profiles.Count == 0)
        {
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var speedtest = new SpeedtestService(_config!, async result =>
        {
            if (string.IsNullOrWhiteSpace(result.IndexId)
                && string.Equals(result.Delay, ServiceLib.Resx.ResUI.SpeedtestingCompleted, StringComparison.Ordinal))
            {
                tcs.TrySetResult(true);
            }
            await Task.CompletedTask;
        });

        speedtest.RunLoop(actionType, profiles);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout)) == tcs.Task;
        if (!done)
        {
            speedtest.ExitLoop();
            Console.Error.WriteLine($"[warning] speed action timeout after {timeout.TotalSeconds:0} seconds");
        }
    }

    private async Task RunGeoDetectAsync(List<NodeRow> rows)
    {
        Console.WriteLine($"[auto] geo detect start: {rows.Count} nodes");
        foreach (var row in rows)
        {
            var geo = await GeoIpService.QueryAsync(row.Profile.Address);
            ProfileExManager.Instance.SetGeoInfo(row.Profile.IndexId, geo);
            await Task.Delay(120);
        }
        await ProfileExManager.Instance.SaveTo();
        Console.WriteLine("[auto] geo detect done");
    }

    private async Task<AutoSelectionResult> SelectBestNodeAsync(List<NodeRow> sourceRows, int top, ESpeedActionType delayAction, int timeoutSeconds)
    {
        var result = new AutoSelectionResult();
        if (sourceRows.Count == 0)
        {
            return result;
        }

        var sourceIds = sourceRows.Select(r => r.Profile.IndexId).ToHashSet(StringComparer.Ordinal);
        await RunSpeedActionAsync(delayAction, sourceRows.Select(r => r.Profile).ToList(), TimeSpan.FromSeconds(timeoutSeconds));

        result.DelayRows = (await GetNodeRowsAsync(null))
            .Where(r => sourceIds.Contains(r.Profile.IndexId))
            .ToList();

        var delayedRows = result.DelayRows
            .Where(r => r.Delay > 0)
            .OrderBy(r => r.Delay)
            .Take(top)
            .ToList();

        if (delayedRows.Count == 0)
        {
            return result;
        }

        await RunSpeedActionAsync(ESpeedActionType.Speedtest, delayedRows.Select(r => r.Profile).ToList(), TimeSpan.FromSeconds(timeoutSeconds));

        result.SpeedRows = (await GetNodeRowsAsync(null))
            .Where(r => delayedRows.Any(d => d.Profile.IndexId == r.Profile.IndexId))
            .ToList();

        result.Selected = result.SpeedRows
            .OrderByDescending(r => r.Speed)
            .ThenBy(r => r.Delay <= 0 ? int.MaxValue : r.Delay)
            .FirstOrDefault();

        return result;
    }

    private static bool ShouldFallbackCountryRestriction(AutoSelectionResult result)
    {
        var delays = result.DelayRows;
        var speeds = result.SpeedRows;

        var allDelayBad = delays.Count > 0 && delays.All(r => r.Delay <= 0 || r.Delay > 600 || r.Delay == -1);
        var allSpeedBad = speeds.Count > 0 && speeds.All(r => r.Speed < 0.1m);
        return allDelayBad || allSpeedBad;
    }

    private static HashSet<string> ParseCountryFilters(ParsedArgs parsed)
    {
        var raw = parsed.GetOption("--countries") ?? parsed.GetOption("--country");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCountryMatched(NodeRow row, HashSet<string> filters)
    {
        if (filters.Count == 0)
        {
            return true;
        }

        var country = row.GeoCountry ?? string.Empty;
        var location = row.GeoLocation ?? string.Empty;
        foreach (var filter in filters)
        {
            if (country.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || location.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<string> ResolveSubscriptionIdAsync(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        var list = await AppManager.Instance.SubItems() ?? new List<SubItem>();
        if (int.TryParse(target, out var no))
        {
            if (no <= 0 || no > list.Count)
            {
                throw new InvalidOperationException($"Subscription number out of range: {target}");
            }
            return list[no - 1].Id;
        }

        var exact = list.FirstOrDefault(s => string.Equals(s.Id, target, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact.Id;
        }

        var matches = list.Where(s => s.Remarks.Contains(target, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
        {
            return matches[0].Id;
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"Subscription keyword '{target}' is ambiguous.");
        }

        throw new InvalidOperationException($"Subscription not found: {target}");
    }

    private async Task<List<NodeRow>> GetNodeRowsAsync(string? subId)
    {
        var profiles = await AppManager.Instance.ProfileItems(subId ?? string.Empty) ?? new List<ProfileItem>();
        var profileEx = await ProfileExManager.Instance.GetProfileExs();
        var exMap = profileEx.ToDictionary(t => t.IndexId, t => t);

        var ordered = profiles
            .Select(p =>
            {
                exMap.TryGetValue(p.IndexId, out var ex);
                return new NodeRow
                {
                    Profile = p,
                    Sort = ex?.Sort ?? 0,
                    Delay = ex?.Delay ?? 0,
                    Speed = ex?.Speed ?? 0,
                    GeoIP = ex?.GeoIP ?? string.Empty,
                    GeoCountry = ex?.GeoCountry ?? string.Empty,
                    GeoLocation = BuildGeoLocation(ex?.GeoCountry, ex?.GeoCity)
                };
            })
            .OrderBy(r => r.Sort)
            .ThenBy(r => r.Profile.Remarks, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].No = i + 1;
        }
        return ordered;
    }

    private static NodeRow? ResolveNode(List<NodeRow> rows, string target)
    {
        if (int.TryParse(target, out var no))
        {
            return rows.FirstOrDefault(r => r.No == no);
        }

        var exact = rows.FirstOrDefault(r => string.Equals(r.Profile.IndexId, target, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        var matches = rows.Where(r =>
            r.Profile.Remarks.Contains(target, StringComparison.OrdinalIgnoreCase)
            || r.Profile.GetSummary().Contains(target, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            Console.Error.WriteLine($"Node keyword '{target}' is ambiguous.");
            return null;
        }

        Console.Error.WriteLine($"Node not found: {target}");
        return null;
    }

    private static async Task<bool> WaitForLocalPortAsync(int port, int timeoutMs)
    {
        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            using var tcp = new TcpClient();
            try
            {
                var connectTask = tcp.ConnectAsync("127.0.0.1", port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(250));
                if (completed == connectTask && tcp.Connected)
                {
                    return true;
                }
            }
            catch
            {
                // ignore and retry
            }
            await Task.Delay(120);
        }
        return false;
    }

    private static List<NodeRow> ResolveNodes(List<NodeRow> rows, IReadOnlyList<string> targets)
    {
        var results = new List<NodeRow>();
        foreach (var raw in targets)
        {
            var pieces = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var target in pieces)
            {
                if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var row in rows)
                    {
                        if (!results.Any(r => r.Profile.IndexId == row.Profile.IndexId))
                        {
                            results.Add(row);
                        }
                    }
                    continue;
                }

                var resolved = ResolveNode(rows, target);
                if (resolved != null && !results.Any(r => r.Profile.IndexId == resolved.Profile.IndexId))
                {
                    results.Add(resolved);
                }
            }
        }
        return results;
    }

    private static void PrintJson(object model)
    {
        Console.WriteLine(JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("v2rayN.Cli - thin terminal controller for v2rayN");
        Console.WriteLine();
        Console.WriteLine("Subscription:");
        Console.WriteLine("  sub list [--json]");
        Console.WriteLine("  sub update [sub-id|no|remarks] [--proxy] [--no-reload]");
        Console.WriteLine();
        Console.WriteLine("Nodes:");
        Console.WriteLine("  node list [--sub <subId>] [--json]");
        Console.WriteLine("  node select <no|indexId|keyword> [--sub <subId>] [--no-reload]");
        Console.WriteLine();
        Console.WriteLine("Delay & speed:");
        Console.WriteLine("  delay all [--mode realping|tcping] [--sub <subId>] [--timeout <sec>] [--json]");
        Console.WriteLine("  delay <no|indexId|keyword> [...] [--mode realping|tcping] [--timeout <sec>] [--json]");
        Console.WriteLine("  speed <no|indexId|keyword> [--sub <subId>] [--timeout <sec>] [--json]");
        Console.WriteLine("  geo all | geo <no|indexId|keyword> [...] [--sub <subId>] [--json]");
        Console.WriteLine();
        Console.WriteLine("Runtime:");
        Console.WriteLine("  proxy status | proxy <on|off|none|pac>");
        Console.WriteLine("  tun status | tun <on|off> [--sudo-password <pwd>] [--no-reload]");
        Console.WriteLine("  status");
        Console.WriteLine();
        Console.WriteLine("Automation:");
        Console.WriteLine("  auto run [--sub <subId>] [--countries <country1,country2,...>] [--delay-mode realping|tcping] [--top <n>] [--proxy] [--enable-tun]");
        Console.WriteLine("  auto loop --interval-minutes <n> [--sub <subId>] [--countries <country1,country2,...>] [--delay-mode realping|tcping] [--top <n>] [--proxy] [--enable-tun] [--iterations <n>]");
    }

    private static string BuildGeoLocation(string? country, string? city)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return string.Empty;
        }
        return string.IsNullOrWhiteSpace(city) ? country : $"{country}/{city}";
    }
}

internal sealed class NodeRow
{
    public int No { get; set; }
    public int Sort { get; set; }
    public int Delay { get; set; }
    public decimal Speed { get; set; }
    public string GeoIP { get; set; } = string.Empty;
    public string GeoCountry { get; set; } = string.Empty;
    public string GeoLocation { get; set; } = string.Empty;
    public required ProfileItem Profile { get; set; }
}

internal sealed class AutoSelectionResult
{
    public NodeRow? Selected { get; set; }
    public List<NodeRow> DelayRows { get; set; } = [];
    public List<NodeRow> SpeedRows { get; set; } = [];
}

internal sealed class ParsedArgs
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positionals { get; } = new();

    public IReadOnlyList<string> PositionalsReadOnly => Positionals;

    public static ParsedArgs Parse(string[] args)
    {
        var parsed = new ParsedArgs();
        for (var i = 0; i < args.Length; i++)
        {
            var item = args[i];
            if (!item.StartsWith("--", StringComparison.Ordinal))
            {
                parsed.Positionals.Add(item);
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed._options[item] = args[i + 1];
                i++;
            }
            else
            {
                parsed._flags.Add(item);
            }
        }
        return parsed;
    }

    public ParsedArgs WithoutFirstPositional()
    {
        var clone = new ParsedArgs();
        foreach (var kv in _options)
        {
            clone._options[kv.Key] = kv.Value;
        }
        foreach (var flag in _flags)
        {
            clone._flags.Add(flag);
        }
        for (var i = 1; i < Positionals.Count; i++)
        {
            clone.Positionals.Add(Positionals[i]);
        }
        return clone;
    }

    public bool HasFlag(string name) => _flags.Contains(name);

    public string? GetOption(string name)
    {
        return _options.GetValueOrDefault(name);
    }

    public int GetIntOption(string name, int defaultValue)
    {
        var val = GetOption(name);
        return int.TryParse(val, out var parsed) ? parsed : defaultValue;
    }

    public string? PositionalAt(int index)
    {
        return index >= 0 && index < Positionals.Count ? Positionals[index] : null;
    }
}
