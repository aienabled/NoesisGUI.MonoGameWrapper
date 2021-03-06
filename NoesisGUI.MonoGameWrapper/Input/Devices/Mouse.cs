﻿namespace NoesisGUI.MonoGameWrapper.Input.Devices
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Input;
    using Noesis;
    using MouseState = Microsoft.Xna.Framework.Input.MouseState;
    using Point = Noesis.Point;

    internal class Mouse
    {
        public readonly ICollection<MouseButton> ConsumedButtons = new List<MouseButton>();

        /// <summary>
        /// Used for excluding consuming of input events by primary LayoutRoot
        /// </summary>
        private readonly HitTestIgnoreDelegate checkIfElementIgnoresHitTest;

        private readonly FrameworkElement controlTreeRoot;

        private readonly TimeSpan doubleClickInterval;

        private readonly bool isProcessMiddleButton;

        /// <summary>
        /// Used for double click handling
        /// </summary>
        private readonly Dictionary<MouseButton, TimeSpan> lastPressTimeDictionary =
            new Dictionary<MouseButton, TimeSpan>();

        private readonly Visual rootVisual;

        private readonly View view;

        private bool isAnyControlUnderMouseCursor;

        private bool isLastFrameWasScrolled;

        private int lastScrollWheelValue;

        private int lastX;

        private int lastY;

        private MouseState previousState;

        private TimeSpan totalGameTime;

        public Mouse(
            View view,
            Visual rootVisual,
            FrameworkElement controlTreeRoot,
            NoesisConfig config)
        {
            this.view = view;
            this.rootVisual = rootVisual;
            this.controlTreeRoot = controlTreeRoot;

            this.checkIfElementIgnoresHitTest = config.CheckIfElementIgnoresHitTest;
            this.doubleClickInterval = TimeSpan.FromSeconds(config.InputMouseDoubleClickIntervalSeconds);
            this.isProcessMiddleButton = config.IsProcessMouseMiddleButton;
        }

        public int ConsumedDeltaWheel { get; private set; }

        public void UpdateMouse(GameTime gameTime, bool isWindowActive)
        {
            this.isAnyControlUnderMouseCursor = this.CheckIsAnyControlUnderMouseCursor();

            this.totalGameTime = gameTime.TotalGameTime;

            if (this.ConsumedButtons.Count > 0)
            {
                this.ConsumedButtons.Clear();
            }

            this.ConsumedDeltaWheel = 0;

            // ReSharper disable once LocalVariableHidesMember
            var previousState = this.previousState;
            MouseState state;
            if (isWindowActive)
            {
                state = Microsoft.Xna.Framework.Input.Mouse.GetState();
            }
            else
            {
                // don't read input if the game window is not focused
                state = new MouseState(
                    previousState.X,
                    previousState.Y,
                    previousState.ScrollWheelValue,
                    leftButton: ButtonState.Released,
                    rightButton: ButtonState.Released,
                    middleButton: ButtonState.Released,
                    xButton1: ButtonState.Released,
                    xButton2: ButtonState.Released);
            }

            var x = state.X;
            var y = state.Y;
            var scrollWheelValue = state.ScrollWheelValue;

            if (this.lastX != x
                || this.lastY != y
                || this.isLastFrameWasScrolled)
            {
                this.view.MouseMove(x, y);
                this.lastX = x;
                this.lastY = y;
                this.isLastFrameWasScrolled = false;
            }

            if (this.lastScrollWheelValue != scrollWheelValue)
            {
                if (this.isAnyControlUnderMouseCursor)
                {
                    var scrollDeltaValue = scrollWheelValue - this.lastScrollWheelValue;
                    this.view.MouseWheel(x, y, scrollDeltaValue);
                    this.ConsumedDeltaWheel = scrollDeltaValue;
                    // on the next frame it's required to update NoesisGUI mouse position
                    // (on the current frame it's doesn't give required affect)
                    this.isLastFrameWasScrolled = true;
                }
                else
                {
                    this.ConsumedDeltaWheel = 0;
                }

                this.lastScrollWheelValue = scrollWheelValue;
            }
            else
            {
                this.ConsumedDeltaWheel = 0;
            }

            this.ProcessMouseButtonDown(MouseButton.Left,  state.LeftButton,  previousState.LeftButton);
            this.ProcessMouseButtonDown(MouseButton.Right, state.RightButton, previousState.RightButton);
            if (this.isProcessMiddleButton)
            {
                this.ProcessMouseButtonDown(MouseButton.Middle, state.MiddleButton, previousState.MiddleButton);
            }

            this.ProcessMouseButtonUp(MouseButton.Left,  state.LeftButton,  previousState.LeftButton);
            this.ProcessMouseButtonUp(MouseButton.Right, state.RightButton, previousState.RightButton);
            if (this.isProcessMiddleButton)
            {
                this.ProcessMouseButtonUp(MouseButton.Middle, state.MiddleButton, previousState.MiddleButton);
            }

            this.previousState = state;
        }

        private bool CheckIsAnyControlUnderMouseCursor()
        {
            using (var hitTestResult = VisualTreeHelper.HitTest(
                this.rootVisual,
                new Point(this.lastX, this.lastY)))
            {
                var visual = hitTestResult.VisualHit as FrameworkElement;
                // ensure that there is no IsHitTestVisible==False parent controls
                while (visual != null)
                {
                    if (visual == this.controlTreeRoot)
                    {
                        // hit test is not ignored (we're at root)
                        return true;
                    }

                    if (this.checkIfElementIgnoresHitTest(visual))
                    {
                        // hit test ignored
                        return false;
                    }

                    if (visual is Popup
                        || visual is ContextMenu)
                    {
                        // hit test is not ignored (we're at Popup/ContextMenu root)
                        return true;
                    }

                    // travel up - maybe the parent control should capture focus
                    visual = visual.Parent ?? VisualTreeHelper.GetParent(visual) as FrameworkElement;
                }

                return false;
            }
        }

        private void ProcessMouseButtonDown(
            MouseButton buttonId,
            ButtonState current,
            ButtonState previous)
        {
            if (current != ButtonState.Pressed)
            {
                return;
            }

            this.TryConsumeMouseButton(buttonId);
            if (current == previous)
            {
                // state didn't change
                return;
            }

            if (buttonId == MouseButton.Left)
            {
                // check double click (NoesisGUI crashes if we check for double click for a mouse button other than the left one)
                this.lastPressTimeDictionary.TryGetValue(buttonId, out var lastPressTime);
                if (this.totalGameTime - lastPressTime < this.doubleClickInterval)
                {
                    //System.Diagnostics.Debug.WriteLine("Mouse double click: " + buttonId);
                    this.view.MouseDoubleClick(this.lastX, this.lastY, buttonId);
                }
            }

            //System.Diagnostics.Debug.WriteLine("Mouse button down: " + buttonId);
            this.view.MouseButtonDown(this.lastX, this.lastY, buttonId);

            if (buttonId == MouseButton.Left)
            {
                // record last press time (for double click handling)
                this.lastPressTimeDictionary[buttonId] = this.totalGameTime;
            }
        }

        private void ProcessMouseButtonUp(
            MouseButton buttonId,
            ButtonState current,
            ButtonState previous)
        {
            if (current != ButtonState.Released)
            {
                return;
            }

            this.TryConsumeMouseButton(buttonId);
            if (current == previous)
            {
                // state didn't change
                return;
            }

            //System.Diagnostics.Debug.WriteLine("Mouse button up: " + buttonId);
            this.view.MouseButtonUp(this.lastX, this.lastY, buttonId);
        }

        private void TryConsumeMouseButton(MouseButton buttonId)
        {
            if (this.isAnyControlUnderMouseCursor)
            {
                // consume!
                this.ConsumedButtons.Add(buttonId);
            }
        }
    }
}