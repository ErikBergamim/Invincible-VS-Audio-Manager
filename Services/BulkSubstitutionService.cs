using System;
using System.Collections.Generic;
using System.IO;
using Invincible_VS_Audio_Manager.Models;

namespace Invincible_VS_Audio_Manager.Services
{
    public sealed class BulkMatchResult
    {
        public int Matched { get; init; }
        public int NotFound { get; init; }
        public int Scanned { get; init; }
        public int FilesIndexed { get; init; }
        public List<string> NotFoundNames { get; init; } = new();
        public List<string> Duplicates { get; init; } = new();
    }

    /// <summary>
    /// Recursively indexes .wem files inside a folder by their basename (without extension)
    /// and assigns matching files to entries' <see cref="MappingEntry.ReplacementPath"/>.
    /// Match key: <c>Path.GetFileNameWithoutExtension(entry.Filename)</c> (e.g. the
    /// <c>Conquest_Fight_6_Line76</c> portion of <c>Conquest_Fight_6_Line76.ubulk</c>).
    /// </summary>
    public static class BulkSubstitutionService
    {
        public static BulkMatchResult AssignByName(
            IReadOnlyList<MappingEntry> entries,
            string folder,
            bool overwriteExisting)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (string.IsNullOrEmpty(folder)) throw new ArgumentException("folder is required", nameof(folder));
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();
            var indexed = 0;

            foreach (var path in Directory.EnumerateFiles(folder, "*.wem", SearchOption.AllDirectories))
            {
                indexed++;
                var key = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(key)) continue;
                if (index.ContainsKey(key))
                {
                    duplicates.Add(path);
                    // keep first match - duplicates are reported but not used.
                    continue;
                }
                index[key] = path;
            }

            var matched = 0;
            var notFound = 0;
            var scanned = 0;
            var notFoundNames = new List<string>();

            foreach (var entry in entries)
            {
                scanned++;
                if (!overwriteExisting && entry.Replaced) continue;

                var key = Path.GetFileNameWithoutExtension(entry.Filename);
                if (string.IsNullOrEmpty(key))
                {
                    notFound++;
                    notFoundNames.Add(entry.Filename);
                    continue;
                }

                if (index.TryGetValue(key, out var wemPath))
                {
                    entry.ReplacementPath = wemPath;
                    matched++;
                }
                else
                {
                    notFound++;
                    notFoundNames.Add(entry.Filename);
                }
            }

            return new BulkMatchResult
            {
                Matched = matched,
                NotFound = notFound,
                Scanned = scanned,
                FilesIndexed = indexed,
                NotFoundNames = notFoundNames,
                Duplicates = duplicates
            };
        }
    }
}
