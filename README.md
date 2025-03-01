# Speed Reader

A cross-platform .NET application that displays text from PDFs or web pages one word at a time at a configurable speed.

## Features

- Load text from PDF files or web pages
- Smart content extraction from web pages
- Configure reading speed (words per minute)
- Customize font family and size
- Pause, resume, and stop reading
- Reading progress indicator
- Cross-platform (Windows, macOS, Linux)

## Requirements

- .NET 9.0 SDK or later

## Dependencies

- Avalonia UI (v11.0.7) - Cross-platform UI framework
- itext7 (v8.0.2) - For PDF parsing
- HtmlAgilityPack (v1.11.59) - For web page parsing
- MessageBox.Avalonia (v3.1.5.1) - For dialog boxes

## Getting Started

1. Clone or download this repository
2. Build and run the application:
   ```
   cd SpeedReader
   dotnet run
   ```

## How to Use

1. Select the source type (PDF or Web Page)
2. For PDF: Click "Browse" to select a PDF file, or enter the path manually
3. For Web Pages: Enter the full URL (e.g., https://example.com/article)
   - The application will analyze the page and identify potential content areas
   - You can select which content area to read from
4. Configure the font family, font size, and reading speed (words per minute)
5. Click "Start" to begin reading
6. Use "Pause" to pause reading and "Stop" to stop completely

## Web Page Content Extraction

The application includes a smart content extraction feature for web pages:

1. Automatically identifies main content areas using common patterns
2. Detects articles, main content sections, and text-rich areas
3. Allows you to preview and select which content to read
4. Filters out navigation, headers, footers, and other non-content elements

## Notes

- For web pages, the quality of text extraction depends on the structure of the website. Some websites may not extract cleanly.
- Very large PDF files may take some time to process.

## Future Improvements

- Add text preprocessing to improve readability
- Support for additional file formats (e.g., EPUB, DOCX)
- Visual selection of content regions on web pages
- Save and load reading preferences
- Text highlighting options
- Night mode / dark theme 

<Window Title="Select Content Region">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <!-- WebView with selection overlay -->
        <Panel Grid.Row="0">
            <WebView x:Name="WebPreview" />
            <Canvas x:Name="SelectionOverlay" />
        </Panel>
        
        <!-- Controls -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Center" Margin="10">
            <Button Content="Auto-Detect Content" Margin="5" />
            <Button Content="Clear Selection" Margin="5" />
            <Button Content="Extract Selected" Margin="5" />
            <Button Content="Cancel" Margin="5" />
        </StackPanel>
    </Grid>
</Window> 