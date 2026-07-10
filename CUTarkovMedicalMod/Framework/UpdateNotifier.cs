using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 更新提醒逻辑（非 MonoBehaviour，由 Plugin 驱动）。
/// 游戏启动时异步检查 GitHub Releases 最新版本，
/// 若本地版本低于远程版本，在主菜单左上角显示更新提醒。
/// 支持「自动更新」（下载 zip → 暂存 → 生成批处理 → 重启游戏）
/// 和「跳转下载页面」两种方式。
/// </summary>
public sealed class UpdateNotifier
{
    private const string GitHubApiUrl =
        "https://api.github.com/repos/hmm1313133/CUTarkovMedicalMod/releases/latest";

    private const string ReleasePageUrl =
        "https://github.com/hmm1313133/CUTarkovMedicalMod/releases/tag/release";

    private const float CheckDelaySeconds = 2f;

    /// <summary>下载停滞超时（连续 N 秒无数据则判定失败）</summary>
    private const int StallTimeoutSeconds = 30;

    /// <summary>整体下载超时上限</summary>
    private const int OverallTimeoutSeconds = 300;

    // ── 状态机 ──────────────────────────────────────────────

    private enum UpdateState
    {
        Idle,
        Checking,
        UpToDate,
        UpdateAvailable,
        Downloading,
        Installing,
        Restarting,
        UpdateFailed,
        UpdateCancelled,
        Error
    }

    private UpdateState _state = UpdateState.Idle;
    private string _statusText = "";
    private string _detailText = "";
    private string _remoteVersion = "";
    private string _downloadUrl = "";   // zip asset 直链
    private bool _dismissed;

    // ── 下载进度（跨线程，用 volatile）─────────────────────

    private volatile float _downloadProgress;     // 0..1，-1 表示未知大小
    private volatile string _downloadProgressText = "";
    private CancellationTokenSource? _cts;
    private long _totalBytes = -1;
    private long _downloadedBytes;

    // ── 版本检查计时 ────────────────────────────────────────

    private float _timer;
    private bool _checkStarted;
    private bool _guiLogged;

    /// <summary>由 Plugin.Update() 每帧调用</summary>
    public void Tick()
    {
        if (_checkStarted) return;

        _timer += Time.deltaTime;
        if (_timer < CheckDelaySeconds) return;

        _checkStarted = true;
        Plugin.Log.LogInfo($"[UpdateNotifier] Starting update check (local version: {Plugin.ModVersion})...");
        _ = BeginCheckAsync();
    }

    // ── 版本检查 ────────────────────────────────────────────

    private async Task BeginCheckAsync()
    {
        _state = UpdateState.Checking;
        _statusText = "Checking for updates...";

        try
        {
            var (remoteVersion, downloadUrl) = await FetchLatestReleaseAsync();
            _remoteVersion = remoteVersion;
            _downloadUrl = downloadUrl;

            if (string.IsNullOrEmpty(remoteVersion))
            {
                _state = UpdateState.Error;
                _statusText = "Could not retrieve latest version info.";
                return;
            }

            bool outdated = IsVersionOutdated(Plugin.ModVersion, remoteVersion);

            if (outdated)
            {
                _state = UpdateState.UpdateAvailable;
                _statusText = $"New version: v{remoteVersion} (current: v{Plugin.ModVersion})";
                Plugin.Log.LogInfo($"[UpdateNotifier] Update found: v{remoteVersion} (current: v{Plugin.ModVersion})");
            }
            else
            {
                _state = UpdateState.UpToDate;
                _statusText = $"Up to date (v{Plugin.ModVersion})";
                Plugin.Log.LogInfo($"[UpdateNotifier] Up to date: v{Plugin.ModVersion}");
            }
        }
        catch (Exception ex)
        {
            _state = UpdateState.Error;
            _statusText = "";
            Plugin.Log.LogWarning($"[UpdateNotifier] Update check failed: {ex.Message}");
        }
    }

