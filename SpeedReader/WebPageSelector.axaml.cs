using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HtmlAgilityPack;

namespace SpeedReader
{
    public partial class WebPageSelector : Window
    {
        private string _url;
        private HtmlDocument _htmlDocument;
        private List<ContentElement> _contentElements = new List<ContentElement>();
        private List<string> _extractedWords = new List<string>();
        
        // Event to notify when content is selected
        public event EventHandler<ContentSelectedEventArgs> ContentSelected;
        
        public WebPageSelector()
        {
            InitializeComponent();
            DataContext = this; // Set DataContext for binding
        }
        
        public async Task InitializeAsync(string url)
        {
            _url = url;
            
            // Set the URL text
            await Dispatcher.UIThread.InvokeAsync(() => {
                UrlTextBlock.Text = url;
            });
            
            await LoadWebPageAsync(url);
        }
        
        private async Task LoadWebPageAsync(string url)
        {
            try
            {
                // Show loading indicators
                await Dispatcher.UIThread.InvokeAsync(() => {
                    LoadingProgress.IsVisible = true;
                    LoadingText.IsVisible = true;
                });
                
                // Load the web page content
                using (var httpClient = new HttpClient())
                {
                    string htmlContent = await httpClient.GetStringAsync(url);
                    _htmlDocument = new HtmlDocument();
                    _htmlDocument.LoadHtml(htmlContent);
                    
                    // Identify potential content elements
                    await IdentifyContentElementsAsync();
                    
                    // Hide loading indicators
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        LoadingProgress.IsVisible = false;
                        LoadingText.IsVisible = false;
                        
                        // Enable the Use Selected button if we found content
                        UseSelectedButton.IsEnabled = _contentElements.Count > 0;
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => {
                    LoadingProgress.IsVisible = false;
                    LoadingText.Text = $"Error loading page: {ex.Message}";
                    LoadingText.IsVisible = true;
                });
            }
        }
        
        private async Task IdentifyContentElementsAsync()
        {
            _contentElements.Clear();
            
            if (_htmlDocument == null)
                return;
                
            // Look for common content containers
            await Task.Run(() => {
                // 1. Look for article elements
                var articles = _htmlDocument.DocumentNode.SelectNodes("//article");
                if (articles != null)
                {
                    foreach (var article in articles)
                    {
                        AddContentElement("Article", article);
                    }
                }
                
                // 2. Look for main element
                var mainElements = _htmlDocument.DocumentNode.SelectNodes("//main");
                if (mainElements != null)
                {
                    foreach (var main in mainElements)
                    {
                        AddContentElement("Main Content", main);
                    }
                }
                
                // 3. Look for common content class names
                var contentClasses = new[] { "content", "post", "entry", "article", "story", "blog-post" };
                foreach (var className in contentClasses)
                {
                    var elements = _htmlDocument.DocumentNode.SelectNodes($"//*[contains(@class, '{className}')]");
                    if (elements != null)
                    {
                        foreach (var element in elements)
                        {
                            AddContentElement($"Content ({className})", element);
                        }
                    }
                }
                
                // 4. Look for divs with lots of text
                var divs = _htmlDocument.DocumentNode.SelectNodes("//div");
                if (divs != null)
                {
                    foreach (var div in divs)
                    {
                        // Only consider divs with significant text content
                        string text = div.InnerText;
                        if (text.Length > 500 && div.SelectNodes(".//p")?.Count() >= 3)
                        {
                            AddContentElement("Text-rich Div", div);
                        }
                    }
                }
                
                // 5. If nothing found, use body as fallback
                if (_contentElements.Count == 0)
                {
                    var body = _htmlDocument.DocumentNode.SelectSingleNode("//body");
                    if (body != null)
                    {
                        AddContentElement("Full Page", body);
                    }
                }
            });
            
            // Update the UI with the found content elements
            await Dispatcher.UIThread.InvokeAsync(() => {
                ContentElementsComboBox.Items.Clear();
                foreach (var element in _contentElements)
                {
                    ContentElementsComboBox.Items.Add(element.Description);
                }
                
                if (ContentElementsComboBox.Items.Count > 0)
                {
                    ContentElementsComboBox.SelectedIndex = 0;
                    UpdateExtractedTextPreview();
                }
            });
        }
        
        private void AddContentElement(string description, HtmlNode node)
        {
            // Skip if node is null or has no text
            if (node == null || string.IsNullOrWhiteSpace(node.InnerText))
                return;
                
            // Skip if this node is a child of an already added node
            foreach (var element in _contentElements)
            {
                if (IsDescendantOf(node, element.Node))
                    return;
            }
            
            // Add the content element
            _contentElements.Add(new ContentElement
            {
                Description = description,
                Node = node,
                Text = CleanText(node.InnerText)
            });
        }
        
        private bool IsDescendantOf(HtmlNode node, HtmlNode potentialAncestor)
        {
            var parent = node.ParentNode;
            while (parent != null)
            {
                if (parent == potentialAncestor)
                    return true;
                parent = parent.ParentNode;
            }
            return false;
        }
        
        private string CleanText(string text)
        {
            // Basic cleaning of HTML text
            return text.Replace("\n", " ")
                      .Replace("\r", " ")
                      .Replace("\t", " ")
                      .Replace("  ", " ")
                      .Trim();
        }
        
        private void UpdateExtractedTextPreview()
        {
            int selectedIndex = ContentElementsComboBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _contentElements.Count)
            {
                var selectedElement = _contentElements[selectedIndex];
                ExtractedTextPreview.Text = selectedElement.Text;
                
                // Process the text into words
                _extractedWords = ProcessText(selectedElement.Text);
            }
            else
            {
                ExtractedTextPreview.Text = "No content selected";
                _extractedWords.Clear();
            }
        }
        
        private List<string> ProcessText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();
                
            // Clean and split the text into words
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
        
        private void ContentElementsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateExtractedTextPreview();
        }
        
        private async void RefreshContentButton_Click(object sender, RoutedEventArgs e)
        {
            await IdentifyContentElementsAsync();
        }
        
        private async void AutoDetectButton_Click(object sender, RoutedEventArgs e)
        {
            // Find the best content element (usually the first one is best)
            if (_contentElements.Count > 0)
            {
                ContentElementsComboBox.SelectedIndex = 0;
                UpdateExtractedTextPreview();
            }
            else
            {
                await IdentifyContentElementsAsync();
            }
        }
        
        private void UseSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            // Notify that content has been selected
            if (_extractedWords.Count > 0)
            {
                ContentSelected?.Invoke(this, new ContentSelectedEventArgs
                {
                    Words = _extractedWords
                });
                
                Close();
            }
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
    
    public class ContentElement
    {
        public string Description { get; set; }
        public HtmlNode Node { get; set; }
        public string Text { get; set; }
    }
    
    public class ContentSelectedEventArgs : EventArgs
    {
        public List<string> Words { get; set; }
    }
} 