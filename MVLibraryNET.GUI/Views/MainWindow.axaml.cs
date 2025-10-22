using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Input;
using MVLibraryNET.GUI.ViewModels;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using ShadUI;

namespace MVLibraryNET.GUI.Views;

[IViewFor<MainWindowViewModel>]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        this.WhenActivated(_ =>
        {
            ViewModel!.SelectOutputDir.RegisterHandler(HandleSelectOutputDir);
        });
        
        AddHandler(DragDrop.DropEvent, OnDropHandler);
    }

    private async Task HandleSelectOutputDir(IInteractionContext<Unit, string?> obj)
    {
        var selectDir =
            await StorageProvider.OpenFolderPickerAsync(new() { Title = "Select Output Folder" });
        
        obj.SetOutput(selectDir.FirstOrDefault()?.Path.LocalPath);
    }

    private void OnDropHandler(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile() is { } file)
        {
            ViewModel!.LoadFile(file.Path.LocalPath);
        }
    }
}