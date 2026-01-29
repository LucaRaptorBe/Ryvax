using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using System;

/// <summary>
/// Bouton de souris avec noms explicites pour l'Inspector.
/// </summary>
public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2
}

/// <summary>
/// Utilitaire de conversion KeyCode vers Key (nouveau Input System).
/// </summary>
public static class KeyCodeConverter
{
    /// <summary>
    /// Convertit un KeyCode (ancien système) vers un Key (nouveau Input System).
    /// </summary>
    public static Key ToKey(KeyCode keyCode)
    {
        // Lettres et chiffres correspondent directement
        if (Enum.TryParse<Key>(keyCode.ToString(), true, out Key key))
            return key;

        // Cas spéciaux qui ne correspondent pas
        return keyCode switch
        {
            KeyCode.Return => Key.Enter,
            KeyCode.KeypadEnter => Key.NumpadEnter,
            KeyCode.Keypad0 => Key.Numpad0,
            KeyCode.Keypad1 => Key.Numpad1,
            KeyCode.Keypad2 => Key.Numpad2,
            KeyCode.Keypad3 => Key.Numpad3,
            KeyCode.Keypad4 => Key.Numpad4,
            KeyCode.Keypad5 => Key.Numpad5,
            KeyCode.Keypad6 => Key.Numpad6,
            KeyCode.Keypad7 => Key.Numpad7,
            KeyCode.Keypad8 => Key.Numpad8,
            KeyCode.Keypad9 => Key.Numpad9,
            KeyCode.KeypadDivide => Key.NumpadDivide,
            KeyCode.KeypadMultiply => Key.NumpadMultiply,
            KeyCode.KeypadMinus => Key.NumpadMinus,
            KeyCode.KeypadPlus => Key.NumpadPlus,
            KeyCode.KeypadPeriod => Key.NumpadPeriod,
            KeyCode.KeypadEquals => Key.NumpadEquals,
            KeyCode.LeftControl => Key.LeftCtrl,
            KeyCode.RightControl => Key.RightCtrl,
            KeyCode.LeftCommand => Key.LeftCommand,
            KeyCode.RightCommand => Key.RightCommand,
            KeyCode.LeftWindows => Key.LeftWindows,
            KeyCode.RightWindows => Key.RightWindows,
            KeyCode.BackQuote => Key.Backquote,
            KeyCode.Minus => Key.Minus,
            KeyCode.Equals => Key.Equals,
            KeyCode.LeftBracket => Key.LeftBracket,
            KeyCode.RightBracket => Key.RightBracket,
            KeyCode.Backslash => Key.Backslash,
            KeyCode.Semicolon => Key.Semicolon,
            KeyCode.Quote => Key.Quote,
            KeyCode.Comma => Key.Comma,
            KeyCode.Period => Key.Period,
            KeyCode.Slash => Key.Slash,
            KeyCode.Alpha0 => Key.Digit0,
            KeyCode.Alpha1 => Key.Digit1,
            KeyCode.Alpha2 => Key.Digit2,
            KeyCode.Alpha3 => Key.Digit3,
            KeyCode.Alpha4 => Key.Digit4,
            KeyCode.Alpha5 => Key.Digit5,
            KeyCode.Alpha6 => Key.Digit6,
            KeyCode.Alpha7 => Key.Digit7,
            KeyCode.Alpha8 => Key.Digit8,
            KeyCode.Alpha9 => Key.Digit9,
            KeyCode.None => Key.None,
            _ => Key.None
        };
    }

    /// <summary>
    /// Obtient le ButtonControl correspondant au MouseButton.
    /// </summary>
    public static ButtonControl GetMouseButton(MouseButton button)
    {
        if (Mouse.current == null) return null;

        return button switch
        {
            MouseButton.Left => Mouse.current.leftButton,
            MouseButton.Right => Mouse.current.rightButton,
            MouseButton.Middle => Mouse.current.middleButton,
            _ => null
        };
    }
}

/// <summary>
/// Utilitaire pour obtenir un label lisible depuis un KeyCode.
/// </summary>
public static class KeyCodeLabel
{
    /// <summary>
    /// Convertit un KeyCode en label lisible pour l'UI.
    /// </summary>
    public static string GetLabel(KeyCode key)
    {
        return key switch
        {
            KeyCode.Alpha0 => "0",
            KeyCode.Alpha1 => "1",
            KeyCode.Alpha2 => "2",
            KeyCode.Alpha3 => "3",
            KeyCode.Alpha4 => "4",
            KeyCode.Alpha5 => "5",
            KeyCode.Alpha6 => "6",
            KeyCode.Alpha7 => "7",
            KeyCode.Alpha8 => "8",
            KeyCode.Alpha9 => "9",
            KeyCode.Space => "Space",
            KeyCode.LeftShift => "Shift",
            KeyCode.RightShift => "Shift",
            KeyCode.LeftControl => "Ctrl",
            KeyCode.RightControl => "Ctrl",
            KeyCode.LeftAlt => "Alt",
            KeyCode.RightAlt => "Alt",
            KeyCode.Tab => "Tab",
            KeyCode.Escape => "Esc",
            KeyCode.None => "-",
            _ => key.ToString()
        };
    }
}

/// <summary>
/// Binding d'input flexible : peut être une touche clavier OU un bouton souris.
/// </summary>
[Serializable]
public struct InputBinding
{
    public enum InputType { Key, MouseButton }

    [Tooltip("Type d'input : touche clavier ou bouton souris")]
    public InputType type;

    [Tooltip("Touche clavier (si type = Key)")]
    public KeyCode key;

    [Tooltip("Bouton souris (si type = MouseButton)")]
    public MouseButton mouseButton;

