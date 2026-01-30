using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Music
{
    public class RecommendationService
    {
        private static readonly HttpClient Http = new HttpClient();

        public static async Task<List<RecommendedTrack>> GetSimilarTracksAsync(string trackId)
        {
            try
            {
                var url = $"https://cdn.shazam.com/shazam/v2/en/US/android/-/tracks/track-similarities-id-{trackId}?startFrom=0&pageSize=20&connected=";
                
                Logger.Log($"Fetching similar tracks for ID: {trackId}", ConsoleColor.Cyan);
                
                var response = await Http.GetStringAsync(url);
                var json = JsonSerializer.Deserialize<JsonElement>(response);

                var recommendations = new List<RecommendedTrack>();

                if (json.TryGetProperty("chart", out var chart) && chart.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in chart.EnumerateArray())
                    {
                        try
                        {
                            var track = new RecommendedTrack
                            {
                                Key = item.GetProperty("key").GetString(),
                                Title = item.GetProperty("heading").GetProperty("title").GetString(),
                                Artist = item.GetProperty("heading").GetProperty("subtitle").GetString()
                            };

                            // Cover art
                            if (item.TryGetProperty("images", out var images) &&
                                images.TryGetProperty("default", out var coverArt))
                            {
                                track.CoverArtUrl = coverArt.GetString();
                            }

                            // Shazam URL
                            if (item.TryGetProperty("url", out var url_elem))
                            {
                                track.ShazamUrl = url_elem.GetString();
                            }

                            // Spotify URI
                            if (item.TryGetProperty("streams", out var streams) &&
                                streams.TryGetProperty("spotify", out var spotify) &&
                                spotify.TryGetProperty("actions", out var spotifyActions) &&
                                spotifyActions.GetArrayLength() > 0)
                            {
                                var firstAction = spotifyActions[0];
                                if (firstAction.TryGetProperty("uri", out var spotifyUri))
                                {
                                    track.SpotifySearchUri = spotifyUri.GetString();
                                }
                            }

                            // Apple Music URI
                            if (item.TryGetProperty("stores", out var stores) &&
                                stores.TryGetProperty("apple", out var apple) &&
                                apple.TryGetProperty("actions", out var appleActions) &&
                                appleActions.GetArrayLength() > 0)
                            {
                                var firstAction = appleActions[0];
                                if (firstAction.TryGetProperty("uri", out var appleUri))
                                {
                                    track.AppleMusicUri = appleUri.GetString();
                                }
                            }

                            recommendations.Add(track);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Error parsing track item: {ex.Message}", ConsoleColor.DarkYellow);
                        }
                    }
                }

                Logger.Log($"Found {recommendations.Count} similar tracks", ConsoleColor.Green);
                return recommendations;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error fetching similar tracks: {ex.Message}", ConsoleColor.Red);
                return new List<RecommendedTrack>();
            }
        }
    }
}



