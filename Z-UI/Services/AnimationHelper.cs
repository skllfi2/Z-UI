using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;

namespace ZUI.Services
{
    public static class AnimationHelper
    {
        public static readonly DependencyProperty EnableCardAnimationsProperty =
            DependencyProperty.RegisterAttached(
                "EnableCardAnimations",
                typeof(bool),
                typeof(AnimationHelper),
                new PropertyMetadata(false, OnEnableCardAnimationsChanged));

        public static bool GetEnableCardAnimations(DependencyObject obj)
            => (bool)obj.GetValue(EnableCardAnimationsProperty);

        public static void SetEnableCardAnimations(DependencyObject obj, bool value)
            => obj.SetValue(EnableCardAnimationsProperty, value);

        private static void OnEnableCardAnimationsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Border border) return;
            
            if ((bool)e.NewValue)
            {
                border.PointerEntered += Border_PointerEntered;
                border.PointerExited += Border_PointerExited;
            }
            else
            {
                border.PointerEntered -= Border_PointerEntered;
                border.PointerExited -= Border_PointerExited;
            }
        }

	private static void Border_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (sender is not Border border) return;
		if (!AppSettings.AnimCards) return;

		var element = border as FrameworkElement;
		if (element == null) return;

		var visual = ElementCompositionPreview.GetElementVisual(element);
		visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0f);
		var compositor = visual.Compositor;

		var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
		scaleAnimation.InsertKeyFrame(1f, new Vector3(1.01f, 1.01f, 1.0f));
		scaleAnimation.Duration = TimeSpan.FromMilliseconds(100);

		visual.StartAnimation("Scale", scaleAnimation);
	}

	private static void Border_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (sender is not Border border) return;
		if (!AppSettings.AnimCards) return;

		var element = border as FrameworkElement;
		if (element == null) return;

		var visual = ElementCompositionPreview.GetElementVisual(element);
		var compositor = visual.Compositor;

		var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
		scaleAnimation.InsertKeyFrame(1f, new Vector3(1.0f, 1.0f, 1.0f));
		scaleAnimation.Duration = TimeSpan.FromMilliseconds(100);

		visual.StartAnimation("Scale", scaleAnimation);
	}

        public static readonly DependencyProperty EnableButtonAnimationsProperty =
            DependencyProperty.RegisterAttached(
                "EnableButtonAnimations",
                typeof(bool),
                typeof(AnimationHelper),
                new PropertyMetadata(false, OnEnableButtonAnimationsChanged));

        public static bool GetEnableButtonAnimations(DependencyObject obj)
            => (bool)obj.GetValue(EnableButtonAnimationsProperty);

        public static void SetEnableButtonAnimations(DependencyObject obj, bool value)
            => obj.SetValue(EnableButtonAnimationsProperty, value);

        private static void OnEnableButtonAnimationsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Button button) return;

            if ((bool)e.NewValue)
            {
                button.PointerEntered += Button_PointerEntered;
                button.PointerExited += Button_PointerExited;
                button.PointerPressed += Button_PointerPressed;
                button.PointerReleased += Button_PointerReleased;
            }
            else
            {
                button.PointerEntered -= Button_PointerEntered;
                button.PointerExited -= Button_PointerExited;
                button.PointerPressed -= Button_PointerPressed;
                button.PointerReleased -= Button_PointerReleased;
            }
        }

	private static void Button_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (sender is not Button button) return;
		if (!AppSettings.AnimButtons) return;

		var visual = ElementCompositionPreview.GetElementVisual(button);
		visual.CenterPoint = new Vector3((float)button.ActualWidth / 2, (float)button.ActualHeight / 2, 0f);
		var compositor = visual.Compositor;

		var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
		scaleAnimation.InsertKeyFrame(1f, new Vector3(1.02f, 1.02f, 1.0f));
		scaleAnimation.Duration = TimeSpan.FromMilliseconds(100);

		visual.StartAnimation("Scale", scaleAnimation);
	}

	private static void Button_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (sender is not Button button) return;
		if (!AppSettings.AnimButtons) return;

		var visual = ElementCompositionPreview.GetElementVisual(button);
		var compositor = visual.Compositor;

		var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
		scaleAnimation.InsertKeyFrame(1f, new Vector3(1.0f, 1.0f, 1.0f));
		scaleAnimation.Duration = TimeSpan.FromMilliseconds(100);

		visual.StartAnimation("Scale", scaleAnimation);
	}

	private static void Button_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (sender is not Button button) return;
		if (!AppSettings.AnimButtons) return;

		var visual = ElementCompositionPreview.GetElementVisual(button);
		visual.CenterPoint = new Vector3((float)button.ActualWidth / 2, (float)button.ActualHeight / 2, 0f);
		var compositor = visual.Compositor;

		var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
		scaleAnimation.InsertKeyFrame(1f, new Vector3(0.98f, 0.98f, 1.0f));
		scaleAnimation.Duration = TimeSpan.FromMilliseconds(50);

		visual.StartAnimation("Scale", scaleAnimation);
	}

	private static void Button_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (sender is not Button button) return;
		if (!AppSettings.AnimButtons) return;

		var visual = ElementCompositionPreview.GetElementVisual(button);
		var compositor = visual.Compositor;

		var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
		scaleAnimation.InsertKeyFrame(1f, new Vector3(1.02f, 1.02f, 1.0f));
		scaleAnimation.Duration = TimeSpan.FromMilliseconds(100);

		visual.StartAnimation("Scale", scaleAnimation);
	}
    }
}
