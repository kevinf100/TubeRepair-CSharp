using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Scriban.Syntax;
using StackExchange.Redis;

namespace TubeRepair_CSharp
{
    /// <summary>
    /// HTTP client with optional Redis caching support
    /// </summary>
    public class CachedHttpClient
    {
        private static readonly CachedHttpClient _instance = new();
        public static CachedHttpClient Instance => _instance;

        private readonly HttpClient _httpClient;
        private readonly ConnectionMultiplexer? _redis;
        private readonly IDatabase? _redisDb;
        private readonly ConfigReader _config;

        private CachedHttpClient()
        {
            _config = ConfigReader.Instance;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TubeRepair");

            // Initialize Redis if enabled
            if (_config.UseRedis)
            {
                Console.WriteLine($"Redis trying: {_config.RedisIP}:{_config.RedisPort}");
                try
                {
                    var redisConnection = $"{_config.RedisIP}:{_config.RedisPort}";
                    _redis = ConnectionMultiplexer.Connect(redisConnection);
                    _redisDb = _redis.GetDatabase();
                    Console.WriteLine($"Redis connected: {redisConnection}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to connect to Redis: {ex.Message}");
                    _redis = null;
                    _redisDb = null;
                }
            }
        }

        /// <summary>
        /// Performs a GET request with optional caching
        /// </summary>
        /// <param name="url">The URL to fetch</param>
        /// <param name="expireAfter">Cache expiration time. If null, defaults to 1 hour</param>
        /// <returns>Response content as string</returns>
        public async Task<string?> GetAsync(string url, TimeSpan? expireAfter = null)
        {
            // Default expiration is 1 hour
            var expiration = expireAfter ?? TimeSpan.FromHours(1);

            // Generate cache key from URL
            string cacheKey = GenerateCacheKey(url);

            // Try to get from cache if Redis is enabled
            if (_redisDb != null)
            {
                try
                {
                    var cachedValue = await _redisDb.StringGetAsync(cacheKey);
                    if (cachedValue.HasValue)
                    {
                        Console.WriteLine($"Cache hit: {url}");
                        if (string.IsNullOrEmpty(cachedValue))
                        {
                            Console.WriteLine("Cached value is empty string");
                            await _redisDb.KeyDeleteAsync(cacheKey);
                        }
                        else
                        {
                            //Console.WriteLine($"Cache hit: {cachedValue}");
                            return cachedValue.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Redis get error: {ex.Message}");
                }
            }

            // Fetch from source
            try
            {
                //Console.WriteLine($"Fetching: {url}");
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"HTTP error {response.StatusCode} for {url}");
                    return null;
                }

                string content = await response.Content.ReadAsStringAsync();

                // Cache the response if Redis is enabled
                if (_redisDb != null && !string.IsNullOrEmpty(content))
                {
                    try
                    {
                        await _redisDb.StringSetAsync(cacheKey, content, expiration);
                        //Console.WriteLine($"Cached: {url} (expires in {expiration.TotalMinutes} minutes)");
                        //Console.WriteLine(content);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Redis set error: {ex.Message}");
                    }
                }

                return content;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTTP request error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Performs a GET request and returns the HttpResponseMessage with optional caching
        /// </summary>
        /// <param name="url">The URL to fetch</param>
        /// <param name="expireAfter">Cache expiration time. If null, defaults to 1 hour</param>
        /// <returns>HttpResponseMessage</returns>
        public async Task<HttpResponseMessage> GetResponseAsync(string url, TimeSpan? expireAfter = null)
        {
            // Default expiration is 1 hour
            var expiration = expireAfter ?? TimeSpan.FromHours(1);

            // Generate cache key from URL
            string cacheKey = GenerateCacheKey(url);

            // Try to get from cache if Redis is enabled
            if (_redisDb != null)
            {
                try
                {
                    var cachedValue = await _redisDb.StringGetAsync(cacheKey);
                    if (cachedValue.HasValue)
                    {
                        Console.WriteLine($"Cache hit: {url}");
                        var cachedResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new StringContent(cachedValue.ToString(), Encoding.UTF8, "application/json")
                        };
                        return cachedResponse;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Redis get error: {ex.Message}");
                }
            }

            // Fetch from source
            Console.WriteLine($"Fetching: {url}");
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                // Read and cache the content
                string content = await response.Content.ReadAsStringAsync();

                if (_redisDb != null && !string.IsNullOrEmpty(content))
                {
                    try
                    {
                        await _redisDb.StringSetAsync(cacheKey, content, expiration);
                        Console.WriteLine($"Cached: {url} (expires in {expiration.TotalMinutes} minutes)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Redis set error: {ex.Message}");
                    }
                }

                // Create a new response with the content
                var newResponse = new HttpResponseMessage(response.StatusCode)
                {
                    Content = new StringContent(content, Encoding.UTF8, response.Content.Headers.ContentType?.MediaType ?? "application/json")
                };

                return newResponse;
            }

            return response;
        }

        /// <summary>
        /// Generates a cache key from a URL using SHA256 hash
        /// </summary>
        private static string GenerateCacheKey(string url)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));
            return $"cache:info:{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
        }

        /// <summary>
        /// Clears all cached entries (if Redis is enabled)
        /// </summary>
        public async Task ClearCacheAsync()
        {
            if (_redis == null)
                return;

            try
            {
                var endpoints = _redis.GetEndPoints();
                foreach (var endpoint in endpoints)
                {
                    var server = _redis.GetServer(endpoint);
                    await server.FlushDatabaseAsync();
                }
                Console.WriteLine("Cache cleared");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing cache: {ex.Message}");
            }
        }
    }
}
