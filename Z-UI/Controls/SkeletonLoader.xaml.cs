using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;

namespace ZUI.Controls;

public sealed partial class SkeletonLoader : UserControl
{
    private Storyboard? _storyboard;

    public SkeletonLoader()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(SkeletonLoader),
            new PropertyMetadata(true, OnIsLoadingChanged));

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SkeletonLoader loader)
        {
            if ((bool)e.NewValue)
                loader.StartAnimation();
            else
                loader.StopAnimation();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (IsLoading)
            StartAnimation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAnimation();
    }

    private void StartAnimation()
    {
        if (_storyboard != null) return;

        _storyboard = new Storyboard();
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = -200 });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1500), Value = 400 });
        
        Storyboard.SetTarget(animation, ShimmerTransform);
        Storyboard.SetTargetProperty(animation, "X");
        
        _storyboard.Children.Add(animation);
        _storyboard.RepeatBehavior = RepeatBehavior.Forever;
        _storyboard.Begin();
    }

    private void StopAnimation()
    {
        _storyboard?.Stop();
        _storyboard = null;
    }
}
