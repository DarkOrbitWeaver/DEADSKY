using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DEADSKY.Audio;

namespace DEADSKY.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool _isCommsDrawerOpen = true;
    [ObservableProperty] private double _commsDrawerWidth = 460;

    public string CommsDrawerToggleText => IsCommsDrawerOpen ? ">" : "<";
    public bool HasUnreadComms => Sim.Comms.TotalUnread > 0;
    public string CommsUnreadBadgeText => Sim.Comms.TotalUnread > 9
        ? "9+"
        : Sim.Comms.TotalUnread.ToString(CultureInfo.InvariantCulture);

    partial void OnIsCommsDrawerOpenChanged(bool value)
    {
        CommsDrawerWidth = value ? 460 : 44;
        OnPropertyChanged(nameof(CommsDrawerToggleText));
        OnPropertyChanged(nameof(HasUnreadComms));
        OnPropertyChanged(nameof(CommsUnreadBadgeText));
        MarkVisibleCommsAsRead();
    }

    [RelayCommand]
    private void ToggleCommsDrawer()
    {
        IsCommsDrawerOpen = !IsCommsDrawerOpen;
        Audio.Play(SoundEvent.UiMenuOpen);
    }
}
