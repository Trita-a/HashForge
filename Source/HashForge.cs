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
    // Gestore Impostazioni Semplice
    public class AppSettings
    {
        public string DefaultAlgorithm { get; set; }
        public bool IsDarkTheme { get; set; } // Nuova proprietà per il tema
        
        public AppSettings()
        {
            DefaultAlgorithm = "SHA256";
            IsDarkTheme = true; // Tema scuro di default
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
                    
                    // Carica algoritmo
                    if (json.Contains("\"DefaultAlgorithm\": \"MD5\"")) settings.DefaultAlgorithm = "MD5";
                    else if (json.Contains("\"DefaultAlgorithm\": \"SHA1\"")) settings.DefaultAlgorithm = "SHA1";
                    else if (json.Contains("\"DefaultAlgorithm\": \"SHA512\"")) settings.DefaultAlgorithm = "SHA512";
                    else settings.DefaultAlgorithm = "SHA256";
                    
                    // Carica tema
                    if (json.Contains("\"IsDarkTheme\": false")) settings.IsDarkTheme = false;
                    else settings.IsDarkTheme = true; // Default scuro
                    
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
                
                // Salva anche il tema
                string json = string.Format("{{ \"DefaultAlgorithm\": \"{0}\", \"IsDarkTheme\": {1} }}", 
                    settings.DefaultAlgorithm, 
                    settings.IsDarkTheme.ToString().ToLower());
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
        private System.Windows.Controls.Primitives.ToggleButton ChkMD5, ChkSHA1, ChkSHA256, ChkSHA512;
        private Button ComputeBtn;
        private ProgressBar Progress;
        private TextBlock StatFiles, StatData, StatSpeed, StatETA;
        private DataGrid ResultsGrid;
        private StackPanel StatsPanel;
        private TextBlock StatusBarText;
        private Button MinimizeBtn, MaximizeBtn, CloseBtn;
        private Button AddFileBtn, AddFolderBtn, ClearBtn, CopyBtn, ExportBtn, StopBtn;
        private Button SettingsBtn, CloseSettingsBtn, ToggleContextBtn;
        private Grid SettingsOverlay;
        private ComboBox SettingsAlgoCombo;
        private TextBlock ContextStatusText;
        // Hash List Controls
        private Button ManageHashesBtn;
        // Algorithm Selection Popup
        private Button AlgoSelectorBtn;
        private System.Windows.Controls.Primitives.Popup AlgoPopup;
        private TextBlock AlgoStatusText;
        private Grid HashListOverlay;
        private TextBox HashListInput;
        private Button LoadHashFileBtn, ClearHashListBtn, CancelHashListBtn, SaveHashListBtn;

        // Controllo Tema
        private Button ThemeBtn;
        private System.Windows.Shapes.Path ThemeIcon;
        
        private ObservableCollection<HashResult> results;
        private bool isComputing = false;
        private Dictionary<string, string> filePathMap = new Dictionary<string, string>();
        private CancellationTokenSource _cancellationTokenSource;
        private AppSettings _currentSettings;
        private HashSet<string> _expectedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                // Applica tema SUBITO dopo aver copiato le risorse, PRIMA che i controlli vengano renderizzati
                ApplyTheme(_currentSettings.IsDarkTheme);

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
                ChkMD5 = (System.Windows.Controls.Primitives.ToggleButton)loadedWindow.FindName("ChkMD5");
                ChkSHA1 = (System.Windows.Controls.Primitives.ToggleButton)loadedWindow.FindName("ChkSHA1");
                ChkSHA256 = (System.Windows.Controls.Primitives.ToggleButton)loadedWindow.FindName("ChkSHA256");
                ChkSHA512 = (System.Windows.Controls.Primitives.ToggleButton)loadedWindow.FindName("ChkSHA512");

                // Algorithm Popup Controls
                AlgoSelectorBtn = (Button)loadedWindow.FindName("AlgoSelectorBtn");
                AlgoPopup = (System.Windows.Controls.Primitives.Popup)loadedWindow.FindName("AlgoPopup");
                AlgoStatusText = (TextBlock)loadedWindow.FindName("AlgoStatusText");

                // Setup Algorithm Selector button to open popup
                if (AlgoSelectorBtn != null && AlgoPopup != null)
                {
                    AlgoSelectorBtn.Click += (s, ev) => { AlgoPopup.IsOpen = !AlgoPopup.IsOpen; };
                }

                // Update status text when toggles change
                if (ChkMD5 != null) ChkMD5.Click += (s, ev) => UpdateAlgoStatusText();
                if (ChkSHA1 != null) ChkSHA1.Click += (s, ev) => UpdateAlgoStatusText();
                if (ChkSHA256 != null) ChkSHA256.Click += (s, ev) => UpdateAlgoStatusText();
                if (ChkSHA512 != null) ChkSHA512.Click += (s, ev) => UpdateAlgoStatusText();

                if (ManageHashesBtn != null)
                {
                    // Aggiorna lo stato del pulsante se necessario
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
                
                // Hash List Controls
                ManageHashesBtn = (Button)loadedWindow.FindName("ManageHashesBtn");
                HashListOverlay = (Grid)loadedWindow.FindName("HashListOverlay");
                HashListInput = (TextBox)loadedWindow.FindName("HashListInput");
                LoadHashFileBtn = (Button)loadedWindow.FindName("LoadHashFileBtn");
                ClearHashListBtn = (Button)loadedWindow.FindName("ClearHashListBtn");
                CancelHashListBtn = (Button)loadedWindow.FindName("CancelHashListBtn");
                SaveHashListBtn = (Button)loadedWindow.FindName("SaveHashListBtn");
                
                // GitHub Link
                var gitHubLink = (TextBlock)loadedWindow.FindName("GitHubLink");
                if (gitHubLink != null)
                {
                    gitHubLink.MouseLeftButtonUp += (s, e) => System.Diagnostics.Process.Start("https://github.com/Trita-a/HashForge");
                }

                // Load Logo Image from embedded resource
                var logoImage = (Image)loadedWindow.FindName("LogoImage");
                if (logoImage != null)
                {
                    try
                    {
                        using (var stream = assembly.GetManifestResourceStream("HashForge.icon.png"))
                        {
                            if (stream != null)
                            {
                                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                                bitmap.BeginInit();
                                bitmap.StreamSource = stream;
                                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bitmap.EndInit();
                                bitmap.Freeze();
                                logoImage.Source = bitmap;
                            }
                        }
                    }
                    catch { }
                }

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
                
                // Controlli Tema (prima di ApplySettings per aggiornare l'icona)
                ThemeBtn = (Button)loadedWindow.FindName("ThemeBtn");
                ThemeIcon = (System.Windows.Shapes.Path)loadedWindow.FindName("ThemeIcon");
                if (ThemeBtn != null) ThemeBtn.Click += (s, e) => ToggleTheme();
                
                // Applica Impostazioni (tema incluso)
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
                
                // Eventi Overlay Lista Hash
                ManageHashesBtn.Click += OpenHashList_Click;
                CloseSettingsBtn.Click += (s, e) => SettingsOverlay.Visibility = Visibility.Collapsed;
                CancelHashListBtn.Click += (s, e) => HashListOverlay.Visibility = Visibility.Collapsed;
                SaveHashListBtn.Click += SaveHashList_Click;
                ClearHashListBtn.Click += (s, e) => HashListInput.Text = "";
                LoadHashFileBtn.Click += LoadHashFile_Click;
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
            if (Application.Current != null)
            {
                Application.Current.Shutdown();
            }
            Environment.Exit(0);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
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
            // Set default algorithm checkbox
            if (ChkSHA256 != null && _currentSettings.DefaultAlgorithm == "SHA256")
                ChkSHA256.IsChecked = true;
            else if (ChkMD5 != null && _currentSettings.DefaultAlgorithm == "MD5")
                ChkMD5.IsChecked = true;
            else if (ChkSHA1 != null && _currentSettings.DefaultAlgorithm == "SHA1")
                ChkSHA1.IsChecked = true;
            else if (ChkSHA512 != null && _currentSettings.DefaultAlgorithm == "SHA512")
                ChkSHA512.IsChecked = true;
        }

        private List<string> GetSelectedAlgorithms()
        {
            var algorithms = new List<string>();
            if (ChkMD5 != null && ChkMD5.IsChecked == true) algorithms.Add("MD5");
            if (ChkSHA1 != null && ChkSHA1.IsChecked == true) algorithms.Add("SHA1");
            if (ChkSHA256 != null && ChkSHA256.IsChecked == true) algorithms.Add("SHA256");
            if (ChkSHA512 != null && ChkSHA512.IsChecked == true) algorithms.Add("SHA512");
            return algorithms;
        }

        private void UpdateAlgoStatusText()
        {
            if (AlgoStatusText == null) return;
            var count = GetSelectedAlgorithms().Count;
            AlgoStatusText.Text = count.ToString();
        }


        private void UpdateContextStatus()
        {
            bool isReg = IntegrationManager.IsRegistered();
            if (ToggleContextBtn != null)
            {
                ToggleContextBtn.Content = isReg ? "Rimuovi dal Menu Contestuale" : "Aggiungi al Menu Contestuale";
                ToggleContextBtn.Background = isReg ? 
                    (SolidColorBrush)this.Resources["DangerBrush"] : 
                    (SolidColorBrush)this.Resources["ControlBackgroundBrush"];
                ToggleContextBtn.Foreground = (SolidColorBrush)this.Resources["TextBrush"];
            }
            if (ContextStatusText != null)
            {
                ContextStatusText.Text = isReg ? "Attualmente integrato" : "Non integrato";
                ContextStatusText.Foreground = isReg ? 
                    (SolidColorBrush)this.Resources["AccentBrush"] : 
                    (SolidColorBrush)this.Resources["SecondaryTextBrush"];
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

        // Metodo per cambiare il tema
        private void ToggleTheme()
        {
            _currentSettings.IsDarkTheme = !_currentSettings.IsDarkTheme;
            ApplyTheme(_currentSettings.IsDarkTheme);
            SettingsManager.Save(_currentSettings);
        }

        // Applica il tema (chiaro o scuro)
        private void ApplyTheme(bool isDark)
        {
            if (isDark)
            {
                // Tema Scuro
                this.Resources["BackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#050b16"));
                this.Resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111a2b"));
                this.Resources["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f8fafc"));
                this.Resources["SecondaryTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94a3b8"));
                this.Resources["PrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00c2ff"));
                this.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34e0a1"));
                this.Resources["DangerBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ff7b00"));
                this.Resources["ControlBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b"));
                this.Resources["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33ffffff"));
                this.Resources["HoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#26ffffff"));
                this.Resources["ButtonForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f172a"));
                this.Resources["WindowControlsBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cbd5e1"));
                this.Resources["HeaderTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
                
                // Icona Luna (indica che siamo in tema scuro)
                if (ThemeIcon != null)
                {
                    ThemeIcon.Data = Geometry.Parse("M17.75,4.09L15.22,6.03L16.13,9.09L13.5,7.28L10.87,9.09L11.78,6.03L9.25,4.09L12.44,4L13.5,1L14.56,4L17.75,4.09M21.25,11L19.61,12.25L20.2,14.23L18.5,13.06L16.8,14.23L17.39,12.25L15.75,11L17.81,10.95L18.5,9L19.19,10.95L21.25,11M18.97,15.95C19.8,15.87 20.69,17.05 20.16,17.8C19.84,18.25 19.5,18.67 19.08,19.07C15.17,23 8.84,23 4.94,19.07C1.03,15.17 1.03,8.83 4.94,4.93C5.34,4.53 5.76,4.17 6.21,3.85C6.96,3.32 8.14,4.21 8.06,5.04C7.79,7.9 8.75,10.87 10.95,13.06C13.14,15.26 16.1,16.22 18.97,15.95M17.33,17.97C14.5,17.81 11.7,16.64 9.53,14.5C7.36,12.31 6.2,9.5 6.04,6.68C3.23,9.82 3.34,14.64 6.35,17.66C9.37,20.67 14.19,20.78 17.33,17.97Z");
                }
            }
            else
            {
                // Tema Chiaro
                this.Resources["BackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f8fafc"));
                this.Resources["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
                this.Resources["TextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f172a"));
                this.Resources["SecondaryTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b"));
                this.Resources["PrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284c7"));
                this.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                this.Resources["DangerBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ea580c"));
                this.Resources["ControlBackgroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f1f5f9"));
                this.Resources["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e2e8f0"));
                this.Resources["HoverBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10000000"));
                this.Resources["ButtonForegroundBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
                this.Resources["WindowControlsBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")); // Slate 600 - più scuro per visibilità
                this.Resources["HeaderTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f172a")); // Slate 900 - scuro per leggibilità
                
                // Icona Sole (indica che siamo in tema chiaro - cliccando si va al tema scuro)
                if (ThemeIcon != null)
                {
                    // Icona sole classica con raggi
                    ThemeIcon.Data = Geometry.Parse("M12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,2L14.39,5.42C13.65,5.15 12.84,5 12,5C11.16,5 10.35,5.15 9.61,5.42L12,2M3.34,7L7.5,6.65C6.9,7.16 6.36,7.78 5.94,8.5C5.5,9.24 5.25,10 5.11,10.79L3.34,7M3.36,17L5.12,13.23C5.26,14 5.53,14.78 5.95,15.5C6.37,16.24 6.91,16.86 7.5,17.37L3.36,17M20.65,7L18.88,10.79C18.74,10 18.47,9.23 18.05,8.5C17.63,7.78 17.1,7.15 16.5,6.64L20.65,7M20.64,17L16.5,17.36C17.09,16.85 17.62,16.22 18.04,15.5C18.46,14.77 18.73,14 18.87,13.21L20.64,17M12,22L9.59,18.56C10.33,18.83 11.14,19 12,19C12.82,19 13.63,18.83 14.37,18.56L12,22Z");
                }
            }
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

            var selectedAlgorithms = GetSelectedAlgorithms();
            if (selectedAlgorithms.Count == 0)
            {
                MessageBox.Show("Seleziona almeno un algoritmo di hash!", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
                isComputing = false;
                ComputeBtn.Visibility = Visibility.Visible;
                if (StopBtn != null) StopBtn.Visibility = Visibility.Collapsed;
                if (StatsPanel != null) StatsPanel.Visibility = Visibility.Collapsed;
                return;
            }
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

                    foreach (var file in files)
                    {
                        if (token.IsCancellationRequested) break;

                        try
                        {
                            long fileSize = new FileInfo(file).Length;
                            
                            // Create all hashers for selected algorithms
                            var hashers = new Dictionary<string, HashAlgorithm>();
                            foreach (var algoName in selectedAlgorithms)
                            {
                                hashers[algoName] = CreateHasher(algoName);
                                hashers[algoName].Initialize();
                            }

                            try
                            {
                                using (var stream = File.OpenRead(file))
                                {
                                    // Buffer size: 4MB chunks for optimal performance/feedback balance
                                    byte[] buffer = new byte[4096 * 1024];
                                    int bytesRead;
                                    long fileProcessed = 0;

                                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        if (token.IsCancellationRequested) break;

                                        // Process chunk for ALL hashers simultaneously
                                        bool isLastBlock = (fileProcessed + bytesRead >= fileSize);
                                        
                                        foreach (var kvp in hashers)
                                        {
                                            if (!isLastBlock)
                                            {
                                                kvp.Value.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                                            }
                                            else
                                            {
                                                kvp.Value.TransformFinalBlock(buffer, 0, bytesRead);
                                            }
                                        }

                                        fileProcessed += bytesRead;
                                        processedBytes += bytesRead;

                                        // Update stats every chunk
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

                                // Report results for each algorithm
                                foreach (var kvp in hashers)
                                {
                                    string algoName = kvp.Key;
                                    string hash = BitConverter.ToString(kvp.Value.Hash).Replace("-", "").ToUpperInvariant();

                                    // Check for match (case-insensitive)
                                    bool? matchStatus = null;
                                    if (_expectedHashes != null && _expectedHashes.Count > 0)
                                    {
                                        matchStatus = _expectedHashes.Any(h => h.Equals(hash, StringComparison.OrdinalIgnoreCase));
                                    }

                                    // Final report for this file+algorithm with result
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
                                        StatusText = null,
                                        FilesText = string.Format("{0}/{1}", currentFile, files.Count),
                                        DataText = string.Format("{0}/{1}", FormatSize(processedBytes), FormatSize(totalBytes)),
                                        SpeedText = "-",
                                        ETAText = "Completato"
                                    });
                                }
                            }
                            finally
                            {
                                // Dispose all hashers
                                foreach (var hasher in hashers.Values)
                                {
                                    hasher.Dispose();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            currentFile++;

                            // Report error for each selected algorithm
                            foreach (var algoName in selectedAlgorithms)
                            {
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

        private string GetRelativePath(string fullPath, string basePath)
        {
            if (string.IsNullOrEmpty(basePath)) return System.IO.Path.GetFileName(fullPath);
            
            try
            {
                Uri pathUri = new Uri(fullPath);
                // Append slash to base path to ensure it's treated as a directory
                if (!basePath.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                    basePath += System.IO.Path.DirectorySeparatorChar;
                    
                Uri folderUri = new Uri(basePath);
                return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString().Replace('/', System.IO.Path.DirectorySeparatorChar));
            }
            catch
            {
                return System.IO.Path.GetFileName(fullPath);
            }
        }

        // Context Menu Handlers
        private void CopyHash_Click(object sender, RoutedEventArgs e)
        {
            var result = ResultsGrid.SelectedItem as HashResult;
            if (result != null)
            {
                Clipboard.SetText(result.Hash);
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            var result = ResultsGrid.SelectedItem as HashResult;
            if (result != null)
            {
                Clipboard.SetText(result.Path);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var result = ResultsGrid.SelectedItem as HashResult;
            if (result != null)
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
            if (ResultsGrid.SelectedItem is HashResult hr)
            {
                // Copia TUTTI gli hash calcolati per quel file
                var sb = new StringBuilder();
        
                sb.AppendLine($"File: {hr.FileName}");
                if (!string.IsNullOrEmpty(hr.MD5)) sb.AppendLine($"MD5: {hr.MD5}");
                if (!string.IsNullOrEmpty(hr.SHA1)) sb.AppendLine($"SHA1: {hr.SHA1}");
                if (!string.IsNullOrEmpty(hr.SHA256)) sb.AppendLine($"SHA256: {hr.SHA256}");
                if (!string.IsNullOrEmpty(hr.SHA512)) sb.AppendLine($"SHA512: {hr.SHA512}");
        
                Clipboard.SetText(sb.ToString());
                StatusBarText.Text = "Copiato negli appunti";
            }
            else
            {
                StatusBarText.Text = "Nessuna riga selezionata";
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
{
    if (results.Count == 0)
    {
        MessageBox.Show("Nessun risultato da esportare.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
    }

    var dlg = new SaveFileDialog 
    { 
        Filter = "Text File (*.txt)|*.txt|MD5 File (*.md5)|*.md5|SHA1 File (*.sha1)|*.sha1|SHA256 File (*.sha256)|*.sha256|All Files (*.*)|*.*",
        FileName = "hash_report.txt" 
    };

    if (dlg.ShowDialog() == true)
    {
        try
        {
            var sb = new StringBuilder();
            string ext = System.IO.Path.GetExtension(dlg.FileName).ToLower();
            string exportDir = System.IO.Path.GetDirectoryName(dlg.FileName);

            // Group results by file path
            var groupedByFile = results.GroupBy(r => r.Path).ToList();

            if (ext == ".txt")
            {
                // Formato Report Raggruppato per File
                sb.AppendLine("========================================");
                sb.AppendLine("           HASH FORGE REPORT            ");
                sb.AppendLine("========================================");
                sb.AppendLine("Data: " + DateTime.Now.ToString());
                sb.AppendLine("Totale File: " + groupedByFile.Count);
                sb.AppendLine("Totale Hash: " + results.Count);
                sb.AppendLine("========================================");
                sb.AppendLine("");

                foreach (var fileGroup in groupedByFile)
                {
                    var first = fileGroup.First();
                    sb.AppendLine("File:       " + first.Name);
                    sb.AppendLine("Percorso:   " + first.Path);
                    sb.AppendLine("Dimensione: " + first.Size);
                    sb.AppendLine("");
                    
                    foreach (var item in fileGroup)
                    {
                        sb.AppendLine(string.Format("  {0,-8}: {1}", item.Algorithm, item.Hash));
                    }
                    sb.AppendLine("----------------------------------------");
                }
                
                sb.AppendLine("");
                sb.AppendLine("Generato da Hash Forge v1.2.0");
            }
            else
            {
                // Formato Standard (hash *filename) - raggruppato per file
                // Compatibile con md5sum, sha1sum, RapidCRC, TeraCopy, ecc.
                foreach (var fileGroup in groupedByFile)
                {
                    var first = fileGroup.First();
                    string relativePath = GetRelativePath(first.Path, exportDir);
                    
                    // Se ci sono più algoritmi, aggiungi commento
                    if (fileGroup.Count() > 1)
                    {
                        sb.AppendLine("; " + relativePath);
                    }
                    
                    foreach (var item in fileGroup)
                    {
                        sb.AppendLine(string.Format("{0} *{1}", item.Hash, relativePath));
                    }
                }
            }
            
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



        private void OpenHashList_Click(object sender, RoutedEventArgs e)
        {
            HashListOverlay.Visibility = Visibility.Visible;
            HashListInput.Focus();
        }

        private void SaveHashList_Click(object sender, RoutedEventArgs e)
        {
            string text = HashListInput.Text;
            _expectedHashes.Clear();

            if (!string.IsNullOrWhiteSpace(text))
            {
                // Split semplice per righe
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string cleanLine = line.Trim();
                    if (!string.IsNullOrEmpty(cleanLine))
                    {
                        // Se la linea contiene spazi (es. "HASH filename"), prendiamo solo la prima parte
                        // Questo gestisce formati tipo md5sum, sha1sum, ecc.
                        var parts = cleanLine.Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            // Assumiamo che l'hash sia la parte più lunga o la prima parte esadecimale valida
                            // Per semplicità, prendiamo la prima parte se sembra un hash (solo hex)
                            // O se non siamo sicuri, prendiamo l'intera riga pulita
                            string potentialHash = parts[0];
                            if (System.Text.RegularExpressions.Regex.IsMatch(potentialHash, "^[a-fA-F0-9]+$"))
                            {
                                _expectedHashes.Add(potentialHash);
                            }
                            else
                            {
                                // Fallback: prova a cercare un hash nella riga
                                var match = System.Text.RegularExpressions.Regex.Match(cleanLine, "[a-fA-F0-9]{32,128}");
                                if (match.Success)
                                {
                                    _expectedHashes.Add(match.Value);
                                }
                                else
                                {
                                    _expectedHashes.Add(cleanLine);
                                }
                            }
                        }
                    }
                }
            }

            if (_expectedHashes.Count > 0)
            {
                ManageHashesBtn.Content = string.Format("✅ {0} Hash Caricati", _expectedHashes.Count);
                ManageHashesBtn.Background = (Brush)this.Resources["AccentBrush"];
                ManageHashesBtn.Foreground = (Brush)this.Resources["ButtonForegroundBrush"];
                ManageHashesBtn.FontWeight = FontWeights.Bold;
                
                // Show Status Column
                if (ResultsGrid != null && ResultsGrid.Columns.Count > 4)
                {
                    ResultsGrid.Columns[4].Visibility = Visibility.Visible;
                }
            }
            else
            {
                ManageHashesBtn.Content = "Gestisci Hash Attesi";
                ManageHashesBtn.Background = (Brush)this.Resources["ControlBackgroundBrush"];
                ManageHashesBtn.Foreground = (Brush)this.Resources["TextBrush"];
                ManageHashesBtn.FontWeight = FontWeights.Normal;
                
                // Hide Status Column
                if (ResultsGrid != null && ResultsGrid.Columns.Count > 4)
                {
                    ResultsGrid.Columns[4].Visibility = Visibility.Collapsed;
                }
            }

            HashListOverlay.Visibility = Visibility.Collapsed;
        }

        private void LoadHashFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Hash Files (*.txt;*.md5;*.sha1;*.sha256)|*.txt;*.md5;*.sha1;*.sha256|All Files (*.*)|*.*",
                Title = "Carica File Hash"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(dlg.FileName);
                    HashListInput.Text = content; // Carica il contenuto grezzo, il parsing avviene al salvataggio
                    MessageBox.Show("File caricato! Clicca 'Salva e Chiudi' per processare gli hash.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Errore caricamento file: " + ex.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
