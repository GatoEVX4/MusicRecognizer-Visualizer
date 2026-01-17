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

## Installation

1. Download the latest release
2. Extract the files
3. Run `Music.exe`

## Usage

1. Start the application
2. Play music from any desktop source
3. Track information appears automatically

### Controls

- Press `L` to toggle between taskbar and window modes
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

## Limitations

- Requires internet access
- Recognition depends on Shazam’s database
- Low-quality audio reduces accuracy

## Disclaimer

This project is for personal and educational use.
Use of Shazam’s API must comply with their terms of service.
