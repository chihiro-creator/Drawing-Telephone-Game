namespace DrawingGame.Shared;

/// <summary>
/// F11で全画面表示⇔通常表示を切り替えるヘルパー。
/// </summary>
public static class FullScreen
{
    private static readonly Dictionary<Form, Rectangle> SavedBounds = new();

    public static void Attach(Form form)
    {
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F11)
            {
                e.Handled = true;
                Toggle(form);
            }
        };
    }

    public static void Toggle(Form form)
    {
        if (form.FormBorderStyle == FormBorderStyle.None)
        {
            form.WindowState = FormWindowState.Normal;
            form.FormBorderStyle = FormBorderStyle.Sizable;
            if (SavedBounds.TryGetValue(form, out var bounds))
            {
                form.Bounds = bounds;
                SavedBounds.Remove(form);
            }
        }
        else
        {
            if (form.WindowState == FormWindowState.Normal)
                SavedBounds[form] = form.Bounds;
            form.WindowState = FormWindowState.Normal;
            form.FormBorderStyle = FormBorderStyle.None;
            form.WindowState = FormWindowState.Maximized;
        }
    }
}
