[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/kebechet)

# Build.RemoteAssets
[![NuGet Version](https://img.shields.io/nuget/v/Kebechet.Build.RemoteAssets)](https://www.nuget.org/packages/Kebechet.Build.RemoteAssets/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Kebechet.Build.RemoteAssets)](https://www.nuget.org/packages/Kebechet.Build.RemoteAssets/)
[![Build](https://github.com/Kebechet/Build.RemoteAssets/actions/workflows/build.yml/badge.svg)](https://github.com/Kebechet/Build.RemoteAssets/actions/workflows/build.yml)
![Last updated](https://img.shields.io/github/last-commit/Kebechet/Build.RemoteAssets/main?label=last%20updated)
[![Twitter](https://img.shields.io/twitter/url/https/twitter.com/samuel_sidor.svg?style=social&label=Follow%20samuel_sidor)](https://x.com/samuel_sidor)

Downloads files over HTTP during the build and registers them as build items, so large or
frequently replaced binaries stay out of the repository.

## Installation

```bash
dotnet add package Kebechet.Build.RemoteAssets
```

## Usage

```xml
<ItemGroup>
  <RemoteAsset Include="https://cdn.example.com/v1/hero.webp" Path="wwwroot/img" />
  <RemoteAsset Include="https://cdn.example.com/v1/intro.mp4" Path="wwwroot/video" />
</ItemGroup>
```

In a Razor SDK project those resolve at `_content/<YourProject>/img/hero.webp`, exactly as if they
had been committed under `wwwroot/`. In a console or class library project they land next to the
built assembly.

### Item metadata

| Metadata | Required | Meaning |
|---|---|---|
| `Include` | yes | The URL, or the file name when `Url` is also set |
| `Path` | no | Directory to save into, relative to the project. Defaults to `$(RemoteAssetPath)` |
| `Name` | no | File name to save as. Defaults to the last segment of the URL |
| `Url` | no | Explicit URL. **Required when the URL contains `?`** - see below |
| `Sha256` | no | Expected hash. The build fails if the bytes do not match |
| `ItemType` | no | `Content` (default), `MauiAsset`, `EmbeddedResource` or `None` |

### Properties

| Property | Default | Meaning |
|---|---|---|
| `RemoteAssetPath` | *(empty)* | Default `Path` for every item |
| `RemoteAssetItemType` | `Content` | Default `ItemType` for every item |
| `RemoteAssetRetries` | `3` | Download attempts before failing |
| `RemoteAssetRetryDelayMilliseconds` | `2000` | Delay between attempts |
| `RemoteAssetsEnabled` | `true` | Set to `false` to skip fetching entirely |
| `RemoteAssetStampDirectory` | `$(BaseIntermediateOutputPath)RemoteAssets\` | Where the cache manifest lives |

Set `RemoteAssetPath` once when everything shares a folder:

```xml
<PropertyGroup>
  <RemoteAssetPath>wwwroot/img/app-previews</RemoteAssetPath>
</PropertyGroup>

<ItemGroup>
  <RemoteAsset Include="https://cdn.example.com/v1/training-plans.webp" />
  <RemoteAsset Include="https://cdn.example.com/v1/workout-summary.webp" />
</ItemGroup>
```

## There is no version property - the URL is the cache key

A file already on disk for the current URL is never requested again, so incremental and offline
builds do no network work. Change the URL and the cached copy is replaced:

```xml
<!-- v1 -> v2 is all it takes; nothing else to bump, no stamp to reset -->
<RemoteAsset Include="https://cdn.example.com/v2/hero.webp" Path="wwwroot/img" />
```

## Verifying content with Sha256

A URL is mutable. If someone re-uploads over `v1/hero.webp` you would silently ship different
bytes and no commit would record it. `Sha256` turns that into a build failure naming the file:

```xml
<RemoteAsset Include="https://cdn.example.com/v1/hero.webp"
             Path="wwwroot/img"
             Sha256="9f2c4e8a1b7d5f30c6e94a2b8d1f7c3e5a09b4d6f28e1c7a3b5d9f04e6a2c8b1" />
```

Compute one with `Get-FileHash hero.webp -Algorithm SHA256` or `sha256sum hero.webp`.

## URLs with a query string (SAS tokens)

`?` is an MSBuild wildcard. A URL containing one, placed in `Include`, is expanded as a file glob,
matches nothing, and **the item disappears without an error**. Pass such URLs as `Url` metadata
instead, with the file name in `Include`:

```xml
<RemoteAsset Include="hero.webp"
             Url="https://account.blob.core.windows.net/assets/hero.webp?sv=2024-01-01&amp;sig=..."
             Path="wwwroot/img" />
```

The package detects the broken form and fails with a message pointing here rather than letting it
pass silently.

## Retired assets are cleaned up

Every downloaded file is tracked by a stamp under `obj/RemoteAssets/`. Remove an item and the next
build deletes the file it left behind. Only files this package downloaded are ever deleted, so a
committed file sitting in the same folder as fetched ones is never touched.

## Multi-targeted projects

For a project building several target frameworks, the fetch runs once in the outer build before
the inner builds fan out. Without that, every inner build would race the others to download the
same URL into the same destination file - they run in parallel, so an existence check is not
enough on its own.

## Ignore the fetched files in git

The point of the package is that these files are not committed:

```gitignore
wwwroot/img/hero.webp
wwwroot/video/intro.mp4
```

## Building without network access

A build whose files are already cached needs no network. A cold build with no cache fails with a
message naming the URL, rather than producing output with the asset silently missing. Set
`RemoteAssetsEnabled=false` to skip fetching altogether.

## License

[MIT](LICENSE)
