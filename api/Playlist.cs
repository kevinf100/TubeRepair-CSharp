using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Web;
using TubeRepair_CSharp.Templates;

namespace TubeRepair_CSharp.api
{
    public class Playlist
    {
        /// <summary>
        /// Gets playlists for a given channel
        /// </summary>
        public static async Task<IResult> GetChannelPlaylists(HttpRequest request, string channelId, int? res = null)
        {
            ConfigReader config = ConfigReader.Instance;

            // Clamp Res if provided
            if (res.HasValue)
            {
                res = Math.Clamp(res.Value, 144, 10000);
            }

            // Build url root including optional res
            var resPart = res.HasValue ? res.Value.ToString() : string.Empty;
            var url = $"{request.Scheme}://{request.Host}/{resPart}";
            if (url.EndsWith('/'))
            {
                url = url[..^1];
            }

            // Get continuation token if present
            string? continuationParam = request.Query["continuation"];
            string continuationToken = !string.IsNullOrEmpty(continuationParam)
  ? $"?continuation={continuationParam}"
     : "";

            // Prepare Invidious API base
            var apiBase = config.InvidiusURL.TrimEnd('/');
            var apiurl = $"{apiBase}/api/v1/channels/{channelId}/playlists{continuationToken}";

            // Fetch API with caching (1 hour expiration)
            string? content;
            try
            {
                content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromHours(1));
                if (content == null)
                {
                    return Results.Content("Failed to fetch channel playlists", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch channel playlists", "text/plain", statusCode: 500);
            }

            // Parse the JSON response
            List<object>? playlists = null;
            string? continuation = null;

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Get playlists array
                if (root.TryGetProperty("playlists", out var playlistsArray) &&
              playlistsArray.ValueKind == JsonValueKind.Array)
                {
                    playlists = [];
                    for (int i = 0; i < playlistsArray.GetArrayLength(); i++)
                    {
                        var playlist = playlistsArray[i];
                        var playlistDict = new Dictionary<string, object>
                        {
                            ["playlistId"] = playlist.GetProperty("playlistId").GetString() ?? "",
                            ["title"] = playlist.GetProperty("title").GetString() ?? "",
                            ["videoCount"] = playlist.TryGetProperty("videoCount", out var count) ? count.GetInt32() : 0,
                            ["playlistThumbnail"] = playlist.TryGetProperty("playlistThumbnail", out var thumb) ? thumb.GetString() ?? "" : ""
                        };

                        // Add optional properties
                        if (playlist.TryGetProperty("description", out var desc))
                        {
                            playlistDict["description"] = desc.GetString() ?? "";
                        }

                        playlists.Add(playlistDict);
                    }

                    // Get continuation token if present
                    if (root.TryGetProperty("continuation", out var contProp))
                    {
                        continuation = contProp.GetString();
                    }
                }
                else
                {
                    throw new Exception("No playlists data found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing channel playlists JSON: {ex.Message}");
                return Results.Content("Failed to parse channel playlists", "text/plain", statusCode: 500);
            }

            // Render template
            var templateData = new Dictionary<string, object?>
            {
                ["data"] = playlists,
                ["continuation"] = continuation,
                ["url"] = url,
                ["channelid"] = channelId
            };

            var rendered = TemplatesLoader.Instance.RenderTemplate("channel_playlists.scriban", templateData);
            return Results.Content(rendered, "application/atom+xml; charset=utf-8");
        }

        /// <summary>
        /// Gets videos in a playlist
        /// </summary>
        public static async Task<IResult> GetPlaylistVideos(HttpRequest request, string playlistId, int? res = null)
        {
            ConfigReader config = ConfigReader.Instance;

            // Check for invalid playlist ID
            if (string.IsNullOrWhiteSpace(playlistId) || playlistId.Trim().Equals("(null)", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Content("Invalid playlist ID", "text/plain", statusCode: 400);
            }

            // Check for max-results=0 (which should return error)
            string? maxResults = request.Query["max-results"];
            if (!string.IsNullOrEmpty(maxResults) && maxResults == "0")
            {
                return Results.Content("Invalid max-results parameter", "text/plain", statusCode: 400);
            }

            // Clamp Res if provided
            if (res.HasValue)
            {
                res = Math.Clamp(res.Value, 144, 10000);
            }

            // Build url root including optional res
            var resPart = res.HasValue ? res.Value.ToString() : string.Empty;
            var url = $"{request.Scheme}://{request.Host}/{resPart}";
            if (url.EndsWith('/'))
            {
                url = url[..^1];
            }

            // Process pagination
            var (currentPage, nextPage) = Helpers.ProcessStartIndex(request);

            // Build query string
            string query = $"page={currentPage}";

            // Prepare Invidious API base
            var apiBase = config.InvidiusURL.TrimEnd('/');
            var apiurl = $"{apiBase}/api/v1/playlists/{playlistId}?{query}";

            // Fetch API with caching (1 hour expiration)
            string? content;
            try
            {
                content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromHours(1));
                if (content == null)
                {
                    return Results.Content("Failed to fetch playlist videos", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch playlist videos", "text/plain", statusCode: 500);
            }

            // Parse the JSON response
            List<object>? videos = null;

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Get videos array
                if (root.TryGetProperty("videos", out var videosArray) &&
             videosArray.ValueKind == JsonValueKind.Array)
                {
                    videos = new List<object>();
                    for (int i = 0; i < videosArray.GetArrayLength(); i++)
                    {
                        var video = videosArray[i];
                        var videoDict = new Dictionary<string, object>
                        {
                            ["video_id"] = video.GetProperty("videoId").GetString() ?? "",
                            ["title"] = video.GetProperty("title").GetString() ?? "",
                            ["author"] = video.TryGetProperty("author", out var author) ? author.GetString() ?? "" : "",
                            ["author_id"] = video.TryGetProperty("authorId", out var authorId) ? authorId.GetString() ?? "" : "",
                            ["length_seconds"] = video.TryGetProperty("lengthSeconds", out var length) ? length.GetInt32() : 0,
                            ["view_count"] = video.TryGetProperty("viewCount", out var views) ? views.GetInt64() : 0
                        };

                        // Add optional properties
                        if (video.TryGetProperty("description", out var desc))
                        {
                            videoDict["description"] = desc.GetString() ?? "";
                        }

                        if (video.TryGetProperty("published", out var published))
                        {
                            videoDict["published"] = published.GetInt64();
                        }

                        videos.Add(videoDict);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing playlist videos JSON: {ex.Message}");
                return Results.Content("Failed to parse playlist videos", "text/plain", statusCode: 500);
            }

            // Set next_page to null if no data
            string? nextPageValue = videos != null && videos.Count > 0 ? nextPage : null;

            // Render template
            var templateData = new Dictionary<string, object?>
            {
                ["data"] = videos,
                ["url"] = url,
                ["next_page"] = nextPageValue
            };

            var rendered = TemplatesLoader.Instance.RenderTemplate("playlist_videos.scriban", templateData);
            return Results.Content(rendered, "application/atom+xml; charset=utf-8");
        }

        /// <summary>
        /// Search for playlists
        /// </summary>
        public static async Task<IResult> SearchPlaylists(HttpRequest request, int? res = null)
        {
            ConfigReader config = ConfigReader.Instance;

            // Clamp Res if provided
            if (res.HasValue)
            {
                res = Math.Clamp(res.Value, 144, 10000);
            }

            // Build url root including optional res
            var resPart = res.HasValue ? res.Value.ToString() : string.Empty;
            var url = $"{request.Scheme}://{request.Host}/{resPart}";
            if (url.EndsWith('/'))
            {
                url = url[..^1];
            }

            // Get search keyword
            string? searchKeyword = request.Query["q"];

            // URL encode the search keyword
            searchKeyword = HttpUtility.UrlEncode(searchKeyword);

            // Process pagination
            var (currentPage, nextPage) = Helpers.ProcessStartIndex(request);

            // Build query string
            string query = $"q={searchKeyword}&type=playlist&page={currentPage}";

            // Prepare Invidious API base
            var apiBase = config.InvidiusURL.TrimEnd('/');
            var apiurl = $"{apiBase}/api/v1/search?{query}";

            // Fetch API with caching (1 hour expiration)
            string? content;
            try
            {
                content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromHours(1));
                if (content == null)
                {
                    return Results.Content("Failed to fetch playlist search results", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch playlist search results", "text/plain", statusCode: 500);
            }

            // Parse the JSON response
            List<object>? data = null;

            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var root = doc.RootElement;
                    data = [];
                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        var playlist = root[i];
                        var playlistDict = new Dictionary<string, object>
                        {
                            ["playlistId"] = playlist.GetProperty("playlistId").GetString() ?? "",
                            ["title"] = playlist.GetProperty("title").GetString() ?? "",
                            ["videoCount"] = playlist.TryGetProperty("videoCount", out var count) ? count.GetInt32() : 0,
                            ["playlistThumbnail"] = playlist.TryGetProperty("playlistThumbnail", out var thumb) ? thumb.GetString() ?? "" : ""
                        };

                        // Add optional properties
                        if (playlist.TryGetProperty("description", out var desc))
                        {
                            playlistDict["description"] = desc.GetString() ?? "";
                        }

                        if (playlist.TryGetProperty("author", out var author))
                        {
                            playlistDict["author"] = author.GetString() ?? "";
                        }

                        if (playlist.TryGetProperty("authorId", out var authorId))
                        {
                            playlistDict["authorId"] = authorId.GetString() ?? "";
                        }

                        data.Add(playlistDict);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing playlist search JSON: {ex.Message}");
                return Results.Content("Failed to parse playlist search results", "text/plain", statusCode: 500);
            }

            // Set next_page to null if no data
            string? nextPageValue = data != null && data.Count > 0 ? nextPage : null;

            // Render template
            var templateData = new Dictionary<string, object?>
            {
                ["data"] = data,
                ["url"] = url,
                ["next_page"] = nextPageValue
            };

            var rendered = TemplatesLoader.Instance.RenderTemplate("channel_playlists.scriban", templateData);
            return Results.Content(rendered, "application/atom+xml; charset=utf-8");
        }
    }
}
