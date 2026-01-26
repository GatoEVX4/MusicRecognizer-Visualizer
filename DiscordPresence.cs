using DiscordRPC;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using static Music.API;

namespace Music
{
    public class DiscordPresence : IDisposable
    {
        private readonly DiscordRpcClient _rpcClient;
        private readonly Stopwatch _uptimeStopwatch;
        private bool _disposed = false;
        private ShazamResult? _currentTrack = null;
        private Timestamps? _trackStartTimestamp = null;
        private string _lastState = "";

        public DiscordPresence()
        {
            _rpcClient = new DiscordRpcClient("1465407038975901914")
            {
                SkipIdenticalPresence = true,
                ShutdownOnly = false,
            };

            _rpcClient.OnReady += OnReady;

            Task.Run(InitializeAsync);

            _uptimeStopwatch = Stopwatch.StartNew();
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (!_rpcClient.Initialize())
                    return;

                await Task.Delay(100);
            }
            catch
            {
                
            }
        }

        private void OnReady(object sender, DiscordRPC.Message.ReadyMessage args)
        {
            Console.WriteLine($"DiscordPresence connected as {args.User.Username}");
        }

        public void UpdateMusic(ShazamResult? track)
        {
            if (_disposed) return;

            ClearPresence();
            _currentTrack = null;
            _trackStartTimestamp = null;

            if (track == null || !track.Success || string.IsNullOrEmpty(track.Title))                         
                return;            

            var startTime = DateTime.UtcNow - TimeSpan.FromSeconds(track.Offset ?? 0);            
            DateTime? endTime = null;

            if (track.DurationSeconds.HasValue && track.DurationSeconds.Value > 0)            
                endTime = startTime + TimeSpan.FromSeconds(track.DurationSeconds.Value);

            _trackStartTimestamp = new Timestamps
            {
                Start = startTime,
                End = endTime
            };

            _currentTrack = track;
            UpdatePresence();
        }

        private void ClearPresence()
        {
            if (_disposed || !_rpcClient.IsInitialized)
                return;

            try
            {
                _rpcClient.ClearPresence();
                _lastState = "";
            }
            catch
            {
                
            }
        }

        private void UpdatePresence()
        {
            if (_disposed || !_rpcClient.IsInitialized || _currentTrack == null)
                return;

            string details = _currentTrack.Title ?? "Unknown";
            string state = _currentTrack.Artist ?? "Unknown Artist";
            
            if (!string.IsNullOrEmpty(_currentTrack.Genre))
            {
                state += $" • {_currentTrack.Genre}";
            }

            string fullState = $"{details} | {state}";

            if (fullState == _lastState)
                return;

            _lastState = fullState;

            try
            {
                var presence = new RichPresence
                {
                    StatusDisplay = StatusDisplayType.Details,
                    Type = ActivityType.Listening,

                    Details = details,
                    State = state,
                    StateUrl = $"https://www.shazam.com/track/{_currentTrack.Id}",
                    Timestamps = _trackStartTimestamp,
                    Assets = new Assets
                    {
                        LargeImageKey = _currentTrack.CoverArtUrl,
                    }
                };

                _rpcClient.SetPresence(presence);
            }
            catch
            {
                
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _rpcClient.OnReady -= OnReady;

                if (_rpcClient.IsInitialized)
                {
                    _rpcClient.ClearPresence();
                }

                _rpcClient.Dispose();
                _uptimeStopwatch.Stop();
            }
            catch (ObjectDisposedException)
            {
                
            }
            catch
            {
                
            }

            GC.SuppressFinalize(this);
        }
    }
}
