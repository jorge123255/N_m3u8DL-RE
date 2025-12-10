using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Util;
using System.Collections.Concurrent;

namespace N_m3u8DL_RE.DownloadManager;

/// <summary>
/// Live streaming manager that outputs decrypted segments to stdout
/// Designed for piping to FFmpeg for real-time HLS generation
/// No large files - each segment is deleted after being written to stdout
/// </summary>
internal class LivePipeStreamManager
{
    private readonly IDownloader _downloader;
    private readonly DownloaderConfig _config;
    private readonly StreamExtractor _streamExtractor;
    private readonly List<StreamSpec> _selectedStreams;
    private bool _stopFlag = false;
    private string? _currentKID;
    private string _mp4InitFile = "";
    private readonly ConcurrentDictionary<long, bool> _processedSegments = new();

    public LivePipeStreamManager(DownloaderConfig config, List<StreamSpec> selectedStreams, StreamExtractor streamExtractor)
    {
        _config = config;
        _downloader = new SimpleDownloader(config);
        _streamExtractor = streamExtractor;
        _selectedStreams = selectedStreams;
    }

    public async Task<bool> StartStreamAsync()
    {
        // For live stdout mode, we only handle the best video+audio stream
        var videoStream = _selectedStreams.FirstOrDefault(s => s.MediaType == MediaType.VIDEO);
        var audioStream = _selectedStreams.FirstOrDefault(s => s.MediaType == MediaType.AUDIO);

        if (videoStream == null && audioStream == null)
        {
            Logger.Error("No video or audio stream selected");
            return false;
        }

        var primaryStream = videoStream ?? audioStream!;
        var tmpDir = Path.Combine(Path.GetTempPath(), $"n3u8_live_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            // Write init segment to stdout first (if exists)
            if (primaryStream.Playlist?.MediaInit != null)
            {
                var initPath = Path.Combine(tmpDir, "_init.mp4");
                var initResult = await _downloader.DownloadSegmentAsync(
                    primaryStream.Playlist.MediaInit, initPath, new SpeedContainer(), _config.Headers);

                if (initResult?.Success == true)
                {
                    // Get KID for decryption
                    _currentKID = MP4DecryptUtil.GetMP4Info(initResult.ActualFilePath).KID;
                    
                    // Decrypt if needed
                    if (!string.IsNullOrEmpty(_currentKID) && _config.MyOptions.Keys?.Length > 0)
                    {
                        var decPath = Path.Combine(tmpDir, "_init_dec.mp4");
                        var decrypted = await MP4DecryptUtil.DecryptAsync(
                            _config.MyOptions.DecryptionEngine,
                            _config.MyOptions.DecryptionBinaryPath!,
                            _config.MyOptions.Keys,
                            initResult.ActualFilePath,
                            decPath,
                            _currentKID);
                        
                        if (decrypted)
                        {
                            _mp4InitFile = initResult.ActualFilePath;
                            await WriteToStdoutAsync(decPath);
                            File.Delete(decPath);
                        }
                        else
                        {
                            await WriteToStdoutAsync(initResult.ActualFilePath);
                        }
                    }
                    else
                    {
                        await WriteToStdoutAsync(initResult.ActualFilePath);
                    }
                    File.Delete(initResult.ActualFilePath);
                }
            }

            // Main loop - continuously fetch and process segments
            long lastIndex = -1;
            var waitSec = _config.MyOptions.LiveWaitTime ?? 2;

            while (!_stopFlag)
            {
                try
                {
                    // Refresh playlist
                    await _streamExtractor.FetchPlayListAsync(_selectedStreams);
                    
                    var playlist = primaryStream.Playlist;
                    if (playlist?.MediaParts == null) 
                    {
                        await Task.Delay(waitSec * 1000);
                        continue;
                    }

                    // Get new segments
                    var segments = playlist.MediaParts
                        .SelectMany(p => p.MediaSegments)
                        .Where(s => s.Index > lastIndex && !_processedSegments.ContainsKey(s.Index))
                        .OrderBy(s => s.Index)
                        .ToList();

                    foreach (var segment in segments)
                    {
                        if (_stopFlag) break;
                        
                        await ProcessSegmentAsync(segment, tmpDir);
                        lastIndex = Math.Max(lastIndex, segment.Index);
                        _processedSegments[segment.Index] = true;

                        // Check record limit
                        if (_config.MyOptions.LiveRecordLimit != null)
                        {
                            var totalDuration = _processedSegments.Count * (segment.Duration);
                            if (totalDuration >= _config.MyOptions.LiveRecordLimit.Value.TotalSeconds)
                            {
                                Logger.Warn($"Record limit reached: {_config.MyOptions.LiveRecordLimit}");
                                _stopFlag = true;
                                break;
                            }
                        }
                    }

                    // Cleanup old segment tracking (keep last 1000)
                    if (_processedSegments.Count > 1000)
                    {
                        var toRemove = _processedSegments.Keys.OrderBy(k => k).Take(500).ToList();
                        foreach (var key in toRemove)
                            _processedSegments.TryRemove(key, out _);
                    }

                    await Task.Delay(waitSec * 1000);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in live stream loop: {ex.Message}");
                    await Task.Delay(waitSec * 1000);
                }
            }

            return true;
        }
        finally
        {
            // Cleanup
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private async Task ProcessSegmentAsync(MediaSegment segment, string tmpDir)
    {
        var segPath = Path.Combine(tmpDir, $"seg_{segment.Index}.tmp");
        
        try
        {
            // Download segment
            var result = await _downloader.DownloadSegmentAsync(
                segment, segPath, new SpeedContainer(), _config.Headers);

            if (result?.Success != true)
            {
                Logger.Warn($"Failed to download segment {segment.Index}");
                return;
            }

            // Decrypt if encrypted
            if (segment.IsEncrypted && !string.IsNullOrEmpty(_currentKID) && _config.MyOptions.Keys?.Length > 0)
            {
                var decPath = Path.Combine(tmpDir, $"seg_{segment.Index}_dec.tmp");
                var decrypted = await MP4DecryptUtil.DecryptAsync(
                    _config.MyOptions.DecryptionEngine,
                    _config.MyOptions.DecryptionBinaryPath!,
                    _config.MyOptions.Keys,
                    result.ActualFilePath,
                    decPath,
                    _currentKID,
                    _mp4InitFile);

                if (decrypted)
                {
                    await WriteToStdoutAsync(decPath);
                    File.Delete(decPath);
                }
                else
                {
                    // Decryption failed, write encrypted (will fail downstream)
                    await WriteToStdoutAsync(result.ActualFilePath);
                }
            }
            else
            {
                // Not encrypted, write directly
                await WriteToStdoutAsync(result.ActualFilePath);
            }
        }
        finally
        {
            // Always cleanup segment file
            try { File.Delete(segPath); } catch { }
        }
    }

    private async Task WriteToStdoutAsync(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        await fs.CopyToAsync(Console.OpenStandardOutput());
        await Console.OpenStandardOutput().FlushAsync();
    }

    public void Stop()
    {
        _stopFlag = true;
    }
}
