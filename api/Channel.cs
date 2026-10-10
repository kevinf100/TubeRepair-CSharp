using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Security;
using System.Text.Json;
using System.Xml;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TubeRepair_CSharp.Templates;

namespace TubeRepair_CSharp.api
{
    public class Channel
    {
        /// <summary>
        /// feeds/api/channels/&lt;channel_id&gt; <br/>
        /// &lt;int:res&gt;/feeds/api/channels/&lt;channel_id&gt; <br/>
        /// feeds/api/channels <br/>
        /// &lt;int:res&gt;/feeds/api/channels <br/>
        /// feeds/api/users/&lt;channel_id/&gt;uploads <br/>
        /// &lt;int:res&gt;/feeds/api/users/&lt;channel_id&gt;/uploads <br/>
        /// </summary>
        public static void LoadRoutes(WebApplication app)
        {
            // Channel Activity uses an events feed, not the uploads feed.
            app.MapGet("/feeds/api/events", (HttpRequest req) => GetActivity(req));
            app.MapGet("/{res:int}/feeds/api/events", (HttpRequest req, int res) => GetActivity(req, res));

            // Channel routes
            // Channel info
            app.MapGet("/feeds/api/channels/{channelId}", async (HttpRequest req, string channelId) =>
            {
                return await Channel.GetChannelInfo(req, channelId, null);
            });

            app.MapGet("/{res:int}/feeds/api/channels/{channelId}", async (HttpRequest req, int res, string channelId) =>
            {
                return await Channel.GetChannelInfo(req, channelId, res);
            });

            // Channel search
            app.MapGet("/feeds/api/channels", async (HttpRequest req) =>
            {
                return await Channel.SearchChannels(req, null);
            });

            app.MapGet("/{res:int}/feeds/api/channels", async (HttpRequest req, int res) =>
            {
                return await Channel.SearchChannels(req, res);
            });

            // Channel uploads
            app.MapGet("/feeds/api/users/{channelId}/uploads", async (HttpRequest req, string channelId) =>
            {
                return await Channel.GetUploads(req, channelId, null);
            });

            app.MapGet("/{res:int}/feeds/api/users/{channelId}/uploads", async (HttpRequest req, int res, string channelId) =>
            {
                return await Channel.GetUploads(req, channelId, res);
            });
        }
        /// <summary>
        /// Translate recent channel uploads into legacy GData upload events.
        /// Inline video entries are always included for the channel Activity client.
        /// </summary>
        public static async Task<IResult> GetActivity(HttpRequest request, int? res = null)
        {
            string? channelId = request.Query["author"];
            if (string.IsNullOrWhiteSpace(channelId))
                return Results.Content("Missing channel query parameter 'author'", "text/plain", statusCode: 400);
            if (!InputValidator.IsValidChannelId(channelId))
                return Results.Content("Invalid channel ID", "text/plain", statusCode: 400);

            int pageSize = ConfigReader.Instance.ActivityMaxResults;
            if (request.Query.ContainsKey("max-results"))
            {
                if (!int.TryParse(request.Query["max-results"], out int requestedSize) || requestedSize < 1)
                    return Results.Content("Invalid 'max-results' parameter", "text/plain", statusCode: 400);
                pageSize = Math.Min(requestedSize, pageSize);
            }
            int startIndex = 1;
            if (request.Query.ContainsKey("start-index") &&
                (!int.TryParse(request.Query["start-index"], out startIndex) || startIndex < 1))
                return Results.Content("Invalid 'start-index' parameter", "text/plain", statusCode: 400);

            string? token = request.Query["continuation"];
            int skip = string.IsNullOrEmpty(token) ? startIndex - 1 : 0;
            // Our next links retain the offset when a capped response ends inside an Invidious page.
            if (request.Query.ContainsKey("page-offset") &&
                (!int.TryParse(request.Query["page-offset"], out skip) || skip < 0))
                return Results.Content("Invalid 'page-offset' parameter", "text/plain", statusCode: 400);

            if (res.HasValue)
                res = Math.Clamp(res.Value, 144, 10000);
            string url = $"{request.Scheme}://{request.Host}{request.PathBase}";
            if (res.HasValue)
                url += $"/{res.Value}";

            // Escape all dynamic XML values before passing them to Scriban.
            static string Xml(string value) => SecurityElement.Escape(XmlConvert.VerifyXmlChars(value)) ?? "";
            static string ReadString(JsonElement element, string name, string fallback = "")
            {
                if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                    return fallback;
                if (value.ValueKind != JsonValueKind.String)
                    throw new JsonException($"Invalid {name}");
                return value.GetString() ?? fallback;
            }
            static long ReadNumber(JsonElement element, string name)
            {
                if (!element.TryGetProperty(name, out var value))
                    return 0;
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
                    throw new JsonException($"Invalid {name}");
                return number;
            }

            Dictionary<string, object?> templateData;
            try
            {
                var selectedVideos = new List<JsonElement>();
                var visitedTokens = new HashSet<string>();
                string? currentToken = token;
                string? nextToken = null;
                int nextOffset = 0;
                bool hasNext = false;
                while (true)
                {
                    if (!visitedTokens.Add(currentToken ?? ""))
                        throw new JsonException("Repeated channel continuation token");
                    string apiUrl = $"{ConfigReader.Instance.InvidiusURL.TrimEnd('/')}/api/v1/channels/{channelId}/videos?sort_by=newest";
                    if (!string.IsNullOrEmpty(currentToken))
                        apiUrl += $"&continuation={Uri.EscapeDataString(currentToken)}";
                    using var response = await CachedHttpClient.Instance.GetResponseAsync(apiUrl, TimeSpan.FromMinutes(30));
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return Results.Content("Channel not found", "text/plain", statusCode: 404);
                    if (!response.IsSuccessStatusCode)
                        return Results.Content("Failed to fetch channel activity", "text/plain", statusCode: 502);
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _) ||
                        !root.TryGetProperty("videos", out var videos) || videos.ValueKind != JsonValueKind.Array)
                        throw new JsonException("Missing channel videos data");

                    int count = videos.GetArrayLength();
                    int offset = Math.Min(skip, count);
                    skip -= offset;
                    while (offset < count && selectedVideos.Count < pageSize)
                        selectedVideos.Add(videos[offset++].Clone());

                    string continuation = ReadString(root, "continuation");
                    if (offset < count)
                    {
                        hasNext = true;
                        nextToken = currentToken;
                        nextOffset = offset;
                        break;
                    }
                    if (string.IsNullOrEmpty(continuation))
                        break;
                    if (selectedVideos.Count == pageSize)
                    {
                        hasNext = true;
                        nextToken = continuation;
                        break;
                    }
                    currentToken = continuation;
                }

