using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using HtmlAgilityPack;

public record ListData(string Name, string Url);

namespace NoiseAnalyzer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            PopulateList();
        }

        async void PopulateList()
        {
            progressBar.Visibility = Visibility.Visible;
            HtmlDocument doc = await new HtmlWeb().LoadFromWebAsync(@"https://pvscmdupload.blob.core.windows.net/reports/allTestHistory/refs/heads/main_arm64_Windows%2010.0.19041/AllTestindex.html");
            var links = doc.DocumentNode.SelectNodes("//a[@href]")
                .Select(link => link.Attributes["href"].Value)
                .Where(link => !link.EndsWith("AllTestindex.html", StringComparison.OrdinalIgnoreCase))
                .ToList();

            ObservableCollection<ListData> data = new();
            foreach (string? link in links)
            {
                string fixedLink = link.Replace(" ", "%20"); // use some API here?
                string benchName = new Uri(fixedLink).Segments.Last();
                benchName = benchName.Substring(benchName.LastIndexOf("%2f", StringComparison.Ordinal) + "%2f".Length);
                data.Add(new ListData(benchName, fixedLink));
            }
            listBox.ItemsSource = data;
            progressBar.Visibility = Visibility.Hidden;
        }

        async void RenderGraph(string link)
        {
            progressBar.Visibility = Visibility.Visible;
            List<DataRecord> data = await ProcessLink(link);

            // Take last 80 measurements:
            data = data.TakeLast(80).ToList();

            // Show them in the text box:
            textBox.Text = string.Join(", ", data.Select(i => i.Y));

            // Plot
            WpfPlot.Plot.Clear();
            WpfPlot.Plot.Add.Scatter(
                xs: data.Select(d => d.X).ToArray(),
                ys: data.Select(d => d.Y).ToArray());

            WpfPlot.Refresh();
            WpfPlot.Plot.Axes.AutoScale();
            progressBar.Visibility = Visibility.Hidden;
        }

        static readonly HttpClient httpClient = new();

        public record DataRecord(DateTime X, double Y, string Commit);
        static async Task<List<DataRecord>> ProcessLink(string link)
        {
            var text = await httpClient.GetStringAsync(link);
            var xAxisMarker = "\"x\": [";
            var yAxisMarker = "\"y\": [";
            var commitMarker = "gitHashruntime: [";
            var indexOfX = text.IndexOf(xAxisMarker) + xAxisMarker.Length;
            var indexOfY = text.IndexOf(yAxisMarker) + yAxisMarker.Length;
            var indexOfCommit = text.IndexOf(commitMarker) + commitMarker.Length;
            var xDataRaw = text.Substring(indexOfX, text.IndexOf("]", indexOfX) - indexOfX);
            var yDataRaw = text.Substring(indexOfY, text.IndexOf("]", indexOfY) - indexOfY);
            var commitDataRaw = text.Substring(indexOfCommit, text.IndexOf("]", indexOfCommit) - indexOfCommit);

            DateTime[] xData = xDataRaw
                .Split(",", StringSplitOptions.RemoveEmptyEntries)
                .Select(i => DateTime.ParseExact(i.Trim('\''), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .ToArray();

            double[] yData = yDataRaw
                .Split(",", StringSplitOptions.RemoveEmptyEntries)
                .Select(double.Parse)
                .ToArray();

            string[] commitData = commitDataRaw
                .Split(",", StringSplitOptions.RemoveEmptyEntries)
                .Select(i => i.Trim('\''))
                .ToArray();

            if (xData.Length != yData.Length || xData.Length != commitData.Length)
                throw new Exception("Data length mismatch");
            if (xData.Length == 0)
                throw new Exception("No data found");

            List<DataRecord> data = new();
            for (int i = 1; i < xData.Length; i++)
                data.Add(new DataRecord(xData[i], yData[i], commitData[i]));

            // It should be already sorted, but just in case:
            data = data.OrderBy(i => i.X).ToList();
            return data;
        }

        private void listBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (listBox.SelectedItem is ListData data)
                RenderGraph(data.Url);
        }
    }
}