    public InputBinding(KeyCode key)
    {
        type = InputType.Key;
        this.key = key;
        mouseButton = MouseButton.Left;
    }

    public InputBinding(MouseButton mouseButton)
    {
        type = InputType.MouseButton;
        key = KeyCode.None;
        this.mouseButton = mouseButton;
    }

    /// <summary>
    /// Retourne true la frame où l'input est pressé.
    /// </summary>
    public bool GetDown()
    {
        if (type == InputType.Key)
        {
            if (Keyboard.current == null) return false;
            Key inputKey = KeyCodeConverter.ToKey(key);
            if (inputKey == Key.None) return false;
            return Keyboard.current[inputKey].wasPressedThisFrame;
        }
        else
        {
            ButtonControl button = KeyCodeConverter.GetMouseButton(mouseButton);
            return button != null && button.wasPressedThisFrame;
        }
    }

    /// <summary>
    /// Retourne true tant que l'input est maintenu.
    /// </summary>
    public bool Get()
    {
        if (type == InputType.Key)
        {
            if (Keyboard.current == null) return false;
            Key inputKey = KeyCodeConverter.ToKey(key);
            if (inputKey == Key.None) return false;
            return Keyboard.current[inputKey].isPressed;
        }
        else
        {
            ButtonControl button = KeyCodeConverter.GetMouseButton(mouseButton);
            return button != null && button.isPressed;
        }
    }

    /// <summary>
    /// Retourne true la frame où l'input est relâché.
    /// </summary>
    public bool GetUp()
    {
        if (type == InputType.Key)
        {
            if (Keyboard.current == null) return false;
            Key inputKey = KeyCodeConverter.ToKey(key);
            if (inputKey == Key.None) return false;
            return Keyboard.current[inputKey].wasReleasedThisFrame;
        }
        else
        {
            ButtonControl button = KeyCodeConverter.GetMouseButton(mouseButton);
            return button != null && button.wasReleasedThisFrame;
        }
    }

    /// <summary>
    /// Retourne un label lisible pour l'affichage UI (ex: "LMB", "Q", "1").
    /// </summary>
    public string GetDisplayLabel()
    {
        if (type == InputType.MouseButton)
        {
            return mouseButton switch
            {
                MouseButton.Left => "LMB",
                MouseButton.Right => "RMB",
                MouseButton.Middle => "MMB",
                _ => "?"
            };
        }
        else
        {
            return KeyCodeLabel.GetLabel(key);
        }
    }
}

// SpellBinding removed - old spell system deleted

/// <summary>
/// Axes de mouvement pour la configuration WASD.
/// </summary>
public enum MovementAxis
{
    Forward,
    Backward,
    Left,
    Right
}

/// <summary>
/// Binding pour les touches de mouvement et actions basiques (sans mode de cast).
/// </summary>
[Serializable]
public struct MovementBindings
{
    [Header("Movement Keys")]
    [Tooltip("Touche pour avancer")]
    public KeyCode forward;

    [Tooltip("Touche pour reculer")]
    public KeyCode backward;

    [Tooltip("Touche pour aller à gauche")]
    public KeyCode left;

    [Tooltip("Touche pour aller à droite")]
    public KeyCode right;

    [Header("Action Keys")]
    [Tooltip("Touche pour sauter")]
    public KeyCode jump;

    [Header("Camera")]
    [Tooltip("Touche pour verrouiller/déverrouiller la caméra sur le joueur")]
    public KeyCode cameraLock;

    /// <summary>
    /// Retourne l'input horizontal (-1, 0, ou 1).
    /// </summary>
    public float GetHorizontal()
    {
        if (Keyboard.current == null) return 0f;

        float horizontal = 0f;
        Key rightKey = KeyCodeConverter.ToKey(right);
        Key leftKey = KeyCodeConverter.ToKey(left);

        if (rightKey != Key.None && Keyboard.current[rightKey].isPressed) horizontal += 1f;
        if (leftKey != Key.None && Keyboard.current[leftKey].isPressed) horizontal -= 1f;
        return horizontal;
    }

    /// <summary>
    /// Retourne l'input vertical (-1, 0, ou 1).
    /// </summary>
    public float GetVertical()
    {
        if (Keyboard.current == null) return 0f;

        float vertical = 0f;
        Key forwardKey = KeyCodeConverter.ToKey(forward);
        Key backwardKey = KeyCodeConverter.ToKey(backward);

        if (forwardKey != Key.None && Keyboard.current[forwardKey].isPressed) vertical += 1f;
        if (backwardKey != Key.None && Keyboard.current[backwardKey].isPressed) vertical -= 1f;
        return vertical;
    }

    /// <summary>
    /// Retourne true la frame où la touche de saut est pressée.
    /// </summary>
    public bool GetJumpDown()
    {
        if (Keyboard.current == null) return false;
        Key jumpKey = KeyCodeConverter.ToKey(jump);
        if (jumpKey == Key.None) return false;
        return Keyboard.current[jumpKey].wasPressedThisFrame;
    }

    /// <summary>
    /// Retourne true tant que la touche de saut est maintenue.
    /// </summary>
    public bool GetJump()
    {
        if (Keyboard.current == null) return false;
        Key jumpKey = KeyCodeConverter.ToKey(jump);
        if (jumpKey == Key.None) return false;
        return Keyboard.current[jumpKey].isPressed;
    }

    /// <summary>
    /// Retourne true la frame où la touche de verrouillage caméra est pressée.
    /// </summary>
    public bool GetCameraLockDown()
    {
        if (Keyboard.current == null) return false;
        Key lockKey = KeyCodeConverter.ToKey(cameraLock);
        if (lockKey == Key.None) return false;
        return Keyboard.current[lockKey].wasPressedThisFrame;
    }
}
