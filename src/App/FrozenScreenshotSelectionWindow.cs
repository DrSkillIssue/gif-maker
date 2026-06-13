using GifMaker.Core;
using GifMaker.Screenshot;
using GifMaker.X11;
using Gtk;

namespace GifMaker.App;

internal sealed class FrozenScreenshotSelectionWindow : Window
{
    public const string SelectionCancelled = "Selection cancelled";

    private const uint EscapeKey = 0xff1b;
    private const string TransparentSurfaceCss = """
        #gifmaker-frozen-selection-window,
        #gifmaker-frozen-selection-area {
            background: transparent;
        }
        """;

    private readonly FrozenScreenshot _frame;
    private readonly DrawingArea _drawingArea;
    private readonly TaskCompletionSource<Result<ScreenRegion>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly X11Desktop _desktop = new();
    private readonly CssProvider _cssProvider;
    private SelectionDrag? _drag;
    private int _disposed;

    public FrozenScreenshotSelectionWindow(Application application, FrozenScreenshot frame)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
        Application = application;
        Title = "Select screenshot area";
        Decorated = false;
        Resizable = false;
        SetDefaultSize(frame.Bounds.Width, frame.Bounds.Height);
        Name = "gifmaker-frozen-selection-window";
        _cssProvider = CssProvider.New();
        _cssProvider.LoadFromString(TransparentSurfaceCss);
        StyleContext.AddProviderForDisplay(
            GetDisplay(),
            _cssProvider,
            (uint)Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);

        _drawingArea = DrawingArea.New();
        _drawingArea.Name = "gifmaker-frozen-selection-area";
        _drawingArea.ContentWidth = frame.Bounds.Width;
        _drawingArea.ContentHeight = frame.Bounds.Height;
        _drawingArea.SetCursor(Gdk.Cursor.NewFromName("crosshair", null));
        _drawingArea.SetDrawFunc(Draw);

        var drag = GestureDrag.New();
        drag.OnDragBegin += (_, args) =>
        {
            var start = Clamp(args.StartX, args.StartY);
            _drag = new SelectionDrag(start, start);
            _drawingArea.QueueDraw();
        };
        drag.OnDragUpdate += (_, args) =>
        {
            if (_drag is not { } active)
                return;

            _drag = active with { Current = Clamp(active.Start.X + args.OffsetX, active.Start.Y + args.OffsetY) };
            _drawingArea.QueueDraw();
        };
        drag.OnDragEnd += (_, args) =>
        {
            if (_drag is not { } active)
            {
                Complete(Result<ScreenRegion>.Fail(SelectionCancelled));
                return;
            }

            var end = Clamp(active.Start.X + args.OffsetX, active.Start.Y + args.OffsetY);
            Complete(CreateRegion(active.Start, end));
        };
        _drawingArea.AddController(drag);

        var key = EventControllerKey.New();
        key.OnKeyPressed += (_, args) =>
        {
            if (args.Keyval != EscapeKey)
                return false;

            Complete(Result<ScreenRegion>.Fail(SelectionCancelled));
            return true;
        };
        AddController(key);

        Child = _drawingArea;
        OnShow += MoveToFrameOrigin;
        OnCloseRequest += (_, _) =>
        {
            Complete(Result<ScreenRegion>.Fail(SelectionCancelled));
            return true;
        };
    }

    public async Task<Result<ScreenRegion>> SelectAsync(CancellationToken ct)
    {
        using var registration = ct.Register(() =>
            GLib.Functions.IdleAdd(0, () =>
            {
                Complete(Result<ScreenRegion>.Fail(SelectionCancelled));
                return false;
            }));

        Present();
        return await _completion.Task;
    }

    public new void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        OnShow -= MoveToFrameOrigin;
        StyleContext.RemoveProviderForDisplay(GetDisplay(), _cssProvider);
        _cssProvider.Dispose();
        Destroy();
        base.Dispose();
    }

    private void MoveToFrameOrigin(Widget sender, EventArgs args)
    {
        var surface = GetSurface();
        if (surface is null)
            return;

        _desktop.MoveWindow(surface, new ScreenPoint(_frame.Bounds.X, _frame.Bounds.Y)).Match(
            () => { },
            _ => { });
    }

    private void Draw(DrawingArea drawingArea, Cairo.Context cr, int width, int height)
    {
        cr.Operator = Cairo.Operator.Clear;
        cr.Rectangle(0, 0, width, height);
        cr.Fill();
        cr.Operator = Cairo.Operator.Over;

        if (_drag is not { } active)
            return;

        var rect = ToRectangle(active.Start, active.Current);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        cr.Save();
        cr.Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
        cr.Clip();
        cr.SetSourceSurface(_frame.Image, 0, 0);
        cr.Paint();
        cr.Restore();

        cr.LineWidth = 1;
        cr.SetSourceRgb(1, 1, 1);
        cr.Rectangle(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1);
        cr.Stroke();

        if (rect.Width > 3 && rect.Height > 3)
        {
            cr.SetSourceRgb(0.16, 0.45, 0.95);
            cr.Rectangle(rect.X + 1.5, rect.Y + 1.5, rect.Width - 3, rect.Height - 3);
            cr.Stroke();
        }
    }

    private void Complete(Result<ScreenRegion> result)
    {
        if (!_completion.TrySetResult(result))
            return;

        Hide();
    }

    private Result<ScreenRegion> CreateRegion(SelectionPoint start, SelectionPoint end)
    {
        var rect = ToRectangle(start, end);
        if (rect.Width <= 0 || rect.Height <= 0)
            return Result<ScreenRegion>.Fail(SelectionCancelled);

        return ScreenRegion.Create(
            _frame.Bounds.X + rect.X,
            _frame.Bounds.Y + rect.Y,
            rect.Width,
            rect.Height);
    }

    private SelectionPoint Clamp(double x, double y) =>
        new(
            Math.Clamp((int)Math.Round(x), 0, _frame.Bounds.Width),
            Math.Clamp((int)Math.Round(y), 0, _frame.Bounds.Height));

    private static SelectionRectangle ToRectangle(SelectionPoint start, SelectionPoint end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        return new SelectionRectangle(
            x,
            y,
            Math.Abs(end.X - start.X),
            Math.Abs(end.Y - start.Y));
    }

    private readonly record struct SelectionPoint(int X, int Y);

    private readonly record struct SelectionRectangle(int X, int Y, int Width, int Height);

    private sealed record SelectionDrag(SelectionPoint Start, SelectionPoint Current);
}
