using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Protocol;
using BaGet.Protocol.Models;
using Microsoft.DotNet.Interactive.Utility;
using NStack;
using Terminal.Gui;

namespace Nugs
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Application.Run<App>();
        }
    }

    public class App : Window
    {
        private readonly NuGetClient _client;
        private IReadOnlyList<SearchResult> _items;

        private readonly Label _search;
        private readonly TextField _searchText;
        private readonly ListView _searchList;

        public App() : base("Nugs")
        {
            _client = new NuGetClient("https://api.nuget.org/v3/index.json");
            _items = null;

            Width = Dim.Fill();
            Height = Dim.Fill();

            _search = new Label ("Search: ") { X = 3, Y = 1 };
            _searchText = new TextField("")
            {
                X = Pos.Right(_search),
                Y = Pos.Top(_search),
                Width = 40,
            };

            _searchText.Changed += SearchText_Changed;

            _searchList = new ListView(new string[0])
            {
                X = Pos.Left(_search),
                Y = Pos.Top(_search) + 3,
                Width = Dim.Fill(),
            };

            Add(_search, _searchText, _searchList);
            FocusLast();
        }

#region Key handlers
        public override bool ProcessHotKey(KeyEvent keyEvent)
        {
            if (base.ProcessHotKey(keyEvent)) return true;
            if (_searchText.ProcessHotKey(keyEvent)) return true;

            return false;
        }

        public override bool ProcessKey(KeyEvent keyEvent)
        {
            if (base.ProcessKey(keyEvent)) return true;
            if (_searchText.ProcessKey(keyEvent)) return true;

            if (_searchList.HasFocus && keyEvent.Key == Key.Enter && _items != null)
            {
                var item = _items[_searchList.SelectedItem];

                Application.Run(new InstallationDialog(item));
            }

            return false;
        }

        public override bool ProcessColdKey(KeyEvent keyEvent)
        {
            if (base.ProcessColdKey(keyEvent)) return true;
            if (_searchText.ProcessColdKey(keyEvent)) return true;

            return false;
        }
#endregion

        private async void SearchText_Changed(object sender, ustring e)
        {
            await OnSearchAsync();
        }

        private async Task OnSearchAsync()
        {
            // TODO: Cancel the previous search if it exists.
            _searchList.SetSource(new string[] { "..." });
            _items = null;

            var text = _searchText.Text;

            await Task.Delay(TimeSpan.FromMilliseconds(200));
            if (_searchText.Text != text)
            {
                return;
            }

            var results = await _client.SearchAsync(text.ToString(), includePrerelease: true);
            if (_searchText.Text != text)
            {
                return;
            }

            _items = results;
            _searchList.SetSource(_items.Select(i => i.PackageId).ToList());
        }
    }

    public class InstallationDialog : Dialog
    {
        private static readonly string[] MagnitudeAbbreviations = new string[] { "", "k", "M", "B", "T", "q", "Q", "s", "S", "o", "n" };

        public InstallationDialog(SearchResult item)
            : base(item.PackageId, width: 80, height: 12)
        {
            var cancel = new Button(3, 14, "Cancel")
            { 
                Clicked = () => Application.RequestStop()
            };
            var install = new Button(10, 14, "Install")
            {
                Clicked = () =>
                {
                    var resultText = new TextView
                    {
                        Text = "Installing...",
                        ReadOnly = true,
                        Height = 15
                    };

                    var resultDialog = new Dialog(
                        "Install package",
                        width: 80,
                        height: 30,
                        new Button("Ok")
                        {
                            Clicked = () => Application.RequestStop()
                        });

                    resultDialog.Add(resultText);

                    Application.MainLoop.Invoke(async () =>
                    {
                        var dotnet = new Dotnet();
                        var result = await dotnet.AddPackage(item.PackageId, item.Version);
                        
                        resultText.Text = string.Join("\n", result.Output);
                    });

                    Application.Run(resultDialog);
                    Application.RequestStop();
                }
            };

            var details = $"Downloads: {FormatDownloads(item.TotalDownloads)}\n";
            details += $"Tags: {string.Join(", ", item.Tags)}\n";
            details += $"\n";
            details += $"Description:\n";
            details += item.Description;

            var text = new TextView
            {
                Text = details,
                ReadOnly = true,
                Height = 5
            };

            AddButton(cancel);
            AddButton(install);
            Add(text);
        }

        // https://github.com/NuGet/NuGetGallery/blob/e8acf7d79ebaf19e561565a865f9be14b99a5f36/src/NuGetGallery/ViewModels/StatisticsPackagesViewModel.cs#L145
        private string FormatDownloads(double number, int sigFigures = 3)
        {
            var numDiv = 0;

            while (number >= 1000)
            {
                number /= 1000;
                numDiv++;
            }

            // Find a rounding factor based on size, and round to sigFigures, e.g. for 3 sig figs, 1.774545 becomes 1.77.
            var placeValues = Math.Ceiling(Math.Log10(number));
            var roundingFactor = Math.Pow(10, sigFigures - placeValues);
            var roundedNum = Math.Round(number * roundingFactor) / roundingFactor;

            // Pad from right with zeroes to sigFigures length, so for 3 sig figs, 1.6 becomes 1.60
            var formattedNum = roundedNum.ToString("F" + sigFigures);
            var desiredLength = formattedNum.Contains('.') ? sigFigures + 1 : sigFigures;
            if (formattedNum.Length > desiredLength)
            {
                formattedNum = formattedNum.Substring(0, desiredLength);
            }

            formattedNum = formattedNum.TrimEnd('.');

            if (numDiv >= MagnitudeAbbreviations.Length)
            {
                return formattedNum + $"10^{numDiv*3}";
            }
            
            return formattedNum + MagnitudeAbbreviations[numDiv];
        }
    }
}
