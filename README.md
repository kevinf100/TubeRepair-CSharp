# TubeRepair CSharp

TubeRepair CSharp is an ASP.NET Core minimal API server that provides legacy YouTube-style feed endpoints backed by Invidious.

You can find the old Python version here: https://github.com/kevinf100/tuberepair.uptimetrackers.com

It is intended for TubeRepair-compatible clients and includes routes for:
- Frontpage/trending feeds
- Video search
- Video comments
- Related videos
- Channel endpoints
- Playlist endpoints

## Requirements

- .NET SDK 10.0+
- Optional: Redis (for distributed caching)

## Run locally

```bash
dotnet restore
dotnet run
```

Default configuration listens on port `4000` (see `appsettings.json`).

## Configuration

Configuration can be provided through `appsettings.json` or environment variables.

Common settings:

- `URL` (default: `https://inv.uptimetrackers.com/`)
- `FEATURED_VIDEOS` (default: `20`)
- `ACTIVITY_MAX_RESULTS` (default: `20`, maximum Activity entries per response)
- `COMMENTS` (default: `20`)
- `SORT_COMMENTS` (default: `popular`)
- `USE_REDIS` (`true`/`false`)
- `REDIS_HOST`
- `REDIS_PORT` (default: `6379`)
- `USE_PLAYLIST_TRENDING` (`true`/`false`)

Playlist trending IDs can also be customized:
- `TRENDING_PLAYLIST_DEFAULT`
- `TRENDING_PLAYLIST_FILM`
- `TRENDING_PLAYLIST_GAMES`
- `TRENDING_PLAYLIST_MUSIC`
- `TRENDING_PLAYLIST_AUTOS`
- `TRENDING_PLAYLIST_ANIMALS`
- `TRENDING_PLAYLIST_SPORTS`
- `TRENDING_PLAYLIST_COMEDY`
- `TRENDING_PLAYLIST_PEOPLE`
- `TRENDING_PLAYLIST_NEWS`
- `TRENDING_PLAYLIST_ENTERTAINMENT`
- `TRENDING_PLAYLIST_HOWTO`
- `TRENDING_PLAYLIST_TECH`

## Example endpoints

- `GET /feeds/api/standardfeeds/{regioncode}/{popular}`
- `GET /feeds/api/videos?q={query}`
- `GET /api/videos/{videoid}/comments`
- `GET /feeds/api/videos/{videoId}/related`
- `GET /feeds/api/users/{channelId}/playlists`
- `GET /feeds/api/playlists/{playlistId}`
- `GET /feeds/api/events?author={channelId}&inline=true`
- `GET /{res:int}/feeds/api/events?author={channelId}&inline=true`

Some endpoints also support a resolution prefix (for example `/{res:int}/...`).

The channel Activity feed translates recent Invidious uploads into legacy GData
`video_uploaded` events, with inline video metadata and links to this server for
playback. It does not include likes, comments, subscriptions, or community posts.
The `author` parameter is required. `max-results` requests a page size, capped by
`ACTIVITY_MAX_RESULTS`; omitted page sizes use that configured limit. The cap can
be set in `appsettings.json` or through the `ACTIVITY_MAX_RESULTS` environment
variable. Invalid or nonpositive configured caps fall back to 20. `start-index`
is a one-based item index and defaults to 1. Invalid or nonpositive query values
return 400. For example:
`/feeds/api/events?author=UCd_ROizZ_2A63WwnuDmLZGg&max-results=20&start-index=1&inline=true`.
Follow the Atom `rel="next"` link to load more activity; it preserves the channel,
resolution, and effective page size. Links carry an encoded `continuation` token
when needed and a `page-offset` to retain unused uploads within an upstream page.
With a continuation token, `start-index` labels the returned page and `page-offset`
selects its starting item (default 0). Without a token or page offset, `start-index`
skips items from the beginning of the channel feed. OpenSearch total results are
a known lower bound because Invidious does not supply the full channel count.
Inline video entries are always included, including when
`inline` is omitted. Upload data uses the existing 30-minute cache when Redis is
enabled. Channels with no uploads return an empty Atom feed. Invalid input returns
400, upstream channel-not-found responses return 404, other upstream failures or
malformed data return 502, and upstream timeouts return 504.

## Community and support

For help, troubleshooting, and general discussion, join the Discord:

- https://discord.bag-xml.com/

## Public servers

Main stable server:

- https://tuberepair.uptimetrackers.com/

Testing server:

- https://testtuberepair.uptimetrackers.com/

## Docker

A `Dockerfile` is included. Build and run with:

```bash
docker build -t tuberepair-csharp .
docker run --rm -p 4000:4000 tuberepair-csharp
```

If you use Docker, ensure your container runtime and base images match the target .NET version used by this project.

### Contributors
- [kendoodoo](https://github.com/kendoodoo) (God)
- [Nishijima Akito](https://github.com/shijimasoft) (Youtube Classic)
- [SpaceSaver](https://github.com/SpaceSaver) (YouTube Private API, HLS playback filter)
- [Kevinf100](https://github.com/kevinf100) (Ported over to C#, the main hoster, and maintainer for the Website and API.)
- (et al.)
