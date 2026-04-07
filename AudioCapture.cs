using System.Runtime.InteropServices;

namespace Music
{
    // Replaces NAudio.Wave.ISampleProvider — used by Analysis.ReadChunk
    public interface ISampleProvider
    {
        int Read(float[] buffer, int offset, int count);
    }

    // Replaces WasapiLoopbackCapture + MMDeviceEnumerator
    // Captures the default render endpoint (loopback) via raw WASAPI COM
    public sealed class LoopbackCapture : IDisposable
    {
        public event Action<byte[], int>? DataAvailable;
        public int SampleRate { get; private set; }
        public int Channels   { get; private set; }

        private volatile bool _running;
        private Thread?       _thread;

        // ── COM GUIDs ────────────────────────────────────────────────────────
        static readonly Guid _clsidMMDE = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        static readonly Guid _iidMMDE   = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
        static readonly Guid _iidAC     = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        static readonly Guid _iidCC     = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

        const uint CLSCTX_ALL        = 0x17;
        const int  SHAREMODE_SHARED  = 0;
        const uint FLAGS_LOOPBACK    = 0x00020000;
        const uint BUFFERFLAG_SILENT = 0x2;

        // IAudioClient vtable indices (IUnknown = 0-2, then own methods from 3):
        //   3  Initialize        7  IsFormatSupported   11 Stop
        //   4  GetBufferSize     8  GetMixFormat        12 Reset
        //   5  GetStreamLatency  9  GetDevicePeriod     13 SetEventHandle
        //   6  GetCurrentPadding 10 Start               14 GetService

        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormatEx
        {
            public ushort wFormatTag, nChannels;
            public uint   nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        // ── P/Invoke ─────────────────────────────────────────────────────────
        [DllImport("ole32.dll")] static extern int  CoCreateInstance(ref Guid c, IntPtr o, uint x, ref Guid i, out IntPtr p);
        [DllImport("ole32.dll")] static extern void CoInitializeEx(IntPtr r, uint f);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

        // ── Vtable delegates (only the methods we actually call) ──────────────
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnGetDefaultEndpoint(IntPtr t, int flow, int role, out IntPtr dev);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnActivate(IntPtr t, ref Guid iid, uint ctx, IntPtr par, out IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnGetMixFormat(IntPtr t, out IntPtr fmt);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnGetDevicePeriod(IntPtr t, out long defPeriod, out long minPeriod);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnAcInit(IntPtr t, int mode, uint flags, long buf, long per, IntPtr fmt, IntPtr sess);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnGetService(IntPtr t, ref Guid iid, out IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnStart(IntPtr t);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnStop(IntPtr t);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnGetNextPacketSize(IntPtr t, out uint frames);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnGetBuffer(IntPtr t, out IntPtr data, out uint frames, out uint flags, out ulong dp, out ulong qp);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int  FnReleaseBuffer(IntPtr t, uint frames);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint FnRelease(IntPtr t);

        // Reads method pointer from COM vtable by index
        static T Vtbl<T>(IntPtr p, int i) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(p), i * IntPtr.Size));

        // Throws a descriptive COMException for any failing HRESULT
        static void Hr(int hr) => Marshal.ThrowExceptionForHR(hr);

        static void ComRelease(IntPtr p) { if (p != IntPtr.Zero) Vtbl<FnRelease>(p, 2)(p); }

        // Initialises WASAPI synchronously on the capture thread, then returns
        // once SampleRate/Channels are set and recording has started.
        public void StartRecording()
        {
            var ready     = new ManualResetEventSlim(false);
            Exception? ex = null;

            _thread = new Thread(() =>
            {
                CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
                IntPtr enumerator = default, device = default, audioClient = default, captureClient = default;
                try
                {
                    // IMMDeviceEnumerator → default render endpoint
                    var c = _clsidMMDE; var ri = _iidMMDE;
                    Hr(CoCreateInstance(ref c, IntPtr.Zero, CLSCTX_ALL, ref ri, out enumerator));
                    Hr(Vtbl<FnGetDefaultEndpoint>(enumerator, 4)(enumerator, 0 /*eRender*/, 1 /*eMultimedia*/, out device));

                    // IAudioClient
                    var iac = _iidAC;
                    Hr(Vtbl<FnActivate>(device, 3)(device, ref iac, CLSCTX_ALL, IntPtr.Zero, out audioClient));

                    // Loopback must use the device's native mix format
                    Hr(Vtbl<FnGetMixFormat>(audioClient, 8)(audioClient, out IntPtr fmtPtr));
                    var fmt    = Marshal.PtrToStructure<WaveFormatEx>(fmtPtr);
                    SampleRate = (int)fmt.nSamplesPerSec;
                    Channels   = fmt.nChannels;
                    int frameBytes = fmt.nBlockAlign;

                    // Guard: shared-mode WASAPI on modern Windows always returns IEEE_FLOAT 32-bit
                    // (wFormatTag = 3, or 0xFFFE EXTENSIBLE with float SubFormat).
                    // Any other layout (PCM int16/int24) would make MemoryMarshal.Cast<byte,float>
                    // produce garbage — fail early with a clear message instead of silent corruption.
                    bool isFloat = fmt.wFormatTag == 3 ||
                                   (fmt.wFormatTag == 0xFFFE && fmt.wBitsPerSample == 32);
                    if (!isFloat)
                        throw new NotSupportedException(
                            $"Unsupported audio format: tag=0x{fmt.wFormatTag:X} bits={fmt.wBitsPerSample}. Expected IEEE_FLOAT 32-bit.");

                    // Use device's default period as buffer duration (avoids AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED
                    // that some drivers return when hnsBufferDuration = 0)
                    Hr(Vtbl<FnGetDevicePeriod>(audioClient, 9)(audioClient, out long bufDuration, out _));
                    Hr(Vtbl<FnAcInit>(audioClient, 3)(audioClient, SHAREMODE_SHARED, FLAGS_LOOPBACK, bufDuration, 0, fmtPtr, IntPtr.Zero));
                    CoTaskMemFree(fmtPtr);

                    // IAudioCaptureClient — GetService is at vtable index 14
                    var icc = _iidCC;
                    Hr(Vtbl<FnGetService>(audioClient, 14)(audioClient, ref icc, out captureClient));
                    Hr(Vtbl<FnStart>(audioClient, 10)(audioClient));

                    _running = true;
                    ready.Set(); // unblock StartRecording() — format is known

                    var fnNext   = Vtbl<FnGetNextPacketSize>(captureClient, 5);
                    var fnGet    = Vtbl<FnGetBuffer>(captureClient, 3);
                    var fnRelBuf = Vtbl<FnReleaseBuffer>(captureClient, 4);

                    // Pre-allocated accumulation buffer — drained and combined each poll cycle
                    // so DataAvailable fires once per interval with all queued packets merged,
                    // matching NAudio's behaviour of delivering a single large chunk per callback.
                    byte[] accum   = new byte[frameBytes * 4800]; // ~100ms headroom at 48 kHz stereo
                    int    accumLen = 0;

                    while (_running)
                    {
                        accumLen = 0;
                        fnNext(captureClient, out uint packetSize);
                        while (packetSize > 0 && _running)
                        {
                            fnGet(captureClient, out IntPtr data, out uint numFrames, out uint flags, out _, out _);
                            int bytes = (int)numFrames * frameBytes;

                            // Grow accum if this batch would overflow (should rarely happen)
                            if (accumLen + bytes > accum.Length)
                                Array.Resize(ref accum, (accumLen + bytes) * 2);

                            if ((flags & BUFFERFLAG_SILENT) == 0 && bytes > 0)
                                Marshal.Copy(data, accum, accumLen, bytes);
                            else
                                Array.Clear(accum, accumLen, bytes);

                            accumLen += bytes;
                            fnRelBuf(captureClient, numFrames);
                            fnNext(captureClient, out packetSize);
                        }

                        if (accumLen > 0)
                        {
                            var buf = new byte[accumLen];
                            Buffer.BlockCopy(accum, 0, buf, 0, accumLen);
                            DataAvailable?.Invoke(buf, accumLen);
                        }

                        Thread.Sleep(20); // 20 ms → ~50 events/s instead of ~200, matching NAudio's cadence
                    }
                    Vtbl<FnStop>(audioClient, 11)(audioClient);
                }
                catch (Exception e)
                {
                    ex = e;
                    if (!ready.IsSet) ready.Set();
                }
                finally
                {
                    ComRelease(captureClient);
                    ComRelease(audioClient);
                    ComRelease(device);
                    ComRelease(enumerator);
                    CoUninitialize();
                }
            })
            { IsBackground = true, Name = "LoopbackCapture" };

            _thread.Start();
            ready.Wait();
            if (ex != null) throw new InvalidOperationException("WASAPI init failed", ex);
        }

        public void Dispose()
        {
            _running = false;
            _thread?.Join(300);
        }
    }

