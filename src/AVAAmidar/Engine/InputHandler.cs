using Avalonia.Input;

namespace AVAAmidar.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: a way to go, and the jump.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// The arrows (or WASD): the way to go, the last one pressed counting most, so a turn can be pressed early and
    /// held. Space, Z, X or Ctrl to jump. On a gamepad: the stick or D-pad, and A or B to jump.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            var way = Dir.None;
            foreach (var k in _held)
            {
                var d = k switch
                {
                    Key.Left or Key.A or Key.NumPad4 => Dir.Left, Key.Right or Key.D or Key.NumPad6 => Dir.Right,
                    Key.Up or Key.W or Key.NumPad8 => Dir.Up, Key.Down or Key.S or Key.NumPad2 => Dir.Down, _ => Dir.None,
                };
                if (d != Dir.None) way = d;
            }
            if (way == Dir.None)
                way = pad.DPadLeft || pad.LeftStickX < -0.5f ? Dir.Left : pad.DPadRight || pad.LeftStickX > 0.5f ? Dir.Right
                    : pad.DPadUp || pad.LeftStickY < -0.5f ? Dir.Up : pad.DPadDown || pad.LeftStickY > 0.5f ? Dir.Down : Dir.None;
            bool jump = Any(Key.Space, Key.Z, Key.X, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonA || pad.ButtonB;
            return new Controls(way, jump);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
