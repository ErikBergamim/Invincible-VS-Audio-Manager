using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Invincible_VS_Audio_Manager.Models
{
    /// <summary>
    /// Top-level structure of the mapping JSON produced by the matcher tool.
    /// Mirrors the layout described by the user (ucas_file, ucas_size, mappings, ...).
    /// </summary>
    public sealed class MappingFile
    {
        [JsonPropertyName("ucas_file")]
        public string? UcasFile { get; set; }

        [JsonPropertyName("ucas_size")]
        public long UcasSize { get; set; }

        [JsonPropertyName("vo_folder")]
        public string? VoFolder { get; set; }

        [JsonPropertyName("total_ubulk_files")]
        public int TotalUbulkFiles { get; set; }

        [JsonPropertyName("matched")]
        public int Matched { get; set; }

        [JsonPropertyName("not_matched")]
        public int NotMatched { get; set; }

        [JsonPropertyName("search_engine")]
        public string? SearchEngine { get; set; }

        [JsonPropertyName("elapsed_seconds")]
        public double ElapsedSeconds { get; set; }

        [JsonPropertyName("mappings")]
        public List<MappingEntryDto> Mappings { get; set; } = new();
    }

    /// <summary>
    /// Raw entry deserialised from JSON. Converted to <see cref="MappingEntry"/> for binding.
    /// </summary>
    public sealed class MappingEntryDto
    {
        [JsonPropertyName("file")]
        public string? File { get; set; }

        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("offset_start")]
        public long OffsetStart { get; set; }

        [JsonPropertyName("offset_end")]
        public long OffsetEnd { get; set; }

        [JsonPropertyName("offset_start_hex")]
        public string? OffsetStartHex { get; set; }

        [JsonPropertyName("offset_end_hex")]
        public string? OffsetEndHex { get; set; }
    }
}
