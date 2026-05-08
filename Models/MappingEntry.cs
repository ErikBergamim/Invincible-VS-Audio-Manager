using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Invincible_VS_Audio_Manager.Models
{
    /// <summary>
    /// Bindable view-model representing a single ubulk slot inside the .ucas container.
    /// </summary>
    public sealed class MappingEntry : INotifyPropertyChanged
    {
        public string File { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public long Size { get; set; }
        public long OffsetStart { get; set; }
        public long OffsetEnd { get; set; }
        public string OffsetStartHex { get; set; } = string.Empty;
        public string OffsetEndHex { get; set; } = string.Empty;

        public string RelativePath
        {
            get
            {
                var path = File ?? string.Empty;
                var idx = -1;
                for (var i = path.Length - 1; i >= 0; i--)
                {
                    if (path[i] == '\\' || path[i] == '/')
                    {
                        idx = i;
                        break;
                    }
                }
                return idx >= 0 ? path.Substring(0, idx) : string.Empty;
            }
        }

        private string? _replacementPath;
        public string? ReplacementPath
        {
            get => _replacementPath;
            set
            {
                if (_replacementPath == value) return;
                _replacementPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Replaced));
                OnPropertyChanged(nameof(ReplacementName));
            }
        }

        public bool Replaced => !string.IsNullOrEmpty(_replacementPath);

        public string ReplacementName
        {
            get
            {
                if (string.IsNullOrEmpty(_replacementPath)) return string.Empty;
                var path = _replacementPath;
                for (var i = path.Length - 1; i >= 0; i--)
                {
                    if (path[i] == '\\' || path[i] == '/')
                        return path.Substring(i + 1);
                }
                return path;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
