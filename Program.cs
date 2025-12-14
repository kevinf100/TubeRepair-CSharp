using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using TubeRepair_CSharp.api;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;

namespace TubeRepair_CSharp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            
            var app = builder.Build();
            _ = CachedHttpClient.Instance; // Initialize singleton instance

            HtmlBuilder htmlBuilder = HtmlBuilder.Instance;

            // Middleware to normalize double slashes in URLs - MUST BE BEFORE UseRouting
            app.Use(async (context, next) =>
            {
                var requestFeature = context.Features.Get<IHttpRequestFeature>();
                if (requestFeature != null)
                {
                    var rawTarget = requestFeature.RawTarget;
                    if (!string.IsNullOrEmpty(rawTarget) && rawTarget.Contains("//"))
                    {
                        // Replace multiple consecutive slashes with a single slash
                        var normalizedTarget = PathNormalizer.MultipleSlashes().Replace(rawTarget, "/");
                        requestFeature.RawTarget = normalizedTarget;
                        
                        // Parse the path and query string
                        var parts = normalizedTarget.Split('?', 2);
                        var normalizedPath = parts[0];
                        var queryString = parts.Length > 1 ? "?" + parts[1] : string.Empty;
                        
                        requestFeature.Path = normalizedPath;
                        requestFeature.QueryString = queryString;
                    }
                }
                await next();
            });

            // Explicitly use routing AFTER our path normalization middleware
            app.UseRouting();

            // Use endpoints for our routes
            // Root HTML
            app.MapGet("/", () => Results.Content(htmlBuilder.Html, "text/html"));

            Channel.LoadRoutes(app);

            // Routes similar to Flask decorators
            // Note: More specific routes must come before less specific ones

            // /feeds/api/standardfeeds/<regioncode>/<popular>
            app.MapGet("/feeds/api/standardfeeds/{regioncode}/{popular}", async (HttpRequest req, string regioncode, string popular) =>
            {
                return await Video.Frontpage(req, regioncode, popular, null);
            });

            // /{int:res}/feeds/api/standardfeeds/<regioncode>/<popular>
            app.MapGet("/{res:int}/feeds/api/standardfeeds/{regioncode}/{popular}", async (HttpRequest req, int res, string regioncode, string popular) =>
            {
                return await Video.Frontpage(req, regioncode, popular, res);
            });

            // /feeds/api/standardfeeds/<popular> - single parameter assumes US region
            app.MapGet("/feeds/api/standardfeeds/{popular}", async (HttpRequest req, string popular) =>
            {
                return await Video.Frontpage(req, "US", popular, null);
            });

            // /{int:res}/feeds/api/standardfeeds/<popular>
            app.MapGet("/{res:int}/feeds/api/standardfeeds/{popular}", async (HttpRequest req, int res, string popular) =>
            {
                return await Video.Frontpage(req, "US", popular, res);
            });

            // Search videos routes
            // /feeds/api/videos
            app.MapGet("/feeds/api/videos", async (HttpRequest req) =>
            {
                return await Video.SearchVideos(req, null);
            });

            // /{int:res}/feeds/api/videos
            app.MapGet("/{res:int}/feeds/api/videos", async (HttpRequest req, int res) =>
            {
                return await Video.SearchVideos(req, res);
            });

            // Comments routes
            // /api/videos/<videoid>/comments
            app.MapGet("/api/videos/{videoid}/comments", async (HttpRequest req, string videoid) =>
            {
                return await Video.Comments(req, videoid, null);
            });

            // /{int:res}/api/videos/<videoid>/comments
            app.MapGet("/{res:int}/api/videos/{videoid}/comments", async (HttpRequest req, int res, string videoid) =>
            {
                return await Video.Comments(req, videoid, res);
            });

            // /feeds/api/videos/<videoid>/comments
            app.MapGet("/feeds/api/videos/{videoid}/comments", async (HttpRequest req, string videoid) =>
            {
                return await Video.Comments(req, videoid, null);
            });

            // /{int:res}/feeds/api/videos/<videoid>/comments
            app.MapGet("/{res:int}/feeds/api/videos/{videoid}/comments", async (HttpRequest req, int res, string videoid) =>
            {
                return await Video.Comments(req, videoid, res);
            });

            // Related/Suggested videos routes
            // /feeds/api/videos/<video_id>/related
            app.MapGet("/feeds/api/videos/{videoId}/related", async (HttpRequest req, string videoId) =>
            {
                return await Video.GetRelated(req, videoId, null);
            });

            // /{int:res}/feeds/api/videos/<video_id>/related
            app.MapGet("/{res:int}/feeds/api/videos/{videoId}/related", async (HttpRequest req, int res, string videoId) =>
            {
                return await Video.GetRelated(req, videoId, res);
            });

            // GetVideo routes
            // /getvideo/<video_id>
            app.MapGet("/getvideo/{videoId}", async (HttpRequest req, string videoId) =>
            {
                return await Video.GetVideo(videoId);
            });

            // /{int:res}/getvideo/<video_id>
            app.MapGet("/{res:int}/getvideo/{videoId}", async (HttpRequest req, int res, string videoId) =>
            {
                return await Video.GetVideo(videoId);
            });

            // Static content routes
            // Categories sidebar
            app.MapGet("/schemas/2007/categories.cat", () => StaticAPI.CategoriesCat());
            app.MapGet("/{res:int}/schemas/2007/categories.cat", (int res) => StaticAPI.CategoriesCat());

            // Legacy login bypass for YouTube Classic (layer 1)
            app.MapPost("/youtube/accounts/applelogin1", () => StaticAPI.LegacyLoginBypass1());
            app.MapPost("/{res:int}/youtube/accounts/applelogin1", (int res) => StaticAPI.LegacyLoginBypass1());

            // Legacy login bypass for YouTube Classic (layer 2)
            app.MapPost("/youtube/accounts/applelogin2", () => StaticAPI.LegacyLoginBypass2());
            app.MapPost("/{res:int}/youtube/accounts/applelogin2", (int res) => StaticAPI.LegacyLoginBypass2());

            // Login bypass for Google YouTube
            app.MapPost("/youtube/accounts/registerDevice", () => StaticAPI.LoginBypass());
            app.MapPost("/{res:int}/youtube/accounts/registerDevice", (int res) => StaticAPI.LoginBypass());

            // Playlist routes
            // Channel playlists
            app.MapGet("/feeds/api/users/{channelId}/playlists", async (HttpRequest req, string channelId) =>
            {
                return await Playlist.GetChannelPlaylists(req, channelId, null);
            });

            app.MapGet("/{res:int}/feeds/api/users/{channelId}/playlists", async (HttpRequest req, int res, string channelId) =>
            {
                return await Playlist.GetChannelPlaylists(req, channelId, res);
            });

            // Playlist videos
            app.MapGet("/feeds/api/playlists/{playlistId}", async (HttpRequest req, string playlistId) =>
            {
                return await Playlist.GetPlaylistVideos(req, playlistId, null);
            });

            app.MapGet("/{res:int}/feeds/api/playlists/{playlistId}", async (HttpRequest req, int res, string playlistId) =>
            {
                return await Playlist.GetPlaylistVideos(req, playlistId, res);
            });

            // Playlist search
            app.MapGet("/feeds/api/playlists/snippets", async (HttpRequest req) =>
            {
                return await Playlist.SearchPlaylists(req, null);
            });

            app.MapGet("/{res:int}/feeds/api/playlists/snippets", async (HttpRequest req, int res) =>
            {
                return await Playlist.SearchPlaylists(req, res);
            });

            app.Run();
        }
    }

    /// <summary>
    /// Provides compiled regex patterns for path normalization
    /// </summary>

    /// <summary>
    /// Provides compiled regex patterns for path normalization
    /// </summary>
    internal static partial class PathNormalizer
    {
        [GeneratedRegex("/+")]
        internal static partial Regex MultipleSlashes();
    }
}