                var events = new List<object>();
                long latestPublished = 0;
                foreach (var video in selectedVideos)
                {
                    if (video.ValueKind != JsonValueKind.Object)
                        throw new JsonException("Invalid channel video");
                    string videoId = ReadString(video, "videoId");
                    if (!InputValidator.IsValidVideoId(videoId))
                        throw new JsonException("Invalid video ID");
                    long published = ReadNumber(video, "published");
                    latestPublished = Math.Max(latestPublished, published);
                    string authorId = ReadString(video, "authorId", channelId);
                    if (string.IsNullOrEmpty(authorId))
                        authorId = channelId;
                    string author = ReadString(video, "author", authorId);
                    if (string.IsNullOrEmpty(author))
                        author = authorId;
                    events.Add(new Dictionary<string, object>
                    {
                        ["id"] = Xml($"tag:youtube.com,2008:user:{channelId}:event:video_uploaded:{videoId}"),
                        ["video_id"] = Xml(videoId),
                        ["title"] = Xml(ReadString(video, "title")),
                        ["description"] = Xml(ReadString(video, "description")),
                        ["author"] = Xml(author),
                        ["author_id"] = Xml(authorId),
                        ["author_uri"] = Xml($"{url}/feeds/api/channels/{Uri.EscapeDataString(authorId)}"),
                        ["published"] = Helpers.UnixToIso8601(published),
                        ["duration"] = Math.Max(0, ReadNumber(video, "lengthSeconds")),
                        ["view_count"] = Math.Max(0, ReadNumber(video, "viewCount")),
                        ["playback_url"] = Xml($"{url}/getvideo/{videoId}"),
                        ["comments_url"] = Xml($"{url}/api/videos/{videoId}/comments"),
                        ["related_url"] = Xml($"{url}/feeds/api/videos/{videoId}/related")
                    });
                }

