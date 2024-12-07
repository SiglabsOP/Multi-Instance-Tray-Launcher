using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace AnimatedFileExplorer
{
    public class FileInfoItem
    {
        public string? FileName { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
    }

    public partial class MainWindow : Window
    {
        private readonly NotifyIcon notifyIcon = new();
        private bool isWindowHidden;
        private SoundPlayer? startupSoundPlayer;
        private ObservableCollection<FileInfoItem> filesList = [];
        private int currentIndex = 0;
        private readonly int filesPerPage = 10;
        private bool logWarningShown = false;

        // Mutex to ensure single instance
        private readonly Mutex singleInstanceMutex;

        public MainWindow()
        {
            // Check if another instance is already running using Mutex
            singleInstanceMutex = new Mutex(true, "{Explorer vermeerLabs}", out bool createdNew);

            if (!createdNew)
            {
                // Another instance is already running, so exit - (c) SIG LABS 2024
               // System.Windows.MessageBox.Show("Another instance of the application is already running.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Windows.Application.Current.Shutdown(); return;
            }

            InitializeComponent();
            InitializeTrayIcon();
            LoadStartupSound();

            // Set the initial state to minimized
            WindowState = WindowState.Minimized;

            // Play the startup sound
            PlayStartupSound();
            Visibility = Visibility.Hidden;

            // Set the icon for the title bar
            try
            {
                string iconPath = @"C:\VermeerSuite 10-1\VermeerSuite Installation\VermeerLabs\GUI\medias\favicon.ico";
                if (File.Exists(iconPath))
                {
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath));
                }
            }
            catch (Exception ex)
            {
                // Handle any exception that might occur while setting the icon
                Console.WriteLine("Error setting the window icon: " + ex.Message);
            }

            // Register event handler for Window StateChanged
            StateChanged += (sender, e) =>
            {
                if (WindowState == WindowState.Minimized)
                {
                    MinimizeToTray();
                }
                else
                {
                    RestoreMainWindow();
                }
            };
        }

        private void InitializeTrayIcon()
        {
            NotifyIcon notifyIcon = new()
            {
                Icon = new Icon(@"C:\VermeerSuite 10-1\VermeerSuite Installation\VermeerLabs\GUI\medias\favicon.ico"),
                Visible = true
            };

            notifyIcon.MouseClick += NotifyIcon_MouseClick;
            notifyIcon.BalloonTipText = "VermeerLab Explorer is running in the background.";

            ContextMenuStrip contextMenu = new();
            ToolStripMenuItem openMenuItem = new("Open", null, OpenMenuItem_Click);
            ToolStripMenuItem aboutMenuItem = new("About", null, (sender, e) => ShowAboutDialog());
            ToolStripSeparator separator = new();
            ToolStripMenuItem exitMenuItem = new("Exit", null, (sender, e) => CloseApplication());

            _ = contextMenu.Items.Add(openMenuItem);
            _ = contextMenu.Items.Add(aboutMenuItem);
            _ = contextMenu.Items.Add(separator);
            _ = contextMenu.Items.Add(exitMenuItem);

            notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void OpenMenuItem_Click(object? sender, EventArgs e)
        {
            if (isWindowHidden)
            {
                RestoreMainWindow();
                WindowState = WindowState.Normal;
                _ = Activate();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private static void ShowAboutDialog()
        {
            _ = System.Windows.MessageBox.Show("VermeerLab Explorer\n\n(c) Peter De Ceuster\n04/01/2024\npeterdeceuster.uk\n This program is part of the Laboratory GUI v9.7", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                if (isWindowHidden)
                {
                    RestoreMainWindow();
                    WindowState = WindowState.Normal;
                    _ = Activate();
                }
                else
                {
                    if (WindowState == WindowState.Normal)
                    {
                        WindowState = WindowState.Minimized;
                    }
                    else
                    {
                        if (WindowState == WindowState.Minimized)
                        {
                            RestoreMainWindow();
                            WindowState = WindowState.Normal;
                            _ = Activate();
                        }
                        else
                        {
                            WindowState = WindowState.Minimized;
                        }
                    }
                }
            }
        }



        private void MinimizeToTray()
        {
            Hide();
            isWindowHidden = true;
            notifyIcon.ShowBalloonTip(500, "VermeerLab Explorer", "Minimized to tray.", ToolTipIcon.Info);
        }

        private void RestoreMainWindow()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            WindowState = WindowState.Maximized; // Set the WindowState to Maximized
            isWindowHidden = false;
            _ = Activate();
        }



        private void CloseApplication()
        {
            // Perform any cleanup tasks if needed

            // Release and close the Mutex
            singleInstanceMutex?.ReleaseMutex();
            singleInstanceMutex?.Close();

            notifyIcon.Dispose();
            Close();
        }

        private bool allowResize = true;

        public MainWindow(bool allowResize) : this()
        {
            this.allowResize = allowResize;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                MinimizeToTray();
            }
            else if (WindowState == WindowState.Normal)
            {
                // If the window is in the normal state, update the allowResize flag
                allowResize = true;
                RestoreMainWindow();
            }
        }


        private async void LocateFilesButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset the logWarningShown flag before initiating the search
            logWarningShown = false;
            PlayStartupSound();
            string selectedFolderPath = GetFolderPath();
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                string fileType = GetFileType();
                if (!string.IsNullOrWhiteSpace(fileType))
                {
                    try
                    {
                        IEnumerable<string> foundFiles = await SearchFilesAsync(selectedFolderPath, fileType);

                        if (!foundFiles.Any())
                        {
                            // Handle the case where no files were found (due to unauthorized access)
                            // Optionally, display a message to the user or perform any other logic.
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _ = HandleUnauthorizedAccessException(ex, selectedFolderPath, CancellationToken.None);
                    }
                }
                else
                {
                    _ = System.Windows.MessageBox.Show("File type cannot be empty.");

                }
            }
        }

        private async void LocateFilesInSubfoldersButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset the logWarningShown flag before initiating the search
            logWarningShown = false;

            string selectedFolderPath = GetFolderPath();
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                string fileType = GetFileType();
                if (!string.IsNullOrWhiteSpace(fileType))
                {
                    try
                    {
                        IEnumerable<string> foundFiles = await SearchFilesAsync(selectedFolderPath, fileType, SearchOption.AllDirectories, CancellationToken.None);

                        if (!foundFiles.Any())
                        {
                            // Handle the case where no files were found (due to unauthorized access)
                            // Optionally, display a message to the user or perform any other logic.
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _ = HandleUnauthorizedAccessException(ex, selectedFolderPath, CancellationToken.None);
                    }
                }
                else
                {
                    _ = System.Windows.MessageBox.Show("File type cannot be empty.");
                }
            }
        }

        private async Task<IEnumerable<string>> SearchFilesAsync(string folderPath, string fileType, SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            filesList.Clear();
            DisplayFiles();

            try
            {
                IEnumerable<string> foundFiles = await Task.Run(() => Directory.GetFiles(folderPath, $"*.{fileType}", searchOption));

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    filesList = new ObservableCollection<FileInfoItem>(
                        foundFiles.Select(filePath => new FileInfoItem
                        {
                            FileName = Path.GetFileName(filePath),
                            CreationTime = File.GetCreationTime(filePath),
                            LastWriteTime = File.GetLastWriteTime(filePath)
                        })
                    );
                    currentIndex = 0;
                    DisplayFiles();
                    UpdatePaginationButtons();
                });

                return foundFiles;
            }
            catch (UnauthorizedAccessException ex)
            {
                _ = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => HandleUnauthorizedAccessException(ex, folderPath, cancellationToken));
            }

            return Enumerable.Empty<string>();
        }
