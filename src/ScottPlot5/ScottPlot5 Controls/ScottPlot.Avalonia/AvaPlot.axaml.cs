using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using ScottPlot.Control;
using SkiaSharp;

namespace ScottPlot.Avalonia;

public partial class AvaPlot : UserControl, IPlotControl
{
    public Plot Plot { get; } = new();

    public Interaction Interaction { get; private set; }

    public GRContext? GRContext => null;

    private readonly List<FilePickerFileType> fileDialogFilters = new()
    {
        FilePickerFileTypes.ImagePng,
        FilePickerFileTypes.ImageJpg,
        new("BMP image") { Patterns = new[] { "*.jpg", "*.jpeg" }, AppleUniformTypeIdentifiers = new[] { "public.bmp" }, MimeTypes = new[] { "image/bmp" } },
        new("WebP image") { Patterns = new[] { "*.jpg", "*.jpeg" }, AppleUniformTypeIdentifiers = new[] { "public.bmp" }, MimeTypes = new[] { "image/bmp" } },
        FilePickerFileTypes.All,
    };

    public AvaPlot()
    {
        InitializeComponent();
        Interaction = new(this);
        Interaction.ContextMenuItems = GetDefaultContextMenuItems();

        Refresh();
    }

    private ContextMenuItem[] GetDefaultContextMenuItems()
    {
        ContextMenuItem saveImage = new() { Label = "Save Image", OnInvoke = OpenSaveImageDialog };
        // TODO: Copying images to the clipboard is still difficult in Avalonia https://github.com/AvaloniaUI/Avalonia/issues/3588

        return new ContextMenuItem[] { saveImage };
    }

    private ContextMenu GetContextMenu()
    {
        List<MenuItem> items = new();
        foreach (var curr in Interaction.ContextMenuItems)
        {
            var menuItem = new MenuItem { Header = curr.Label };
            menuItem.Click += (s, e) => curr.OnInvoke();

            items.Add(menuItem);
        }


        return new()
        {
            Items = items
        };
    }

    private async void OpenSaveImageDialog()
    {
        var topLevel = TopLevel.GetTopLevel(this) ?? throw new NullReferenceException("Invalid TopLevel");
        var filenameTask = topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions() { SuggestedFileName = Interaction.DefaultSaveImageFilename, FileTypeChoices = fileDialogFilters });
        var filename = await filenameTask;

        if (filenameTask.IsFaulted || filename is null || string.IsNullOrEmpty(filename.Name))
            return;

        ImageFormat format = ImageFormatLookup.FromFilePath(filename.Path.AbsolutePath);
        Plot.Save(filename.Path.AbsolutePath, (int)Bounds.Width, (int)Bounds.Height, format);
    }

    public void Replace(Interaction interaction)
    {
        Interaction = interaction;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        SKImageInfo imageInfo = new((int)Bounds.Width, (int)Bounds.Height);

        using var surface = SKSurface.Create(imageInfo);
        if (surface is null)
            return;

        Plot.Render(surface);

        SKImage img = surface.Snapshot();
        SKPixmap pixels = img.ToRasterImage().PeekPixels();
        byte[] bytes = pixels.GetPixelSpan().ToArray();

        using WriteableBitmap bmp = new(
            size: new global::Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height),
            dpi: new Vector(1, 1),
            format: PixelFormat.Bgra8888,
            alphaFormat: AlphaFormat.Unpremul);

        using ILockedFramebuffer buf = bmp.Lock();
        {
            Marshal.Copy(bytes, 0, buf.Address, pixels.BytesSize);
        }

        Rect rect = new(0, 0, Bounds.Width, Bounds.Height);

        context.DrawImage(bmp, rect, rect, BitmapInterpolationMode.HighQuality);
    }

    public void Refresh()
    {
        InvalidateVisual();
    }

    public void ShowContextMenu(Pixel position)
    {
        var manualContextMenu = GetContextMenu();

        // I am fully aware of how janky it is to place the menu in a 1x1 rect, unfortunately the Avalonia docs were down when I wrote this
        manualContextMenu.PlacementRect = new(position.X, position.Y, 1, 1);
        manualContextMenu.Open(this);
    }

    private void OnMouseDown(object sender, PointerPressedEventArgs e)
    {
        Interaction.MouseDown(
            position: e.ToPixel(this),
            button: e.GetCurrentPoint(this).Properties.PointerUpdateKind.ToButton());

        e.Pointer.Capture(this);

        if (e.ClickCount == 2)
        {
            Interaction.DoubleClick();
        }
    }

    private void OnMouseUp(object sender, PointerReleasedEventArgs e)
    {
        Interaction.MouseUp(
            position: e.ToPixel(this),
            button: e.GetCurrentPoint(this).Properties.PointerUpdateKind.ToButton());

        e.Pointer.Capture(null);
    }

    private void OnMouseMove(object sender, PointerEventArgs e)
    {
        Interaction.OnMouseMove(e.ToPixel(this));
    }

    private void OnMouseWheel(object sender, PointerWheelEventArgs e)
    {
        // Avalonia flips the delta vector when shift is held. This is seemingly intentional: https://github.com/AvaloniaUI/Avalonia/pull/7520
        float delta = (float)(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? e.Delta.X : e.Delta.Y);

        if (delta != 0)
        {
            Interaction.MouseWheelVertical(e.ToPixel(this), delta);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        Interaction.KeyDown(e.ToKey());
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        Interaction.KeyUp(e.ToKey());
    }
}
