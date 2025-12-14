using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TubeRepair_CSharp.Templates;

namespace TubeRepair_CSharp.api
{
    public class Video
    {
        public static async Task<IResult> Frontpage(HttpRequest request, string? regioncode = "US", string? popular = null, int? res = null)
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

            // Prepare Invidious API base
            var apiBase = config.InvidiusURL.TrimEnd('/');
            var apiurl = $"{apiBase}/api/v1/trending?region={regioncode}";
            if (!string.IsNullOrEmpty(popular))
            {
                if (popular == "most_popular_Film")
                    apiurl = $"{apiBase}/api/v1/trending?type=Movies&region={regioncode}";
                else if (popular == "most_popular_Games")
                    apiurl = $"{apiBase}/api/v1/trending?type=Gaming&region={regioncode}";
                else if (popular == "most_popular_Music")
                    apiurl = $"{apiBase}/api/v1/trending?type=Music&region={regioncode}";
            }

            // Fetch API with caching (1 hour expiration)
            string? content;
            try
            {
                content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromHours(1));
                if (content == null)
                {
                    return Results.Content("Failed to fetch upstream API", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch upstream API", "text/plain", statusCode: 500);
            }

            // Determine client user-agent for classic handling
            var ua = request.Headers.UserAgent.ToString().ToLowerInvariant();

            int takeCount = config.FeaturedVideosCount;
            bool isClassic = ua.Contains("youtube/1.0.0") || ua.Contains("youtube v1.0.0");

            // Try to parse JSON array and return only the requested number of items
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var root = doc.RootElement;
                    var items = new List<object>();
                    for (int i = 0; i < Math.Min(takeCount, root.GetArrayLength()); i++)
                    {
                        var video = root[i];
                        // Convert JSON element to a dictionary for template
                        var videoDict = new Dictionary<string, object>
                        {
                            ["video_id"] = video.GetProperty("videoId").GetString() ?? "",
                            ["title"] = video.GetProperty("title").GetString() ?? "",
                            ["author"] = video.GetProperty("author").GetString() ?? "",
                            ["author_id"] = video.GetProperty("authorId").GetString() ?? "",
                            ["description"] = video.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                            ["view_count"] = video.TryGetProperty("viewCount", out var views) ? views.GetInt64() : 0,
                            ["published"] = video.GetProperty("published").GetInt64(),
                            ["length_seconds"] = video.TryGetProperty("lengthSeconds", out var length) ? length.GetInt32() : 0
                        };
                        items.Add(videoDict);
                    }

                    // Render template
                    var templateData = new Dictionary<string, object?>
                    {
                        ["data"] = items,
                        ["url"] = url
                    };

                    string templateName = isClassic ? "classic_featured.scriban" : "frontpage_feed.scriban";
                    var rendered = TemplatesLoader.Instance.RenderTemplate(templateName, templateData);
                    return Results.Content(rendered, "application/atom+xml; charset=utf-8");
                }
                else
                {
                    // Not an array — return raw content
                    return Results.Content(content, "application/json");
                }
            }
            catch (JsonException)
            {
                // If parsing fails, return raw content
                return Results.Content(content, "application/json");
            }
        }

        public static async Task<IResult> SearchVideos(HttpRequest request, int? res = null)
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

            // Process pagination
            var (currentPage, nextPage) = Helpers.ProcessStartIndex(request);

            // Get user agent
            var userAgent = request.Headers.UserAgent.ToString().ToLowerInvariant();

            // Get search keyword
            string? searchKeyword = request.Query["q"];
            if (string.IsNullOrEmpty(searchKeyword))
            {
                return Results.Content("Missing search query parameter 'q'", "text/plain", statusCode: 400);
            }

            // URL encode the search keyword
            searchKeyword = HttpUtility.UrlEncode(searchKeyword);

            // Build query string
            var queryParams = new List<string>
            {
       $"q={searchKeyword}",
       "type=video",
         $"page={currentPage}"
        };

            // If we have orderby, turn it into invidious friendly parameters
            string? orderby = request.Query["orderby"];
            if (!string.IsNullOrEmpty(orderby) && Helpers.ValidSearchOrderBy.TryGetValue(orderby, out var sortValue))
            {
                queryParams.Add($"sort={sortValue}");
            }

            // If we have time, turn it into invidious friendly parameters
            string? time = request.Query["time"];
            if (!string.IsNullOrEmpty(time) && Helpers.ValidSearchTime.TryGetValue(time, out var dateValue))
            {
                queryParams.Add($"date={dateValue}");
            }

            // If we have duration, turn it into invidious friendly parameters
            string? duration = request.Query["duration"];
            if (!string.IsNullOrEmpty(duration) && Helpers.ValidSearchDuration.TryGetValue(duration, out var durationValue))
            {
                queryParams.Add($"duration={durationValue}");
            }

            // If we have captions, turn it into invidious friendly parameters
            string? caption = request.Query["caption"];
            if (!string.IsNullOrEmpty(caption) && caption.ToLower() == "true")
            {
                queryParams.Add("features=subtitles");
            }

            // Build full query string
            string query = string.Join("&", queryParams);

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
                    return Results.Content("Failed to fetch upstream API", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch upstream API", "text/plain", statusCode: 500);
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
                        var video = root[i];
                        // Convert JSON element to a dictionary for template
                        var videoDict = new Dictionary<string, object>
                        {
                            ["video_id"] = video.GetProperty("videoId").GetString() ?? "",
                            ["title"] = video.GetProperty("title").GetString() ?? "",
                            ["author"] = video.GetProperty("author").GetString() ?? "",
                            ["author_id"] = video.GetProperty("authorId").GetString() ?? "",
                            ["description"] = video.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                            ["view_count"] = video.TryGetProperty("viewCount", out var views) ? views.GetInt64() : 0,
                            ["length_seconds"] = video.TryGetProperty("lengthSeconds", out var length) ? length.GetInt32() : 0
                        };

                        // Add published if it exists
                        if (video.TryGetProperty("published", out var published))
                        {
                            videoDict["published"] = published.GetInt64();
                        }

                        data.Add(videoDict);
                    }
                }
            }
            catch (JsonException)
            {
                // If parsing fails, return raw content
                return Results.Content(content, "application/json");
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

            // Classic tube check
            bool isClassic = userAgent.Contains("youtube/1.0.0") || userAgent.Contains("youtube v1.0.0");
            string templateName = isClassic ? "classic_search.scriban" : "search_results.scriban";

            var rendered = TemplatesLoader.Instance.RenderTemplate(templateName, templateData);
            return Results.Content(rendered, "application/atom+xml; charset=utf-8");
        }

        public static async Task<IResult> Comments(HttpRequest request, string videoid, int? res = null)
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
          ? $"&continuation={continuationParam}"
        : "";

            // Prepare Invidious API base
            var apiBase = config.InvidiusURL.TrimEnd('/');
            var apiurl = $"{apiBase}/api/v1/comments/{videoid}?sortby={config.SortComments}{continuationToken}";

            // Fetch API with caching (shorter duration for comments - 30 minutes)
            string? content;
            try
            {
                content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromMinutes(30));
                if (content == null)
                {
                    return Results.Content("Failed to fetch comments API", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch comments API", "text/plain", statusCode: 500);
            }

            // Parse the JSON response
            List<object>? comments = null;
            string? continuation = null;

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Check for error
                if (root.TryGetProperty("error", out var errorProp))
                {
                    // No comments or error occurred
                    comments = null;
                }
                else if (root.TryGetProperty("comments", out var commentsArray) &&
                 commentsArray.ValueKind == JsonValueKind.Array)
                {
                    comments = new List<object>();
                    for (int i = 0; i < commentsArray.GetArrayLength(); i++)
                    {
                        var comment = commentsArray[i];
                        var commentDict = new Dictionary<string, object>
                        {
                            ["video_id"] = videoid,
                            ["content"] = comment.GetProperty("content").GetString() ?? "",
                            ["author"] = comment.GetProperty("author").GetString() ?? "",
                            ["author_id"] = comment.GetProperty("authorId").GetString() ?? "",
                            ["published"] = comment.GetProperty("published").GetInt64()
                        };

                        comments.Add(commentDict);
                    }

                    // Get continuation token if present
                    if (root.TryGetProperty("continuation", out var contProp))
                    {
                        continuation = contProp.GetString();
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing comments JSON: {ex.Message}");
                return Results.Content("Failed to parse comments", "text/plain", statusCode: 500);
            }

            // Render template
            var templateData = new Dictionary<string, object?>
            {
                ["data"] = comments,
                ["url"] = url,
                ["continuation"] = continuation,
                ["video_id"] = videoid
            };

            var rendered = TemplatesLoader.Instance.RenderTemplate("comments.scriban", templateData);
            return Results.Content(rendered, "application/atom+xml; charset=utf-8");
        }

        public static async Task<IResult> GetRelated(HttpRequest request, string videoId, int? res = null)
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

            // Prepare Invidious API base
            var apiBase = config.InvidiusURL.TrimEnd('/');
            var apiurl = $"{apiBase}/api/v1/videos/{videoId}";

            // Fetch API with caching (4 hour expiration - same as GetVideo since it's the same endpoint)
            string? content;
            try
            {
                content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromHours(4));
                if (content == null)
                {
                    return Results.Content("Failed to fetch video data", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch video data", "text/plain", statusCode: 500);
            }
            // Get user agent
            var userAgent = request.Headers.UserAgent.ToString();

            // Parse the JSON response
            List<object>? data = null;
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Check for error
                if (root.TryGetProperty("error", out var errorProp))
                {
                    data = null;
                }
                else if (root.TryGetProperty("recommendedVideos", out var recommendedArray) &&
                     recommendedArray.ValueKind == JsonValueKind.Array)
                {
                    data = [];
                    for (int i = 0; i < recommendedArray.GetArrayLength(); i++)
                    {
                        var video = recommendedArray[i];
                        var videoDict = new Dictionary<string, object>
                        {
                            ["video_id"] = video.GetProperty("videoId").GetString() ?? "",
                            ["title"] = video.GetProperty("title").GetString() ?? "",
                            ["author"] = video.TryGetProperty("author", out var author) ? author.GetString() ?? "" : "",
                            ["author_id"] = video.TryGetProperty("authorId", out var authorId) ? authorId.GetString() ?? "" : "",
                            ["length_seconds"] = video.TryGetProperty("lengthSeconds", out var length) ? length.GetInt32() : 0,
                            ["view_count"] = video.TryGetProperty("viewCount", out var views) ? views.GetInt64() : 0,
                            ["description"] = video.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                        };
                        // Add published if it exists
                        // I don't even, why is this a string in suggested??
                        if (video.TryGetProperty("published", out var published))
                        {
                            DateTimeOffset dto = DateTimeOffset.Parse(published.GetString() ?? "1970-01-01T00:00:00Z");
                            long unixSeconds = dto.ToUnixTimeSeconds();
                            videoDict["published"] = unixSeconds;
                        }

                        data.Add(videoDict);
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing related videos JSON: {ex.Message}");
                return Results.Content("Failed to parse video data", "text/plain", statusCode: 500);
            }

            // Render template
            var templateData = new Dictionary<string, object?>
            {
                ["data"] = data,
                ["url"] = url,
                ["next_page"] = (string?)null
            };

            // Classic tube check
            bool isClassic = userAgent.Contains("YouTube v1.0.0", StringComparison.OrdinalIgnoreCase);
            string templateName = isClassic ? "classic_search.scriban" : "search_results.scriban";

            var rendered = TemplatesLoader.Instance.RenderTemplate(templateName, templateData);
            return Results.Content(rendered, "application/atom+xml; charset=utf-8");
        }

        public static async Task<IResult> GetVideo(string videoId)
        {
            ConfigReader config = ConfigReader.Instance;

            // Prepare Invidious API base
            var apiBase = config.InvidiusURL.TrimEnd('/');
            var apiurl = $"{apiBase}/api/v1/videos/{videoId}";

            // Fetch API with caching (4 hour expiration)
            string? content = string.Empty;
            // 3 tries to fetch video data, starts at one for displaying.
            for (int i = 1; i < 6 && string.IsNullOrEmpty(content); i++)
            {
                try
                {
                    content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromHours(4));
                    if (string.IsNullOrEmpty(content))
                    {
                        Console.WriteLine($"On try {i} /getvideo/{videoId} failed to fetch from API");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" On try {i} Error fetching {apiurl}: {ex.Message}");
                }
                if (string.IsNullOrEmpty(content) && i < 2)
                {
                    await Task.Delay(100 * i);
                }
            }
            if (string.IsNullOrEmpty(content))
            {
                Console.WriteLine($"/getvideo/{videoId} failed to fetch from API after retries");
                return Results.Content("Failed to fetch video data", "text/plain", statusCode: 500);
            }

            // Parse the JSON response
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Check if formatStreams exists
                if (!root.TryGetProperty("formatStreams", out var formatStreams) ||
               formatStreams.ValueKind != JsonValueKind.Array ||
     formatStreams.GetArrayLength() == 0)
                {
                    Console.WriteLine($"/getvideo/{videoId} had no formatStreams");
                    return Results.Content("Video format streams not available", "text/plain", statusCode: 404);
                }

                // Get the first format stream URL (360p if enabled)
                var firstStream = formatStreams[0];
                if (firstStream.TryGetProperty("url", out var urlProperty))
                {
                    string? streamUrl = urlProperty.GetString();
                    if (!string.IsNullOrEmpty(streamUrl))
                    {
                        // Return 307 Temporary Redirect (same as Python redirect with code=307)
                        return Results.Redirect(streamUrl, false, true);
                    }
                }

                Console.WriteLine($"/getvideo/{videoId} - formatStreams[0] had no url property");
                return Results.Content("Video stream URL not found", "text/plain", statusCode: 404);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing video JSON for {videoId}: {ex.Message}");
                return Results.Content("Failed to parse video data", "text/plain", statusCode: 500);
            }
        }
    }
}
