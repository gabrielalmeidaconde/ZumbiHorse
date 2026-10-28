using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum DeviceKind { Keyboard1, Keyboard2, Gamepad, Bot }

/// <summary>
/// Fonte de input de um jogador: metade esquerda do teclado (WASD), metade direita (setas),
/// um controle, um bot, ou (no multiplayer online) um desses dispositivos em outro computador.
/// </summary>
public class InputSource
{
    public DeviceKind Kind;
    public int PadNumber;
#if ENABLE_INPUT_SYSTEM
    public Gamepad Pad;
#endif

    // Estado controlado pelo DemoDriver quando Kind == Bot.
    public Vector2 BotMove;
    public bool BotJumpHeld;
    public int BotJumpFrame = -1, BotConfirmFrame = -1, BotRotateFrame = -1, BotRotateCCWFrame = -1;

    // Dispositivo de outro computador (multiplayer online). Kind diz qual é o dispositivo de lá.
    public bool Remote;
    public int RemoteConn, RemoteId;
    public bool RemoteAlive = true;
    public Vector2 RemoteMove;
    public bool RemoteJumpHeld;
    /// <summary>Quadro em que cada botão foi apertado lá: pulo, confirmar, voltar, girar horário, anti-horário, pausar.</summary>
    public readonly int[] RemotePressFrame = { -1, -1, -1, -1, -1, -1 };
    public const int BtnJump = 0, BtnConfirm = 1, BtnBack = 2, BtnCW = 3, BtnCCW = 4, BtnPause = 5;

    bool RemoteDown(int b) => RemotePressFrame[b] == Time.frameCount;

    /// <summary>Dispositivos dos computadores conectados (só no host).</summary>
    public static readonly List<InputSource> RemoteDevices = new List<InputSource>();

    const float DeadZone = 0.3f;

    public string Label
    {
        get
        {
            switch (Kind)
            {
                case DeviceKind.Keyboard1: return "Teclado (WASD)";
                case DeviceKind.Keyboard2: return "Teclado (Setas)";
                case DeviceKind.Gamepad: return "Controle " + PadNumber;
                default: return "CPU";
            }
        }
    }

    /// <summary>Mesmo dispositivo físico (usado para não deixar o mesmo controle entrar duas vezes).</summary>
    public bool SameDevice(InputSource other)
    {
        if (other == null || other.Kind != Kind || other.Remote != Remote) return false;
        if (Remote) return other.RemoteConn == RemoteConn && other.RemoteId == RemoteId;
#if ENABLE_INPUT_SYSTEM
        if (Kind == DeviceKind.Gamepad) return other.Pad == Pad;
#endif
        return Kind != DeviceKind.Bot;
    }

    public bool Connected
    {
        get
        {
            if (Remote) return RemoteAlive;
#if ENABLE_INPUT_SYSTEM
            if (Kind == DeviceKind.Gamepad) return Pad != null && Pad.added;
#endif
            return true;
        }
    }

    public Vector2 Move
    {
        get
        {
            if (Remote) return Vector2.ClampMagnitude(RemoteMove, 1f);
            Vector2 v = Vector2.zero;
            switch (Kind)
            {
                case DeviceKind.Keyboard1:
                    v.x = (Held(K.D) ? 1 : 0) - (Held(K.A) ? 1 : 0);
                    v.y = (Held(K.W) ? 1 : 0) - (Held(K.S) ? 1 : 0);
                    break;
                case DeviceKind.Keyboard2:
                    v.x = (Held(K.Right) ? 1 : 0) - (Held(K.Left) ? 1 : 0);
                    v.y = (Held(K.Up) ? 1 : 0) - (Held(K.Down) ? 1 : 0);
                    break;
                case DeviceKind.Gamepad:
#if ENABLE_INPUT_SYSTEM
                    if (Connected)
                    {
                        Vector2 stick = Pad.leftStick.ReadValue();
                        Vector2 dpad = Pad.dpad.ReadValue();
                        v = dpad.sqrMagnitude > 0.1f ? dpad : stick;
                        if (v.magnitude < DeadZone) v = Vector2.zero;
                    }
#endif
                    break;
                case DeviceKind.Bot:
                    v = BotMove;
                    break;
            }
            return Vector2.ClampMagnitude(v, 1f);
        }
    }