    // Replaces BufferedWaveProvider + MediaFoundationResampler + ToSampleProvider
    // Accepts raw float32 PCM bytes at srcRate/srcChannels, outputs 16 kHz mono float via ISampleProvider
    public sealed class ResamplingBuffer : ISampleProvider
    {
        private readonly int    _srcChannels;
        private readonly double _step;       // src frames per dst frame
        private readonly Queue<float> _queue = new();
        private readonly object _lock        = new();
        private double _fracPos;

        public ResamplingBuffer(int srcRate, int srcChannels)
        {
            _srcChannels = srcChannels;
            _step        = (double)srcRate / 16000.0;
        }

        public TimeSpan BufferedDuration
        {
            get { lock (_lock) return TimeSpan.FromSeconds((double)_queue.Count / 16000); }
        }

        // Called from the LoopbackCapture thread
        public void AddSamples(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            var src       = MemoryMarshal.Cast<byte, float>(data.AsSpan(offset, count));
            int srcFrames = src.Length / _srcChannels;
            if (srcFrames == 0) return;

            lock (_lock)
            {
                double pos = _fracPos;
                while (pos < srcFrames)
                {
                    int   i0   = (int)pos;
                    float frac = (float)(pos - i0);
                    int   i1   = Math.Min(i0 + 1, srcFrames - 1);

                    float s0 = 0f, s1 = 0f;
                    for (int c = 0; c < _srcChannels; c++)
                    {
                        s0 += src[i0 * _srcChannels + c];
                        s1 += src[i1 * _srcChannels + c];
                    }
                    if (_srcChannels > 1) { s0 /= _srcChannels; s1 /= _srcChannels; }

                    _queue.Enqueue(s0 + frac * (s1 - s0));
                    pos += _step;
                }
                _fracPos = pos - srcFrames;
            }
        }

        // Blocks until count samples are available (called from async context via Analysis.ReadChunk)
        public int Read(float[] buffer, int offset, int count)
        {
            while (true)
            {
                lock (_lock)
                {
                    if (_queue.Count >= count)
                    {
                        for (int i = 0; i < count; i++)
                            buffer[offset + i] = _queue.Dequeue();
                        return count;
                    }
                }
                Thread.Sleep(1);
            }
        }
    }
}
