# Redis Caching Implementation

## Overview

The application now includes Redis caching support for all outgoing HTTP requests. This significantly improves performance by caching API responses and reducing the load on upstream services.

## Architecture

### CachedHttpClient Class

The `CachedHttpClient` class is a singleton that wraps `HttpClient` with Redis caching capabilities:

- **Singleton Pattern**: One instance shared across the entire application
- **Automatic Caching**: All HTTP GET requests are automatically cached when Redis is enabled
- **Configurable Expiration**: Each request can specify its own cache expiration time
- **Fallback Support**: Works seamlessly even when Redis is disabled or unavailable

### Key Features

1. **SHA256 Cache Keys**: URLs are hashed using SHA256 to create consistent cache keys
2. **Configurable Expiration**: Default expiration is 1 hour, but can be customized per request
3. **Automatic User-Agent**: Sets "TubeRepair" as the User-Agent header
4. **Error Resilience**: Continues working even if Redis connection fails

## Configuration

### Enable Redis Caching

You can configure Redis through either `appsettings.json` or environment variables:

#### appsettings.json
```json
{
  "USE_REDIS": true,
  "REDIS_IP": "127.0.0.1",
  "REDIS_PORT": "6379"
}
```

#### Environment Variables
```bash
USE_REDIS=true
REDIS_IP=127.0.0.1
REDIS_PORT=6379
```

### Disable Redis Caching

Simply set `USE_REDIS` to `false` in either location:

```json
{
  "USE_REDIS": false
}
```

When disabled, all requests bypass caching and go directly to the source.

## Usage

### Basic Usage

The `CachedHttpClient` is used automatically by the `Video` class for all API requests:

```csharp
// Default 1-hour cache expiration
string? content = await CachedHttpClient.Instance.GetAsync(apiurl);

// Custom cache expiration (e.g., 30 minutes)
string? content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromMinutes(30));

// Short-lived cache (e.g., 5 minutes)
string? content = await CachedHttpClient.Instance.GetAsync(apiurl, TimeSpan.FromMinutes(5));
```

### Advanced Usage

For responses that need the full `HttpResponseMessage`:

```csharp
HttpResponseMessage response = await CachedHttpClient.Instance.GetResponseAsync(apiurl, TimeSpan.FromHours(2));
```

### Clearing Cache

To manually clear all cached entries:

```csharp
await CachedHttpClient.Instance.ClearCacheAsync();
```

## How It Works

1. **Request Arrives**: Application receives an API request
2. **Generate Cache Key**: URL is hashed using SHA256 to create a unique cache key
3. **Check Redis**: If Redis is enabled, check for cached response
   - **Cache Hit**: Return cached content immediately (faster!)
   - **Cache Miss**: Proceed to step 4
4. **Fetch from Source**: Make HTTP request to upstream API
5. **Store in Cache**: Save response to Redis with expiration time
6. **Return Response**: Send response back to client

## Cache Key Format

Cache keys are generated using SHA256 hashing:

```
cache:info:<sha256_hash_of_url>
```

Example:
```
cache:info:a7b3f2c8d1e9...
```

## Performance Benefits

- **Reduced Latency**: Cached responses are returned in microseconds instead of milliseconds
- **Lower API Usage**: Fewer requests to upstream Invidious API
- **Better Scalability**: Can handle more concurrent users
- **Cost Savings**: Reduced bandwidth and API quota usage

## Current Implementation

### Endpoints Using Caching

Both major API endpoints now use caching with 1-hour expiration:

1. **Frontpage/Trending** (`/feeds/api/standardfeeds/*`)
   - Cache Duration: 1 hour
   - Caches trending video lists by region and category

2. **Search Videos** (`/feeds/api/videos`)
   - Cache Duration: 1 hour
   - Caches search results with all query parameters

## Redis Connection

The application connects to Redis during startup:

- **Success**: Logs "Redis connected: {ip}:{port}"
- **Failure**: Logs error but continues without caching
- **Disabled**: Skips Redis entirely when `USE_REDIS=false`

## Troubleshooting

### Redis Connection Issues

If you see "Failed to connect to Redis" errors:

1. Check Redis is running: `redis-cli ping` (should return "PONG")
2. Verify IP and port in configuration
3. Check firewall rules allow connection to Redis
4. Ensure Redis accepts connections from your app's IP

### Cache Not Working

If caching seems ineffective:

1. Verify `USE_REDIS` is set to `true`
2. Check Redis has sufficient memory
3. Look for "Cache hit" messages in logs
4. Monitor Redis using: `redis-cli MONITOR`

### Clear Specific Keys

To manually clear specific cache entries:

```bash
# Connect to Redis CLI
redis-cli

# Find keys
KEYS cache:info:*

# Delete specific key
DEL cache:info:a7b3f2c8d1e9...

# Or delete all cache keys
FLUSHDB
```

## Future Enhancements

Potential improvements for the caching system:

1. **Configurable Expiration**: Per-endpoint cache duration settings
2. **Cache Warming**: Pre-populate cache with popular requests
3. **Cache Statistics**: Track hit/miss rates
4. **Conditional Caching**: Cache only successful responses
5. **Compression**: Compress cached data to save Redis memory
6. **Tag-based Invalidation**: Invalidate related cache entries

## Dependencies

- `StackExchange.Redis` (v2.9.32): Redis client library
- Redis Server: Any compatible Redis version (tested with 6.x and 7.x)

## Environment Notes

The Python implementation used:
```python
backend = RedisCache(host=socket.gethostbyname(OSEnv["REDIS_HOST"]), port=OSEnv["REDIS_PORT"])
session = CachedSession('cache/info', expire_after=timedelta(hours=1), backend=config.backend)
```

The C# implementation provides equivalent functionality with:
- Singleton pattern instead of session instances
- SHA256-based cache keys
- Configurable expiration per request
- Automatic User-Agent header setting
