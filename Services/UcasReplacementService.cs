using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Invincible_VS_Audio_Manager.Models;

namespace Invincible_VS_Audio_Manager.Services
{
    public sealed class ReplacementResult
    {
        public int Total { get; init; }
        public int Succeeded { get; init; }
        public int Failed { get; init; }
        public List<string> Errors { get; init; } = new();
    }

    public sealed class ReplacementProgress
    {
        public int Done { get; init; }
        public int Total { get; init; }
        public string Current { get; init; } = string.Empty;
    }

    /// <summary>
    /// Applies replacements to a .ucas container in-place. Each replacement reads bytes from a
    /// source file (typically .wem) and writes them at the slot's offset, truncating to the slot
    /// size if the source is larger or padding with 0x00 if the source is smaller. This preserves
    /// the original byte layout of the container so surrounding data remains valid.
    /// </summary>
    public static class UcasReplacementService
    {
        public static async Task<ReplacementResult> ApplyReplacementsAsync(
            string ucasPath,
            IReadOnlyList<MappingEntry> entries,
            IProgress<ReplacementProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ucasPath))
                throw new ArgumentException("ucas path is required", nameof(ucasPath));
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var errors = new List<string>();
            var succeeded = 0;
            var failed = 0;
            var ucasLength = new FileInfo(ucasPath).Length;

            await using var ucas = new FileStream(
                ucasPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1 << 16,
                useAsync: true);

            for (var i = 0; i < entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = entries[i];
                progress?.Report(new ReplacementProgress
                {
                    Done = i,
                    Total = entries.Count,
                    Current = entry.Filename
                });

                try
                {
                    if (string.IsNullOrEmpty(entry.ReplacementPath) || !File.Exists(entry.ReplacementPath))
                    {
                        failed++;
                        errors.Add($"{entry.Filename}: arquivo de substituicao nao encontrado.");
                        continue;
                    }

                    if (entry.Size <= 0 || entry.OffsetStart < 0)
                    {
                        failed++;
                        errors.Add($"{entry.Filename}: offset/size invalido (size={entry.Size}, offset={entry.OffsetStart}).");
                        continue;
                    }

                    if (entry.OffsetStart + entry.Size > ucasLength)
                    {
                        failed++;
                        errors.Add(
                            $"{entry.Filename}: slot ultrapassa o tamanho do arquivo .ucas " +
                            $"(offset={entry.OffsetStart}, size={entry.Size}, ucas={ucasLength}).");
                        continue;
                    }

                    var payload = await BuildPayloadAsync(entry.ReplacementPath, entry.Size, cancellationToken)
                        .ConfigureAwait(false);

                    ucas.Seek(entry.OffsetStart, SeekOrigin.Begin);
                    await ucas.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{entry.Filename}: {ex.Message}");
                }
            }

            await ucas.FlushAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report(new ReplacementProgress
            {
                Done = entries.Count,
                Total = entries.Count,
                Current = string.Empty
            });

            return new ReplacementResult
            {
                Total = entries.Count,
                Succeeded = succeeded,
                Failed = failed,
                Errors = errors
            };
        }

        /// <summary>
        /// Reads <paramref name="sourcePath"/> into a buffer of size <paramref name="targetSize"/>.
        /// If the source has more bytes than the slot, only the first <paramref name="targetSize"/>
        /// are kept (truncation). If it has fewer, the remainder of the buffer stays zeroed (0x00 padding).
        /// </summary>
        private static async Task<byte[]> BuildPayloadAsync(string sourcePath, long targetSize, CancellationToken cancellationToken)
        {
            if (targetSize <= 0)
                throw new InvalidOperationException("target size must be positive");
            if (targetSize > int.MaxValue)
                throw new InvalidOperationException($"slot size too large to fit in memory: {targetSize}");

            var size = (int)targetSize;
            // CLR zero-initialises the buffer - any unread tail stays as 0x00 (the requested padding).
            var buffer = new byte[size];

            await using var src = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 16,
                useAsync: true);

            var totalRead = 0;
            while (totalRead < size)
            {
                var read = await src.ReadAsync(buffer, totalRead, size - totalRead, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;
                totalRead += read;
            }

            return buffer;
        }
    }
}