    private static async Task<(string version, string url)> FetchLatestReleaseAsync()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CUTarkovMedicalMod-UpdateChecker/1.0");
        client.Timeout = TimeSpan.FromSeconds(10);

        var response = await client.GetAsync(GitHubApiUrl);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var obj = JObject.Parse(json);

        string tagName = obj["tag_name"]?.ToString() ?? "";
        string version = tagName.TrimStart('v').Trim();

        // 查找 zip asset 下载链接
        string downloadUrl = "";
        string assetVersion = "";
        var assets = obj["assets"] as JArray;
        if (assets != null)
        {
            foreach (var asset in assets)
            {
                string name = asset["name"]?.ToString() ?? "";
                if (name.IndexOf("CUTarkovMedicalMod", StringComparison.OrdinalIgnoreCase) >= 0
                    && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset["browser_download_url"]?.ToString() ?? "";

                    // 从文件名中提取版本号（如 CUTarkovMedicalMod_v0.1.0.zip -> 0.1.0）
                    int vIdx = name.IndexOf("_v", StringComparison.OrdinalIgnoreCase);
                    if (vIdx >= 0)
                    {
                        string sub = name.Substring(vIdx + 2);
                        int dotZip = sub.LastIndexOf(".zip", StringComparison.OrdinalIgnoreCase);
                        if (dotZip > 0)
                            assetVersion = sub.Substring(0, dotZip);
                    }

                    break;
                }
            }
        }

        // tag_name 不是数字版本号时，使用从 asset 文件名提取的版本
        if (!IsNumericVersion(version) && !string.IsNullOrEmpty(assetVersion))
        {
            version = assetVersion;
        }

        // 如果没有找到 zip，用 release html_url 作为跳转
        if (string.IsNullOrEmpty(downloadUrl))
        {
            downloadUrl = obj["html_url"]?.ToString() ?? "";
        }

