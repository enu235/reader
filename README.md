# Speed Reader

A cross-platform .NET application that displays text from PDFs or web pages one word at a time at a configurable speed.

## Features

- Load text from PDF files or web pages
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
4. Configure the font family, font size, and reading speed (words per minute)
5. Click "Start" to begin reading
6. Use "Pause" to pause reading and "Stop" to stop completely

## Notes

- For web pages, the quality of text extraction depends on the structure of the website. Some websites may not extract cleanly.
- Very large PDF files may take some time to process.

## Future Improvements

- Add text preprocessing to improve readability
- Support for additional file formats (e.g., EPUB, DOCX)
- More advanced text extraction from web pages
- Save and load reading preferences
- Text highlighting options
- Night mode / dark theme 