using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode.Bridge;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Utils.Extensions;

namespace YoutubeExplode.Videos.ClosedCaptions;

/// <summary>
/// Operations related to closed captions of YouTube videos.
/// </summary>
public class ClosedCaptionClient
{
    private readonly YoutubeController _controller;

    /// <summary>
    /// Initializes an instance of <see cref="ClosedCaptionClient"/>.
    /// </summary>
    public ClosedCaptionClient(HttpClient httpClient)
    {
        _controller = new YoutubeController(httpClient);
    }

    /// <summary>
    /// Gets the manifest containing information about available closed caption tracks on the specified video.
    /// </summary>
    public async ValueTask<ClosedCaptionManifest> GetManifestAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default)
    {
        var playerResponse = await _controller.GetPlayerResponseAsync(videoId, cancellationToken);

        var trackInfos = playerResponse
            .GetClosedCaptionTracks()
            .Select(t =>
            {
                var url =
                    t.TryGetUrl() ??
                    throw new YoutubeExplodeException("Could not extract track URL.");

                var languageCode =
                    t.TryGetLanguageCode() ??
                    throw new YoutubeExplodeException("Could not extract track language code.");

                var languageName =
                    t.TryGetLanguageName() ??
                    throw new YoutubeExplodeException("Could not extract track language name.");

                var isAutoGenerated = t.IsAutoGenerated();

                return new ClosedCaptionTrackInfo(
                    url,
                    new Language(languageCode, languageName),
                    isAutoGenerated
                );
            })
            .ToArray();

        return new ClosedCaptionManifest(trackInfos);
    }

    /// <summary>
    /// Gets the closed caption track identified by the specified metadata.
    /// </summary>
    public async ValueTask<ClosedCaptionTrack> GetAsync(
        ClosedCaptionTrackInfo trackInfo,
        CancellationToken cancellationToken = default)
    {
        var trackExtractor = await _controller.GetClosedCaptionTrackAsync(trackInfo.Url, cancellationToken);

        var captions = trackExtractor
            .GetClosedCaptions()
            .Select(c =>
            {
                var text = c.TryGetText();
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                var offset =
                    c.TryGetOffset() ??
                    throw new YoutubeExplodeException("Could not extract caption offset.");

                var duration =
                    c.TryGetDuration() ??
                    throw new YoutubeExplodeException("Could not extract caption duration.");

                var parts = c
                    .GetParts()
                    .Select(p =>
                    {
                        var partText = p.TryGetText();
                        if (string.IsNullOrWhiteSpace(partText))
                            return null;

                        var partOffset =
                            p.TryGetOffset() ??
                            throw new YoutubeExplodeException("Could not extract caption part offset.");

                        return new ClosedCaptionPart(partText, partOffset);
                    })
                    .WhereNotNull()
                    .ToArray();

                return new ClosedCaption(text, offset, duration, parts);
            })
            .WhereNotNull()
            .ToArray();

        return new ClosedCaptionTrack(captions);
    }

    /// <summary>
    /// Writes the closed caption track identified by the specified metadata to the specified writer.
    /// </summary>
    /// <remarks>
    /// Closed captions are written in the SRT file format.
    /// </remarks>
    public async ValueTask WriteToAsync(
        ClosedCaptionTrackInfo trackInfo,
        TextWriter writer,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var track = await GetAsync(trackInfo, cancellationToken);

        var buffer = new StringBuilder();
        for (var i = 0; i < track.Captions.Count; i++)
        {
            var caption = track.Captions[i];
            buffer.Clear();

            cancellationToken.ThrowIfCancellationRequested();

            // Line number
            buffer.AppendLine((i + 1).ToString());

            // Time start --> time end
            buffer.Append(caption.Offset.ToString(@"hh\:mm\:ss\,fff"));
            buffer.Append(" --> ");
            buffer.Append((caption.Offset + caption.Duration).ToString(@"hh\:mm\:ss\,fff"));
            buffer.AppendLine();

            // Actual text
            buffer.AppendLine(caption.Text);

            await writer.WriteLineAsync(buffer.ToString());
            progress?.Report((i + 1.0) / track.Captions.Count);
        }
    }

    /// <summary>
    /// Downloads the closed caption track identified by the specified metadata to the specified file.
    /// </summary>
    /// <remarks>
    /// Closed captions are written in the SRT file format.
    /// </remarks>
    public async ValueTask DownloadAsync(
        ClosedCaptionTrackInfo trackInfo,
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var writer = File.CreateText(filePath);
        await WriteToAsync(trackInfo, writer, progress, cancellationToken);
    }
}