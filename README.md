# Music Recognizer

Desktop application for real-time music recognition built with WPF and C#.
The application captures system audio output, generates audio fingerprints, and identifies the currently playing track using Shazam’s recognition service.

<img width="1439" height="59" alt="image" src="https://github.com/user-attachments/assets/126ccb6c-799b-48c5-a608-5ec57c9700de" />

## Description

The application runs in the background and monitors desktop audio through WASAPI loopback. Audio is processed in real time and converted into fingerprints compatible with Shazam’s API. When a match is found, basic track metadata is displayed in a lightweight overlay.

The UI is minimal and supports both taskbar-style and floating window modes.

## Features

- Real-time music recognition from system audio
- WASAPI loopback audio capture
- FFT-based audio analysis
- Track metadata display (title, artist, year, cover art)
- Simple audio spectrum visualizer
- Taskbar overlay mode
- Floating window mode
- **Music history** - Track all recognized songs
- **Smart recommendations** - Get similar tracks based on your listening habits
- **Settings panel** - Customize recognition, visualizer, and privacy settings
- **Quick actions** - Open tracks in Spotify, YouTube, or Apple Music

## Demo

https://streamable.com/6zhf9a

## Tech Stack

- .NET 8
- WPF
- NAudio
- MathNet.Numerics
- Shazam API

## How It Works

### Audio Processing

- Captures system audio using WASAPI loopback
- Resamples audio to 16 kHz
- Processes audio in continuous chunks

### Fingerprinting

- Applies FFT to audio windows
- Detects frequency peaks in predefined bands
- Generates landmarks based on frequency and time offsets
- Encodes landmarks into a compact signature
- Sends the signature to Shazam’s API

### Recognition Flow

- Continuously analyzes audio while playback is detected
- Attempts recognition periodically
- Updates the UI when a track is identified

### Controls

- Press `L` to toggle between taskbar and window modes
- Press `Ctrl+H` to open history and recommendations
- Press `Ctrl+S` to open settings
- Right-click on the window for quick menu
- Drag the window to reposition it

## Technical Notes

### Audio Settings

- Sample rate: 16 kHz
- FFT window size: 2048 samples
- Processing chunk size: 128 samples
- Frequency bands:
  - 250 Hz
  - 520 Hz
  - 1450 Hz
  - 3500 Hz
  - 5500 Hz

### Fingerprint Data

- Peak-based landmark detection
- Time-offset pairing
- CRC32 checksum for signature validation

## New Features

### History

All recognized tracks are automatically saved and can be accessed via `Ctrl+H`. The history includes:
- Track title, artist, and cover art
- Recognition date and time
- Recognition count (how many times the track was detected)
- Quick actions to open in Spotify, YouTube, or Shazam
- **Real-time updates** - The window automatically refreshes when new tracks are recognized

### Smart Recommendations

When a track is recognized 3 times, the app automatically fetches similar tracks from Shazam's recommendation API. The recommendations tab shows:
- Top 5 most recurring tracks across all your recognized music
- Relevance score based on frequency
- Direct links to streaming platforms
- **Live updates** - Recommendations refresh automatically as new data becomes available

The recommendation algorithm analyzes all similar tracks from your listening history and surfaces the most common suggestions, ensuring you discover music aligned with your taste.

### Settings

Access via `Ctrl+S` or right-click menu. Customize:
- Recognition delays (min/max)
- Visualizer settings (bars count, FPS)
- History preferences (enable/disable, max items)
- Discord Rich Presence integration
- Clear history option

### Data Storage

All data is stored locally in JSON format at:
```
%APPDATA%\MusicRecognizer\data.json
```

## Limitations

- Recognition relies on Shazam's database
## Disclaimer

This project is for personal and educational use.
Use of Shazam’s API must comply with their terms of service.

