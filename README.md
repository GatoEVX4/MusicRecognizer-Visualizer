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
- **Music history** - Track all recognized songs
- **Smart recommendations** - Get similar tracks based on your listening habits
- **Automatic Music Download**

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

### Data Storage

All data is stored locally in SQLite database at:
```
%APPDATA%\MusicRecognizer\music.db
```

## Limitations

- Recognition relies on Shazam's database

## Disclaimer

This project is for personal and educational use.
Use of Shazam’s API must comply with their terms of service.

