using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace TubeRepair_CSharp
{
    public static class Helpers
    {
        public static readonly Dictionary<string, string> ValidSearchOrderBy = new()
        {
            { "relevance", "relevance" },
            { "published", "date" },
            { "viewCount", "views" },
            { "rating", "rating" }
        };

        public static readonly Dictionary<string, string> ValidSearchTime = new()
        {
            { "today", "today" },
            { "this_week", "week" },
            { "this_month", "month" }
        };

        public static readonly Dictionary<string, string> ValidSearchDuration = new()
        {
            { "short", "short" },
            { "long", "long" }
        };

        /// <summary>
        /// Converts Unix timestamp to ISO 8601 format
        /// </summary>
        public static string UnixToIso8601(long unixTimestamp)
        {
            var dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
            return dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }

        /// <summary>
        /// Process start-index query parameter for pagination
        /// </summary>
        public static (string currentPage, string nextPage) ProcessStartIndex(HttpRequest request)
        {
            // Get 'start-index' query for later use
            string? startIndex = request.Query["start-index"];

            // Get current page or start at the first page if 'start-index' is missing or invalid
            string currentPage;
            if (!string.IsNullOrEmpty(startIndex) && int.TryParse(startIndex, out _))
            {
                currentPage = startIndex;
            }
            else
            {
                currentPage = "1";
            }

            // Setup for next page
            int nextPageNumber = int.Parse(currentPage) + 1;

            // Getting current url with all the query info
            string nextPage = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";

            // Checks if we have a 'start-index'
            if (!string.IsNullOrEmpty(startIndex))
            {
                // Replace for next page
                nextPage = nextPage.Replace($"start-index={currentPage}", $"start-index={nextPageNumber}");
            }
            else
            {
                // Add query for next page
                char separator = nextPage.Contains('?') ? '&' : '?';
                nextPage += $"{separator}start-index={nextPageNumber}";
            }

            // Sanitize
            nextPage = nextPage.Replace("&", "&amp;");

            return (currentPage, nextPage);
        }
    }
}
