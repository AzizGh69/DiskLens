using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using DiskLens.Mvvm;

namespace DiskLens.ViewModels;

/// <summary>One "quick answer" card: the heaviest file / folder / app / system item on a drive.</summary>
public sealed class AnswerCardViewModel : ObservableObject
{
    private string _value = "—";
    private string _name = "";
    private string _detail = "";
    private string _emptyText = "";
    private bool _hasValue;
    private IReadOnlyList<string> _runnersUp = new List<string>();
    private string _actionText = "";
    private ICommand? _action;

    public AnswerCardViewModel(string caption, string glyph)
    {
        Caption = caption;
        Glyph = glyph;
    }

    public string Caption { get; }
    public string Glyph { get; }

    public string Value { get => _value; private set => SetProperty(ref _value, value); }
    public string Name { get => _name; private set => SetProperty(ref _name, value); }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public string EmptyText { get => _emptyText; private set => SetProperty(ref _emptyText, value); }
    public bool HasValue { get => _hasValue; private set => SetProperty(ref _hasValue, value); }
    public IReadOnlyList<string> RunnersUp { get => _runnersUp; private set => SetProperty(ref _runnersUp, value); }
    public string ActionText { get => _actionText; private set => SetProperty(ref _actionText, value); }
    public ICommand? Action { get => _action; private set { SetProperty(ref _action, value); OnPropertyChanged(nameof(HasAction)); } }
    public bool HasAction => Action is not null;

    public void Set(string value, string name, string detail, IEnumerable<string> runnersUp, string actionText, ICommand? action)
    {
        Value = value;
        Name = name;
        Detail = detail;
        RunnersUp = runnersUp.ToList();
        ActionText = actionText;
        Action = action;
        HasValue = true;
    }

    public void Clear(string emptyText)
    {
        Value = "—";
        Name = "";
        Detail = "";
        RunnersUp = new List<string>();
        Action = null;
        EmptyText = emptyText;
        HasValue = false;
    }
}
