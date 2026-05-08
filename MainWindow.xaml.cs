using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Invincible_VS_Audio_Manager.Models;
using Invincible_VS_Audio_Manager.Services;
using Microsoft.Win32;

namespace Invincible_VS_Audio_Manager
{
    public partial class MainWindow : Window
    {
        private MappingFile? _mappingFile;
        private string? _jsonPath;
        private string? _ucasPath;
        private List<MappingEntry> _entries = new();
        private ICollectionView? _entriesView;
        private DispatcherTimer? _filterTimer;
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();

            _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _filterTimer.Tick += FilterTimer_Tick;

            UpdateStatus();
            TryAutoLoadJson();
        }

        private void TryAutoLoadJson()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!Directory.Exists(baseDir)) return;
                var json = Directory.EnumerateFiles(baseDir, "*.json", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (json != null)
                {
                    _ = LoadJsonAsync(json);
                }
            }
            catch
            {
                // best-effort auto-load; ignored if it fails.
            }
        }

        private async void LoadJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Arquivos JSON (*.json)|*.json|Todos os arquivos|*.*",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            if (dlg.ShowDialog(this) != true) return;
            await LoadJsonAsync(dlg.FileName);
        }

        private async Task LoadJsonAsync(string path)
        {
            if (_busy) return;
            try
            {
                SetBusy(true);
                StatusText.Text = "Carregando JSON...";
                ProgressBarControl.Visibility = Visibility.Visible;
                ProgressBarControl.IsIndeterminate = true;

                var (mapping, list) = await Task.Run(() => ParseJson(path)).ConfigureAwait(true);

                _mappingFile = mapping;
                _jsonPath = path;

                foreach (var entry in _entries) entry.PropertyChanged -= Entry_PropertyChanged;

                _entries = list;
                foreach (var entry in _entries) entry.PropertyChanged += Entry_PropertyChanged;

                MappingsGrid.ItemsSource = _entries;
                _entriesView = CollectionViewSource.GetDefaultView(_entries);
                _entriesView.Filter = FilterPredicate;

                _ucasPath = TryFindUcas(path, mapping.UcasFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Erro ao carregar JSON:\n{ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressBarControl.IsIndeterminate = false;
                ProgressBarControl.Visibility = Visibility.Collapsed;
                SetBusy(false);
                UpdateStatus();
            }
        }

        private static (MappingFile mapping, List<MappingEntry> entries) ParseJson(string path)
        {
            using var fs = File.OpenRead(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            var mapping = JsonSerializer.Deserialize<MappingFile>(fs, options)
                ?? throw new InvalidOperationException("JSON invalido ou vazio.");
            var list = new List<MappingEntry>(mapping.Mappings.Count);
            foreach (var dto in mapping.Mappings)
            {
                list.Add(new MappingEntry
                {
                    File = dto.File ?? string.Empty,
                    Filename = dto.Filename ?? string.Empty,
                    Size = dto.Size,
                    OffsetStart = dto.OffsetStart,
                    OffsetEnd = dto.OffsetEnd,
                    OffsetStartHex = dto.OffsetStartHex ?? FormatHex(dto.OffsetStart),
                    OffsetEndHex = dto.OffsetEndHex ?? FormatHex(dto.OffsetEnd)
                });
            }
            return (mapping, list);
        }

        private static string FormatHex(long value) => "0x" + value.ToString("x");

        private string? TryFindUcas(string jsonPath, string? ucasFile)
        {
            if (string.IsNullOrEmpty(ucasFile)) return null;
            var jsonDir = Path.GetDirectoryName(jsonPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(jsonDir, ucasFile),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ucasFile)
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private void LoadUcas_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Arquivos UCAS (*.ucas)|*.ucas|Todos os arquivos|*.*",
                Title = "Selecionar arquivo .ucas"
            };
            if (!string.IsNullOrEmpty(_mappingFile?.UcasFile)) dlg.FileName = _mappingFile.UcasFile;
            if (dlg.ShowDialog(this) != true) return;

            if (_mappingFile is { UcasSize: > 0 })
            {
                var actual = new FileInfo(dlg.FileName).Length;
                if (actual != _mappingFile.UcasSize)
                {
                    var resp = MessageBox.Show(this,
                        $"O tamanho do arquivo selecionado ({actual:N0}) e diferente do esperado pelo JSON ({_mappingFile.UcasSize:N0}).\n\nDeseja continuar mesmo assim?",
                        "Aviso", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (resp != MessageBoxResult.Yes) return;
                }
            }

            _ucasPath = dlg.FileName;
            UpdateStatus();
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not MappingEntry entry) return;
            var dlg = new OpenFileDialog
            {
                Filter = "Arquivos WEM (*.wem)|*.wem|Todos os arquivos|*.*",
                Title = $"Selecionar substituto para {entry.Filename}"
            };
            if (dlg.ShowDialog(this) != true) return;
            entry.ReplacementPath = dlg.FileName;
            UpdateStatus();
        }

        private void ClearReplacement_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not MappingEntry entry) return;
            entry.ReplacementPath = null;
            UpdateStatus();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var pending = _entries.Count(x => x.Replaced);
            if (pending == 0) return;
            var resp = MessageBox.Show(this,
                $"Limpar {pending} substituicao(oes) marcada(s)?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resp != MessageBoxResult.Yes) return;
            foreach (var entry in _entries) entry.ReplacementPath = null;
            UpdateStatus();
        }

        private async void ApplyChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (string.IsNullOrEmpty(_ucasPath))
            {
                MessageBox.Show(this, "Selecione o arquivo .ucas primeiro.", "Atencao",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!File.Exists(_ucasPath))
            {
                MessageBox.Show(this, $"Arquivo .ucas nao encontrado:\n{_ucasPath}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var pending = _entries.Where(x => x.Replaced).ToList();
            if (pending.Count == 0)
            {
                MessageBox.Show(this, "Nenhuma substituicao marcada.", "Atencao",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(this,
                $"Aplicar {pending.Count} substituicao(oes) no arquivo:\n{_ucasPath}\n\n" +
                "O arquivo .ucas sera modificado in-place. Recomendamos que voce tenha um backup antes de continuar.\n\nDeseja prosseguir?",
                "Confirmar", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                SetBusy(true);
                ProgressBarControl.Visibility = Visibility.Visible;
                ProgressBarControl.IsIndeterminate = false;
                ProgressBarControl.Minimum = 0;
                ProgressBarControl.Maximum = pending.Count;
                ProgressBarControl.Value = 0;

                var prog = new Progress<ReplacementProgress>(p =>
                {
                    ProgressBarControl.Value = p.Done;
                    StatusText.Text = $"Aplicando: {p.Done}/{p.Total} - {p.Current}";
                });

                var result = await UcasReplacementService.ApplyReplacementsAsync(
                    _ucasPath!, pending, prog, CancellationToken.None).ConfigureAwait(true);

                var msg = $"Substituicoes concluidas.\nSucesso: {result.Succeeded}\nFalhas: {result.Failed}";
                if (result.Errors.Count > 0)
                {
                    msg += "\n\nPrimeiros erros:\n" + string.Join("\n", result.Errors.Take(15));
                    if (result.Errors.Count > 15)
                        msg += $"\n... ({result.Errors.Count - 15} omitidos)";
                }
                MessageBox.Show(this, msg, "Resultado",
                    MessageBoxButton.OK,
                    result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Erro ao aplicar substituicoes:\n{ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressBarControl.Visibility = Visibility.Collapsed;
                SetBusy(false);
                UpdateStatus();
            }
        }

        private void Filter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filterTimer?.Stop();
            _filterTimer?.Start();
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            _entriesView?.Refresh();
        }

        private void FilterTimer_Tick(object? sender, EventArgs e)
        {
            _filterTimer?.Stop();
            _entriesView?.Refresh();
        }

        private bool FilterPredicate(object obj)
        {
            if (obj is not MappingEntry entry) return false;
            if (OnlyReplacedCheck.IsChecked == true && !entry.Replaced) return false;
            var text = FilterTextBox.Text;
            if (string.IsNullOrEmpty(text)) return true;
            return entry.Filename.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.File.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.OffsetStartHex.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MappingEntry.Replaced)) return;
            if (OnlyReplacedCheck.IsChecked == true)
            {
                _filterTimer?.Stop();
                _filterTimer?.Start();
            }
            Dispatcher.BeginInvoke(new Action(UpdateStatus), DispatcherPriority.Background);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            LoadJsonButton.IsEnabled = !busy;
            LoadUcasButton.IsEnabled = !busy;
            ApplyButton.IsEnabled = !busy;
            ClearAllButton.IsEnabled = !busy;
            MappingsGrid.IsEnabled = !busy;
        }

        private void UpdateStatus()
        {
            var total = _entries.Count;
            var replaced = _entries.Count(x => x.Replaced);
            var jsonInfo = string.IsNullOrEmpty(_jsonPath) ? "(nenhum)" : Path.GetFileName(_jsonPath);
            var ucasInfo = string.IsNullOrEmpty(_ucasPath) ? "(nenhum)" : Path.GetFileName(_ucasPath);

            StatusText.Text = $"JSON: {jsonInfo}  |  UCAS: {ucasInfo}  |  Total: {total:N0}  |  Substituicoes marcadas: {replaced:N0}";

            if (_mappingFile != null)
            {
                HeaderInfo.Text = $"Mapeados: {_mappingFile.Matched:N0} / {_mappingFile.TotalUbulkFiles:N0}  |  UCAS esperado: {_mappingFile.UcasFile}";
            }
            else
            {
                HeaderInfo.Text = "Carregue o JSON de mapeamento gerado pelo matcher.";
            }
        }
    }
}
