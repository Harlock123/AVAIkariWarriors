using Avalonia.Controls;
using AVAIkariWarriors.Views;

namespace AVAIkariWarriors;

public partial class MainWindow : Window
{
    public MainWindow() : this([]) { }

    public MainWindow(string[] args)
    {
        InitializeComponent();
        Content = new GameCanvas(silent: args.Contains("--no-audio"));
    }
}
