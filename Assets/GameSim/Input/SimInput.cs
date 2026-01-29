// SimInput.cs - Simulation input structure
// PURE C# - No external dependencies

namespace MOBANet.GameSim.Input
{
    /// <summary>
    /// Button indices for input bitfield
    /// </summary>
    public static class InputButtons
    {
        public const int PrimaryFire = 0;
        public const int SecondaryFire = 1;
        public const int Skill1 = 2;
        public const int Skill2 = 3;
        public const int Skill3 = 4;
        public const int Skill4 = 5;
        public const int Ultimate = 6;
        public const int Jump = 7;
        public const int Dash = 8;
        public const int Reload = 9;
        public const int Interact = 10;
        public const int Crouch = 11;
        public const int Sprint = 12;

        public static ushort SetButton(ushort buttons, int index, bool pressed)
        {
            if (pressed)
                return (ushort)(buttons | (1 << index));
            else
                return (ushort)(buttons & ~(1 << index));
        }
    }

    /// <summary>
    /// Simulation input for a single tick.
    /// Decoupled from network messages for clean architecture.
    /// </summary>
    public struct SimInput
    {
        /// <summary>
        /// Input sequence number (from client)
        /// </summary>
        public uint Sequence;

        /// <summary>
        /// Movement X axis (-1 to 1)
        /// </summary>
        public float MoveX;

        /// <summary>
        /// Movement Y axis (-1 to 1)
        /// </summary>
        public float MoveY;

        /// <summary>
        /// Aim X axis (-1 to 1)
        /// </summary>
        public float AimX;

        /// <summary>
        /// Aim Y axis (-1 to 1)
        /// </summary>
        public float AimY;

        /// <summary>
        /// Button states bitfield
        /// </summary>
        public ushort Buttons;

        #region Button Helpers

        /// <summary>
        /// Check if a button is pressed
        /// </summary>
        public bool GetButton(int index)
        {
            return (Buttons & (1 << index)) != 0;
        }

        /// <summary>
        /// Set a button state
        /// </summary>
        public void SetButton(int index, bool pressed)
        {
            if (pressed)
                Buttons |= (ushort)(1 << index);
            else
                Buttons &= (ushort)~(1 << index);
        }

        /// <summary>
        /// Primary fire button
        /// </summary>
        public bool PrimaryFire => GetButton(InputButtons.PrimaryFire);

        /// <summary>
        /// Secondary fire button
        /// </summary>
        public bool SecondaryFire => GetButton(InputButtons.SecondaryFire);

        /// <summary>
        /// Jump button
        /// </summary>
        public bool Jump => GetButton(InputButtons.Jump);

        /// <summary>
        /// Dash button
        /// </summary>
        public bool Dash => GetButton(InputButtons.Dash);

        #endregion

        #region Movement Helpers

        /// <summary>
        /// Is there any movement input?
        /// </summary>
        public bool HasMovement => MoveX != 0 || MoveY != 0;

        /// <summary>
        /// Is there any aim input?
        /// </summary>
        public bool HasAim => AimX != 0 || AimY != 0;

        /// <summary>
        /// Movement magnitude (0 to 1)
        /// </summary>
        public float MoveMagnitude
        {
            get
            {
                float sqrMag = MoveX * MoveX + MoveY * MoveY;
                return sqrMag > 1f ? 1f : (float)System.Math.Sqrt(sqrMag);
            }
        }

        #endregion

        #region Factory

        /// <summary>
        /// Empty input (no movement, no buttons)
        /// </summary>
        public static readonly SimInput Empty = new SimInput();

        /// <summary>
        /// Create SimInput with movement only
        /// </summary>
        public static SimInput Movement(uint sequence, float moveX, float moveY)
        {
            return new SimInput
            {
                Sequence = sequence,
                MoveX = moveX,
                MoveY = moveY,
                AimX = 0,
                AimY = 0,
                Buttons = 0
            };
        }

        /// <summary>
        /// Create SimInput with full parameters
        /// </summary>
        public static SimInput Create(
            uint sequence,
            float moveX,
            float moveY,
            float aimX,
            float aimY,
            ushort buttons)
        {
            return new SimInput
            {
                Sequence = sequence,
                MoveX = moveX,
                MoveY = moveY,
                AimX = aimX,
                AimY = aimY,
                Buttons = buttons
            };
        }

        #endregion

        public override string ToString()
        {
            return $"Input[Seq:{Sequence}] Move:({MoveX:F2},{MoveY:F2}) Aim:({AimX:F2},{AimY:F2}) Btn:{Buttons:X4}";
        }
    }
}