        return (version, downloadUrl);
    }

    /// <summary>
    /// 简单版本比较：按 . 分隔后逐段比较数值。
    /// </summary>
    private static bool IsVersionOutdated(string local, string remote)
    {
        if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(remote))
            return false;

        var localParts = local.Split('.');
        var remoteParts = remote.Split('.');
        int maxLen = Math.Max(localParts.Length, remoteParts.Length);

        for (int i = 0; i < maxLen; i++)
        {
            int l = i < localParts.Length && int.TryParse(localParts[i], out var lv) ? lv : 0;
            int r = i < remoteParts.Length && int.TryParse(remoteParts[i], out var rv) ? rv : 0;

            if (r > l) return true;
            if (r < l) return false;
        }

        return false; // 完全相等
    }

    /// <summary>
    /// 判断字符串是否为数字版本号（如 "0.2.0" 或 "0.2.0.0"）。
    /// </summary>
    private static bool IsNumericVersion(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var parts = s.Split('.');
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out _)) return false;
        }
        return parts.Length > 0;
    }

    // ── 自动更新流程 ─────────────────────────────────────────

    private async void StartAutoUpdate()
    {
        // 检查是否有直接下载链接
        if (string.IsNullOrEmpty(_downloadUrl) || !IsZipDownloadUrl(_downloadUrl))
        {
            _state = UpdateState.UpdateFailed;
            _statusText = "Auto-update not available for this release.";
            _detailText = "Please use 'Release Page' to download manually.";
            return;
        }

        // 清理旧的临时文件
        CleanupOldTempFiles();

        _cts = new CancellationTokenSource();
        _state = UpdateState.Downloading;
        _downloadProgress = 0f;
        _downloadedBytes = 0;
        _totalBytes = -1;
        _statusText = "Downloading update...";
        _detailText = "";

        try
        {
            string zipPath = await DownloadAsync(_downloadUrl, _cts.Token);

            _state = UpdateState.Installing;
            _statusText = "Installing update...";

            string modDir = GetModDirectory();
            string stagingDir = Path.Combine(modDir, ".staging");

            // 清理旧暂存目录
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
            Directory.CreateDirectory(stagingDir);

            ExtractZip(zipPath, stagingDir);
            FlattenIfSingleSubdir(stagingDir);

            // 删除临时 zip
            try { File.Delete(zipPath); } catch { }

            _state = UpdateState.Restarting;
            _statusText = "Restarting game...";

            LaunchUpdateScript(stagingDir, modDir);

            Plugin.Log.LogInfo("[UpdateNotifier] Update script launched. Quitting game...");
            Application.Quit();
        }
        catch (OperationCanceledException)
        {
            _state = UpdateState.UpdateCancelled;
            _statusText = "Update cancelled by user.";
            _detailText = "";
            Plugin.Log.LogInfo("[UpdateNotifier] Update cancelled by user.");
        }
        catch (TimeoutException ex)
        {
            _state = UpdateState.UpdateFailed;
            _statusText = "Download timed out.";
            _detailText = ex.Message;
            Plugin.Log.LogWarning($"[UpdateNotifier] Download timeout: {ex.Message}");
        }
        catch (Exception ex)
        {
            _state = UpdateState.UpdateFailed;
            _statusText = "Update failed.";
            _detailText = ex.Message.Length > 200 ? ex.Message.Substring(0, 200) : ex.Message;
            Plugin.Log.LogError($"[UpdateNotifier] Auto-update failed: {ex}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 下载 zip 到临时文件，实时报告进度，支持取消和停滞超时。
    /// </summary>
    private async Task<string> DownloadAsync(string url, CancellationToken ct)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CUTarkovMedicalMod-UpdateChecker/1.0");
        client.Timeout = TimeSpan.FromSeconds(OverallTimeoutSeconds);

        string tempZip = Path.Combine(
            Path.GetTempPath(),
            $"CUTarkovMedicalMod_update_{DateTime.UtcNow.Ticks}.zip");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        _totalBytes = response.Content.Headers.ContentLength ?? -1;
        _downloadedBytes = 0;

        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write,
            FileShare.None, 8192, useAsync: true);

        var buffer = new byte[81920];
        long lastSpeedCheckBytes = 0;
        var lastSpeedCheckTime = DateTime.UtcNow;

        while (true)
        {
            // 带停滞超时的读取
            var readTask = contentStream.ReadAsync(buffer, 0, buffer.Length, ct);
            var delayTask = Task.Delay(TimeSpan.FromSeconds(StallTimeoutSeconds), ct);
            var completed = await Task.WhenAny(readTask, delayTask);

            if (completed == delayTask && !ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"No data received for {StallTimeoutSeconds} seconds.");
            }

            int bytesRead = await readTask;
            if (bytesRead == 0) break;

            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            _downloadedBytes += bytesRead;

            // 更新进度
            if (_totalBytes > 0)
                _downloadProgress = (float)_downloadedBytes / _totalBytes;
            else
                _downloadProgress = -1f;

            // 每 0.5 秒更新速度文本
            var now = DateTime.UtcNow;
            var elapsed = (now - lastSpeedCheckTime).TotalSeconds;
            if (elapsed >= 0.5)
            {
                long deltaBytes = _downloadedBytes - lastSpeedCheckBytes;
                double speed = deltaBytes / elapsed;
                _downloadProgressText = _totalBytes > 0
                    ? $"{FormatBytes(_downloadedBytes)} / {FormatBytes(_totalBytes)}  ({FormatBytes(speed)}/s)"
                    : $"{FormatBytes(_downloadedBytes)}  ({FormatBytes(speed)}/s)";
                lastSpeedCheckBytes = _downloadedBytes;
                lastSpeedCheckTime = now;
            }
        }

        Plugin.Log.LogInfo(
            $"[UpdateNotifier] Download complete: {FormatBytes(_downloadedBytes)} -> {tempZip}");
        return tempZip;
    }

    /// <summary>
    /// 解压 zip 到目标目录（仅依赖 System.IO.Compression，不需要 FileSystem 程序集）。
    /// </summary>
    private static void ExtractZip(string zipPath, string extractDir)
    {
        string fullExtractDir = Path.GetFullPath(extractDir);

        using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(extractDir, relativePath);
            string targetPath = Path.GetFullPath(fullPath);

            // 防止路径穿越（zip slip）
            if (!targetPath.StartsWith(fullExtractDir, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.LogWarning($"[UpdateNotifier] Skipping suspicious entry: {entry.FullName}");
                continue;
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            string? dir = Path.GetDirectoryName(fullPath);
            if (dir != null) Directory.CreateDirectory(dir);

            using var entryStream = entry.Open();
            using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
            entryStream.CopyTo(fileStream);
        }
    }

    /// <summary>
    /// 如果暂存目录只包含一个子目录（无文件），将子目录内容提升到根级。
    /// </summary>
    private static void FlattenIfSingleSubdir(string dir)
    {
        var entries = Directory.GetFileSystemEntries(dir);
        if (entries.Length == 1 && Directory.Exists(entries[0]))
        {
            string subDir = entries[0];
            foreach (var entry in Directory.GetFileSystemEntries(subDir))
            {
                string dest = Path.Combine(dir, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                    Directory.Move(entry, dest);
                else
                    File.Move(entry, dest);
            }
            Directory.Delete(subDir, true);
        }
    }

    /// <summary>
    /// 获取当前 mod 所在目录（DLL 所在目录）。
    /// </summary>
    private static string GetModDirectory()
    {
        string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        return Path.GetDirectoryName(dllPath) ?? ".";
    }

    /// <summary>
    /// 生成并启动批处理脚本：等待游戏退出 → 覆盖文件 → 重启游戏。
    /// </summary>
    private void LaunchUpdateScript(string stagingDir, string modDir)
    {
        int gamePid = Process.GetCurrentProcess().Id;
        string gameExe = GetGameExecutablePath();
        string gameDir = Path.GetDirectoryName(gameExe) ?? modDir;
        string? steamAppId = TryFindSteamAppId(gameDir);

        string batPath = Path.Combine(modDir, ".update.bat");

        var lines = new List<string>
        {
            "@echo off",
            "chcp 65001 >NUL 2>NUL",
            ":: Auto-generated update script for CUTarkovMedicalMod",
            $":: Waiting for game (PID {gamePid}) to exit...",
            "",
            ":wait",
            $"tasklist /FI \"PID eq {gamePid}\" 2>NUL | find \"{gamePid}\" >NUL",
            "if not errorlevel 1 (",
            "    timeout /t 1 /nobreak >NUL",
            "    goto wait",
            ")",
            "",
            ":: Extra delay to ensure file locks are released",
            "timeout /t 2 /nobreak >NUL",
            "",
            ":: Copy staging files over mod directory",
            $"xcopy /E /Y /I \"{stagingDir}\\*\" \"{modDir}\\\"",
            $"rmdir /S /Q \"{stagingDir}\"",
            "",
        };

        if (!string.IsNullOrEmpty(steamAppId))
        {
            lines.AddRange(new[]
            {
                ":: Restart game via Steam (ensures correct working dir + BepInEx init)",
                $"start \"\" \"steam://run/{steamAppId}\"",
                "",
                ":: Wait for game to start, then self-delete",
                "timeout /t 5 /nobreak >NUL",
                "del \"%~f0\"",
            });
        }
        else
        {
            lines.AddRange(new[]
            {
                ":: Restart game from game root directory",
                $"cd /d \"{gameDir}\"",
                $"start \"\" \"{gameExe}\"",
                "",
                ":: Self-delete",
                "del \"%~f0\"",
            });
        }

        File.WriteAllLines(batPath, lines);

        Plugin.Log.LogInfo($"[UpdateNotifier] Update bat: {batPath}");
        Plugin.Log.LogInfo($"[UpdateNotifier] Game exe: {gameExe}, Game dir: {gameDir}, Steam AppID: {steamAppId ?? "(not found)"}");

        var psi = new ProcessStartInfo
        {
            FileName = batPath,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = modDir,
        };

        Process.Start(psi);
    }

    /// <summary>
    /// 获取游戏可执行文件路径。
    /// </summary>
    private static string GetGameExecutablePath()
    {
        try
        {
            string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return exe;
        }
        catch { }

        // 回退：从 Application.dataPath 推导
        try
        {
            string dataPath = Application.dataPath;
            string parent = Directory.GetParent(dataPath)?.FullName ?? "";
            string gameName = Path.GetFileName(dataPath);
            if (gameName.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
                gameName = gameName.Substring(0, gameName.Length - 5);
            return Path.Combine(parent, gameName + ".exe");
        }
        catch { }

        return "";
    }

    /// <summary>
    /// 尝试从 Steam appmanifest 文件中查找游戏的 App ID。
    /// </summary>
    private static string? TryFindSteamAppId(string gameDir)
    {
        try
        {
            var commonDir = Directory.GetParent(gameDir);
            if (commonDir == null) return null;
            var steamappsDir = commonDir.Parent;
            if (steamappsDir == null) return null;
            if (!steamappsDir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                return null;

            // 当前游戏目录名（如 "Casualties Unknown Demo"）
            string gameDirName = Path.GetFileName(gameDir.TrimEnd('\\', '/'));

            foreach (var acf in Directory.GetFiles(steamappsDir.FullName, "appmanifest_*.acf"))
            {
                string content = File.ReadAllText(acf);

                // 先检查 installdir 是否匹配当前游戏目录
                var installMatch = System.Text.RegularExpressions.Regex.Match(
                    content, @"""installdir""\s+""([^""]+)""");
                if (!installMatch.Success) continue;
                if (!string.Equals(installMatch.Groups[1].Value, gameDirName,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                // 匹配成功，提取 appid
                var idMatch = System.Text.RegularExpressions.Regex.Match(
                    content, @"""appid""\s+""(\d+)""");
                if (idMatch.Success)
                    return idMatch.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[UpdateNotifier] Failed to find Steam AppID: {ex.Message}");
        }
        return null;
    }

    // ── 辅助方法 ────────────────────────────────────────────

    private static bool IsZipDownloadUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes < 1024) return $"{bytes:F0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024):F1} MB";
        return $"{bytes / (1024 * 1024 * 1024):F2} GB";
    }

    private static void CleanupOldTempFiles()
    {
        try
        {
            foreach (var f in Directory.GetFiles(
                Path.GetTempPath(), "CUTarkovMedicalMod_update_*.zip"))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    // ── IMGUI 绘制（由 Plugin.OnGUI() 调用）──────────────────

    public void OnGUI()
    {
        if (!_guiLogged)
        {
            _guiLogged = true;
            Plugin.Log.LogInfo(
                $"[UpdateNotifier] OnGUI first frame. State={_state}, Dismissed={_dismissed}, IsInGame={IsInGame()}");
        }

        if (_dismissed) return;

        bool shouldShow = _state == UpdateState.UpdateAvailable
            || _state == UpdateState.Downloading
            || _state == UpdateState.Installing
            || _state == UpdateState.Restarting
            || _state == UpdateState.UpdateFailed
            || _state == UpdateState.UpdateCancelled;

        if (!shouldShow) return;

        // 下载中/安装中/重启中：即使进入游戏也继续显示
        bool inGame = IsInGame();
        if (inGame && _state == UpdateState.UpdateAvailable) return;
        if (inGame && (_state == UpdateState.UpdateFailed || _state == UpdateState.UpdateCancelled))
            return;

        DrawUpdateNotification();
    }

    private void DrawUpdateNotification()
    {
        float x = 10f;
        float y = 10f;
        float width = 380f;
        float height = GetPanelHeight();

        var rect = new Rect(x, y, width, height);

        // 半透明背景
        var oldColor = GUI.color;
        GUI.color = new Color(0.12f, 0.12f, 0.15f, 0.92f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = oldColor;

        // 边框
        var borderColor = GetBorderColor();
        DrawBorder(rect, borderColor, 2f);

        // 标题
        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft
        };
        titleStyle.normal.textColor = borderColor;

        var titleRect = new Rect(x + 10, y + 6, width - 60, 20);
        GUI.Label(titleRect, GetTitle(), titleStyle);

        // 关闭按钮（下载/安装/重启中不显示）
        if (CanClose())
        {
            var closeRect = new Rect(x + width - 28, y + 4, 22, 22);
            if (GUI.Button(closeRect, "X"))
            {
                _dismissed = true;
                return;
            }
        }

        // 状态文本
        var bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
        bodyStyle.normal.textColor = Color.white;

        var bodyRect = new Rect(x + 10, y + 28, width - 20, 20);
        GUI.Label(bodyRect, _statusText, bodyStyle);

        float currentY = y + 50;

        // ── 下载中：进度条 + Cancel 按钮 ──
        if (_state == UpdateState.Downloading)
        {
            currentY = DrawProgressBar(x, currentY, width);
            currentY += 6;

            var cancelRect = new Rect(x + 10, currentY, 130, 24);
            if (GUI.Button(cancelRect, "Cancel Download"))
            {
                _cts?.Cancel();
            }
            currentY += 28;
        }

        // ── 错误详情 ──
        if (!string.IsNullOrEmpty(_detailText))
        {
            var detailStyle = new GUIStyle(bodyStyle) { fontSize = 11 };
            detailStyle.normal.textColor = new Color(1f, 0.5f, 0.5f);
            var detailRect = new Rect(x + 10, currentY, width - 20, 28);
            GUI.Label(detailRect, _detailText, detailStyle);
            currentY += 30;
        }

        // ── 按钮行 ──
        DrawButtons(x, currentY, width);
    }

    private float DrawProgressBar(float x, float y, float width)
    {
        float barWidth = width - 20;
        float barHeight = 22;

        // 背景
        var oldColor = GUI.color;
        GUI.color = new Color(0.2f, 0.2f, 0.25f, 1f);
        GUI.DrawTexture(new Rect(x + 10, y, barWidth, barHeight), Texture2D.whiteTexture);
        GUI.color = oldColor;

        if (_downloadProgress >= 0)
        {
            // 确定性进度条
            float fillWidth = barWidth * Mathf.Clamp01(_downloadProgress);
            GUI.color = new Color(0.3f, 0.7f, 1f, 1f);
            GUI.DrawTexture(new Rect(x + 10, y, fillWidth, barHeight), Texture2D.whiteTexture);
            GUI.color = oldColor;
        }
        else
        {
            // 不确定大小 — 移动指示器
            float t = (Time.realtimeSinceStartup % 1.5f) / 1.5f;
            float indWidth = 80f;
            float indX = x + 10 + t * (barWidth - indWidth);
            GUI.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(indX, y, indWidth, barHeight), Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        // 进度文本
        var progStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter
        };
        progStyle.normal.textColor = Color.white;

        string progText;
        if (_downloadProgress >= 0)
            progText = $"{_downloadProgress * 100:F0}%  |  {_downloadProgressText}";
        else
            progText = _downloadProgressText;

        GUI.Label(new Rect(x + 10, y, barWidth, barHeight), progText, progStyle);

        return y + barHeight;
    }

    private void DrawButtons(float x, float y, float width)
    {
        switch (_state)
        {
            case UpdateState.UpdateAvailable:
                DrawUpdateAvailableButtons(x, y);
                break;

            case UpdateState.UpdateFailed:
            case UpdateState.UpdateCancelled:
                DrawFailedCancelledButtons(x, y);
                break;
        }
    }

    private void DrawUpdateAvailableButtons(float x, float y)
    {
        // Auto Update
        if (GUI.Button(new Rect(x + 10, y, 130, 24), "Auto Update"))
        {
            StartAutoUpdate();
        }

        // Release Page
        if (GUI.Button(new Rect(x + 150, y, 120, 24), "Release Page"))
        {
            Application.OpenURL(ReleasePageUrl);
        }

        // Remind Later
        if (GUI.Button(new Rect(x + 280, y, 70, 24), "Later"))
        {
            _dismissed = true;
        }
    }

    private void DrawFailedCancelledButtons(float x, float y)
    {
        // Retry / Try Again
        string retryLabel = _state == UpdateState.UpdateCancelled ? "Try Again" : "Retry";
        if (GUI.Button(new Rect(x + 10, y, 100, 24), retryLabel))
        {
            StartAutoUpdate();
        }

        // Dismiss
        if (GUI.Button(new Rect(x + 120, y, 80, 24), "Dismiss"))
        {
            _dismissed = true;
        }

        // Release Page
        if (GUI.Button(new Rect(x + 210, y, 120, 24), "Release Page"))
        {
            Application.OpenURL(ReleasePageUrl);
        }
    }

    private float GetPanelHeight()
    {
        switch (_state)
        {
            case UpdateState.UpdateAvailable:
                return 82f;
            case UpdateState.Downloading:
                return 110f;
            case UpdateState.Installing:
            case UpdateState.Restarting:
                return 58f;
            case UpdateState.UpdateFailed:
                return _detailText.Length > 0 ? 114f : 84f;
            case UpdateState.UpdateCancelled:
                return 82f;
            default:
                return 82f;
        }
    }

    private Color GetBorderColor()
    {
        switch (_state)
        {
            case UpdateState.Downloading:
            case UpdateState.Installing:
            case UpdateState.Restarting:
                return new Color(0.3f, 0.7f, 1f, 0.9f);
            case UpdateState.UpdateFailed:
                return new Color(1f, 0.3f, 0.3f, 0.9f);
            case UpdateState.UpdateCancelled:
                return new Color(0.9f, 0.8f, 0.3f, 0.9f);
            default:
                return new Color(1f, 0.65f, 0.2f, 0.9f);
        }
    }

    private string GetTitle()
    {
        switch (_state)
        {
            case UpdateState.Downloading:
                return "Downloading Update...";
            case UpdateState.Installing:
                return "Installing Update...";
            case UpdateState.Restarting:
                return "Restarting Game...";
            case UpdateState.UpdateFailed:
                return "Update Failed";
            case UpdateState.UpdateCancelled:
                return "Update Cancelled";
            default:
                return "CUTarkov Medical Mod - Update Available";
        }
    }

    private bool CanClose()
    {
        return _state == UpdateState.UpdateAvailable
            || _state == UpdateState.UpdateFailed
            || _state == UpdateState.UpdateCancelled;
    }

    private static void DrawBorder(Rect rect, Color color, float thickness)
    {
        var oldColor = GUI.color;
        GUI.color = color;

        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);

        GUI.color = oldColor;
    }

    /// <summary>
    /// 判断是否在游戏内（非主菜单）。
    /// </summary>
    private static bool IsInGame()
    {
        try
        {
            var worldType = Type.GetType("WorldGeneration, Assembly-CSharp");
            if (worldType == null) return false;

            var field = worldType.GetField("world", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (field == null) return false;

            var world = field.GetValue(null);
            return world != null;
        }
        catch
        {
            return false;
        }
    }
}
