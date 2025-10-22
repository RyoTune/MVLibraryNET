using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading.Tasks;
using MVLibraryNET.MVGL;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace MVLibraryNET.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IActivatableViewModel
{
    [Reactive] private string _searchText = string.Empty;
    [Reactive] private List<MvglFileItem> _fileItems = [];
    [Reactive] private StatusInfo _status = new("Drag and Drop a File", 0, 0, 0);
    [Reactive] private string _message = string.Empty;

    private MvglReader? _mvgl;
    private MvglFile[] _files = [];

    public MainWindowViewModel()
    {
        _searchResults = this.WhenAnyValue(vm => vm.SearchText, vm => vm.FileItems)
            .Throttle(TimeSpan.FromMilliseconds(250), RxApp.MainThreadScheduler)
            .Select(tuple => Observable.Start(() =>
            {
                var (search, files) = tuple;
                if (string.IsNullOrEmpty(search))
                    return files;
                return files.Where(x =>
                    x.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            }, RxApp.TaskpoolScheduler))
            .Switch() // avoids overlapping work
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, vm => vm.SearchResults);
        
        this.WhenActivated(disp =>
        {
            _searchResults .DisposeWith(disp);
        });
    }

    private readonly ObservableAsPropertyHelper<IEnumerable<MvglFileItem>> _searchResults;
    public IEnumerable<MvglFileItem> SearchResults => _searchResults.Value;

    public Interaction<Unit, string?> SelectOutputDir { get; } = new();

    [ReactiveCommand]
    private async Task ExtractFiles(IList files)
    {
        var outputDir = await SelectOutputDir.Handle(new());
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir)) return;

        var sw = new Stopwatch();
        double totalBytes = 0;
        
        sw.Start();
        foreach (var file in files.Cast<MvglFileItem>())
        {
            var outputFile = Path.Join(outputDir, file.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

            var data = _mvgl!.ExtractFile(_files[file.FileIdx]).Span;
            totalBytes += data.Length;
            
            await File.WriteAllBytesAsync(outputFile, data.ToArray());
        }
        sw.Stop();

        Message = $"Extracted {files.Count} file(s) in {sw.ElapsedMilliseconds}ms (({totalBytes / 1024 / 1024:F2}MBs)).";
    }
    
    public void LoadFile(string file)
    {
        if (file.EndsWith(".mvgl", StringComparison.OrdinalIgnoreCase))
        {
            _mvgl?.Dispose();
            var stopwatch = new Stopwatch();
            
            stopwatch.Start();
            _mvgl = new(File.OpenRead(file), true);
            _files = _mvgl.GetFiles();
            stopwatch.Stop();
            
            FileItems = _files.Select((x, i) => new MvglFileItem(i, x.FileName, (float)x.ExtractSize / 1024 / 1024)).OrderBy(x => x.FileName).ToList();
            Status = new(file, FileItems.Count, _files.Select(x => (float)x.ExtractSize).Sum() / 1024 / 1024, stopwatch.ElapsedMilliseconds);
        }
    }

    public ViewModelActivator Activator { get; } = new();

    public record MvglFileItem(int FileIdx, string FileName, float TotalSizeMbs);

    public record StatusInfo(string InputMvglFile, int NumFiles, float TotalSizeMb, long LoadedMs);
}