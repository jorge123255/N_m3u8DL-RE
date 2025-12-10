# N_m3u8DL-RE

A cross-platform command-line tool for downloading DASH, HLS, and MSS streaming content. Supports both on-demand videos and live stream recording.

## Features

- **Multi-Protocol Support**: Download DASH (MPD), HLS (M3U8), and Microsoft Smooth Streaming (ISM)
- **Live Stream Recording**: Capture live broadcasts with real-time merging
- **Stream Selection**: Filter video, audio, and subtitle tracks using regex patterns
- **Encryption Handling**: Decrypt DRM content using mp4decrypt, shaka-packager, or ffmpeg
- **Concurrent Downloads**: Download audio, video, and subtitles simultaneously
- **Automatic Muxing**: Combine separated media files after download
- **Speed Control**: Rate limiting with Mbps/Kbps support
- **Output Customization**: Variable-based naming patterns (resolution, bandwidth, language, codec)

## Installation

### Pre-built Releases
Download the latest release from the [Releases](https://github.com/jorge123255/N_m3u8DL-RE/releases) page.

### Arch Linux
```bash
# AUR packages available
yay -S n-m3u8dl-re-bin
# or
yay -S n-m3u8dl-re-git
```

### Build from Source
```bash
dotnet publish -r linux-x64 -c Release
```

## Platform Notes

- **Windows**: Older Windows terminals may not support this program. Use [Cmder](https://cmder.app/) or Windows Terminal as an alternative.
- **Linux**: Requires .NET runtime or use the self-contained builds
- **macOS**: Use the osx-x64 or osx-arm64 builds

## Basic Usage

```bash
# Download a stream
N_m3u8DL-RE "https://example.com/playlist.m3u8"

# Download with decryption key
N_m3u8DL-RE "https://example.com/manifest.mpd" --key KID:KEY

# Record live stream
N_m3u8DL-RE "https://example.com/live.m3u8" --live-real-time-merge

# Select specific quality
N_m3u8DL-RE "URL" -sv best -sa best
```

## Key Options

| Option | Description |
|--------|-------------|
| `--key` | Decryption key in KID:KEY format |
| `-sv`, `--select-video` | Video track selection (regex or "best") |
| `-sa`, `--select-audio` | Audio track selection (regex or "best") |
| `-ss`, `--select-subtitle` | Subtitle track selection |
| `--save-dir` | Output directory |
| `--save-name` | Output filename |
| `--live-real-time-merge` | Enable real-time merging for live streams |
| `--live-record-limit` | Recording duration limit (HH:MM:SS) |
| `-M`, `--mux-after-done` | Mux format after download (e.g., `format=ts`) |
| `--append-url-params` | Append URL parameters to segment requests |
| `--tmp-dir` | Temporary directory for downloads |

## Live Recording Example

```bash
N_m3u8DL-RE "https://live.example.com/stream.mpd" \
  --key KID:KEY \
  --live-real-time-merge \
  --live-record-limit 02:00:00 \
  -sv best -sa best \
  -M format=mp4 \
  --save-dir /output \
  --save-name stream
```

## License

MIT License - See [LICENSE](LICENSE) for details.

## Credits

Original project by [nilaoda](https://github.com/nilaoda/N_m3u8DL-RE)
