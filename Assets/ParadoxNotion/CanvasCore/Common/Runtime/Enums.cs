namespace ParadoxNotion
{

    ///<summary>Enumeration for comparisons ops</summary>
    public enum CompareOp
    {
        EqualTo,
        GreaterThan,
        LessThan,
        GreaterOrEqualTo,
        LessOrEqualTo
    }

    ///<summary>Enumeration for assign ops</summary>
    public enum AssignOp
    {
        Set,
        Add,
        Subtract,
        Multiply,
        Divide
    }

    ///<summary>Enumeration for mouse button keys</summary>
	public enum ButtonKey
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }

    ///<summary>Enumeration for press types for inputs</summary>
	public enum PressType
    {
        Down,
        Up,
        Pressed
    }

    ///<summary>Enumeration for mouse press</summary>
	public enum MouseClickEvent
    {
        MouseDown = 0,
        MouseUp = 1
    }

    ///<summary>Enumeration for trigger unity events</summary>
	public enum TriggerType
    {
        TriggerEnter = 0,
        TriggerExit = 1,
        TriggerStay = 2
    }

    ///<summary>Enumeration for collision unity events</summary>
	public enum CollisionType
    {
        CollisionEnter = 0,
        CollisionExit = 1,
        CollisionStay = 2
    }

    ///<summary>Enumeration for mouse unity events</summary>
	public enum MouseInteractionType
    {
        MouseEnter = 0,
        MouseExit = 1,
        MouseOver = 2
    }

    ///<summary>Enumeration for Animation playing direction</summary>
    public enum PlayDirection
    {
        Forward,
        Backward,
        Toggle
    }

    ///<summary>Enumeration for planar direction</summary>
    public enum PlanarDirection
    {
        Horizontal,
        Vertical,
        Auto
    }

    ///<summary>Enumeration Alignment 2x2</summary>
    public enum Alignment2x2
    {
        Default,
        Left,
        Right,
        Top,
        Bottom
    }

    ///<summary>Enumeration Alignment 3x3</summary>
    public enum Alignment3x3
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }
}