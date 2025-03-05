using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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
        private string _fullText = "";
        private int _currentWordIndex = 0;
        private DispatcherTimer _timer = new DispatcherTimer();
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isPaused = false;
        private const int CONTEXT_WINDOW = 100; // Number of words to show before and after

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
                
                // Initialize context display
                InitializeContextDisplay();
            });
        }

        private void InitializeContextDisplay()
        {
            // Set initial visibility based on toggle state
            bool showContext = ContextToggle.IsChecked ?? true;
            ContextBorder.IsVisible = showContext;
            
            // Adjust word display z-index to ensure it's on top
            WordHighlight.ZIndex = 10;
        }

        private void ContextToggle_Checked(object sender, RoutedEventArgs e)
        {
            ContextBorder.IsVisible = true;
            UpdateContextDisplay();
        }

        private void ContextToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            ContextBorder.IsVisible = false;
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
                        // For web pages, use the WebPageSelector
                        string url = SourceTextBox.Text;
                        
                        // Validate URL
                        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                        {
                            await ShowMessageAsync("Error", "Please enter a valid URL.");
                            await ResetControlsAsync();
                            return;
                        }
                        
                        try
                        {
                            // Show the web page selector
                            var webPageSelector = new WebPageSelector();
                            
                            // Handle the content selected event
                            List<string> webWords = new List<string>();
                            webPageSelector.ContentSelected += (s, args) => {
                                webWords = args.Words;
                            };
                            
                            // Initialize and show the selector
                            await webPageSelector.InitializeAsync(url);
                            await webPageSelector.ShowDialog(this);
                            
                            // Check if words were extracted
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
                        catch (Exception ex)
                        {
                            await ShowMessageAsync("Error", $"Web page error: {ex.Message}");
                            await ResetControlsAsync();
                            return;
                        }
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
                        
                        // Update context text
                        UpdateContextDisplay();
                        
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
        
        private void UpdateContextDisplay()
        {
            try
            {
                if (_words.Count == 0 || !ContextBorder.IsVisible) return;
                
                // Update full context display if it's empty
                if (string.IsNullOrEmpty(ContextTextDisplay.Text))
                {
                    ContextTextDisplay.Text = _fullText;
                }
                
                // Calculate the visible range
                int startIndex = Math.Max(0, _currentWordIndex - CONTEXT_WINDOW);
                int endIndex = Math.Min(_words.Count - 1, _currentWordIndex + CONTEXT_WINDOW);
                
                // Clear existing highlights
                HighlightCanvas.Children.Clear();
                
                // Find position of current word in the text
                if (_currentWordIndex < _words.Count && !string.IsNullOrEmpty(_fullText))
                {
                    // Get the current word's position
                    int position = GetPositionOfWordInText(_currentWordIndex);
                    if (position >= 0)
                    {
                        // Scroll to make sure the word is visible
                        ScrollToPosition(position);
                        
                        // Add highlight for the current word
                        HighlightCurrentWord(position, _words[_currentWordIndex].Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating context display: {ex.Message}");
            }
        }
        
        private int GetPositionOfWordInText(int wordIndex)
        {
            // This is a simplified approach - might need refinement depending on text format
            if (string.IsNullOrEmpty(_fullText) || wordIndex < 0 || wordIndex >= _words.Count)
                return -1;
            
            try
            {
                // Find the position by joining words up to the current one
                // This is not 100% accurate but serves as an approximation
                
                // If there's too many words, we'll use a sliding window approach
                int startWindow = Math.Max(0, wordIndex - 5);
                int endWindow = Math.Min(_words.Count - 1, wordIndex + 5);
                
                // Build a search pattern with the current word plus some context
                StringBuilder pattern = new StringBuilder();
                
                // Add words before the current one
                for (int i = startWindow; i < wordIndex; i++)
                {
                    pattern.Append(_words[i]).Append(" ");
                }
                
                // Add the current word
                string currentWord = _words[wordIndex];
                pattern.Append(currentWord);
                
                // Add a few words after as context
                if (wordIndex < endWindow)
                {
                    pattern.Append(" ");
                    for (int i = wordIndex + 1; i <= endWindow; i++)
                    {
                        pattern.Append(_words[i]);
                        if (i < endWindow) pattern.Append(" ");
                    }
                }
                
                // Try to find this pattern in the text
                string searchPattern = pattern.ToString();
                int foundPos = _fullText.IndexOf(searchPattern);
                
                // If found, calculate the exact position of the current word
                if (foundPos >= 0)
                {
                    // Calculate offset to the start of the current word within the pattern
                    int offsetInPattern = 0;
                    for (int i = startWindow; i < wordIndex; i++)
                    {
                        offsetInPattern += _words[i].Length + 1; // +1 for space
                    }
                    
                    return foundPos + offsetInPattern;
                }
                else if (!string.IsNullOrEmpty(currentWord))
                {
                    // Fallback: just try to find the word by itself
                    // This is less accurate but better than nothing
                    
                    // Find all occurrences of the word
                    List<int> positions = new List<int>();
                    int pos = 0;
                    while ((pos = _fullText.IndexOf(currentWord, pos)) >= 0)
                    {
                        positions.Add(pos);
                        pos += currentWord.Length;
                    }
                    
                    // Try to find the occurrence closest to where we expect it
                    if (positions.Count > 0)
                    {
                        // Estimate where we expect to be based on progress
                        double progress = (double)wordIndex / _words.Count;
                        int expectedPos = (int)(_fullText.Length * progress);
                        
                        // Find closest position
                        return positions.OrderBy(p => Math.Abs(p - expectedPos)).First();
                    }
                }
                
                // If no match found, return a position based on progress through the text
                return (int)(_fullText.Length * ((double)wordIndex / _words.Count));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding word position: {ex.Message}");
                return -1;
            }
        }
        
        private void ScrollToPosition(int position)
        {
            try
            {
                // Estimate the position in the document by creating a temporary TextBlock
                // with the same formatting as ContextTextDisplay but only containing text up to the position
                var tempText = _fullText.Substring(0, position);
                var tempTextBlock = new TextBlock
                {
                    Text = tempText,
                    TextWrapping = TextWrapping.Wrap,
                    Width = ContextTextDisplay.Bounds.Width
                };
                
                // Measure the temporary TextBlock
                tempTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                
                // Calculate the approximate vertical position
                double lineHeight = 20; // Estimated line height
                int lineCount = (int)(tempTextBlock.DesiredSize.Height / lineHeight);
                double verticalOffset = lineCount * lineHeight;
                
                // Scroll to the position
                ContextScrollViewer.Offset = new Vector(0, Math.Max(0, verticalOffset - 100));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scrolling to position: {ex.Message}");
            }
        }
        
        private void HighlightCurrentWord(int position, int length)
        {
            try
            {
                // Create a rectangle to highlight the current word
                var highlight = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.Parse("#3078D7")),
                    Opacity = 0.5,
                    RadiusX = 3,
                    RadiusY = 3
                };
                
                // Position the highlight at the right spot
                // This is an approximation and might need refinement
                double approxCharWidth = 7; // Approximate width of a character
                double approxLineHeight = 20; // Approximate line height
                
                // Calculate text before the position
                string textBefore = _fullText.Substring(0, position);
                int lineBreaksBefore = textBefore.Count(c => c == '\n');
                
                // Calculate character position in line
                int lastLineBreak = textBefore.LastIndexOf('\n');
                int charsInCurrentLine = lastLineBreak >= 0 ? position - lastLineBreak - 1 : position;
                
                // Calculate position of highlight
                double x = charsInCurrentLine * approxCharWidth + 15; // +15 for TextBlock margin
                double y = lineBreaksBefore * approxLineHeight + 15; // +15 for TextBlock margin
                
                // Set size and position
                highlight.Width = length * approxCharWidth;
                highlight.Height = approxLineHeight;
                
                Canvas.SetLeft(highlight, x);
                Canvas.SetTop(highlight, y);
                
                // Add to canvas
                HighlightCanvas.Children.Add(highlight);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error highlighting word: {ex.Message}");
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

                        // Try to find the main content area using heuristics
                        HtmlNode contentNode = FindMainContent(htmlDocument);
                        
                        if (contentNode != null)
                        {
                            string text = contentNode.InnerText;
                            extractedWords = ProcessText(text);
                        }
                        else
                        {
                            // Fallback to body if no content area found
                            var bodyNode = htmlDocument.DocumentNode.SelectSingleNode("//body");
                            if (bodyNode != null)
                            {
                                string text = bodyNode.InnerText;
                                extractedWords = ProcessText(text);
                            }
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

        private HtmlNode FindMainContent(HtmlDocument htmlDocument)
        {
            // Try to find the main content using common patterns
            
            // 1. Look for article elements
            var articles = htmlDocument.DocumentNode.SelectNodes("//article");
            if (articles?.Count > 0)
                return articles[0];
            
            // 2. Look for main element
            var mainElements = htmlDocument.DocumentNode.SelectNodes("//main");
            if (mainElements?.Count > 0)
                return mainElements[0];
            
            // 3. Look for common content class names
            var contentClasses = new[] { "content", "post", "entry", "article", "story", "blog-post" };
            foreach (var className in contentClasses)
            {
                var elements = htmlDocument.DocumentNode.SelectNodes($"//*[contains(@class, '{className}')]");
                if (elements?.Count > 0)
                    return elements[0];
            }
            
            // 4. Look for divs with lots of text
            var divs = htmlDocument.DocumentNode.SelectNodes("//div");
            if (divs != null)
            {
                HtmlNode bestDiv = null;
                int maxTextLength = 0;
                
                foreach (var div in divs)
                {
                    string text = div.InnerText;
                    if (text.Length > maxTextLength && div.SelectNodes(".//p")?.Count >= 3)
                    {
                        maxTextLength = text.Length;
                        bestDiv = div;
                    }
                }
                
                if (bestDiv != null)
                    return bestDiv;
            }
            
            // No suitable content found
            return null;
        }

        private List<string> ProcessText(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return new List<string>();
                }
                
                // Store the full text for context display
                _fullText = text;
                
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
                        MinWidth = 300,
                        MinHeight = 150,
                        Background = new SolidColorBrush(Color.Parse("#252526")),
                        Content = new StackPanel
                        {
                            Margin = new Thickness(20),
                            Children = 
                            {
                                new TextBlock
                                {
                                    Text = message,
                                    Margin = new Thickness(0, 0, 0, 20),
                                    Foreground = new SolidColorBrush(Color.Parse("White")),
                                    TextWrapping = TextWrapping.Wrap
                                },
                                new Button
                                {
                                    Content = "OK",
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                    Width = 100,
                                    Height = 35,
                                    Background = new SolidColorBrush(Color.Parse("#0078D7")),
                                    Foreground = new SolidColorBrush(Color.Parse("White"))
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