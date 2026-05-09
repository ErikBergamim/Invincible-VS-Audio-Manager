using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Invincible_VS_Audio_Manager.Models;

namespace Invincible_VS_Audio_Manager.Services
{
    public sealed class ExtractionResult
    {
        public int Total { get; init; }
        public int Succeeded { get; init; }
        public int Failed { get; init; }
        public List<string> Errors { get; init; } = new();
    }

    public sealed class ExtractionProgress
    {
        public int Done { get; init; }
        public int Total { get; init; }
        public string Current { get; init; } = string.Empty;
    }

    /// <summary>
    /// Reads slots out of a .ucas container. Each extracted slot is written as a standalone
    /// .wem file containing exactly <see cref="MappingEntry.Size"/> bytes copied from the
    /// container starting at <see cref="MappingEntry.OffsetStart"/>.
    /// </summary>
    public static class UcasExtractionService
    {
        public static async Task ExtractAsync(
            string ucasPath,
            MappingEntry entry,
            string outputPath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ucasPath))
                throw new ArgumentException("ucas path is required", nameof(ucasPath));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("output path is required", nameof(outputPath));

            ValidateEntry(entry);

            await using var ucas = new FileStream(
                ucasPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 16,
                useAsync: true);

            var buffer = await ReadSlotAsync(ucas, entry, cancellationToken).ConfigureAwait(false);

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await using var output = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1 << 16,
                useAsync: true);

            await output.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<ExtractionResult> ExtractBatchAsync(
            string ucasPath,
            IReadOnlyList<MappingEntry> entries,
            string outputFolder,
            bool preserveStructure,
            IProgress<ExtractionProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ucasPath))
                throw new ArgumentException("ucas path is required", nameof(ucasPath));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (string.IsNullOrEmpty(outputFolder))
                throw new ArgumentException("output folder is required", nameof(outputFolder));

            Directory.CreateDirectory(outputFolder);

            var errors = new List<string>();
            var succeeded = 0;
            var failed = 0;

            await using var ucas = new FileStream(
                ucasPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1 << 16,
                useAsync: true);

            for (var i = 0; i < entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = entries[i];

                progress?.Report(new ExtractionProgress
                {
                    Done = i,
                    Total = entries.Count,
                    Current = entry.Filename
                });

                try
                {
                    ValidateEntry(entry);
                    var buffer = await ReadSlotAsync(ucas, entry, cancellationToken).ConfigureAwait(false);
                    var targetPath = BuildTargetPath(outputFolder, entry, preserveStructure);

                    var dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    await using var output = new FileStream(
                        targetPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 1 << 16,
                        useAsync: true);

                    await output.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
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

            progress?.Report(new ExtractionProgress
            {
                Done = entries.Count,
                Total = entries.Count,
                Current = string.Empty
            });

            return new ExtractionResult
            {
                Total = entries.Count,
                Succeeded = succeeded,
                Failed = failed,
                Errors = errors
            };
        }

        public static string ChangeExtensionToWem(string filename)
        {
            var noExt = Path.GetFileNameWithoutExtension(filename);
            return noExt + ".wem";
        }

        private static string BuildTargetPath(string outputFolder, MappingEntry entry, bool preserveStructure)
        {
            var wemName = ChangeExtensionToWem(entry.Filename);
            if (!preserveStructure) return Path.Combine(outputFolder, wemName);

            var rel = entry.RelativePath ?? string.Empty;
            // JSON paths use Windows separators; normalise to the host before combining.
            rel = rel.Replace('\\', Path.DirectorySeparatorChar);
            return string.IsNullOrEmpty(rel)
                ? Path.Combine(outputFolder, wemName)
                : Path.Combine(outputFolder, rel, wemName);
        }

        private static void ValidateEntry(MappingEntry entry)
        {
            if (entry.Size <= 0 || entry.OffsetStart < 0)
                throw new InvalidOperationException(
                    $"offset/size invalido (size={entry.Size}, offset={entry.OffsetStart})");
            if (entry.Size > int.MaxValue)
                throw new InvalidOperationException($"slot size too large to fit in memory: {entry.Size}");
        }

        private static async Task<byte[]> ReadSlotAsync(FileStream ucas, MappingEntry entry, CancellationToken cancellationToken)
        {
            var size = (int)entry.Size;
            var buffer = new byte[size];

            ucas.Seek(entry.OffsetStart, SeekOrigin.Begin);
            var total = 0;
            while (total < size)
            {
                var read = await ucas.ReadAsync(buffer, total, size - total, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException(
                        $"fim inesperado do .ucas em offset {entry.OffsetStart + total}");
                total += read;
            }
            return buffer;
        }
    }
}
