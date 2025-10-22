using ReactiveUI.SourceGenerators;

namespace MVLibraryNET.GUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [Reactive] private string _searchText = string.Empty;

    public MainWindowViewModel()
    {
        
    }
}