                string feedUrl = $"{url}/feeds/api/events?author={Uri.EscapeDataString(channelId)}&inline=true";
                string pageUrl = $"{feedUrl}&max-results={pageSize}";
                string selfUrl = $"{pageUrl}&start-index={startIndex}";
                if (!string.IsNullOrEmpty(token))
                    selfUrl += $"&continuation={Uri.EscapeDataString(token)}";
                if (request.Query.ContainsKey("page-offset"))
                    selfUrl += $"&page-offset={request.Query["page-offset"]}";
                string? nextUrl = null;
                if (hasNext)
                {
                    nextUrl = $"{pageUrl}&start-index={(long)startIndex + events.Count}&page-offset={nextOffset}";
                    if (!string.IsNullOrEmpty(nextToken))
                        nextUrl += $"&continuation={Uri.EscapeDataString(nextToken)}";
                }
                templateData = new Dictionary<string, object?>
                {
                    ["data"] = events,
                    ["feed_url"] = Xml(feedUrl),
                    ["self_url"] = Xml(selfUrl),
                    ["next_url"] = nextUrl == null ? null : Xml(nextUrl),
                    ["channel_id"] = Xml(channelId),
                    ["updated"] = Helpers.UnixToIso8601(latestPublished),
                    ["item_count"] = events.Count,
                    ["start_index"] = startIndex,
                    // Invidious supplies no total; report the known lower bound, including a next item.
                    ["total_results"] = (long)startIndex - 1 + events.Count + (hasNext ? 1 : 0)
                };
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error fetching channel activity: {ex.Message}");
                return Results.Content("Failed to fetch channel activity", "text/plain", statusCode: 502);
            }
            catch (TaskCanceledException ex)
            {
                Console.WriteLine($"Timeout fetching channel activity: {ex.Message}");
                return Results.Content("Timed out fetching channel activity", "text/plain", statusCode: 504);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or XmlException)
            {
                Console.WriteLine($"Error parsing channel activity JSON: {ex.Message}");
                return Results.Content("Failed to parse channel activity", "text/plain", statusCode: 502);
            }

