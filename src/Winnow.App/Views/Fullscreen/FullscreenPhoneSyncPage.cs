using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Phone sync from the couch: the switch, a pairing QR code and paired phones,
/// over the same view model as the desktop card.</summary>
public sealed class FullscreenPhoneSyncPage : FullscreenPage
{
    private readonly PhoneSyncViewModel _model;
    private bool _renderPending;
    private bool _disposed;
    private TextBlock? _expiry;
    public override string Title => "Phone sync";

    public FullscreenPhoneSyncPage(FullscreenContext context) : base(context)
    {
        _model = context.Shared.ApplicationSettings.PhoneSync;
        _model.PropertyChanged += ModelChanged;
        _model.Devices.CollectionChanged += DevicesChanged;
        Render();
        AttachedToVisualTree += (_, _) => _ = _model.RefreshAsync();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The countdown ticks every second; only its line changes.
        if (e.PropertyName == nameof(PhoneSyncViewModel.PairingExpiry) && _expiry is not null) _expiry.Text = _model.PairingExpiry;
        else ScheduleRender();
    }
    private void DevicesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRender();

    // A status refresh changes several properties at once; rebuild once after they settle.
    private void ScheduleRender()
    {
        if (_renderPending || _disposed) return;
        _renderPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _renderPending = false;
            if (_disposed) return;
            var restore = PreserveFocus();
            Render();
            restore();
        }, DispatcherPriority.Background);
    }

    private void Render()
    {
        var content = FullscreenInformation.Column();
        content.Children.Add(FullscreenUi.Text(Title, 64));
        content.Children.Add(FullscreenInformation.Text(_model.Intro));

        var toggle = Button((_model.Enabled ? "Turn off phone sync" : "Turn on phone sync"), () => _model.Enabled = !_model.Enabled);
        toggle.Name = "PhoneSyncToggle";
        toggle.IsEnabled = !_model.IsBusy;
        content.Children.Add(toggle);
        content.Children.Add(Live(FullscreenInformation.Text(_model.Status)));
        if (_model.Address.Length > 0) content.Children.Add(FullscreenInformation.Metadata("Address: " + _model.Address));
        if (_model.Problem is { } problem) content.Children.Add(Live(FullscreenInformation.Text(problem, 24, "AmberForeground")));

        FullscreenInformation.AddSection(content, "Pair a phone");
        content.Children.Add(FullscreenInformation.Text("In Winnow Deck, open Sync, choose Pair with Winnow and scan the code. Each code works once, for five minutes."));
        var pairingRow = new List<Control>();
        _expiry = null;
        if (_model.IsPairing)
        {
            var pairing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 32 };
            pairing.Children.Add(new QrCodeView { Name = "PhoneSyncQr", Payload = _model.QrPayload, Width = 320, Height = 320 });
            var details = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(FullscreenInformation.Metadata("Pairing code"));
            var code = FullscreenInformation.Text(_model.PairingCode, 32);
            code.Name = "PhoneSyncCode";
            code[!TextBlock.FontFamilyProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("DataFont");
            details.Children.Add(code);
            var expiry = FullscreenInformation.Metadata(_model.PairingExpiry);
            expiry.Name = "PhoneSyncExpiry";
            _expiry = expiry;
            details.Children.Add(expiry);
            pairing.Children.Add(details);
            content.Children.Add(pairing);
            pairingRow.Add(Command("New code", _model.StartPairingCommand));
            pairingRow.Add(Command("Stop pairing", _model.StopPairingCommand));
        }
        else pairingRow.Add(Command("Start pairing", _model.StartPairingCommand));
        if (_model.PairingNote is { } note) content.Children.Add(Live(FullscreenInformation.Text(note)));
        var pairingActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var action in pairingRow) pairingActions.Children.Add(action);
        content.Children.Add(pairingActions);

        FullscreenInformation.AddSection(content, "Paired phones");
        var removes = new List<Control[]>();
        if (_model.Devices.Count == 0) content.Children.Add(FullscreenInformation.Metadata("No phones are paired."));
        foreach (var device in _model.Devices)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24 };
            var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(FullscreenInformation.Text(device.Name));
            text.Children.Add(FullscreenInformation.Metadata(device.Detail));
            line.Children.Add(text);
            var remove = Button("Remove", () => _model.RemoveDeviceCommand.Execute(device));
            AutomationProperties.SetName(remove, "Remove " + device.Name);
            AutomationProperties.SetAutomationId(remove, "remove:" + device.Id);
            remove.IsEnabled = _model.RemoveDeviceCommand.CanExecute(device);
            Grid.SetColumn(remove, 1);
            line.Children.Add(remove);
            content.Children.Add(line);
            removes.Add([remove]);
        }
        if (_model.Devices.Count > 0)
            content.Children.Add(FullscreenInformation.Metadata("A removed phone can no longer sync. Pair it again to bring it back."));

        content.Children.Add(FullscreenInformation.Rule());
        var back = Button("Back", Context.Back);
        content.Children.Add(back);
        Content = FullscreenUi.Scroll(content);
        SetFocusRows([[toggle], [.. pairingRow], .. removes, [back]]);
    }

    private static Button Button(string label, Action action)
    {
        var button = FullscreenUi.Button(label, action);
        button.FontSize = 24;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }

    private static Button Command(string label, CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command)
    {
        var button = Button(label, () => { });
        button.Command = command;
        return button;
    }

    private static TextBlock Live(TextBlock text)
    {
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        return text;
    }

    public override void Dispose()
    {
        _disposed = true;
        _model.PropertyChanged -= ModelChanged;
        _model.Devices.CollectionChanged -= DevicesChanged;
        base.Dispose();
    }
}
