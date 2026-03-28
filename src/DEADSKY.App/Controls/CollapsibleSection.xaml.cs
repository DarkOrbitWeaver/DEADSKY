using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DEADSKY.App.Controls;

/// <summary>
/// Custom collapsible panel that matches the DEADSKY military dark theme.
/// Provides smooth 250ms ease-in-out height animation and chevron rotation.
/// </summary>
public partial class CollapsibleSection : UserControl
{
    // ── Dependency Properties ────────────────────────────────────────────

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(CollapsibleSection),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(CollapsibleSection),
            new FrameworkPropertyMetadata(true,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnIsExpandedChanged));

    public static readonly DependencyProperty SectionContentProperty =
        DependencyProperty.Register(nameof(SectionContent), typeof(object), typeof(CollapsibleSection),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SectionIdProperty =
        DependencyProperty.Register(nameof(SectionId), typeof(string), typeof(CollapsibleSection),
            new PropertyMetadata(string.Empty));

    // ── CLR Wrappers ─────────────────────────────────────────────────────

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public object SectionContent
    {
        get => GetValue(SectionContentProperty);
        set => SetValue(SectionContentProperty, value);
    }

    public string SectionId
    {
        get => (string)GetValue(SectionIdProperty);
        set => SetValue(SectionIdProperty, value);
    }

    // ── Constructor ──────────────────────────────────────────────────────

    public CollapsibleSection()
    {
        InitializeComponent();
    }

    // ── Animation ────────────────────────────────────────────────────────

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] OnIsExpandedChanged: {e.OldValue} -> {e.NewValue}");
            if (d is CollapsibleSection ctrl)
            {
                System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] Calling ApplyExpandedState for section: {ctrl.SectionId}");
                ctrl.ApplyExpandedState((bool)e.NewValue, animate: true);
                System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] ApplyExpandedState completed for section: {ctrl.SectionId}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] ERROR in OnIsExpandedChanged: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private void ApplyExpandedState(bool expanded, bool animate)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] ApplyExpandedState START - Section: {SectionId}, Expanded: {expanded}, Animate: {animate}");
            
            // ── CRITICAL: Stop all animations and layout updates immediately when collapsing ─────
            if (!expanded)
            {
                System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] COLLAPSING - Disabling content immediately");
                
                // Stop all animations on ContentBorder
                ContentBorder.BeginAnimation(Border.MaxHeightProperty, null);
                ContentBorder.BeginAnimation(Border.HeightProperty, null);
                ContentBorder.BeginAnimation(Border.OpacityProperty, null);
                
                // Disable layout updates by setting IsEnabled = false
                ContentBorder.IsEnabled = false;
                
                // Hide immediately
                ContentBorder.Visibility = Visibility.Collapsed;
                
                // Rotate chevron
                ChevronRotate.Angle = 180;
                
                System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] Content disabled and hidden");
                return;
            }
            
            // ── EXPANDING ─────────────────────────────────────────────────────
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] EXPANDING - Enabling content");
            
            var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
            const double duration = 10; // 10ms = instant feel

            // Re-enable layout
            ContentBorder.IsEnabled = true;
            ContentBorder.Visibility = Visibility.Visible;

            // ── Chevron rotation ─────────────────────────────────────────────
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] Animating chevron...");
            if (animate)
            {
                var rotAnim = new DoubleAnimation(0,
                    TimeSpan.FromMilliseconds(duration))
                { EasingFunction = easing };
                ChevronRotate.BeginAnimation(RotateTransform.AngleProperty, rotAnim);
            }
            else
            {
                ChevronRotate.Angle = 0;
            }
            
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] ApplyExpandedState END - Section: {SectionId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] ERROR in ApplyExpandedState: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] Stack trace: {ex.StackTrace}");
            
            // CRITICAL: Never crash - just hide the content
            try
            {
                ContentBorder.Visibility = Visibility.Collapsed;
                ContentBorder.IsEnabled = false;
            }
            catch { /* ignore */ }
        }
    }

    // ── Header click ─────────────────────────────────────────────────────

    private void Header_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] Header clicked for section: {SectionId}, Current IsExpanded: {IsExpanded}");
            IsExpanded = !IsExpanded;
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] IsExpanded toggled to: {IsExpanded}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] ERROR in Header_MouseLeftButtonUp: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    // ── Loaded — apply initial state without animation ───────────────────

    protected override void OnInitialized(EventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] OnInitialized for section: {SectionId}");
            base.OnInitialized(e);
            Loaded += (_, _) =>
            {
                System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] Loaded event for section: {SectionId}, IsExpanded: {IsExpanded}");
                ApplyExpandedState(IsExpanded, animate: false);
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] ERROR in OnInitialized: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[CollapsibleSection] Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}
