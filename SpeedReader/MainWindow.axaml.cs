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
using System.Text.RegularExpressions;

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
        
        // Cache for word positions to avoid recalculating
        private Dictionary<int, int> _wordPositionCache = new Dictionary<int, int>();
        
        // Control how often the scroll position updates (higher = less jumping)
        private double _scrollThreshold = 0.33; // Only scroll when position changes by 1/3 of viewport

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
            
            // Make the word highlight appear on top
            WordHighlight.ZIndex = 100;
            
            // Set the initial text opacity and make sure the ScrollViewer is transparent
            ContextTextDisplay.Opacity = 1.0;
            ContextScrollViewer.Background = Brushes.Transparent;
            
            // Set the highlight canvas to appear behind the text
            HighlightCanvas.ZIndex = 0;
            
            // Configure scroll settings for smoother behavior
            // Higher threshold means less frequent updates but smoother scrolling
            _scrollThreshold = 0.33; // Only scroll when position changes by 1/3 of viewport
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
                _wordPositionCache.Clear(); // Clear the word position cache

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
                    _wordPositionCache.Clear(); // Clear the cache when stopping
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
                Dispatcher.UIThread.Post(async () => {
                    if (_currentWordIndex < _words.Count)
                    {
                        // Update the word display with animation
                        UpdateWordDisplay(_words[_currentWordIndex]);
                        
                        // Only update context highlighting if context is visible
                        if (ContextBorder.IsVisible)
                        {
                            // Update context text and highlighting
                            UpdateContextDisplay();
                            
                            // Wait for layout to complete
                            await Task.Delay(1); // Minimal delay to allow UI update
                        }
                        
                        // Update progress and stats
                        ReadingProgress.Value = _currentWordIndex + 1;
                        UpdateStatsDisplay();
                        
                        _currentWordIndex++;
                    }
                    else
                    {
                        _timer.Stop();
                        await ShowMessageAsync("Information", "Reading completed!");
                        await ResetControlsAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(async () => {
                    _timer.Stop();
                    await ShowMessageAsync("Error", $"Error during reading: {ex.Message}");
                    await ResetControlsAsync();
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
                
                // Clear existing highlights
                HighlightCanvas.Children.Clear();
                
                // Find position of current word in the text
                if (_currentWordIndex < _words.Count && !string.IsNullOrEmpty(_fullText))
                {
                    // Get the current word's position
                    int position = GetPositionOfWordInText(_currentWordIndex);
                    if (position >= 0)
                    {
                        // Add highlight for the current word first, before scrolling
                        // This reduces visual jumpiness by showing the highlight in place
                        HighlightCurrentWord(position, _words[_currentWordIndex].Length);
                        
                        // Then scroll to make sure the word is visible
                        ScrollToPosition(position);
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
            
            // Check if this position is already cached
            if (_wordPositionCache.TryGetValue(wordIndex, out int cachedPosition))
            {
                return cachedPosition;
            }
            
            try
            {
                // Get the current word
                string currentWord = _words[wordIndex];
                if (string.IsNullOrEmpty(currentWord))
                    return -1;
                
                int position = -1;
                
                // If this is the word right after the previous one, try to optimize
                if (wordIndex > 0 && _wordPositionCache.TryGetValue(wordIndex - 1, out int prevPosition))
                {
                    // Find the previous word in the text
                    string prevWord = _words[wordIndex - 1];
                    int searchStart = prevPosition + prevWord.Length;
                    
                    // Look for the current word close to the previous one
                    // This covers the common case where words are consecutive
                    int nearPosition = _fullText.IndexOf(currentWord, searchStart, 
                        Math.Min(30, _fullText.Length - searchStart)); // Look within 30 chars
                    
                    if (nearPosition >= 0)
                    {
                        position = nearPosition;
                    }
                }
                
                // If the quick search failed, use the regular approach
                if (position < 0)
                {
                    // For better context, create a phrase with words around the target word
                    // Use a smaller window size to improve performance
                    int windowSize = 2; // Words to include before and after
                    int startWindow = Math.Max(0, wordIndex - windowSize);
                    int endWindow = Math.Min(_words.Count - 1, wordIndex + windowSize);
                    
                    // Build the search pattern
                    StringBuilder searchPhrase = new StringBuilder();
                    
                    // Words before current word
                    for (int i = startWindow; i < wordIndex; i++)
                    {
                        searchPhrase.Append(_words[i]).Append(" ");
                    }
                    
                    // The current word
                    searchPhrase.Append(currentWord);
                    
                    // Words after current word
                    if (wordIndex < endWindow)
                    {
                        searchPhrase.Append(" ");
                        for (int i = wordIndex + 1; i <= endWindow; i++)
                        {
                            searchPhrase.Append(_words[i]);
                            if (i < endWindow) searchPhrase.Append(" ");
                        }
                    }
                    
                    // Try to find the phrase in the text
                    string searchPattern = searchPhrase.ToString();
                    int phrasePos = _fullText.IndexOf(searchPattern);
                    
                    // If the phrase is found, calculate the exact position of the current word
                    if (phrasePos >= 0)
                    {
                        // Calculate the offset to the current word within the phrase
                        int offsetToCurrentWord = 0;
                        for (int i = startWindow; i < wordIndex; i++)
                        {
                            offsetToCurrentWord += _words[i].Length + 1; // +1 for space
                        }
                        
                        position = phrasePos + offsetToCurrentWord;
                    }
                    else if (!string.IsNullOrEmpty(currentWord))
                    {
                        // Fallback: Basic word search
                        // For performance, use string.IndexOf instead of Regex
                        List<int> positions = new List<int>();
                        int pos = 0;
                        
                        // Simple search without regex for better performance
                        while ((pos = _fullText.IndexOf(currentWord, pos)) >= 0)
                        {
                            // Simple check for word boundaries
                            bool isWordStart = pos == 0 || !char.IsLetterOrDigit(_fullText[pos - 1]);
                            bool isWordEnd = pos + currentWord.Length >= _fullText.Length || 
                                !char.IsLetterOrDigit(_fullText[pos + currentWord.Length]);
                            
                            if (isWordStart && isWordEnd)
                                positions.Add(pos);
                            
                            pos += currentWord.Length;
                        }
                        
                        if (positions.Count > 0)
                        {
                            // For performance, use a simpler algorithm to find best match
                            double progress = (double)wordIndex / _words.Count;
                            int expectedPos = (int)(_fullText.Length * progress);
                            
                            // Find closest position with a simple loop
                            int closestPos = positions[0];
                            int closestDiff = Math.Abs(closestPos - expectedPos);
                            
                            for (int i = 1; i < positions.Count; i++)
                            {
                                int diff = Math.Abs(positions[i] - expectedPos);
                                if (diff < closestDiff)
                                {
                                    closestDiff = diff;
                                    closestPos = positions[i];
                                }
                            }
                            
                            position = closestPos;
                        }
                    }
                }
                
                // If all methods failed, use a position based on progress
                if (position < 0)
                {
                    position = (int)(_fullText.Length * ((double)wordIndex / _words.Count));
                }
                
                // Cache the result to avoid recalculating
                _wordPositionCache[wordIndex] = position;
                
                return position;
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
                if (position < 0 || ContextTextDisplay == null || ContextScrollViewer == null) return;
                
                // Ensure position is within text bounds
                position = Math.Min(position, _fullText.Length - 1);
                position = Math.Max(position, 0);
                
                // Find the line number where the position occurs
                string textBeforePosition = _fullText.Substring(0, position);
                int lineNumber = textBeforePosition.Count(c => c == '\n');
                
                // Calculate the vertical position based on line count
                double lineHeight = ContextTextDisplay.FontSize * ContextTextDisplay.LineHeight;
                double lineSpacing = ContextTextDisplay.LineSpacing;
                double verticalPosition = lineNumber * (lineHeight + lineSpacing);
                
                // Get viewport height
                double viewportHeight = ContextScrollViewer.Viewport.Height;
                
                // Center the current word in the viewport - using 1/3 so it's biased toward the top
                double targetOffset = Math.Max(0, verticalPosition - (viewportHeight / 3));
                
                // Get current scroll position
                double currentOffset = ContextScrollViewer.Offset.Y;
                
                // Only scroll if necessary and if the change is significant based on threshold
                if (Math.Abs(targetOffset - currentOffset) > viewportHeight * _scrollThreshold)
                {
                    // Apply scroll
                    ContextScrollViewer.Offset = new Vector(0, targetOffset);
                }
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
                if (length <= 0) length = 1; // Ensure minimum length
                
                // Create a rectangle to highlight the current word
                var highlight = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.Parse("#3078D7")),
                    Opacity = 0.4,
                    RadiusX = 3,
                    RadiusY = 3
                };
                
                // Get accurate measurements from the text display
                double fontSizeInPixels = ContextTextDisplay.FontSize;
                double lineHeight = ContextTextDisplay.LineHeight * fontSizeInPixels;
                double letterSpacing = ContextTextDisplay.LetterSpacing;
                double lineSpacing = ContextTextDisplay.LineSpacing;
                
                // Adjust character width for better highlight accuracy
                double charWidth = (fontSizeInPixels * 0.6) + letterSpacing; 
                
                // Calculate text before the position
                string textBefore = position < _fullText.Length ? _fullText.Substring(0, position) : "";
                
                // Only count actual line breaks to reduce computation
                int lineBreaksBefore = 0;
                int lastNewLine = -1;
                
                // Find the last line break and count total line breaks
                for (int i = 0; i < textBefore.Length; i++)
                {
                    if (textBefore[i] == '\n')
                    {
                        lineBreaksBefore++;
                        lastNewLine = i;
                    }
                }
                
                // Get the last line's text for character position calculation
                int charsInCurrentLine = lastNewLine >= 0 ? 
                    textBefore.Length - (lastNewLine + 1) : 
                    textBefore.Length;
                
                // Calculate position of highlight
                double x = charsInCurrentLine * charWidth + ContextTextDisplay.Margin.Left;
                double y = lineBreaksBefore * (lineHeight + lineSpacing) + ContextTextDisplay.Margin.Top;
                
                // Set size and position
                highlight.Width = Math.Max(length * charWidth, 10); // Ensure minimum width
                highlight.Height = lineHeight;
                
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
                
                // Normalize line breaks for consistent processing
                string normalizedText = text.Replace("\r\n", "\n")
                                           .Replace("\r", "\n");
                
                // Ensure paragraphs are properly formatted by adding extra line breaks
                normalizedText = normalizedText.Replace("\n\n", "\n \n");  // Add a space between empty lines
                
                // Double line breaks for paragraph separation
                normalizedText = normalizedText.Replace("\n", "\n\n");
                
                // Store the full text with normalized line breaks for context display
                _fullText = normalizedText;
                
                // Process text for word extraction
                // Replace tabs with spaces but keep line breaks for word splitting
                string textForSplitting = normalizedText.Replace("\t", " ")
                                                      .Replace("\n", " ");
                
                // Replace multiple spaces with single space
                while (textForSplitting.Contains("  "))
                {
                    textForSplitting = textForSplitting.Replace("  ", " ");
                }
                
                // Split by spaces and filter out empty entries
                string[] words = textForSplitting.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
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