    public bool JumpHeld
    {
        get
        {
            if (Remote) return RemoteJumpHeld;
            switch (Kind)
            {
                case DeviceKind.Keyboard1: return Held(K.W) || Held(K.Space);
                case DeviceKind.Keyboard2: return Held(K.Up) || Held(K.Num0);
                case DeviceKind.Gamepad: return PadHeld(0);
                default: return BotJumpHeld;
            }
        }
    }

    public bool JumpDown
    {
        get
        {
            if (Remote) return RemoteDown(BtnJump);
            switch (Kind)
            {
                case DeviceKind.Keyboard1: return Down(K.W) || Down(K.Space);
                case DeviceKind.Keyboard2: return Down(K.Up) || Down(K.Num0);
                case DeviceKind.Gamepad: return PadDown(0);
                default: return BotJumpFrame == Time.frameCount;
            }
        }
    }

    /// <summary>Confirmar: entrar no lobby, ficar pronto, pegar/posicionar item.</summary>
    public bool ConfirmDown
    {
        get
        {
            if (Remote) return RemoteDown(BtnConfirm);
            switch (Kind)
            {
                case DeviceKind.Keyboard1: return Down(K.Space);
                case DeviceKind.Keyboard2: return Down(K.Enter) || Down(K.NumEnter);
                case DeviceKind.Gamepad: return PadDown(0);
                default: return BotConfirmFrame == Time.frameCount;
            }
        }
    }

    /// <summary>Confirmar segurado (no criador de fases: pintar enquanto anda).</summary>
    public bool ConfirmHeld
    {
        get
        {
            if (Remote) return false;
            switch (Kind)
            {
                case DeviceKind.Keyboard1: return Held(K.Space);
                case DeviceKind.Keyboard2: return Held(K.Enter) || Held(K.NumEnter);
                case DeviceKind.Gamepad: return PadHeld(0);
                default: return false;
            }
        }
    }

    /// <summary>Botão Select (View) do controle: abre o criador de fases no lobby.</summary>
    public bool SelectDown => !Remote && PadDown(7);

    /// <summary>Girar o item no sentido horário.</summary>
    public bool RotateCWDown
    {
        get
        {
            if (Remote) return RemoteDown(BtnCW);
            switch (Kind)
            {
                case DeviceKind.Keyboard1: return Down(K.E);
                case DeviceKind.Keyboard2: return Down(K.RightShift);
                case DeviceKind.Gamepad: return PadDown(3) || PadDown(5);
                default: return BotRotateFrame == Time.frameCount;
            }
        }
    }

    /// <summary>Girar o item no sentido anti-horário.</summary>
    public bool RotateCCWDown
    {
        get
        {
            if (Remote) return RemoteDown(BtnCCW);
            switch (Kind)
            {
                case DeviceKind.Keyboard1: return Down(K.Q);
                case DeviceKind.Keyboard2: return Down(K.RightCtrl);
                case DeviceKind.Gamepad: return PadDown(2) || PadDown(4);
                default: return BotRotateCCWFrame == Time.frameCount;
            }
        }
    }

    int prevSignY, edgeValueY, edgeFrameY = -1;

    /// <summary>-1/+1 no quadro em que o jogador aperta para baixo/cima (para menus).</summary>
    public int MoveYPressed()
    {
        if (edgeFrameY == Time.frameCount) return edgeValueY;
        edgeFrameY = Time.frameCount;
        float y = Move.y;
        int sign = y > 0.5f ? 1 : y < -0.5f ? -1 : 0;
        edgeValueY = sign != 0 && sign != prevSignY ? sign : 0;
        prevSignY = sign;
        return edgeValueY;
    }

    int prevSignX, edgeValue, edgeFrame = -1;

