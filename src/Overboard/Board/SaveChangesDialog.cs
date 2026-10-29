using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Overboard.Board;

public enum SaveChoice
{
    Cancel,
    Save,
    Discard,
}

/// <summary>Asks whether to save the board before replacing it. Closing the dialog cancels.</summary>
public sealed class SaveChangesDialog : Window
{
    public SaveChangesDialog(string boardName)
    {
        Title = "Overboard";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var save = new Button { Content = "Enregistrer", IsDefault = true, Classes = { "accent" } };
        save.Click += (_, _) => Close(SaveChoice.Save);
        var discard = new Button { Content = "Ne pas enregistrer" };
        discard.Click += (_, _) => Close(SaveChoice.Discard);
        var cancel = new Button { Content = "Annuler", IsCancel = true };
        cancel.Click += (_, _) => Close(SaveChoice.Cancel);

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = $"Enregistrer les modifications de « {boardName} » ?",
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { save, discard, cancel },
                },
            },
        };
    }
}
