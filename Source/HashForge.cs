using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using System.Windows.Markup;

namespace HashForge
{
    // Simple Settings Manager
    public class AppSettings
    {
        public string DefaultAlgorithm { get; set; }
        
        public AppSettings()
        {
            DefaultAlgorithm = "SHA256";
        }
    }

    public static class SettingsManager
    {
        private static string SettingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HashForge", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = new AppSettings();
                    if (json.Contains("\"DefaultAlgorithm\": \"MD5\"")) settings.DefaultAlgorithm = "MD5";
                    else if (json.Contains("\"DefaultAlgorithm\": \"SHA1\"")) settings.DefaultAlgorithm = "SHA1";
                    else if (json.Contains("\"DefaultAlgorithm\": \"SHA512\"")) settings.DefaultAlgorithm = "SHA512";
                    else settings.DefaultAlgorithm = "SHA256";
                    return settings;
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                string json = string.Format("{{ \"DefaultAlgorithm\": \"{0}\" }}", settings.DefaultAlgorithm);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }

    // Registry Integration Manager
    public static class IntegrationManager
    {
        private const string KeyName = @"Software\Classes\*\shell\HashForge";
        private const string MenuText = "Hash Forge";

        public static bool IsRegistered()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyName))
                {
                    return key != null;
                }
            }
            catch { return false; }
        }

        public static void Register()
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
                using (var key = Registry.CurrentUser.CreateSubKey(KeyName))
                {
                    key.SetValue("", MenuText);
                    key.SetValue("Icon", exePath);
                    
                    using (var commandKey = key.CreateSubKey("command"))
                    {
                        commandKey.SetValue("", string.Format("\"{0}\" \"%1\"", exePath));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante la registrazione: " + ex.Message);
            }
        }

        public static void Unregister()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(KeyName);
            }
            catch (Exception)
            {
                // Ignore if key doesn't exist
            }
        }
    }

    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                var app = new Application();
                app.Run(new MainWindow(args));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore critico: " + ex.Message);
            }
        }
    }

    public class MainWindow : Window
    {
        // UI Controls
        private ListBox FileList;
        private ComboBox AlgoCombo;
        private Button ComputeBtn;
        private ProgressBar Progress;
        private TextBlock StatFiles, StatData, StatSpeed, StatETA;
        private DataGrid ResultsGrid;
        private StackPanel StatsPanel;
        private TextBlock StatusBarText;
        private TextBox ExpectedHashBox;
        
        // Window Controls
        private Button MinimizeBtn, MaximizeBtn, CloseBtn;
        private Button AddFileBtn, AddFolderBtn, ClearBtn, CopyBtn, ExportBtn, StopBtn;
        
        // Settings Controls
        private Button SettingsBtn, CloseSettingsBtn, ToggleContextBtn;
        private Grid SettingsOverlay;
        private ComboBox SettingsAlgoCombo;
        private TextBlock ContextStatusText;

        private ObservableCollection<HashResult> results;
        private bool isComputing = false;
        private Dictionary<string, string> filePathMap = new Dictionary<string, string>(); // Maps display name to full path
        private CancellationTokenSource _cancellationTokenSource;
        private AppSettings _currentSettings;

        public MainWindow(string[] args)
        {
            try
            {
                // Load Settings
                _currentSettings = SettingsManager.Load();

                // Load XAML from Embedded Resource
                Window loadedWindow = null;
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "HashForge.MainWindow.xaml";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        MessageBox.Show("Risorsa XAML non trovata: " + resourceName);
                        Close();
                        return;
                    }
                    loadedWindow = (Window)XamlReader.Load(stream);
                }

                // Copy Window properties
                this.Title = loadedWindow.Title;
                this.Height = loadedWindow.Height;
                this.Width = loadedWindow.Width;
                this.MinWidth = loadedWindow.MinWidth;
                this.MinHeight = loadedWindow.MinHeight;
                this.WindowStyle = loadedWindow.WindowStyle;
                this.AllowsTransparency = loadedWindow.AllowsTransparency;
                this.Background = loadedWindow.Background;
                this.ResizeMode = loadedWindow.ResizeMode;
                this.WindowStartupLocation = loadedWindow.WindowStartupLocation;

                // Copy Resources (CRITICAL for Styles)
                foreach (System.Collections.DictionaryEntry res in loadedWindow.Resources)
                {
                    this.Resources.Add(res.Key, res.Value);
                }

                // Set Content
                this.Content = loadedWindow.Content;

                // Apply WindowChrome
                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(loadedWindow);
                if (chrome != null)
                {
                    System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
                }

                // Find Controls
                FileList = (ListBox)loadedWindow.FindName("FileList");
                AlgoCombo = (ComboBox)loadedWindow.FindName("AlgoCombo");
                ExpectedHashBox = (TextBox)loadedWindow.FindName("ExpectedHashBox");
                if (ExpectedHashBox != null)
                {
                    ExpectedHashBox.TextChanged += (s, e) =>
                    {
                        if (ResultsGrid != null && ResultsGrid.Columns.Count > 4)
                        {
                            var statusCol = ResultsGrid.Columns[4]; // Status is the 5th column (index 4)
                            statusCol.Visibility = string.IsNullOrWhiteSpace(ExpectedHashBox.Text) ? Visibility.Collapsed : Visibility.Visible;
                        }
                    };
                }
                ComputeBtn = (Button)loadedWindow.FindName("ComputeBtn");
                Progress = (ProgressBar)loadedWindow.FindName("Progress");
                // Find new stat controls
                StatFiles = (TextBlock)loadedWindow.FindName("StatFiles");
                StatData = (TextBlock)loadedWindow.FindName("StatData");
                StatSpeed = (TextBlock)loadedWindow.FindName("StatSpeed");
                StatETA = (TextBlock)loadedWindow.FindName("StatETA");
                ResultsGrid = (DataGrid)loadedWindow.FindName("ResultsGrid");
                
                // Find UI panels
                StatsPanel = (StackPanel)loadedWindow.FindName("StatsPanel");
                StatusBarText = (TextBlock)loadedWindow.FindName("StatusBarText");

                MinimizeBtn = (Button)loadedWindow.FindName("MinimizeBtn");
                MaximizeBtn = (Button)loadedWindow.FindName("MaximizeBtn");
                CloseBtn = (Button)loadedWindow.FindName("CloseBtn");
                AddFileBtn = (Button)loadedWindow.FindName("AddFileBtn");
                AddFolderBtn = (Button)loadedWindow.FindName("AddFolderBtn");
                ClearBtn = (Button)loadedWindow.FindName("ClearBtn");
                CopyBtn = (Button)loadedWindow.FindName("CopyBtn");
                ExportBtn = (Button)loadedWindow.FindName("ExportBtn");
                StopBtn = (Button)loadedWindow.FindName("StopBtn");
                
                // Settings Controls
                SettingsBtn = (Button)loadedWindow.FindName("SettingsBtn");
                SettingsOverlay = (Grid)loadedWindow.FindName("SettingsOverlay");
                CloseSettingsBtn = (Button)loadedWindow.FindName("CloseSettingsBtn");
                ToggleContextBtn = (Button)loadedWindow.FindName("ToggleContextBtn");
                SettingsAlgoCombo = (ComboBox)loadedWindow.FindName("SettingsAlgoCombo");
                ContextStatusText = (TextBlock)loadedWindow.FindName("ContextStatusText");

                // Verify critical controls were found
                if (StatFiles == null || StatData == null || StatSpeed == null || StatETA == null)
                {
                    MessageBox.Show("Errore: Impossibile trovare i controlli delle statistiche nello XAML.");
                }

                // Initialize Data
                results = new ObservableCollection<HashResult>();
                ResultsGrid.ItemsSource = results;

                // Events
                ComputeBtn.Click += Compute_Click;
                AddFileBtn.Click += AddFile_Click;
                AddFolderBtn.Click += AddFolder_Click;
                ClearBtn.Click += Clear_Click;
                CopyBtn.Click += Copy_Click;
                ExportBtn.Click += Export_Click;
                StopBtn.Click += Stop_Click;
                
                // Settings Events
                if (SettingsBtn != null) SettingsBtn.Click += (s, e) => OpenSettings();
                if (CloseSettingsBtn != null) CloseSettingsBtn.Click += (s, e) => SaveAndCloseSettings();
                if (ToggleContextBtn != null) ToggleContextBtn.Click += ToggleContext_Click;

                // Window Events
                MinimizeBtn.Click += (s, e) => this.WindowState = WindowState.Minimized;
                MaximizeBtn.Click += (s, e) => this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                CloseBtn.Click += Close_Click;
                
                // Drag Move
                var titleBar = (Grid)((Grid)((Border)this.Content).Child).Children[0];
                titleBar.MouseLeftButtonDown += (s, e) => this.DragMove();

                // Drag & Drop
                FileList.Drop += FileList_Drop;
                
                // Apply Settings
                ApplySettings();
                
                // Context Menu Wiring
                var ctxMenu = this.Resources["RowContextMenu"] as ContextMenu;
                if (ctxMenu != null)
                {
                    // Item 0: Copy Hash
                    var itemHash = ctxMenu.Items[0] as MenuItem;
                    if (itemHash != null) itemHash.Click += CopyHash_Click;

                    // Item 1: Copy Path
                    var itemPath = ctxMenu.Items[1] as MenuItem;
                    if (itemPath != null) itemPath.Click += CopyPath_Click;

                    // Item 3: Open Folder (2 is separator)
                    var itemFolder = ctxMenu.Items[3] as MenuItem;
                    if (itemFolder != null) itemFolder.Click += OpenFolder_Click;
                }
                
                // Process Args
                if (args != null && args.Length > 0)
                {
                    foreach (var arg in args)
                    {
                        if (File.Exists(arg)) AddFile(arg);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore avvio: " + ex.Message);
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
            }
            Application.Current.Shutdown();
        }

        private void OpenSettings()
        {
            if (SettingsOverlay != null)
            {
                SettingsOverlay.Visibility = Visibility.Visible;
                
                // Sync UI with current settings
                foreach (ComboBoxItem item in SettingsAlgoCombo.Items)
                {
                    if (item.Content.ToString() == _currentSettings.DefaultAlgorithm)
                    {
                        SettingsAlgoCombo.SelectedItem = item;
                        break;
                    }
                }
                
                UpdateContextStatus();
            }
        }

        private void SaveAndCloseSettings()
        {
            if (SettingsOverlay != null)
            {
                // Save Settings
                if (SettingsAlgoCombo.SelectedItem != null)
                {
                    _currentSettings.DefaultAlgorithm = ((ComboBoxItem)SettingsAlgoCombo.SelectedItem).Content.ToString();
                    SettingsManager.Save(_currentSettings);
                    ApplySettings();
                }
                
                SettingsOverlay.Visibility = Visibility.Collapsed;
            }
        }
        
        private void ApplySettings()
        {
            // Set Algo Combo
            if (AlgoCombo != null)
            {
                foreach (ComboBoxItem item in AlgoCombo.Items)
                {
                    if (item.Content.ToString() == _currentSettings.DefaultAlgorithm)
                    {
                        AlgoCombo.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void UpdateContextStatus()
        {
            bool isReg = IntegrationManager.IsRegistered();
            if (ToggleContextBtn != null)
            {
                ToggleContextBtn.Content = isReg ? "Rimuovi dal Menu Contestuale" : "Aggiungi al Menu Contestuale";
                ToggleContextBtn.Background = isReg ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#ef4444") : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#334155");
            }
            if (ContextStatusText != null)
            {
                ContextStatusText.Text = isReg ? "Attualmente integrato" : "Non integrato";
                ContextStatusText.Foreground = isReg ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#4ade80") : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#94a3b8");
            }
        }

        private void ToggleContext_Click(object sender, RoutedEventArgs e)
        {
            if (IntegrationManager.IsRegistered())
            {
                IntegrationManager.Unregister();
            }
            else
            {
                IntegrationManager.Register();
            }
            UpdateContextStatus();
        }

        private void AddFile(string file)
        {
            string displayName = System.IO.Path.GetFileName(file);
            if (!filePathMap.ContainsKey(displayName))
            {
                filePathMap[displayName] = file;
                FileList.Items.Add(displayName);
            }
        }

        private void AddFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Multiselect = true, Title = "Seleziona file" };
            if (dlg.ShowDialog() == true)
            {
                foreach (var file in dlg.FileNames)
                {
                    AddFile(file);
                }
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "Seleziona una cartella";
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string displayName = "\ud83d\udcc1 " + System.IO.Path.GetFileName(dlg.SelectedPath.TrimEnd('\\'));
                if (!filePathMap.ContainsKey(displayName))
                {
                    filePathMap[displayName] = dlg.SelectedPath;
                    FileList.Items.Add(displayName);
                }
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            FileList.Items.Clear();
            filePathMap.Clear();
            results.Clear();
            Progress.Value = 0;
            StatFiles.Text = "-";
            StatData.Text = "-";
            StatSpeed.Text = "-";
            StatETA.Text = "-";
            if (StatsPanel != null) StatsPanel.Visibility = Visibility.Collapsed;
            if (StatusBarText != null) StatusBarText.Text = "Pronto";
        }

        private void FileList_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (File.Exists(file))
                    {
                        AddFile(file);
                    }
                    else if (Directory.Exists(file))
                    {
                        string displayName = "\ud83d\udcc1 " + System.IO.Path.GetFileName(file.TrimEnd('\\'));
                        if (!filePathMap.ContainsKey(displayName))
                        {
                            filePathMap[displayName] = file;
                            FileList.Items.Add(displayName);
                        }
                    }
                }
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                if (StatusBarText != null) StatusBarText.Text = "Arresto in corso...";
                if (StopBtn != null) StopBtn.IsEnabled = false;
            }
        }

        private async void Compute_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.Items.Count == 0)
            {
                MessageBox.Show("Aggiungi almeno un file o una cartella!", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (isComputing) return;

            isComputing = true;
            ComputeBtn.Visibility = Visibility.Collapsed;
            if (StopBtn != null)
            {
                StopBtn.Visibility = Visibility.Visible;
                StopBtn.IsEnabled = true;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            results.Clear();
            Progress.Value = 0;
            StatFiles.Text = "Enumerazione...";
            StatData.Text = "-";
            StatSpeed.Text = "-";
            StatETA.Text = "-";
            if (StatsPanel != null) StatsPanel.Visibility = Visibility.Visible;
            if (StatusBarText != null) StatusBarText.Text = "Enumerazione file in corso...";

            var algoItem = AlgoCombo.SelectedItem as ComboBoxItem;
            var algoName = algoItem != null ? algoItem.Content.ToString() : "SHA256";
            var files = new List<string>();

            try
            {
                // Enumerate files
                await Task.Run(() =>
                {
                    foreach (string displayName in Dispatcher.Invoke(() => FileList.Items.Cast<string>().ToList()))
                    {
                        if (token.IsCancellationRequested) return;

                        // Get full path from map
                        string fullPath = Dispatcher.Invoke(() => filePathMap.ContainsKey(displayName) ? filePathMap[displayName] : displayName);
                        
                        if (File.Exists(fullPath))
                        {
                            files.Add(fullPath);
                        }
                        else if (Directory.Exists(fullPath))
                        {
                            try
                            {
                                files.AddRange(Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories));
                            }
                            catch { }
                        }
                    }
                }, token);

                if (token.IsCancellationRequested) throw new OperationCanceledException();

                if (files.Count == 0)
                {
                    MessageBox.Show("Nessun file trovato!", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Calculate total size
                long totalBytes = 0;
                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException();
                    try { totalBytes += new FileInfo(file).Length; }
                    catch { }
                }

                var startTime = DateTime.Now;

                // Create progress reporter - automatically marshals to UI thread
                var progress = new Progress<ProgressReport>(report =>
                {
                    // Add result if available
                    if (report.Result != null)
                        results.Add(report.Result);

                    // Update progress bar
                    Progress.Value = report.ProgressPercentage;
                    
                    // Update stats
                    if (report.FilesText != null) StatFiles.Text = report.FilesText;
                    if (report.DataText != null) StatData.Text = report.DataText;
                    if (report.SpeedText != null) StatSpeed.Text = report.SpeedText;
                    if (report.ETAText != null) StatETA.Text = report.ETAText;
                    
                    // Update status bar
                    if (report.CurrentFileName != null && StatusBarText != null)
                    {
                        StatusBarText.Text = string.Format("Calcolando: {0} - ETA: {1}", report.CurrentFileName, report.ETAText ?? "Calcolo...");
                    }
                });

                // Run hash computation on background thread
                await Task.Run(() =>
                {
                    int currentFile = 0;
                    long processedBytes = 0;

                    using (var hasher = CreateHasher(algoName))
                    {
                        foreach (var file in files)
                        {
                            if (token.IsCancellationRequested) break;

                            try
                            {
                                long fileSize = new FileInfo(file).Length;
                                string hash = "";

                                using (var stream = File.OpenRead(file))
                                {
                                    // Buffer size: 4MB chunks for optimal performance/feedback balance
                                    byte[] buffer = new byte[4096 * 1024];
                                    int bytesRead;
                                    long fileProcessed = 0;

                                    // Initialize hasher for incremental processing
                                    hasher.Initialize();

                                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        if (token.IsCancellationRequested) break;

                                        // Process chunk
                                        if (fileProcessed + bytesRead < fileSize)
                                        {
                                            hasher.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                                        }
                                        else
                                        {
                                            // Last block
                                            hasher.TransformFinalBlock(buffer, 0, bytesRead);
                                            hash = BitConverter.ToString(hasher.Hash).Replace("-", "").ToLowerInvariant();
                                        }

                                        fileProcessed += bytesRead;
                                        processedBytes += bytesRead;

                                        // Update stats every chunk (or throttle if needed)
                                        var elapsed = (DateTime.Now - startTime).TotalSeconds;
                                        var speed = elapsed > 0 ? processedBytes / elapsed : 0;
                                        var remaining = totalBytes - processedBytes;
                                        var eta = speed > 0 ? remaining / speed : 0;

                                        // Format statistics
                                        var speedText = FormatSize((long)speed) + "/s";
                                        var etaText = eta < 60 ? string.Format("{0:0}s", eta) : 
                                                     eta < 3600 ? string.Format("{0:0}m {1:0}s", eta / 60, eta % 60) :
                                                     string.Format("{0:0}h {1:0}m", eta / 3600, (eta % 3600) / 60);

                                        // Report progress
                                        ((IProgress<ProgressReport>)progress).Report(new ProgressReport
                                        {
                                            Result = null,
                                            ProgressPercentage = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                                            FilesText = string.Format("{0}/{1}", currentFile + 1, files.Count),
                                            DataText = string.Format("{0}/{1}", FormatSize(processedBytes), FormatSize(totalBytes)),
                                            SpeedText = speedText,
                                            ETAText = etaText,
                                            CurrentFileName = System.IO.Path.GetFileName(file)
                                        });
                                    }
                                }

                                if (token.IsCancellationRequested) break;

                                currentFile++;
                                
                                // Check for match
                                bool? matchStatus = null;
                                string expectedHash = "";
                                Dispatcher.Invoke(() => expectedHash = ExpectedHashBox.Text.Trim());
                                
                                if (!string.IsNullOrEmpty(expectedHash))
                                {
                                    matchStatus = string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase);
                                }

                                // Final report for this file with result
                                ((IProgress<ProgressReport>)progress).Report(new ProgressReport
                                {
                                    Result = new HashResult
                                    {
                                        Name = System.IO.Path.GetFileName(file),
                                        Path = file,
                                        Algorithm = algoName,
                                        Hash = hash,
                                        Size = FormatSize(fileSize),
                                        MatchStatus = matchStatus
                                    },
                                    ProgressPercentage = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                                    StatusText = null, // Not used anymore
                                    FilesText = string.Format("{0}/{1}", currentFile, files.Count),
                                    DataText = string.Format("{0}/{1}", FormatSize(processedBytes), FormatSize(totalBytes)),
                                    SpeedText = "-",
                                    ETAText = "Completato"
                                });
                            }
                            catch (Exception ex)
                            {
                                currentFile++;
                                
                                // Report error
                                ((IProgress<ProgressReport>)progress).Report(new ProgressReport
                                {
                                    Result = new HashResult
                                    {
                                        Name = System.IO.Path.GetFileName(file),
                                        Path = file,
                                        Algorithm = algoName,
                                        Hash = "ERRORE: " + ex.Message,
                                        Size = "-"
                                    },
                                    ProgressPercentage = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : (double)currentFile / files.Count * 100,
                                    StatusText = null,
                                    FilesText = string.Format("{0}/{1}", currentFile, files.Count),
                                    DataText = "Errore",
                                    SpeedText = "-",
                                    ETAText = "-"
                                });
                            }
                        }
                    }
                }, token);

                if (token.IsCancellationRequested) throw new OperationCanceledException();
                
                var totalTime = (DateTime.Now - startTime).TotalSeconds;
                var avgSpeed = totalTime > 0 ? totalBytes / totalTime : 0;

                StatFiles.Text = string.Format("{0} file", files.Count);
                StatData.Text = FormatSize(totalBytes);
                StatSpeed.Text = string.Format("{0}/s (Media)", FormatSize((long)avgSpeed));
                StatETA.Text = string.Format("Finito in {0:0.0}s", totalTime);
                
                // Hide stats and update status bar
                if (StatsPanel != null) StatsPanel.Visibility = Visibility.Collapsed;
                if (StatusBarText != null) 
                {
                    StatusBarText.Text = string.Format("Completato! {0} file processati in {1:0.0}s", files.Count, totalTime);
                }
            }
            catch (OperationCanceledException)
            {
                if (StatusBarText != null) StatusBarText.Text = "Operazione annullata dall'utente.";
                if (StatsPanel != null) StatsPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il calcolo: " + ex.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                if (StatsPanel != null) StatsPanel.Visibility = Visibility.Collapsed;
                if (StatusBarText != null) StatusBarText.Text = "Errore";
            }
            finally
            {
                ComputeBtn.Visibility = Visibility.Visible;
                if (StopBtn != null) StopBtn.Visibility = Visibility.Collapsed;
                ComputeBtn.IsEnabled = true;
                isComputing = false;
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
            }
        }

        private HashAlgorithm CreateHasher(string name)
        {
            switch (name)
            {
                case "MD5": return MD5.Create();
                case "SHA1": return SHA1.Create();
                case "SHA256": return SHA256.Create();
                case "SHA512": return SHA512.Create();
                default: return SHA256.Create();
            }
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return string.Format("{0:0.##} {1}", len, sizes[order]);
        }

        // Context Menu Handlers
        private void CopyHash_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is HashResult result)
            {
                Clipboard.SetText(result.Hash);
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is HashResult result)
            {
                Clipboard.SetText(result.Path);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is HashResult result)
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + result.Path + "\"");
                }
                catch { }
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var item = ResultsGrid.SelectedItem as HashResult;
            if (item != null && !item.Hash.StartsWith("ERRORE"))
            {
                Clipboard.SetText(item.Hash);
                if (StatusBarText != null) StatusBarText.Text = "Hash copiato negli appunti!";
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (results.Count == 0)
            {
                MessageBox.Show("Nessun risultato da esportare.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog { Filter = "Text File (*.txt)|*.txt", FileName = "hash_report.txt" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("========================================");
                    sb.AppendLine("           HASH FORGE REPORT            ");
                    sb.AppendLine("========================================");
                    sb.AppendLine("Data: " + DateTime.Now.ToString());
                    sb.AppendLine("Totale File: " + results.Count);
                    sb.AppendLine("========================================");
                    sb.AppendLine("");

                    foreach (var item in results)
                    {
                        sb.AppendLine("File:       " + item.Name);
                        sb.AppendLine("Percorso:   " + item.Path);
                        sb.AppendLine("Algoritmo:  " + item.Algorithm);
                        sb.AppendLine("Hash:       " + item.Hash);
                        sb.AppendLine("Dimensione: " + item.Size);
                        sb.AppendLine("----------------------------------------");
                    }
                    
                    sb.AppendLine("");
                    sb.AppendLine("Generato da Hash Forge");
                    
                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("Report esportato con successo!", "Successo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Errore durante l'esportazione: " + ex.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }



        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class HashResult
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Algorithm { get; set; }
        public string Hash { get; set; }
        public string Size { get; set; }
        public bool? MatchStatus { get; set; } // null = no verification, true = match, false = mismatch

        public HashResult()
        {
            Name = "";
            Path = "";
            Algorithm = "";
            Hash = "";
            Size = "";
            MatchStatus = null;
        }
    }

    public class ProgressReport
    {
        public HashResult Result { get; set; }
        public double ProgressPercentage { get; set; }
        public string StatusText { get; set; } // Legacy, kept for compatibility if needed
        
        // New Smart Stats
        public string FilesText { get; set; }
        public string DataText { get; set; }
        public string SpeedText { get; set; }
        public string ETAText { get; set; }
        public string CurrentFileName { get; set; }
    }
    
    // Converter to extract filename from full path
    public class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var path = value as string;
            if (path != null)
            {
                return System.IO.Path.GetFileName(path);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
