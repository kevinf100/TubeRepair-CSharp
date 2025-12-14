using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
        /// Get channel information
        /// </summary>
        public static async Task<IResult> GetChannelInfo(HttpRequest request, string channelId, int? res = null)
        {
            ConfigReader config = ConfigReader.Instance;

            // Validate channel ID
            if (string.IsNullOrEmpty(channelId))
            {
                return Results.Content("Missing channel ID", "text/plain", statusCode: 400);
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
            if (string.IsNullOrEmpty(channelId))
            {
                return Results.Content("Missing channel ID", "text/plain", statusCode: 400);
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
