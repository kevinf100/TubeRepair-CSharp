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

Some endpoints also support a resolution prefix (for example `/{res:int}/...`).

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