            string rendered = TemplatesLoader.Instance.RenderTemplate("activity.scriban", templateData);
            return Results.Content(rendered, "application/atom+xml; charset=utf-8");
        }
        /// <summary>
        /// Get channel information
        /// </summary>
        public static async Task<IResult> GetChannelInfo(HttpRequest request, string channelId, int? res = null)
        {
            ConfigReader config = ConfigReader.Instance;

            // Validate channel ID
            if (!InputValidator.IsValidChannelId(channelId))
            {
                return Results.Content("Invalid channel ID", "text/plain", statusCode: 400);
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

            // Prepare Invidious API base
            // TODO: Make this a config setting letting users use innertube or Invidious!
            var apiBase = config.InvidiusURL.TrimEnd('/');
            var apiurl = $"{apiBase}/api/v1/channels/{channelId}";

            // Fetch API with caching (2 hour expiration - channel info doesn't change often)
            string? content;
            try
            {
                content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromHours(2));
                if (content == null)
                {
                    return Results.Content("Failed to fetch channel info", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch channel info", "text/plain", statusCode: 500);
            }

            // Parse the JSON response
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Check for error
                if (root.TryGetProperty("error", out var errorProp))
                {
                    return Results.Content("Channel not found", "text/plain", statusCode: 404);
                }

                // Extract channel information
                string channelUrl = root.GetProperty("authorId").GetString() ?? channelId;
                string channelName = root.GetProperty("author").GetString() ?? "";
                string channelPicUrl = "";
                long subCount = 0;
                string description = "";

                // Get profile picture from authorThumbnails
                if (root.TryGetProperty("authorThumbnails", out var thumbnails) &&
             thumbnails.ValueKind == JsonValueKind.Array &&
             thumbnails.GetArrayLength() > 0)
                {
                    var firstThumb = thumbnails[0];
                    if (firstThumb.TryGetProperty("url", out var thumbUrl))
                    {
                        channelPicUrl = thumbUrl.GetString() ?? "";
                    }
                }

                // Get subscriber count
                if (root.TryGetProperty("subCount", out var subCountProp))
                {
                    subCount = subCountProp.GetInt64();
                }

                // Get description
                if (root.TryGetProperty("description", out var descProp))
                {
                    description = descProp.GetString() ?? "";
                }

                // Render template
                var templateData = new Dictionary<string, object?>
                {
                    ["author"] = channelName,
                    ["author_id"] = channelUrl,
                    ["channel_pic_url"] = channelPicUrl,
                    ["subcount"] = subCount,
                    ["description"] = description,
                    ["url"] = url
                };

                var rendered = TemplatesLoader.Instance.RenderTemplate("channel_info.scriban", templateData);
                return Results.Content(rendered, "application/atom+xml; charset=utf-8");
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing channel info JSON: {ex.Message}");
                return Results.Content("Failed to parse channel info", "text/plain", statusCode: 500);
            }
        }

        /// <summary>
        /// Search for channels
        /// </summary>
        public static async Task<IResult> SearchChannels(HttpRequest request, int? res = null)
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

            // Get search query
            string? query = request.Query["q"];
            if (string.IsNullOrEmpty(query))
            {
                return Results.Content("Missing search query parameter 'q'", "text/plain", statusCode: 400);
            }

            // URL encode the query
            query = HttpUtility.UrlEncode(query);

            // Process pagination
            var (currentPage, nextPage) = Helpers.ProcessStartIndex(request);

            // Prepare Invidious API base
            var apiBase = config.InvidiusURL.TrimEnd('/');
            var apiurl = $"{apiBase}/api/v1/search?q={query}&type=channel&page={currentPage}";

            // Fetch API with caching (1 hour expiration)
            string? content;
            try
            {
                content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromHours(1));
                if (content == null)
                {
                    return Results.Content("Failed to fetch channel search results", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch channel search results", "text/plain", statusCode: 500);
            }

            // Parse the JSON response
            List<object>? data = null;
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var root = doc.RootElement;
                    data = new List<object>();
                    for (int i = 0; i < root.GetArrayLength(); i++)
                    {
                        var channel = root[i];
                        var channelDict = new Dictionary<string, object>
                        {
                            ["author"] = channel.GetProperty("author").GetString() ?? "",
                            ["authorId"] = channel.GetProperty("authorId").GetString() ?? "",
                            ["subCount"] = channel.TryGetProperty("subCount", out var subCount) ? subCount.GetInt64() : 0
                        };

                        // Get author thumbnails
                        if (channel.TryGetProperty("authorThumbnails", out var thumbnails) &&
                                 thumbnails.ValueKind == JsonValueKind.Array)
                        {
                            var thumbList = new List<object>();
                            for (int j = 0; j < thumbnails.GetArrayLength(); j++)
                            {
                                var thumb = thumbnails[j];
                                thumbList.Add(new Dictionary<string, object>
                                {
                                    ["url"] = thumb.GetProperty("url").GetString() ?? ""
                                });
                            }
                            channelDict["authorThumbnails"] = thumbList;
                        }

                        data.Add(channelDict);
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing channel search JSON: {ex.Message}");
                return Results.Content("Failed to parse channel search results", "text/plain", statusCode: 500);
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

            var rendered = TemplatesLoader.Instance.RenderTemplate("search_results_channel.scriban", templateData);
            return Results.Content(rendered, "application/atom+xml; charset=utf-8");
        }

        /// <summary>
        /// Get channel uploads
        /// </summary>
        public static async Task<IResult> GetUploads(HttpRequest request, string channelId, int? res = null)
        {
            ConfigReader config = ConfigReader.Instance;

            // Validate channel ID
            if (!InputValidator.IsValidChannelId(channelId))
            {
                return Results.Content("Invalid channel ID", "text/plain", statusCode: 400);
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

            // Get continuation token if present
            string? continuationParam = request.Query["continuation"];
            string continuationToken = !string.IsNullOrEmpty(continuationParam)
                           ? $"&continuation={continuationParam}"
                 : "";

            // Prepare Invidious API base
            var apiBase = config.InvidiusURL.TrimEnd('/');
            // Despite documentation says /latest takes in a continuation token, it doesn't
            // sort_by is broken according to documentation and will default to newest
            // we will add it anyway in case it ever gets fixed
            var apiurl = $"{apiBase}/api/v1/channels/{channelId}/videos?sort_by=newest{continuationToken}";

            // Fetch API with caching (30 minutes - uploads change more frequently)
            string? content;
            try
            {
                content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromMinutes(30));
                if (content == null)
                {
                    return Results.Content("Failed to fetch channel uploads", "text/plain", statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {apiurl}: {ex.Message}");
                return Results.Content("Failed to fetch channel uploads", "text/plain", statusCode: 500);
            }

            // Parse the JSON response
            List<object>? videos = null;
            string? continuation = null;

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
                            ["videoId"] = video.GetProperty("videoId").GetString() ?? "",
                            ["title"] = video.GetProperty("title").GetString() ?? "",
                            ["author"] = video.TryGetProperty("author", out var author) ? author.GetString() ?? "" : "",
                            ["authorId"] = video.TryGetProperty("authorId", out var authorId) ? authorId.GetString() ?? "" : "",
                            ["lengthSeconds"] = video.TryGetProperty("lengthSeconds", out var length) ? length.GetInt32() : 0,
                            ["viewCount"] = video.TryGetProperty("viewCount", out var views) ? views.GetInt64() : 0,
                            ["published"] = video.TryGetProperty("published", out var published) ? published.GetInt64() : 0
                        };

                        // Add description if available
                        if (video.TryGetProperty("description", out var desc))
                        {
                            videoDict["description"] = desc.GetString() ?? "";
                        }

                        videos.Add(videoDict);
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
                Console.WriteLine($"Error parsing uploads JSON: {ex.Message}");
                return Results.Content("Failed to parse channel uploads", "text/plain", statusCode: 500);
            }

            // Render template
            var templateData = new Dictionary<string, object?>
            {
                ["data"] = videos,
                ["url"] = url,
                ["continuation"] = continuation
            };

            var rendered = TemplatesLoader.Instance.RenderTemplate("uploads.scriban", templateData);
            return Results.Content(rendered, "application/atom+xml; charset=utf-8");
        }
    }
}