#pragma warning disable IDE0060 // Remove unused parameter
        private IEnumerable<string> HandleUnauthorizedAccessException(UnauthorizedAccessException ex, string folderPath, CancellationToken _1cancellationToken)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            LogError($"Access to the folder '{folderPath}' is denied: {ex.Message}");

            if (!logWarningShown)
            {
                logWarningShown = true;

                IEnumerable<string> accessibleFiles = Enumerable.Empty<string>();
                IEnumerable<string> accessibleSubfolders = Enumerable.Empty<string>();

                try
                {
                    accessibleFiles = Directory.EnumerateFiles(folderPath);
                    accessibleSubfolders = Directory.EnumerateDirectories(folderPath);
                }
                catch (UnauthorizedAccessException)
                {
                    LogError($"Error accessing files or subfolders in '{folderPath}'.");
                    return Enumerable.Empty<string>();
                }

                accessibleFiles = accessibleFiles.Where(file => Path.GetExtension(file)?.Equals("." + GetFileType(), StringComparison.OrdinalIgnoreCase) ?? false);

                IEnumerable<string> accessibleItems = accessibleFiles.Concat(accessibleSubfolders);

                _ = System.Windows.MessageBox.Show("Operation completed with errors. Check the log file for details.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);

                return accessibleItems;
            }

            return Enumerable.Empty<string>();
        }

        private static void LogError(string message)
        {
            File.AppendAllText("error.log", $"{DateTime.Now} - {message}\n");
        }

        private static string GetFolderPath()
        {
            using CommonOpenFileDialog openFileDialog = new()
            {
                IsFolderPicker = true,
                Title = "Select a Folder"
            };

            return openFileDialog.ShowDialog() == CommonFileDialogResult.Ok ? openFileDialog.FileName ?? string.Empty : string.Empty;
        }

        private static string GetFileName()
        {
            return Microsoft.VisualBasic.Interaction.InputBox("Enter File Name:", "File Name", "");
        }

        private static string GetFileType()
        {
            return Microsoft.VisualBasic.Interaction.InputBox("Enter File Type (e.g., txt):", "File Type", "");
        }

        private void DisplayFiles()
        {
            List<FileInfoItem> currentFiles = filesList.Skip(currentIndex).Take(filesPerPage).ToList();
            FilesListView.ItemsSource = currentFiles;
        }
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            currentIndex += filesPerPage;
            DisplayFiles();
            UpdatePaginationButtons();
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            currentIndex -= filesPerPage;
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            DisplayFiles();
            UpdatePaginationButtons();
        }

        private void UpdatePaginationButtons()
        {
            _ = (int)Math.Ceiling((double)filesList.Count / filesPerPage);

            PreviousButton.Visibility = currentIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Visibility = currentIndex + filesPerPage < filesList.Count ? Visibility.Visible : Visibility.Collapsed;

            if (currentIndex + filesPerPage >= filesList.Count)
            {
                NextButton.Visibility = Visibility.Collapsed;
                PreviousButton.Visibility = Visibility.Visible;
            }
            else
            {
                NextButton.Visibility = Visibility.Visible;
            }
        }

        private async void SearchFilesByNameButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedFolderPath = GetFolderPath();
            if (!string.IsNullOrWhiteSpace(selectedFolderPath)) // Check if folder path is not empty
            {
                string fileName = GetFileName();
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    try
                    {
                        IEnumerable<string> foundFiles = await SearchFilesByNameAsync(selectedFolderPath, fileName);

                        if (!foundFiles.Any())
                        {
                            // Handle the case where no files were found
                            // Optionally, display a message to the user or perform any other logic.
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _ = HandleUnauthorizedAccessException(ex, selectedFolderPath, CancellationToken.None);
                    }
                }
                else
                {
                    _ = System.Windows.MessageBox.Show("File name cannot be empty.");
                }
            }
        }


        private async Task<IEnumerable<string>> SearchFilesByNameAsync(string folderPath, string fileName, CancellationToken cancellationToken = default)
        {
            filesList.Clear();
            DisplayFiles();

            try
            {
                IEnumerable<string> foundFiles = await Task.Run(() =>
                {
                    return Directory.EnumerateFiles(folderPath, $"*{fileName}*", SearchOption.AllDirectories);
                });

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    filesList = new ObservableCollection<FileInfoItem>(
                        foundFiles.Select(filePath => new FileInfoItem
                        {
                            FileName = Path.GetFileName(filePath),
                            CreationTime = File.GetCreationTime(filePath),
                            LastWriteTime = File.GetLastWriteTime(filePath)
                        })
                    );
                    currentIndex = 0;
                    DisplayFiles();
                    UpdatePaginationButtons();
                });

                return foundFiles;
            }
            catch (UnauthorizedAccessException ex)
            {
                _ = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => HandleUnauthorizedAccessException(ex, folderPath, cancellationToken));
            }

            return Enumerable.Empty<string>();
        }

        private void LoadStartupSound()
        {
            try
            {
                string soundFilePath = @"C:\VermeerSuite 10-1\VermeerSuite Installation\VermeerLabs\GUI\medias\snd\intro.wav";
                if (File.Exists(soundFilePath))
                {
                    startupSoundPlayer = new SoundPlayer(soundFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading startup sound: " + ex.Message);
            }
        }

        private void PlayStartupSound()
        {
            try
            {
                startupSoundPlayer?.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error playing startup sound: " + ex.Message);
            }
        }
        private void OpenSecondExternalAppButton_Click(object sender, RoutedEventArgs e)
        {
            string secondExternalAppPath = @"C:\VermeerSuite 10-1\VermeerSuite Installation\VermeerLabs\Pulse log central\pulse-12.3.exe";

            if (File.Exists(secondExternalAppPath))
            {
                // Play the sound effect adjust this to some other sound
                PlayStartupSound();

                _ = Process.Start(secondExternalAppPath);
            }
            else
            {
                // Play an error sound or show a message
                
                _ = System.Windows.MessageBox.Show("Second external application not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the application
            Close();
        }
        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            // Path to the new executable
            string newExePath = @"C:\VermeerSuite 10-1\VermeerSuite Installation\VermeerLabs\Papermill 4\Papermill.exe";

            // Check if the executable file exists
            if (File.Exists(newExePath))
            {
                try
                {
                    // Start the process
                    _ = Process.Start(newExePath);
                }
                catch (Exception ex)
                {
                    // Handle any exceptions that might occur
                    _ = System.Windows.MessageBox.Show($"Error launching executable: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Show a message if the executable file is not found
                _ = System.Windows.MessageBox.Show("Executable file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewButtonF_Click(object sender, RoutedEventArgs e)
        {
            // Path to the new executable
            string newExePath = @"C:\VermeerSuite 10-1\VermeerSuite Installation\VermeerLabs\Vermeer Finance Hub\VermeerSuite Finance Hub.exe";

            // Check if the executable file exists
            if (File.Exists(newExePath))
            {
                try
                {
                    // Start the process
                    _ = Process.Start(newExePath);
                }
                catch (Exception ex)
                {
                    // Handle any exceptions that might occur
                    _ = System.Windows.MessageBox.Show($"Error launching executable: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Show a message if the executable file is not found
                _ = System.Windows.MessageBox.Show("Executable file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewButtonA_Click(object sender, RoutedEventArgs e)
        {
            // Path to the new executable
            string newExePath = @"C:\VermeerSuite 10-1\VermeerSuite Installation\VermeerLabs\Vermeer Administrative Hub\VermeerSuite Admin Hub.exe";

            // Check if the executable file exists
            if (File.Exists(newExePath))
            {
                try
                {
                    // Start the process
                    _ = Process.Start(newExePath);
                }
                catch (Exception ex)
                {
                    // Handle any exceptions that might occur
                    _ = System.Windows.MessageBox.Show($"Error launching executable: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Show a message if the executable file is not found
                _ = System.Windows.MessageBox.Show("Executable file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void OpenExternalAppButton_Click(object sender, RoutedEventArgs e)
        {                   
            string externalAppPath = @"C:\VermeerSuite 10-1\VermeerSuite Development\Library\VS\GUI\Tangaroa\x64\Debug\WinApp.exe";

            if (File.Exists(externalAppPath))
            {
                _ = Process.Start(externalAppPath);
            }
            else
            {
                _ = System.Windows.MessageBox.Show("External application not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Other methods remain unchanged...
    }
}