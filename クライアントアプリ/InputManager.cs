namespace DrawingGame.Client;

/// <summary>
/// 当てる側の回答入力を管理する。
/// キーボードEnter・送信ボタンで回答を確定し、サーバーへ送信する。
/// 正解・不正解の判定はサーバー側で行われる。
/// </summary>
public class InputManager
{
    private readonly TextBox _textBox;
    private readonly Button _sendButton;
    private readonly Action<string> _submit;
    private bool _enabled;

    public event Action<string>? Submitted;

    public InputManager(TextBox textBox, Button sendButton, Action<string> submit)
    {
        _textBox = textBox;
        _sendButton = sendButton;
        _submit = submit;

        _sendButton.Click += (_, _) => TrySubmit();
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                TrySubmit();
            }
        };
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _textBox.Enabled = enabled;
        _sendButton.Enabled = enabled;
        if (!enabled) _textBox.Clear();
    }

    private void TrySubmit()
    {
        if (!_enabled) return;
        var text = _textBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _submit(text);
        _textBox.Clear();
        Submitted?.Invoke(text);
    }
}
