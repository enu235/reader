using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HtmlAgilityPack;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace SpeedReader
{
    public partial class MainWindow : Window
    {
        private List<string> _words = new List<string>();
        private int _currentWordIndex = 0;
        private DispatcherTimer _timer = new DispatcherTimer();
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isPaused = false;

        public MainWindow()
        {
            InitializeComponent();
            _timer.Tick += Timer_Tick;
            
            // Initialize UI controls
            Dispatcher.UIThread.Post(() => {
                SpeedComboBox.SelectedIndex = 0;
                FontSizeComboBox.SelectedIndex = 0;
                FontFamilyComboBox.SelectedIndex = 0;
                PdfRadioButton.IsChecked = true;
            });
        }

        private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
        {
            try 
            {
                if (PdfRadioButton != null && PdfRadioButton.IsChecked == true)
                {
                    var storageProvider = StorageProvider;
                    var fileTypes = new FilePickerFileType("PDF Documents")
                    {
                        Patterns = new[] { "*.pdf" },
                        MimeTypes = new[] { "application/pdf" }
                    };

                    var options = new FilePickerOpenOptions
                    {
                        Title = "Select PDF File",
                        AllowMultiple = false,
                        FileTypeFilter = new[] { fileTypes, new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } } }
                    };

                    var result = await storageProvider.OpenFilePickerAsync(options);
                    if (result.Count > 0)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => {
                            SourceTextBox.Text = result[0].Path.LocalPath;
                        });
                    }
                }
                else
                {
                    // For web pages, let the user enter the URL directly
                    await ShowMessageAsync("Information", "For web pages, please enter the URL directly in the source field.");
                }
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Error", $"An error occurred: {ex.Message}");
            }
        }

        private async void StartButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SourceTextBox?.Text))
                {
                    await ShowMessageAsync("Error", "Please enter a source path or URL.");
                    return;
                }

                if (_isPaused)
                {
                    ResumeReading();
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() => {
                    StartButton.IsEnabled = false;
                    PauseButton.IsEnabled = false;
                    StopButton.IsEnabled = false;
                });

                _cancellationTokenSource = new CancellationTokenSource();
                _currentWordIndex = 0;
                _words.Clear();

                try
                {
                    // Extract text based on source type
                    if (PdfRadioButton != null && PdfRadioButton.IsChecked == true)
                    {
                        string pdfPath = SourceTextBox.Text;
                        if (!File.Exists(pdfPath))
                        {
                            await ShowMessageAsync("Error", "The specified PDF file does not exist.");
                            await ResetControlsAsync();
                            return;
                        }

                        try
                        {
                            List<string> extractedWords = await Task.Run(() => {
                                try
                                {
                                    return ExtractTextFromPdf(pdfPath);
                                }
                                catch (Exception ex)
                                {
                                    Dispatcher.UIThread.Post(() => {
                                        ShowMessageAsync("Error", $"PDF extraction error: {ex.Message}");
                                    });
                                    return new List<string>();
                                }
                            });

                            if (extractedWords.Count == 0)
                            {
                                await ShowMessageAsync("Warning", "No words were extracted from the PDF.");
                                await ResetControlsAsync();
                                return;
                            }

                            await Dispatcher.UIThread.InvokeAsync(() => {
                                _words = new List<string>(extractedWords);
                            });
                        }
                        catch (Exception ex)
                        {
                            await ShowMessageAsync("Error", $"PDF error: {ex.Message}");
                            await ResetControlsAsync();
                            return;
                        }
                    }
                    else
                    {
                        List<string> webWords = await ExtractTextFromWebPage(SourceTextBox.Text);
                        if (webWords.Count == 0)
                        {
                            await ShowMessageAsync("Warning", "No words were extracted from the web page.");
                            await ResetControlsAsync();
                            return;
                        }
                        
                        await Dispatcher.UIThread.InvokeAsync(() => {
                            _words = new List<string>(webWords);
                        });
                    }

                    if (_words.Count == 0)
                    {
                        await ShowMessageAsync("Warning", "No words were extracted from the source.");
                        await ResetControlsAsync();
                        return;
                    }

                    // Configure display
                    await ConfigureWordDisplayAsync();

                    // Start the timer to display words
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        var selectedItem = (ComboBoxItem)SpeedComboBox.SelectedItem!;
                        string content = selectedItem?.Content?.ToString() ?? "250";
                        int wordsPerMinute = int.Parse(content.Split(' ')[0]);
                        double intervalInMs = 60000.0 / wordsPerMinute;
                        _timer.Interval = TimeSpan.FromMilliseconds(intervalInMs);

                        ReadingProgress.Maximum = _words.Count;
                        ReadingProgress.Value = 0;
                        _timer.Start();
                        PauseButton.IsEnabled = true;
                        StopButton.IsEnabled = true;
                    });
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync("Error", $"An error occurred: {ex.Message}");
                    await ResetControlsAsync();
                }
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Error", $"Unhandled error: {ex.Message}");
                await ResetControlsAsync();
            }
        }

        private void PauseButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                Dispatcher.UIThread.Post(() => {
                    if (_timer.IsEnabled)
                    {
                        _timer.Stop();
                        _isPaused = true;
                        PauseButton.Content = "Resume";
                        StartButton.IsEnabled = true;
                    }
                    else
                    {
                        ResumeReading();
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => {
                    ShowMessageAsync("Error", $"Error during pause: {ex.Message}");
                });
            }
        }

        private void ResumeReading()
        {
            try
            {
                Dispatcher.UIThread.Post(() => {
                    _timer.Start();
                    _isPaused = false;
                    PauseButton.Content = "Pause";
                    StartButton.IsEnabled = false;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => {
                    ShowMessageAsync("Error", $"Error during resume: {ex.Message}");
                });
            }
        }

        private void StopButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                Dispatcher.UIThread.Post(() => {
                    _timer.Stop();
                    _cancellationTokenSource?.Cancel();
                    WordDisplay.Text = "Words will appear here";
                    ReadingProgress.Value = 0;
                    _currentWordIndex = 0;
                });
                
                ResetControlsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => {
                    ShowMessageAsync("Error", $"Error during stop: {ex.Message}");
                });
            }
        }

        private async Task ResetControlsAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => {
                StartButton.IsEnabled = true;
                PauseButton.IsEnabled = false;
                PauseButton.Content = "Pause";
                StopButton.IsEnabled = false;
                _isPaused = false;
            });
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.UIThread.Post(() => {
                    if (_currentWordIndex < _words.Count)
                    {
                        // Update the word display with animation
                        UpdateWordDisplay(_words[_currentWordIndex]);
                        
                        // Update progress and stats
                        ReadingProgress.Value = _currentWordIndex + 1;
                        UpdateStatsDisplay();
                        
                        _currentWordIndex++;
                    }
                    else
                    {
                        _timer.Stop();
                        ShowMessageAsync("Information", "Reading completed!").ConfigureAwait(false);
                        ResetControlsAsync().ConfigureAwait(false);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => {
                    _timer.Stop();
                    ShowMessageAsync("Error", $"Error during reading: {ex.Message}").ConfigureAwait(false);
                    ResetControlsAsync().ConfigureAwait(false);
                });
            }
        }
        
        private void UpdateWordDisplay(string word)
        {
            try
            {
                // Update the word text with a simple fade effect
                Dispatcher.UIThread.Post(() => {
                    // Apply a subtle fade effect
                    WordDisplay.Opacity = 0.7;
                    
                    // Update the text
                    WordDisplay.Text = word;
                    
                    // Restore opacity
                    WordDisplay.Opacity = 1.0;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating word display: {ex.Message}");
            }
        }
        
        private void UpdateStatsDisplay()
        {
            try
            {
                if (StatsDisplay != null)
                {
                    StatsDisplay.Text = $"{_currentWordIndex + 1} / {_words.Count} words";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating stats display: {ex.Message}");
            }
        }

        private List<string> ExtractTextFromPdf(string pdfPath)
        {
            var extractedWords = new List<string>();
            
            try
            {
                using (var pdfReader = new PdfReader(pdfPath))
                using (var pdfDocument = new PdfDocument(pdfReader))
                {
                    int pageCount = pdfDocument.GetNumberOfPages();
                    Console.WriteLine($"PDF has {pageCount} pages");
                    
                    for (int i = 1; i <= pageCount; i++)
                    {
                        if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                            break;

                        var page = pdfDocument.GetPage(i);
                        var strategy = new SimpleTextExtractionStrategy();
                        string text = PdfTextExtractor.GetTextFromPage(page, strategy);
                        
                        Console.WriteLine($"Extracted text from page {i}: {text.Substring(0, Math.Min(50, text.Length))}...");
                        
                        // Process extracted text
                        var pageWords = ProcessText(text);
                        extractedWords.AddRange(pageWords);
                        
                        Console.WriteLine($"Processed {pageWords.Count} words from page {i}");
                    }
                }
                
                Console.WriteLine($"Total extracted words: {extractedWords.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PDF extraction error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw new Exception($"Error processing PDF: {ex.Message}", ex);
            }
            
            return extractedWords;
        }

        private async Task<List<string>> ExtractTextFromWebPage(string url)
        {
            var extractedWords = new List<string>();
            
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                {
                    await ShowMessageAsync("Error", "Please enter a valid URL.");
                    return extractedWords;
                }

                using (var httpClient = new HttpClient())
                {
                    try
                    {
                        string htmlContent = await httpClient.GetStringAsync(url);
                        var htmlDocument = new HtmlDocument();
                        htmlDocument.LoadHtml(htmlContent);

                        // Extract text from the main content of the page
                        // This is a simple implementation and might need refinement for specific websites
                        var bodyNode = htmlDocument.DocumentNode.SelectSingleNode("//body");
                        if (bodyNode != null)
                        {
                            string text = bodyNode.InnerText;
                            extractedWords = ProcessText(text);
                        }
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageAsync("Error", $"Error fetching web page: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error processing web page: {ex.Message}", ex);
            }
            
            return extractedWords;
        }

        private List<string> ProcessText(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return new List<string>();
                }
                
                // Clean and split the text into words
                // Remove common non-alphanumeric characters except those that might be part of words
                string cleanedText = text.Replace("\n", " ")
                                        .Replace("\r", " ")
                                        .Replace("\t", " ");

                // Split by spaces and filter out empty entries
                string[] words = cleanedText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(w => w.Trim())
                                        .Where(w => !string.IsNullOrWhiteSpace(w))
                                        .ToArray();

                return words.ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing text: {ex.Message}");
                throw new Exception($"Error processing text: {ex.Message}", ex);
            }
        }

        private async Task ConfigureWordDisplayAsync()
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => {
                    // Set font size based on selection
                    var fontSizeItem = (ComboBoxItem)FontSizeComboBox.SelectedItem!;
                    int fontSize = int.Parse(fontSizeItem?.Content?.ToString() ?? "20");
                    
                    // Set font family based on selection
                    var fontFamilyItem = (ComboBoxItem)FontFamilyComboBox.SelectedItem!;
                    string fontFamily = fontFamilyItem?.Content?.ToString() ?? "Arial";
                    
                    WordDisplay.FontSize = fontSize;
                    WordDisplay.FontFamily = new FontFamily(fontFamily);
                    
                    // Reset stats display
                    if (StatsDisplay != null)
                    {
                        StatsDisplay.Text = $"0 / {_words.Count} words";
                    }
                });
            }
            catch (Exception ex)
            {
                throw new Exception($"Error configuring display: {ex.Message}", ex);
            }
        }
        
        // Helper method to show a message dialog
        private async Task ShowMessageAsync(string title, string message)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(async () => {
                    var messageBox = new Window()
                    {
                        Title = title,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new StackPanel
                        {
                            Margin = new Thickness(20),
                            Children = 
                            {
                                new TextBlock
                                {
                                    Text = message,
                                    Margin = new Thickness(0, 0, 0, 20)
                                },
                                new Button
                                {
                                    Content = "OK",
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                    Width = 80,
                                    Height = 30
                                }
                            }
                        }
                    };

                    var button = ((StackPanel)messageBox.Content).Children.Last() as Button;
                    if (button != null)
                    {
                        button.Click += (s, e) => { messageBox.Close(); };
                    }

                    await messageBox.ShowDialog(this);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing message: {ex.Message}");
            }
        }
    }
} 