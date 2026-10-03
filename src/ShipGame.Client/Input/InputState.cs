using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace ShipGame.Client.Input;

/// <summary>Current and previous frame input, for edge detection (pressed this frame vs held).</summary>
public sealed class InputState
{
    public KeyboardState Keyboard { get; private set; }
    public KeyboardState PreviousKeyboard { get; private set; }
    public MouseState Mouse { get; private set; }
    public MouseState PreviousMouse { get; private set; }

    public void Update()
    {
        PreviousKeyboard = Keyboard;
        PreviousMouse = Mouse;
        Keyboard = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        Mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
    }

    public Vector2 MousePosition => Mouse.Position.ToVector2();

    public bool IsKeyDown(Keys key) => Keyboard.IsKeyDown(key);

    public bool WasKeyPressed(Keys key) => Keyboard.IsKeyDown(key) && PreviousKeyboard.IsKeyUp(key);

    public bool WasKeyReleased(Keys key) => Keyboard.IsKeyUp(key) && PreviousKeyboard.IsKeyDown(key);

    public bool WasLeftMousePressed =>
        Mouse.LeftButton == ButtonState.Pressed && PreviousMouse.LeftButton == ButtonState.Released;

    public bool IsRightMouseDown => Mouse.RightButton == ButtonState.Pressed;

    public bool WasRightMousePressed =>
        Mouse.RightButton == ButtonState.Pressed && PreviousMouse.RightButton == ButtonState.Released;

    public int ScrollDelta => Mouse.ScrollWheelValue - PreviousMouse.ScrollWheelValue;
}
