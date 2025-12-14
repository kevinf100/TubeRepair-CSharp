using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace TubeRepair_CSharp
{
    public class ConfigReader
    {
        private static readonly ConfigReader instance = new();
        public static ConfigReader Instance
        {
            get
            {
                return instance;
            }
        }
        static ConfigReader()
        {
        }

        public string ServerID { get; private set; } = string.Empty;
        public bool UseRedis { get; private set; } = false;
        public string RedisPort { get; private set; } = "6379";
        public string RedisIP { get; private set; } = string.Empty;
        public string InvidiusURL { get; private set; } = "https://inv.uptimetrackers.com/"; //https://redirect.invidious.io/";
        public int FeaturedVideosCount { get; private set; } = 20;
        public int CommentCount { get; private set; } = 20;
        public string SortComments { get; private set; } = "popular";
        public bool UsePlaylistTrending { get; private set; } = false;
        public string TrendingPlaylistDefault { get; private set; } = "PL-p0-Yh03xpi2AsCiyuafMeQrMF6czMoL";
        public string TrendingPlaylistFilm { get; private set; } = "PL-p0-Yh03xpiso5oS6ZEa7PeV8wHp2dVp";
        public string TrendingPlaylistGames { get; private set; } = "PL-p0-Yh03xpi_x9L-Lqop_Kj6MTY38jqv";
        public string TrendingPlaylistMusic { get; private set; } = "PL-p0-Yh03xpgeN91B_sPpv4lJY-UfThEi";

        private void ServerIDHandler()
        {
            try
            {
                using StreamReader sr = new("serverid.txt");
                string? line = sr.ReadLine();
                if (!string.IsNullOrEmpty(line))
                {
                    ServerID = line;
                    Console.WriteLine($"ServerID Found {line}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("The file could not be read if this is the first run ignore this");
                Console.WriteLine(e.Message);
                Console.WriteLine("End error");
            }
            finally
            {
                if (string.IsNullOrEmpty(ServerID))
                {
                    var chars = Enumerable.Range(0, 10)
                    .Select(_ => "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"[Random.Shared.Next(36)])
                    .ToArray();
                    ServerID = new string(chars);
                    try
                    {
                        using StreamWriter sw = new("serverid.txt");
                        sw.WriteLine(ServerID);
                        Console.WriteLine($"ServerID Created {ServerID}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("The file could not be written");
                        Console.WriteLine(e.Message);
                        Console.WriteLine("End error");
                    }
                }
            }
        }

        private void RedisHandler(IConfiguration config)
        {
            UseRedis = FindEnvironmentVariableOrConfigBool(config, "USE_REDIS", false);
            if (!UseRedis)
            {
                Console.WriteLine("Redis Disabled");
                return;
            }
            RedisIP = FindEnvironmentVariableOrConfig(config, "REDIS_HOST", string.Empty);
            if (string.IsNullOrEmpty(RedisIP))
            {
                UseRedis = false;
                Console.WriteLine($"Redis IP IsNullOrEmpty true.");
            }
            else
            {
                RedisPort = FindEnvironmentVariableOrConfig(config, "REDIS_PORT", "6379");
                Console.WriteLine($"Redis Enabled IP: {RedisIP} PORT: {RedisPort}");
            }
        }
        private static string FindEnvironmentVariableOrConfig(IConfiguration config, string variableName, string defaultReturn)
        {
            string? envValue = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrEmpty(envValue))
            {
                return envValue;
            }
            string? configValue = config[variableName];
            return configValue ?? defaultReturn;
        }
        private static bool FindEnvironmentVariableOrConfigBool(IConfiguration config, string variableName, bool defaultReturn)
        {
            string? envValue = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrEmpty(envValue) && bool.TryParse(envValue, out var boolEnvValue))
            {
                return boolEnvValue;
            }
            string? configValue = config[variableName];
            if (!string.IsNullOrEmpty(configValue) && bool.TryParse(configValue, out var boolConfigValue))
            {
                return boolConfigValue;
            }
            return defaultReturn;
        }
        private static int FindEnvironmentVariableOrConfigInt(IConfiguration config, string variableName, int defaultReturn)
        {
            string? envValue = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrEmpty(envValue) && int.TryParse(envValue, out var intEnvValue))
            {
                return intEnvValue;
            }
            string? configValue = config[variableName];
            if (!string.IsNullOrEmpty(configValue) && int.TryParse(configValue, out var intConfigValue))
            {
                return intConfigValue;
            }
            return defaultReturn;
        }
        private ConfigReader()
        {
            IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();
            RedisHandler(config);
            ServerIDHandler();
            InvidiusURL = FindEnvironmentVariableOrConfig(config, "URL", InvidiusURL);
            Console.WriteLine($"Invidious URL: {InvidiusURL}");
            FeaturedVideosCount = FindEnvironmentVariableOrConfigInt(config, "FEATURED_VIDEOS", FeaturedVideosCount);
            CommentCount = FindEnvironmentVariableOrConfigInt(config, "COMMENTS", CommentCount);
            SortComments = FindEnvironmentVariableOrConfig(config, "SORT_COMMENTS", SortComments);
            UsePlaylistTrending = FindEnvironmentVariableOrConfigBool(config, "USE_PLAYLIST_TRENDING", UsePlaylistTrending);
            Console.WriteLine($"Use Playlist Trending: {UsePlaylistTrending}");
            TrendingPlaylistDefault = FindEnvironmentVariableOrConfig(config, "TRENDING_PLAYLIST_DEFAULT", TrendingPlaylistDefault);
            TrendingPlaylistFilm = FindEnvironmentVariableOrConfig(config, "TRENDING_PLAYLIST_FILM", TrendingPlaylistFilm);
            TrendingPlaylistGames = FindEnvironmentVariableOrConfig(config, "TRENDING_PLAYLIST_GAMES", TrendingPlaylistGames);
            TrendingPlaylistMusic = FindEnvironmentVariableOrConfig(config, "TRENDING_PLAYLIST_MUSIC", TrendingPlaylistMusic);
        }

    }
}
