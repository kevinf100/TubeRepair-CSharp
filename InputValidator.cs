using System;
using System.Text.RegularExpressions;

namespace TubeRepair_CSharp
{
    /// <summary>
    /// Provides input validation to prevent injection attacks and SSRF
    /// </summary>
    public static partial class InputValidator
    {
        // YouTube video IDs are typically 11 characters: alphanumeric, hyphen, underscore
        [GeneratedRegex(@"^[a-zA-Z0-9_-]{11}$")]
        private static partial Regex VideoIdPattern();

        // Channel IDs: UC followed by 22 alphanumeric characters, OR username format
        [GeneratedRegex(@"^(UC[a-zA-Z0-9_-]{22}|[a-zA-Z0-9_-]{1,100})$")]
        private static partial Regex ChannelIdPattern();

        // Playlist IDs: PL, UU, LL, RD, etc. followed by alphanumeric characters
        [GeneratedRegex(@"^[a-zA-Z0-9_-]{13,41}$")]
        private static partial Regex PlaylistIdPattern();

        // Region codes: 2-letter country codes
        [GeneratedRegex(@"^[A-Z]{2}$")]
        private static partial Regex RegionCodePattern();

        // Generic alphanumeric with limited special chars
        [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
        private static partial Regex AlphanumericPattern();

        /// <summary>
        /// Validates a YouTube video ID
        /// </summary>
        public static bool IsValidVideoId(string? videoId)
        {
            if (string.IsNullOrWhiteSpace(videoId))
                return false;

            return VideoIdPattern().IsMatch(videoId);
        }

        /// <summary>
        /// Validates a YouTube channel ID or username
        /// </summary>
        public static bool IsValidChannelId(string? channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return false;

            return ChannelIdPattern().IsMatch(channelId);
        }

        /// <summary>
        /// Validates a YouTube playlist ID
        /// </summary>
        public static bool IsValidPlaylistId(string? playlistId)
        {
            if (string.IsNullOrWhiteSpace(playlistId))
                return false;

            return PlaylistIdPattern().IsMatch(playlistId);
        }

        /// <summary>
        /// Validates a region code (e.g., US, GB, JP)
        /// </summary>
        public static bool IsValidRegionCode(string? regionCode)
        {
            if (string.IsNullOrWhiteSpace(regionCode))
                return false;

            return RegionCodePattern().IsMatch(regionCode);
        }

        /// <summary>
        /// Validates a generic identifier (alphanumeric with hyphens and underscores)
        /// </summary>
        public static bool IsValidIdentifier(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;

            if (identifier.Length > 100)
                return false;

            return AlphanumericPattern().IsMatch(identifier);
        }
    }
}