    /// <summary>-1/+1 no quadro em que o jogador aperta para a esquerda/direita (para menus).</summary>
    public int MoveXPressed()
    {
        if (edgeFrame == Time.frameCount) return edgeValue;
        edgeFrame = Time.frameCount;
        float x = Move.x;
        int sign = x > 0.5f ? 1 : x < -0.5f ? -1 : 0;
        edgeValue = sign != 0 && sign != prevSignX ? sign : 0;
        prevSignX = sign;
        return edgeValue;
    }

    /// <summary>Voltar: sair do lobby / cancelar "pronto" / sair da partida no pause.</summary>
    public bool BackDown
    {
        get
        {
            if (Remote) return RemoteDown(BtnBack);
            switch (Kind)
            {
                case DeviceKind.Keyboard1: return Down(K.Q);
                case DeviceKind.Keyboard2: return Down(K.Backspace);
                case DeviceKind.Gamepad: return PadDown(1);
                default: return false;
            }
        }
    }

    public bool PauseDown
    {
        get
        {
            if (Remote) return RemoteDown(BtnPause);
            switch (Kind)
            {
                case DeviceKind.Keyboard1:
                case DeviceKind.Keyboard2: return Down(K.Escape);
                case DeviceKind.Gamepad: return PadDown(6);
                default: return false;
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    enum K { W, A, S, D, Q, E, Space, Up, Down, Left, Right, Enter, NumEnter, Num0, RightCtrl, RightShift, Backspace, Escape, O, C }

    /// <summary>Tecla O: abre/fecha o painel online.</summary>
    public static bool OnlineKeyDown => Down(K.O);
    public static bool EscapeKeyDown => Down(K.Escape);
    /// <summary>Tecla C: abre o criador de fases no lobby.</summary>
    public static bool CreatorKeyDown => Down(K.C);

#if ENABLE_INPUT_SYSTEM
    static UnityEngine.InputSystem.Key Map(K k)
    {
        switch (k)
        {
            case K.W: return UnityEngine.InputSystem.Key.W;
            case K.A: return UnityEngine.InputSystem.Key.A;
            case K.S: return UnityEngine.InputSystem.Key.S;
            case K.D: return UnityEngine.InputSystem.Key.D;
            case K.Q: return UnityEngine.InputSystem.Key.Q;
            case K.E: return UnityEngine.InputSystem.Key.E;
            case K.Space: return UnityEngine.InputSystem.Key.Space;
            case K.Up: return UnityEngine.InputSystem.Key.UpArrow;
            case K.Down: return UnityEngine.InputSystem.Key.DownArrow;
            case K.Left: return UnityEngine.InputSystem.Key.LeftArrow;
            case K.Right: return UnityEngine.InputSystem.Key.RightArrow;
            case K.Enter: return UnityEngine.InputSystem.Key.Enter;
            case K.NumEnter: return UnityEngine.InputSystem.Key.NumpadEnter;
            case K.Num0: return UnityEngine.InputSystem.Key.Numpad0;
            case K.RightCtrl: return UnityEngine.InputSystem.Key.RightCtrl;
            case K.RightShift: return UnityEngine.InputSystem.Key.RightShift;
            case K.Backspace: return UnityEngine.InputSystem.Key.Backspace;
            case K.O: return UnityEngine.InputSystem.Key.O;
            case K.C: return UnityEngine.InputSystem.Key.C;
            default: return UnityEngine.InputSystem.Key.Escape;
        }
    }

    static bool Held(K k) => Keyboard.current != null && Keyboard.current[Map(k)].isPressed;
    static bool Down(K k) => Keyboard.current != null && Keyboard.current[Map(k)].wasPressedThisFrame;

    // 0 A(sul) 1 B(leste) 2 X(oeste) 3 Y(norte) 4 LB 5 RB 6 Start 7 Select
    UnityEngine.InputSystem.Controls.ButtonControl PadButton(int b)
    {
        switch (b)
        {
            case 0: return Pad.buttonSouth;
            case 1: return Pad.buttonEast;
            case 2: return Pad.buttonWest;
            case 3: return Pad.buttonNorth;
            case 4: return Pad.leftShoulder;
            case 5: return Pad.rightShoulder;
            case 6: return Pad.startButton;
            default: return Pad.selectButton;
        }
    }

    bool PadHeld(int b) => Kind == DeviceKind.Gamepad && Connected && PadButton(b).isPressed;
    bool PadDown(int b) => Kind == DeviceKind.Gamepad && Connected && PadButton(b).wasPressedThisFrame;

    public static bool HasMouse => Mouse.current != null;
    /// <summary>Posição do mouse em pixels da tela (origem embaixo à esquerda).</summary>
    public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    /// <summary>Botão 0 = esquerdo, 1 = direito.</summary>
    public static bool MouseHeld(int b) => Mouse.current != null && (b == 0 ? Mouse.current.leftButton : Mouse.current.rightButton).isPressed;
    public static bool MouseDown(int b) => Mouse.current != null && (b == 0 ? Mouse.current.leftButton : Mouse.current.rightButton).wasPressedThisFrame;
    /// <summary>Rodinha: +1 para cima, -1 para baixo, 0 parada.</summary>
    public static int MouseScroll => Mouse.current != null ? System.Math.Sign(Mouse.current.scroll.ReadValue().y) : 0;
#else
    // Fallback para o Input Manager antigo: só os dois lados do teclado (sem controles).
    static KeyCode Map(K k)
    {
        switch (k)
        {
            case K.W: return KeyCode.W;
            case K.A: return KeyCode.A;
            case K.S: return KeyCode.S;
            case K.D: return KeyCode.D;
            case K.Q: return KeyCode.Q;
            case K.E: return KeyCode.E;
            case K.Space: return KeyCode.Space;
            case K.Up: return KeyCode.UpArrow;
            case K.Down: return KeyCode.DownArrow;
            case K.Left: return KeyCode.LeftArrow;
            case K.Right: return KeyCode.RightArrow;
            case K.Enter: return KeyCode.Return;
            case K.NumEnter: return KeyCode.KeypadEnter;
            case K.Num0: return KeyCode.Keypad0;
            case K.RightCtrl: return KeyCode.RightControl;
            case K.RightShift: return KeyCode.RightShift;
            case K.Backspace: return KeyCode.Backspace;
            case K.O: return KeyCode.O;
            case K.C: return KeyCode.C;
            default: return KeyCode.Escape;
        }
    }

    static bool Held(K k) => Input.GetKey(Map(k));
    static bool Down(K k) => Input.GetKeyDown(Map(k));
    bool PadHeld(int b) => false;
    bool PadDown(int b) => false;

    public static bool HasMouse => Input.mousePresent;
    public static Vector2 MousePosition => Input.mousePosition;
    public static bool MouseHeld(int b) => Input.GetMouseButton(b);
    public static bool MouseDown(int b) => Input.GetMouseButtonDown(b);
    public static int MouseScroll => System.Math.Sign(Input.mouseScrollDelta.y);
#endif

    // ------------------------------------------------------------------ dispositivos

    static readonly InputSource keyboard1 = new InputSource { Kind = DeviceKind.Keyboard1 };
    static readonly InputSource keyboard2 = new InputSource { Kind = DeviceKind.Keyboard2 };
#if ENABLE_INPUT_SYSTEM
    static readonly Dictionary<Gamepad, InputSource> pads = new Dictionary<Gamepad, InputSource>();
#endif

    /// <summary>Todos os dispositivos que podem entrar na partida neste momento.</summary>
    public static List<InputSource> AvailableDevices()
    {
        var list = new List<InputSource> { keyboard1, keyboard2 };
#if ENABLE_INPUT_SYSTEM
        int n = 0;
        foreach (var pad in Gamepad.all)
        {
            n++;
            if (!pads.TryGetValue(pad, out var src))
            {
                src = new InputSource { Kind = DeviceKind.Gamepad, Pad = pad };
                pads[pad] = src;
            }
            src.PadNumber = n;
            list.Add(src);
        }
#endif
        foreach (var r in RemoteDevices) if (r.RemoteAlive) list.Add(r);
        return list;
    }

    public static int GamepadCount
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Gamepad.all.Count;
#else
            return 0;
#endif
        }
    }

    public static bool GamepadsSupported
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return true;
#else
            return false;
#endif
        }
    }
}
