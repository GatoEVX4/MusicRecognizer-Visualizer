# Music Recognizer

A real-time desktop music recognition application built with WPF and C#. This application continuously monitors your desktop audio output, identifies songs using audio fingerprinting technology, and displays detailed information about the currently playing track.

## Features

- **Real-time Music Recognition**: Automatically identifies songs playing on your desktop using advanced audio fingerprinting
- **Audio Visualization**: Beautiful real-time audio spectrum visualizer synchronized with the music
- **Rich Song Information**: Displays track title, artist, genre, cover art, and release year
- **Dynamic UI**: Adaptive color scheme extracted from album artwork using Shazam's JoeColor algorithm
- **Multiple View Modes**: 
  - TaskBar mode - Minimal overlay integrated with your taskbar
  - Window mode - Floating window that can be repositioned
- **Low Latency**: Optimized audio processing pipeline for fast recognition
- **Desktop Audio Capture**: Uses WASAPI loopback to capture audio from any application

## Showcase

Watch the application in action:
**https://streamable.com/6zhf9a**

## Technology Stack

- **.NET 8.0** - Modern C# framework
- **WPF (Windows Presentation Foundation)** - Native Windows UI framework
- **NAudio** - Audio capture and processing
- **MathNet.Numerics** - FFT and signal processing for audio analysis
- **Shazam API** - Music recognition service

## How It Works

### Audio Fingerprinting

The application uses a landmark-based audio fingerprinting algorithm similar to Shazam's technology:

1. **Audio Capture**: Captures desktop audio via WASAPI loopback at 16kHz sample rate
2. **Spectral Analysis**: Performs FFT (Fast Fourier Transform) on audio chunks to generate frequency spectrograms
3. **Landmark Detection**: Identifies distinctive peaks in the frequency-time domain
4. **Signature Generation**: Creates a compact audio signature from detected landmarks
5. **Recognition**: Sends the signature to Shazam's API for song identification

### Recognition Pipeline

- Continuously monitors desktop audio output
- Processes audio in real-time chunks
- Generates fingerprints using frequency band analysis
- Retries with adaptive delays based on recognition success rate
- Updates UI automatically when a new song is detected

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the files to a folder of your choice
3. Run `Music.exe`
4. The application will start monitoring your desktop audio automatically

## Usage

### Basic Usage

1. Start the application
2. Play music from any source (Spotify, YouTube, local files, etc.)
3. The application will automatically detect and display the song information
4. The overlay appears in TaskBar mode by default

### View Modes

- **Press `L`** to toggle between TaskBar and Window modes
- **Drag** the window to reposition it in Window mode
- The window stays on top by default for easy visibility

### Visual Features

- **Cover Art**: Album artwork appears with animated transitions
- **Color Themes**: UI colors adapt based on the album artwork's dominant colors
- **Audio Visualizer**: Real-time frequency bars respond to the music
- **Text Scrolling**: Long song titles and artist names scroll smoothly

## Technical Details

### Audio Processing

- **Sample Rate**: 16kHz (resampled from original format)
- **FFT Window Size**: 2048 samples
- **Chunk Size**: 128 samples per processing chunk
- **Frequency Bands**: 5 bands (250Hz, 520Hz, 1450Hz, 3500Hz, 5500Hz)

### Recognition Algorithm

The fingerprinting process uses:
- Peak detection in frequency-time spectrograms
- Landmark pairing with time offsets
- Band-based landmark organization
- CRC32 checksums for signature integrity

### Performance

- **CPU Usage**: Low, optimized audio processing
- **Memory**: Minimal footprint with efficient buffer management
- **Recognition Speed**: Typically 3-8 seconds depending on audio clarity
- **Accuracy**: High, leveraging Shazam's extensive music database

## Limitations

- Requires active internet connection for recognition
- Recognition depends on Shazam's database coverage
- Works best with clear audio (may struggle with low-quality or distorted audio)

---

**Note**: This application is for personal and educational use. Ensure you comply with Shazam's terms of service and API usage policies when using this software.
